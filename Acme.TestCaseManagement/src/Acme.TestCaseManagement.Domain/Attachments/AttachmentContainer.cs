using Volo.Abp.BlobStoring;

namespace Acme.TestCaseManagement.Attachments;

/// <summary>The blob container of the files. The host configures its provider (file system, database, cloud storage...).</summary>
[BlobContainerName(AttachmentConsts.ContainerName)]
public class AttachmentContainer
{
}
