using System.Security.Cryptography;
using Acme.TestCaseManagement.Attachments.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.TestCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Attachments;

// The permission depends on what the file is attached to, so each method checks it itself; [Authorize] here means signed in.
[Authorize]
public class AttachmentAppService : TestCaseManagementAppService, IAttachmentAppService
{
    private readonly IRepository<Attachment, Guid> _repository;
    private readonly IRepository<TestCase, Guid> _testCases;
    private readonly IRepository<TestExecution, Guid> _executions;
    private readonly IBlobContainer<AttachmentContainer> _container;
    private readonly AttachmentManager _manager;
    private readonly TestCaseManagementAttachmentOptions _options;

    public AttachmentAppService(
        IRepository<Attachment, Guid> repository,
        IRepository<TestCase, Guid> testCases,
        IRepository<TestExecution, Guid> executions,
        IBlobContainer<AttachmentContainer> container,
        AttachmentManager manager,
        IOptions<TestCaseManagementAttachmentOptions> options)
    {
        _repository = repository;
        _testCases = testCases;
        _executions = executions;
        _container = container;
        _manager = manager;
        _options = options.Value;
    }

    public virtual async Task<List<AttachmentDto>> GetListAsync(GetAttachmentsInput input)
    {
        await CheckAsync(input.OwnerType, write: false);

        var ownerIds = input.OwnerIds.Distinct().ToList();
        var attachments = await _repository.GetListAsync(x => x.OwnerType == input.OwnerType && ownerIds.Contains(x.OwnerId));

        return attachments.OrderBy(x => x.CreationTime).ThenBy(x => x.FileName).Select(ToDto).ToList();
    }

    public virtual async Task<AttachmentDto> UploadAsync(UploadAttachmentInput input)
    {
        await CheckAsync(input.OwnerType, write: true);
        await EnsureOwnerExistsAsync(input.OwnerType, input.OwnerId);

        // The limits are checked on the name and on the declared length first, so that a file that is too large or of a
        // kind that is not accepted is refused before it is read.
        var declared = input.File.ContentLength;
        _manager.Validate(input.File.FileName, declared is > 0 ? declared.Value : 1);

        await using var stream = input.File.GetStream();
        var (buffer, sha256) = await ReadAsync(stream);

        var attachment = await _manager.CreateAsync(input.OwnerType, input.OwnerId, input.File.FileName, buffer.Length, sha256, input.Description);

        // The content first, then the record: a failure in between leaves a file that nothing points to, which is removed
        // here; the other order would leave a record that points to nothing.
        buffer.Position = 0;
        await _container.SaveAsync(attachment.BlobName, buffer);
        try
        {
            await _repository.InsertAsync(attachment, autoSave: true);
        }
        catch
        {
            await TryDeleteBlobAsync(attachment.BlobName);
            throw;
        }

        return ToDto(attachment);
    }

    public virtual async Task<IRemoteStreamContent> DownloadAsync(Guid id)
    {
        var attachment = await _repository.GetAsync(id);
        await CheckAsync(attachment.OwnerType, write: false);

        var stream = await _container.GetOrNullAsync(attachment.BlobName);
        if (stream == null)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentFileMissing).WithData("FileName", attachment.FileName);
        }

        return new RemoteStreamContent(stream, attachment.FileName, attachment.ContentType, attachment.Size);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var attachment = await _repository.GetAsync(id);
        await CheckAsync(attachment.OwnerType, write: true);

        await _repository.DeleteAsync(attachment);

        // The content goes once the deletion of the record is committed; if the commit fails the file is still needed.
        var blobName = attachment.BlobName;
        var unitOfWork = CurrentUnitOfWork;
        if (unitOfWork == null)
        {
            await TryDeleteBlobAsync(blobName);
        }
        else
        {
            unitOfWork.OnCompleted(() => TryDeleteBlobAsync(blobName));
        }
    }

    private async Task CheckAsync(AttachmentOwnerType ownerType, bool write)
    {
        var permission = (ownerType, write) switch
        {
            (AttachmentOwnerType.TestCase, false) => TestCaseManagementPermissions.TestCases.Default,
            (AttachmentOwnerType.TestCase, true) => TestCaseManagementPermissions.TestCases.Update,
            (AttachmentOwnerType.TestExecution, false) => TestCaseManagementPermissions.TestRuns.Default,
            (AttachmentOwnerType.TestExecution, true) => TestCaseManagementPermissions.TestRuns.Execute,
            _ => throw new BusinessException(TestCaseManagementErrorCodes.AttachmentOwnerNotFound),
        };

        await AuthorizationService.CheckAsync(permission);
    }

    private async Task EnsureOwnerExistsAsync(AttachmentOwnerType ownerType, Guid ownerId)
    {
        var exists = ownerType switch
        {
            AttachmentOwnerType.TestCase => await _testCases.AnyAsync(x => x.Id == ownerId),
            AttachmentOwnerType.TestExecution => await _executions.AnyAsync(x => x.Id == ownerId),
            _ => false,
        };

        if (!exists)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentOwnerNotFound);
        }
    }

    /// <summary>Reads the file into memory (it is limited) and computes its SHA-256; reads one byte more than allowed at most.</summary>
    private async Task<(MemoryStream Buffer, string Sha256)> ReadAsync(Stream source)
    {
        var buffer = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk)) > 0)
        {
            hash.AppendData(chunk, 0, read);
            await buffer.WriteAsync(chunk.AsMemory(0, read));
            if (buffer.Length > _options.MaxFileSizeBytes)
            {
                // The declared length was wrong or missing; the real one is over the limit, which is what is reported.
                _manager.Validate("file.txt", buffer.Length);
            }
        }

        return (buffer, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private async Task TryDeleteBlobAsync(string blobName)
    {
        try
        {
            await _container.DeleteAsync(blobName);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "The attachment content {BlobName} could not be deleted from the storage.", blobName);
        }
    }

    private static AttachmentDto ToDto(Attachment attachment) => new()
    {
        Id = attachment.Id,
        OwnerType = attachment.OwnerType,
        OwnerId = attachment.OwnerId,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        Size = attachment.Size,
        Sha256 = attachment.Sha256,
        Description = attachment.Description,
        CreationTime = attachment.CreationTime,
        CreatorId = attachment.CreatorId,
    };
}
