using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement;

public class TestPlanAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestPlanAppService _plans;
    private readonly ITestRunAppService _runs;

    public TestPlanAppService_Tests()
    {
        _plans = GetRequiredService<ITestPlanAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
    }

    [Fact]
    public async Task Create_Should_Persist_A_Draft_Plan_With_Dates()
    {
        var milestone = Guid.NewGuid();

        var plan = await _plans.CreateAsync(new CreateTestPlanDto
        {
            Name = "Sprint 24",
            Description = "Checkout rework",
            MilestoneId = milestone,
            StartDate = new DateTime(2026, 10, 1),
            EndDate = new DateTime(2026, 10, 14),
        });

        plan.Id.ShouldNotBe(Guid.Empty);
        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.MilestoneId.ShouldBe(milestone);
        (await _plans.GetAsync(plan.Id)).Name.ShouldBe("Sprint 24");
    }

    [Fact]
    public async Task Create_Should_Reject_An_End_Date_Before_The_Start_Date()
    {
        (await Should.ThrowAsync<BusinessException>(() => _plans.CreateAsync(new CreateTestPlanDto
            {
                Name = "Bad",
                StartDate = new DateTime(2026, 10, 14),
                EndDate = new DateTime(2026, 10, 1),
            })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTestPlanDates);
    }

    [Fact]
    public async Task Update_Should_Change_The_Plan_Fields()
    {
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Old" });

        var updated = await _plans.UpdateAsync(plan.Id, new UpdateTestPlanDto { Name = "New", Description = "d" });

        updated.Name.ShouldBe("New");
        updated.Description.ShouldBe("d");
    }

    [Fact]
    public async Task Status_Should_Follow_The_Allowed_Transitions()
    {
        var plan = await _plans.CreateAsync(new CreateTestPlanDto { Name = "P" });

        (await _plans.ChangeStatusAsync(plan.Id, new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Active })).Status
            .ShouldBe(PlanStatus.Active);

        (await Should.ThrowAsync<BusinessException>(() => _plans.ChangeStatusAsync(
                plan.Id, new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Draft })))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidTestPlanStatusTransition);

        await _plans.ChangeStatusAsync(plan.Id, new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Completed });
        (await _plans.ChangeStatusAsync(plan.Id, new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Archived })).Status
            .ShouldBe(PlanStatus.Archived);
    }

    [Fact]
    public async Task GetList_Should_Filter_By_Name_And_Status_And_Sort()
    {
        await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 23" });
        var active = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Sprint 24" });
        await _plans.ChangeStatusAsync(active.Id, new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Active });

        (await _plans.GetListAsync(new GetTestPlanListInput { Filter = "24" })).Items.Single().Id.ShouldBe(active.Id);
        (await _plans.GetListAsync(new GetTestPlanListInput { Status = PlanStatus.Active })).Items.Single().Id.ShouldBe(active.Id);

        var sorted = await _plans.GetListAsync(new GetTestPlanListInput { Sorting = "name desc" });
        sorted.Items.Select(x => x.Name).ShouldBe(new[] { "Sprint 24", "Sprint 23" });
        sorted.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Delete_Should_Soft_Delete_A_Plan_Without_Runs_And_Reject_One_With_Runs()
    {
        var empty = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Empty" });
        await _plans.DeleteAsync(empty.Id);
        await Should.ThrowAsync<EntityNotFoundException>(() => _plans.GetAsync(empty.Id));

        var used = await _plans.CreateAsync(new CreateTestPlanDto { Name = "Used" });
        await _runs.CreateAsync(new CreateTestRunDto { TestPlanId = used.Id, Title = "R", Environment = "Staging" });

        (await Should.ThrowAsync<BusinessException>(() => _plans.DeleteAsync(used.Id)))
            .Code.ShouldBe(TestCaseManagementErrorCodes.TestPlanHasRuns);
    }
}
