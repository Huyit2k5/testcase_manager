using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace Acme.TestCaseManagement;

public class AutomationResultsAppService_Tests : TestCaseManagementApplicationTestBase
{
    static AutomationResultsAppService_Tests()
    {
        // The messages are asserted in English, whatever the language of the machine that runs the tests.
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = new System.Globalization.CultureInfo("en");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = new System.Globalization.CultureInfo("en");
    }

    private readonly ITestSuiteAppService _suites;
    private readonly ITestCaseAppService _testCases;
    private readonly ITestRunAppService _runs;
    private readonly IAutomationResultsAppService _publisher;
    private readonly IRepository<TestCase, Guid> _caseRepository;
    private Guid? _suiteId;

    public AutomationResultsAppService_Tests()
    {
        _suites = GetRequiredService<ITestSuiteAppService>();
        _testCases = GetRequiredService<ITestCaseAppService>();
        _runs = GetRequiredService<ITestRunAppService>();
        _publisher = GetRequiredService<IAutomationResultsAppService>();
        _caseRepository = GetRequiredService<IRepository<TestCase, Guid>>();
    }

    private async Task<TestCaseDto> CaseAsync(string code, string? automationId, bool approved = true, SeverityLevel severity = SeverityLevel.High)
    {
        _suiteId ??= (await _suites.CreateAsync(new CreateTestSuiteDto { Name = "Automated" })).Id;
        var created = await _testCases.CreateAsync(new CreateUpdateTestCaseDto
        {
            SuiteId = _suiteId.Value,
            Code = code,
            Title = $"Title of {code}",
            Severity = severity,
            AutomationId = automationId,
            Steps = { new TestStepDto { Action = "Run the script", ExpectedResult = "It passes" } },
        });

        return approved
            ? await _testCases.ChangeStatusAsync(created.Id, new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved })
            : created;
    }

    private static AutomationResultInput Result(string automationId, TestResultStatus status = TestResultStatus.Passed, int seconds = 5) =>
        new() { AutomationId = automationId, Status = status, DurationSeconds = seconds };

    private static PublishAutomationResultsInput NewRun(params AutomationResultInput[] results) =>
        new() { Run = new AutomationRunInput { Title = "Nightly", Environment = "Staging" }, Results = results.ToList() };

    private static PublishAutomationResultsInput IntoRun(Guid runId, params AutomationResultInput[] results) =>
        new() { RunId = runId, Results = results.ToList() };

    // ---- the plain case

    [Fact]
    public async Task A_Pipeline_Publishes_Results_Into_A_New_Run_Matched_By_Automation_Id()
    {
        var login = await CaseAsync("TC-1", "e2e.login");
        var checkout = await CaseAsync("TC-2", "e2e.checkout", severity: SeverityLevel.Critical);

        var answer = await _publisher.PublishAsync(NewRun(
            Result("e2e.login"),
            new AutomationResultInput
            {
                AutomationId = "e2e.checkout",
                Status = TestResultStatus.Failed,
                ActualResult = "Timeout after 30s",
                DurationSeconds = 31,
                Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-7" } },
            }));

        answer.Accepted.ShouldBeTrue();
        answer.RunCreated.ShouldBeTrue();
        answer.RunStatus.ShouldBe(RunStatus.InProgress);
        (answer.Received, answer.Recorded, answer.Scheduled, answer.Unmatched).ShouldBe((2, 2, 2, 0));
        answer.Results.Select(r => (r.Index, r.Outcome, r.TestCaseCode, r.AttemptNumber)).ShouldBe(new[]
        {
            (0, AutomationOutcome.Recorded, (string?)"TC-1", (int?)1),
            (1, AutomationOutcome.Recorded, "TC-2", 1),
        });

        var run = await _runs.GetAsync(answer.RunId!.Value);
        run.Title.ShouldBe("Nightly");
        run.Environment.ShouldBe("Staging");
        run.Items.Select(i => (i.TestCaseCode, i.CurrentStatus, i.VersionNumber, i.AttemptCount)).ShouldBe(new[]
        {
            ((string?)"TC-1", TestResultStatus.Passed, 1, 1),
            ("TC-2", TestResultStatus.Failed, 1, 1),
        });

        var attempts = await _runs.GetExecutionsAsync(run.Id, run.Items.Single(i => i.TestCaseCode == "TC-2").Id);
        attempts.Single().ActualResult.ShouldBe("Timeout after 30s");
        attempts.Single().DurationSeconds.ShouldBe(31);
        var defect = attempts.Single().DefectLinks.Single();
        (defect.ExternalSystem, defect.IssueKey, defect.Severity).ShouldBe(("Jira", "BUG-7", SeverityLevel.Critical));
        login.Id.ShouldNotBe(checkout.Id);
    }

    [Fact]
    public async Task The_Automation_Id_Is_Matched_Without_Regard_To_Case_Or_Surrounding_Spaces()
    {
        await CaseAsync("TC-1", "Suite.Login");

        var answer = await _publisher.PublishAsync(NewRun(Result("  suite.LOGIN ")));

        answer.Recorded.ShouldBe(1);
        answer.Results.Single().TestCaseCode.ShouldBe("TC-1");
    }

    [Fact]
    public async Task Results_Of_Tests_The_Library_Does_Not_Know_Are_Reported_And_Do_Not_Stop_The_Others()
    {
        await CaseAsync("TC-1", "known");

        var answer = await _publisher.PublishAsync(NewRun(Result("known"), Result("unknown.one"), Result("unknown.two", TestResultStatus.Failed)));

        answer.Accepted.ShouldBeTrue();
        (answer.Received, answer.Recorded, answer.Unmatched).ShouldBe((3, 1, 2));
        answer.Results[1].Outcome.ShouldBe(AutomationOutcome.Unmatched);
        answer.Results[1].Message.ShouldBe("No test case has the automation id 'unknown.one'.");
        answer.Results[1].AttemptNumber.ShouldBeNull();
        (await _runs.GetAsync(answer.RunId!.Value)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task In_Strict_Mode_Nothing_Is_Recorded_And_No_Run_Is_Created_Unless_Every_Result_Matches()
    {
        await CaseAsync("TC-1", "known");
        var input = NewRun(Result("known"), Result("unknown"));
        input.FailOnUnmatched = true;

        var answer = await _publisher.PublishAsync(input);

        answer.Accepted.ShouldBeFalse();
        answer.Recorded.ShouldBe(0);
        answer.Unmatched.ShouldBe(1);
        answer.RunId.ShouldBeNull();
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(0);

        // The same request without the unknown test goes through.
        input.Results.RemoveAt(1);
        (await _publisher.PublishAsync(input)).Accepted.ShouldBeTrue();
    }

    [Fact]
    public async Task When_Nothing_Can_Be_Recorded_No_Run_Is_Created()
    {
        var answer = await _publisher.PublishAsync(NewRun(Result("nothing.here")));

        answer.RunId.ShouldBeNull();
        answer.RunCreated.ShouldBeFalse();
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(0);
    }

    // ---- which test cases can be recorded

    [Fact]
    public async Task A_Test_Case_That_Is_Not_Approved_Cannot_Join_A_Run()
    {
        await CaseAsync("TC-1", "still.draft", approved: false);

        var answer = await _publisher.PublishAsync(NewRun(Result("still.draft")));

        answer.NotApproved.ShouldBe(1);
        answer.Results.Single().Message.ShouldBe("Test case 'TC-1' is Draft, and only approved test cases can be scheduled in a run.");
        answer.RunId.ShouldBeNull();
    }

    [Fact]
    public async Task Two_Test_Cases_With_The_Same_Automation_Id_Are_Ambiguous()
    {
        await CaseAsync("TC-1", "twin.a");
        var second = await CaseAsync("TC-2", "twin.b");

        // The application service refuses duplicates, so the second one is made a twin directly in the database.
        await WithUnitOfWorkAsync(async () =>
        {
            var entity = await _caseRepository.GetAsync(second.Id);
            entity.SetDetails(null, null, null, entity.Priority, entity.Severity, entity.ExecutionType, entity.Kind, entity.Layer, "twin.a");
            await _caseRepository.UpdateAsync(entity, autoSave: true);
        });

        var answer = await _publisher.PublishAsync(NewRun(Result("twin.a"), Result("twin.b")));

        answer.Ambiguous.ShouldBe(1);
        answer.Results[0].Message.ShouldBe("2 test cases have the automation id 'twin.a': TC-1, TC-2.");
        answer.Results[1].Outcome.ShouldBe(AutomationOutcome.Unmatched); // nobody has twin.b any more
    }

    [Fact]
    public async Task Into_An_Existing_Run_Missing_Approved_Test_Cases_Are_Added_And_Others_Keep_Their_Item()
    {
        var inRun = await CaseAsync("TC-1", "in.run");
        await CaseAsync("TC-2", "not.yet");
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Manual", Environment = "QA", TestCaseIds = { inRun.Id } });

        var answer = await _publisher.PublishAsync(IntoRun(run.Id, Result("in.run"), Result("not.yet", TestResultStatus.Skipped)));

        answer.RunCreated.ShouldBeFalse();
        answer.RunId.ShouldBe(run.Id);
        (answer.Recorded, answer.Scheduled).ShouldBe((2, 1));
        var after = await _runs.GetAsync(run.Id);
        after.Items.Select(i => (i.TestCaseCode, i.CurrentStatus, i.Sequence)).ShouldBe(new[]
        {
            ((string?)"TC-1", TestResultStatus.Passed, 1),
            ("TC-2", TestResultStatus.Skipped, 2),
        });
    }

    [Fact]
    public async Task When_Adding_Is_Off_A_Test_Case_Outside_The_Run_Gets_Not_In_Run()
    {
        var inRun = await CaseAsync("TC-1", "in.run");
        await CaseAsync("TC-2", "outside");
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Manual", Environment = "QA", TestCaseIds = { inRun.Id } });
        var input = IntoRun(run.Id, Result("in.run"), Result("outside"));
        input.AddMissingToRun = false;

        var answer = await _publisher.PublishAsync(input);

        (answer.Recorded, answer.NotInRun, answer.Scheduled).ShouldBe((1, 1, 0));
        answer.Results[1].Message.ShouldBe("Test case 'TC-2' is not in the run.");
        (await _runs.GetAsync(run.Id)).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_Test_Case_Edited_After_It_Joined_The_Run_Is_Still_Recorded_Against_The_Version_In_The_Run()
    {
        var created = await CaseAsync("TC-1", "e2e.versioned");
        var run = await _runs.CreateAsync(new CreateTestRunDto { Title = "Manual", Environment = "QA", TestCaseIds = { created.Id } });

        var full = await _testCases.GetAsync(created.Id);
        await _testCases.UpdateAsync(created.Id, new CreateUpdateTestCaseDto
        {
            SuiteId = full.SuiteId, Code = full.Code, Title = "Edited", AutomationId = full.AutomationId,
            Steps = full.Steps.Select(s => new TestStepDto { Id = s.Id, Action = s.Action, ExpectedResult = s.ExpectedResult }).ToList(),
        });

        var answer = await _publisher.PublishAsync(IntoRun(run.Id, Result("e2e.versioned")));

        answer.Recorded.ShouldBe(1);
        answer.Scheduled.ShouldBe(0); // the run already has the test case, at version 1
        (await _runs.GetAsync(run.Id)).Items.Single().VersionNumber.ShouldBe(1);
    }

    // ---- retries and flaky tests

    [Fact]
    public async Task The_Retries_Of_A_Test_Are_Recorded_In_The_Order_Of_Their_Attempt_Numbers_And_The_Test_Is_Flagged_Flaky()
    {
        var created = await CaseAsync("TC-1", "e2e.retry");

        var answer = await _publisher.PublishAsync(NewRun(
            new AutomationResultInput { AutomationId = "e2e.retry", Status = TestResultStatus.Passed, AttemptNumber = 3 },
            new AutomationResultInput { AutomationId = "e2e.retry", Status = TestResultStatus.Failed, AttemptNumber = 1, ActualResult = "first" },
            new AutomationResultInput { AutomationId = "e2e.retry", Status = TestResultStatus.Failed, AttemptNumber = 2, ActualResult = "second" }));

        // Stored attempt numbers follow the order of recording; each outcome names the one its result got.
        answer.Results.Select(r => (r.Index, r.AttemptNumber)).ShouldBe(new[] { (0, (int?)3), (1, 1), (2, 2) });
        answer.FlaggedFlaky.ShouldBe(new[] { "TC-1" });

        var run = await _runs.GetAsync(answer.RunId!.Value);
        var attempts = await _runs.GetExecutionsAsync(run.Id, run.Items.Single().Id);
        attempts.Select(a => (a.AttemptNumber, a.Status)).ShouldBe(new[]
        {
            (1, TestResultStatus.Failed), (2, TestResultStatus.Failed), (3, TestResultStatus.Passed),
        });
        run.Items.Single().CurrentStatus.ShouldBe(TestResultStatus.Passed);
        (await _testCases.GetAsync(created.Id)).IsFlaky.ShouldBeTrue();

        // Publishing again does not flag it a second time.
        var again = await _publisher.PublishAsync(IntoRun(run.Id,
            new AutomationResultInput { AutomationId = "e2e.retry", Status = TestResultStatus.Failed },
            new AutomationResultInput { AutomationId = "e2e.retry", Status = TestResultStatus.Passed }));
        again.FlaggedFlaky.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_Runner_Can_Say_A_Test_Is_Flaky_And_A_Test_That_Only_Passes_Or_Only_Fails_Is_Not_Flagged()
    {
        var said = await CaseAsync("TC-1", "said.flaky");
        var passes = await CaseAsync("TC-2", "only.passes");
        var fails = await CaseAsync("TC-3", "only.fails");

        var answer = await _publisher.PublishAsync(NewRun(
            new AutomationResultInput { AutomationId = "said.flaky", Status = TestResultStatus.Passed, IsFlaky = true },
            Result("only.passes"), Result("only.passes"),
            Result("only.fails", TestResultStatus.Failed), Result("only.fails", TestResultStatus.Failed)));

        answer.FlaggedFlaky.ShouldBe(new[] { "TC-1" });
        (await _testCases.GetAsync(said.Id)).IsFlaky.ShouldBeTrue();
        (await _testCases.GetAsync(passes.Id)).IsFlaky.ShouldBeFalse();
        (await _testCases.GetAsync(fails.Id)).IsFlaky.ShouldBeFalse();
    }

    [Fact]
    public async Task Flagging_A_Test_As_Flaky_Does_Not_Publish_A_New_Version()
    {
        var created = await CaseAsync("TC-1", "e2e.flag");

        await _publisher.PublishAsync(NewRun(
            new AutomationResultInput { AutomationId = "e2e.flag", Status = TestResultStatus.Failed },
            new AutomationResultInput { AutomationId = "e2e.flag", Status = TestResultStatus.Passed }));

        (await _testCases.GetAsync(created.Id)).CurrentVersion.ShouldBe(1);
        (await _testCases.GetVersionsAsync(created.Id)).Count.ShouldBe(1);
    }

    // ---- the run

    [Fact]
    public async Task The_Run_Can_Be_Completed_By_The_Last_Publish_And_Then_Takes_No_More()
    {
        await CaseAsync("TC-1", "e2e.one");
        var input = NewRun(Result("e2e.one"));
        input.CompleteRun = true;

        var answer = await _publisher.PublishAsync(input);

        answer.RunStatus.ShouldBe(RunStatus.Completed);
        (await _runs.GetAsync(answer.RunId!.Value)).Status.ShouldBe(RunStatus.Completed);

        var exception = await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(IntoRun(answer.RunId.Value, Result("e2e.one"))));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.TestRunAlreadyCompleted);
    }

    [Fact]
    public async Task Shards_Of_One_Pipeline_Share_The_Run_Created_By_The_First()
    {
        await CaseAsync("TC-1", "shard.a");
        await CaseAsync("TC-2", "shard.b");

        var first = await _publisher.PublishAsync(NewRun(Result("shard.a")));
        var second = await _publisher.PublishAsync(IntoRun(first.RunId!.Value, Result("shard.b", TestResultStatus.Failed)));

        second.RunId.ShouldBe(first.RunId);
        (await _runs.GetAsync(first.RunId.Value)).Summary.TotalItems.ShouldBe(2);
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task An_Unknown_Run_Is_Not_Found()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => _publisher.PublishAsync(IntoRun(Guid.NewGuid(), Result("x"))));
    }

    // ---- idempotency

    [Fact]
    public async Task The_Same_Request_With_The_Same_Key_Is_Answered_Again_And_Recorded_Once()
    {
        await CaseAsync("TC-1", "e2e.once");
        var input = NewRun(Result("e2e.once"));
        input.IdempotencyKey = "build-4711";

        var first = await _publisher.PublishAsync(input);
        var second = await _publisher.PublishAsync(input);

        first.Replayed.ShouldBeFalse();
        second.Replayed.ShouldBeTrue();
        second.RunId.ShouldBe(first.RunId);
        second.Recorded.ShouldBe(1);
        second.Results.Single().AttemptNumber.ShouldBe(1);
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(1);
        var run = await _runs.GetAsync(first.RunId!.Value);
        run.Items.Single().AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_Key_Used_For_A_Different_Request_Is_Refused_And_Other_Keys_Are_Independent()
    {
        await CaseAsync("TC-1", "e2e.a");
        var input = NewRun(Result("e2e.a"));
        input.IdempotencyKey = "build-1";
        var first = await _publisher.PublishAsync(input);

        var different = IntoRun(first.RunId!.Value, Result("e2e.a", TestResultStatus.Failed));
        different.IdempotencyKey = "build-1";
        var exception = await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(different));
        exception.Code.ShouldBe(TestCaseManagementErrorCodes.IdempotencyKeyReused);

        different.IdempotencyKey = "build-2";
        (await _publisher.PublishAsync(different)).Replayed.ShouldBeFalse();
        (await _runs.GetAsync(first.RunId.Value)).Items.Single().AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_Strict_Request_That_Was_Refused_Does_Not_Use_Up_Its_Key()
    {
        await CaseAsync("TC-1", "known");
        var input = NewRun(Result("known"), Result("unknown"));
        input.IdempotencyKey = "build-9";
        input.FailOnUnmatched = true;

        (await _publisher.PublishAsync(input)).Accepted.ShouldBeFalse();

        input.Results.RemoveAt(1);
        var retried = await _publisher.PublishAsync(input);
        retried.Accepted.ShouldBeTrue();
        retried.Replayed.ShouldBeFalse();
    }

    [Fact]
    public async Task Without_A_Key_Every_Request_Is_Recorded()
    {
        await CaseAsync("TC-1", "e2e.twice");
        var first = await _publisher.PublishAsync(NewRun(Result("e2e.twice")));

        await _publisher.PublishAsync(IntoRun(first.RunId!.Value, Result("e2e.twice")));
        await _publisher.PublishAsync(IntoRun(first.RunId.Value, Result("e2e.twice")));

        (await _runs.GetAsync(first.RunId.Value)).Items.Single().AttemptCount.ShouldBe(3);
    }

    // ---- refusing a request

    [Fact]
    public async Task A_Request_Names_Either_An_Existing_Run_Or_A_New_One()
    {
        var neither = new PublishAutomationResultsInput { Results = { Result("x") } };
        var both = NewRun(Result("x"));
        both.RunId = Guid.NewGuid();

        foreach (var input in new[] { neither, both })
        {
            (await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(input))).Code
                .ShouldBe(TestCaseManagementErrorCodes.InvalidAutomationRun);
        }
    }

    [Fact]
    public async Task A_Request_May_Not_Carry_More_Results_Than_The_Limit()
    {
        var options = GetRequiredService<IOptions<TestCaseManagementAutomationOptions>>().Value;
        options.MaxResultsPerRequest = 2;
        try
        {
            var exception = await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(NewRun(Result("a"), Result("b"), Result("c"))));
            exception.Code.ShouldBe(TestCaseManagementErrorCodes.AutomationTooManyResults);
            exception.Data["Count"].ShouldBe(3);
            exception.Data["Limit"].ShouldBe(2);
        }
        finally
        {
            options.MaxResultsPerRequest = new TestCaseManagementAutomationOptions().MaxResultsPerRequest;
        }
    }

    [Fact]
    public async Task Untested_Is_Not_A_Result_And_Only_A_Failed_Result_Carries_Defects()
    {
        await CaseAsync("TC-1", "e2e.rules");

        (await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(NewRun(Result("e2e.rules", TestResultStatus.Untested)))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.InvalidExecutionStatus);

        var passedWithDefect = new AutomationResultInput
        {
            AutomationId = "e2e.rules", Status = TestResultStatus.Passed,
            Defects = { new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" } },
        };
        (await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(NewRun(passedWithDefect))))
            .Code.ShouldBe(TestCaseManagementErrorCodes.DefectRequiresFailedExecution);

        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_Failure_In_The_Middle_Leaves_Nothing_Behind()
    {
        await CaseAsync("TC-1", "e2e.fine");
        await CaseAsync("TC-2", "e2e.dup.defect");
        var twice = new AutomationResultInput
        {
            AutomationId = "e2e.dup.defect", Status = TestResultStatus.Failed,
            Defects =
            {
                new AddDefectLinkDto { ExternalSystem = "Jira", IssueKey = "BUG-1" },
                new AddDefectLinkDto { ExternalSystem = "jira", IssueKey = "bug-1" },
            },
        };

        var exception = await Should.ThrowAsync<BusinessException>(() => _publisher.PublishAsync(NewRun(Result("e2e.fine"), twice)));

        exception.Code.ShouldBe(TestCaseManagementErrorCodes.DuplicateDefectLink);
        (await _runs.GetListAsync(new GetTestRunListInput())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_Messages_Follow_The_Language_Of_The_Caller()
    {
        using (new CultureScope("vi"))
        {
            var answer = await _publisher.PublishAsync(NewRun(Result("khong.co")));

            answer.Results.Single().Message.ShouldBe("Không có test case nào có automation id 'khong.co'.");
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly System.Globalization.CultureInfo _previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        private readonly System.Globalization.CultureInfo _previousUi = System.Globalization.CultureInfo.CurrentUICulture;

        public CultureScope(string name)
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(name);
        }

        public void Dispose()
        {
            System.Globalization.CultureInfo.CurrentCulture = _previousCulture;
            System.Globalization.CultureInfo.CurrentUICulture = _previousUi;
        }
    }
}
