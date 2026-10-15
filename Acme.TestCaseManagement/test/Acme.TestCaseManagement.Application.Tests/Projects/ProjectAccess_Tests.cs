using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.SharedSteps.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using NSubstitute;
using Shouldly;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using Xunit;

namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// Members of a project (FR-028): a restricted project is seen only by its members and by the people who manage projects. What is in a
/// project that is out of reach is not found, whichever way it is asked for.
/// </summary>
public class ProjectAccess_Tests : TestCaseManagementApplicationTestBase
{
    private readonly IProjectAppService _projects;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;
    private readonly IRequirementAppService _requirements;

    public ProjectAccess_Tests()
    {
        _projects = GetRequiredService<IProjectAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _requirements = GetRequiredService<IRequirementAppService>();
    }

    private sealed record Made(ProjectDto Project, TestSuiteDto Suite, TestCaseDto TestCase, TestPlanDto Plan, TestRunDto Run, RequirementDto Requirement);

    /// <summary>A project with one of everything in it.</summary>
    private async Task<Made> MakeAsync(string key)
    {
        var project = await _projects.CreateAsync(new CreateProjectDto { Key = key, Name = $"Project {key}" });
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = $"Suite {key}", ProjectId = project.Id });
        var testCase = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = $"{key}-001", Title = $"Test {key}", Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });
        await _testCases.ChangeStatusAsync(testCase.Id, new ChangeTestCaseStatusDto { TargetStatus = Enums.TestCaseStatus.Approved });
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = $"Plan {key}", ProjectId = project.Id });
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = $"Run {key}", Environment = "Staging", TestPlanId = plan.Id, TestCaseIds = { testCase.Id } });
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = $"{key}-R1", Title = $"Requirement {key}", ProjectId = project.Id });
        return new Made(project, suite, testCase, plan, run, requirement);
    }

    private ProjectAccessProvider Provider(Guid? userId, bool manager)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Id.Returns(userId);
        user.IsAuthenticated.Returns(userId.HasValue);
        var permissions = Substitute.For<IPermissionChecker>();
        permissions.IsGrantedAsync(TestCaseManagementPermissions.Projects.Manage).Returns(manager);
        return new ProjectAccessProvider(
            GetRequiredService<IRepository<Project, Guid>>(), GetRequiredService<IRepository<ProjectMember, Guid>>(), user, permissions);
    }

    // ---- what a person sees

    [Fact]
    public async Task What_Is_In_A_Project_Out_Of_Reach_Is_Left_Out_Of_Every_List_And_Not_Found_By_Id()
    {
        var open = await MakeAsync("OPEN");
        var hidden = await MakeAsync("HIDE");

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            (await _projects.GetListAsync(new GetProjectListInput())).Select(p => p.Key).ShouldBe(new[] { "OPEN" });
            (await _suites.GetTreeAsync(null)).Select(s => s.Id).ShouldBe(new[] { open.Suite.Id });
            (await _testCases.GetListAsync(new GetTestCaseListInput())).Items.Select(t => t.Code).ShouldBe(new[] { "OPEN-001" });
            (await _plans.GetListAsync(new GetTestPlanListInput())).Items.Select(p => p.Name).ShouldBe(new[] { "Plan OPEN" });
            (await _runs.GetListAsync(new GetTestRunListInput())).Items.Select(r => r.Title).ShouldBe(new[] { "Run OPEN" });
            (await _requirements.GetListAsync(new GetRequirementListInput())).Items.Select(r => r.Code).ShouldBe(new[] { "OPEN-R1" });

            await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.GetAsync(hidden.TestCase.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _runs.GetAsync(hidden.Run.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _plans.GetAsync(hidden.Plan.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.GetAsync(hidden.Requirement.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.GetHistoryAsync(hidden.TestCase.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.GetVersionsAsync(hidden.TestCase.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.GetVersionAsync(hidden.TestCase.Id, 1));
        }

        // Out of the scope, every project is in reach again.
        (await _testCases.GetListAsync(new GetTestCaseListInput())).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_Code_Or_An_Automation_Id_Taken_In_A_Project_Out_Of_Reach_Is_Still_Taken()
    {
        var open = await MakeAsync("OPEN");
        var hidden = await MakeAsync("HIDE");
        await _testCases.UpdateAsync(hidden.TestCase.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = hidden.Suite.Id, Code = "HIDE-001", Title = "Test HIDE", AutomationId = "auto.hidden", Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _testCases.CreateAsync(new CreateUpdateTestCaseDto { SuiteId = open.Suite.Id, Code = "HIDE-001", Title = "Same code" })))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateTestCaseCode);
            (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _testCases.CreateAsync(new CreateUpdateTestCaseDto { SuiteId = open.Suite.Id, Code = "OPEN-NEW", Title = "Same id", AutomationId = "AUTO.HIDDEN" })))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateAutomationId);
            (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "hide-r1", Title = "Same code", ProjectId = open.Project.Id })))
                .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateRequirementCode);
        }
    }

    [Fact]
    public async Task A_Shared_Step_Group_Used_Only_In_A_Project_Out_Of_Reach_Cannot_Be_Deleted_By_Someone_Who_Cannot_See_The_Use()
    {
        var open = await MakeAsync("OPEN");
        var hidden = await MakeAsync("HIDE");
        var groups = GetRequiredService<ISharedStepGroupAppService>();
        var group = await groups.CreateAsync(new CreateUpdateSharedStepGroupDto { Name = "Log in", Steps = { new SharedStepDto { Action = "Sign in", ExpectedResult = "Ok" } } });
        await _testCases.InsertSharedStepsAsync(hidden.TestCase.Id, new InsertSharedStepsDto { SharedStepGroupId = group.Id });

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            (await groups.GetUsageAsync(group.Id)).ShouldBeEmpty();   // what the person sees: nobody uses it
            (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => groups.DeleteAsync(group.Id)))
                .Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupInUse);
        }

        // Out of the scope the use is seen, and still refuses; a group that nobody uses is deleted.
        (await Should.ThrowAsync<Volo.Abp.BusinessException>(() => groups.DeleteAsync(group.Id))).Code.ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupInUse);
        var unused = await groups.CreateAsync(new CreateUpdateSharedStepGroupDto { Name = "Unused", Steps = { new SharedStepDto { Action = "x", ExpectedResult = "y" } } });
        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            await groups.DeleteAsync(unused.Id);
        }
    }

    [Fact]
    public async Task A_Change_Of_A_Project_Out_Of_Reach_Is_Refused_And_Leaves_It_As_It_Was()
    {
        var open = await MakeAsync("OPEN");
        var hidden = await MakeAsync("HIDE");

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _testCases.ChangeStatusAsync(hidden.TestCase.Id, new ChangeTestCaseStatusDto { TargetStatus = Enums.TestCaseStatus.Deprecated }));
            await _testCases.DeleteAsync(hidden.TestCase.Id);   // like an id that does not exist: nothing is found, so nothing is deleted
            await Should.ThrowAsync<Exception>(() => _runs.CompleteAsync(hidden.Run.Id));
            await Should.ThrowAsync<Exception>(() => _suites.CreateAsync(new CreateTestSuiteDto { Name = "In the hidden project", ProjectId = hidden.Project.Id }));
        }

        (await _testCases.GetAsync(hidden.TestCase.Id)).Status.ShouldBe(Enums.TestCaseStatus.Approved);
        (await _runs.GetAsync(hidden.Run.Id)).Status.ShouldNotBe(Enums.RunStatus.Completed);
    }

    [Fact]
    public async Task An_Attempt_And_Its_Defect_Links_In_A_Run_Out_Of_Reach_Are_Not_Found_Either()
    {
        var open = await MakeAsync("OPEN");
        var hidden = await MakeAsync("HIDE");
        var attempt = await _runs.ExecuteItemAsync(hidden.Run.Id, hidden.Run.Items[0].Id,
            new ExecuteTestItemDto { Status = Enums.TestResultStatus.Failed, DurationSeconds = 3 });

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            await Should.ThrowAsync<EntityNotFoundException>(() => _runs.GetDefectLinksAsync(attempt.Id));
            await Should.ThrowAsync<EntityNotFoundException>(() => _runs.AddDefectLinkAsync(attempt.Id, new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "X-1" }));
            await Should.ThrowAsync<EntityNotFoundException>(() => _runs.GetExecutionsAsync(hidden.Run.Id, hidden.Run.Items[0].Id));
        }

        (await _runs.GetDefectLinksAsync(attempt.Id)).ShouldBeEmpty();   // out of the scope the attempt is there, and nothing was added
    }

    [Fact]
    public async Task The_Counts_And_The_Dashboard_Of_A_Person_Do_Not_Include_A_Project_Out_Of_Reach()
    {
        var open = await MakeAsync("OPEN");
        await MakeAsync("HIDE");

        using (ProjectAccessScope.Restrict(new[] { open.Project.Id }))
        {
            var dashboard = await GetRequiredService<Acme.TestCaseManagement.Insights.IDashboardAppService>()
                .GetAsync(new Acme.TestCaseManagement.Insights.Dtos.GetDashboardInput());
            dashboard.RunCount.ShouldBe(1);
            (await GetRequiredService<Acme.TestCaseManagement.Rtm.IRtmAppService>().GetMatrixAsync(new Acme.TestCaseManagement.Rtm.Dtos.GetRtmInput()))
                .Requirements.Select(r => r.Code).ShouldBe(new[] { "OPEN-R1" });
        }
    }

    [Fact]
    public async Task The_Bootstrap_Of_The_Default_Project_Is_Not_Fooled_By_A_Caller_That_Sees_No_Project()
    {
        await MakeAsync("HIDE");   // the only project there is, and the caller below may not see it

        using (ProjectAccessScope.Restrict(Array.Empty<Guid>()))
        {
            (await _projects.GetListAsync(new GetProjectListInput())).ShouldBeEmpty();
        }

        // No DEFAULT project was made on the side: the library has a project, so it needs none.
        (await _projects.GetListAsync(new GetProjectListInput())).Select(p => p.Key).ShouldBe(new[] { "HIDE" });
    }

    // ---- who may see what

    [Fact]
    public async Task A_Project_That_Is_Not_Restricted_Is_Seen_By_Everyone_And_A_Restricted_One_Only_By_Its_Members()
    {
        var open = await MakeAsync("OPEN");
        var secret = await MakeAsync("SECRET");
        var member = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await _projects.UpdateAccessAsync(secret.Project.Id, new ProjectAccessDto { IsRestricted = true, UserIds = { member } });

        var seenByMember = await Provider(member, manager: false).GetVisibleProjectIdsAsync();
        var seenByStranger = await Provider(stranger, manager: false).GetVisibleProjectIdsAsync();

        seenByMember.ShouldNotBeNull().ShouldBe(new[] { open.Project.Id, secret.Project.Id }, ignoreOrder: true);
        seenByStranger.ShouldNotBeNull().ShouldBe(new[] { open.Project.Id });
    }

    [Fact]
    public async Task Whoever_Manages_Projects_And_A_Caller_That_Is_Not_A_User_See_Every_Project()
    {
        var secret = await MakeAsync("SECRET");
        await _projects.UpdateAccessAsync(secret.Project.Id, new ProjectAccessDto { IsRestricted = true });

        (await Provider(Guid.NewGuid(), manager: true).GetVisibleProjectIdsAsync()).ShouldBeNull();
        (await Provider(null, manager: false).GetVisibleProjectIdsAsync()).ShouldBeNull();   // an API key has no user
    }

    [Fact]
    public async Task The_Members_Are_Replaced_By_The_List_Sent_And_Counted_And_Do_Not_Count_Once_The_Project_Is_Open_Again()
    {
        var made = await MakeAsync("TEAM");
        var one = Guid.NewGuid();
        var two = Guid.NewGuid();
        var three = Guid.NewGuid();

        var first = await _projects.UpdateAccessAsync(made.Project.Id, new ProjectAccessDto { IsRestricted = true, UserIds = { one, two, two, Guid.Empty } });
        first.IsRestricted.ShouldBeTrue();
        first.UserIds.ShouldBe(new[] { one, two }, ignoreOrder: true);

        var second = await _projects.UpdateAccessAsync(made.Project.Id, new ProjectAccessDto { IsRestricted = true, UserIds = { two, three } });
        second.UserIds.ShouldBe(new[] { two, three }, ignoreOrder: true);
        (await _projects.GetAccessAsync(made.Project.Id)).UserIds.ShouldBe(new[] { two, three }, ignoreOrder: true);

        var listed = (await _projects.GetListAsync(new GetProjectListInput())).Single(p => p.Id == made.Project.Id);
        (listed.IsRestricted, listed.MemberCount).ShouldBe((true, 2));

        var open = await _projects.UpdateAccessAsync(made.Project.Id, new ProjectAccessDto { IsRestricted = false, UserIds = { two } });
        open.IsRestricted.ShouldBeFalse();
        (await Provider(Guid.NewGuid(), manager: false).GetVisibleProjectIdsAsync()).ShouldNotBeNull().ShouldContain(made.Project.Id);
    }

    // ---- the project kept on the test case

    [Fact]
    public async Task A_Test_Case_Keeps_The_Project_Of_Its_Suite_And_One_Made_Before_That_Is_Given_It_The_Next_Time_The_Projects_Are_Listed()
    {
        var made = await MakeAsync("KEEP");
        var repository = GetRequiredService<IRepository<TestCase, Guid>>();

        (await repository.GetAsync(made.TestCase.Id)).ProjectId.ShouldBe(made.Project.Id);

        // As in a library from before: the test case has no project.
        await WithUnitOfWorkAsync(async () =>
        {
            var testCase = await repository.GetAsync(made.TestCase.Id);
            testCase.SetProject(Guid.Empty);
            await repository.UpdateAsync(testCase, autoSave: true);
        });

        await _projects.GetListAsync(new GetProjectListInput());

        (await repository.GetAsync(made.TestCase.Id)).ProjectId.ShouldBe(made.Project.Id);
    }
}
