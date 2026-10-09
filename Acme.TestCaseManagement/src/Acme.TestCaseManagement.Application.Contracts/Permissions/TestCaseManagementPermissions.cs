using Volo.Abp.Reflection;

namespace Acme.TestCaseManagement.Permissions;

public static class TestCaseManagementPermissions
{
    public const string GroupName = "TestCaseManagement";

    public static class TestCases
    {
        public const string Default = GroupName + ".TestCases";
        public const string Create = Default + ".Create";
        public const string Update = Default + ".Update";
        public const string Delete = Default + ".Delete";
        public const string Approve = Default + ".Approve";

        /// <summary>Asking the configured AI model to propose steps from a requirement text. What is sent leaves the application.</summary>
        public const string SuggestSteps = Default + ".SuggestSteps";
    }

    public static class Projects
    {
        public const string Default = GroupName + ".Projects";
        public const string Manage = Default + ".Manage";
    }

    public static class TestSuites
    {
        public const string Default = GroupName + ".TestSuites";
        public const string Manage = Default + ".Manage";
    }

    public static class TestPlans
    {
        public const string Default = GroupName + ".TestPlans";
        public const string Manage = Default + ".Manage";
    }

    public static class TestRuns
    {
        public const string Default = GroupName + ".TestRuns";
        public const string Execute = Default + ".Execute";
    }

    public static class Requirements
    {
        public const string Default = GroupName + ".Requirements";
        public const string Manage = Default + ".Manage";
    }

    public static class QualityGates
    {
        public const string Default = GroupName + ".QualityGates";
        public const string Manage = Default + ".Manage";
    }

    /// <summary>Reserved for QA Lead / Release Manager.</summary>
    public static class SignOff
    {
        public const string Default = GroupName + ".SignOff";
        public const string Approve = Default + ".Approve";
    }

    /// <summary>The library of reusable groups of steps: reading it, and changing it (which can put many test cases behind).</summary>
    public static class SharedSteps
    {
        public const string Default = GroupName + ".SharedSteps";
        public const string Manage = Default + ".Manage";
    }

    /// <summary>Creating, listing and revoking the API keys of CI/CD pipelines.</summary>
    public static class ApiKeys
    {
        public const string Default = GroupName + ".ApiKeys";
        public const string Manage = Default + ".Manage";
    }

    /// <summary>
    /// Publishing results of automated runs. An API key can do this and nothing else; a signed-in user needs the
    /// permission as well.
    /// </summary>
    public static class AutomationResults
    {
        public const string Default = GroupName + ".AutomationResults";
        public const string Publish = Default + ".Publish";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(TestCaseManagementPermissions));
    }
}
