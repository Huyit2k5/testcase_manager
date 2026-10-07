using System.Net;
using System.Text.Json.Nodes;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// A pipeline over HTTP with the credential it will really have: an API key. What the key may do, what happens when it is
/// revoked, expired or wrong, and how a signed-in user compares.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiAutomation_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiAutomation_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<ApiKeyCreatedDto> CreateKeyAsync(ApiClient qaLead, string name, DateTime? expiresAt = null) =>
        await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = name, ExpiresAt = expiresAt });

    /// <summary>An approved test case with an Automation ID, as the pipeline's tests will be known in the library.</summary>
    private static async Task<TestCaseDto> ApprovedCaseAsync(ApiClient qaLead, string suffix, string automationId)
    {
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Automated {suffix}" });
        var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id,
            Code = $"AUTO-{suffix}",
            Title = "An automated test",
            AutomationId = automationId,
            Steps = { new TestStepDto { Action = "Run the script", ExpectedResult = "It passes" } },
        });

        return await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    private static PublishAutomationResultsInput NewRun(string title, params AutomationResultInput[] results) =>
        new() { Run = new AutomationRunInput { Title = title, Environment = "CI" }, Results = results.ToList() };

    private static AutomationResultInput Result(string automationId, TestResultStatus status = TestResultStatus.Passed) =>
        new() { AutomationId = automationId, Status = status, DurationSeconds = 4 };

    [Fact]
    public async Task A_Pipeline_Publishes_With_An_Api_Key_And_The_Attempts_Have_No_User()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"e2e.{id}");
        var key = await CreateKeyAsync(qaLead, $"GitHub Actions {id}");
        var pipeline = ApiClient.WithApiKey(_host, key.Key);

        var answer = await pipeline.PostAsync<PublishAutomationResultsDto>(
            $"{Root}/automation/results",
            NewRun($"Nightly {id}", new AutomationResultInput
            {
                AutomationId = $"E2E.{id}", Status = TestResultStatus.Failed, ActualResult = "Boom", DurationSeconds = 9,
                Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = $"BUG-{id}" } },
            }));

        answer.Accepted.ShouldBeTrue();
        answer.RunCreated.ShouldBeTrue();
        answer.Results.Single().Outcome.ShouldBe(AutomationOutcome.Recorded);

        var run = await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{answer.RunId}");
        run.Title.ShouldBe($"Nightly {id}");
        run.Items.Single().CurrentStatus.ShouldBe(TestResultStatus.Failed);

        // No user stands behind the attempt: it came from a key. Its defect is there like any other.
        var attempts = await qaLead.GetAsync<List<TestExecutionDto>>($"{Root}/runs/{run.Id}/items/{run.Items.Single().Id}/executions");
        attempts.Single().CreatorId.ShouldBeNull();
        attempts.Single().DefectLinks.Single().IssueKey.ShouldBe($"BUG-{id}");
    }

    [Fact]
    public async Task A_Key_Can_Publish_And_Nothing_Else()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        var testCase = await ApprovedCaseAsync(qaLead, id, $"scope.{id}");
        var pipeline = ApiClient.WithApiKey(_host, (await CreateKeyAsync(qaLead, $"Scope {id}")).Key);
        var published = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", NewRun($"Scope {id}", Result($"scope.{id}")));

        var forbidden = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, $"{Root}/test-cases", null),
            (HttpMethod.Get, $"{Root}/test-cases/{testCase.Id}", null),
            (HttpMethod.Get, $"{Root}/suites/tree", null),
            (HttpMethod.Get, $"{Root}/runs/{published.RunId}", null),
            (HttpMethod.Get, $"{Root}/runs", null),
            (HttpMethod.Get, $"{Root}/rtm", null),
            (HttpMethod.Get, $"{Root}/sign-off", null),
            (HttpMethod.Get, $"{Root}/test-cases/export", null),
            (HttpMethod.Post, $"{Root}/runs", new CreateTestRunDto { Title = "Mine", Environment = "CI" }),
            (HttpMethod.Post, $"{Root}/runs/{published.RunId}/complete", null),
            (HttpMethod.Post, $"{Root}/suites", new CreateSuiteBody { Name = "Mine" }),
            (HttpMethod.Get, $"{Root}/api-keys", null),
            (HttpMethod.Post, $"{Root}/api-keys", new CreateApiKeyDto { Name = "Escalate" }),
            (HttpMethod.Get, $"{Root}/dashboard", null),
            (HttpMethod.Get, $"{Root}/shared-step-groups", null),
            (HttpMethod.Get, $"{Root}/test-cases/tags", null),
            (HttpMethod.Get, $"{Root}/attachments?OwnerType=0&OwnerIds={Guid.NewGuid()}", null),
            (HttpMethod.Get, $"{Root}/flaky-tests", null),
            (HttpMethod.Post, $"{Root}/flaky-tests/apply", new ApplyFlakyFlagsInput()),
        };

        foreach (var (method, url, body) in forbidden)
        {
            (await pipeline.SendExpectingErrorAsync(method, url, body)).Status.ShouldBe(HttpStatusCode.Forbidden, $"{method} {url}");
        }

        // The application configuration of the key lists the one permission, which is what a client can read about it.
        var configuration = await pipeline.GetAsync<JsonNode>("/api/abp/application-configuration?includeLocalizationResources=false");
        configuration["auth"]!["grantedPolicies"]!.AsObject().Where(p => (bool)p.Value!).Select(p => p.Key)
            .ShouldBe(new[] { "TestCaseManagement.AutomationResults.Publish" });
    }

    private sealed class CreateSuiteBody
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task A_Revoked_A_Wrong_And_A_Malformed_Key_Are_All_A_401()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"revoke.{id}");
        var key = await CreateKeyAsync(qaLead, $"Revoke {id}");
        var input = NewRun($"Revoke {id}", Result($"revoke.{id}"));

        (await ApiClient.WithApiKey(_host, key.Key).PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input)).Accepted.ShouldBeTrue();

        var revoked = await qaLead.PostAsync<ApiKeyDto>($"{Root}/api-keys/{key.Id}/revoke");
        revoked.IsActive.ShouldBeFalse();

        foreach (var secret in new[]
                 {
                     key.Key, // revoked
                     key.Key[..12] + "_" + new string('x', 43), // right prefix, wrong secret
                     "tcm_00000000_" + new string('y', 43), // no such key
                     "not-a-key",
                     " ",
                     string.Empty,
                 })
        {
            var attempt = ApiClient.WithApiKey(_host, secret);
            (await attempt.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", input)).Status
                .ShouldBe(HttpStatusCode.Unauthorized, $"key '{secret}'");
        }
    }

    [Fact]
    public async Task A_Key_Stops_Working_When_It_Expires()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"expire.{id}");
        var key = await CreateKeyAsync(qaLead, $"Short lived {id}", DateTime.UtcNow.AddSeconds(3));
        var pipeline = ApiClient.WithApiKey(_host, key.Key);
        var input = NewRun($"Expire {id}", Result($"expire.{id}"));

        (await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input)).Accepted.ShouldBeTrue();

        await Task.Delay(TimeSpan.FromSeconds(3.5));

        (await pipeline.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", input)).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await qaLead.GetAsync<List<ApiKeyDto>>($"{Root}/api-keys")).Single(k => k.Id == key.Id).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task The_List_Shows_When_A_Key_Was_Used_And_Never_Its_Secret()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"used.{id}");
        var key = await CreateKeyAsync(qaLead, $"Used {id}");

        (await qaLead.GetAsync<List<ApiKeyDto>>($"{Root}/api-keys")).Single(k => k.Id == key.Id).LastUsedAt.ShouldBeNull();

        await ApiClient.WithApiKey(_host, key.Key).PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", NewRun($"Used {id}", Result($"used.{id}")));

        var raw = await qaLead.SendRawAsync(HttpMethod.Get, $"{Root}/api-keys");
        var text = await raw.Content.ReadAsStringAsync();
        text.ShouldNotContain(key.Key);
        text.ShouldNotContain(key.Key[13..]);
        var listed = JsonNode.Parse(text)!.AsArray().Single(k => (string)k!["id"]! == key.Id.ToString())!;
        listed["lastUsedAt"].ShouldNotBeNull();
        ((string)listed["keyPrefix"]!).ShouldBe(key.Key[..12]);
        listed.AsObject().ContainsKey("key").ShouldBeFalse();
    }

    [Fact]
    public async Task Only_A_Qa_Lead_Manages_Keys()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        foreach (var user in new[] { "tester", "product.owner" })
        {
            var client = await ApiClient.LoginAsync(_host, user);
            (await client.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/api-keys")).Status.ShouldBe(HttpStatusCode.Forbidden, user);
            (await client.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/api-keys", new CreateApiKeyDto { Name = "Nope" })).Status
                .ShouldBe(HttpStatusCode.Forbidden, user);
            (await client.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/api-keys/{Guid.NewGuid()}/revoke")).Status
                .ShouldBe(HttpStatusCode.Forbidden, user);
        }

        (await CreateKeyAsync(qaLead, "Allowed")).Key.ShouldStartWith("tcm_");
    }

    [Fact]
    public async Task A_Signed_In_User_Needs_The_Publish_Permission_Too()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"user.{id}");

        (await tester.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", NewRun($"Tester {id}", Result($"user.{id}"))))
            .Status.ShouldBe(HttpStatusCode.Forbidden);

        var answer = await qaLead.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", NewRun($"QA lead {id}", Result($"user.{id}")));
        answer.Recorded.ShouldBe(1);

        // A person's attempt does carry the user, unlike a key's.
        var run = await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{answer.RunId}");
        (await qaLead.GetAsync<List<TestExecutionDto>>($"{Root}/runs/{run.Id}/items/{run.Items.Single().Id}/executions")).Single().CreatorId.ShouldBe(qaLead.UserId);
    }

    [Fact]
    public async Task When_A_Request_Has_Both_A_Token_And_A_Key_The_Key_Decides()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"both.{id}");

        // A valid token does not rescue a bad key, and a key does not borrow the rights of the token.
        var withBadKey = (await ApiClient.LoginAsync(_host, "qa.lead")).WithHeader("X-Api-Key", "tcm_00000000_" + new string('z', 43));
        (await withBadKey.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", NewRun($"Both {id}", Result($"both.{id}"))))
            .Status.ShouldBe(HttpStatusCode.Unauthorized);

        var key = await CreateKeyAsync(qaLead, $"Both {id}");
        var withKeyAndToken = (await ApiClient.LoginAsync(_host, "qa.lead")).WithHeader("X-Api-Key", key.Key);
        (await withKeyAndToken.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/test-cases")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Retry_With_The_Same_Idempotency_Key_Is_Answered_Again_And_Recorded_Once()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"idem.{id}");
        var pipeline = ApiClient.WithApiKey(_host, (await CreateKeyAsync(qaLead, $"Idem {id}")).Key);
        var input = NewRun($"Idem {id}", Result($"idem.{id}"));
        input.IdempotencyKey = $"build-{id}";

        var first = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input);
        var retry = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input);

        (first.Replayed, retry.Replayed).ShouldBe((false, true));
        retry.RunId.ShouldBe(first.RunId);
        (await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{first.RunId}")).Items.Single().AttemptCount.ShouldBe(1);

        input.Results[0].Status = TestResultStatus.Failed;
        var reused = await pipeline.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", input);
        reused.Status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)reused.Error["code"]).ShouldBe(TestCaseManagementErrorCodes.IdempotencyKeyReused);
    }

    [Fact]
    public async Task Unmatched_Results_Are_Reported_In_The_Language_Of_The_Caller_And_Strict_Mode_Refuses_Them()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        await ApprovedCaseAsync(qaLead, id, $"lang.{id}");
        var key = (await CreateKeyAsync(qaLead, $"Lang {id}")).Key;
        var input = NewRun($"Lang {id}", Result($"lang.{id}"), Result($"missing.{id}"));

        var vietnamese = await ApiClient.WithApiKey(_host, key).PreferLanguage("vi")
            .PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input);
        vietnamese.Recorded.ShouldBe(1);
        vietnamese.Results[1].Message.ShouldBe($"Không có test case nào có automation id 'missing.{id}'.");

        input.Run!.Title = $"Strict {id}";
        input.FailOnUnmatched = true;
        var strict = await ApiClient.WithApiKey(_host, key).PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", input);
        strict.Accepted.ShouldBeFalse();
        strict.RunId.ShouldBeNull();
        strict.Results[1].Message.ShouldBe($"No test case has the automation id 'missing.{id}'.");
    }

    [Fact]
    public async Task A_Malformed_Request_Is_A_400_Or_A_Business_Error_Not_A_Server_Error()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var pipeline = ApiClient.WithApiKey(_host, (await CreateKeyAsync(qaLead, "Malformed")).Key);

        // No results at all.
        var empty = await pipeline.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", NewRun("Empty"));
        empty.Status.ShouldBe(HttpStatusCode.BadRequest);

        // Neither a run nor a new one.
        var neither = await pipeline.SendExpectingErrorAsync(
            HttpMethod.Post, $"{Root}/automation/results", new PublishAutomationResultsInput { Results = { Result("x") } });
        neither.Status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)neither.Error["code"]).ShouldBe(TestCaseManagementErrorCodes.InvalidAutomationRun);

        // Untested is not a result.
        var untested = await pipeline.SendExpectingErrorAsync(
            HttpMethod.Post, $"{Root}/automation/results", NewRun("Untested", Result("x", TestResultStatus.Untested)));
        ((string?)untested.Error["code"]).ShouldBe(TestCaseManagementErrorCodes.InvalidExecutionStatus);

        // A run that does not exist.
        var unknown = await pipeline.SendExpectingErrorAsync(
            HttpMethod.Post, $"{Root}/automation/results", new PublishAutomationResultsInput { RunId = Guid.NewGuid(), Results = { Result("x") } });
        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Shards_Share_The_Run_And_The_Last_One_Completes_It()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Shards {id}" });
        foreach (var shard in new[] { "a", "b" })
        {
            var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
            {
                SuiteId = suite.Id, Code = $"SH-{id}-{shard}", Title = shard, AutomationId = $"shard.{id}.{shard}",
                Steps = { new TestStepDto { Action = "Run", ExpectedResult = "Pass" } },
            });
            await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        }

        var pipeline = ApiClient.WithApiKey(_host, (await CreateKeyAsync(qaLead, $"Shards {id}")).Key);
        var first = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", NewRun($"Shards {id}", Result($"shard.{id}.a")));
        var lastShard = new PublishAutomationResultsInput { RunId = first.RunId, Results = { Result($"shard.{id}.b", TestResultStatus.Failed) }, CompleteRun = true };
        var last = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", lastShard);

        last.RunId.ShouldBe(first.RunId);
        last.RunStatus.ShouldBe(RunStatus.Completed);
        var run = await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{first.RunId}");
        run.Summary.TotalItems.ShouldBe(2);
        (run.Summary.Passed, run.Summary.Failed).ShouldBe((1, 1));

        (await pipeline.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/automation/results", lastShard)).Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
