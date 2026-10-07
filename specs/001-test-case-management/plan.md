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
(UTC). `LastUsedAt` is written at most once a minute and is best effort.

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
  returns the stored answer without recording again, and the same key with a different request is refused.

**FR-019.** The Automation ID now names one test case. It is checked when a test case is created and when its Automation ID changes
(a legacy duplicate can still be edited until the ID itself is changed). The import reports a clash, with the library or inside the
file, as an invalid row.

**Front end.** The Automation page lists keys, creates one (the secret is in a dialog with a copy button and is gone once the dialog is
closed), revokes with a confirmation, and shows a curl and a GitHub Actions example. The tab is shown only with `ApiKeys`.

**Findings while testing.** A wide `<pre>` pushed the dialog off the screen (the snippet now wraps). The browser run proves the whole
chain: create a key, publish with it, a wrong key gets 401, revoke it, the revoked key gets 401.

**Not covered.**
- A result made through a key has no creator (there is no user).
- Multi-tenant behavior is designed for (keys are found across tenants, data is per tenant) but not tested.
- The unique index of the idempotency key does not protect a null tenant on SQLite and PostgreSQL (nulls are distinct there); the
  service checks first, so only a race can slip through.
- Publications are never cleaned up, there is no rate limiting, and `LastUsedAt` can lag by a minute.
- Tested on SQLite only.

## 5. Security, RBAC & Permissions

Defined in `TestCaseManagementPermissions`:
- `TestCaseManagement.TestCases.Default` / `Create` / `Update` / `Delete` / `Approve`
- `TestCaseManagement.TestSuites.Default` / `Manage`
- `TestCaseManagement.TestPlans.Default` / `Manage`
- `TestCaseManagement.TestRuns.Default` / `Execute`
- `TestCaseManagement.Requirements.Default` / `Manage` (added in Phase 2 for the RTM user story)
- `TestCaseManagement.QualityGates.Default` / `Manage` (added in Phase 2 for the Quality Gate user story)
- `TestCaseManagement.SignOff.Default` / `Approve` (Approve reserved for QA Lead / Release Manager)
