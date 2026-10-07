namespace Acme.TestCaseManagement.Attachments;

/// <summary>
/// Limits of attachments. Change them in the host with <c>Configure&lt;TestCaseManagementAttachmentOptions&gt;</c>; where the
/// files are kept is the host's choice through ABP blob storing (<c>AbpBlobStoringOptions</c>).
/// </summary>
public class TestCaseManagementAttachmentOptions
{
    /// <summary>Largest file, in bytes. Default 25 MB; a video of a failure fits, a whole screen recording may not.</summary>
    public long MaxFileSizeBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Most files one test case or one attempt may have. Default 25.</summary>
    public int MaxFilesPerOwner { get; set; } = 25;

    /// <summary>
    /// The extensions that are accepted, each with the content type that downloads are served as. The default has the files a
    /// tester gathers: screenshots, logs, documents, archives and short videos. SVG and HTML are left out on purpose, since a
    /// browser would run the scripts in them.
    /// </summary>
    public Dictionary<string, string> AllowedTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".md"] = "text/plain",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".har"] = "application/json",
        [".zip"] = "application/zip",
        [".gz"] = "application/gzip",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".mov"] = "video/quicktime",
    };
}
