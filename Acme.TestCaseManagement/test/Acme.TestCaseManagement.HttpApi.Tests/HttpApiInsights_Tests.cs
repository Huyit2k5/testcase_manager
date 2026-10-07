using System.Net;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>Flaky detection and the dashboard over HTTP: the contract of the answers and who may ask for what.</summary>
[Collection(HostCollection.Name)]
public class HttpApiInsights_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiInsights_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A test case that a pipeline has run once per letter (P or F), each time as a run of its own.</summary>
    private static async Task<TestCaseDto> HistoryAsync(ApiClient qaLead, ApiClient pipeline, string suffix, string pattern)
    {
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Insights {suffix}" });
        var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id,
            Code = $"INS-{suffix}",
            Title = "A jumpy test",
            AutomationId = $"ins.{suffix}",
            Steps = { new TestStepDto { Action = "Run the script", ExpectedResult = "It passes" } },
        });
        var approved = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        foreach (var letter in pattern)
        {
            await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", new PublishAutomationResultsInput
            {
                Run = new AutomationRunInput { Title = $"CI {suffix}", Environment = "CI" },
                Results =
                {
                    new AutomationResultInput { AutomationId = $"ins.{suffix}", Status = letter == 'P' ? TestResultStatus.Passed : TestResultStatus.Failed, DurationSeconds = 2 },
                },
            });
        }

        return approved;
    }

    [Fact]
    public async Task A_Jumpy_Test_Is_Listed_With_Its_Score_And_Can_Be_Flagged_Through_Apply()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var key = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = $"Insights {Unique()}" });
        var pipeline = ApiClient.WithApiKey(_host, key.Key);
        var suffix = Unique();
        var testCase = await HistoryAsync(qaLead, pipeline, suffix, "PFPFPFPF");

        var list = await qaLead.GetAsync<FlakyTestListDto>($"{Root}/flaky-tests?Filter=ins.{suffix}");

        var item = list.Items.ShouldHaveSingleItem();
        item.TestCaseId.ShouldBe(testCase.Id);
        item.Level.ShouldBe(FlakinessLevel.Flaky);
        item.Score.ShouldBe(1m);
        item.Flips.ShouldBe(7);
        item.IsFlagged.ShouldBeFalse();

        // The level is a number in the contract, like every enum of the module.
        var raw = await qaLead.GetAsync<System.Text.Json.Nodes.JsonNode>($"{Root}/flaky-tests?Filter=ins.{suffix}");
        ((int)raw["items"]![0]!["level"]!).ShouldBe((int)FlakinessLevel.Flaky);

        var applied = await qaLead.PostAsync<ApplyFlakyFlagsResultDto>($"{Root}/flaky-tests/apply", new ApplyFlakyFlagsInput());
        applied.FlaggedCodes.ShouldContain($"INS-{suffix}");
        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).IsFlaky.ShouldBeTrue();
        (await qaLead.GetAsync<FlakyTestListDto>($"{Root}/flaky-tests?Filter=ins.{suffix}")).Items.Single().IsFlagged.ShouldBeTrue();
    }

    [Fact]
    public async Task The_Dashboard_Answers_With_All_Four_Metrics_And_Validates_Its_Input()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        var dashboard = await qaLead.GetAsync<DashboardDto>($"{Root}/dashboard?Days=10");

        dashboard.Days.ShouldBe(10);
        dashboard.Velocity.Points.Count.ShouldBe(10);
        dashboard.BurnDown.Points.ShouldNotBeEmpty();
        dashboard.Progress.ShouldNotBeNull();
        dashboard.DefectDensity.ShouldNotBeNull();
        dashboard.Flaky.ShouldNotBeNull();

        foreach (var days in new[] { 0, 6, 91 })
        {
            (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/dashboard?Days={days}")).Status.ShouldBe(HttpStatusCode.BadRequest, $"Days={days}");
        }

        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/dashboard?TestPlanId={Guid.NewGuid()}")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/flaky-tests?MaxResultCount=201")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Who_May_Read_And_Who_May_Write_The_Flags()
    {
        // product.owner reads everything but may not update test cases; tester may.
        var productOwner = await ApiClient.LoginAsync(_host, "product.owner");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var anonymous = ApiClient.Anonymous(_host);

        (await productOwner.GetAsync<DashboardDto>($"{Root}/dashboard")).ShouldNotBeNull();
        (await productOwner.GetAsync<FlakyTestListDto>($"{Root}/flaky-tests")).ShouldNotBeNull();
        (await productOwner.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/flaky-tests/apply", new ApplyFlakyFlagsInput())).Status.ShouldBe(HttpStatusCode.Forbidden);

        (await tester.GetAsync<FlakyTestListDto>($"{Root}/flaky-tests")).ShouldNotBeNull();
        (await tester.PostAsync<ApplyFlakyFlagsResultDto>($"{Root}/flaky-tests/apply", new ApplyFlakyFlagsInput())).ShouldNotBeNull();

        foreach (var url in new[] { "/dashboard", "/flaky-tests" })
        {
            (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}{url}")).Status.ShouldBe(HttpStatusCode.Unauthorized, url);
        }
    }
}
