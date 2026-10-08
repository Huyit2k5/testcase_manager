# Implementation Plan: Test Case Management (TCM) Reusable Module

**Branch**: `001-test-case-management` | **Date**: 2026-10-06 | **Spec**: [spec.md](file:///d:/quanlytestcase/specs/001-test-case-management/spec.md)  
**Input**: Feature specification from `/specs/001-test-case-management/spec.md` and project rules from `/.specify/memory/constitution.md`

---

## 1. Technical Context & Architecture Decision Records (ADR)

- **Platform & Language**: .NET 10 (LTS), C# 14
- **Framework**: ABP Framework (v10.x, pinned to 10.6.1) - Application Module structure, scaffolded manually (abp CLI 3.1.0 has no `module` template)
- **Architectural Pattern**: Domain-Driven Design (DDD), Clean Architecture, CQRS-lite via ABP Application Services
- **Data Access & ORM**: Entity Framework Core (Code-First Migrations, PostgreSQL & SQL Server support)
- **Primary Data Formats**:
  - Structured SQL tables for relational entities
  - JSON columns for test step snapshots (`StepsJson`) and sign-off summary stats (`SummaryStatsJson`)
- **Testing Frameworks**: `xUnit`, `Shouldly`, `NSubstitute`, `Volo.Abp.TestBase`, `Volo.Abp.EntityFrameworkCore.Sqlite` (for in-memory integration tests), `Microsoft.AspNetCore.Mvc.Testing` (HTTP-level tests of the sample host, see 4.6)
- **External Integration Points**:
  - REST Webhooks for CI/CD test runners (Automation results ingestion)
  - Jira / GitHub Issue reference adapters

---

## 2. Solution & Project Directory Structure

```text
Acme.TestCaseManagement/
├── src/
│   ├── Acme.TestCaseManagement.Domain.Shared/
│   │   ├── Enums/
│   │   │   ├── PriorityLevel.cs           (Low, Medium, High, Urgent)
│   │   │   ├── SeverityLevel.cs           (Low, Medium, High, Critical)
│   │   │   ├── TestCaseStatus.cs          (Draft, UnderReview, Approved, Deprecated)
│   │   │   ├── TestResultStatus.cs        (Untested, Passed, Failed, Blocked, Skipped)
│   │   │   ├── ExecutionType.cs           (Manual, Automated, Hybrid)
│   │   │   ├── TestKind.cs                (Functional, Performance, Security, Usability)
│   │   │   └── TestLayer.cs               (Unit, Integration, E2E, Acceptance)
│   │   ├── Localization/
│   │   │   └── TestCaseManagementResource.cs
│   │   └── TestCaseManagementErrorCodes.cs
│   │
│   ├── Acme.TestCaseManagement.Domain/
│   │   ├── Suites/
│   │   │   ├── TestSuite.cs               (Aggregate Root)
│   │   │   └── TestSuiteManager.cs        (Domain Service - hierarchy & cycle detection)
│   │   ├── TestCases/
│   │   │   ├── TestCase.cs                (Aggregate Root)
│   │   │   ├── TestStep.cs                (Entity)
│   │   │   ├── TestCaseVersion.cs         (Immutable Snapshot Entity)
│   │   │   └── TestCaseManager.cs         (Domain Service - versioning logic)
│   │   ├── Plans/
│   │   │   └── TestPlan.cs                (Aggregate Root)
│   │   ├── Runs/
│   │   │   ├── TestRun.cs                 (Aggregate Root)
│   │   │   ├── TestRunItem.cs             (Entity)
│   │   │   ├── TestExecution.cs           (Append-only Attempt Entity)
│   │   │   └── TestRunManager.cs          (Domain Service - attempt calculation & status rollup)
│   │   ├── Requirements/
│   │   │   ├── Requirement.cs             (Aggregate Root)
│   │   │   └── RequirementTestCase.cs     (N-N Join Entity)
│   │   ├── Quality/
│   │   │   ├── QualityGate.cs             (Aggregate Root)
│   │   │   ├── SignOffReport.cs           (Aggregate Root)
│   │   │   └── DefectLink.cs              (Entity)
│   │   └── Repositories/
│   │       ├── ITestCaseRepository.cs
│   │       ├── ITestRunRepository.cs
│   │       └── IRtmRepository.cs
│   │
│   ├── Acme.TestCaseManagement.Application.Contracts/
│   │   ├── Permissions/
│   │   │   ├── TestCaseManagementPermissions.cs
│   │   │   └── TestCaseManagementPermissionDefinitionProvider.cs
│   │   ├── TestCases/
│   │   │   ├── Dtos/ (TestCaseDto, CreateUpdateTestCaseDto, TestStepDto, TestCaseVersionDto)
│   │   │   └── ITestCaseAppService.cs
│   │   ├── Suites/
│   │   │   ├── Dtos/ (TestSuiteDto, CreateTestSuiteDto, TestSuiteTreeDto)
│   │   │   └── ITestSuiteAppService.cs
│   │   ├── Runs/
│   │   │   ├── Dtos/ (TestRunDto, CreateTestRunDto, ExecuteTestItemDto, TestExecutionDto)
│   │   │   └── ITestRunAppService.cs
│   │   ├── Plans/
│   │   │   ├── Dtos/ (TestPlanDto, CreateTestPlanDto)
│   │   │   └── ITestPlanAppService.cs
│   │   └── Rtm/
│   │       ├── Dtos/ (RtmMatrixDto, RequirementCoverageDto)
│   │       └── IRtmAppService.cs
│   │
│   ├── Acme.TestCaseManagement.Application/
│   │   ├── TestCases/
│   │   │   └── TestCaseAppService.cs
│   │   ├── Suites/
│   │   │   └── TestSuiteAppService.cs
│   │   ├── Runs/
│   │   │   └── TestRunAppService.cs
│   │   ├── Plans/
│   │   │   └── TestPlanAppService.cs
│   │   ├── Rtm/
│   │   │   └── RtmAppService.cs
│   │   └── TestCaseManagementApplicationAutoMapperProfile.cs
│   │
│   ├── Acme.TestCaseManagement.EntityFrameworkCore/
│   │   ├── EntityFrameworkCore/
│   │   │   ├── ITestCaseManagementDbContext.cs
│   │   │   ├── TestCaseManagementDbContext.cs
│   │   │   ├── TestCaseManagementDbContextModelCreatingExtensions.cs
│   │   │   └── Repositories/ (Custom EF Core Repositories)
│   │
│   └── Acme.TestCaseManagement.HttpApi/
│       └── Controllers/
│           ├── TestCaseController.cs
│           ├── TestSuiteController.cs
│           ├── TestRunController.cs
│           ├── TestPlanController.cs
│           └── RtmController.cs
│
├── host/                                          (Phase 8: sample host, never packed)
│   └── Acme.TestCaseManagement.HttpApi.Host/      (ABP + Swashbuckle, SQLite, development-only sign-in)
│
├── build/
│   └── build.ps1                                  (restore, build, test, pack)
│
├── test/
│   ├── Acme.TestCaseManagement.TestBase/          (shared ABP test module: Autofac + SQLite in-memory)
│   ├── Acme.TestCaseManagement.Domain.Tests/
│   │   ├── Suites/TestSuiteManager_Tests.cs
│   │   ├── TestCases/TestCase_Versioning_Tests.cs
│   │   └── Runs/TestRun_MultiAttempt_Tests.cs
│   ├── Acme.TestCaseManagement.Application.Tests/
│   │   ├── TestCaseAppService_Tests.cs
│   │   └── TestRunAppService_Tests.cs
│   └── Acme.TestCaseManagement.HttpApi.Tests/     (Phase 8: OpenAPI contract and HTTP flow against the sample host)
│
└── README.md, global.json, Directory.Build.props, Directory.Packages.props, src/Directory.Build.props, ...
```

The listing above shows the original design; later phases added further files inside the same folders (see the notes in section 4).

---

## 3. Core Domain Entities & Database Schema Mapping

### 3.1. Master Library (Design-Time)

```csharp
public class TestCase : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid SuiteId { get; set; }
    public string Code { get; set; }           // Unique within Tenant (e.g. TC-001)
    public string Title { get; set; }
    public string? Description { get; set; }
    public string? Preconditions { get; set; }
    public string? Postconditions { get; set; }
    public PriorityLevel Priority { get; set; }
    public SeverityLevel Severity { get; set; }
    public TestCaseStatus Status { get; set; }
    public ExecutionType ExecutionType { get; set; }
    public TestKind Kind { get; set; }          // added in Phase 3 (spec FR-002)
    public TestLayer Layer { get; set; }        // added in Phase 3 (spec FR-002)
    public string? AutomationId { get; set; }
    public bool IsFlaky { get; set; }
    public int CurrentVersion { get; set; }

    public Collection<TestStep> Steps { get; set; } = new();
}

public class TestStep : Entity<Guid>
{
    public Guid TestCaseId { get; set; }
    public int StepOrder { get; set; }
    public string Action { get; set; }
    public string ExpectedResult { get; set; }
    public string? TestData { get; set; }
}

public class TestCaseVersion : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid TestCaseId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; }
    public string? Preconditions { get; set; }
    public string StepsJson { get; set; }       // Serialized List<TestStep>
    public string? Postconditions { get; set; }
    public string? ChangeSummary { get; set; }
}
```

### 3.2. Execution Cycle (Runtime)

```csharp
public class TestPlan : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public string Name { get; set; }
    public Guid? MilestoneId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public PlanStatus Status { get; set; }
}

public class TestRun : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid? TestPlanId { get; set; }
    public string Title { get; set; }
    public string Environment { get; set; }     // "Staging", "Production", "iOS 17"
    public Guid? AssignedToUserId { get; set; }
    public RunStatus Status { get; set; }

    public Collection<TestRunItem> Items { get; set; } = new();
}

public class TestRunItem : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid TestRunId { get; set; }
    public Guid TestCaseVersionId { get; set; } // Frozen snapshot reference
    public Guid? AssignedUserId { get; set; }
    public TestResultStatus CurrentStatus { get; set; }

    public Collection<TestExecution> Executions { get; set; } = new();
}

public class TestExecution : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }
    public Guid TestRunItemId { get; set; }
    public int AttemptNumber { get; set; }      // 1, 2, 3...
    public TestResultStatus Status { get; set; }
    public string? ActualResult { get; set; }
    public int DurationSeconds { get; set; }
    public string? DefectKey { get; set; }      // e.g. "JIRA-102"
}
```

---

## 4. Key Domain Services & Business Logic

1. **`TestSuiteManager.ValidateParentHierarchyAsync(Guid suiteId, Guid newParentId)`**:
   - Traverses ancestors using recursive query.
   - Throws `CircularSuiteDependencyException` if `newParentId` is equal to `suiteId` or any of its descendants.

2. **`TestCaseManager.PublishNewVersionAsync(TestCase testCase, string changeSummary)`**:
   - Increments `testCase.CurrentVersion`.
   - Serializes existing steps to JSON.
   - Inserts new immutable `TestCaseVersion` record.

3. **`TestRunManager.RecordExecutionAttemptAsync(Guid runItemId, TestResultStatus status, string actualResult, int duration)`**:
   - Queries existing `Executions` for `runItemId`.
   - Computes `nextAttempt = max(AttemptNumber) + 1`.
   - Adds new `TestExecution(attemptNumber: nextAttempt, status, actualResult, duration)`.
   - Updates `TestRunItem.CurrentStatus = status`.
   - Recalculates `TestRun.Status` and rolls up progress percentage.

---

### 4.1. Phase 3 implementation notes

- Entities expose `protected set`; state changes go through methods (`SetDetails`, `SetSteps`, `ReorderSteps`) or
  `internal` members used only by the domain services (`SetStatus`, `IncrementVersion`).
- Allowed status transitions: Draft→UnderReview/Approved, UnderReview→Draft/Approved, Approved→Draft/Deprecated,
  Deprecated→Draft. Reaching `Approved` requires ≥ 1 step and publishes a `TestCaseVersion`.
- Per spec FR-004, modifying an `Approved` test case (update or step reorder) publishes a new version automatically.
- `TestCaseManager.CreateAsync` / `TestSuiteManager.CreateAsync` return unsaved entities; app services insert them.
- Test case code uniqueness is enforced by `TestCaseManager` (non-unique DB index, because soft-deleted rows keep their code).
- `ITestCaseRepository` (custom EF Core repository) provides filtered/paged queries; sorting is whitelisted in the app service.
- Error codes use the `TestCaseManagement:` namespace (mapped to the localization resource) instead of the `TCM:0001:` form in the spec.

### 4.2. Phase 4 implementation notes

- `PlanStatus` = Draft, Active, Completed, Archived (transitions: Draft→Active/Archived, Active→Completed/Archived, Completed→Archived).
  `RunStatus` = Planned, InProgress, Completed. The first attempt moves Planned→InProgress; a lead closes a run explicitly
  (`Complete`), after which items and attempts are rejected.
- `TestRunItem` has no `Executions` navigation and `TestExecution` has no `DefectKey`: attempts are inserted through their own
  repository so the aggregate can never rewrite them, and defects are linked through the `DefectLink` entity (Phase 5).
  `TestRunItem.Sequence` keeps a stable display order. `TestExecution.CreatorId` is the executing tester.
- `TestRunManager.RecordExecutionAttemptAsync` inserts the attempt with `autoSave` so that consecutive attempts in one unit of
  work (batch execution) get consecutive numbers. A unique index on `(TestRunItemId, AttemptNumber)` stops concurrent testers
  from creating the same attempt.
- Items are added from the **approved** library only and bind to `TestCase.CurrentVersion` at that moment
  (`TestRunManager.AddTestCaseAsync`). A unique index on `(TestRunId, TestCaseVersionId)` prevents duplicates.
- Run metrics (`TestRunMetrics`) are derived, never stored: status counts, completion %, and First-Time Pass Rate (share of executed
  items whose attempt #1 passed; null until something is executed).
- Permissions: reading runs needs `TestRuns.Default`; recording results needs `TestRuns.Execute`; creating runs, adding items,
  assigning testers and completing a run need `TestPlans.Manage`.
- Both the interface and the class of the module DbContext resolve to one instance per unit of work
  (`ReplaceDbContext<ITestCaseManagementDbContext>()`); without it custom and default repositories used separate contexts.

### 4.3. Phase 5 implementation notes

- `DefectLink` is a `FullAuditedEntity<Guid>` (soft-deletable) with `TestExecutionId`, `ExternalSystem` (free text such as "Jira"),
  `IssueKey` and an optional absolute http(s) `IssueUrl`. It is a separate row, so linking never touches the append-only execution.
- Only **Failed** executions can carry a defect (spec US3 scenario 1). A defect can also be supplied while executing
  (`ExecuteTestItemDto.Defects`, including batch entries); it is rejected up front for any other result.
- The same system + key (ignoring case) can be linked to an execution once. Uniqueness is checked by `DefectLinkManager`,
  not by a DB index, because soft-deleted links keep their key.
- `GET test-cases/{id}/defects` lists every defect across all runs and versions of a test case, oldest execution first
  (spec US3 scenario 2), through `IDefectLinkRepository.GetListByTestCaseAsync`.
- Routes: `POST|GET /api/test-case-management/executions/{executionId}/defects` and
  `DELETE .../defects/{defectLinkId}` (the delete route is an addition so a mistyped key can be corrected).
- Updated in Phase 7: `DefectLink` now has `Severity` and `IsResolved`/`ResolvedTime` (see 4.5). `AddDefectLinkDto.Severity` is
  optional and defaults to the severity of the failing test case; `PUT .../defects/{defectLinkId}` changes severity or resolves /
  reopens a defect.

### 4.4. ADR: how the RTM computes "covered" and "passed" (Phase 6)

The spec asks for "Total Requirements, Covered Requirements (%), Passed Requirements" but does not say when a requirement
counts as passed when it has several test cases. The rules below follow ISTQB coverage terminology and the behaviour of
Xray, AgileTest, Zephyr Scale, TestRail and Azure DevOps, with the deliberate differences listed at the end.

**Two separate questions, two separate numbers**

| Metric | Definition | Basis |
|---|---|---|
| **Covered** (design coverage) | Requirement has at least one linked, non-deprecated test case. Coverage % = covered / total requirements. | ISTQB: coverage = coverage items exercised / total items, as a percentage; Xray/TestRail: "covered" means linked to tests, independent of results |
| **Passed** (verification) | Calculated status is Passed. Passed % = passed requirements / **total** requirements (uncovered ones count in the denominator). | Xray "OK"; Azure DevOps requirements quality (pass rate per requirement) |

**Step 1: result of one test case.** Only *executed* evidence counts (a run item that is still Untested is ignored, so an
older result is not erased by a newly scheduled retest).
1. For every run item of the test case (any version), take its **latest attempt**.
2. Group by **environment** (case-insensitive) and keep the most recent attempt of each environment.
3. Combine the environments, worst first: Failed, then Blocked, then Passed, then Skipped. No evidence = Untested.
   (A test that fails on Safari but passes on Chrome is not passed. Xray combines environments the same way: FAIL wins.)

**Step 2: status of a requirement** from its linked, non-deprecated test cases (first matching rule wins):

| # | Condition | Status |
|---|---|---|
| 1 | No linked, non-deprecated test case | **Uncovered** |
| 2 | Any test case Failed | **Failed** (Xray NOK) |
| 3 | Any test case Untested (not executed yet, or still Draft/UnderReview) | **NotRun** (Xray NOTRUN) |
| 4 | Any test case Blocked | **Blocked** (Xray UNKNOWN / AgileTest UNKNOWN) |
| 5 | At least one Passed and every other one Skipped | **Passed** (Xray OK) |
| 6 | All test cases Skipped | **NotRun** |

Precedence Failed > NotRun > Blocked matches AgileTest (NOK > NOT RUN > UNKNOWN > OK).

**Blocking open defects of a requirement** = the **unresolved** defect links (`IsResolved = false`) on executions of its linked,
non-deprecated test cases, within the scope filters. *Revised in Phase 7:* the first version derived "blocking" from the current
Failed result, which lost a defect as soon as the next attempt failed for another reason (BUG-1 on attempt 1, BUG-2 on attempt 2
left only BUG-2) and hid defects whose fix had not been confirmed. The same definition of "open" is now used by the quality gate (4.5).
A passing re-test therefore does not clear a defect; marking it resolved does.

**Scope filters**: the matrix can be restricted by `TestPlanId` and `Environment` (Xray calculates per version / test plan /
environment too) and by `MilestoneId` of the requirement.

**Deliberate differences from the tools**
- Skipped is neutral instead of passing (AgileTest treats Skipped as OK): a requirement whose tests were all skipped has no
  verifying evidence, so it is NotRun, not Passed.
- Deprecated test cases are ignored, so retiring an obsolete test cannot leave a requirement "covered".
- Requirement hierarchies (Xray sub-requirements) are not modelled; requirements are a flat list.
- Results are not tied to the test case version. A pass recorded on v1 still counts after v2 is published; the matrix shows the
  last execution time so staleness is visible. Version-scoped calculation can be added later (Xray offers it).

**Implementation notes (Phase 6)**
- The rules live in `RequirementCoverageCalculator` (pure, no dependencies, covered by table-driven tests); `RtmAppService` only
  loads data and calls it. `IRtmRepository.GetLatestResultsAsync` returns each run item's latest attempt in one query.
- `RequirementTestCase` has the composite key (RequirementId, TestCaseId) as T037 asks, and is soft-deletable to respect
  constitution VI: unlinking soft-deletes the row, linking again restores it.
- `RequirementAppService` (create/edit/delete requirements, link/unlink test cases) was added although tasks T036-T040 do not
  list it: without it the RTM could not be fed through the API, and the US4 independent test needs it.
- RTM endpoint: `GET /api/test-case-management/rtm` with filters `milestoneId`, `filter`, `testPlanId`, `environment`, `status`
  and paging. The summary covers all requirements matching the milestone/text filters and ignores status filter and paging.

Sources: Xray [Understanding coverage and the calculation of Test and requirement statuses](https://docs.getxray.app/display/XRAY/Understanding+coverage+and+the+calculation+of+Test+and+requirement+statuses);
AgileTest [test case calculation scope](https://docs.devsamurai.com/agiletest/test-case-calculation-scope);
Zephyr Scale [reports overview](https://support.smartbear.com/tm4j-cloud/docs/reports-and-analysis/reports-overview.html);
TestRail [traceability and coverage](https://www.testrail.com/blog/traceability-test-coverage-in-testrail/);
Azure DevOps [requirements traceability](https://learn.microsoft.com/en-us/azure/devops/pipelines/test/requirements-traceability);
ISTQB glossary "coverage" / "coverage item" (via [imbus glossary](https://www.imbus.de/en/glossar/term/coverage)).

### 4.5. ADR: quality gate, release metrics and sign-off (Phase 7)

The spec gives the gate examples ("Pass Rate >= 95%", "0 Critical unresolved bugs", "100% of P1 executed") and the constitution
(principle V) makes them mandatory, but neither defines the formulas. This section fixes them.

**What is evaluated.** All run items of the test runs that belong to the chosen **test plan**, or to **every plan of a milestone**
(`TestPlan.MilestoneId`). Each item counts once with its **current status** (result of its latest attempt), so a failure that was
fixed and re-tested counts as Passed. An item is one test case in one run, so a test case planned on two environments counts twice.

| Metric | Definition |
|---|---|
| Applicable items | all items minus Skipped (Skipped means "not applicable", as the "Not executed / Not applicable" outcomes of Azure DevOps) |
| **Pass rate** | Passed / applicable items x 100. Failed, Blocked and Untested items all count against it. |
| **P1 executed** | P1 = test case priority `Urgent`. Executed = latest status Passed or Failed. Blocked, Skipped and Untested P1 items are **not** executed. Rate = executed P1 / all P1 items; no P1 items = 100% (nothing to execute). |
| **Open defects** | Distinct issues (system + key, ignoring case) with at least one unresolved link in scope, counted at the highest severity among their open links. |
| First-time pass rate | Informational only: executed items whose attempt #1 passed. |

Both pass-rate formulas exist in practice (passed/total, e.g. [ACCELQ](https://www.accelq.com/blog/software-testing-metrics), and
passed/executed). Passed/applicable is the conservative one: leaving tests unexecuted cannot raise the rate, which would otherwise
let a plan with 5 of 100 tests run reach 100%. Rates are shown **rounded down** to two decimals and the gate compares the exact
ratio, so a value that fails is never displayed as equal to the threshold.

**Gate criteria** (all must pass, like a SonarQube quality gate where any failing condition fails the gate):

| Code | Rule | Configurable |
|---|---|---|
| `PassRate` | pass rate >= `MinPassRate` (no applicable items = fails) | yes, per gate, 0.01-100, default 95 |
| `P1Executed` | executed P1 items = all P1 items | no (constitution V) |
| `OpenCriticalDefects` | open Critical defects = 0 | no (constitution V) |
| `OpenHighDefects` | open High defects = 0 | no (constitution V) |

The constitution says Critical/High unresolved defects "must be 0", so those limits are rules, not settings. Making them
configurable is a one-field change if the constitution is amended. A gate also carries `RequiredApprovals` (default 2: QA Lead and
Product Owner in the spec) and `IsDefault`. When no gate is chosen, the default gate of the tenant is used; when there is none, the
built-in baseline (95%, 2 approvals) applies, so sign-off is never unguarded. There is no override: the spec's "documented
management override" is not covered by any acceptance scenario, task or permission, and a gate that can be waived is not deterministic.

**Defects now have a state.** `DefectLink` gets `Severity` and `IsResolved` (+ `ResolvedTime`). Severity defaults to the severity of
the failing test case when not supplied (the safe default: a forgotten severity can never hide a critical bug). Open means
`IsResolved = false`. The earlier derivation of "blocking" from the latest result is replaced everywhere (RTM included), because it
loses a defect as soon as the next attempt fails for another reason.

**Sign-off workflow** (`SignOffReport`, status Pending, Approved or Superseded):
1. `POST sign-off` by a user with `SignOff.Approve` evaluates the gate. If it fails the call is rejected with the failed criteria
   (error `QualityGateNotPassed`). If it passes, a report is created with the **frozen snapshot**, i.e. the complete evaluation as JSON,
   the SHA-256 of that JSON, and the caller's approval.
2. Other users call `POST sign-off/{id}/approvals`. Each user can approve once. Before an approval is accepted the gate is evaluated
   again with the thresholds frozen in the report; if it fails now, the approval is rejected (a defect found after the first signature
   cannot be signed over).
3. When approvals reach `RequiredApprovals` the report becomes Approved. Starting a new sign-off for the same scope supersedes a
   Pending report (kept for history); Approved reports are never changed.
4. Every approval stores user, role label, comment, time and a SHA-256 digest of (report, snapshot hash, user, role, comment, time).
   `IntegrityVerified` recomputes all digests. These are integrity digests, not asymmetric signatures.

The snapshot mirrors the test completion report of ISO/IEC/IEEE 29119-3 (summary of testing performed, test completion evaluation
against the exit criteria, factors that blocked progress, test measures, residual risks, approval authority): metrics, criteria
results, blocked count, open defects of every severity (the residual risks), and approvals. See the
[standard](https://iteh.eu/catalog/standards/iso/b4f42a41-dd8f-446c-9674-238ba5dc90f1/iso-iec-ieee-29119-3-2021); the element list
above is taken from secondary summaries, not from the standard's text.

**Implementation notes (Phase 7)**
- The rules are pure code with no dependencies: `QualityMetricsCalculator` (metrics) and `QualityGateEvaluator` (criteria), both
  covered by table-driven tests. `QualityGateManager` only resolves the gate and the plans, loads
  `IQualityRepository.GetScopeDataAsync` and calls them. `SignOffManager` creates and approves reports.
- Items of a soft-deleted library test case stay in the evaluated scope, with their priority, so deleting a failing test case can never
  improve a pass rate.
- A defect link removed by mistake leaves the counts (it is soft-deleted and audited). Linking is the control point: the gate is only as
  honest as the links.
- `QualityGateAppService` (create/edit/delete gates, `POST quality-gates/evaluate`) was added beside the two endpoints named in T044,
  because without it nobody can configure a gate. Permissions: evaluating needs `QualityGates.Default`, managing gates
  `QualityGates.Manage`, starting or approving a sign-off `SignOff.Approve`, reading reports `SignOff.Default`.
- Error code `TestCaseManagement:QualityGateNotPassed` carries the failed criteria in its details, e.g. `PassRate 90 (required >= 95)`;
  `POST quality-gates/evaluate` returns the structured breakdown. Over HTTP the details are not sent (ABP only exposes `details` for
  user-friendly exceptions); the same text arrives in the localized `message` and in `data.FailedCriteria` (see 4.6).

### 4.6. Phase 8 implementation notes: packaging, sample host and HTTP contract

**Packaging (T045)**
- Every project under `src/` is a NuGet package (`src/Directory.Build.props` switches `IsPackable` on); `test/` and `host/` are never
  packed. Shared metadata is in the root `Directory.Build.props`, each package has its own `Description`, `README.md` is packed into
  all of them, and XML documentation plus `.snupkg` symbol packages are produced.
- The version is `VersionPrefix` 1.0.0 plus an optional `VersionSuffix` (`./build/build.ps1 -VersionSuffix preview.1`).
  `global.json` pins SDK 10.0.100 with `rollForward: latestFeature`.
- `build/build.ps1` runs restore, build, test and pack into `artifacts/packages`, and fails when the number of packages differs from the
  number of projects under `src/`.
- Verified by restoring a throw-away application from the packed files only: three package references (Application,
  EntityFrameworkCore, HttpApi) were enough, the model was embedded in the application's own `DbContext`
  (`[ReplaceDbContext(typeof(ITestCaseManagementDbContext))]` plus `ConfigureTestCaseManagement()`), the schema was created, an
  application service created a suite in it, and the embedded localization resolved.
- Not decided by the code and left as placeholders: license (no license expression), company and authors (`Acme`), repository URL.

**Sample host (T047)** `host/Acme.TestCaseManagement.HttpApi.Host`
- ABP with `Volo.Abp.Swashbuckle`, a SQLite file, and the schema created by `EnsureCreated` because the module ships no migrations.
- Login is required (updated after the Angular work, see 4.7). The host uses ABP Identity and Permission Management with one
  `HostDbContext` that embeds this module's model, JWT bearer tokens from `POST /api/auth/login` (HS256, key from
  `Auth:Jwt:SigningKey`, which must be 32+ characters or start-up fails), and lockout after repeated failed passwords. The login
  answer is the same for an unknown user and a wrong password. In Development it creates the schema and seeds the roles QA Lead
  (all permissions), Product Owner (reads everything, `Requirements.Manage`, `SignOff.Approve`) and Tester (test cases, executions,
  reads) with one demo user each, from `Seed:Password`; without that setting nothing is seeded, so no account with a well-known
  password appears by accident. Static permission definitions are not saved to the database.
- The OpenAPI document lists the module's routes only, uses `Controller_Action` operation ids and readable schema ids, and carries the
  member names of every enum (in the description and as `x-enum-varnames`). Operation summaries are copied from the application
  service interfaces, because Swashbuckle does not expand the `<inheritdoc />` that the controllers carry.

**Contract and HTTP tests** `test/Acme.TestCaseManagement.HttpApi.Tests`, the host in-process (`WebApplicationFactory<Program>`) on a
temporary SQLite file:
- `OpenApiContract_Tests`: the document is valid OpenAPI 3 (parsed with Microsoft.OpenApi) and every `$ref` resolves; it holds exactly
  the 53 expected operations (id, verb, route), so adding, removing or renaming an endpoint is a visible contract change; every
  operation has a unique id, documents 200 and the shared error response, declares each route placeholder as a required path parameter,
  and takes a JSON body if it is a POST or PUT (except `TestRun_Complete`); list endpoints take paging and filters in the query;
  validation rules of the DTOs (required, lengths, ranges) are in the schemas; enum values and names match the code.
- `HttpApiFlow_Tests`: one release cycle over HTTP (library, versions, plan, run, attempts, defects, requirements, RTM, gate refusal
  and pass, two-user sign-off, deletes) that also fails when any operation of the contract was not called.
- `HttpApiErrors_Tests`: 404 for unknown ids, 400 with the member names for invalid input, 403 with the module error code for a broken
  business rule.
- `HttpApiSecurity_Tests`: with the seeded accounts, no token or a token signed with another key gives 401; a wrong password and an
  unknown user are refused identically; a Tester reads everything and writes test cases but gets 403 on creating suites or plans,
  approving a test case and starting a sign-off; a Product Owner reads and approves sign-offs but cannot author test cases; the
  application configuration lists exactly the granted policies. A refused permission check has an empty body (the authentication
  scheme answers it), unlike business errors, which carry `code` and `message`.
- `Controller_Conventions_Tests` and an addition to `Foundation_Tests`: each controller implements one application service interface
  and exposes all of its methods, nothing is `[AllowAnonymous]`, and every application service has `[Authorize]`.

**Findings**
- ABP answers a `BusinessException` with 403, `code`, a localized `message` and `data`, but `details` is null. The sign-off refusal
  therefore reaches HTTP clients as `message` ("... Failed criteria: OpenCriticalDefects 1 (required <= 0).") and
  `data.FailedCriteria`.
- Only a sample of the permission matrix is exercised over HTTP (suites, sign-off, the read endpoints); the other permissions rely on
  the reflection check that every application service is `[Authorize]`d and on the attribute values being reviewed in code.
- `SQLitePCLRaw.lib.e_sqlite3` is raised to 2.1.13 in the projects that use SQLite (tests and host) to clear advisory GHSA-2m69-gcr7-jv3q
  (SQLite before 3.50.2). The AutoMapper advisory stays, as decided in Phase 3.

### 4.7. Angular front end (after Phase 8)

Not part of the original tasks. `angular/` is a standalone Angular 22 app. It is not the ABP Angular template: the ABP CLI is unusable
here and the template needs an OpenIddict server. It calls the REST API through typed services in `src/app/proxy`, mirrors the domain's
status transitions in the UI, requires a login (`/login`, bearer token in `localStorage`, route guard, sign-out on 401) and hides the
buttons that the user's permissions rule out, reading them from ABP's application configuration. Hiding is a convenience; the API
enforces the permission. The login is the sample host's own JWT endpoint, not OpenIddict with the authorization-code flow, and the
password is sent to the API directly (acceptable for a first-party client of the same host, not what a public client should do).
A scripted browser run (Edge) went through the redirect to login, a refused wrong password, a Tester without management buttons, then
suite, test cases, approval, plan, run, execution with a Critical defect, gate refusal, defect resolution, sign-off by two different
users (sign out and in) and RTM, with no unexpected console or HTTP errors. The browser script is not in the repository; unit tests
cover error mapping, enum helpers and the session handling. There is no user or role administration screen: accounts come from the
seed.

Languages: the UI is available in English and Vietnamese with an English / Tiếng Việt switch (login page and header). It uses its own
small runtime dictionary (`core/i18n`: `en.ts`, `vi.ts`, a `t` pipe, enum names looked up as `enum.<Enum>.<Member>`, dates formatted per
language) rather than Angular's compile-time `@angular/localize`, because the latter needs one build per language and cannot switch
while the app runs. The compiler checks that `vi.ts` has every key of `en.ts`, a unit test checks the placeholders and that every enum
member is named. The choice is kept in `localStorage` (default: the browser language), set on `<html lang>`, and sent to the API as
`Accept-Language`. The sample host now calls `UseAbpRequestLocalization` with the languages `en` and `vi`, so the messages of the
module (`vi.json` in Domain.Shared, which already had every key of `en.json`) and of ABP come back in the language of the caller; an
HTTP test covers `vi`, `vi-VN`, `en` and an unsupported language (falls back to English). Not translated: the role names (data from the
host), the field-level validation text of ASP.NET DataAnnotations ("The Name field is required.", English only), and toasts already on
screen when the language is switched. While adding the language interceptor, the 401 handler of the auth interceptor was found to call
`inject()` inside an asynchronous callback; the services are now resolved when the interceptor starts, with a test for that path. The
run history dialog also no longer reopens when a user closes it while a refresh is in flight.

### 4.8. Phase 9: Excel and CSV import and export (FR-018)

Added after Phase 8 on request. Two application services, `TestCaseTransferAppService` (library) and `TestResultTransferAppService`
(results of one run), behind `GET test-cases/export`, `POST test-cases/import`, `GET runs/{runId}/results/export` and
`POST runs/{runId}/results/import`. No new permission: exporting needs the read permission (`TestCases` or `TestRuns`), importing test
cases needs `TestCases.Create` (and `TestCases.Update` to update, `TestSuites.Manage` to create suites from the Suite column), importing
results needs `TestRuns.Execute`. The import calls the existing application services (`ITestCaseAppService`, `ITestSuiteAppService`,
`ITestRunAppService.BatchExecuteAsync`), so every domain rule, version publication and permission check of the normal path applies.

**File layout.** One row per step, the test case columns repeated on every row of the case. Industry research (Xray's default CSV
profile and TestRail's "separated steps" import both describe a test case as several rows that carry the same test-level values, with
only the step columns changing) says this is what teams already have in their files. The import is a little more lenient than those tools:
a row without a Code continues the case above it, and the later rows of a case may leave the test case columns blank (they may not
contradict the first row). Columns: Suite, Code, Title, Description, Preconditions, Postconditions, Priority, Severity, Kind, Layer,
ExecutionType, AutomationId, Flaky, Status, Version, StepNo, Action, ExpectedResult, TestData. Status, Version and StepNo are written for
information and ignored on import (the status moves by the workflow and approval publishes a version; the step order is the row order).
The Suite column is a path, `Payments/Cards`; a "/" or a backslash inside a name is escaped with a backslash. Names match without
regard to case; a path that matches two suites is an error rather than a guess; a missing suite is created when the caller may manage
suites, and is an error otherwise. Results: one row per attempt, columns Code, Title, Version, Attempt, Result, ActualResult,
DurationSeconds, Defects (`Jira:BUG-88; GitHub:#42`), ExecutedAt, ExecutedBy; only Code and Result are required to import, and a row
whose Result is blank or Untested is skipped (an export lists items never run as Untested). Rows are matched to run items by Code, and by
Version when the run holds two versions of the same test case.

**All or nothing, in two passes.** Pass 1 parses and checks everything against the library without writing: types, lengths, enum values,
suites, existing codes, permissions, the run. If any row is invalid, nothing is written, and the report lists every problem (row number,
code, localized message), not only the first. Pass 2 runs in the unit of work of the request, so an unexpected failure also leaves
nothing behind. `DryRun` stops after pass 1, and the dialog of the UI makes the check a required step before the import button works.
A half-imported library, where the user has to work out which rows went in, was judged worse than a file that must be fixed first.

**Existing codes.** `Skip` (default) leaves them. `Update` applies the file; a column that is absent from the file leaves that field
alone (a file with only Code and Title renames test cases), while a column that is present and blank clears the field, so that an export
imported again is exact. An update that changes nothing is reported as skipped and publishes no version: without it, importing an
export back would add a version to every approved test case. When the steps are replaced, the step at the same position keeps its
identity. Rows without a Suite never move an existing test case to the default suite.

**Safety.** The format is detected from the content (zip signature, binary check), never from the name or the content type. Limits are
options (`TestCaseManagementTransferOptions`): file size 5 MiB, 10,000 rows, 50 MiB unzipped (a small file that expands is refused before
it is opened), 20,000 test cases per export. An upload is read only one byte past the size limit. CSV cells that start with `=`, `+`,
`-`, `@`, tab or carriage return get a leading apostrophe on export, as OWASP recommends against formula injection, and the import takes
it away again; Excel cells are written as plain text, which Excel does not evaluate. Characters that XML 1.0 forbids are removed from
Excel output so that the file still opens.

**Library choice.** `DocumentFormat.OpenXml` 3.x, Microsoft's own MIT-licensed SDK, written to directly (a few hundred lines for reading and
writing a table), so that the packages of this module add one Microsoft dependency and no third-party one. I did not weigh the licences
of the higher-level libraries in detail.

**CSV details.** RFC 4180 quoting, UTF-8 with a byte order mark on export (Excel needs it to show diacritics), comma delimiter. On import
the delimiter (comma, semicolon or tab) and the encoding (BOM) are detected, because Excel writes semicolons in regions that use a decimal
comma. Known limit: a comma CSV double-clicked in Excel with such regional settings opens in one column; the Excel export has no such
problem. Legacy code pages (Windows-1258) are not detected.

**Findings while testing.** The sign-in showed the menu before the permissions were loaded, so a fast click could be undone by the login
page finishing its navigation; the menu now waits for `auth.ready()`. A failed download carries its error body as a blob, so the error
interceptor reads the blob to show the message of the API. The OpenAPI document described the downloads as `text/plain`; the export
actions now declare their real content types. The upload is described as `multipart/form-data` with a binary `File`, and the contract
test covers it.

**Not covered.** `ExecutedBy` in the results export is a user id, not a name (the module does not depend on a user directory). The URL of
a defect is not exchanged. Excel files are read from the first worksheet only; merged cells, images and macros are ignored. Dates in
Excel cells are not interpreted (no date column is imported). No attachments. Import is tested against SQLite only, with up to a few
dozen test cases; the 10,000-row limit was not load tested.

### 4.9. Phase 10: CI/CD ingestion of automated results (FR-020, FR-019)

Added after Phase 9. A pipeline publishes the results of an automated run with `POST api/test-case-management/automation/results`
(`AutomationResultsAppService`, permission `AutomationResults.Publish`) and authenticates with an API key
(`ApiKeyAppService`, routes `api-keys`, permission `ApiKeys` to list and `ApiKeys.Manage` to create or revoke).

**API key.** `tcm_<8 hex>_<43 base64url>`: 256 random bits, and the prefix is the lookup handle. Only the SHA-256 hash is stored (the
secret has full entropy, so a slow password hash buys nothing and would slow every request); the secret is returned once, by the
create call. Validation finds the key by prefix across tenants, compares the hash in fixed time, and refuses a revoked or expired key
(UTC). `LastUsedAt` is written at most once a minute, in a unit of work of its own, and a failure there never fails the request.

**Authentication.** A handler reads the `X-Api-Key` header. The principal has no user id and no role, only the claim `tcm_api_key_id`,
and a custom `PermissionValueProvider` grants that principal exactly `AutomationResults.Publish`. So a leaked key can publish results
and can do nothing else; an API key cannot manage keys, read the library or approve anything. The sample host uses a policy scheme
(`BearerOrApiKey`): a request with `X-Api-Key` goes to the key handler, any other to JWT; when both are sent the key wins.

**Publishing.**
- Results are matched to test cases by Automation ID (trimmed, case-insensitive). An unmatched or ambiguous ID is reported per result
  and the rest are recorded (partial acceptance: the pipeline should not lose 999 results over one typo). `FailOnUnmatched` makes
  the whole request fail instead.
- The run is an existing `RunId` or a new `Run` (exactly one of them). A matched approved test case that is not in the run is added
  (`AddMissingToRun`, default true); a case that is not approved is reported, not scheduled.
- Several results for the same test case become ordered attempts (`AttemptNumber`, then request order), the way a retry works.
- A test is flagged flaky when the runner says so, or when it both fails and passes inside one request. The flag is never cleared by
  this path. (Detection from the history across runs is Phase 11.)
- `CompleteRun` completes the run after the results are written. Everything is one unit of work: a failure midway leaves no run.
- At most 2000 results per request.
- `Idempotency-Key` (header or body): the first request is stored in `AutomationPublication` with a hash of the request, a replay
  returns the stored answer without recording again, and the same key with a different request is refused. Requests with one key
  run one after the other: a lock per key is held until the unit of work of the request is over (a nested unit of work would block on
  SQLite behind the read transaction of the outer one), so a retry that overlaps the first request waits, then finds the stored answer (tested with three parallel requests; the lock is in-process unless the host registers a
  distributed lock provider).

**FR-019.** The Automation ID now names one test case. It is checked when a test case is created and when its Automation ID changes
(a legacy duplicate can still be edited until the ID itself is changed). The import reports a clash, with the library or inside the
file, as an invalid row. An import may move an ID (A takes what B had, B takes a free ID or none): the file's own changes are taken
into account and the test cases are written in an order that keeps the rule true at every step. A swap or a cycle has no such order
and is refused. There is no unique index on the Automation ID: a legacy library may hold duplicates, soft-deleted rows would clash with a
plain index, and a filtered index is provider specific. The rule is kept by the application services, so two simultaneous edits can
still give one ID to two test cases; the publish endpoint then reports that ID as ambiguous instead of guessing.

**Front end.** The Automation page lists keys, creates one (the secret is in a dialog with a copy button and is gone once the dialog is
closed), revokes with a confirmation, and shows a curl and a GitHub Actions example. The tab is shown only with `ApiKeys`.

**Findings while testing.** A wide `<pre>` pushed the dialog off the screen (the snippet now wraps). The browser run proves the whole
chain: create a key, publish with it, a wrong key gets 401, revoke it, the revoked key gets 401.

**Not covered.**
- A result made through a key has no creator (there is no user).
- Multi-tenant behavior is designed for (keys are found across tenants, data is per tenant) but not tested.
- The unique index of the idempotency key does not protect a null tenant on SQLite and PostgreSQL (nulls are distinct there); the
  per-key lock covers one process, so several server nodes need a distributed lock provider.
- Publications are never cleaned up, there is no rate limiting, and `LastUsedAt` can lag by a minute.
- Tested on SQLite only.

### 4.10. Phase 11: flaky detection and dashboard (FR-012, FR-025)

Added after Phase 10. Two read-mostly services, `FlakyTestAppService` (`GET flaky-tests`, `POST flaky-tests/apply`) and
`DashboardAppService` (`GET dashboard`), on one repository, `IInsightsRepository`, that loads the run items, the attempts of a
lookback window and the defect links of a scope (the runs of one plan, or every run, CI runs without a plan included). The figures
are calculated when asked, by pure functions in the domain (`FlakinessCalculator`, `DashboardCalculator`), so nothing is stored and
nothing can drift. No new permission: the dashboard needs `TestRuns` (read), the flaky list needs `TestCases` (read), and `apply`
needs `TestCases.Update`. An API key can call none of them.

**Flakiness score (FR-012).** Take the latest `WindowSize` (20) Passed or Failed outcomes of a test case, from every run, in time
order (ties by attempt number), and count how often neighbouring outcomes differ. Score = changes / (outcomes - 1), from 0 to 1,
rounded down to two decimals. Research: Buildkite Test Engine's transition score counts the same changes in a window (PPPPP and
FFFFF have none, PPFFF one, PFPFF three), "flip rate" is the common name of it (Testkube combines it with the pass rate), and
Katalon uses a probabilistic model that needs far more history than a library of this size has. Why not the pass rate: a test that
always fails is broken, not flaky, and a test that was fixed once has a middling pass rate but no instability; only changes back
and forth show intermittence. A retry that passes after a failure is two attempts one after the other, so it counts as a flip with no
special rule. Blocked and Skipped are not outcomes of the test and are left out.

| Level | Rule | Meaning |
|---|---|---|
| Insufficient | fewer than 5 outcomes | not scored |
| Stable | score below 0.15 | always passing, always failing, or one real change (a regression or a fix) |
| Watch | 0.15 or more | 3 changes in 20 outcomes |
| Flaky | 0.30 or more | 6 changes in 20 outcomes (a lone failure inside passes is 2 changes) |

The thresholds are defaults (`TestCaseManagementInsightsOptions`: window, minimum, watch, flaky, lookback of 90 days) and the answer
carries the settings the scores were made with. `apply` writes the finding into the library: a test case that scores Flaky gets
the Flaky flag; with `ClearRecovered`, a flagged one that now scores Stable loses it. Nothing clears a flag by itself, and a flag set
by a runner or a person on a test with too little history is never touched.

**Dashboard (FR-025).** All days are dates of the server clock (the clock that stamps the attempts).
- *Pass rate*: passed over items that are not skipped, exactly the quality gate's definition (plan 4.5), with the completion
  percentage and the first-time pass rate beside it.
- *Execution velocity*: attempts per day for the last 7 to 90 days (passed, failed, other), the average per day, the average of the
  last 7 days and, when the window has 14 days, the change against the 7 days before. Items first executed per day are returned
  too, because that is the pace the burn-down needs.
- *Burn-down*: items without any attempt, at the end of each day, against an ideal straight line from the items left before the first
  day to zero at the end of the end day. Industry tools (QA Touch, AIO Tests) draw the same two lines; above the ideal line means
  behind. The chart runs over the dates of the plan (start to end, then on to today when the plan is late) or, with no plan or no
  start date, over the velocity window; it is cut to the latest 120 days. A projected finish is today plus remaining items divided by
  the items first executed per day over the last 7 days; there is none when nothing is left or nothing moved. Items stay in scope
  when their test case is later deleted, as in the quality gate.
- *Defect density*: distinct defects (tracker and key, ignoring case, one ticket linked from many tests is one defect) per 100
  executed test cases, with the open and resolved counts, the open ones by severity (the worst severity of the open links), and the
  share of executed test cases that have a defect (the "test case defect density" of the test-metrics literature). A defect is open
  while any of its links is.

**Front end.** The Dashboard page shows five cards, the burn-down and velocity charts (inline SVG, no chart library; the geometry is
a pure function with unit tests), the defect density, and the flaky table with the score bar, the level and a button to apply the flags
(only with `TestCases.Update`). The tab needs `TestRuns`.

**Not covered.**
- Flakiness is judged per test case across all runs and environments: a test that fails only on one environment shows as flaky.
- The attempts of the lookback window are read into memory (fine for thousands of tests, not for millions; a database side
  aggregation would be the next step).
- The days follow the server clock, not the time zone of the viewer.
- The burn-down counts items as they are today: an item added to a run later does not appear in the earlier days' total.
- Tested on SQLite only.

### 4.11. Phase 12: attachments (FR-015)

Added after Phase 11. `AttachmentAppService` behind `GET attachments`, `POST attachments` (multipart), `GET attachments/{id}/content` and
`DELETE attachments/{id}`. A file belongs to a test case (a reference file, an example input) or to one execution attempt (the
screenshot, the crash log or the video of a failure).

**Storage.** The bytes go into an ABP blob container (`AttachmentContainer`, name `test-case-management-attachments`) and the database holds
only the record (`Attachment`: owner type and id, clean name, content type, size, SHA-256, description, audit columns). The module depends on
`Volo.Abp.BlobStoring` and leaves the provider to the host, so a company chooses a folder, the database, S3 or Azure Blob Storage with one
`Configure<AbpBlobStoringOptions>`; the sample host uses the file system provider (`Storage:Path`, default `App_Data/attachments` next to the
binaries). Why not a binary column: videos would grow the main database and its backups, and a blob store can move to the cloud later with no
change in the module. Blob name = the id of the record, so a name from the user never reaches a path.

**Rules** (`AttachmentManager`, `TestCaseManagementAttachmentOptions`):
- Type: an extension whitelist, each with the content type that downloads are served as; the content type of the upload is ignored. The
  default is what a tester gathers (png, jpg, gif, webp, bmp, pdf, txt, log, md, csv, json, xml, har, zip, gz, docx, xlsx, pptx, mp4, webm, mov).
  SVG and HTML are out on purpose (a browser runs the scripts in them); so are programs and scripts.
- Size: 25 MB a file (the declared length is checked before reading, and the bytes read are counted, so a wrong declared length does not
  get past); 25 files an owner. Both are options.
- Name: path (either slash), control and reserved characters and leading dots are removed, at most 255 characters with the extension kept.
- Download: `Content-Disposition: attachment`, the fixed content type, `X-Content-Type-Options: nosniff` and a sandboxing
  `Content-Security-Policy`, so a file is never shown as a page of this site. No magic-byte check of the content: the whitelist and these
  headers are what protect the reader, and a renamed program stays a download.

**Permissions.** No new permission: an attachment follows what it is attached to. Reading needs `TestCases` (or `TestRuns` for an attempt);
adding and deleting needs `TestCases.Update` (or `TestRuns.Execute`). A pipeline's API key has none of them.

**Consistency.** Upload saves the content first and the record second, and removes the content if the record fails, so nothing points to a missing
file. Delete is a soft delete of the record (who deleted stays in the audit columns) and the content is removed after the commit. A file lost from the storage
is answered with a clear message, not a server error. Attachments of attempts stay editable in a completed run: the evidence of an attempt may
arrive later, and the attempt itself is not changed (the append-only rule of execution history concerns the attempt).

**Front end.** One panel, `app-attachments`, in the test case dialog and in each attempt of the history: upload by button, drag and drop or paste of a
screenshot (a pasted image is named after the time), thumbnails of images, download, delete with confirmation; read-only without the permission.

**Not covered.**
- Deleting a test case does not delete its attachments (the records and the files stay, unreachable from any screen).
- The file is read into memory for the limit and the hash (fine up to the 25 MB default; a larger limit needs a streaming upload).
- No virus scan, no thumbnails made on the server, no preview of video or PDF inside the page, no upload from the CI endpoint (a pipeline cannot attach
  its screenshots yet).
- Tested with the file system provider and an in-memory one only.

### 4.12. Phase 13: tags and the automation facet of search (FR-024)

Added after Phase 12. A test case has free-form tags (`TestCaseTag`, a child entity like a step), and the list, the export and the screen filter
by them and by whether an Automation ID is linked. The facets of FR-024 are now: text, suite, priority, severity, status, kind, layer, execution
type, tag and automation. (Reusable steps, FR-005, are a separate piece of work.)

**Model.** Free-text labels, as in the labels of Jira and Xray and the tags of TestRail, rather than a managed catalogue of tags: nothing to
administer, a new tag costs nothing, and the screen suggests the ones in use (`GET test-cases/tags`: every tag with the number of test cases that
have it, deleted test cases not counted). The rules, in one place (`TagNames`) so that the screen, the API and the import agree: a tag is cleaned
(trimmed, runs of white space made one space), is 1 to 50 characters, has no comma, semicolon or control character (the two separate tags in a file
and on screen), and a test case has at most 20. "Smoke" and "smoke" are one tag; the first spelling is kept and the list shows the spelling most used.
A refused tag is a `BusinessException` that names the tag, with the message in the language asked for.

**Tags are labels, not content.** They are not in any `TestCaseVersion`. `PUT test-cases/{id}/tags` replaces the tags without publishing a version
and without leaving the status, also for an approved test case, and needs `TestCases.Update` (a tester can label). On a normal update, `Tags` omitted
(null) keeps the tags and an empty list removes them, so a client that does not know about tags does not wipe them; sending tags with an update of an
approved test case still publishes a version, as any update does.

**Filter.** `Tags` (repeat the parameter) means all of them, compared ignoring case; `HasAutomationId` true or false. The same two are on the export,
so that "export what the filters show" stays true.

**Import and export.** A `Tags` column, tags separated by semicolons. As with every column: absent leaves the tags of an existing test case alone, present
and blank clears them, the same tags in another case or order are no change, a row may not contradict the first row of its test case, and a tag that cannot
be used makes that row invalid (nothing is written, as always).

**Front end.** A chip input (Enter, comma or semicolon adds, Backspace removes the last, a pasted list becomes several tags) in the form and, in place,
in the detail dialog (saved on its own, with no new version), chips in the list, and the tag and automation filters.

**Not covered.**
- No rename or merge of a tag across test cases, and no colours: a tag is text.
- Tags are on test cases only, not on runs or plans.
- The filter takes one tag on screen (the API takes several, all required).
- Tested on SQLite only.

### 4.13. Phase 14: reusable steps (FR-005)

Added after Phase 13. `SharedStepGroupAppService` (`shared-step-groups`) is the library: a group (`SharedStepGroup`, name, description, a revision
and ordered `SharedStep`s). A test case uses a group through three operations on `ITestCaseAppService`: insert, refresh and detach.

**The decision: copy with a link, not a live reference.** Two designs were weighed. A live reference (the test case holds only "include group X" and the
steps are read from the group when needed) is what the word "by reference" suggests, but it breaks FR-004 and the constitution: an approved test case,
its immutable version and every historical run would change when somebody edits the group, so a run could show steps that were never tested. The
chosen design copies the steps into the test case, as ordinary `TestStep`s, and records on each copied step which group it came from and at which
revision (`SharedStepGroupId`, `SharedStepRevision`). Everything downstream is untouched: versions snapshot the copied steps, run items bind to the
version, history, import and export see plain steps. Changing a group raises its revision (only when the steps change; a name or description does not)
and changes no test case; the test cases that copied an older revision are *behind*, which the screen shows, and a person (or one bulk action) brings
them up to date. Here the freeze is at the time of the copy, which keeps the question "what was tested?" answerable from the version alone and is simple to audit; the cost is that bringing a test case up to date is an act, not automatic, which is what the behind marker and the bulk update are for.

**Rules.**
- A group has 1 to 50 steps and a name that is unique ignoring case (among groups not deleted). Deleting is refused while a test case uses the group
  (detach it first), so that no link points to nothing.
- Insert puts a copy at a position (default: the end; a group is used once by a test case, see 4.16), refresh replaces the copy by the current steps at the place of the first copied step, detach
  drops the link and keeps the steps. Insert and refresh change content, so an approved test case gets a new version like for any edit of steps (the
  screen asks first); detach changes no content, so it publishes nothing.
- A copied step edited inside the test case becomes the test case's own (its link is dropped); a save that leaves it alone keeps the link, and so do reordering
  and an import of an unchanged file. New steps are never linked, whatever a client sends: the link is made only by insert and refresh.
- Bulk update (`POST shared-step-groups/{id}/update-test-cases`): refreshes all test cases that are behind, or the chosen ones; approved ones get a version
  whose change summary says which group and revision. It needs `SharedSteps.Manage` and `TestCases.Update`.

**Permissions.** `SharedSteps` reads the library (the demo roles QA lead, tester and product owner have it) and `SharedSteps.Manage` changes it (QA lead):
a change can put many test cases behind. Using a group in a test case needs `TestCases.Update`. An API key has none.

**Front end.** A Shared steps page (list, edit with revision note, usage with the test cases that are behind selected and a bulk update). In the test
case dialog the steps carry a "Shared: name" chip, a section lists the groups used with Behind or Up to date and Update or Detach, and a selector adds a
group; in the edit form copied steps are read-only.

**Not covered.**
- The version snapshot does not record that a step came from a group (it holds the steps as tested); the link is on the live test case only.
- No nesting of groups in groups, and no parameters in a group (a "Log in as {user}" with a value chosen per test case); the test data column of a step is
  the place for a value.
- Refreshing after somebody removed one step of a group by hand puts all the group's steps back, at the place of the first remaining one.
- Import and export carry the steps, not the link (an import of a test case that is new has no links).
- Tested on SQLite only.

### 4.14. Phase 15: plugging into a host application (an ABP Angular app)

The module is meant to be added to a company's main web application, which is an ABP application with its own sign-in, left
sidebar, user and role screens. Until now it had only been run in its own sample host, so this phase built a second host that
looks like the real one (`abp new` with the application template, Angular UI, LeptonX theme, OpenIddict sign-in, one SQLite
database; kept outside the repository), added the module to it as a person would, and ran the browser against it.

**The decision: a library folder with a thin adapter per host, not a rewrite on the ABP services.** The Angular pages used to
depend on their own sign-in, language service, toasts and the address `/api`. They now depend on four small contracts in
`core/host.ts` and `core/auth.ts`: `AuthService` (who is signed in, may they), `TCM_LANGUAGE` (the language of the host),
`TCM_NOTIFIER` (the host's notifications) and `TCM_API_URL` / `TCM_BASE_PATH` (where the API and the pages are). Each host
supplies them: the standalone app keeps its JWT sign-in (`LocalAuthService`), and an ABP application uses
`provideTestCaseManagementForAbp()` (entry point `test-case-management/abp`), which reads the user and permissions from ABP's
application configuration, follows ABP's language switch, shows messages through ABP's toaster and takes the API address from
ABP's environment. The pages themselves were not rewritten, which kept the 73 unit tests and the whole browser script valid.
The alternative (using ABP's `RestService`, `LocalizationService` and components inside the pages) would have tied the module to
ABP Angular and thrown away the standalone app.

**Layout.** `angular/projects/test-case-management/` is the library: `src/lib/{core,features,proxy,styles}`, `src/public-api.ts`
and `abp/`. The standalone app in `angular/src` is now a small host of that library (login page, top bar, `LocalAuthService`).
The library is shared **as source** through a TypeScript path alias, not as an npm package: a package compiled with another Angular
minor version, the second copy of `@angular/core` that a linked folder would bring, and the extra build step were more risk than
the plan needed; the host compiles the folder with its own Angular. Packing it with ng-packagr is a later, mechanical step.

**What the host needs to do (README, "Using the module in an ABP application").** On the server: reference the six projects and
depend on their modules, embed the model in its DbContext and add a migration, call `AddTestCaseManagementApiKeyAuthentication()`.
On the client: copy the library folder, add two path aliases, two providers and one lazy route.

**What running the module in a real template showed, and what was changed because of it.**
- The module needs ABP 10.6.1; a template generated with 10.6.0 packages stops with a NuGet downgrade error (NU1605). The sample
  host's packages were raised to 10.6.1. The ABP Angular packages declare a peer of Angular 21.2 although the template installs 22.0,
  so `npm install` needed `--legacy-peer-deps` there; that is the template's inconsistency, not the module's.
- The template's default scheme is the ASP.NET Core Identity scheme forwarding bearer tokens, so a request with `X-Api-Key` was
  unauthenticated. `AddTestCaseManagementApiKeyAuthentication()` adds the key scheme and makes that scheme forward a key request to it
  and everything else as before (3 tests). A pipeline sends no cookie, so ABP's anti-forgery check (which applies to requests that
  carry the sign-in cookie) does not affect it; a first attempt to exempt the endpoint was dropped after a client without cookies proved
  it unnecessary.
- ABP shows an error dialog only for calls made with its `RestService`; the module uses `HttpClient`, so a refused request showed
  nothing. The ABP adapter registers an interceptor, for the module's API only, that turns the failed response into a message in the
  host's toaster (in the language of the user: ABP sends `Accept-Language`).
- LeptonX's Bootstrap also defines `.row`, `.card`, `.btn`, `.badge` and `.alert`; its `.row` has negative margins and makes children 100%
  wide, which stacked the buttons of every page header. The module's styles now live under `.tcm` (the pages are wrapped by
  `TcmShellComponent`, which also loads them, so the host adds no stylesheet) and put back what they rely on for those classes.
- LeptonX's sidebar has `z-index: 1000`, above the dialogs (50). The dialog is now at 1055, the value Bootstrap uses for its modals.
- The tab title showed the raw key (`title.repository | MainApp`) because the page titles were dictionary keys that only the standalone app
  translated. Each route now resolves its title to the text in the user's language, and keeps the key in its data so that the standalone
  app still retitles the tab when the language is switched (2 tests).
- The run page took its id from router input binding, which only the standalone app had switched on; it now reads the route itself.
- A reader with only `TestCases` got 403 toasts because the repository asked for the suite tree and the shared steps without the
  permission to read them. Those two calls now wait for `TestSuites` and `SharedSteps`.
- The module's menu: a group and seven entries added to ABP's `RoutesService`, each with the permission that opens its page (the
  standalone app keeps showing the pages a signed-in user may open). The labels come from the module's localization resource
  (`Menu:*` in `Localization/TestCaseManagement/*.json`), so ABP's language switch translates the sidebar too.
- Attachments need no setup in the template: it already includes ABP's database BlobStoring provider, and the files went there.

**Verified in the browser against that host** (the admin of the template, then a user with three permissions): the sidebar group and the
seven pages; a suite, two test cases and their approval; attachments (upload, an `.exe` refused with the message in the host's toaster,
download); a plan and a run with an execution; an API key created, a result published with it from a client without cookies, a wrong
key and a revoked key refused; the dashboard; the module's permissions listed in the host's Roles screen; a role with
`TestCases`, `TestRuns` and `TestPlans` sees exactly Dashboard, Test repository and Plans and runs and no management buttons; switching
the host to Tiếng Việt translates the sidebar and the pages. The standalone app passes the whole earlier browser script again.

**Not covered.**
- The UI is shared as source, not as an npm package; the sample host compiled it with Angular 22.0. A host on Angular 21 (which the
  ABP 10.6 packages declare as a peer) was not tried.
- Only ABP's LeptonX Lite side menu was tried. The pages keep their own light palette: they do not follow the host's dark mode or theme
  colours, and the standalone dark mode switches only when the host asks for it (`TCM_FOLLOW_OS_THEME`).
- A host with MVC or Blazor UI can use the API but not these pages.
- One SQLite database and one tenant, as before; SQL Server and PostgreSQL were not run.

### 4.15. Phase 17: AI step suggestions (FR-027)

(The guide for production deployment and the run on SQL Server or PostgreSQL have not been done yet; this phase was done before them because it was asked for first.)

FR-027 asks for an "integration hook for AI-powered test step generation from requirements text". A person writes or pastes a requirement, the
module asks an AI model for steps, and the person decides which of them go into the test case.

**The decision: a hook plus one built-in provider that is configured, not coded.** `IStepSuggestionProvider` (`IsEnabled`, `SuggestAsync`) is the
extension point; `StepSuggestionAppService` (`step-suggestions`: `GET status`, `POST`) is what the screen calls, and it works with whatever provider
is registered. The module ships `OpenAiCompatibleStepSuggestionProvider`, which calls a chat completions endpoint and is set up entirely from the
host's configuration section `TestCaseManagement:AiSuggestions` (`Endpoint`, `ApiKey`, `Model`, and optionally `ApiKeyHeader`, `TimeoutSeconds`,
`MaxResponseBytes`, `SystemPrompt`): a host writes no code. Without an `Endpoint` the provider reports `IsEnabled = false`, the status says so, and
the screen shows no button. A host that uses a service with another protocol registers its own provider in place of the built-in one.

**Why that protocol.** The chat completions shape is spoken by OpenAI and by the servers that run models inside a company (Ollama, vLLM, LM Studio),
where the text never leaves the network, which is the safest answer for confidential requirements. The key goes in `Authorization: Bearer` by default
or in a header the host names (`ApiKeyHeader`, for example `api-key`), and the full URL is the setting, query string included, so a service such as
Azure OpenAI should work with `ApiKeyHeader = api-key` and its own URL; that was not tried. Anthropic's own API has another shape and needs a
provider of the host's.

**The model is not trusted, and neither is the text.**
- The key lives only in the host's configuration (user secrets, environment variables, a secret store): it is not in the database, not on a screen,
  not in an API answer, not in a log, not in an error message. A refused or failed call logs the status and the host name of the endpoint, never the
  body (which may repeat the requirement), the URL (which may carry a key) or the message of the exception. A test checks each of those.
- The redirect of the HTTP client is switched off, so a key cannot follow a redirect to another address; the answer is read up to a limit (256 KB) and
  the wait is limited (30 s).
- The requirement and the title go to the model as data between markers that are new for every request (a random token), with a system
  prompt that says to ignore instructions inside them, so the text can neither contain nor build the end marker and close its own frame; the
  title is put on one line inside the frame; the language is only ever one of the names the module knows, never the caller's text. This
  reduces prompt injection, it does not make it impossible, which is why a person reviews.
- A header name that cannot be sent (a setting error) is reported at once and nothing is sent, instead of dropping the key and getting a 401 for
  every user; a key sent over plain `http` is logged as a warning (it travels unencrypted, which is acceptable inside a trusted network).
- The answer is read leniently (a code fence, a sentence before the JSON, even one with brackets in it, other spellings of the fields: every
  opening bracket is tried, up to 50, until one gives steps) and then **cleaned for every provider**, the
  built-in or a host's: steps without an action or an expected result and repeated steps are dropped, control and zero-width characters are removed (an emoji is never cut in half),
  every field is capped (4000 characters, test data 1000) and so is the number of steps (at most 20, 8 by default).
- Nothing is saved by the service. The screen adds the steps the person ticked to the open form, and they exist only when the test case is saved.
- It has its own permission, `TestCases.SuggestSteps`, because the text leaves the application: in the sample host the QA lead and the tester hold it,
  the product owner and the API keys do not. The status needs it too, so a person without it is never told a model exists.

**The minimum length counts the trimmed text** (ten spaces are refused by the API as well as by the screen).

**Front end.** In the test case form a "Suggest steps with AI" button (only when the status is enabled): a dialog starts the requirement from the
description and the title, asks for 3 to 20 steps in the language of the screen, and lists the proposals with a tick each; "Add n step(s)" puts them in
the form (replacing the blank first step of a new form). English and Vietnamese. In an ABP host the button follows the host's permissions like the rest.

**Verified.** 44 application tests (parser, built-in provider against a fake model with the points above, service with a fake provider), 10 HTTP tests
against the sample host configured only through its configuration section, with a fake model that is down, refuses, says nothing, or answers; 13 Angular
tests; the browser run of the standalone app against a fake OpenAI-compatible server (key checked, English and Vietnamese answers, a model that is down, an
answer without steps, a product owner without the button). **Not tried against a real service**: no key was available, so OpenAI, Ollama and Azure are
untested except through the protocol they share.

**Not covered.**
- No quota, rate limit or cost control: anyone with the permission can send requests, and the host pays for them. `max_tokens` is not set.
- ABP's auditing records the call (who, when, how long) but not the requirement text or the title: those two members are marked `DisableAuditing` (a test
  checks it), because they may be confidential. The steps that come back are not audited either.
- One request at a time, no streaming, no memory between requests, no feedback on the quality of the proposals.
- Suggestions are for the steps of a test case only: no suggestion of test cases from a requirement, of test data, or of a title.

### 4.16. Phase 16: a review of the whole module, and what it changed

Before the module is plugged into the company's application, every layer was reviewed once by independent readers (Domain; core application
services; the other application services; EF Core, HTTP API and sample host; the Angular library). They reported about 40 findings, about 34 distinct;
the ones that mattered were checked against the code before anything was changed, and each fix has a test that shows it.

**Security and robustness**
- *Excel import could be made to allocate gigabytes.* The reader kept blank rows between filled ones to keep row numbers true, and padded a row up
  to the column number of a cell, both taken from the file: a header and one cell at row 2,000,000,000 (or at column ZZZZZZZZ) was a few hundred bytes that
  passed every size limit. Row numbers now must be real sheet rows (up to 1,048,576), the span of blank rows counts against the row limit, columns above
  XFD are refused, and the unzipped size is counted on what really comes out of the archive, not on what its directory claims.
- *Enum values were not checked.* JSON turns `99` into `(TestResultStatus)99` and the module stored and counted it. A validation contributor now refuses
  any number that is not a member of the enum, for every enum property of every input of the module, in nested objects and lists (a new DTO is covered
  without an attribute).
- *An approved test case could be edited down to no steps*, keeping the status and publishing an empty version that a run could bind. Refused.
- *An empty `X-Api-Key:` header next to a valid bearer token* sent the request to the key scheme and made it anonymous (some proxies and CI templates
  send the empty header). A header with no value does not name a key. A `null` in the list of results, or a `null` list of defects, was a
  `NullReferenceException` (500); it is a 400 or read as no defects.
- *The file name of an upload* went into the change summary of every version the import creates, which is limited to 1,000 characters: a long or odd
  name made the import fail after its check passed. It is cleaned and cut.
- *The reader of the AI answer* gives up after 20 opening brackets that never close instead of scanning to the end from each of them.
- Sample host: Swagger is served in Development only (setting `Swagger:Enabled`), and an unknown or inactive user name costs a password hash like a
  known one, so the time of the answer does not tell which names exist. The 8-hour token that cannot be revoked and the missing `UseMultiTenancy` of the sample
  host are not changed (a host of its own has its own sign-in and tenants) but are said in the README.

**Business rules and counts**
- *The same test case could sit in a run twice.* The run refused only the same version, and every edit of an approved test case makes a new one, so
  adding it again after an edit scheduled it a second time: totals, completion, pass rate and the gate counted it twice. The check is per test case now
  (error `TestCaseAlreadyInRun`). Runs that already hold two versions (made before) are still read by the results import, which asks for the Version column.
- A new run is refused for an *archived* plan (its end of life). A completed plan still accepts one: a retest after completion is ordinary work.
- *Two approvals of one sign-off at once* each counted only themselves and could leave a fully signed report Pending; *two sign-offs started at once* for one plan
  left two Pending reports; *two saves of the default gate at once* left two defaults, and every evaluation without a gate then failed. Each of the three
  changes now takes a lock for the thing it changes, held until the unit of work ends, before it reads (a second change waits, then is told to try again:
  `OperationInProgress`). The lock is in this process unless the host registers a distributed lock provider, as for the publish of results.
- The *dashboard* scored flakiness over the longer window of its velocity chart (up to 90 days) while the list of flaky tests and the "flag" action use the lookback
  of the options (30): the same test could be Flaky in one place and Stable in the other. The dashboard now uses the lookback.
- *A shared group can be used once by a test case.* A second copy could not be told from the first when the group is refreshed (which replaces the steps of
  the group at one place), and a refresh silently dropped one block. To use a group again, detach the first copy.
- Lists sorted by a column with equal values (priority, status, title, creation time...) had no final unique key, so on SQL Server and PostgreSQL a page could
  repeat or skip rows (SQLite happened to be stable). Every list sort ends with the id now.

**Front end** (11 findings)
- A dialog closed when a mouse drag that began inside it (selecting the one-time API key, text in a form) ended on the backdrop: the secret or the whole form was lost.
  It closes only when the press and the click are both on the backdrop; Escape closes the top dialog, focus goes in and comes back, and Tab stays inside.
- Double click or Enter twice created two of a record (executions, runs, plans, sign-offs, keys, suites, requirements): every such action has an in-flight guard.
- A slow answer could overwrite a newer one (filters, the case picker, the matrix, the dashboard, the run page when the address changes): only the latest request
  updates the screen.
- The Quality page could start a sign-off for a plan that was never evaluated (the result was tied to what the selects said now); the result is now tied to what was
  evaluated and cleared when it changes. The import dialog could enable "Import" for options that were never checked; its options are locked while a check runs.
- Pages called secondary endpoints the user may not call (plans, flaky tests, sign-off list) and showed 403 toasts; they check the permission first.
  The standalone top bar hides the tabs a user may not open. A page beyond the last one after a delete, a tag filter that stayed after its tag vanished, a run page
  stuck on "Loading..." after a 404, an expiry date shown a day late in some time zones, and thumbnail URLs created after the dialog was closed are fixed.

**Left as they are, on purpose or for later**
- Approving again after returning a test case to draft, without an edit, still publishes a version that equals the last. The plan says approval publishes; the
  duplicate is harmless now that a run takes a test case once.
- Idempotency keys are per tenant: two pipelines of one tenant that both use "123" collide. The README says to build the key from the pipeline and the build.
- Lookups by Automation ID lower-case the column (to behave the same on every database), which an index cannot serve; at tens of thousands of test cases add
  an index on the lowered value for your database. The code of a test case is compared as the database compares text (case sensitive on SQLite and
  PostgreSQL, not on SQL Server).
- The dashboard and the flaky list still read every run item and defect of the scope into memory (with two correlated subqueries per item). It is correct, but it
  grows with the data: to be measured with a large data set and, if it needs it, rewritten as aggregates in the database. Not done yet.
- Tested on SQLite only; the constructs that would behave differently on other providers were looked for (sorting, case, `LOWER`), not run.

### 4.17. Phase 18: the module on MySQL, and on a large data set

The company's application uses MySQL, so the whole test suite was run against a real MySQL 8.4 server (a Docker container with its data in memory), not only
against SQLite.

**How.** The package `Volo.Abp.EntityFrameworkCore.MySQL` (the one the ABP application template chooses; it sits on Oracle's `MySql.EntityFrameworkCore`; the
Pomelo variant, `Volo.Abp.EntityFrameworkCore.MySQL.Pomelo`, was not run) is added to the test base and to the sample host. Setting the environment variable
`TCM_TEST_MYSQL` (for example `Server=localhost;Port=3307;User ID=root;Password=...`, no database name) makes `TestCaseManagementTestBaseModule` use MySQL: one
database is created for the run (its schema built by EF Core from the module's model, which also shows that the model creates on MySQL) and emptied before each test,
so the tests must not run in parallel: `dotnet test --settings test/mysql.runsettings`. The HTTP tests start the sample host on MySQL with `Host:Database=MySql` in a
database of their own. With the variable unset nothing changes (in-memory SQLite).

**Result.** All suites pass on MySQL: Domain 211, Application 375 (374 plus the scale test, which does nothing unless asked) and HTTP API 79. Two tests needed a change,
both because they assumed what SQLite does, not because the module was wrong: one looked a defect up with `IssueKey == "bug-1"` and expected not to find `BUG-1` (MySQL's default
collation compares text without regard to case, so the lookup is now done in memory), and one compared a stored time with the value that was returned before it was stored (MySQL
keeps six decimals of a second, .NET seven; the comparison now has a tolerance of a millisecond). Everything else (the sorting with a unique last key, the case-insensitive
Automation ID lookup, the locks, the imports and exports, the dashboard queries, the attachments, the quality gate) behaved the same.

**Large data set** (`Scale_Tests`, run with `TCM_SCALE=<test cases>`; it prints a time per operation): 10,000 test cases with 100 runs of 1,000 items each, that is 100,000 run
items and about 133,000 attempts, on the same machine that ran everything else, so the figures are only good for order of magnitude and for comparing.

| Operation | 1,000 test cases (1,000 items) | 5,000 (25,000 items) | 10,000 (100,000 items) |
|---|---|---|---|
| Dashboard of every run | 1.1 s | 3.7 s | 5.9 to 7.8 s |
| Dashboard of one plan | 0.3 s | 2.3 s | 5.1 to 5.6 s |
| List of flaky tests | 0.7 s | 2.7 s | 5.7 to 6.8 s |
| Quality gate evaluation of a plan | 0.3 s | 1.2 s | 2.7 s |
| Test cases: first page, filter on text, page 100, with an Automation ID | 0.1 to 1 s | 0.1 to 0.9 s | 0.1 to 0.5 s |

The lists of test cases stay fast whatever the size (they page in the database). The dashboard, the flaky list and the quality gate read every run item and attempt of their
scope: of the 5.9 seconds of the dashboard at 100,000 items, 5.1 are the queries (reading about 370,000 rows through the MySQL connector) and 0.5 the arithmetic. A rewrite of the items
query without correlated subqueries was tried and did not help, so it was dropped. What would help is computing the sums in the database (counts by status, attempts by day) instead of
reading rows, which is provider-specific work (dates, windows) and a change of the repository contract; a short-lived cache of the dashboard would help the repeated views. Neither is
done: at the size of a company's test library (a few thousand test cases) the dashboard answers in about one to three seconds.

**Found while measuring.** ABP matches the entities of a unit of work against each other when it ends (domain events), which is quadratic: seeding 500,000 rows in one unit of work took
more than 15 minutes of CPU before it was stopped. Tools and imports that insert very many rows should commit in units of a few thousand. The module's own import is bounded (10,000 rows) and was not affected.

**Not covered.** The Pomelo provider; MySQL 5.7 or MariaDB (only 8.4 was run); a migration made with `dotnet ef` for a MySQL DbContext (the schema was created from the model); more than one
server node; the dashboard queries rewritten as aggregates.

### 4.18. Phase 19: assigning testers in the run page

The back end could already assign a person to each item of a run (`AssignedUserId`, `PUT /runs/{id}/items/{itemId}/assignee`, and an assignee when test cases are added) and
required `TestPlans.Manage` for it, but no screen used it. The run page now does.

**What the page does.** The table of a run has a *Tester* column. Who holds `TestPlans.Manage` sees a select per item on a run that is not completed (a completed run
shows the name only); a person who may only execute sees the name. The dialog that adds test cases offers "Assign the new test cases to". Above the table a filter shows
*All testers*, *My tests*, *Not assigned* or one person, each with how many of their items are done out of how many they have (`Ann Lee (3/5)`). A failed assignment
puts the select back to what the server has. Anyone may still execute an item that is assigned to someone else: the assignment says who is expected to, not who may.

**The users belong to the host.** The module has no user table, so the host provides the list through a new contract, `TCM_USER_DIRECTORY` (`list(): Observable<{ id, userName, displayName }[]>`).
The default knows nobody: the assignment, the filter and the dialog's select are then hidden and an assigned person shows as "Assigned". A directory that fails (for example no
permission to list users) is treated the same way, silently.
- *Standalone app:* the sample host has a small `GET /api/host/users` (signed-in users, the active ones, at most 100).
- *ABP application:* `provideTestCaseManagementForAbp()` registers an adapter that reads ABP's user lookup and, when that is refused, the Identity user list.
  Found while trying it: the lookup has a permission of its own (`AbpIdentity.UserLookup`) that a standard ABP application does not define, so even `admin` gets a 403
  from it; the fallback needs `AbpIdentity.Users`. A role that assigns testers but holds neither sees no assignment. A host that wants it for such roles provides
  its own `TCM_USER_DIRECTORY` (an endpoint of its own that lists the people of the project).

**Tests.** 9 unit tests of the page (a select per item and the request it sends, clearing, who may not assign, a completed run, no directory, a failing directory, the three filters and
the counts, the dialog, a refused change), 2 HTTP tests of the sample host's list. Tried in the browser in the ABP sample application: two users made through ABP's API, assigned
from the run page, filtered.

**Not done.** A notification to the person who is assigned; a "my work" page across runs (the filter is per run); limiting execution to the assignee; assigning many items at once
from the table (the dialog assigns the test cases it adds); more than 100 people in the list (it would need a search box).

## 5. Security, RBAC & Permissions

Defined in `TestCaseManagementPermissions`:
- `TestCaseManagement.TestCases.Default` / `Create` / `Update` / `Delete` / `Approve`
- `TestCaseManagement.TestSuites.Default` / `Manage`
- `TestCaseManagement.TestPlans.Default` / `Manage`
- `TestCaseManagement.TestRuns.Default` / `Execute`
- `TestCaseManagement.Requirements.Default` / `Manage` (added in Phase 2 for the RTM user story)
- `TestCaseManagement.QualityGates.Default` / `Manage` (added in Phase 2 for the Quality Gate user story)
- `TestCaseManagement.SignOff.Default` / `Approve` (Approve reserved for QA Lead / Release Manager)
