using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Validation;
using Xunit;

namespace Acme.TestCaseManagement.Validation;

/// <summary>A number that is not a member of an enum is refused at the door, wherever the enum is used.</summary>
public class EnumValidation_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestRunAppService _runs;
    private readonly ITestPlanAppService _plans;

    public EnumValidation_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
    }

    private async Task<(Guid SuiteId, TestCaseDto Approved)> ApprovedCaseAsync(string code = "TC-ENUM")
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Enums " + code });
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = code, Title = "Title", Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        });
        return (suite.Id, await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved }));
    }

    private static void ShouldNameMember(AbpValidationException exception, string member)
    {
        exception.ValidationErrors.ShouldContain(e => e.MemberNames.Contains(member), $"no validation error names {member}");
    }

    [Fact]
    public async Task A_Result_Status_That_Is_Not_A_Member_Of_The_Enum_Is_Refused_And_Nothing_Is_Recorded()
    {
        var (_, approved) = await ApprovedCaseAsync();
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Run", Environment = "QA", TestCaseIds = { approved.Id } });
        var item = run.Items.Single();

        var exception = await Should.ThrowAsync<AbpValidationException>(
            () => _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = (TestResultStatus)99 }));

        ShouldNameMember(exception, nameof(ExecuteTestItemDto.Status));
        (await _runs.GetAsync(run.Id)).Items.Single().CurrentStatus.ShouldBe(TestResultStatus.Untested);

        // A real status still works.
        await _runs.ExecuteItemAsync(run.Id, item.Id, new ExecuteTestItemDto { Status = TestResultStatus.Passed });
    }

    [Fact]
    public async Task The_Severity_Of_A_Defect_Link_Is_Checked_Inside_The_Nested_Object()
    {
        var (_, approved) = await ApprovedCaseAsync();
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Run", Environment = "QA", TestCaseIds = { approved.Id } });

        var exception = await Should.ThrowAsync<AbpValidationException>(() => _runs.ExecuteItemAsync(
            run.Id,
            run.Items.Single().Id,
            new ExecuteTestItemDto
            {
                Status = TestResultStatus.Failed,
                Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1", Severity = (SeverityLevel)99 } },
            }));

        ShouldNameMember(exception, "Defects[0].Severity");
    }

    [Fact]
    public async Task The_Fields_Of_A_Test_Case_And_A_Filter_Are_Checked_Too()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Fields" });

        var create = await Should.ThrowAsync<AbpValidationException>(() => _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id,
            Code = "TC-BAD",
            Title = "Bad",
            Priority = (PriorityLevel)42,
            Kind = (TestKind)42,
            Steps = { new TestStepDto { Action = "a", ExpectedResult = "b" } },
        }));
        ShouldNameMember(create, nameof(CreateUpdateTestCaseDto.Priority));
        ShouldNameMember(create, nameof(CreateUpdateTestCaseDto.Kind));

        var filter = await Should.ThrowAsync<AbpValidationException>(() => _testCases.GetListAsync(new GetTestCaseListInput { Status = (TestCaseStatus)77 }));
        ShouldNameMember(filter, nameof(GetTestCaseListInput.Status));

        // A nullable filter that is left out, and a valid one, are fine.
        (await _testCases.GetListAsync(new GetTestCaseListInput { Status = null })).ShouldNotBeNull();
        (await _testCases.GetListAsync(new GetTestCaseListInput { Status = TestCaseStatus.Draft })).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_Target_Status_That_Does_Not_Exist_Is_Refused_For_Test_Cases_And_Plans()
    {
        var (_, approved) = await ApprovedCaseAsync("TC-TARGET");

        ShouldNameMember(
            await Should.ThrowAsync<AbpValidationException>(
                () => _testCases.ChangeStatusAsync(approved.Id, new ChangeTestCaseStatusDto { TargetStatus = (TestCaseStatus)9 })),
            nameof(ChangeTestCaseStatusDto.TargetStatus));

        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Plan" });
        ShouldNameMember(
            await Should.ThrowAsync<AbpValidationException>(
                () => _plans.ChangeStatusAsync(plan.Id, new ChangeTestPlanStatusDto { TargetStatus = (PlanStatus)9 })),
            nameof(ChangeTestPlanStatusDto.TargetStatus));
    }

    [Fact]
    public async Task An_Approved_Test_Case_Edited_Down_To_No_Steps_Goes_To_Review_And_Cannot_Be_Approved_Again()
    {
        var (suiteId, approved) = await ApprovedCaseAsync("TC-STEPS");
        var edit = new CreateUpdateTestCaseDto { SuiteId = suiteId, Code = approved.Code, Title = approved.Title };

        var edited = await _testCases.UpdateAsync(approved.Id, edit);

        // The edit is saved but waits for review; nothing is published, and approval needs a step.
        edited.Status.ShouldBe(TestCaseStatus.UnderReview);
        edited.Steps.ShouldBeEmpty();
        edited.CurrentVersion.ShouldBe(1);
        (await _testCases.GetVersionsAsync(approved.Id)).Count.ShouldBe(1);
        (await Should.ThrowAsync<BusinessException>(
                () => _testCases.ChangeStatusAsync(approved.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestCaseHasNoSteps);
    }
}
