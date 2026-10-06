---
description: "Task list for Acme.TestCaseManagement implementation following Spec Kit SDD format"
---

# Tasks: Test Case Management (TCM) Reusable Module

**Input**: Design documents from `/specs/001-test-case-management/` (`spec.md`, `plan.md`)  
**Prerequisites**: `plan.md` (completed), `spec.md` (completed), `/.specify/memory/constitution.md` (ratified)  
**Target Solution**: `Acme.TestCaseManagement.sln` (.NET 10, ABP Framework 10.6.1) in `/Acme.TestCaseManagement/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Module solution scaffolding and project reference setup.

- [X] T001 Scaffold ABP Application Module solution structure (`Acme.TestCaseManagement.sln`) with Domain.Shared, Domain, Application.Contracts, Application, EntityFrameworkCore, HttpApi projects.
- [X] T002 [P] Install core NuGet packages across projects (`Volo.Abp.Ddd.Domain`, `Volo.Abp.AutoMapper`, `Volo.Abp.EntityFrameworkCore`, `Volo.Abp.AspNetCore.Mvc`).
- [X] T003 [P] Configure `.editorconfig`, C# nullable reference types, and standard compiler warning rules.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared enums, domain base types, and database context foundation.

**⚠️ CRITICAL**: Must be completed before implementing any user story entities.

- [X] T004 Define domain enums in `Acme.TestCaseManagement.Domain.Shared/Enums/` (`PriorityLevel`, `SeverityLevel`, `TestCaseStatus`, `TestResultStatus`, `ExecutionType`, `TestKind`, `TestLayer`).
- [X] T005 [P] Create error codes and localization resources in `Acme.TestCaseManagement.Domain.Shared/Localization/` and `TestCaseManagementErrorCodes.cs`.
- [X] T006 [P] Define permission constants and definitions in `Acme.TestCaseManagement.Application.Contracts/Permissions/TestCaseManagementPermissions.cs`.
- [X] T007 Setup `ITestCaseManagementDbContext` and `TestCaseManagementDbContext` in `Acme.TestCaseManagement.EntityFrameworkCore/`.
- [X] T008 Configure entity table prefixes (`TcmTestCases`, `TcmSuites`, etc.) and multi-tenant filters in `TestCaseManagementDbContextModelCreatingExtensions.cs`.

**Checkpoint**: Foundation ready - User Story 1 and 2 implementation can proceed.

---

## Phase 3: User Story 1 - Master Test Case Authoring & Suite Organization (Priority: P1) 🎯 MVP

**Goal**: Provide full CRUD for hierarchical Test Suites and Master Test Cases with ordered steps and automatic `TestCaseVersion` snapshot generation.

**Independent Test**: Create a suite tree, author a test case with 3 steps, transition from Draft to Approved, and verify that `TestCaseVersion` (v1) is created.

### Tests for User Story 1
- [X] T009 [P] [US1] Unit test for `TestSuiteManager.ValidateParentHierarchyAsync` verifying circular dependency detection in `test/Acme.TestCaseManagement.Domain.Tests/Suites/TestSuiteManager_Tests.cs`.
- [X] T010 [P] [US1] Unit test for `TestCaseManager.PublishNewVersionAsync` verifying version number increments and step snapshot JSON integrity in `test/Acme.TestCaseManagement.Domain.Tests/TestCases/TestCase_Versioning_Tests.cs`.

### Implementation for User Story 1
- [X] T011 [P] [US1] Create `TestSuite` aggregate root with self-referencing `ParentId` in `src/Acme.TestCaseManagement.Domain/Suites/TestSuite.cs`.
- [X] T012 [P] [US1] Implement `TestSuiteManager` domain service with cycle prevention logic in `src/Acme.TestCaseManagement.Domain/Suites/TestSuiteManager.cs`.
- [X] T013 [P] [US1] Create `TestCase` aggregate root, `TestStep` entity, and `TestCaseVersion` snapshot entity in `src/Acme.TestCaseManagement.Domain/TestCases/`.
- [X] T014 [US1] Implement `TestCaseManager` domain service to handle approval and snapshot serialization in `src/Acme.TestCaseManagement.Domain/TestCases/TestCaseManager.cs`.
- [X] T015 [US1] Configure EF Core mappings for `TestSuite`, `TestCase`, `TestStep`, and `TestCaseVersion` in `TestCaseManagementDbContextModelCreatingExtensions.cs`.
- [X] T016 [P] [US1] Define DTOs for Suites (`TestSuiteDto`, `CreateTestSuiteDto`, `TestSuiteTreeDto`) in `Application.Contracts/Suites/Dtos/`.
- [X] T017 [P] [US1] Define DTOs for Test Cases (`TestCaseDto`, `CreateUpdateTestCaseDto`, `TestStepDto`, `TestCaseVersionDto`) in `Application.Contracts/TestCases/Dtos/`.
- [X] T018 [US1] Implement `TestSuiteAppService` with tree building and drag-drop reordering in `src/Acme.TestCaseManagement.Application/Suites/TestSuiteAppService.cs`.
- [X] T019 [US1] Implement `TestCaseAppService` with filtering, step reordering, and version history in `src/Acme.TestCaseManagement.Application/TestCases/TestCaseAppService.cs`.
- [X] T020 [P] [US1] Expose REST endpoints in `TestSuiteController.cs` and `TestCaseController.cs` in `src/Acme.TestCaseManagement.HttpApi/Controllers/`.

**Checkpoint**: Master Test Case authoring and Suite hierarchy fully operational.

---

## Phase 4: User Story 2 - Test Plan, Test Run & Multi-Attempt Execution (Priority: P1) 🎯 MVP

**Goal**: Implement sprint test planning, environment-bound test runs, test item assignments with immutable version snapshot binding, and append-only execution logging.

**Independent Test**: Create a Test Run, add test items, execute Attempt #1 (Failed), execute Attempt #2 (Passed), and verify that both attempts are stored and First-Time Pass Rate is accurately computed.

### Tests for User Story 2
- [X] T021 [P] [US2] Integration test verifying that executing a test item twice creates 2 discrete `TestExecution` records and does NOT mutate the master `TestCase` in `test/Acme.TestCaseManagement.Application.Tests/TestRunAppService_Tests.cs`.
- [X] T022 [P] [US2] Domain test verifying that `TestRunItem` permanently binds to `TestCaseVersionId` even if the library test case is updated in `test/Acme.TestCaseManagement.Domain.Tests/Runs/TestRun_MultiAttempt_Tests.cs`.

### Implementation for User Story 2
- [X] T023 [P] [US2] Create `TestPlan` aggregate root in `src/Acme.TestCaseManagement.Domain/Plans/TestPlan.cs`.
- [X] T024 [P] [US2] Create `TestRun` aggregate root, `TestRunItem` entity, and `TestExecution` entity in `src/Acme.TestCaseManagement.Domain/Runs/`.
- [X] T025 [US2] Implement `TestRunManager` domain service to handle execution attempt sequencing, item status rollups, and run completion percentage in `src/Acme.TestCaseManagement.Domain/Runs/TestRunManager.cs`.
- [X] T026 [US2] Configure EF Core mappings for `TestPlan`, `TestRun`, `TestRunItem`, and `TestExecution` with indexes on `TestRunId` and `TestCaseVersionId`.
- [X] T027 [P] [US2] Define DTOs for Plans (`TestPlanDto`, `CreateTestPlanDto`) in `Application.Contracts/Plans/Dtos/`.
- [X] T028 [P] [US2] Define DTOs for Runs and Executions (`TestRunDto`, `CreateTestRunDto`, `ExecuteTestItemDto`, `TestExecutionDto`) in `Application.Contracts/Runs/Dtos/`.
- [X] T029 [US2] Implement `TestPlanAppService` in `src/Acme.TestCaseManagement.Application/Plans/TestPlanAppService.cs`.
- [X] T030 [US2] Implement `TestRunAppService` (create run, assign items, record execution attempt, batch execute) in `src/Acme.TestCaseManagement.Application/Runs/TestRunAppService.cs`.
- [X] T031 [P] [US2] Expose REST endpoints in `TestPlanController.cs` and `TestRunController.cs` in `src/Acme.TestCaseManagement.HttpApi/Controllers/`.

**Checkpoint**: Core test execution lifecycle with multi-attempt fidelity fully working.

---

## Phase 5: User Story 3 - Defect Linking & Issue Synchronization (Priority: P2)

**Goal**: Link failed test executions to external defect tracking keys (Jira, GitHub) and display linked defects across test history.

- [X] T032 [P] [US3] Create `DefectLink` entity with `TestExecutionId`, `ExternalSystem`, `IssueKey`, `IssueUrl` in `src/Acme.TestCaseManagement.Domain/Quality/DefectLink.cs`.
- [X] T033 [US3] Configure EF Core mapping for `DefectLink` with foreign key to `TestExecution`.
- [X] T034 [P] [US3] Define DTOs and methods in `TestRunAppService` to attach defect keys during or after execution.
- [X] T035 [US3] Expose endpoint `POST /api/test-case-management/executions/{id}/defects` in `TestRunController.cs`.

---

## Phase 6: User Story 4 - Requirements Traceability Matrix (RTM) (Priority: P2)

**Goal**: Model Requirements, link them to Test Cases, and compute coverage percentages and defect rollups.

- [X] T036 [P] [US4] Create `Requirement` aggregate root and `RequirementTestCase` join entity in `src/Acme.TestCaseManagement.Domain/Requirements/`.
- [X] T037 [US4] Configure EF Core mappings for `Requirement` and `RequirementTestCase` with composite primary key.
- [X] T038 [P] [US4] Create DTOs (`RtmMatrixDto`, `RequirementCoverageDto`) in `Application.Contracts/Rtm/Dtos/`.
- [X] T039 [US4] Implement `RtmAppService` with coverage calculations (Total Requirements, % Covered, % Passed) in `src/Acme.TestCaseManagement.Application/Rtm/RtmAppService.cs`.
- [X] T040 [P] [US4] Expose endpoint `GET /api/test-case-management/rtm` in `RtmController.cs`.

---

## Phase 7: User Story 5 - Quality Gate Enforcement & Sign-Off (Priority: P3)

**Goal**: Evaluate release readiness against quantitative quality rules and archive signed milestone certificates.

- [X] T041 [P] [US5] Create `QualityGate` and `SignOffReport` aggregate roots in `src/Acme.TestCaseManagement.Domain/Quality/`.
- [X] T042 [US5] Implement `QualityGateManager` domain service to evaluate criteria (PassRate >= X, OpenCriticalDefects == 0, 100% P1 executed).
- [X] T043 [US5] Implement `SignOffAppService` to evaluate gates, freeze summary statistics in JSON, and record sign-off approvals.
- [X] T044 [P] [US5] Expose endpoints `POST /api/test-case-management/quality-gates/evaluate` and `POST /api/test-case-management/sign-off`.

---

## Phase 8: Polish, Packaging & CI/CD Verification

**Goal**: Package module for consumption by host applications and verify test suites.

- [X] T045 Configure `Acme.TestCaseManagement.sln` build scripts and NuGet package metadata (.nuspec / csproj properties).
- [X] T046 Run full test suite (`dotnet test`) across domain and application test projects; verify all tests pass.
- [X] T047 Validate Swagger / OpenAPI contract generation in `HttpApi` module.

---

## Phase 9: Import / Export of test cases and execution results (FR-018)

**Goal**: Exchange test case libraries and execution results with Excel (.xlsx) and CSV files, the way teams move data in and out of other test management tools. Added after Phase 8 on request; first of three follow-up phases (9: import/export FR-018, 10: CI/CD ingestion FR-020, 11: flaky detection and dashboard FR-012/FR-025).

- [X] T048 [P] Add `DocumentFormat.OpenXml` (MIT, Microsoft) and a small tabular layer in `Application/Transfer/Tabular/`: CSV reader/writer (RFC 4180, delimiter detection, BOM, CSV formula-injection guard) and XLSX reader/writer, behind one `Table` type; with limits (file size, row count, uncompressed size).
- [X] T049 [P] Add enums `TransferFormat`, `ImportConflictMode`, `ImportOutcome`, the contracts (`ITestCaseTransferAppService`, `ITestResultTransferAppService`, input and report DTOs) and the localized messages (en, vi).
- [X] T050 Implement `TestCaseTransferAppService`: export with the library filters; import in two passes (validate everything, then write all or nothing) with dry run, suite paths, skip or update of existing codes, unchanged detection.
- [X] T051 Implement `TestResultTransferAppService`: export every attempt of a run; import results by test case code into a run (through the existing batch execution, all or nothing) with dry run.
- [X] T052 [P] Expose `GET test-cases/export`, `POST test-cases/import`, `GET runs/{runId}/results/export`, `POST runs/{runId}/results/import` in two controllers; update the OpenAPI contract test and the controller conventions test.
- [X] T053 Tests: tabular layer, mapping and validation rules, app services (round trip is idempotent, dry run writes nothing, a bad row blocks the whole file), HTTP (multipart upload, download, roles).
- [X] T054 Angular: export (current filters, CSV or Excel) and import dialog with "check file" before import on the repository page; export and import of results on the run page; English and Vietnamese texts; unit tests and a browser run.
- [X] T055 Document the format and the decisions (plan.md 4.8, README); run the full build.
