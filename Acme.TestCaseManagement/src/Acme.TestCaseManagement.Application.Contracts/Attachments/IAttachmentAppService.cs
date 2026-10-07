using Acme.TestCaseManagement.Attachments.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Attachments;

/// <summary>
/// Files attached to test cases and to execution attempts (FR-015). What a caller may do follows what the file is attached
/// to: reading needs the read permission of test cases (or of test runs, for an attempt), adding and deleting needs the
/// permission to update test cases (or to execute test runs).
/// </summary>
public interface IAttachmentAppService : IApplicationService
{
    /// <summary>The files of up to 100 owners of one kind, oldest first.</summary>
    Task<List<AttachmentDto>> GetListAsync(GetAttachmentsInput input);

    /// <summary>
    /// Stores a file. The type is decided by the extension, which must be one the module accepts, and the size and the number of
    /// files of the owner are limited; the content type of the upload is ignored.
    /// </summary>
    Task<AttachmentDto> UploadAsync(UploadAttachmentInput input);

    /// <summary>The content of the file, with the type its extension stands for.</summary>
    Task<IRemoteStreamContent> DownloadAsync(Guid id);

    /// <summary>Removes the file. The record stays in the audit trail; the content is deleted from the storage.</summary>
    Task DeleteAsync(Guid id);
}
