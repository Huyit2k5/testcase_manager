using Acme.TestCaseManagement.Enums;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.Attachments;

public class AttachmentManager : DomainService
{
    private readonly IRepository<Attachment, Guid> _repository;
    private readonly TestCaseManagementAttachmentOptions _options;

    public AttachmentManager(IRepository<Attachment, Guid> repository, IOptions<TestCaseManagementAttachmentOptions> options)
    {
        _repository = repository;
        _options = options.Value;
    }

    /// <summary>
    /// A file name that is safe to store and to show: no directory part (either kind of slash), no control or reserved
    /// characters, no leading dots, at most 255 characters with the extension kept. An empty result becomes "attachment".
    /// </summary>
    public static string CleanFileName(string? name)
    {
        var text = (name ?? string.Empty).Replace('\\', '/');
        text = text[(text.LastIndexOf('/') + 1)..];

        var cleaned = new string(text.Where(c => !char.IsControl(c) && "\"<>|:*?".IndexOf(c) < 0).ToArray()).Trim().TrimStart('.').Trim();
        if (cleaned.Length == 0)
        {
            return "attachment";
        }

        if (cleaned.Length > AttachmentConsts.MaxFileNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            extension = extension.Length > 32 ? extension[..32] : extension;
            cleaned = cleaned[..(AttachmentConsts.MaxFileNameLength - extension.Length)] + extension;
        }

        return cleaned;
    }

    /// <summary>Checks the file against the limits and gives its clean name and the content type it will be served as.</summary>
    public virtual (string FileName, string ContentType) Validate(string? name, long size)
    {
        var fileName = CleanFileName(name);

        if (size <= 0)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentEmpty);
        }

        if (size > _options.MaxFileSizeBytes)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentTooLarge)
                .WithData("Size", Math.Round(size / 1024m / 1024m, 1))
                .WithData("Limit", Math.Round(_options.MaxFileSizeBytes / 1024m / 1024m, 1));
        }

        var extension = Path.GetExtension(fileName);
        if (extension.Length == 0 || !_options.AllowedTypes.TryGetValue(extension, out var contentType))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentTypeNotAllowed)
                .WithData("Extension", extension.Length == 0 ? "(none)" : extension.ToLowerInvariant())
                .WithData("Allowed", string.Join(", ", _options.AllowedTypes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)));
        }

        return (fileName, contentType);
    }

    public virtual async Task<Attachment> CreateAsync(
        AttachmentOwnerType ownerType, Guid ownerId, string? fileName, long size, string sha256, string? description)
    {
        var (cleanName, contentType) = Validate(fileName, size);

        var count = await _repository.CountAsync(x => x.OwnerType == ownerType && x.OwnerId == ownerId);
        if (count >= _options.MaxFilesPerOwner)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.AttachmentTooMany).WithData("Count", count);
        }

        return new Attachment(GuidGenerator.Create(), CurrentTenant.Id, ownerType, ownerId, cleanName, contentType, size, sha256, description);
    }
}
