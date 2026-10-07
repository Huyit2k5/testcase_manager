using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Attachments;

/// <summary>
/// The record of a file attached to a test case or to an execution attempt. The bytes are in the blob container, under the
/// name <see cref="BlobName"/>; this record is what says whose they are, who put them there and what they hold (size and
/// SHA-256). Deleting is a soft delete, so who removed what stays in the audit columns.
/// </summary>
public class Attachment : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual AttachmentOwnerType OwnerType { get; protected set; }

    public virtual Guid OwnerId { get; protected set; }

    /// <summary>The name the file had when it was uploaded, cleaned of path and control characters.</summary>
    public virtual string FileName { get; protected set; }

    /// <summary>Set from the extension by the module, never taken from the upload.</summary>
    public virtual string ContentType { get; protected set; }

    public virtual long Size { get; protected set; }

    /// <summary>Lowercase hexadecimal SHA-256 of the content.</summary>
    public virtual string Sha256 { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>The name of the content in the blob container.</summary>
    public virtual string BlobName => Id.ToString("N");

    protected Attachment()
    {
        FileName = default!;
        ContentType = default!;
        Sha256 = default!;
    }

    public Attachment(
        Guid id,
        Guid? tenantId,
        AttachmentOwnerType ownerType,
        Guid ownerId,
        string fileName,
        string contentType,
        long size,
        string sha256,
        string? description)
        : base(id)
    {
        TenantId = tenantId;
        OwnerType = ownerType;
        OwnerId = ownerId;
        FileName = Check.NotNullOrWhiteSpace(fileName, nameof(fileName), AttachmentConsts.MaxFileNameLength);
        ContentType = Check.NotNullOrWhiteSpace(contentType, nameof(contentType), AttachmentConsts.MaxContentTypeLength);
        Size = Check.Range(size, nameof(size), 1, long.MaxValue);
        Sha256 = Check.NotNullOrWhiteSpace(sha256, nameof(sha256), SignOffConsts.HashLength);
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : Check.Length(description.Trim(), nameof(description), AttachmentConsts.MaxDescriptionLength);
    }
}
