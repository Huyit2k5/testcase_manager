# Acme.TestCaseManagement

A reusable [ABP Framework](https://abp.io) module (.NET 10, ABP 10.x) for managing a test-case library, test
plans and runs, requirement traceability, and quality-gated release sign-off. It has no dependency on a
particular host application: users, tenants and auditing come from ABP abstractions only.

## What it provides

- **Test library** – nested suites, test cases with ordered steps, an approval status workflow, and immutable
  `TestCaseVersion` snapshots: every edit of a test case creates a new version, and plans and runs stay bound to
  the version they were created with.
- **Execution** – test plans, test runs with run items, and append-only executions with multiple attempts per
  item (the latest attempt is the current status; history is never rewritten). Defects from an issue tracker are
  linked to a failed execution with a severity and a resolved flag.
- **Traceability** – requirements linked to test cases and a Requirements Traceability Matrix (RTM) that
  distinguishes *covered* from *passed* and exposes blocking defects.
- **Import and export** – the test library and the results of a run as Excel (.xlsx) or CSV, one row per step or per attempt;
  imports are checked completely first (dry run) and are all or nothing.
- **Quality gate and sign-off** – configurable gates (minimum pass rate, all P1 executed, no open Critical or
  High defect) evaluated per plan or milestone; sign-off is refused while the gate fails, freezes the evaluated
  figures with a SHA-256 hash, and completes when enough distinct approvers have signed.

## Packages

| Package | Contents | Reference it from |
|---|---|---|
| `Acme.TestCaseManagement.Domain.Shared` | enums, constants, error codes, localization (en, vi) | any layer |
| `Acme.TestCaseManagement.Domain` | aggregates, domain services, repository interfaces | host (transitive) |
| `Acme.TestCaseManagement.Application.Contracts` | DTOs, application service interfaces, permissions | host and client applications |
| `Acme.TestCaseManagement.Application` | application services | host |
| `Acme.TestCaseManagement.EntityFrameworkCore` | `DbContext`, entity mappings, repositories | host |
| `Acme.TestCaseManagement.HttpApi` | REST controllers | host |

A host references `Application`, `EntityFrameworkCore` and `HttpApi`; the other packages come transitively.

## Using the module in a host

### 1. Reference the packages and depend on the modules

```csharp
[DependsOn(
    typeof(TestCaseManagementApplicationModule),
    typeof(TestCaseManagementEntityFrameworkCoreModule),
    typeof(TestCaseManagementHttpApiModule))]
public class MyHostModule : AbpModule
{
}
```

### 2. Database

The module does not ship EF Core migrations because the database provider belongs to the host. Pick one of two
ways to own the schema.

**Use the module's own `TestCaseManagementDbContext`.** It reads the connection string named
`TestCaseManagement` and falls back to `Default`. Choose the provider in the host
(`Configure<AbpDbContextOptions>(o => o.UseSqlServer())`) and create migrations with a design-time factory.

**Embed the model in the host's `DbContext`** (recommended when the host already has migrations):

```csharp
// using Volo.Abp.DependencyInjection;  (ReplaceDbContext)
// using Volo.Abp.EntityFrameworkCore;
[ReplaceDbContext(typeof(ITestCaseManagementDbContext))]
public class MyHostDbContext : AbpDbContext<MyHostDbContext>, ITestCaseManagementDbContext
{
    // one DbSet per member of ITestCaseManagementDbContext (TestSuites, TestCases, ...)

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureTestCaseManagement();
    }
}
```

Register it with `AddAbpDbContext<MyHostDbContext>(o => o.AddDefaultRepositories(includeAllEntities: true))`.

Tables are named `Tcm...` (`TcmTestCases`, `TcmTestRuns`, ...). Change
`TestCaseManagementDbProperties.DbTablePrefix` and `DbSchema` before the model is built to rename them.

### 3. Authentication and permissions

Every application service requires a permission (see below). The host must provide authentication and a
permission store, for example the ABP Permission Management module. Sign-off records the calling user, so it
needs an authenticated user with an id.

**API keys for pipelines.** A CI job publishes results with an API key instead of a user login. Wire the handler next to your
other schemes with `AddTestCaseManagementApiKey()` and pick it per request, as the sample host does with a policy scheme:

```csharp
services.AddAuthentication(o => { o.DefaultScheme = "BearerOrApiKey"; o.DefaultChallengeScheme = "BearerOrApiKey"; })
    .AddPolicyScheme("BearerOrApiKey", "Bearer token or API key", o =>
        o.ForwardDefaultSelector = ctx => ctx.Request.IsApiKeyRequest() ? ApiKeyDefaults.Scheme : JwtBearerDefaults.AuthenticationScheme)
    .AddTestCaseManagementApiKey()
    .AddJwtBearer(/* ... */);
```

The key principal has no user and no role; the module grants it only `AutomationResults.Publish`. Create keys with
`POST api-keys` (permission `ApiKeys.Manage`); the secret (`tcm_...`) is shown once and only its hash is stored.

### 4. API documentation

The controllers are ordinary MVC controllers and appear in Swagger. Use `Volo.Abp.Swashbuckle`
(`AddAbpSwaggerGen`) as the sample host in `host/Acme.TestCaseManagement.HttpApi.Host` does. Enums are serialized
as integers by default, as everywhere in ABP; add a `JsonStringEnumConverter` in the host if you prefer names. The
sample host also shows `CustomOperationIds` (`TestCase_GetList`, ...) for stable generated-client method names, readable
schema ids for generic DTOs, and enum member names in the schema.

The sample host requires a login. It uses ABP Identity (users, roles) and Permission Management (grants), keeps
everything in one SQLite database through a host `DbContext` that embeds this module's model, and accepts JWT bearer
tokens issued by `POST /api/auth/login`. In Development it creates the schema and three demo roles with one user each
(`qa.lead` has every permission, `product.owner` reads everything, maintains requirements and approves sign-offs,
`tester` writes and executes test cases); the password is `Seed:Password` of `appsettings.Development.json`.
Outside Development nothing is created: configure `Auth:Jwt:SigningKey` (32+ characters), `Seed:Password` and
`Host:InitializeDatabase=true` for a first start, or supply your own database. Run it with
`dotnet run --project host/Acme.TestCaseManagement.HttpApi.Host` and open `/swagger`: log in through
`Auth_Login`, then use Authorize with the returned `accessToken`. The host serves the languages `en` and `vi`
(`UseAbpRequestLocalization`): error messages follow the `Accept-Language` header.

## HTTP API

All routes start with `api/test-case-management/`.

| Route | Purpose |
|---|---|
| `suites` | suite tree, create, update, move, delete |
| `test-cases` | test cases, step ordering, status changes, versions, linked defects |
| `test-cases/export`, `test-cases/import` | Excel or CSV export (with the list filters) and import of test cases |
| `runs/{runId}/results/export`, `.../import` | Excel or CSV export of every attempt of a run, and import of results |
| `plans` | test plans and plan status |
| `runs` | runs, run items, assignment, executions (single and batch), completion |
| `executions/{executionId}/defects` | link, list, update (severity, resolved) and remove defect links |
| `requirements` | requirements and their test-case links |
| `rtm` | requirements traceability matrix |
| `quality-gates` | gate CRUD and `POST quality-gates/evaluate` (dry run with per-criterion breakdown) |
| `sign-off` | start a sign-off, add approvals, read reports |
| `api-keys` | list, create (secret shown once) and revoke API keys |
| `automation/results` | `POST`: a pipeline publishes automated results (API key, or a user with `AutomationResults.Publish`) |

## Permissions

Group `TestCaseManagement`. Each `...Default` permission allows reading; the child permissions allow changes.

| Permission | Children |
|---|---|
| `TestCaseManagement.TestCases` | `Create`, `Update`, `Delete`, `Approve` |
| `TestCaseManagement.TestSuites` | `Manage` |
| `TestCaseManagement.TestPlans` | `Manage` (also required to create, populate, assign and complete runs) |
| `TestCaseManagement.TestRuns` | `Execute` (record executions, manage defect links) |
| `TestCaseManagement.Requirements` | `Manage` |
| `TestCaseManagement.QualityGates` | `Manage` |
| `TestCaseManagement.SignOff` | `Approve` |
| `TestCaseManagement.ApiKeys` | `Manage` (create and revoke keys) |
| `TestCaseManagement.AutomationResults` | `Publish` (the only permission an API key has) |

## Import and export

Test cases: one row per step, with the columns `Suite, Code, Title, Description, Preconditions, Postconditions, Priority, Severity, Kind, Layer, ExecutionType, AutomationId, Flaky, Status, Version, StepNo, Action, ExpectedResult, TestData`. Rows that share a `Code` are one
test case; `Suite` is a path such as `Payments/Cards` and missing suites are created when the caller may manage suites. Status, Version
and StepNo are written for information and ignored on import. Results: `Code, Title, Version, Attempt, Result, ActualResult, DurationSeconds, Defects, ExecutedAt, ExecutedBy`;
importing needs only `Code` and `Result`, and every row becomes a new attempt.

- An import is **all or nothing**: the whole file is checked first and any invalid row blocks it. `DryRun=true` returns the report
  (per row: outcome and localized messages) without writing anything.
- Existing codes are skipped by default; `OnExisting=Update` applies the file. A column that the file does not have leaves that field
  alone, a blank cell in a column that it has clears the field, and an update that changes nothing publishes no version.
- Permissions: export needs the read permission, importing test cases needs `TestCases.Create` (`Update` to update existing ones,
  `TestSuites.Manage` to create suites), importing results needs `TestRuns.Execute`.
- CSV is UTF-8 with a byte order mark (comma, semicolon or tab are read); cells that would run as a formula get a leading apostrophe on
  export. Limits are set through `TestCaseManagementTransferOptions`: 5 MiB, 10,000 rows, 50 MiB unzipped, 20,000 test cases per export.

## Publishing automated results (CI/CD)

A test case is linked to its automated test by the **Automation ID**, which is unique. A pipeline sends its results with an API key
(create one on the Automation page, or with `POST api-keys`):

```bash
curl --fail-with-body -X POST https://tcm.example/api/test-case-management/automation/results \
  -H "X-Api-Key: $TCM_API_KEY" -H "Idempotency-Key: build-123" -H "Content-Type: application/json" \
  -d '{ "run": { "title": "CI build 123", "environment": "staging", "buildVersion": "1.4.2" },
        "completeRun": true,
        "results": [ { "automationId": "e2e.login.valid", "status": "Passed", "durationSeconds": 12 },
                     { "automationId": "e2e.checkout.card", "status": "Failed", "actualResult": "Card declined" } ] }'
```

GitHub Actions (keep the key in `secrets.TCM_API_KEY`):

```yaml
- name: Publish results to Test Case Management
  if: always()
  run: |
    curl --fail-with-body -X POST https://tcm.example/api/test-case-management/automation/results \
      -H "X-Api-Key: ${{ secrets.TCM_API_KEY }}" \
      -H "Idempotency-Key: ${{ github.run_id }}-${{ github.run_attempt }}" \
      -H "Content-Type: application/json" -d @results.json
```

Give either `run` (a new run) or `runId` (an existing one). Results whose Automation ID matches no test case are listed in the answer
and the others are still recorded (`failOnUnmatched: true` rejects the whole request). Approved test cases missing from the run are
added (`addMissingToRun`). The same test case twice in a request counts as retries; a test that fails and then passes is marked flaky.
A request holds at most 2000 results. Send an `Idempotency-Key` so that retrying a request does not record twice. A host that embeds
the module's model in its own DbContext needs the two new `DbSet`s (`ApiKey`, `AutomationPublication`).

## Quality gate rules in one place

- **Pass rate** = Passed / (all run items − Skipped), using the current (latest-attempt) status. The gate compares
  the exact ratio with `MinPassRate` (default 95); the figure shown is rounded down to two decimals.
- **P1 executed** – every item whose test case has priority Urgent has a Passed or Failed result.
- **Open Critical / High defects** – counted per distinct issue (system and key, case-insensitive) that still has an
  unresolved link; both limits are fixed at zero.
- A gate has `RequiredApprovals` (default 2). There is deliberately no override: a failing gate blocks sign-off.

## Build, test and pack

```powershell
./build/build.ps1                           # restore, build, test, pack into artifacts/packages
./build/build.ps1 -VersionSuffix preview.1  # 1.0.0-preview.1
./build/build.ps1 -SkipTests                # build and pack only
```

The script runs in Windows PowerShell 5.1 and PowerShell 7. Packages are written as `.nupkg` plus `.snupkg`
symbol packages. Requires the .NET SDK 10 (see `global.json`).

## Known limitations

- `Volo.Abp.AutoMapper` 10.6.1 depends on AutoMapper 14.0.0, which is flagged by advisory GHSA-rvv3-g6hj-g44x
  (NuGet warning NU1903). Newer AutoMapper releases are binary-incompatible with ABP 10.6.1, so the warning
  reaches host applications through ABP itself.
- Sign-off approvals carry a SHA-256 integrity digest that detects tampering; they are not asymmetric digital
  signatures and do not give non-repudiation.
- The module contains no user interface and no EF Core migrations.
- Results published with an API key have no creator, there is no rate limiting on the publish endpoint, stored idempotency records
  are never cleaned up, and publishing was tested on SQLite with a single tenant only. Requests that share an idempotency key are
  serialized by an in-process lock; with several server nodes register a distributed lock provider. The Automation ID is unique by
  the application services, not by a database index.

## Angular front end

`angular/` holds a ready-made UI (Angular 22) for the module: test repository with suite tree, versions and defects;
plans and runs with execution and retest; the traceability matrix; quality gates and two-user sign-off; API keys for pipelines, in English and
Vietnamese with a language switch. See
[angular/README.md](angular/README.md). It is not packed into the NuGet packages.
