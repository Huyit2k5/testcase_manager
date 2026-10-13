using System.Net;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// Members of a project, with real users and real permissions: the QA lead manages projects and so sees every one; the tester is an
/// ordinary user, who sees a restricted project only after being made a member.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiProjectAccess_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiProjectAccess_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task A_Restricted_Project_Is_Seen_By_Its_Members_And_By_Whoever_Manages_Projects_And_By_Nobody_Else()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var key = "S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var project = await qaLead.PostAsync<ProjectDto>($"{Root}/projects", new CreateProjectDto { Key = key, Name = "Secret " + key });
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = "Secret suite " + key, ProjectId = project.Id });
        var testCase = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = key + "-001", Title = "Only for the members", Steps = { new TestStepDto { Action = "Do", ExpectedResult = "Done" } },
        });

        // Open to everyone to begin with: the tester sees it.
        (await tester.GetAsync<List<ProjectDto>>($"{Root}/projects")).Select(p => p.Id).ShouldContain(project.Id);
        (await tester.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).Code.ShouldBe(key + "-001");

        // Restricted, with nobody in it: the tester sees nothing of it; the QA lead still sees everything.
        var restricted = await qaLead.PutAsync<ProjectAccessDto>($"{Root}/projects/{project.Id}/access", new ProjectAccessDto { IsRestricted = true });
        restricted.IsRestricted.ShouldBeTrue();
        (await tester.GetAsync<List<ProjectDto>>($"{Root}/projects")).Select(p => p.Id).ShouldNotContain(project.Id);
        (await tester.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/test-cases/{testCase.Id}")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await tester.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?MaxResultCount=500")).Items.Select(t => t.Id).ShouldNotContain(testCase.Id);
        (await tester.GetAsync<List<TestSuiteTreeDto>>($"{Root}/suites/tree")).SelectMany(Flatten).Select(s => s.Id).ShouldNotContain(suite.Id);
        (await qaLead.GetAsync<List<ProjectDto>>($"{Root}/projects")).Select(p => p.Id).ShouldContain(project.Id);
        (await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).Id.ShouldBe(testCase.Id);

        // A member sees it again, and may work in it with the permissions the role gives.
        await qaLead.PutAsync<ProjectAccessDto>($"{Root}/projects/{project.Id}/access", new ProjectAccessDto { IsRestricted = true, UserIds = { tester.UserId } });
        (await tester.GetAsync<List<ProjectDto>>($"{Root}/projects")).Select(p => p.Id).ShouldContain(project.Id);
        (await tester.GetAsync<TestCaseDto>($"{Root}/test-cases/{testCase.Id}")).Id.ShouldBe(testCase.Id);

        // Open again: the list of members stays, and counts for nothing.
        await qaLead.PutAsync<ProjectAccessDto>($"{Root}/projects/{project.Id}/access", new ProjectAccessDto { IsRestricted = false, UserIds = { tester.UserId } });
        (await tester.GetAsync<List<ProjectDto>>($"{Root}/projects")).Select(p => p.Id).ShouldContain(project.Id);
    }

    [Fact]
    public async Task Only_Whoever_Manages_Projects_May_Read_Or_Change_Who_May_See_A_Project()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var key = "A" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var project = await qaLead.PostAsync<ProjectDto>($"{Root}/projects", new CreateProjectDto { Key = key, Name = "Access " + key });

        (await tester.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/projects/{project.Id}/access")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await tester.SendExpectingErrorAsync(HttpMethod.Put, $"{Root}/projects/{project.Id}/access", new ProjectAccessDto { IsRestricted = true }))
            .Status.ShouldBe(HttpStatusCode.Forbidden);
        (await qaLead.GetAsync<ProjectAccessDto>($"{Root}/projects/{project.Id}/access")).IsRestricted.ShouldBeFalse();
    }

    private static IEnumerable<TestSuiteTreeDto> Flatten(TestSuiteTreeDto node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
