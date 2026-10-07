using System.Security.Cryptography;
using System.Text;
using Acme.TestCaseManagement.Attachments.Dtos;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace Acme.TestCaseManagement.Attachments;

public class AttachmentAppService_Tests : TestCaseManagementApplicationTestBase
{
    static AttachmentAppService_Tests()
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly IAutomationResultsAppService _publisher;
    private readonly ITestRunAppService _runs;
    private readonly IAttachmentAppService _attachments;
    private Guid? _suiteId;

    public AttachmentAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _publisher = GetRequiredService<IAutomationResultsAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _attachments = GetRequiredService<IAttachmentAppService>();
    }

    private async Task<TestCaseDto> CaseAsync(string code = "TC-1", string? automationId = null)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Attachments" })).Id;
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            AutomationId = automationId,
            Steps = { new TestStepDto { Action = "Do it", ExpectedResult = "It works" } },
        });

        return await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
    }

    /// <summary>An attempt made by a pipeline, which is the quickest way to have one.</summary>
    private async Task<(Guid RunId, Guid ExecutionId)> AttemptAsync(string automationId)
    {
        await CaseAsync("TC-RUN", automationId);
        var answer = await _publisher.PublishAsync(new PublishAutomationResultsInput
        {
            Run = new AutomationRunInput { Title = "CI", Environment = "Staging" },
            Results = { new AutomationResultInput { AutomationId = automationId, Status = TestResultStatus.Failed, DurationSeconds = 4 } },
        });
        var run = await _runs.GetAsync(answer.RunId!.Value);
        var attempt = (await _runs.GetExecutionsAsync(run.Id, run.Items.Single().Id)).Single();
        return (run.Id, attempt.Id);
    }

    private static UploadAttachmentInput Upload(AttachmentOwnerType owner, Guid ownerId, string fileName, byte[] content, string? description = null) => new()
    {
        OwnerType = owner,
        OwnerId = ownerId,
        File = new RemoteStreamContent(new MemoryStream(content), fileName, "application/x-whatever-the-client-says", content.Length),
        Description = description,
    };

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task<byte[]> ReadAsync(IRemoteStreamContent content)
    {
        using var copy = new MemoryStream();
        await using var stream = content.GetStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private async Task<BusinessException> RefusedAsync(UploadAttachmentInput input, string code)
    {
        var before = InMemoryBlobProvider.Count;
        var exception = await Should.ThrowAsync<BusinessException>(() => _attachments.UploadAsync(input));
        exception.Code.ShouldBe(code);
        InMemoryBlobProvider.Count.ShouldBe(before, "a refused file must leave nothing in the storage");
        return exception;
    }

    [Fact]
    public async Task A_File_Is_Attached_To_A_Test_Case_And_Comes_Back_Byte_For_Byte()
    {
        var testCase = await CaseAsync();
        var content = Bytes("PNG? no, but it will do for the test");

        var attachment = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "login-page.png", content, "  The login page  "));

        attachment.FileName.ShouldBe("login-page.png");
        attachment.ContentType.ShouldBe("image/png", "the type comes from the extension, not from the upload");
        attachment.Size.ShouldBe(content.Length);
        attachment.Sha256.ShouldBe(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        attachment.Description.ShouldBe("The login page");
        attachment.OwnerType.ShouldBe(AttachmentOwnerType.TestCase);
        attachment.OwnerId.ShouldBe(testCase.Id);
        attachment.CreationTime.ShouldBeGreaterThan(DateTime.Now.AddMinutes(-1));
        InMemoryBlobProvider.Contains(attachment.Id.ToString("N")).ShouldBeTrue();

        var download = await _attachments.DownloadAsync(attachment.Id);
        download.FileName.ShouldBe("login-page.png");
        download.ContentType.ShouldBe("image/png");
        (await ReadAsync(download)).ShouldBe(content);

        var list = await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase, OwnerIds = { testCase.Id } });
        list.Single().Id.ShouldBe(attachment.Id);
    }

    [Fact]
    public async Task The_Name_Is_Cleaned_And_The_Extension_May_Be_In_Any_Case()
    {
        var testCase = await CaseAsync();

        var attachment = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, @"C:\Users\me\..\evil/Screen<shot>.PNG", Bytes("x")));

        attachment.FileName.ShouldBe("Screenshot.PNG");
        attachment.ContentType.ShouldBe("image/png");
    }

    [Theory]
    [InlineData(@"..\..\a.txt", "a.txt")]
    [InlineData("/etc/passwd.log", "passwd.log")]
    [InlineData("  .hidden.txt ", "hidden.txt")]
    [InlineData("a\u0000b\tc.txt", "abc.txt")]
    [InlineData("what?.log", "what.log")]
    [InlineData("", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData("///", "attachment")]
    public void File_Names_Lose_Their_Path_And_Reserved_Characters(string? name, string expected)
    {
        AttachmentManager.CleanFileName(name).ShouldBe(expected);
    }

    [Fact]
    public void A_Long_Name_Is_Cut_But_Keeps_Its_Extension()
    {
        var cleaned = AttachmentManager.CleanFileName(new string('a', 400) + ".png");

        cleaned.Length.ShouldBe(AttachmentConsts.MaxFileNameLength);
        cleaned.ShouldEndWith(".png");
    }

    [Fact]
    public async Task Files_That_Are_Empty_Too_Large_Or_Of_A_Kind_That_Is_Not_Accepted_Are_Refused_And_Leave_Nothing()
    {
        var testCase = await CaseAsync();

        await RefusedAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "empty.png", Array.Empty<byte>()), TestCaseManagementErrorCodes.AttachmentEmpty);

        var tooLarge = await RefusedAsync(
            Upload(AttachmentOwnerType.TestCase, testCase.Id, "video.mp4", new byte[26 * 1024 * 1024]), TestCaseManagementErrorCodes.AttachmentTooLarge);
        tooLarge.Data["Size"].ShouldBe(26.0m);
        tooLarge.Data["Limit"].ShouldBe(25.0m);

        foreach (var name in new[] { "setup.exe", "page.html", "image.svg", "script.js", "archive.7z", "noextension", "trick.png.exe", "dot." })
        {
            await RefusedAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, name, Bytes("x")), TestCaseManagementErrorCodes.AttachmentTypeNotAllowed);
        }

        (await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase, OwnerIds = { testCase.Id } })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Wrongly_Declared_Length_Does_Not_Get_Past_The_Limit()
    {
        var testCase = await CaseAsync();
        var big = new byte[26 * 1024 * 1024];
        var input = new UploadAttachmentInput
        {
            OwnerType = AttachmentOwnerType.TestCase,
            OwnerId = testCase.Id,
            // The caller says 10 bytes and sends 26 MB.
            File = new RemoteStreamContent(new MemoryStream(big), "lie.mp4", "video/mp4", 10),
        };

        await RefusedAsync(input, TestCaseManagementErrorCodes.AttachmentTooLarge);
    }

    [Fact]
    public async Task The_Message_For_A_Type_That_Is_Not_Accepted_Lists_What_Is()
    {
        var testCase = await CaseAsync();

        var refused = await RefusedAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "setup.exe", Bytes("x")), TestCaseManagementErrorCodes.AttachmentTypeNotAllowed);

        refused.Data["Extension"].ShouldBe(".exe");
        var allowed = (string)refused.Data["Allowed"]!;
        allowed.ShouldContain(".png");
        allowed.ShouldNotContain(".svg");
    }

    [Fact]
    public async Task An_Owner_Has_At_Most_As_Many_Files_As_The_Limit()
    {
        var testCase = await CaseAsync();
        for (var i = 0; i < 25; i++)
        {
            await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, $"note-{i}.txt", Bytes("n")));
        }

        await RefusedAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "one-more.txt", Bytes("n")), TestCaseManagementErrorCodes.AttachmentTooMany);

        // Another owner has a limit of its own.
        var other = await CaseAsync("TC-2");
        await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, other.Id, "fine.txt", Bytes("n")));
    }

    [Fact]
    public async Task A_File_Needs_An_Owner_That_Exists()
    {
        await RefusedAsync(Upload(AttachmentOwnerType.TestCase, Guid.NewGuid(), "a.txt", Bytes("x")), TestCaseManagementErrorCodes.AttachmentOwnerNotFound);
        await RefusedAsync(Upload(AttachmentOwnerType.TestExecution, Guid.NewGuid(), "a.txt", Bytes("x")), TestCaseManagementErrorCodes.AttachmentOwnerNotFound);

        // The id of a test case is not the id of an execution.
        var testCase = await CaseAsync();
        await RefusedAsync(Upload(AttachmentOwnerType.TestExecution, testCase.Id, "a.txt", Bytes("x")), TestCaseManagementErrorCodes.AttachmentOwnerNotFound);
    }

    [Fact]
    public async Task A_Deleted_Test_Case_Cannot_Be_Given_Files()
    {
        var testCase = await CaseAsync();
        await _testCases.DeleteAsync(testCase.Id);

        await RefusedAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "a.txt", Bytes("x")), TestCaseManagementErrorCodes.AttachmentOwnerNotFound);
    }

    [Fact]
    public async Task An_Execution_Attempt_Gets_Its_Evidence_And_The_Lists_Of_Several_Owners_Are_Told_Apart()
    {
        var (_, executionId) = await AttemptAsync("e2e.evidence");
        var testCase = await CaseAsync("TC-OTHER");

        var screenshot = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestExecution, executionId, "failure.png", Bytes("shot"), "The error toast"));
        var log = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestExecution, executionId, "console.log", Bytes("Error: boom")));
        await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "spec.pdf", Bytes("%PDF")));

        var forExecution = await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestExecution, OwnerIds = { executionId } });
        forExecution.Select(a => a.FileName).ShouldBe(new[] { "failure.png", "console.log" });
        forExecution[0].Id.ShouldBe(screenshot.Id);
        forExecution[1].ContentType.ShouldBe("text/plain");
        log.Id.ShouldBe(forExecution[1].Id);

        // The same id asked as a test case finds nothing: the owner type is part of the owner.
        (await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase, OwnerIds = { executionId } })).ShouldBeEmpty();

        var both = await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase, OwnerIds = { testCase.Id, executionId, testCase.Id } });
        both.Single().FileName.ShouldBe("spec.pdf");
    }

    [Fact]
    public async Task Deleting_Removes_The_File_From_The_List_And_From_The_Storage()
    {
        var testCase = await CaseAsync();
        var attachment = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "old.txt", Bytes("old")));
        var keep = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "keep.txt", Bytes("keep")));

        await _attachments.DeleteAsync(attachment.Id);

        (await _attachments.GetListAsync(new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase, OwnerIds = { testCase.Id } }))
            .Select(a => a.Id).ShouldBe(new[] { keep.Id });
        InMemoryBlobProvider.Contains(attachment.Id.ToString("N")).ShouldBeFalse();
        InMemoryBlobProvider.Contains(keep.Id.ToString("N")).ShouldBeTrue();
        await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.DownloadAsync(attachment.Id));
        await Should.ThrowAsync<EntityNotFoundException>(() => _attachments.DeleteAsync(attachment.Id));

        // The slot is free again: the limit counts files that exist.
        await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "new.txt", Bytes("new")));
    }

    [Fact]
    public async Task A_File_That_Was_Lost_From_The_Storage_Is_Reported_And_Not_A_Server_Error()
    {
        var testCase = await CaseAsync();
        var attachment = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "lost.txt", Bytes("gone")));
        InMemoryBlobProvider.Remove(attachment.Id.ToString("N"));

        var exception = await Should.ThrowAsync<BusinessException>(() => _attachments.DownloadAsync(attachment.Id));

        exception.Code.ShouldBe(TestCaseManagementErrorCodes.AttachmentFileMissing);
        exception.Data["FileName"].ShouldBe("lost.txt");
    }

    [Fact]
    public async Task The_Same_File_Can_Be_Attached_Twice_And_Each_Is_Its_Own_Copy()
    {
        var testCase = await CaseAsync();

        var first = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "same.txt", Bytes("same")));
        var second = await _attachments.UploadAsync(Upload(AttachmentOwnerType.TestCase, testCase.Id, "same.txt", Bytes("same")));

        second.Id.ShouldNotBe(first.Id);
        second.Sha256.ShouldBe(first.Sha256);
        await _attachments.DeleteAsync(first.Id);
        (await ReadAsync(await _attachments.DownloadAsync(second.Id))).ShouldBe(Bytes("same"));
    }

    [Fact]
    public async Task Asking_For_Files_Needs_At_Least_One_And_At_Most_A_Hundred_Owners()
    {
        // The limits are declared on the input, which the validation of the ABP pipeline applies over HTTP (see the HTTP tests).
        var input = new GetAttachmentsInput { OwnerType = AttachmentOwnerType.TestCase };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(input, new System.ComponentModel.DataAnnotations.ValidationContext(input), results, true).ShouldBeFalse();

        input.OwnerIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();
        results.Clear();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(input, new System.ComponentModel.DataAnnotations.ValidationContext(input), results, true).ShouldBeFalse();
        await Task.CompletedTask;
    }
}
