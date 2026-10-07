using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.Attachments.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/attachments")]
public class AttachmentController : TestCaseManagementController, IAttachmentAppService
{
    private readonly IAttachmentAppService _attachmentAppService;

    public AttachmentController(IAttachmentAppService attachmentAppService)
    {
        _attachmentAppService = attachmentAppService;
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<List<AttachmentDto>> GetListAsync([FromQuery] GetAttachmentsInput input)
    {
        return _attachmentAppService.GetListAsync(input);
    }

    /// <inheritdoc />
    [HttpPost]
    [Consumes("multipart/form-data")]
    public virtual Task<AttachmentDto> UploadAsync([FromForm] UploadAttachmentInput input)
    {
        return _attachmentAppService.UploadAsync(input);
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}/content")]
    public virtual async Task<IRemoteStreamContent> DownloadAsync(Guid id)
    {
        var content = await _attachmentAppService.DownloadAsync(id);

        // The browser must not guess a type other than the one declared, and a file is never shown as a page of this site.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        return content;
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _attachmentAppService.DeleteAsync(id);
    }
}
