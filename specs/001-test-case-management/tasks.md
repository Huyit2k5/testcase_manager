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

---

## Phase 10: CI/CD ingestion of automated results (FR-020, FR-019)

**Goal**: Let a pipeline publish test results in bulk without a person signing in: its own credential (an API key that can do nothing else), results matched to test cases by Automation ID, safe retries, and the flaky flag of the spec's edge case 3. Second of three follow-up phases (11: flaky detection and dashboard, FR-012/FR-025).

- [x] T056 [P] Domain: `ApiKey` aggregate (only a SHA-256 hash is stored; prefix, expiry, revocation, last use), `ApiKeyManager` (generate, validate in constant time), `AutomationPublication` (idempotency record); Automation ID made unique as FR-019 says (`TestCaseManager`, repository lookups).
- [x] T057 [P] EF Core mappings and DbSets for the new entities (module context and interface); contracts: permissions (`ApiKeys`, `AutomationResults.Publish`), enums, DTOs, `IApiKeyAppService`, `IAutomationResultsAppService`, `IApiKeyValidator`; localized messages (en, vi).
- [x] T058 `ApiKeyAppService` (list, create with the secret shown once, revoke), `ApiKeyValidator`, and an ABP permission value provider that grants an API key principal the single permission to publish results.
- [x] T059 `AutomationResultsAppService.PublishAsync`: match by Automation ID, find or create the run, schedule missing approved test cases, record attempts in order, flag flaky tests, optional completion, per-result outcomes, strict mode, idempotency key with replay.
- [x] T060 [P] HttpApi: `ApiKeyController`, `AutomationResultsController`, the `X-Api-Key` authentication handler and `AddTestCaseManagementApiKey`; host: policy scheme choosing JWT or API key, new DbSets, Swagger scheme; contract and convention tests.
- [x] T061 Tests: key generation and validation, publishing rules (matching, ambiguity, strictness, retries and flaky, run creation and completion, idempotency), Automation ID uniqueness, HTTP with real keys (valid, revoked, expired, wrong, scope limits, roles).
- [x] T062 Angular: an Automation page to create, list and revoke API keys (secret shown once, copy button) with a ready-made curl and CI example; English and Vietnamese; unit tests and a browser run.
- [x] T063 Document the contract and the decisions (plan.md 4.9, README with a GitHub Actions example); run the full build.

---

## Phase 11: Flaky detection and dashboard (FR-012, FR-025)

**Goal**: Find tests whose results flip between pass and fail from their history, and show the metrics of the spec's dashboard (pass rate, execution velocity, burn-down, defect density). Third of three follow-up phases. Formulas follow industry practice (plan.md 4.10): the flakiness score is the transition score used by Buildkite Test Engine (status changes between consecutive outcomes in a window), the burn-down has the ideal line of QA Touch and AIO Tests, and defect density is counted per 100 executed test cases.

- [x] T064 [P] Domain: `FlakinessCalculator` (window, minimum observations, transition score, level), `DashboardCalculator` (velocity, burn-down with ideal line and projection, defect density), models and the `FlakinessLevel` enum; `IInsightsRepository`.
- [x] T065 [P] EF Core: `EfCoreInsightsRepository` (items with first execution time, attempts in a lookback window, defect links of the scope runs).
- [x] T066 Application and contracts: `FlakyTestAppService` (list with scores, apply flags to the library, optional un-flag of recovered tests) and `DashboardAppService`; DTOs; options (window, thresholds, lookback); localized messages.
- [x] T067 [P] HttpApi: `FlakyTestController`, `DashboardController`; contract and convention tests.
- [x] T068 Tests: calculators (patterns, window, ignored statuses, boundaries, burn-down edge cases, projection), services with real history, HTTP.
- [x] T069 Angular: a Dashboard page (cards, burn-down and velocity charts in SVG, flaky table with apply button), English and Vietnamese, unit tests and a browser run.
- [x] T070 Document the formulas and decisions (plan.md 4.10, README); run the full build.

---

## Phase 12: Attachments (FR-015)

**Goal**: Let a tester attach a screenshot, a crash log or a video to the test case or to an execution attempt. Files live in an ABP blob container (the host chooses the storage), the database holds only the metadata; the type, the size and the number of files are limited; downloads are always served as the fixed type of their extension. No new permission: an attachment follows the permission of what it is attached to.

- [x] T071 [P] Domain: `Attachment` aggregate (owner type and id, name, type, size, SHA-256), `AttachmentManager` (file name, extension whitelist, size and count limits), blob container, options, `AttachmentOwnerType`; `Volo.Abp.BlobStoring` dependency.
- [x] T072 [P] EF Core mapping and DbSet (module context, interface, host context); contracts: DTOs, `IAttachmentAppService`; localized messages (en, vi).
- [x] T073 `AttachmentAppService` (upload, list for several owners, download, delete) with the permission of the owner, blob and row kept consistent; `AttachmentController` (multipart upload, stream download with safe headers); contract and convention tests.
- [x] T074 Host: file system blob storage configured, contract; tests for the manager and the service (limits, permissions, consistency, owners) and over HTTP.
- [x] T075 Angular: an attachments panel (upload by button, drop or paste of a screenshot; list, download, delete) in the test case dialog and in the attempt history; English and Vietnamese; unit tests and a browser run.
- [x] T076 Document the decisions (plan.md 4.11, README: storage setup); run the full build.

---

## Phase 13: Tags and the automation facet of search (FR-024)

**Goal**: Classify test cases with free-form tags and filter by them, as the spec's search and filter asks (priority, severity, suite, tag, automation status). Tags are labels, not content: changing them never publishes a version and never needs approval. Following the labels of Jira, Xray and TestRail: free text, case-insensitive, a handful per test case.

- [x] T077 [P] Domain: `TestCaseTag` child entity and `TestCase.SetTags` (trimmed, case-insensitive, at most 20 of 50 characters, no `;` or `,`), `TagNames` parser shared with import; filter by tags (all of them) and by whether an Automation ID is linked; localized messages.
- [x] T078 [P] EF Core mapping, includes (the list carries tags), filter, tag summaries (name and number of test cases); contracts: tags in the DTOs and the list input, `SetTagsAsync` and `GetTagsAsync`.
- [x] T079 Application and HttpApi: tags on create and update (omitted leaves them unchanged), `PUT test-cases/{id}/tags` without a new version, `GET test-cases/tags`; import and export of a `Tags` column; contract and convention tests.
- [x] T080 Tests: tag rules and normalization, filtering, versioning is untouched, import and export round trip, HTTP.
- [x] T081 Angular: tag chips in the form, the detail dialog (edit in place) and the list; filters by tag and by automation; English and Vietnamese; unit tests and a browser run.
- [x] T082 Document the decisions (plan.md 4.12, README); run the full build.

---

## Phase 14: Reusable steps (FR-005)

**Goal**: Let a team write a group of steps once ("Log in") and put it into many test cases, and then see which test cases are behind when the group changes, and bring them up to date in one action. The design keeps the versioning of FR-004 intact: a test case holds a *copy* of the steps of the group, with a link (group and revision), so every version and every run still holds exactly the steps that were tested. Changing a group never changes an approved test case behind a person's back.

- [x] T083 [P] Domain: `SharedStepGroup` aggregate (name, description, revision, steps) and `SharedStepGroupManager` (unique name); the link on `TestStep` (group and revision), and on `TestCase`: insert a group at a position, refresh from the group, detach, and unlinking of a step whose content is edited.
- [x] T084 [P] EF Core mappings and DbSet (module, interface, host); repository: test cases that use a group, usage counts; permissions `SharedSteps` and `SharedSteps.Manage`; localized messages (en, vi).
- [x] T085 Contracts and application: `SharedStepGroupAppService` (list with usage, get, create, update with revision, delete refused while used, usage, bulk update of test cases); insert, refresh and detach on `ITestCaseAppService`; the link, the name and the "outdated" flag in the step DTO.
- [x] T086 [P] HttpApi: `SharedStepGroupController` and the three test case routes; host seed (tester reads, QA lead manages); contract and convention tests.
- [x] T087 Tests: group rules, the link surviving edits and reorders, unlinking on content change, insert and refresh at the right position, approved test cases get a version, versions and runs keep the copy, usage and bulk update, permissions, import and export of linked test cases.
- [x] T088 Angular: a Shared steps page (list, edit, usage, bulk update); in the test case dialog the groups as blocks with "outdated", update and detach, and a button to insert a group; the form shows linked steps as read-only; English and Vietnamese; unit tests and a browser run.
- [x] T089 Document the decisions (plan.md 4.13, README); run the full build.

---

## Phase 15: Plugging into a host application (an ABP Angular app)

**Goal**: Prove that the module works inside an application that is not its sample host: an ABP application with its own sign-in, sidebar, roles and theme. Make the Angular pages independent of how the host signs in, which language it uses and where its API is, add the module to a second, template-built host, and fix what that shows.

- [x] T090 Library layout: move the pages, the proxy, the core and the styles to `angular/projects/test-case-management/` with a public API; the standalone app becomes a small host of it (login page, top bar, `LocalAuthService`).
- [x] T091 Host contracts: `AuthService` (abstract, signed-out default), `TCM_API_URL`, `TCM_BASE_PATH`, `TCM_LANGUAGE`, `TCM_NOTIFIER`; the services build their URLs from the API root; the language and toasts follow the host when it provides them; run links honour the base path.
- [x] T092 Routes and menu: `createTestCaseManagementRoutes` (all pages behind the host's guards, wrapped by `TcmShellComponent`), `TCM_MENU` with a permission and an ABP name per entry; menu labels in the module's localization resource (en, vi); the module's styles scoped under `.tcm`.
- [x] T093 ABP adapter (`test-case-management/abp`): `AbpAuthAdapter`, `provideTestCaseManagementForAbp`, `provideTestCaseManagementMenu`, an interceptor that shows the errors of the module's API in the host's toaster.
- [x] T094 Server: `AddTestCaseManagementApiKeyAuthentication()` for hosts whose default scheme is ASP.NET Core Identity's (the ABP templates), with tests.
- [x] T095 Second host: an `abp new` application (Angular, LeptonX, SQLite) with the module attached (project references, DbContext, migration, API key scheme), `vi` added to its languages; run it with its Angular app.
- [x] T096 Fix what running it showed: Bootstrap class clashes, dialog z-index, route id binding, calls the user may not make, errors shown through the host.
- [x] T097 Tests and browser runs: unit tests of the host contracts, the routes and the menu (85 Angular tests); a browser run of the whole module in the second host (sign-in, sidebar, repository, attachments, run, API key, dashboard, permissions in the Roles screen, a user with three permissions, Vietnamese); the earlier browser script again on the standalone app; the full build.
- [x] T098 Document the decisions and the steps (plan.md 4.14, README "Using the module in an ABP application").

---

## Phase 17: AI step suggestions (FR-027)

(The guide for production deployment and the run on SQL Server or PostgreSQL are not done yet.)

**Goal**: Let a person turn a requirement text into proposed test steps with an AI model, review them and add the ones they want, without the module storing or showing a key, trusting the model's answer, or saving anything on its own.

- [x] T099 [P] Contracts: `IStepSuggestionProvider` (the hook) with its request type, `IStepSuggestionAppService` (status, suggest), DTOs and limits; permission `TestCases.SuggestSteps` (localized en, vi); error codes and messages (not configured, failed, no usable steps).
- [x] T100 [P] Application: `TestCaseManagementAiOptions` bound from `TestCaseManagement:AiSuggestions`; the OpenAI-compatible provider (key as a bearer token or in a named header, timeout, response size limit, no redirect, nothing secret in logs or errors); the response parser and the cleaning of every provider's answer; `StepSuggestionAppService`.
- [x] T101 HttpApi and sample host: `StepSuggestionController` (`GET status`, `POST`), the permission for QA lead and tester, the configuration section in `appsettings.json`; contract and convention tests (17 controllers and services).
- [x] T102 Tests: parser and cleaning; the provider against a fake model (request shape, key placement, answer shapes, refusal, network error, timeout, size, endpoint validity, secrets absent from errors and logs); the service with a fake provider; HTTP with the model configured only through configuration (permissions, validation, errors in two languages, the key never in an answer).
- [x] T103 Angular: the suggestion dialog in the test case form (button only when enabled and permitted, requirement prefilled, pick, add to the form); proxy, permission, English and Vietnamese texts; unit tests and a browser run against a fake OpenAI-compatible server.
- [x] T104 Document the decisions and the configuration (plan.md 4.15, README "AI step suggestions"); run the full build.

---

## Phase 16: Review of the whole module and the fixes it asked for

**Goal**: Find what is wrong with the module before it is plugged into the company's application, fix what matters, and leave the rest written down. The performance of the dashboard queries on a large data set and a run on SQL Server or PostgreSQL are not part of this phase yet.

- [x] T105 Review every layer with independent readers (Domain, application services in two parts, EF Core with the HTTP API and the sample host, the Angular library); check the important findings against the code.
- [x] T106 Excel import limits: real row and column numbers, the span of blank rows against the row limit, the real unzipped size; tests with sparse rows, far columns and the last column.
- [x] T107 Business rules: one item per test case in a run, no new run for an archived plan, no approved test case without steps, a group once per test case, the flaky window of the dashboard, enum values checked everywhere, unique sort tie-breakers; tests for each.
- [x] T108 Concurrency: locks for approving a sign-off, starting a sign-off for a plan and saving the default gate; the tests hold the lock and see the refusal and the release.
- [x] T109 Pipelines and host: an empty API key header, null results, the file name of an import, the reader of AI answers; Swagger and login timing of the sample host; tests.
- [x] T110 Front end: the dialog (drag, Escape, focus), double submits, stale answers, the Quality and import dialogs, permission checks for secondary calls, the standalone tabs and the smaller findings; 42 new unit tests.
- [x] T111 Document the changes and what is left (plan.md 4.16, README); run the full build, the browser script of the standalone app and the three browser scripts of the ABP host.

---

## Phase 18: The module on MySQL and on a large data set

**Goal**: Run the module on the database the company's application uses (MySQL), and see how the screens that read a lot behave with a large amount of data.

- [x] T112 Run the tests on MySQL: the MySQL provider in the test base and the sample host, `TCM_TEST_MYSQL`, a shared database emptied before each test, `test/mysql.runsettings`; the HTTP tests on a MySQL host.
- [x] T113 Fix what MySQL showed (two tests that assumed SQLite's case sensitivity and time precision); all suites pass on MySQL 8.4 and on SQLite.
- [x] T114 `Scale_Tests`: 1,000 to 10,000 test cases with 100,000 run items; the times of the dashboard, the flaky list, the gate and the lists; what the time is spent on.
- [x] T115 Document the way to run on MySQL, the figures and what would improve them (plan.md 4.17, README).

---

## Phase 19: Assigning testers in the run page

**Goal**: Use what the back end already had (the assignee of a run item) in the screens, without giving the module a user table of its own.

- [x] T116 `TCM_USER_DIRECTORY`: the contract with which a host says who can be assigned; the default (nobody), the ABP adapter (lookup, then the Identity list) and the sample host's `GET /api/host/users`.
- [x] T117 Run page: Tester column with a select for who manages plans, filter by tester with the done/total of each person, assignee when adding test cases, a refused change put back.
- [x] T118 Tests (9 unit, 2 HTTP) and a trial in the ABP sample application; document the contract and the permission found there (plan.md 4.18, README).

---

## Phase 20: Polishing the screens and a bug in the idempotency key

**Goal**: Remove what looked unfinished in the screens, and fix what a pipeline demo showed.

- [x] T119 Filter bar and fields (one height, a select with its own arrow, the host theme's reset outweighed), quiet row actions, the back link of the run page.
- [x] T120 Icons for edit and delete in four tables, `app-row-menu` for the next states of a plan; tests (naming, choice, Escape, a click elsewhere).
- [x] T121 `ConfirmService` and the question dialog of the shell instead of `confirm()` and `prompt()` in eleven places; fallback without a shell; tests; a preview dialog for attachments.
- [x] T122 `Idempotency-Key` header read by the endpoint of the pipeline, with a test; found by sending results to the sample application as a pipeline would.
- [x] T123 Run the whole verification again after these changes: all back-end suites, the Angular tests and build, and the browser scripts of the standalone app and of the ABP application. It found a failing production build (style budget), a menu that closed on scroll and pages wider than a phone; fixed (plan.md 4.19).

---

## Phase 21: Details in a drawer

**Goal**: Open the details of a record in a resizable panel at the right edge, with the list dimmed behind it.

- [x] T124 `side` mode of `ModalComponent` with a resize handle (mouse and keyboard), a width kept for all drawers, full width on a phone; tests.
- [x] T125 Use it for the detail and form of a test case, the attempts and the result of a run item, requirements, shared steps and sign-off reports (plan.md 4.20).

---

## Phase 22: The guide done for real

- [x] T126 Do the ten exercises of the guide on the screens with the accounts of each role, take the 61 pictures, correct the text where the screens differ; fix what that showed (the tester filter without a user directory).

---

## Phase 23: The automation part, switched off

- [x] T127 `TCM_FEATURES` (automation off by default): no sidebar entry or tab, the route redirects, no filter and no Automation ID field; one line to turn it on; code and server untouched.
- [x] T128 Take the pipeline exercise and its pictures out of the team guide, and say in the READMEs that the part is off and how to turn it on.

---

## Phase 24: A change to an approved test case is reviewed again

- [x] T129 `SendBackForReviewIfApproved` in `TestCaseManager`; every edit of an approved test case (update, reorder, insert and refresh of shared steps, bulk update, import) sends it to Under review and publishes nothing; the approval publishes the version (plan.md 4.23).
- [x] T130 Drawer: a banner for an edit waiting for review, an optional note when approving again (`askText` `optional`), a hint in the form instead of the summary field; `shared.bulkDone` counts the test cases sent to review.
- [x] T131 Update the tests that expected an immediate version (nine in the application suites, one in the HTTP flow), add the manager test and the drawer spec; redo pictures b6-04 to b6-08 of the guide; docs.

---

## Phase 25: Projects

- [x] T132 `Project` (key, name, archive), `ProjectManager`, `ProjectId` on suites, plans, requirements, runs and sign-off reports; EF mapping; error codes and localization (plan.md 4.24).
- [x] T133 The default project, the backfill of old data, no mixing of projects (suites, test cases, runs, requirements), archived projects take nothing, delete only when empty.
- [x] T134 `ProjectAppService` and controller, the permission `Projects.Manage`, the `ProjectId` filter on every list, dashboard, flaky tests, quality gate, sign-off, tags, import and export; the documentation filter reads `Nullable{Guid}`.
- [x] T135 Angular: `ProjectContext`, the bar in the shell with the page built again on a change, the page that manages projects, the services that send the project; tests.
- [x] T136 Tests of the back end (17 for the projects, 2 in Domain), update of the ones that count controllers and operations; the sandbox: `DbSet`, migration, run on a copy with two projects; the browser script of the standalone application.
- [x] T137 The documentation: READMEs, the guide (exercise 10 with 8 pictures), spec FR-028, plan 4.24.

---

## Phase 26: Many runs and plans

- [x] T138 The list of runs carries the progress of each run (it was 0 for all); a test that the list and the run agree.
- [x] T139 The page of runs and plans: filters, paging of 15 runs, plans filtered and paged in the page; tests (plan.md 4.25).
- [x] T140 The runs grouped under their plans (`RunCount` on the plan, `NoPlan` on the list of runs, groups opened on request, the view kept), with tests for the back end and the page.
