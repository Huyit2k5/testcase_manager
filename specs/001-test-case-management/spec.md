# Feature Specification: Test Case Management (TCM) Reusable Module

**Feature Branch**: `001-test-case-management`  
**Created**: 2026-10-06  
**Status**: Ready for Planning & Implementation  
**Specification Standard**: GitHub Spec Kit (Spec-Driven Development - SDD)  
**Input Source**: [BUSINESS_ANALYSIS_TEST_CASE_MANAGEMENT.md](file:///d:/quanlytestcase/BUSINESS_ANALYSIS_TEST_CASE_MANAGEMENT.md)

---

## Executive Summary & Problem Statement

Currently, development teams manage test cases and test execution using fragmented spreadsheets, Jira sub-tasks, or expensive external SaaS tools (TestRail, Zephyr). This causes:
1. **Lost historical test execution fidelity**: Retesting an existing test case overwrites the previous results, making it impossible to compute First-Time Pass Rates (FTPR) or defect reopen metrics.
2. **Unfrozen test scripts**: Modifying a test case in the master library invalidates older release audit trails.
3. **Lack of deep integration with enterprise .NET systems**: Inability to easily embed test case management into internal ABP Framework-based enterprise portals with unified RBAC, multi-tenancy, and audit logging.

This specification defines the complete functional requirements for **Acme.TestCaseManagement**, an enterprise-grade, reusable ABP Application Module that provides master test case authoring, hierarchical suite organization, version snapshotting, test plan & run execution with multi-attempt result capture, RTM traceability, quality gates, and automated sign-off reporting.

---

## User Scenarios & Prioritized User Stories

### User Story 1 - Master Test Case Authoring & Suite Organization (Priority: P1 - MVP Core)

As a **QA Engineer / Tester**,  
I want to create, categorize, and maintain structured test cases with ordered steps, preconditions, expected outcomes, and rich metadata within a hierarchical folder/suite tree,  
So that our team establishes a centralized, reusable test case repository for the entire software lifecycle.

**Why this priority**: Without the master library and suite tree, no test execution, planning, or reporting is possible. This is the foundation of the entire system.

**Independent Test**: Can be tested independently by creating a suite tree (`Authentication -> Login`), creating test cases with 3 ordered steps, editing steps, and verifying retrieval via REST API.

**Acceptance Scenarios**:
1. **Given** an authenticated QA user with `TestCaseManagement.TestCases.Create` permission,  
   **When** they create a new test case with Code `"TC-AUTH-001"`, Title `"Verify successful login with valid credentials"`, Priority `High`, Severity `Critical`, and 2 test steps,  
   **Then** the test case is persisted with status `Draft`, assigned a unique UUID, and linked to the designated `SuiteId`.
2. **Given** an existing test case in `Draft` status,  
   **When** the QA Lead reviews and approves the test case,  
   **Then** the status transitions to `Approved`, and an immutable `TestCaseVersion` (v1) snapshot is automatically generated.
3. **Given** a parent Test Suite `"Orders"`,  
   **When** a user creates a child suite `"Checkout"` with `ParentId = Orders.Id`,  
   **Then** the system renders a recursive tree hierarchy without circular reference errors.

---

### User Story 2 - Test Plan, Test Run & Multi-Attempt Execution (Priority: P1 - MVP Core)

As a **Tester / Test Runner**,  
I want to assign test cases to a specific Test Run on a target environment (e.g., Staging), execute each item, and record execution results (Passed/Failed/Blocked) with actual output and execution duration,  
So that sprint testing can be conducted systematically with full attempt history preserved.

**Why this priority**: Test execution is the primary daily activity of QA teams. Delivering this enables immediate operational value.

**Independent Test**: Can be tested by creating a Test Plan for "Sprint 24", adding a Test Run for "Staging Chrome", adding 5 test items, executing item #1 with `Failed`, re-testing with `Passed`, and verifying that 2 discrete `TestExecution` records exist.

**Acceptance Scenarios**:
1. **Given** an active Test Run on `"Staging"`,  
   **When** test items are added from the approved library,  
   **Then** each `TestRunItem` captures the immutable `TestCaseVersionId` of the test case at that exact moment.
2. **Given** an assigned `TestRunItem` in `Untested` state,  
   **When** a tester executes the item and marks it `Failed` with actual result `"Button disabled"`, duration `45s`,  
   **Then** a new `TestExecution` record (Attempt #1) is created, and the item's `CurrentStatus` becomes `Failed`.
3. **Given** a `TestRunItem` currently marked as `Failed`,  
   **When** the tester re-tests the item after a bug fix and marks it `Passed`,  
   **Then** a second `TestExecution` record (Attempt #2) is inserted without modifying or deleting Attempt #1, and the item's `CurrentStatus` updates to `Passed`.

---

### User Story 3 - Defect Linking & Issue Synchronization (Priority: P2)

As a **QA Engineer or Developer**,  
I want to link a failed test execution directly to an external bug ticket (e.g., Jira issue `PROJ-1234` or GitHub Issue #42),  
So that developers can immediately see the exact reproduction steps, logs, and screenshots directly from the defect ticket.

**Why this priority**: Connects testing directly to developer defect resolution workflows, eliminating manual copy-pasting of reproduction steps.

**Independent Test**: Can be tested by executing a test item to `Failed`, invoking `AddDefectLinkAsync` with `ExternalSystem = "Jira"` and `IssueKey = "BUG-88"`, and verifying the link appears in both test execution details and RTM queries.

**Acceptance Scenarios**:
1. **Given** a `Failed` test execution record,  
   **When** a tester inputs an external defect URL and ticket key,  
   **Then** a `DefectLink` entity is created and linked to the `TestExecutionId`.
2. **Given** multiple executions for a test item that failed across 2 separate runs,  
   **When** viewing the test case details,  
   **Then** all linked defects across all historical executions are listed chronologically.

---

### User Story 4 - Requirements Traceability Matrix (RTM) & Coverage (Priority: P2)

As a **Product Owner / QA Lead**,  
I want to link User Stories / Requirements to their corresponding Test Cases and view a real-time Traceability Matrix,  
So that I can identify untested requirements (coverage gaps) and assess release readiness.

**Why this priority**: Required for compliance, enterprise auditability, and verifying 100% test coverage of business features.

**Independent Test**: Can be tested by creating 3 Requirements, linking Test Cases to 2 of them, and generating the RTM matrix to verify that the 3rd requirement is flagged as `0% Coverage (Uncovered)`.

**Acceptance Scenarios**:
1. **Given** a Requirement `"REQ-AUTH-01: Two-Factor Authentication"`,  
   **When** linked to test cases `"TC-AUTH-005"` and `"TC-AUTH-006"`,  
   **Then** the requirement status reflects test execution outcomes of those linked cases.
2. **Given** a Milestone with 10 requirements,  
   **When** the RTM summary query is executed,  
   **Then** the system outputs: Total Requirements, Covered Requirements (%), Passed Requirements, and a list of blocking open defects per requirement.

---

### User Story 5 - Quality Gate Enforcement & Formal Sign-Off Report (Priority: P3)

As a **Release Manager / Tech Lead**,  
I want the system to automatically validate quantitative Quality Gates (e.g., Pass Rate >= 95%, 0 Critical unresolved bugs) before allowing a Milestone Sign-Off,  
So that buggy releases are blocked from deploying to Production without documented management override.

**Why this priority**: Guarantees delivery quality and creates permanent, signed audit certificates for enterprise governance.

**Independent Test**: Can be tested by configuring a Quality Gate with `MinPassRate = 95%` and `MaxCriticalDefects = 0`, attempting to sign off a Milestone with 90% pass rate (fails with error), resolving test cases to 96% pass rate, and signing off successfully.

**Acceptance Scenarios**:
1. **Given** a Test Plan with pass rate 92% and a Quality Gate requiring 95%,  
   **When** a user attempts to execute Sign-Off,  
   **Then** the system blocks the action with a detailed validation breakdown of failed gate criteria.
2. **Given** all Quality Gate criteria satisfied,  
   **When** the QA Lead and Product Owner submit their digital approval,  
   **Then** a `SignOffReport` is generated, freezing the summary statistics into an immutable snapshot.

---

## Functional Requirements (FR) Breakdown

| Requirement ID | Module / Area | Description | Related Use Case |
|---|---|---|---|
| **FR-001** | Test Suite | Hierarchical tree management (CRUD, drag-and-drop reordering, circular dependency prevention). | UC-01 |
| **FR-002** | Test Case | Master test case authoring (Code, Title, Precondition, Postcondition, Priority, Severity, Kind, Layer). | UC-02 |
| **FR-003** | Test Step | Ordered step management with discrete Action and Expected Outcome fields. | UC-03 |
| **FR-004** | Versioning | Automatic creation of immutable `TestCaseVersion` upon approval or modification. | UC-04 |
| **FR-005** | Reusable Steps | Shared step libraries that can be embedded into multiple test cases by reference. | UC-05 |
| **FR-006** | Test Plan | Milestone and Sprint test planning, defining scope, dates, and target environments. | UC-06 |
| **FR-007** | Test Run | Creation of execution runs bound to specific environments (Staging, Prod, Mobile OS). | UC-07 |
| **FR-008** | Run Items | Allocation of test case version snapshots to test runs with tester assignment. | UC-08 |
| **FR-009** | Execution | Multi-attempt execution logging (Passed, Failed, Blocked, Skipped) with execution duration. | UC-09 |
| **FR-010** | Attempt History | Append-only execution history preserving all attempts without in-place updates. | UC-10 |
| **FR-011** | Defect Link | Bi-directional linking of failed executions to external bug tracking keys (Jira, GitHub). | UC-11 |
| **FR-012** | Flaky Detection | Automatic identification and scoring of tests with flipping pass/fail results. | UC-12 |
| **FR-013** | Requirements | Requirement inventory management (User Stories, Acceptance Criteria). | UC-13 |
| **FR-014** | RTM Matrix | Generation of 2-way Requirements Traceability Matrix with coverage percentage. | UC-14 |
| **FR-015** | Attachments | Attachment upload (screenshots, crash logs, videos) bound to test executions or cases. | UC-15 |
| **FR-016** | Quality Gate | Rule engine evaluating release readiness metrics against configurable thresholds. | UC-16 |
| **FR-017** | Sign-Off | Generation and immutable archiving of milestone sign-off reports. | UC-17 |
| **FR-018** | Import / Export | Excel and CSV import/export for test case libraries and execution results. | UC-18 |
| **FR-019** | Automation ID | Unique key mapping linking manual test cases to automated test scripts (Playwright, xUnit). | UC-19 |
| **FR-020** | CI/CD Webhook | REST API endpoints for automated test runners to publish execution results in bulk. | UC-20 |
| **FR-021** | Audit Trail | Full ABP entity audit logging (`CreatorId`, `CreationTime`, `LastModifierId`, `DeleterId`). | UC-21 |
| **FR-022** | Multi-Tenancy | Tenant isolation via ABP `IMultiTenant` and EF Core global query filters. | UC-22 |
| **FR-023** | Permissions | Granular RBAC permissions for Author, Reviewer, Tester, Lead, and Administrator. | UC-23 |
| **FR-024** | Search & Filter | Full-text search and multi-facet filtering (Priority, Severity, Suite, Tag, Automation status). | UC-24 |
| **FR-025** | Dashboard Metrics | Real-time calculation of Pass Rate, Execution Velocity, Burn-down, and Defect Density. | UC-25 |
| **FR-026** | Bulk Execution | Batch update capabilities for marking multiple test items in automated or regression runs. | UC-26 |
| **FR-027** | AI Assistance | Integration hook for AI-powered test step generation from requirements text. | UC-27 |

---

## Data Models & Entity Relationships

```text
TestSuite (1) ──────────< (N) TestCase (1) ──────────< (N) TestStep
                               │
                               ├──────< (N) TestCaseVersion (Snapshot)
                               │                 │
                               │                 └──────────┐
                               │                            │
Requirement (1) ──< (N) RequirementTestCase >── (1) TestCase│
                                                            │
TestPlan (1) ──────────< (N) TestRun (1) ─────────< (N) TestRunItem
                                                            │
                                                            ├──────< (N) TestExecution (Attempt 1..N)
                                                            │                 │
                                                            │                 ├──────< (N) DefectLink
                                                            │                 └──────< (N) Attachment
                                                            │
QualityGate ──────────> SignOffReport (Frozen Snapshot)
```

---

## Edge Cases & Mitigation Strategies

1. **Test Case Modified Mid-Run**:
   - *Scenario*: Tester is running Test Run A. An author edits Test Case #10 in the master library.
   - *Mitigation*: The Test Run Item is permanently bound to `TestCaseVersionId` (Snapshot v1). The running test continues with v1. A non-blocking banner indicates a newer version v2 is available in library.
2. **Infinite Recursion in Suite Tree**:
   - *Scenario*: User moves Suite A to be a child of Suite B, where Suite B is already inside Suite A.
   - *Mitigation*: Domain service checks ancestor tree before saving `ParentId`. Circular dependencies throw a `BusinessException("TCM:0001:CircularSuiteDependency")`.
3. **Flaky Test Result Flooding**:
   - *Scenario*: CI/CD pipeline retries a test 10 times in 3 minutes, generating 10 execution logs.
   - *Mitigation*: Test runner API accepts a batch payload with attempt index (`AttemptNumber`) and flags intermittent runs as `IsFlaky = true`.
4. **Large Attachment Uploads**:
   - *Scenario*: User uploads 100MB screen recording on test execution.
   - *Mitigation*: Uploads use ABP BLOB Storing with configurable maximum file size limit (default 25MB) and mime-type whitelist.
