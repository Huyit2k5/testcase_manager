# Test Case Management (TCM) Reusable Module Constitution

## Core Principles

### I. Reusable ABP Application Module Architecture
- The system MUST be developed as an independent, plug-and-play ABP Application Module (`Acme.TestCaseManagement`).
- Zero hard dependencies on host applications; all cross-cutting dependencies (User, Tenant, Audit) MUST rely on ABP abstractions (`ICurrentUser`, `ICurrentTenant`, `IAuditingStore`, `IMultiTenant`).
- Adhere strictly to Domain-Driven Design (DDD):
  - `Acme.TestCaseManagement.Domain.Shared`: Enums, constants, localization, error codes.
  - `Acme.TestCaseManagement.Domain`: Aggregate roots, entities, domain services, repository interfaces.
  - `Acme.TestCaseManagement.Application.Contracts`: DTOs, application service interfaces, permissions.
  - `Acme.TestCaseManagement.Application`: Application service implementations, validation, mapper profiles.
  - `Acme.TestCaseManagement.EntityFrameworkCore`: DbContext, EF Core configurations, custom repository implementations.
  - `Acme.TestCaseManagement.HttpApi`: Auto API controllers or custom controller endpoints.

### II. Strict Separation of Design-Time Repository vs Runtime Execution
- **Design-Time Repository (Master Library)**: Represents long-term testing assets (`TestCase`, `TestStep`, `TestSuite`). It MUST NOT store execution states (Passed/Failed) or run assignments.
- **Runtime Execution (Run Cycle)**: Represents dynamic testing sessions (`TestPlan`, `TestRun`, `TestRunItem`, `TestExecution`).
- Updating, executing, or re-testing in a Test Run MUST NEVER modify or mutate the master `TestCases` table.

### III. Immutability via TestCaseVersion Snapshots (NON-NEGOTIABLE)
- When a `TestCase` is attached to a `TestRun` or `TestPlan`, a `TestCaseVersion` snapshot MUST be referenced.
- If a tester edits a Test Case in the library after a Test Run was initiated, historical runs MUST remain bound to their frozen version snapshot.
- Historic test reports, audit trails, and sign-off records MUST NEVER be invalidated by future library edits.

### IV. Multi-Attempt Append-Only Execution Model
- Test results are recorded as discrete execution attempts (`TestExecution`).
- When a test case fails and is re-tested after a bug fix, the system MUST `INSERT` a new `TestExecution` record (Attempt #2).
- The system MUST NEVER `UPDATE` or overwrite previous execution attempts.
- First-time pass rate (FTPR) and defect turnaround metrics MUST be fully derivable from the immutable attempt log.

### V. Quality Gates & Release Enforcement
- Releases and milestones MUST enforce deterministic quality gate evaluation:
  - Critical/High severity unresolved defect count must be 0 for sign-off approval.
  - Minimum test run pass rate threshold (e.g. >= 95%).
  - 100% of P1/Smoke test cases executed.
- Formal sign-off reports (`SignOffReport`) MUST freeze the milestone metrics with cryptographic/audited approval metadata.

### VI. Clean Architecture, Auditing & Multi-Tenancy
- All domain aggregate roots MUST inherit from `FullAuditedAggregateRoot<Guid>` or `FullAuditedEntity<Guid>`.
- Multi-tenancy support is mandatory: entities MUST implement `IMultiTenant` where applicable, with tenant isolation guaranteed by EF Core global query filters.
- Soft-delete (`ISoftDelete`) MUST be preserved across all entity deletions; hard deletes are strictly forbidden in production audit trails.

## Technical Constraints & Technology Stack

- **Target Framework**: .NET 10 (LTS)
- **Architecture Framework**: ABP Framework (v10.x, pinned to 10.6.1)
- **Database & ORM**: Entity Framework Core (PostgreSQL & SQL Server support via EF Core migrations)
- **Primary Data Format**: JSON for step snapshots and dynamic test parameters
- **API Standard**: RESTful JSON API following ABP conventions with OpenAPI / Swagger documentation
- **External Integration**: Webhook & REST API adapters for Jira, GitHub Issues, and CI/CD test runners

## Development & AI Coding Workflow

1. **Specification First**: Every feature begins with an approved Spec Kit specification (`spec.md`).
2. **Technical Plan First**: Architecture, entity definitions, and DTO contracts are mapped before implementation (`plan.md`).
3. **Task-Driven Coding**: All code generation by AI agents follows ordered tasks (`tasks.md`) with explicit verification steps.
4. **Automated Verification**: Unit tests (`xUnit`, `Shouldly`, `NSubstitute`) and integrated ABP test harness (`TestCaseManagementDomainTestModule`, `TestCaseManagementApplicationTestModule`) must pass before concluding any task.

## Governance

- The rules in this Constitution supersede all temporary conventions.
- Any architectural change (e.g., adding an aggregate root, modifying version snapshot schemas) requires amending this document with clear justification.
- AI coding agents MUST verify compliance with these principles before generating or modifying code.

**Version**: 1.0.1 | **Ratified**: 2026-10-06 | **Last Amended**: 2026-10-06 (target framework moved from .NET 8/9 + ABP 8.x/9.x to .NET 10 + ABP 10.x to match the installed SDK; no change to architectural principles)
