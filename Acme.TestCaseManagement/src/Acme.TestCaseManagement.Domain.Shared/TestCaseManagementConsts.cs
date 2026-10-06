namespace Acme.TestCaseManagement;

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
