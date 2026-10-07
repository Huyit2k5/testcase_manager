using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Acme.TestCaseManagement.Attachments.Dtos;
using Acme.TestCaseManagement.Automation.Dtos;
using Acme.TestCaseManagement.Insights.Dtos;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Rtm.Dtos;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.SignOff.Dtos;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Xunit;

namespace Acme.TestCaseManagement;

/// <summary>
/// The controllers had only been compile-checked. This runs a whole release cycle over HTTP against the hosted
/// module (routing, model binding, JSON, ABP pipeline) and proves that every operation of the OpenAPI contract
/// was called at least once.
/// </summary>
[Collection(HostCollection.Name)]
public class HttpApiFlow_Tests
{
    private const string Root = "/api/test-case-management";

    private readonly TestCaseManagementHost _host;

    public HttpApiFlow_Tests(TestCaseManagementHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task A_Release_Cycle_Runs_Through_Every_Operation_Of_The_Contract()
    {
        var calls = new ConcurrentBag<(HttpMethod Method, string Path)>();
        var qaLead = await ApiClient.LoginAsync(_host, "qa.lead", calls);
        var productOwner = await ApiClient.LoginAsync(_host, "product.owner", calls);
        var qaLeadId = qaLead.UserId;
        var productOwnerId = productOwner.UserId;
        var milestone = Guid.NewGuid();

        // ---- Library: suites ---------------------------------------------------------------------------------
        var suite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = "Checkout" });
        var archive = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = "Archive", Description = "Old flows" });

        var renamed = await qaLead.PutAsync<TestSuiteDto>($"{Root}/suites/{suite.Id}", new UpdateTestSuiteDto { Name = "Checkout flows" });
        renamed.Name.ShouldBe("Checkout flows");

        var moved = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites/{archive.Id}/move", new MoveTestSuiteDto { NewParentId = suite.Id, NewOrder = 0 });
        moved.ParentId.ShouldBe(suite.Id);

        var tree = await qaLead.GetAsync<List<TestSuiteTreeDto>>($"{Root}/suites/tree");
        tree.Single(node => node.Id == suite.Id).Children.Select(child => child.Name).ShouldBe(new[] { "Archive" });
        (await qaLead.GetAsync<TestSuiteDto>($"{Root}/suites/{suite.Id}")).Name.ShouldBe("Checkout flows");

        // ---- Library: test cases and versions ----------------------------------------------------------------
        var caseA = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", NewTestCase(suite.Id, "PAY-001", "Pay with a valid card", PriorityLevel.Urgent));
        var caseB = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", NewTestCase(suite.Id, "PAY-002", "Pay with an expired card"));
        var caseC = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", NewTestCase(suite.Id, "PAY-003", "Pay with a gift voucher"));
        caseA.Status.ShouldBe(TestCaseStatus.Draft);
        caseA.Steps.Count.ShouldBe(2);
        caseA.Steps.ShouldAllBe(step => step.Id != null);

        foreach (var testCase in new[] { caseA, caseB, caseC })
        {
            var approvedCase = await qaLead.PostAsync<TestCaseDto>(
                $"{Root}/test-cases/{testCase.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
            approvedCase.Status.ShouldBe(TestCaseStatus.Approved);
        }

        // Guid, enum and text filters are bound from the query string.
        var urgent = await qaLead.GetAsync<PagedResultDto<TestCaseDto>>(
            $"{Root}/test-cases?SuiteId={suite.Id}&Priority={(int)PriorityLevel.Urgent}&Status={(int)TestCaseStatus.Approved}&MaxResultCount=10");
        urgent.Items.Select(testCase => testCase.Code).ShouldBe(new[] { "PAY-001" });
        urgent.TotalCount.ShouldBe(1);
        var voucher = await qaLead.GetAsync<PagedResultDto<TestCaseDto>>($"{Root}/test-cases?Filter=voucher&SkipCount=0&MaxResultCount=10");
        voucher.Items.Select(testCase => testCase.Code).ShouldBe(new[] { "PAY-003" });

        // Editing an approved test case publishes version 2; the old snapshot stays readable.
        var detail = await qaLead.GetAsync<TestCaseDto>($"{Root}/test-cases/{caseA.Id}");
        var edit = NewTestCase(suite.Id, "PAY-001", "Pay with a valid card (3-D Secure)", PriorityLevel.Urgent);
        edit.Steps = detail.Steps;
        edit.ChangeSummary = "Added 3-D Secure";
        var edited = await qaLead.PutAsync<TestCaseDto>($"{Root}/test-cases/{caseA.Id}", edit);
        edited.CurrentVersion.ShouldBe(2);
        (await qaLead.GetAsync<List<TestCaseVersionDto>>($"{Root}/test-cases/{caseA.Id}/versions"))
            .Select(version => version.VersionNumber).ShouldBe(new[] { 1, 2 }, ignoreOrder: true);
        (await qaLead.GetAsync<TestCaseVersionDto>($"{Root}/test-cases/{caseA.Id}/versions/1")).Title.ShouldBe("Pay with a valid card");

        var reordered = await qaLead.PutAsync<TestCaseDto>(
            $"{Root}/test-cases/{caseC.Id}/steps/order",
            new ReorderTestStepsDto { StepIds = caseC.Steps.Select(step => step.Id!.Value).Reverse().ToList() });
        reordered.Steps.Select(step => step.Action).ShouldBe(caseC.Steps.Select(step => step.Action).Reverse());

        // ---- Execution: plan, run, attempts ------------------------------------------------------------------
        var plan = await qaLead.PostAsync<TestPlanDto>($"{Root}/plans", NewPlan("Sprint 1", milestone));
        (await qaLead.GetAsync<TestPlanDto>($"{Root}/plans/{plan.Id}")).Name.ShouldBe("Sprint 1");
        (await qaLead.PutAsync<TestPlanDto>($"{Root}/plans/{plan.Id}", NewPlan("Sprint 1 - Checkout", milestone))).Name.ShouldBe("Sprint 1 - Checkout");
        (await qaLead.PostAsync<TestPlanDto>($"{Root}/plans/{plan.Id}/status", new ChangeTestPlanStatusDto { TargetStatus = PlanStatus.Active }))
            .Status.ShouldBe(PlanStatus.Active);
        var plans = await qaLead.GetAsync<PagedResultDto<TestPlanDto>>(
            $"{Root}/plans?Filter=Checkout&Status={(int)PlanStatus.Active}&MilestoneId={milestone}");
        plans.Items.Select(candidate => candidate.Id).ShouldBe(new[] { plan.Id });

        var run = await qaLead.PostAsync<TestRunDto>($"{Root}/runs", new CreateTestRunDto
        {
            TestPlanId = plan.Id, Title = "Sprint 1 regression", Environment = "Staging", TestCaseIds = { caseA.Id, caseB.Id },
        });
        run.Status.ShouldBe(RunStatus.Planned);
        run.Items.Count.ShouldBe(2);
        var itemA = run.Items.Single(item => item.TestCaseId == caseA.Id);
        var itemB = run.Items.Single(item => item.TestCaseId == caseB.Id);
        itemA.VersionNumber.ShouldBe(2, "the item is bound to the version that was current when the run was created");

        var withC = await qaLead.PostAsync<TestRunDto>($"{Root}/runs/{run.Id}/items", new AddTestRunItemsDto { TestCaseIds = { caseC.Id } });
        withC.Items.Count.ShouldBe(3);
        var itemC = withC.Items.Single(item => item.TestCaseId == caseC.Id);
        var assigned = await qaLead.PutAsync<TestRunDto>(
            $"{Root}/runs/{run.Id}/items/{itemC.Id}/assignee", new AssignTestRunItemDto { AssignedUserId = productOwnerId });
        assigned.Items.Single(item => item.Id == itemC.Id).AssignedUserId.ShouldBe(productOwnerId);

        var passedAttempt = await qaLead.PostAsync<TestExecutionDto>(
            $"{Root}/runs/{run.Id}/items/{itemA.Id}/executions", new ExecuteTestItemDto { Status = TestResultStatus.Passed, DurationSeconds = 30 });
        passedAttempt.AttemptNumber.ShouldBe(1);

        var failedAttempt = await qaLead.PostAsync<TestExecutionDto>($"{Root}/runs/{run.Id}/items/{itemB.Id}/executions", new ExecuteTestItemDto
        {
            Status = TestResultStatus.Failed,
            ActualResult = "The card was declined twice",
            DurationSeconds = 45,
            Defects =
            {
                new AddDefectLinkDto
                {
                    ExternalSystem = "Jira", IssueKey = "BUG-1", IssueUrl = "https://jira.example.com/browse/BUG-1", Severity = SeverityLevel.Critical,
                },
            },
        });
        var bug1 = failedAttempt.DefectLinks.Single();
        bug1.IssueKey.ShouldBe("BUG-1");
        bug1.IsResolved.ShouldBeFalse();

        var skipped = await qaLead.PostAsync<List<TestExecutionDto>>($"{Root}/runs/{run.Id}/executions/batch", new BatchExecuteTestItemsDto
        {
            Items = { new BatchExecuteTestItemDto { TestRunItemId = itemC.Id, Status = TestResultStatus.Skipped, ActualResult = "Voucher service is offline" } },
        });
        skipped.Single().Status.ShouldBe(TestResultStatus.Skipped);

        var current = await qaLead.GetAsync<TestRunDto>($"{Root}/runs/{run.Id}");
        current.Status.ShouldBe(RunStatus.InProgress);
        (current.Summary.TotalItems, current.Summary.Passed, current.Summary.Failed, current.Summary.Skipped).ShouldBe((3, 1, 1, 1));
        var runs = await qaLead.GetAsync<PagedResultDto<TestRunDto>>(
            $"{Root}/runs?TestPlanId={plan.Id}&Status={(int)RunStatus.InProgress}&Environment=staging");
        runs.Items.Select(candidate => candidate.Id).ShouldBe(new[] { run.Id });
        (await qaLead.GetAsync<List<TestExecutionDto>>($"{Root}/runs/{run.Id}/items/{itemB.Id}/executions"))
            .Select(attempt => attempt.AttemptNumber).ShouldBe(new[] { 1 });

        // ---- Defects, addressed by execution (absolute routes) -----------------------------------------------
        var executionB = failedAttempt.Id;
        var gitHub = await qaLead.PostAsync<DefectLinkDto>(
            $"{Root}/executions/{executionB}/defects", new AddDefectLinkDto { ExternalSystem = "GitHub", IssueKey = "#7" });
        gitHub.Severity.ShouldBe(SeverityLevel.Medium, "an omitted severity defaults to the severity of the failing test case");
        (await qaLead.GetAsync<List<DefectLinkDto>>($"{Root}/executions/{executionB}/defects")).Count.ShouldBe(2);
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/executions/{executionB}/defects/{gitHub.Id}");
        (await qaLead.GetAsync<List<DefectLinkDto>>($"{Root}/executions/{executionB}/defects"))
            .Select(defect => defect.IssueKey).ShouldBe(new[] { "BUG-1" });

        var caseDefects = await qaLead.GetAsync<List<TestCaseDefectDto>>($"{Root}/test-cases/{caseB.Id}/defects");
        caseDefects.Single().IssueKey.ShouldBe("BUG-1");
        caseDefects.Single().TestRunTitle.ShouldBe("Sprint 1 regression");

        // ---- Traceability ------------------------------------------------------------------------------------
        var requirement = await qaLead.PostAsync<RequirementDto>($"{Root}/requirements", NewRequirement("REQ-PAY-01", milestone, acceptanceCriteria: null));
        await qaLead.SendAsync(HttpMethod.Post, $"{Root}/requirements/{requirement.Id}/test-cases", new LinkTestCasesDto { TestCaseIds = { caseA.Id, caseB.Id, caseC.Id } });
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/requirements/{requirement.Id}/test-cases/{caseC.Id}");
        var revised = await qaLead.PutAsync<RequirementDto>(
            $"{Root}/requirements/{requirement.Id}", NewRequirement("REQ-PAY-01", milestone, acceptanceCriteria: "A confirmed order exists"));
        revised.AcceptanceCriteria.ShouldBe("A confirmed order exists");
        (await qaLead.GetAsync<RequirementDto>($"{Root}/requirements/{requirement.Id}")).Code.ShouldBe("REQ-PAY-01");
        (await qaLead.GetAsync<PagedResultDto<RequirementDto>>($"{Root}/requirements?Filter=PAY&MilestoneId={milestone}"))
            .Items.Select(candidate => candidate.Code).ShouldBe(new[] { "REQ-PAY-01" });

        var rtmUrl = $"{Root}/rtm?MilestoneId={milestone}&TestPlanId={plan.Id}&Environment=Staging";
        var rtm = await qaLead.GetAsync<RtmMatrixDto>(rtmUrl);
        rtm.Summary.TotalRequirements.ShouldBe(1);
        rtm.Summary.FailedRequirements.ShouldBe(1);
        var row = rtm.Requirements.Single();
        row.Status.ShouldBe(RequirementCoverageStatus.Failed);
        row.TestCases.Count.ShouldBe(2);
        row.BlockingDefects.Select(defect => defect.IssueKey).ShouldBe(new[] { "BUG-1" });

        // ---- Quality gate: blocked by the open Critical defect ----------------------------------------------
        var gate = await qaLead.PostAsync<QualityGateDto>($"{Root}/quality-gates", NewGate("Release", description: null));
        (await qaLead.PutAsync<QualityGateDto>($"{Root}/quality-gates/{gate.Id}", NewGate("Release", description: "Gate for releases")))
            .Description.ShouldBe("Gate for releases");
        (await qaLead.GetAsync<QualityGateDto>($"{Root}/quality-gates/{gate.Id}")).IsDefault.ShouldBeTrue();
        (await qaLead.GetAsync<List<QualityGateDto>>($"{Root}/quality-gates")).Select(candidate => candidate.Name).ShouldContain("Release");

        var blocked = await qaLead.PostAsync<QualityGateEvaluationDto>($"{Root}/quality-gates/evaluate", new EvaluateQualityGateInput { TestPlanId = plan.Id });
        blocked.Passed.ShouldBeFalse();
        blocked.Gate.Name.ShouldBe("Release");
        blocked.Metrics.PassRate.ShouldBe(50m, "1 passed of 2 applicable items; the skipped one is left out");
        blocked.Criteria.Where(criterion => !criterion.Passed).Select(criterion => criterion.Code).ShouldBe(new[] { "OpenCriticalDefects" });

        var refused = await qaLead.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/sign-off", new StartSignOffDto { TestPlanId = plan.Id, ApproverRole = "QA Lead" });
        refused.Status.ShouldBe(HttpStatusCode.Forbidden);
        ((string?)refused.Error["code"]).ShouldBe(TestCaseManagementErrorCodes.QualityGateNotPassed);
        // ABP exposes `details` only for user-friendly exceptions; the failed criteria travel in the message and in `data`.
        ((string?)refused.Error["message"]).ShouldNotBeNull().ShouldContain("OpenCriticalDefects 1 (required <= 0)");
        ((string?)refused.Error["data"]?["GateName"]).ShouldBe("Release");
        ((string?)refused.Error["data"]?["FailedCriteria"]).ShouldBe("OpenCriticalDefects 1 (required <= 0)");

        // ---- The defect is fixed: the gate opens and two users sign off --------------------------------------
        var resolved = await qaLead.PutAsync<DefectLinkDto>(
            $"{Root}/executions/{executionB}/defects/{bug1.Id}", new UpdateDefectLinkDto { Severity = SeverityLevel.Critical, IsResolved = true });
        resolved.IsResolved.ShouldBeTrue();
        resolved.ResolvedTime.ShouldNotBeNull();
        (await qaLead.GetAsync<RtmMatrixDto>(rtmUrl)).Requirements.Single().BlockingDefects.ShouldBeEmpty();

        var passing = await qaLead.PostAsync<QualityGateEvaluationDto>($"{Root}/quality-gates/evaluate", new EvaluateQualityGateInput { TestPlanId = plan.Id });
        passing.Passed.ShouldBeTrue();
        passing.Criteria.ShouldAllBe(criterion => criterion.Passed);

        (await qaLead.PostAsync<TestRunDto>($"{Root}/runs/{run.Id}/complete")).Status.ShouldBe(RunStatus.Completed);

        var pending = await qaLead.PostAsync<SignOffReportDto>(
            $"{Root}/sign-off", new StartSignOffDto { TestPlanId = plan.Id, ApproverRole = "QA Lead", Comment = "Regression complete" });
        pending.Status.ShouldBe(SignOffStatus.Pending);
        pending.Approvals.Single().ApproverUserId.ShouldBe(qaLeadId);
        pending.Approvals.Single().ApproverName.ShouldBe("qa.lead");
        pending.SnapshotHash.Length.ShouldBe(SignOffConsts.HashLength);
        pending.Summary.ShouldNotBeNull().Passed.ShouldBeTrue();

        var duplicate = await qaLead.SendExpectingErrorAsync(HttpMethod.Post, $"{Root}/sign-off/{pending.Id}/approvals", new ApproveSignOffDto { ApproverRole = "QA Lead" });
        ((string?)duplicate.Error["code"]).ShouldBe(TestCaseManagementErrorCodes.DuplicateSignOffApproval);

        var approved = await productOwner.PostAsync<SignOffReportDto>(
            $"{Root}/sign-off/{pending.Id}/approvals", new ApproveSignOffDto { ApproverRole = "Product Owner", Comment = "Accepted" });
        approved.Status.ShouldBe(SignOffStatus.Approved);
        approved.Approvals.Select(approval => approval.ApproverName).ShouldBe(new[] { "qa.lead", "product.owner" });
        approved.IntegrityVerified.ShouldBeTrue();

        (await qaLead.GetAsync<SignOffReportDto>($"{Root}/sign-off/{pending.Id}")).SnapshotHash.ShouldBe(pending.SnapshotHash);
        (await qaLead.GetAsync<PagedResultDto<SignOffReportDto>>($"{Root}/sign-off?TestPlanId={plan.Id}&Status={(int)SignOffStatus.Approved}"))
            .Items.Select(report => report.Id).ShouldBe(new[] { pending.Id });

        (await qaLead.SendExpectingErrorAsync(HttpMethod.Delete, $"{Root}/plans/{plan.Id}")).Status.ShouldBe(HttpStatusCode.Forbidden);

        // ---- Deleting throw-away data ------------------------------------------------------------------------
        var scratchSuite = await qaLead.PostAsync<TestSuiteDto>($"{Root}/suites", new CreateTestSuiteDto { Name = "Scratch" });
        var scratchCase = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", NewTestCase(scratchSuite.Id, "SCR-001", "Scratch"));
        var scratchPlan = await qaLead.PostAsync<TestPlanDto>($"{Root}/plans", NewPlan("Scratch plan", milestoneId: null));
        var scratchRequirement = await qaLead.PostAsync<RequirementDto>($"{Root}/requirements", NewRequirement("REQ-SCR-01", milestoneId: null, acceptanceCriteria: null));
        var scratchGate = await qaLead.PostAsync<QualityGateDto>($"{Root}/quality-gates", NewGate("Scratch gate", description: null, isDefault: false));

        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/test-cases/{scratchCase.Id}");
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/suites/{scratchSuite.Id}");
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/plans/{scratchPlan.Id}");
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/requirements/{scratchRequirement.Id}");
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/quality-gates/{scratchGate.Id}");
        foreach (var url in new[]
                 {
                     $"{Root}/test-cases/{scratchCase.Id}", $"{Root}/suites/{scratchSuite.Id}", $"{Root}/plans/{scratchPlan.Id}",
                     $"{Root}/requirements/{scratchRequirement.Id}", $"{Root}/quality-gates/{scratchGate.Id}",
                 })
        {
            (await qaLead.SendExpectingErrorAsync(HttpMethod.Get, url)).Status.ShouldBe(HttpStatusCode.NotFound, url);
        }

        // ---- Import and export (their behaviour is covered by HttpApiTransfer_Tests; here they are part of the cycle) -----
        (await qaLead.DownloadAsync($"{Root}/test-cases/export?Format=Csv")).Bytes.ShouldNotBeEmpty();
        var dryRun = new Dictionary<string, string> { ["DryRun"] = "true" };
        (await qaLead.UploadAsync<ImportReportDto>(
                $"{Root}/test-cases/import", "flow.csv", Encoding.UTF8.GetBytes("Suite,Code,Title\nCheckout flows,PAY-001,Pay with a valid card\n"), dryRun))
            .Skipped.ShouldBe(1);
        (await qaLead.DownloadAsync($"{Root}/runs/{run.Id}/results/export?format=Csv")).Bytes.ShouldNotBeEmpty();
        (await qaLead.UploadAsync<ImportReportDto>(
                $"{Root}/runs/{run.Id}/results/import", "flow.csv", Encoding.UTF8.GetBytes("Code,Result\nPAY-001,Passed\n"), dryRun))
            .DryRun.ShouldBeTrue();

        // ---- Automation: a key for the pipeline, its results, and the key revoked (see HttpApiAutomation_Tests) --------------
        var automated = await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases", new CreateUpdateTestCaseDto
        {
            SuiteId = suite.Id, Code = "PAY-AUTO", Title = "An automated test", AutomationId = "flow.automated",
            Steps = { new TestStepDto { Action = "Run the script", ExpectedResult = "It passes" } },
        });
        await qaLead.PostAsync<TestCaseDto>($"{Root}/test-cases/{automated.Id}/status", new ChangeTestCaseStatusDto { TargetStatus = TestCaseStatus.Approved });
        var apiKey = await qaLead.PostAsync<ApiKeyCreatedDto>($"{Root}/api-keys", new CreateApiKeyDto { Name = "Release cycle" });
        (await qaLead.GetAsync<List<ApiKeyDto>>($"{Root}/api-keys")).ShouldContain(candidate => candidate.Id == apiKey.Id);
        var pipeline = ApiClient.WithApiKey(_host, apiKey.Key, calls);
        var published = await pipeline.PostAsync<PublishAutomationResultsDto>($"{Root}/automation/results", new PublishAutomationResultsInput
        {
            Run = new AutomationRunInput { Title = "Pipeline run", Environment = "CI" },
            Results = { new AutomationResultInput { AutomationId = "flow.automated", Status = TestResultStatus.Passed, DurationSeconds = 3 } },
        });
        published.Recorded.ShouldBe(1);
        (await qaLead.PostAsync<ApiKeyDto>($"{Root}/api-keys/{apiKey.Id}/revoke")).IsActive.ShouldBeFalse();

        // ---- Insights: the flaky list and the dashboard (see HttpApiInsights_Tests for the detail) ------------------------
        (await qaLead.GetAsync<FlakyTestListDto>($"{Root}/flaky-tests")).Settings.WindowSize.ShouldBeGreaterThan(0);
        (await qaLead.PostAsync<ApplyFlakyFlagsResultDto>($"{Root}/flaky-tests/apply", new ApplyFlakyFlagsInput { ClearRecovered = true })).Flagged.ShouldBe(0);
        (await qaLead.GetAsync<DashboardDto>($"{Root}/dashboard?Days=7")).Velocity.Points.Count.ShouldBe(7);

        // ---- Attachments: a file on the test case, listed, downloaded and deleted (see HttpApiAttachments_Tests) --------------
        var attached = await qaLead.UploadAsync<AttachmentDto>($"{Root}/attachments", "evidence.txt", Encoding.UTF8.GetBytes("evidence"), new Dictionary<string, string>
        {
            ["OwnerType"] = ((int)AttachmentOwnerType.TestCase).ToString(), ["OwnerId"] = automated.Id.ToString(),
        });
        (await qaLead.GetAsync<List<AttachmentDto>>($"{Root}/attachments?OwnerType=0&OwnerIds={automated.Id}")).Single().Id.ShouldBe(attached.Id);
        (await qaLead.DownloadAsync($"{Root}/attachments/{attached.Id}/content")).Bytes.ShouldBe(Encoding.UTF8.GetBytes("evidence"));
        await qaLead.SendAsync(HttpMethod.Delete, $"{Root}/attachments/{attached.Id}");

        await AssertEveryOperationWasCalledAsync(calls);
    }

    private async Task AssertEveryOperationWasCalledAsync(ConcurrentBag<(HttpMethod Method, string Path)> calls)
    {
        using var client = _host.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;

        var operations = (
            from path in document["paths"]!.AsObject()
            from verb in path.Value!.AsObject()
            select new
            {
                Id = (string)verb.Value!["operationId"]!,
                Method = verb.Key.ToUpperInvariant(),
                Template = path.Key,
                Pattern = new Regex("^" + Regex.Replace(Regex.Escape(path.Key), @"\\\{\w+\}", "[^/]+") + "$"),
                Placeholders = Regex.Matches(path.Key, @"\{\w+\}").Count,
            }).ToList();

        var covered = new HashSet<string>();
        foreach (var (method, path) in calls)
        {
            // A literal route such as /suites/tree also fits /suites/{id}; the most specific template is the one called.
            var target = operations
                .Where(operation => operation.Method == method.Method && operation.Pattern.IsMatch(path))
                .OrderBy(operation => operation.Placeholders)
                .FirstOrDefault();

            if (target is not null)
            {
                covered.Add(target.Id);
            }
        }

        operations.Select(operation => operation.Id).Except(covered).ShouldBeEmpty("these operations were not called by the flow");
    }

    private static CreateUpdateTestCaseDto NewTestCase(Guid suiteId, string code, string title, PriorityLevel priority = PriorityLevel.Medium)
    {
        return new CreateUpdateTestCaseDto
        {
            SuiteId = suiteId,
            Code = code,
            Title = title,
            Priority = priority,
            Steps =
            {
                new TestStepDto { Action = "Open the checkout page", ExpectedResult = "The page is shown" },
                new TestStepDto { Action = "Pay", ExpectedResult = "The order is confirmed", TestData = "4111 1111 1111 1111" },
            },
        };
    }

    private static CreateTestPlanDto NewPlan(string name, Guid? milestoneId)
    {
        return new CreateTestPlanDto
        {
            Name = name, MilestoneId = milestoneId, StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 15),
        };
    }

    private static CreateUpdateRequirementDto NewRequirement(string code, Guid? milestoneId, string? acceptanceCriteria)
    {
        return new CreateUpdateRequirementDto
        {
            Code = code,
            Title = "Customers can pay by card",
            Priority = PriorityLevel.High,
            MilestoneId = milestoneId,
            AcceptanceCriteria = acceptanceCriteria,
        };
    }

    private static CreateUpdateQualityGateDto NewGate(string name, string? description, bool isDefault = true)
    {
        return new CreateUpdateQualityGateDto
        {
            Name = name, Description = description, MinPassRate = 50m, RequiredApprovals = 2, IsDefault = isDefault,
        };
    }
}
