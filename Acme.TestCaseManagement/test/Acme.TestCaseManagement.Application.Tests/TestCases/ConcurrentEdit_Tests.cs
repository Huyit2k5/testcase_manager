using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Data;
using Xunit;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>
/// Two people open the same test case and both save: the second save must not silently remove what the first one did. The form sends back the
/// stamp it read; a save from an older stamp is refused (HTTP 409), and a save that sends none (an import, a script) works as it always did.
/// </summary>
public class ConcurrentEdit_Tests : TestCaseManagementApplicationTestBase
{
    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;

    public ConcurrentEdit_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
    }

    private async Task<TestCaseDto> NewAsync()
    {
        var suite = await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Suite" });
        return await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = "CE-1", Title = "Original", Steps = { new TestStepDto { Action = "One", ExpectedResult = "Ok" } },
        });
    }

    private static CreateUpdateTestCaseDto Edit(TestCaseDto from, string title, params string[] steps) => new()
    {
        SuiteId = from.SuiteId, Code = from.Code, Title = title, ConcurrencyStamp = from.ConcurrencyStamp,
        Steps = steps.Select(s => new TestStepDto { Action = s, ExpectedResult = "Ok" }).ToList(),
    };

    [Fact]
    public async Task A_Test_Case_Carries_A_Stamp_That_Changes_With_Every_Save()
    {
        var created = await NewAsync();
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();

        var saved = await _testCases.UpdateAsync(created.Id, Edit(created, "Second", "One"));
        saved.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        saved.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);

        (await _testCases.GetAsync(created.Id)).ConcurrencyStamp.ShouldBe(saved.ConcurrencyStamp);
    }

    [Fact]
    public async Task The_Second_Of_Two_Saves_From_The_Same_Copy_Is_Refused_And_Does_Not_Remove_What_The_First_Did()
    {
        var created = await NewAsync();
        var copyOfA = await _testCases.GetAsync(created.Id);
        var copyOfB = await _testCases.GetAsync(created.Id);

        await _testCases.UpdateAsync(created.Id, Edit(copyOfA, "Original", "One", "Two, added by A"));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _testCases.UpdateAsync(created.Id, Edit(copyOfB, "Title changed by B", "One")));

        var now = await _testCases.GetAsync(created.Id);
        now.Title.ShouldBe("Original");
        now.Steps.Select(s => s.Action).ShouldBe(new[] { "One", "Two, added by A" });
    }

    [Fact]
    public async Task After_The_Refusal_A_Save_From_A_Fresh_Copy_Goes_Through()
    {
        var created = await NewAsync();
        var stale = await _testCases.GetAsync(created.Id);
        await _testCases.UpdateAsync(created.Id, Edit(created, "Changed", "One"));
        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _testCases.UpdateAsync(created.Id, Edit(stale, "Late", "One")));

        var fresh = await _testCases.GetAsync(created.Id);
        (await _testCases.UpdateAsync(created.Id, Edit(fresh, "Late, from a fresh copy", "One"))).Title.ShouldBe("Late, from a fresh copy");
    }

    [Fact]
    public async Task Another_Kind_Of_Change_Also_Makes_The_Copy_Stale_Because_The_Test_Case_Is_Not_What_It_Was()
    {
        var created = await NewAsync();
        var copy = await _testCases.GetAsync(created.Id);

        await _testCases.SetTagsAsync(created.Id, new SetTestCaseTagsDto { Tags = new List<string> { "smoke" } });

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => _testCases.UpdateAsync(created.Id, Edit(copy, "From the old copy", "One")));
    }

    [Fact]
    public async Task A_Save_That_Sends_No_Stamp_Works_As_It_Always_Did_And_The_Last_Save_Wins()
    {
        var created = await NewAsync();
        await _testCases.UpdateAsync(created.Id, Edit(created, "First", "One"));

        var withoutStamp = Edit(created, "Second, no stamp", "One");
        withoutStamp.ConcurrencyStamp = null;

        (await _testCases.UpdateAsync(created.Id, withoutStamp)).Title.ShouldBe("Second, no stamp");
    }
}
