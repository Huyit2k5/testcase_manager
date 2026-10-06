using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement;

public class RequirementAppService_Tests : TestCaseManagementApplicationTestBase
{
    private readonly IRequirementAppService _requirements;

    public RequirementAppService_Tests()
    {
        _requirements = GetRequiredService<IRequirementAppService>();
    }

    private static CreateUpdateRequirementDto New(string code, string title = "Two-Factor Authentication") =>
        new() { Code = code, Title = title };

    [Fact]
    public async Task Create_Should_Persist_The_Requirement_With_Its_Details()
    {
        var milestone = Guid.NewGuid();

        var created = await _requirements.CreateAsync(new CreateUpdateRequirementDto
        {
            Code = "REQ-AUTH-01",
            Title = "Two-Factor Authentication",
            Description = "Users can enable 2FA",
            AcceptanceCriteria = "Given a user with 2FA, when logging in, then a code is requested",
            Priority = PriorityLevel.High,
            MilestoneId = milestone,
        });

        var loaded = await _requirements.GetAsync(created.Id);
        loaded.Code.ShouldBe("REQ-AUTH-01");
        loaded.Priority.ShouldBe(PriorityLevel.High);
        loaded.MilestoneId.ShouldBe(milestone);
        loaded.AcceptanceCriteria.ShouldNotBeNull().ShouldContain("2FA");
    }

    [Fact]
    public async Task Create_And_Update_Should_Reject_A_Duplicate_Code()
    {
        await _requirements.CreateAsync(New("REQ-1"));
        var second = await _requirements.CreateAsync(New("REQ-2"));

        (await Should.ThrowAsync<BusinessException>(() => _requirements.CreateAsync(New("req-1"))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateRequirementCode);
        (await Should.ThrowAsync<BusinessException>(() => _requirements.UpdateAsync(second.Id, New("REQ-1"))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateRequirementCode);

        (await _requirements.UpdateAsync(second.Id, New("REQ-2", "Renamed"))).Title.ShouldBe("Renamed");
    }

    [Fact]
    public async Task A_Deleted_Requirement_Can_Be_Recreated_With_The_Same_Code()
    {
        var first = await _requirements.CreateAsync(New("REQ-1"));

        await _requirements.DeleteAsync(first.Id);

        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.GetAsync(first.Id));
        (await _requirements.CreateAsync(New("REQ-1"))).Id.ShouldNotBe(first.Id);
    }

    [Fact]
    public async Task GetList_Should_Filter_Sort_And_Page()
    {
        var milestone = Guid.NewGuid();
        await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "AUTH-1", Title = "Login", MilestoneId = milestone, Priority = PriorityLevel.Low });
        await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "AUTH-2", Title = "Logout", MilestoneId = milestone, Priority = PriorityLevel.Urgent });
        await _requirements.CreateAsync(New("PAY-1", "Checkout"));

        (await _requirements.GetListAsync(new GetRequirementListInput { MilestoneId = milestone })).TotalCount.ShouldBe(2);
        (await _requirements.GetListAsync(new GetRequirementListInput { Filter = "checkout" })).Items.Single().Code.ShouldBe("PAY-1");
        (await _requirements.GetListAsync(new GetRequirementListInput { Sorting = "priority desc" })).Items.First().Code.ShouldBe("AUTH-2");
        (await _requirements.GetListAsync(new GetRequirementListInput { Sorting = "code desc", SkipCount = 1, MaxResultCount = 1 }))
            .Items.Single().Code.ShouldBe("AUTH-2");
    }

    [Fact]
    public async Task Link_Should_Reject_An_Unknown_Test_Case_And_Unlink_Should_Be_Safe_To_Repeat()
    {
        var requirement = await _requirements.CreateAsync(New("REQ-1"));

        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.LinkTestCasesAsync(
            requirement.Id, new LinkTestCasesDto { TestCaseIds = { Guid.NewGuid() } }));
        await _requirements.UnlinkTestCaseAsync(requirement.Id, Guid.NewGuid());
        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.LinkTestCasesAsync(
            Guid.NewGuid(), new LinkTestCasesDto()));
    }
}
