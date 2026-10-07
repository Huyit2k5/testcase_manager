using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Attachments.Dtos;

public class AttachmentDto
{
    public Guid Id { get; set; }

    public AttachmentOwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>Lowercase hexadecimal SHA-256 of the content, to check a downloaded copy.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid? CreatorId { get; set; }
}

public class GetAttachmentsInput
{
    public AttachmentOwnerType OwnerType { get; set; }

    /// <summary>The test cases, or the execution attempts, whose files are wanted.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(100)]
    public List<Guid> OwnerIds { get; set; } = new();
}

public class UploadAttachmentInput
{
    public AttachmentOwnerType OwnerType { get; set; }

    public Guid OwnerId { get; set; }

    [Required]
    public IRemoteStreamContent File { get; set; } = default!;

    /// <summary>What the file shows, for example "Checkout page after the card was declined".</summary>
    [StringLength(AttachmentConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
