using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>The code a project gives to a test case that has none, and the copy of a test case.</summary>
public class GeneratedCodeAndCopy_Tests : TestCaseManagementApplicationTestBase
{
    static GeneratedCodeAndCopy_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly IProjectAppService _projects;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;

    public GeneratedCodeAndCopy_Tests()
    {
        _projects = GetRequiredService<IProjectAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private async Task<TestSuiteDto> SuiteOfNewProjectAsync(string key)
    {
        var project = await _projects.CreateAsync(new CreateProjectDto { Key = key, Name = $"Project {key}" });
        return await _suites.CreateAsync(new CreateTestSuiteDto { Name = $"Suite {key}", ProjectId = project.Id });
    }

    private Task<TestCaseDto> CreateAsync(Guid suiteId, string? code, string title = "A test case") =>
        _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suiteId, Code = code, Title = title, Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

    [Fact]
    public async Task A_Test_Case_Without_A_Code_Gets_The_Next_Code_Of_Its_Project_In_Order()
    {
        var suite = await SuiteOfNewProjectAsync("EINV");

        var codes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add((await CreateAsync(suite.Id, null)).Code);
        }

        codes.ShouldBe(new[] { "EINV-001", "EINV-002", "EINV-003" });
    }

    [Fact]
    public async Task Each_Project_Counts_On_Its_Own_And_A_Blank_Code_Counts_As_No_Code()
    {
        var first = await SuiteOfNewProjectAsync("AAA");
        var second = await SuiteOfNewProjectAsync("BBB");

        (await CreateAsync(first.Id, "  ")).Code.ShouldBe("AAA-001");
        (await CreateAsync(second.Id, "")).Code.ShouldBe("BBB-001");
        (await CreateAsync(first.Id, null)).Code.ShouldBe("AAA-002");
    }

    [Fact]
    public async Task A_Code_That_A_Person_Typed_Is_Skipped_And_A_Deleted_Test_Case_Does_Not_Give_Its_Number_Back()
    {
        var suite = await SuiteOfNewProjectAsync("SKIP");
        await CreateAsync(suite.Id, "SKIP-002");

        var one = await CreateAsync(suite.Id, null);
        var three = await CreateAsync(suite.Id, null);
        one.Code.ShouldBe("SKIP-001");
        three.Code.ShouldBe("SKIP-003", "SKIP-002 is taken by the one typed by hand");

        await _testCases.DeleteAsync(three.Id);
        (await CreateAsync(suite.Id, null)).Code.ShouldBe("SKIP-004");
    }

    [Fact]
    public async Task A_Code_That_Is_Given_Is_Used_As_It_Is_And_Still_Has_To_Be_Free()
    {
        var suite = await SuiteOfNewProjectAsync("FREE");

        (await CreateAsync(suite.Id, "  MY-1  ")).Code.ShouldBe("MY-1");
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync(suite.Id, "MY-1"))).Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestCaseCode);
        (await CreateAsync(suite.Id, null)).Code.ShouldBe("FREE-001", "a code that was typed does not use a number of the project");
    }

    [Fact]
    public async Task Updating_Without_A_Code_Keeps_The_Code_The_Test_Case_Has()
    {
        var suite = await SuiteOfNewProjectAsync("KEEP");
        var created = await CreateAsync(suite.Id, null);

        var updated = await _testCases.UpdateAsync(created.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = null, Title = "New title", Steps = created.Steps,
        });

        (updated.Code, updated.Title).ShouldBe(("KEEP-001", "New title"));
    }

    [Fact]
    public async Task A_Copy_Is_A_Draft_With_The_Content_Of_The_Original_Under_A_New_Code()
    {
        var suite = await SuiteOfNewProjectAsync("COPY");
        var original = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = null, Title = "Login", Description = "Users sign in", Preconditions = "An account", Postconditions = "Signed in",
            Priority = PriorityLevel.Urgent, Severity = SeverityLevel.Critical, Kind = TestKind.Security, Layer = TestLayer.Integration,
            AutomationId = "login-spec", IsFlaky = true, Tags = new List<string> { "smoke", "login" },
            Steps = { new TestStepDto { Action = "Open", ExpectedResult = "Form", TestData = "url" }, new TestStepDto { Action = "Submit", ExpectedResult = "Home" } },
        });
        await _testCases.ChangeStatusAsync(original.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var copy = await _testCases.DuplicateAsync(original.Id, new DuplicateTestCaseDto());

        copy.Id.ShouldNotBe(original.Id);
        copy.Code.ShouldBe("COPY-002");
        copy.Title.ShouldBe("Login (copy)");
        copy.Status.ShouldBe(TestCaseStatus.Draft);
        copy.CurrentVersion.ShouldBe(0);
        (copy.Description, copy.Preconditions, copy.Postconditions).ShouldBe(("Users sign in", "An account", "Signed in"));
        (copy.Priority, copy.Severity, copy.Kind, copy.Layer, copy.IsFlaky).ShouldBe((PriorityLevel.Urgent, SeverityLevel.Critical, TestKind.Security, TestLayer.Integration, true));
        copy.Tags.ShouldBe(new[] { "login", "smoke" }, ignoreOrder: true);
        copy.Steps.Select(s => (s.StepOrder, s.Action, s.ExpectedResult, s.TestData)).ShouldBe(new (int, string, string, string?)[] { (1, "Open", "Form", "url"), (2, "Submit", "Home", null) });
        copy.Steps.Select(s => s.Id).ShouldNotContain(original.Steps[0].Id, "the steps are the copy's own");
        copy.AutomationId.ShouldBeNull("an Automation ID names one test case only");
        (await _testCases.GetVersionsAsync(copy.Id)).ShouldBeEmpty();

        var history = await _testCases.GetHistoryAsync(copy.Id);
        history.Single(h => h.Type == TestCaseHistoryType.Created).Note.ShouldBe("Copied from COPY-001");
    }

    [Fact]
    public async Task A_Copy_Can_Be_Given_Its_Code_Title_And_Suite_Even_In_Another_Project()
    {
        var here = await SuiteOfNewProjectAsync("HERE");
        var there = await SuiteOfNewProjectAsync("THERE");
        var original = await CreateAsync(here.Id, null, "Checkout");

        var named = await _testCases.DuplicateAsync(original.Id, new DuplicateTestCaseDto { Code = "CHK-OLD", Title = "Checkout, old way" });
        (named.Code, named.Title, named.SuiteId).ShouldBe(("CHK-OLD", "Checkout, old way", here.Id));

        var moved = await _testCases.DuplicateAsync(original.Id, new DuplicateTestCaseDto { SuiteId = there.Id });
        (moved.Code, moved.SuiteId).ShouldBe(("THERE-001", there.Id));
        (await _testCases.GetAsync(original.Id)).SuiteId.ShouldBe(here.Id);
    }

    [Fact]
    public async Task A_Copy_Needs_A_Free_Code_And_An_Existing_Test_Case()
    {
        var suite = await SuiteOfNewProjectAsync("DUP");
        var original = await CreateAsync(suite.Id, null);

        (await Should.ThrowAsync<BusinessException>(() => _testCases.DuplicateAsync(original.Id, new DuplicateTestCaseDto { Code = original.Code })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestCaseCode);
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() => _testCases.DuplicateAsync(Guid.NewGuid(), new DuplicateTestCaseDto()));
    }
}
