namespace Acme.TestCaseManagement;

public static class ProjectConsts
{
    /// <summary>The key is a short code in capitals and digits ("EINV", "HRM2"), like a Jira project key.</summary>
    public const int MinKeyLength = 2;
    public const int MaxKeyLength = 10;
    public const int MaxNameLength = 128;
    public const int MaxDescriptionLength = 2000;

    /// <summary>The key of the project that holds what was there before projects existed, and what is created without naming one.</summary>
    public const string DefaultKey = "DEFAULT";
    public const string DefaultName = "Default project";
}

public static class TestSuiteConsts
{
    public const int MaxNameLength = 128;
    public const int MaxDescriptionLength = 2000;
}

public static class TestCaseConsts
{
    public const int MaxCodeLength = 64;
    public const int MaxTitleLength = 256;
    public const int MaxTextLength = 4000;
    public const int MaxAutomationIdLength = 256;
    public const int MaxChangeSummaryLength = 1000;
}

public static class TestStepConsts
{
    public const int MaxTextLength = 4000;
}

public static class TestPlanConsts
{
    public const int MaxNameLength = 256;
    public const int MaxDescriptionLength = 2000;
}

public static class TestRunConsts
{
    public const int MaxTitleLength = 256;
    public const int MaxEnvironmentLength = 128;
}

public static class TestExecutionConsts
{
    public const int MaxActualResultLength = 4000;
}

public static class RequirementConsts
{
    public const int MaxCodeLength = 64;
    public const int MaxTitleLength = 256;
    public const int MaxTextLength = 4000;
}

public static class QualityGateConsts
{
    public const int MaxNameLength = 128;
    public const int MaxDescriptionLength = 1000;

    /// <summary>Pass rate required by the built-in baseline gate and proposed for new gates (percent).</summary>
    public const decimal DefaultMinPassRate = 95m;

    /// <summary>QA Lead and Product Owner in the spec.</summary>
    public const int DefaultRequiredApprovals = 2;

    public const int MaxRequiredApprovals = 10;
}

public static class SignOffConsts
{
    public const int MaxTitleLength = 256;
    public const int MaxApproverNameLength = 256;
    public const int MaxRoleLength = 128;
    public const int MaxCommentLength = 1000;

    /// <summary>Length of a SHA-256 digest written as lowercase hexadecimal.</summary>
    public const int HashLength = 64;
}

public static class DefectLinkConsts
{
    public const int MaxExternalSystemLength = 64;
    public const int MaxIssueKeyLength = 128;
    public const int MaxIssueUrlLength = 1024;
}

public static class ApiKeyConsts
{
    public const int MaxNameLength = 128;

    /// <summary>"tcm_" followed by 8 hexadecimal characters: what is shown and what a lookup uses.</summary>
    public const int KeyPrefixLength = 12;
}

public static class AutomationConsts
{
    public const int MaxIdempotencyKeyLength = 128;

    public const int MaxRunTitleLength = TestRunConsts.MaxTitleLength;
}

public static class AttachmentConsts
{
    public const int MaxFileNameLength = 255;

    public const int MaxContentTypeLength = 128;

    public const int MaxDescriptionLength = 512;

    /// <summary>Name of the blob container that holds the files.</summary>
    public const string ContainerName = "test-case-management-attachments";
}

public static class TagConsts
{
    public const int MaxLength = 50;

    public const int MaxPerTestCase = 20;
}

public static class SharedStepGroupConsts
{
    public const int MaxNameLength = 128;

    public const int MaxDescriptionLength = 1000;

    public const int MaxSteps = 50;
}
