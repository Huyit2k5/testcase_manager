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

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(TestCaseManagementPermissions));
    }
}
