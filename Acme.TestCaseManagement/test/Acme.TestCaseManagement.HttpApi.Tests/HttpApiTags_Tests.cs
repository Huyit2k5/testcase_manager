using System.Net;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>Tags and the filters of the list over HTTP: the query string, the contract of the answers and who may label.</summary>
[Collection(HostCollection.Name)]
public class HttpApiTags_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiTags_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<TestCaseDto> CaseAsync(ApiClient qaLead, Guid suiteId, string code, string[] tags, string? automationId = null) =>
        await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suiteId,
            Code = code,
            Title = $"Title of {code}",
            AutomationId = automationId,
            Tags = tags.ToList(),
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "It works" } },
        });

    [Fact]
    public async Task The_List_Is_Filtered_By_Tags_And_By_Automation_Over_The_Query_String()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Tagged {id}" });
        await CaseAsync(qaLead, suite.Id, $"T1-{id}", new[] { $"smoke-{id}", $"api-{id}" }, $"auto.{id}.1");
        await CaseAsync(qaLead, suite.Id, $"T2-{id}", new[] { $"smoke-{id}" });
        await CaseAsync(qaLead, suite.Id, $"T3-{id}", new[] { $"api-{id}" }, $"auto.{id}.3");

        async Task<string[]> CodesAsync(string query)
        {
            var page = await qaLead.GetAsync<PagedResult>($"{Root}/test-cases?SuiteId={suite.Id}&{query}");
            return page.Items.Select(i => i.Code).Order().ToArray();
        }

        (await CodesAsync($"Tags=SMOKE-{id}")).ShouldBe(new[] { $"T1-{id}", $"T2-{id}" });
        (await CodesAsync($"Tags=smoke-{id}&Tags=api-{id}")).ShouldBe(new[] { $"T1-{id}" });
        (await CodesAsync("HasAutomationId=true")).ShouldBe(new[] { $"T1-{id}", $"T3-{id}" });
        (await CodesAsync("HasAutomationId=false")).ShouldBe(new[] { $"T2-{id}" });
        (await CodesAsync($"Tags=api-{id}&HasAutomationId=false")).ShouldBeEmpty();

        // The tags are an array of strings in the contract, and are in the list.
        var raw = await qaLead.GetAsync<System.Text.Json.Nodes.JsonNode>($"{Root}/test-cases?SuiteId={suite.Id}&Tags=smoke-{id}&Tags=api-{id}");
        raw["items"]![0]!["tags"]!.AsArray().Select(t => (string)t!).ShouldBe(new[] { $"api-{id}", $"smoke-{id}" });

        var tags = await qaLead.GetAsync<List<TagSummaryDto>>($"{Root}/test-cases/tags");
        tags.Single(t => t.Name == $"smoke-{id}").Count.ShouldBe(2);
        tags.Single(t => t.Name == $"api-{id}").Count.ShouldBe(2);
    }

    private sealed class PagedResult
    {
        public List<TestCaseDto> Items { get; set; } = new();
    }

    [Fact]
    public async Task Tags_Are_Set_Without_A_New_Version_And_Refused_In_The_Language_Asked_For()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var id = Unique();
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Labels {id}" });
        var created = await CaseAsync(qaLead, suite.Id, $"L-{id}", Array.Empty<string>());
        var approved = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });

        var tagged = await qaLead.PutAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/tags", new SetTestCaseTagsDto { Tags = { "regression", "Smoke" } });

        tagged.Tags.ShouldBe(new[] { "regression", "Smoke" });
        tagged.CurrentVersion.ShouldBe(approved.CurrentVersion);

        var (status, error) = await qaLead.SendExpectingErrorAsync(HttpMethod.Put, $"{Root}/test-cases/{created.Id}/tags", new SetTestCaseTagsDto { Tags = { "a;b" } });
        status.ShouldNotBe(HttpStatusCode.OK);
        error["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.InvalidTag);
        error["message"]!.GetValue<string>().ShouldStartWith("The tag 'a;b' cannot be used.");

        var vietnamese = (await ApiClient.LoginAsync(_host, "qa.lead")).PreferLanguage("vi");
        var (_, viError) = await vietnamese.SendExpectingErrorAsync(HttpMethod.Put, $"{Root}/test-cases/{created.Id}/tags", new SetTestCaseTagsDto { Tags = { "a;b" } });
        viError["message"]!.GetValue<string>().ShouldStartWith("Không dùng được tag 'a;b'.");

        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}")).Tags.ShouldBe(new[] { "regression", "Smoke" });
    }

    [Fact]
    public async Task Who_May_Label_A_Test_Case()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var productOwner = await ApiClient.LoginAsync(_host, "product.owner");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var id = Unique();
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Who {id}" });
        var created = await CaseAsync(qaLead, suite.Id, $"W-{id}", new[] { "x" });
        var url = $"{Root}/test-cases/{created.Id}/tags";

        (await productOwner.SendExpectingErrorAsync(HttpMethod.Put, url, new SetTestCaseTagsDto { Tags = { "nope" } })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await productOwner.GetAsync<List<TagSummaryDto>>($"{Root}/test-cases/tags")).ShouldNotBeNull();
        (await tester.PutAsync<TestCaseDto>(url, new SetTestCaseTagsDto { Tags = { "by-tester" } })).Tags.ShouldBe(new[] { "by-tester" });

        (await ApiClient.Anonymous(_host).SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/test-cases/tags")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var key = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = $"Tags {id}" });
        (await ApiClient.WithApiKey(_host, key.Key).SendExpectingErrorAsync(HttpMethod.Put, url, new SetTestCaseTagsDto { Tags = { "pipeline" } })).Status.ShouldBe(HttpStatusCode.Forbidden);

        (await qaLead.SendExpectingErrorAsync(HttpMethod.Put, url, new { tags = (string[]?)null })).Status.ShouldBe(HttpStatusCode.BadRequest);
    }
}
