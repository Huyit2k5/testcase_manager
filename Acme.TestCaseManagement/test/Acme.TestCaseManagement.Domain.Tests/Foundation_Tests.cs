using System.Globalization;
using Acme.TestCaseManagement.EntityFrameworkCore;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp.Localization;
using Xunit;

namespace Acme.TestCaseManagement;

public class Foundation_Tests : TestCaseManagementDomainTestBase
{
    [Fact]
    public void DbContext_Should_Be_Resolvable_Through_Its_Interface()
    {
        GetRequiredService<ITestCaseManagementDbContext>().ShouldNotBeNull();
        GetRequiredService<TestCaseManagementDbContext>().ShouldNotBeNull();
    }

    [Fact]
    public void Table_Names_Should_Use_The_Tcm_Prefix()
    {
        TestCaseManagementDbContextModelCreatingExtensions.TableName("TestCases").ShouldBe("TcmTestCases");
        TestCaseManagementDbContextModelCreatingExtensions.TableName("Suites").ShouldBe("TcmSuites");
    }

    [Fact]
    public void Entities_Should_Map_To_Tcm_Prefixed_Tables_With_Tenant_And_Soft_Delete_Filters()
    {
        var model = GetRequiredService<TestCaseManagementDbContext>().Model;

        var expected = new Dictionary<Type, string>
        {
            [typeof(Suites.TestSuite)] = "TcmSuites",
            [typeof(TestCases.TestCase)] = "TcmTestCases",
            [typeof(TestCases.TestStep)] = "TcmTestSteps",
            [typeof(TestCases.TestCaseVersion)] = "TcmTestCaseVersions",
        };

        foreach (var (type, table) in expected)
        {
            model.FindEntityType(type)!.GetTableName().ShouldBe(table);
        }

        // ABP registers one query filter per entity that is IMultiTenant and/or ISoftDelete.
        model.FindEntityType(typeof(Suites.TestSuite))!.GetDeclaredQueryFilters().ShouldNotBeEmpty();
        model.FindEntityType(typeof(TestCases.TestCase))!.GetDeclaredQueryFilters().ShouldNotBeEmpty();
        model.FindEntityType(typeof(TestCases.TestCaseVersion))!.GetDeclaredQueryFilters().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("en", "Test Case Management")]
    [InlineData("vi", "Quản lý Test Case")]
    public void Localization_Resource_Should_Be_Registered_For_En_And_Vi(string culture, string expected)
    {
        var localizer = GetRequiredService<IStringLocalizer<TestCaseManagementResource>>();

        using (CultureHelper.Use(new CultureInfo(culture)))
        {
            localizer["Menu:TestCaseManagement"].Value.ShouldBe(expected);
        }
    }

    [Fact]
    public void Error_Codes_Should_Have_A_Localized_Message_For_Every_Constant()
    {
        var localizer = GetRequiredService<IStringLocalizer<TestCaseManagementResource>>();
        var codes = typeof(TestCaseManagementErrorCodes)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        foreach (var code in codes)
        {
            localizer[code].ResourceNotFound.ShouldBeFalse($"Missing localization for {code}");
        }
    }

    [Fact]
    public void Enums_Should_Have_The_Values_Defined_In_The_Plan()
    {
        Enum.GetNames<PriorityLevel>().ShouldBe(["Low", "Medium", "High", "Urgent"]);
        Enum.GetNames<SeverityLevel>().ShouldBe(["Low", "Medium", "High", "Critical"]);
        Enum.GetNames<TestCaseStatus>().ShouldBe(["Draft", "UnderReview", "Approved", "Deprecated"]);
        Enum.GetNames<TestResultStatus>().ShouldBe(["Untested", "Passed", "Failed", "Blocked", "Skipped"]);
        Enum.GetNames<ExecutionType>().ShouldBe(["Manual", "Automated", "Hybrid"]);
        Enum.GetNames<TestKind>().ShouldBe(["Functional", "Performance", "Security", "Usability"]);
        Enum.GetNames<TestLayer>().ShouldBe(["Unit", "Integration", "E2E", "Acceptance"]);
    }
}
