using System.Net;
using System.Net.Http.Json;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.SignOff.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// The sample host signs users in with ABP Identity and checks the permissions that roles were granted. These tests
/// use the seeded accounts: 401 without a valid token, 403 without the permission, success once the role has it.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiSecurity_Tests
{
    private const string Root = "/api/test-case-management";

    private static readonly string[] ReadEndpoints =
    {
        "suites/tree", "test-cases", "plans", "runs", "requirements", "rtm", "quality-gates", "sign-off",
    };

    private readonly TestCaseManagementHost _host;

    public HttpApiSecurity_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Without_A_Token_Every_Request_Is_A_401()
    {
        var anonymous = ApiClient.Anonymous(_host);

        foreach (var endpoint in ReadEndpoints)
        {
            (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/{endpoint}")).Status.ShouldBe(HttpStatusCode.Unauthorized, endpoint);
        }

        (await anonymous.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/sign-off", new StartSignOffDto { TestPlanId = Guid.NewGuid() }))
            .Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Token_The_Host_Did_Not_Issue_Is_A_401()
    {
        // Signed with another key, so it must be refused even though the claims look right.
        var forged = ApiClient.WithToken(_host, "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");

        (await forged.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/suites/tree")).Status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Refuses_A_Wrong_Password_And_An_Unknown_User_In_The_Same_Way()
    {
        var anonymous = ApiClient.Anonymous(_host);

        var wrongPassword = await anonymous.SendExpectingErrorAsync(HttpMethod.Post, "/api/auth/login", new { userName = "tester", password = "not-the-password" });
        var unknownUser = await anonymous.SendExpectingErrorAsync(HttpMethod.Post, "/api/auth/login", new { userName = "nobody", password = "whatever" });

        wrongPassword.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unknownUser.Status.ShouldBe(HttpStatusCode.Unauthorized);
        ((string?)wrongPassword.Error["message"]).ShouldBe((string?)unknownUser.Error["message"]);
    }

    [Fact]
    public async Task The_Roles_Of_The_Seeded_Users_Are_In_The_Login_Result()
    {
        using var http = _host.CreateClient();

        using var response = await http.PostAsJsonAsync("/api/auth/login", new { userName = "product.owner", password = ApiClient.DemoPassword });

        response.IsSuccessStatusCode.ShouldBeTrue();
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        body["roles"]!.AsArray().Select(role => (string)role!).ShouldBe(new[] { "Product Owner" });
    }

    [Fact]
    public async Task A_Tester_Reads_And_Writes_Test_Cases_But_Cannot_Manage_Suites_Or_Sign_Off()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");

        foreach (var endpoint in ReadEndpoints)
        {
            await tester.SendAsync(HttpMethod.Get, $"{Root}/{endpoint}");
        }

        (await tester.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/suites", new CreateTestSuiteDto { Name = "Denied" })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await tester.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/plans", new CreateTestPlanDto { Name = "Denied" })).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await tester.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/sign-off", new StartSignOffDto { TestPlanId = Guid.NewGuid() })).Status.ShouldBe(HttpStatusCode.Forbidden);

        // The suite is created by someone who may; the tester can then write test cases into it.
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = "Tester area" });
        var testCase = await tester.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = $"TST-{Guid.NewGuid():N}"[..12], Title = "Written by a tester",
            Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });
        testCase.Title.ShouldBe("Written by a tester");

        // Approving needs its own permission, which testers lack.
        (await tester.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/test-cases/{testCase.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = Enums.TestCaseStatus.Approved }))
            .Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Product_Owner_Can_Sign_Off_But_Cannot_Author_Test_Cases()
    {
        var owner = await ApiClient.LoginAsync(_host, "product.owner");

        await owner.SendAsync(HttpMethod.Get, $"{Root}/test-cases");
        (await owner.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/test-cases", new CreateUpdateTestCaseDto { SuiteId = Guid.NewGuid(), Code = "X-1", Title = "Denied" }))
            .Status.ShouldBe(HttpStatusCode.Forbidden);

        // Past the permission check the request reaches the business rules (the plan does not exist).
        var reached = await owner.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/sign-off", new StartSignOffDto { TestPlanId = Guid.NewGuid() });
        ((string?)reached.Error["code"]).ShouldNotBeNullOrWhiteSpace("a business error, not a refused permission");
    }

    [Fact]
    public async Task The_Application_Configuration_Lists_Exactly_What_The_User_May_Do()
    {
        var tester = await ApiClient.LoginAsync(_host, "tester");

        var configuration = await tester.GetAsync<System.Text.Json.Nodes.JsonNode>("/api/abp/application-configuration?includeLocalizationResources=false");

        var granted = configuration["auth"]!["grantedPolicies"]!.AsObject().Select(policy => policy.Key).ToList();
        granted.ShouldContain("TestCaseManagement.TestCases.Create");
        granted.ShouldContain("TestCaseManagement.TestRuns.Execute");
        granted.ShouldNotContain("TestCaseManagement.TestSuites.Manage");
        granted.ShouldNotContain("TestCaseManagement.SignOff.Approve");
        ((string?)configuration["currentUser"]!["userName"]).ShouldBe("tester");
    }
}
