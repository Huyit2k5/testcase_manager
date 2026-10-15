using System.Net;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>Two people saving the same test case, over HTTP: the second one is told (409) instead of removing the first one's change.</summary>
[Collection(HostCollection.Name)]
public class HttpApiConcurrency_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiConcurrency_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static CreateUpdateTestCaseDto Edit(TestCaseDto from, string title, string? stamp, params string[] steps) => new()
    {
        SuiteId = from.SuiteId, Code = from.Code, Title = title, ConcurrencyStamp = stamp,
        Steps = steps.Select(s => new TestStepDto { Action = s, ExpectedResult = "Ok" }).ToList(),
    };

    [Fact]
    public async Task A_Save_From_A_Copy_That_Is_No_Longer_Current_Answers_409_And_Changes_Nothing()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Concurrency {suffix}" });
        var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", Edit(new TestCaseDto { SuiteId = suite.Id, Code = $"CON-{suffix}" }, "Original", null, "One"));
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();

        var first = await qaLead.PutAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}", Edit(created, "Original", created.ConcurrencyStamp, "One", "Two, added by the first"));
        first.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);

        var (status, _) = await qaLead.SendExpectingErrorAsync(HttpMethod.Put, $"{Root}/test-cases/{created.Id}", Edit(created, "Title of the second", created.ConcurrencyStamp, "One"));

        status.ShouldBe(HttpStatusCode.Conflict);
        var now = await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}");
        now.Title.ShouldBe("Original");
        now.Steps.Select(s => s.Action).ShouldBe(new[] { "One", "Two, added by the first" });
    }

    [Fact]
    public async Task A_Save_That_Sends_No_Stamp_Still_Works()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Concurrency {suffix}" });
        var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", Edit(new TestCaseDto { SuiteId = suite.Id, Code = $"CNS-{suffix}" }, "Original", null, "One"));
        await qaLead.PutAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}", Edit(created, "First", created.ConcurrencyStamp, "One"));

        var saved = await qaLead.PutAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}", Edit(created, "Second, no stamp", null, "One"));

        saved.Title.ShouldBe("Second, no stamp");
    }
}
