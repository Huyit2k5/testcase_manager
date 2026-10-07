using System.Net;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.SharedSteps.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>Reusable steps over HTTP: the contract of the answers, the messages in both languages, and who may read, use and change the library.</summary>
[Collection(HostCollection.Name)]
public class HttpApiSharedSteps_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiSharedSteps_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static CreateUpdateSharedStepGroupDto GroupInput(string name, params string[] actions) => new()
    {
        Name = name,
        Steps = actions.Select(a => new SharedStepDto { Action = a, ExpectedResult = "Ok" }).ToList(),
    };

    private static async Task<TestCaseDto> CaseAsync(ApiClient qaLead, string suffix)
    {
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Shared {suffix}" });
        return await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id,
            Code = $"SH-{suffix}",
            Title = "Uses shared steps",
            Steps = { new TestStepDto { Action = "Search", ExpectedResult = "Found" } },
        });
    }

    [Fact]
    public async Task A_Group_Goes_Into_A_Test_Case_Falls_Behind_Is_Updated_And_Is_Detached_Over_HTTP()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var suffix = Unique();
        var group = await qaLead.PostAsync<SharedStepGroupDto>($"{Root}/shared-step-groups", GroupInput($"Log in {suffix}", "Open the login page", "Sign in"));
        var testCase = await CaseAsync(qaLead, suffix);

        var inserted = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/shared-steps", new InsertSharedStepsDto { SharedStepGroupId = group.Id, Position = 1 });
        inserted.Steps.OrderBy(s => s.StepOrder).Select(s => s.Action).ShouldBe(new[] { "Open the login page", "Sign in", "Search" });
        inserted.Steps.Count(s => s.SharedStepGroupName == $"Log in {suffix}").ShouldBe(2);

        var edit = GroupInput($"Log in {suffix}", "unused");
        edit.Steps = group.Steps.Select(s => new SharedStepDto { Id = s.Id, Action = s.StepOrder == 2 ? "Sign in with a code" : s.Action, ExpectedResult = s.ExpectedResult }).ToList();
        (await qaLead.PutAsync<SharedStepGroupDto>($"{Root}/shared-step-groups/{group.Id}", edit)).Revision.ShouldBe(2);

        var usage = await qaLead.GetAsync<List<SharedStepUsageDto>>($"{Root}/shared-step-groups/{group.Id}/usage");
        usage.Single().IsOutdated.ShouldBeTrue();
        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => s.SharedStepOutdated);

        var result = await qaLead.PostAsync<UpdateSharedStepUsersResultDto>($"{Root}/shared-step-groups/{group.Id}/update-test-cases", new UpdateSharedStepUsersInput());
        result.Codes.ShouldBe(new[] { $"SH-{suffix}" });
        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).Steps.OrderBy(s => s.StepOrder).Select(s => s.Action)
            .ShouldBe(new[] { "Open the login page", "Sign in with a code", "Search" });

        var refreshed = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/shared-steps/{group.Id}/refresh", new RefreshSharedStepsDto());
        refreshed.Steps.Where(s => s.SharedStepGroupId == group.Id).ShouldAllBe(s => !s.SharedStepOutdated);

        // The contract: the link fields are in the answer, as numbers and a boolean.
        var raw = await qaLead.GetAsync<System.Text.Json.Nodes.JsonNode>($"{Root}/test-cases/{testCase.Id}");
        var first = raw["steps"]!.AsArray().First(s => s!["stepOrder"]!.GetValue<int>() == 1)!;
        first["sharedStepRevision"]!.GetValue<int>().ShouldBe(2);
        first["sharedStepOutdated"]!.GetValue<bool>().ShouldBeFalse();

        var detached = await qaLead.SendRawAsync(HttpMethod.Delete, $"{Root}/test-cases/{testCase.Id}/shared-steps/{group.Id}");
        detached.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await qaLead.GetAsync<List<SharedStepUsageDto>>($"{Root}/shared-step-groups/{group.Id}/usage")).ShouldBeEmpty();
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/shared-step-groups/{group.Id}");
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/shared-step-groups/{group.Id}")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task What_Is_Refused_Is_Refused_With_A_Message_In_The_Language_Asked_For()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var suffix = Unique();
        var group = await qaLead.PostAsync<SharedStepGroupDto>($"{Root}/shared-step-groups", GroupInput($"Pay {suffix}", "Pay by card"));
        var testCase = await CaseAsync(qaLead, suffix);
        await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/shared-steps", new InsertSharedStepsDto { SharedStepGroupId = group.Id });

        var (_, duplicate) = await qaLead.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/shared-step-groups", GroupInput($"PAY {suffix}", "x"));
        duplicate["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.DuplicateSharedStepGroupName);
        duplicate["message"]!.GetValue<string>().ShouldBe($"A group of shared steps named 'PAY {suffix}' already exists.");

        var vietnamese = (await ApiClient.LoginAsync(_host, "qa.lead")).PreferLanguage("vi");
        var (_, inUse) = await vietnamese.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/shared-step-groups/{group.Id}");
        inUse["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.SharedStepGroupInUse);
        inUse["message"]!.GetValue<string>().ShouldBe($"Nhóm 'Pay {suffix}' đang được 1 test case dùng nên không thể xóa. Hãy tách nhóm khỏi các test case đó trước.");

        (await qaLead.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/shared-step-groups", GroupInput($"Empty {suffix}"))).Status.ShouldBe(HttpStatusCode.BadRequest);
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/test-cases/{testCase.Id}/shared-steps", new InsertSharedStepsDto { SharedStepGroupId = Guid.NewGuid() })).Status.ShouldBe(HttpStatusCode.NotFound);
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/test-cases/{testCase.Id}/shared-steps/{Guid.NewGuid()}")).Status.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Who_May_Read_Use_And_Change_The_Library()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var productOwner = await ApiClient.LoginAsync(_host, "product.owner");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var suffix = Unique();
        var group = await qaLead.PostAsync<SharedStepGroupDto>($"{Root}/shared-step-groups", GroupInput($"Shared {suffix}", "Step one"));
        var testCase = await CaseAsync(qaLead, suffix);

        // Everyone who is given the permission reads the library.
        (await productOwner.GetAsync<List<SharedStepGroupSummaryDto>>($"{Root}/shared-step-groups")).ShouldContain(g => g.Id == group.Id);
        (await tester.GetAsync<SharedStepGroupDto>($"{Root}/shared-step-groups/{group.Id}")).Name.ShouldBe($"Shared {suffix}");

        // Only the QA lead changes it: a change can put many test cases behind.
        foreach (var user in new[] { productOwner, tester })
        {
            (await user.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/shared-step-groups", GroupInput($"Mine {suffix}", "x"))).Status.ShouldBe(HttpStatusCode.Forbidden);
            (await user.SendExpectingErrorAsync(HttpMethod.Put, $"{Root}/shared-step-groups/{group.Id}", GroupInput("Renamed", "x"))).Status.ShouldBe(HttpStatusCode.Forbidden);
            (await user.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/shared-step-groups/{group.Id}")).Status.ShouldBe(HttpStatusCode.Forbidden);
            (await user.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/shared-step-groups/{group.Id}/update-test-cases", new UpdateSharedStepUsersInput())).Status.ShouldBe(HttpStatusCode.Forbidden);
        }

        // A tester writes test cases, so may put a group into one; a product owner may not.
        (await tester.PostAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}/shared-steps", new InsertSharedStepsDto { SharedStepGroupId = group.Id })).Steps.Count.ShouldBe(2);
        (await productOwner.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/test-cases/{testCase.Id}/shared-steps", new InsertSharedStepsDto { SharedStepGroupId = group.Id })).Status.ShouldBe(HttpStatusCode.Forbidden);

        (await ApiClient.Anonymous(_host).SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/shared-step-groups")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        var key = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = $"Shared {suffix}" });
        (await ApiClient.WithApiKey(_host, key.Key).SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/shared-step-groups")).Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
