using System.Text;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Rtm;
using Acme.TestCaseManagement.Rtm.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer;
using Acme.TestCaseManagement.Transfer.Dtos;
using Acme.TestCaseManagement.Insights;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement;

public class ProjectAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly IProjectAppService _projects;
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;
    private readonly IRequirementAppService _requirements;

    public ProjectAppService_Tests()
    {
        _projects = GetRequiredService<IProjectAppService>();
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _requirements = GetRequiredService<IRequirementAppService>();
    }

    private Task<ProjectDto> ProjectAsync(string key) =>
        _projects.CreateAsync(new CreateProjectDto { Key = key, Name = $"Project {key}" });

    /// <summary>An approved test case in a new root suite of the project.</summary>
    private async Task<(TestSuiteDto Suite, TestCaseDto TestCase)> ApprovedCaseAsync(ProjectDto project, string code)
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = $"Suite of {code}", ProjectId = project.Id });
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = code, Title = $"Title of {code}",
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        });
        var approved = await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        return (suite, approved);
    }

    // ---- the project itself

    [Fact]
    public async Task A_Key_Is_Kept_In_Capitals_Must_Be_Valid_And_Is_Unique()
    {
        var project = await ProjectAsync("einv");

        project.Key.ShouldBe("EINV");
        project.IsArchived.ShouldBeFalse();

        (await Should.ThrowAsync<BusinessException>(() => ProjectAsync("EINV"))).Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateProjectKey);
        foreach (var bad in new[] { "A", "1ABC", "HR M", "AB-C" })
        {
            (await Should.ThrowAsync<BusinessException>(() => ProjectAsync(bad))).Code.ShouldBe(TestCaseManagementErrorCodes.InvalidProjectKey, bad);
        }
    }

    [Fact]
    public async Task The_Name_And_Description_Change_But_The_Key_Does_Not()
    {
        var project = await ProjectAsync("HRM");

        var updated = await _projects.UpdateAsync(project.Id, new UpdateProjectDto { Name = "Human resources", Description = "Payroll and leave" });

        updated.Key.ShouldBe("HRM");
        updated.Name.ShouldBe("Human resources");
        updated.Description.ShouldBe("Payroll and leave");
    }

    [Fact]
    public async Task A_Library_Without_Projects_Gets_The_Default_Project_And_Gives_It_What_Was_There_Before()
    {
        // What a library made before projects existed looks like: a suite, a plan, a requirement and a run with no project.
        Guid suiteId = Guid.NewGuid(), planId = Guid.NewGuid(), requirementId = Guid.NewGuid(), runId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await GetRequiredService<IRepository<TestSuite, Guid>>().InsertAsync(new TestSuite(suiteId, null, "Old suite", null, 0), autoSave: true);
            await GetRequiredService<IRepository<TestPlan, Guid>>().InsertAsync(new TestPlan(planId, null, "Old plan"), autoSave: true);
            await GetRequiredService<IRepository<Requirement, Guid>>().InsertAsync(new Requirement(requirementId, null, "REQ-OLD", "Old requirement"), autoSave: true);
            await GetRequiredService<IRepository<TestRun, Guid>>().InsertAsync(new TestRun(runId, null, "Old run", "Staging"), autoSave: true);
        });
        (await _suites.GetAsync(suiteId)).ProjectId.ShouldBe(Guid.Empty);

        var list = await _projects.GetListAsync(new GetProjectListInput());

        var project = list.ShouldHaveSingleItem();
        project.Key.ShouldBe(ProjectConsts.DefaultKey);
        (await _suites.GetAsync(suiteId)).ProjectId.ShouldBe(project.Id);
        (await _plans.GetAsync(planId)).ProjectId.ShouldBe(project.Id);
        (await _requirements.GetAsync(requirementId)).ProjectId.ShouldBe(project.Id);
        (await _runs.GetAsync(runId)).ProjectId.ShouldBe(project.Id);
        project.SuiteCount.ShouldBe(1);
        project.PlanCount.ShouldBe(1);
        project.RequirementCount.ShouldBe(1);
        project.RunCount.ShouldBe(1);

        // Asking again changes nothing.
        (await _projects.GetListAsync(new GetProjectListInput())).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_A_Project_New_Things_Go_To_The_Default_Project()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" });
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Plan" });
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "REQ-1", Title = "One" });
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Run", Environment = "Staging" });

        var projects = await _projects.GetListAsync(new GetProjectListInput());

        var fallback = projects.ShouldHaveSingleItem();
        fallback.Key.ShouldBe(ProjectConsts.DefaultKey);
        new[] { suite.ProjectId, plan.ProjectId, requirement.ProjectId, run.ProjectId }.ShouldAllBe(id => id == fallback.Id);
    }

    [Fact]
    public async Task The_Project_Counts_What_Is_In_It()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (suite, _) = await ApprovedCaseAsync(web, "WEB-1");
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Child", ParentId = suite.Id });
        await _plans.CreateAsync(new CreateTestPlanDto { Name = "Plan", ProjectId = web.Id });
        await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "WEB-R1", Title = "One", ProjectId = web.Id });

        var list = await _projects.GetListAsync(new GetProjectListInput());

        var counted = list.Single(p => p.Id == web.Id);
        (counted.SuiteCount, counted.TestCaseCount, counted.PlanCount, counted.RequirementCount, counted.RunCount).ShouldBe((2, 1, 1, 1, 0));
        var empty = list.Single(p => p.Id == hrm.Id);
        (empty.SuiteCount, empty.TestCaseCount, empty.PlanCount, empty.RequirementCount, empty.RunCount).ShouldBe((0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task An_Archived_Project_Takes_Nothing_New_And_Is_Left_Out_Of_The_List()
    {
        var project = await ProjectAsync("OLD");
        var (suite, _) = await ApprovedCaseAsync(project, "OLD-1");

        await _projects.ArchiveAsync(project.Id);

        (await _projects.GetListAsync(new GetProjectListInput())).ShouldNotContain(p => p.Id == project.Id);
        (await _projects.GetListAsync(new GetProjectListInput { IncludeArchived = true })).ShouldContain(p => p.Id == project.Id);

        async Task Refused(Func<Task> action) =>
            (await Should.ThrowAsync<BusinessException>(action)).Code.ShouldBe(TestCaseManagementErrorCodes.ProjectArchived);

        await Refused(() => _suites.CreateAsync(new CreateTestSuiteDto { Name = "More", ProjectId = project.Id }));
        await Refused(() => _suites.CreateAsync(new CreateTestSuiteDto { Name = "Child", ParentId = suite.Id }));
        await Refused(() => _plans.CreateAsync(new CreateTestPlanDto { Name = "Plan", ProjectId = project.Id }));
        await Refused(() => _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "OLD-R", Title = "x", ProjectId = project.Id }));
        await Refused(() => _testCases.CreateAsync(new CreateUpdateTestCaseDto { SuiteId = suite.Id, Code = "OLD-2", Title = "x" }));

        // It can be read, and it can come back.
        (await _suites.GetTreeAsync(project.Id)).Count.ShouldBe(1);
        (await _projects.RestoreAsync(project.Id)).IsArchived.ShouldBeFalse();
        (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "More", ProjectId = project.Id })).ProjectId.ShouldBe(project.Id);
    }

    [Fact]
    public async Task A_Project_Is_Deleted_Only_While_It_Is_Empty()
    {
        var kept = await ProjectAsync("KEEP");
        await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library", ProjectId = kept.Id });
        (await Should.ThrowAsync<BusinessException>(() => _projects.DeleteAsync(kept.Id))).Code.ShouldBe(TestCaseManagementErrorCodes.ProjectNotEmpty);

        var empty = await ProjectAsync("GONE");
        await _projects.DeleteAsync(empty.Id);

        (await _projects.GetListAsync(new GetProjectListInput { IncludeArchived = true })).ShouldNotContain(p => p.Id == empty.Id);
        // Its key is free again.
        (await ProjectAsync("GONE")).Key.ShouldBe("GONE");
    }

    // ---- suites and test cases

    [Fact]
    public async Task A_Suite_Below_Another_Is_In_Its_Project_And_The_Tree_Shows_One_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (webSuite, _) = await ApprovedCaseAsync(web, "WEB-1");
        await ApprovedCaseAsync(hrm, "HRM-1");

        var child = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Checkout", ParentId = webSuite.Id });

        child.ProjectId.ShouldBe(web.Id);
        (await Should.ThrowAsync<BusinessException>(() =>
                _suites.CreateAsync(new CreateTestSuiteDto { Name = "Wrong", ParentId = webSuite.Id, ProjectId = hrm.Id })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);

        var webTree = await _suites.GetTreeAsync(web.Id);
        webTree.Single().Children.Single().Name.ShouldBe("Checkout");
        (await _suites.GetTreeAsync(hrm.Id)).Single().Children.ShouldBeEmpty();
        (await _suites.GetTreeAsync()).Count.ShouldBe(2);   // without a project: all of them
    }

    [Fact]
    public async Task A_Suite_Cannot_Be_Moved_Under_A_Suite_Of_Another_Project_And_Root_Suites_Keep_Their_Own_Order()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (webSuite, _) = await ApprovedCaseAsync(web, "WEB-1");
        var (hrmSuite, _) = await ApprovedCaseAsync(hrm, "HRM-1");

        (await Should.ThrowAsync<BusinessException>(() => _suites.MoveAsync(webSuite.Id, new MoveTestSuiteDto { NewParentId = hrmSuite.Id, NewOrder = 0 })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);

        // The first root suite of each project is number 0: the order is per project.
        (await _suites.GetAsync(webSuite.Id)).Order.ShouldBe(0);
        (await _suites.GetAsync(hrmSuite.Id)).Order.ShouldBe(0);
    }

    [Fact]
    public async Task The_List_Of_Test_Cases_Can_Be_Limited_To_A_Project_And_A_Test_Case_Stays_In_Its_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, webCase) = await ApprovedCaseAsync(web, "WEB-1");
        var (hrmSuite, hrmCase) = await ApprovedCaseAsync(hrm, "HRM-1");

        (await _testCases.GetListAsync(new GetTestCaseListInput { ProjectId = web.Id })).Items.Select(t => t.Code).ShouldBe(new[] { "WEB-1" });
        (await _testCases.GetListAsync(new GetTestCaseListInput { ProjectId = hrm.Id })).Items.Select(t => t.Code).ShouldBe(new[] { "HRM-1" });
        (await _testCases.GetListAsync(new GetTestCaseListInput())).TotalCount.ShouldBe(2);
        (await _testCases.GetListAsync(new GetTestCaseListInput { ProjectId = (await ProjectAsync("EMPTY")).Id })).TotalCount.ShouldBe(0);

        var move = new CreateUpdateTestCaseDto
        {
            SuiteId = hrmSuite.Id, Code = webCase.Code, Title = webCase.Title,
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "Done" } },
        };
        (await Should.ThrowAsync<BusinessException>(() => _testCases.UpdateAsync(webCase.Id, move)))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);
        (await _testCases.GetAsync(hrmCase.Id)).SuiteId.ShouldBe(hrmSuite.Id);
    }

    [Fact]
    public async Task The_Tags_Can_Be_Limited_To_The_Test_Cases_Of_A_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, webCase) = await ApprovedCaseAsync(web, "WEB-1");
        var (_, hrmCase) = await ApprovedCaseAsync(hrm, "HRM-1");
        await _testCases.SetTagsAsync(webCase.Id, new SetTestCaseTagsDto { Tags = { "checkout", "smoke" } });
        await _testCases.SetTagsAsync(hrmCase.Id, new SetTestCaseTagsDto { Tags = { "payroll", "smoke" } });

        (await _testCases.GetTagsAsync(web.Id)).Select(t => t.Name).OrderBy(n => n).ShouldBe(new[] { "checkout", "smoke" });
        (await _testCases.GetTagsAsync(hrm.Id)).Select(t => t.Name).OrderBy(n => n).ShouldBe(new[] { "payroll", "smoke" });
        (await _testCases.GetTagsAsync()).Single(t => t.Name == "smoke").Count.ShouldBe(2);
    }

    // ---- plans and runs

    [Fact]
    public async Task A_Run_Of_A_Plan_Is_In_The_Project_Of_The_Plan_And_Takes_Only_Its_Test_Cases()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, webCase) = await ApprovedCaseAsync(web, "WEB-1");
        var (_, hrmCase) = await ApprovedCaseAsync(hrm, "HRM-1");
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Web 1.0", ProjectId = web.Id });

        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Smoke", Environment = "Staging", TestPlanId = plan.Id, TestCaseIds = { webCase.Id } });

        run.ProjectId.ShouldBe(web.Id);
        (await Should.ThrowAsync<BusinessException>(() => _runs.AddItemsAsync(run.Id, new AddTestRunItemsDto { TestCaseIds = { hrmCase.Id } })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);
        (await Should.ThrowAsync<BusinessException>(() =>
                _runs.CreateAsync(new CreateTestRunDto { Title = "Wrong", Environment = "Staging", TestPlanId = plan.Id, ProjectId = hrm.Id })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);
        (await _runs.GetAsync(run.Id)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_Run_Without_A_Plan_Is_In_The_Project_Of_Its_First_Test_Case_Or_The_One_Named()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, hrmCase) = await ApprovedCaseAsync(hrm, "HRM-1");

        var derived = await _runs.CreateAsync(new CreateTestRunDto { Title = "From a test case", Environment = "Staging", TestCaseIds = { hrmCase.Id } });
        var named = await _runs.CreateAsync(new CreateTestRunDto { Title = "Named", Environment = "Staging", ProjectId = web.Id });

        derived.ProjectId.ShouldBe(hrm.Id);
        named.ProjectId.ShouldBe(web.Id);
        (await _runs.GetListAsync(new GetTestRunListInput { ProjectId = hrm.Id })).Items.Select(r => r.Title).ShouldBe(new[] { "From a test case" });
        (await _runs.GetListAsync(new GetTestRunListInput { ProjectId = web.Id })).Items.Select(r => r.Title).ShouldBe(new[] { "Named" });
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_List_Of_Plans_Can_Be_Limited_To_A_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        await _plans.CreateAsync(new CreateTestPlanDto { Name = "Web 1.0", ProjectId = web.Id });
        await _plans.CreateAsync(new CreateTestPlanDto { Name = "HRM 1.0", ProjectId = hrm.Id });

        (await _plans.GetListAsync(new GetTestPlanListInput { ProjectId = web.Id })).Items.Select(p => p.Name).ShouldBe(new[] { "Web 1.0" });
        (await _plans.GetListAsync(new GetTestPlanListInput())).TotalCount.ShouldBe(2);
    }

    // ---- requirements, traceability, dashboard

    [Fact]
    public async Task A_Requirement_Is_Linked_Only_To_Test_Cases_Of_Its_Project_And_The_Lists_Follow_The_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, webCase) = await ApprovedCaseAsync(web, "WEB-1");
        var (_, hrmCase) = await ApprovedCaseAsync(hrm, "HRM-1");
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "WEB-R1", Title = "Pay", ProjectId = web.Id });
        await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "HRM-R1", Title = "Leave", ProjectId = hrm.Id });

        await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = { webCase.Id } });
        (await Should.ThrowAsync<BusinessException>(() => _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = { hrmCase.Id } })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DifferentProject);

        (await _requirements.GetListAsync(new GetRequirementListInput { ProjectId = web.Id })).Items.Select(r => r.Code).ShouldBe(new[] { "WEB-R1" });
        (await GetRequiredService<IRtmAppService>().GetMatrixAsync(new GetRtmInput { ProjectId = hrm.Id })).Requirements.Select(r => r.Code).ShouldBe(new[] { "HRM-R1" });
        (await GetRequiredService<IRtmAppService>().GetMatrixAsync(new GetRtmInput())).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_Dashboard_Counts_Only_The_Runs_Of_The_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var (_, webCase) = await ApprovedCaseAsync(web, "WEB-1");
        var (_, hrmCase1) = await ApprovedCaseAsync(hrm, "HRM-1");
        var (_, hrmCase2) = await ApprovedCaseAsync(hrm, "HRM-2");
        await _runs.CreateAsync(new CreateTestRunDto { Title = "Web", Environment = "Staging", TestCaseIds = { webCase.Id } });
        await _runs.CreateAsync(new CreateTestRunDto { Title = "HRM", Environment = "Staging", TestCaseIds = { hrmCase1.Id, hrmCase2.Id } });
        var dashboard = GetRequiredService<IDashboardAppService>();

        (await dashboard.GetAsync(new GetDashboardInput { ProjectId = web.Id })).Progress.TotalItems.ShouldBe(1);
        (await dashboard.GetAsync(new GetDashboardInput { ProjectId = hrm.Id })).Progress.TotalItems.ShouldBe(2);
        (await dashboard.GetAsync(new GetDashboardInput())).Progress.TotalItems.ShouldBe(3);
    }

    // ---- import and export

    private static ImportTestCasesInput Csv(string text, Guid? projectId) => new()
    {
        File = new RemoteStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(text)), "cases.csv", "text/csv"),
        ProjectId = projectId,
    };

    [Fact]
    public async Task An_Import_Makes_Its_Suites_In_The_Project_And_Looks_For_Them_There_And_An_Export_Takes_One_Project()
    {
        var web = await ProjectAsync("WEB");
        var hrm = await ProjectAsync("HRM");
        var transfer = GetRequiredService<ITestCaseTransferAppService>();
        const string header = "Suite,Code,Title,Priority,Action,ExpectedResult,TestData\n";

        // The same suite name in two projects is two suites.
        (await transfer.ImportAsync(Csv(header + "Checkout,WEB-1,Pay,,A,B,\n", web.Id))).Created.ShouldBe(1);
        (await transfer.ImportAsync(Csv(header + "Checkout,HRM-1,Leave,,A,B,\n", hrm.Id))).Created.ShouldBe(1);

        (await _suites.GetTreeAsync(web.Id)).Single().Name.ShouldBe("Checkout");
        (await _suites.GetTreeAsync(hrm.Id)).Single().Name.ShouldBe("Checkout");
        (await _suites.GetTreeAsync()).Count.ShouldBe(2);
        (await _testCases.GetListAsync(new GetTestCaseListInput { ProjectId = web.Id })).Items.Select(t => t.Code).ShouldBe(new[] { "WEB-1" });

        var export = await transfer.ExportAsync(new ExportTestCasesInput { Format = TransferFormat.Csv, ProjectId = hrm.Id });
        using var stream = new MemoryStream();
        await export.GetStream().CopyToAsync(stream);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        text.ShouldContain("HRM-1");
        text.ShouldNotContain("WEB-1");
    }

    [Fact]
    public async Task An_Import_Into_An_Archived_Project_Is_Refused()
    {
        var project = await ProjectAsync("OLD");
        await _projects.ArchiveAsync(project.Id);
        var transfer = GetRequiredService<ITestCaseTransferAppService>();

        (await Should.ThrowAsync<BusinessException>(() =>
                transfer.ImportAsync(Csv("Suite,Code,Title,Priority,Action,ExpectedResult,TestData\nS,OLD-1,x,,A,B,\n", project.Id))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.ProjectArchived);
    }
}
