using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.TestCases.Dtos;

/// <summary>One line of the history of a test case.</summary>
public class TestCaseHistoryDto
{
    public Guid Id { get; set; }

    public Guid TestCaseId { get; set; }

    public TestCaseHistoryType Type { get; set; }

    /// <summary>The old value, for example the old status.</summary>
    public string? FromValue { get; set; }

    /// <summary>The new value, for example the new status.</summary>
    public string? ToValue { get; set; }

    /// <summary>The number of the version, when a version was published.</summary>
    public int? VersionNumber { get; set; }

    /// <summary>The fields that changed, the tags, or the summary of a change.</summary>
    public string? Note { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>Who did it; null when it is not known.</summary>
    public Guid? CreatorId { get; set; }
}
