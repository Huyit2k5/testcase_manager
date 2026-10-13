using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Rtm;
using Acme.TestCaseManagement.Rtm.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement.Requirements;

/// <summary>
/// A requirement changes: the test cases linked to it are to be checked against it (suspect links), until someone confirms them.
/// </summary>
public class RequirementChange_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly IRequirementAppService _requirements;
    private readonly IRtmAppService _rtm;
    private Guid? _suiteId;
    private int _count;

    public RequirementChange_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _requirements = GetRequiredService<IRequirementAppService>();
        _rtm = GetRequiredService<IRtmAppService>();
    }

    private async Task<TestCaseDto> CaseAsync(string? code = null)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Library" })).Id;
        return await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value, Code = code ?? $"RC-{++_count}", Title = "A test", Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });
    }

    private async Task<RequirementDto> RequirementAsync(string code, params Guid[] testCaseIds)
    {
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto
        {
            Code = code, Title = "Pay by card", Description = "The customer pays", AcceptanceCriteria = "A paid order exists", Priority = PriorityLevel.Medium,
        });
        if (testCaseIds.Length > 0)
        {
            await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = testCaseIds.ToList() });
        }

        return requirement;
    }

    private static CreateUpdateRequirementDto Edit(RequirementDto from, Action<CreateUpdateRequirementDto> change)
    {
        var input = new CreateUpdateRequirementDto
        {
            Code = from.Code, Title = from.Title, Description = from.Description, AcceptanceCriteria = from.AcceptanceCriteria,
            Priority = from.Priority, MilestoneId = from.MilestoneId,
        };
        change(input);
        return input;
    }

    private async Task<RequirementLinkDto> LinkOfAsync(Guid testCaseId, string requirementCode) =>
        (await _requirements.GetForTestCaseAsync(testCaseId)).Single(l => l.Code == requirementCode);

    // ---- a change marks the links

    [Fact]
    public async Task A_Change_Of_The_Title_The_Description_Or_The_Acceptance_Criteria_Makes_Every_Linked_Test_Case_Suspect()
    {
        var one = await CaseAsync();
        var two = await CaseAsync();
        var requirement = await RequirementAsync("REQ-1", one.Id, two.Id);
        (await _requirements.GetAsync(requirement.Id)).SuspectTestCaseCount.ShouldBe(0);

        var changed = await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => { r.Title = "Pay by card or wallet"; r.AcceptanceCriteria = "Two orders exist"; }));

        changed.SuspectTestCaseCount.ShouldBe(2);
        foreach (var id in new[] { one.Id, two.Id })
        {
            var link = await LinkOfAsync(id, "REQ-1");
            link.IsSuspect.ShouldBeTrue();
            link.SuspectReason.ShouldBe("Title, AcceptanceCriteria");
            link.SuspectSince.ShouldNotBeNull();
        }

        (await _requirements.GetAsync(requirement.Id)).SuspectTestCaseCount.ShouldBe(2);
        (await _requirements.GetListAsync(new GetRequirementListInput())).Items.Single(r => r.Code == "REQ-1").SuspectTestCaseCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_Change_Of_The_Code_The_Priority_Or_The_Milestone_Does_Not_Make_Anything_Suspect()
    {
        var testCase = await CaseAsync();
        var requirement = await RequirementAsync("REQ-2", testCase.Id);

        var changed = await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => { r.Code = "REQ-2B"; r.Priority = PriorityLevel.Urgent; r.MilestoneId = Guid.NewGuid(); }));

        changed.SuspectTestCaseCount.ShouldBe(0);
        (await LinkOfAsync(testCase.Id, "REQ-2B")).IsSuspect.ShouldBeFalse();
    }

    [Fact]
    public async Task Saving_The_Same_Text_Again_Or_With_Spaces_Around_It_Changes_Nothing()
    {
        var testCase = await CaseAsync();
        var requirement = await _requirements.CreateAsync(new CreateUpdateRequirementDto { Code = "REQ-3", Title = "Same", Description = null, AcceptanceCriteria = "Criteria" });
        await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = { testCase.Id } });

        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => { r.Title = "  Same "; r.Description = "   "; r.AcceptanceCriteria = "Criteria  "; }));

        (await LinkOfAsync(testCase.Id, "REQ-3")).IsSuspect.ShouldBeFalse();
        (await _testCases.GetHistoryAsync(testCase.Id)).ShouldNotContain(h => h.Type == TestCaseHistoryType.RequirementChanged);
    }

    [Fact]
    public async Task A_Second_Change_Keeps_The_Time_Of_The_First_And_Gathers_The_Fields()
    {
        var testCase = await CaseAsync();
        var requirement = await RequirementAsync("REQ-4", testCase.Id);

        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => r.Title = "First"));
        var first = await LinkOfAsync(testCase.Id, "REQ-4");
        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => { r.Title = "Second"; r.Description = "Other"; }));
        var second = await LinkOfAsync(testCase.Id, "REQ-4");

        second.SuspectSince.ShouldBe(first.SuspectSince);
        second.SuspectReason.ShouldBe("Title, Description");
        (await _testCases.GetHistoryAsync(testCase.Id)).Count(h => h.Type == TestCaseHistoryType.RequirementChanged).ShouldBe(2);
    }

    [Fact]
    public async Task The_History_Of_The_Test_Case_Says_Which_Requirement_Changed_And_In_What()
    {
        var testCase = await CaseAsync();
        var requirement = await RequirementAsync("REQ-5", testCase.Id);

        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => r.Description = "A different story"));

        var line = (await _testCases.GetHistoryAsync(testCase.Id)).Single(h => h.Type == TestCaseHistoryType.RequirementChanged);
        line.Note.ShouldBe("REQ-5: Description");
    }

    // ---- who is not marked

    [Fact]
    public async Task A_Test_Case_Linked_Later_Or_Linked_Again_Starts_Clean_And_One_That_Was_Unlinked_Is_Not_Marked()
    {
        var kept = await CaseAsync();
        var unlinked = await CaseAsync();
        var requirement = await RequirementAsync("REQ-6", kept.Id, unlinked.Id);
        await _requirements.UnlinkTestCaseAsync(requirement.Id, unlinked.Id);

        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => r.Title = "Changed"));
        var later = await CaseAsync();
        await _requirements.LinkTestCasesAsync(requirement.Id, new LinkTestCasesDto { TestCaseIds = { later.Id, unlinked.Id } });

        (await LinkOfAsync(kept.Id, "REQ-6")).IsSuspect.ShouldBeTrue();
        (await LinkOfAsync(later.Id, "REQ-6")).IsSuspect.ShouldBeFalse("a link made after the change is made knowing it");
        (await LinkOfAsync(unlinked.Id, "REQ-6")).IsSuspect.ShouldBeFalse("a link made again is a link made now");
        (await _requirements.GetAsync(requirement.Id)).SuspectTestCaseCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_Deprecated_Test_Case_Is_Not_Counted_And_A_Deleted_One_Is_Not_Counted_Either()
    {
        var live = await CaseAsync();
        var old = await CaseAsync();
        var gone = await CaseAsync();
        await _testCases.ChangeStatusAsync(old.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        await _testCases.ChangeStatusAsync(old.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Deprecated });
        var requirement = await RequirementAsync("REQ-7", live.Id, old.Id, gone.Id);
        await _testCases.DeleteAsync(gone.Id);

        var changed = await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => r.Title = "Changed"));

        changed.SuspectTestCaseCount.ShouldBe(1);
        var row = (await _rtm.GetMatrixAsync(new GetRtmInput { Filter = "REQ-7" })).Requirements.Single();
        row.SuspectTestCaseCount.ShouldBe(1);
        row.TestCases.Single(t => t.TestCaseId == old.Id).IsSuspect.ShouldBeTrue("the flag is shown, but a deprecated test case is not asked for");
    }

    // ---- confirming

    [Fact]
    public async Task Confirming_One_Link_Clears_Only_That_One_And_Leaves_A_Line_In_The_History()
    {
        var one = await CaseAsync();
        var two = await CaseAsync();
        var requirement = await RequirementAsync("REQ-8", one.Id, two.Id);
        await _requirements.UpdateAsync(requirement.Id, Edit(requirement, r => r.Title = "Changed"));

        var result = await _requirements.ReviewLinkAsync(requirement.Id, one.Id);

        result.Reviewed.ShouldBe(1);
        var cleared = await LinkOfAsync(one.Id, "REQ-8");
        (cleared.IsSuspect, cleared.SuspectSince, cleared.SuspectReason).ShouldBe((false, null, null));
        (await LinkOfAsync(two.Id, "REQ-8")).IsSuspect.ShouldBeTrue();
        (await _requirements.GetAsync(requirement.Id)).SuspectTestCaseCount.ShouldBe(1);
        (await _testCases.GetHistoryAsync(one.Id)).Single(h => h.Type == TestCaseHistoryType.RequirementReviewed).Note.ShouldBe("REQ-8");
    }

    [Fact]
    public async Task Confirming_A_Link_That_Is_Not_Suspect_Or_Does_Not_Exist_Does_Nothing_And_Leaves_No_Line()
    {
        var linked = await CaseAsync();
        var stranger = await CaseAsync();
        var requirement = await RequirementAsync("REQ-9", linked.Id);

        (await _requirements.ReviewLinkAsync(requirement.Id, linked.Id)).Reviewed.ShouldBe(0);
        (await _requirements.ReviewLinkAsync(requirement.Id, stranger.Id)).Reviewed.ShouldBe(0);
        (await _requirements.ReviewAllAsync(requirement.Id)).Reviewed.ShouldBe(0);

        (await _testCases.GetHistoryAsync(linked.Id)).ShouldNotContain(h => h.Type == TestCaseHistoryType.RequirementReviewed);
        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.ReviewLinkAsync(requirement.Id, Guid.NewGuid()));
        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.ReviewLinkAsync(Guid.NewGuid(), linked.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.ReviewAllAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Confirming_All_Clears_Every_Suspect_Link_Of_The_Requirement_And_No_Other_Requirement()
    {
        var one = await CaseAsync();
        var two = await CaseAsync();
        var first = await RequirementAsync("REQ-10", one.Id, two.Id);
        var other = await RequirementAsync("REQ-11", one.Id);
        await _requirements.UpdateAsync(first.Id, Edit(first, r => r.Title = "Changed"));
        await _requirements.UpdateAsync(other.Id, Edit(other, r => r.Title = "Changed too"));

        (await _requirements.ReviewAllAsync(first.Id)).Reviewed.ShouldBe(2);

        (await LinkOfAsync(one.Id, "REQ-10")).IsSuspect.ShouldBeFalse();
        (await LinkOfAsync(two.Id, "REQ-10")).IsSuspect.ShouldBeFalse();
        (await LinkOfAsync(one.Id, "REQ-11")).IsSuspect.ShouldBeTrue();
        (await _testCases.GetHistoryAsync(two.Id)).Count(h => h.Type == TestCaseHistoryType.RequirementReviewed).ShouldBe(1);
    }

    // ---- what is shown

    [Fact]
    public async Task The_Matrix_Counts_The_Requirements_To_Check_Filters_Them_And_Does_Not_Change_The_Coverage()
    {
        var one = await CaseAsync();
        var two = await CaseAsync();
        var changed = await RequirementAsync("REQ-12", one.Id);
        await RequirementAsync("REQ-13", two.Id);
        await RequirementAsync("REQ-14");
        var before = (await _rtm.GetMatrixAsync(new GetRtmInput())).Summary;
        await _requirements.UpdateAsync(changed.Id, Edit(changed, r => r.AcceptanceCriteria = "A new criterion"));

        var all = await _rtm.GetMatrixAsync(new GetRtmInput());
        var onlySuspect = await _rtm.GetMatrixAsync(new GetRtmInput { OnlySuspect = true });

        all.Summary.SuspectRequirements.ShouldBe(1);
        all.Summary.CoveredRequirements.ShouldBe(before.CoveredRequirements);
        all.Summary.UncoveredRequirements.ShouldBe(before.UncoveredRequirements);
        all.Requirements.Single(r => r.Code == "REQ-12").Status.ShouldBe(RequirementCoverageStatus.NotRun);
        onlySuspect.Requirements.Select(r => r.Code).ShouldBe(new[] { "REQ-12" });
        onlySuspect.TotalCount.ShouldBe(1);
        onlySuspect.Summary.TotalRequirements.ShouldBe(all.Summary.TotalRequirements, "the summary is not narrowed by the filter");
    }

    [Fact]
    public async Task What_A_Test_Case_Is_Linked_To_Is_Listed_By_Code_And_An_Unknown_Test_Case_Is_Not_Found()
    {
        var testCase = await CaseAsync();
        var b = await RequirementAsync("REQ-B", testCase.Id);
        await RequirementAsync("REQ-A", testCase.Id);
        await RequirementAsync("REQ-Z");
        await _requirements.UpdateAsync(b.Id, Edit(b, r => r.Title = "Changed"));

        var links = await _requirements.GetForTestCaseAsync(testCase.Id);

        links.Select(l => (l.Code, l.IsSuspect)).ShouldBe(new[] { ("REQ-A", false), ("REQ-B", true) });
        await Should.ThrowAsync<EntityNotFoundException>(() => _requirements.GetForTestCaseAsync(Guid.NewGuid()));
    }
}
