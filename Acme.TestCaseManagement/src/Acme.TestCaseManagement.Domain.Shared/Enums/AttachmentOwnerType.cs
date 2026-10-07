namespace Acme.TestCaseManagement.Enums;

/// <summary>What a file is attached to.</summary>
public enum AttachmentOwnerType
{
    /// <summary>A library test case (a reference file, an example input).</summary>
    TestCase = 0,

    /// <summary>One execution attempt of a run item (a screenshot, a crash log, a video of a failure).</summary>
    TestExecution = 1,
}
