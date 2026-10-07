using System.Net;
using System.Text;
using Acme.TestCaseManagement.Attachments.Dtos;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>Attachments over HTTP, against the real file storage of the sample host: the upload, the safe download and who may do what.</summary>
[Collection(HostCollection.Name)]
public class HttpApiAttachments_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiAttachments_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<TestCaseDto> CaseAsync(ApiClient qaLead, string suffix)
    {
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = $"Files {suffix}" });
        var created = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id,
            Code = $"FILE-{suffix}",
            Title = "A test with files",
            AutomationId = $"files.{suffix}",
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "It works" } },
        });

        return await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{created.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    /// <summary>The id of an attempt, made by a pipeline.</summary>
    private async Task<Guid> ExecutionAsync(ApiClient qaLead, string suffix)
    {
        var key = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = $"Files {suffix}" });
        var published = await ApiClient.WithApiKey(_host, key.Key).PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", new PublishAutomationResultsInput
        {
            Run = new AutomationRunInput { Title = $"CI {suffix}", Environment = "CI" },
            Results = { new AutomationResultInput { AutomationId = $"files.{suffix}", Status = TestResultStatus.Failed, DurationSeconds = 2 } },
        });
        var run = await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{published.RunId}");
        return (await qaLead.GetAsync<List<TestExecutionDto>>($"{Root}/runs/{run.Id}/items/{run.Items.Single().Id}/executions")).Single().Id;
    }

    private static Dictionary<string, string> Owner(AttachmentOwnerType type, Guid id, string? description = null)
    {
        var fields = new Dictionary<string, string> { ["OwnerType"] = ((int)type).ToString(), ["OwnerId"] = id.ToString() };
        if (description != null)
        {
            fields["Description"] = description;
        }

        return fields;
    }

    [Fact]
    public async Task A_File_Goes_Up_Is_Listed_And_Comes_Down_With_Safe_Headers_Then_Is_Deleted()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var testCase = await CaseAsync(qaLead, Unique());
        var content = Encoding.UTF8.GetBytes("2026-03-01 10:00:01 ERROR checkout failed: card declined");

        var uploaded = await qaLead.UploadAsync<AttachmentDto>($"{Root}/attachments", "..\\console.LOG", content, Owner(AttachmentOwnerType.TestCase, testCase.Id, "Console output"));

        uploaded.FileName.ShouldBe("console.LOG");
        uploaded.ContentType.ShouldBe("text/plain");
        uploaded.Size.ShouldBe(content.Length);
        uploaded.Description.ShouldBe("Console output");
        uploaded.CreatorId.ShouldBe(qaLead.UserId);

        var list = await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}");
        list.Single().Id.ShouldBe(uploaded.Id);

        using var response = await qaLead.SendRawAsync(HttpMethod.Get, $"{Root}/attachments/{uploaded.Id}/content");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(content);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        (response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName)!.Trim('"').ShouldBe("console.LOG");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(new[] { "nosniff" });
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("sandbox");

        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/attachments/{uploaded.Id}");

        (await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).ShouldBeEmpty();
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments/{uploaded.Id}/content")).Status.ShouldBe(HttpStatusCode.NotFound);
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/attachments/{uploaded.Id}")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Evidence_Of_An_Attempt_Is_Attached_To_The_Attempt_And_Not_To_The_Test_Case()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var suffix = Unique();
        var testCase = await CaseAsync(qaLead, suffix);
        var executionId = await ExecutionAsync(qaLead, suffix);

        var shot = await qaLead.UploadAsync<AttachmentDto>($"{Root}/attachments", "failure.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }, Owner(AttachmentOwnerType.TestExecution, executionId));

        shot.OwnerType.ShouldBe(AttachmentOwnerType.TestExecution);
        (await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=1&OwnerIds={executionId}")).Single().Id.ShouldBe(shot.Id);
        (await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).ShouldBeEmpty();

        // The enum is a number in the contract, like every enum of the module.
        var raw = await qaLead.GetAsync<System.Text.Json.Nodes.JsonNode>($"{Root}/attachments?OwnerType=1&OwnerIds={executionId}");
        ((int)raw[0]!["ownerType"]!).ShouldBe(1);
    }

    [Fact]
    public async Task What_Is_Refused_Is_Refused_With_A_Message_In_The_Language_Asked_For()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var testCase = await CaseAsync(qaLead, Unique());
        var owner = Owner(AttachmentOwnerType.TestCase, testCase.Id);

        var (_, english) = await Refused(qaLead, "setup.exe", new byte[] { 1 }, owner);
        english["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.AttachmentTypeNotAllowed);
        english["message"]!.GetValue<string>().ShouldStartWith("Files of type '.exe' are not accepted. Accepted: ");

        var (_, vietnamese) = await Refused((await ApiClient.LoginAsync(_host, "qa.lead")).PreferLanguage("vi"), "setup.exe", new byte[] { 1 }, owner);
        vietnamese["message"]!.GetValue<string>().ShouldStartWith("Không nhận tệp loại '.exe'.");

        var (_, empty) = await Refused(qaLead, "empty.png", Array.Empty<byte>(), owner);
        empty["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.AttachmentEmpty);

        var (_, tooLarge) = await Refused(qaLead, "video.mp4", new byte[26 * 1024 * 1024], owner);
        tooLarge["message"]!.GetValue<string>().ShouldBe("The file is 26 MB, and the limit is 25 MB.");

        var (_, orphan) = await Refused(qaLead, "a.txt", new byte[] { 1 }, Owner(AttachmentOwnerType.TestExecution, Guid.NewGuid()));
        orphan["code"]!.GetValue<string>().ShouldBe(TestCaseManagementErrorCodes.AttachmentOwnerNotFound);

        (await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).ShouldBeEmpty();
    }

    private static async Task<(HttpStatusCode Status, System.Text.Json.Nodes.JsonNode Error)> Refused(
        ApiClient client, string fileName, byte[] content, Dictionary<string, string> fields)
    {
        using var response = await client.UploadRawAsync($"{Root}/attachments", fileName, content, fields);
        response.IsSuccessStatusCode.ShouldBeFalse();
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (response.StatusCode, body["error"]!);
    }

    [Fact]
    public async Task The_Query_And_The_Upload_Are_Validated()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");

        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments?OwnerType=0")).Status.ShouldBe(HttpStatusCode.BadRequest);
        var tooMany = string.Join("&", Enumerable.Range(0, 101).Select(_ => $"OwnerIds={Guid.NewGuid()}"));
        (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments?OwnerType=0&{tooMany}")).Status.ShouldBe(HttpStatusCode.BadRequest);

        using var noFile = await qaLead.SendRawAsync(HttpMethod.Post, $"{Root}/attachments", null);
        noFile.IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Who_May_Read_Who_May_Write_And_Who_May_Not_Even_Ask()
    {
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead");
        var productOwner = await ApiClient.LoginAsync(_host, "product.owner");
        var tester = await ApiClient.LoginAsync(_host, "tester");
        var suffix = Unique();
        var testCase = await CaseAsync(qaLead, suffix);
        var executionId = await ExecutionAsync(qaLead, suffix);
        var onCase = await qaLead.UploadAsync<AttachmentDto>($"{Root}/attachments", "spec.pdf", new byte[] { 1, 2, 3 }, Owner(AttachmentOwnerType.TestCase, testCase.Id));
        var onAttempt = await qaLead.UploadAsync<AttachmentDto>($"{Root}/attachments", "failure.png", new byte[] { 1, 2, 3 }, Owner(AttachmentOwnerType.TestExecution, executionId));

        // The product owner reads everything and writes nothing.
        (await productOwner.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).Count.ShouldBe(1);
        (await productOwner.DownloadAsync($"{Root}/attachments/{onAttempt.Id}/content")).Bytes.ShouldBe(new byte[] { 1, 2, 3 });
        using (var upload = await productOwner.UploadRawAsync($"{Root}/attachments", "a.txt", new byte[] { 1 }, Owner(AttachmentOwnerType.TestCase, testCase.Id)))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await productOwner.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/attachments/{onCase.Id}")).Status.ShouldBe(HttpStatusCode.Forbidden);

        // The tester writes test cases and executes runs, so both kinds of owner are open to them.
        (await tester.UploadAsync<AttachmentDto>($"{Root}/attachments", "mine.txt", new byte[] { 1 }, Owner(AttachmentOwnerType.TestExecution, executionId))).ShouldNotBeNull();
        await tester.SendAsync(HttpMethod.Delete, $"{Root}/attachments/{onAttempt.Id}");

        // Not signed in: nothing. A pipeline's key: nothing either, it can only publish results.
        var anonymous = ApiClient.Anonymous(_host);
        (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments/{onCase.Id}/content")).Status.ShouldBe(HttpStatusCode.Unauthorized);

        var key = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = $"Files {suffix}" });
        var pipeline = ApiClient.WithApiKey(_host, key.Key);
        (await pipeline.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments/{onCase.Id}/content")).Status.ShouldBe(HttpStatusCode.Forbidden);
        (await pipeline.SendExpectingErrorAsync(HttpMethod.Get, $"{Root}/attachments?OwnerType=0&OwnerIds={testCase.Id}")).Status.ShouldBe(HttpStatusCode.Forbidden);
        using var byKey = await pipeline.UploadRawAsync($"{Root}/attachments", "a.txt", new byte[] { 1 }, Owner(AttachmentOwnerType.TestCase, testCase.Id));
        byKey.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
