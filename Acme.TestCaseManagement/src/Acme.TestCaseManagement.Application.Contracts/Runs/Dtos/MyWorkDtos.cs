using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class GetMyWorkInput
{
    /// <summary>Only the items of this project. Leave it out for every project the caller may see.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>Also the items that already have a result. By default only what is still to run (Untested) or has to be run again (Failed and Blocked are kept: they are not done).</summary>
    public bool IncludeDone { get; set; }
}

/// <summary>One test case assigned to the caller, in a run that is not completed.</summary>
public class MyWorkItemDto
{
    public Guid RunItemId { get; set; }

    public Guid RunId { get; set; }

    public string RunTitle { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }

    public Guid TestCaseId { get; set; }

    public string? TestCaseCode { get; set; }

    public string TestCaseTitle { get; set; } = string.Empty;

    public int VersionNumber { get; set; }

    public TestResultStatus CurrentStatus { get; set; }

    public int AttemptCount { get; set; }
}

public class MyWorkDto
{
    /// <summary>Untested first, then Failed, Blocked, Skipped and Passed; inside a status by run (newest first) and position. At most 500.</summary>
    public List<MyWorkItemDto> Items { get; set; } = new();

    /// <summary>All the items assigned to the caller in open runs, whatever <see cref="GetMyWorkInput.IncludeDone"/> says.</summary>
    public int Total { get; set; }

    /// <summary>Of those, the ones without any result yet.</summary>
    public int Untested { get; set; }

    /// <summary>Of those, the ones whose last result is Failed or Blocked.</summary>
    public int NeedAttention { get; set; }

    /// <summary>True when more than 500 items matched and the list was cut.</summary>
    public bool Truncated { get; set; }
}
