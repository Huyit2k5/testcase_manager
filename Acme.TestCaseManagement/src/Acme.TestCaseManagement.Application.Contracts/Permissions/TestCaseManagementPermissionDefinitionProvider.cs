using Acme.TestCaseManagement.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Acme.TestCaseManagement.Permissions;

public class TestCaseManagementPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(
            TestCaseManagementPermissions.GroupName,
            L("Permission:TestCaseManagement"));

        var testCases = group.AddPermission(TestCaseManagementPermissions.TestCases.Default, L("Permission:TestCases"));
        testCases.AddChild(TestCaseManagementPermissions.TestCases.Create, L("Permission:TestCases.Create"));
        testCases.AddChild(TestCaseManagementPermissions.TestCases.Update, L("Permission:TestCases.Update"));
        testCases.AddChild(TestCaseManagementPermissions.TestCases.Delete, L("Permission:TestCases.Delete"));
        testCases.AddChild(TestCaseManagementPermissions.TestCases.Approve, L("Permission:TestCases.Approve"));

        var suites = group.AddPermission(TestCaseManagementPermissions.TestSuites.Default, L("Permission:TestSuites"));
        suites.AddChild(TestCaseManagementPermissions.TestSuites.Manage, L("Permission:TestSuites.Manage"));

        var plans = group.AddPermission(TestCaseManagementPermissions.TestPlans.Default, L("Permission:TestPlans"));
        plans.AddChild(TestCaseManagementPermissions.TestPlans.Manage, L("Permission:TestPlans.Manage"));

        var runs = group.AddPermission(TestCaseManagementPermissions.TestRuns.Default, L("Permission:TestRuns"));
        runs.AddChild(TestCaseManagementPermissions.TestRuns.Execute, L("Permission:TestRuns.Execute"));

        var requirements = group.AddPermission(TestCaseManagementPermissions.Requirements.Default, L("Permission:Requirements"));
        requirements.AddChild(TestCaseManagementPermissions.Requirements.Manage, L("Permission:Requirements.Manage"));

        var gates = group.AddPermission(TestCaseManagementPermissions.QualityGates.Default, L("Permission:QualityGates"));
        gates.AddChild(TestCaseManagementPermissions.QualityGates.Manage, L("Permission:QualityGates.Manage"));

        var signOff = group.AddPermission(TestCaseManagementPermissions.SignOff.Default, L("Permission:SignOff"));
        signOff.AddChild(TestCaseManagementPermissions.SignOff.Approve, L("Permission:SignOff.Approve"));

        var apiKeys = group.AddPermission(TestCaseManagementPermissions.ApiKeys.Default, L("Permission:ApiKeys"));
        apiKeys.AddChild(TestCaseManagementPermissions.ApiKeys.Manage, L("Permission:ApiKeys.Manage"));

        var automation = group.AddPermission(TestCaseManagementPermissions.AutomationResults.Default, L("Permission:AutomationResults"));
        automation.AddChild(TestCaseManagementPermissions.AutomationResults.Publish, L("Permission:AutomationResults.Publish"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<TestCaseManagementResource>(name);
    }
}
