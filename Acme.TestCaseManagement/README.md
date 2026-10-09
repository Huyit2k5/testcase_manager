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
- **AI assistance** – proposed steps from a requirement text through a configured OpenAI-compatible model (or a provider of the host's own); a person reviews before anything is added.
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

## Using the module in an ABP application

This is how the module was added to an application built from ABP's own template (Angular UI, LeptonX, OpenIddict), and the
browser was run against it (plan.md, section 4.14).

**On the server**
1. The host needs ABP 10.6.1 or newer (a template with 10.6.0 packages fails with NU1605 until its packages are raised).
2. Reference the six `Acme.TestCaseManagement.*` projects (or packages) and add each module to the `[DependsOn]` of the matching layer
   of the host: Domain.Shared, Domain, Application.Contracts, Application, EntityFrameworkCore, HttpApi.
3. Embed the model in the host's DbContext as in "2. Database" above (`ITestCaseManagementDbContext`, `[ReplaceDbContext]`, one `DbSet`
   per member, `builder.ConfigureTestCaseManagement()`), then add a migration and run the migrator. The template's admin role receives
   the module's permissions when the migrator seeds.
4. For pipelines, after the template's `ForwardIdentityAuthenticationForBearer(...)` call `context.Services.AddTestCaseManagementApiKeyAuthentication();`.
   The template's default scheme then sends a request with `X-Api-Key` to the key scheme and everything else where it went.
5. Attachments use ABP's BlobStoring; the template already includes the database provider, which works as it is. Choose another provider
   (file system, S3...) for large volumes.
6. Add `vi` to the host's `AbpLocalizationOptions.Languages` if users should switch to Vietnamese; the module's resource has both languages.

**In the Angular application** (the pages are shared as source, see "Angular front end")
1. Copy `angular/projects/test-case-management/` into the application (a git submodule or a copy), and add to its `tsconfig.json`:
   `"paths": { "test-case-management": ["./projects/test-case-management/src/public-api.ts"], "test-case-management/abp": ["./projects/test-case-management/abp/public-api.ts"] }`.
2. In `app.config.ts`, after `provideAbpCore(...)` and the theme: `provideTestCaseManagementForAbp(), provideTestCaseManagementMenu()`.
3. In `app.routes.ts`: `{ path: 'test-case-management', loadChildren: () => import('test-case-management').then(m => m.createTestCaseManagementRoutes({ canActivate: [authGuard] })) }`.
   Another path needs the same value in `TCM_BASE_PATH` (the adapter provides `/test-case-management`).

There is no stylesheet to add: the pages load their own, scoped under `.tcm`. The sidebar gets a "Test Case Management" group with
the pages the user may open, the permissions are managed in the host's own Roles screen, the language follows the host's switch, and
messages and errors appear in the host's toaster. The pages keep their own light palette and do not follow the theme's dark mode.

## HTTP API

All routes start with `api/test-case-management/`.

| Route | Purpose |
|---|---|
| `suites` | suite tree, create, update, move, delete |
| `test-cases` | test cases, step ordering, status changes, versions, linked defects; `GET` filters by text, suite, status, priority, severity, kind, layer, execution type, `Tags` (all of them) and `HasAutomationId` |
| `test-cases/{id}/tags`, `test-cases/tags` | replace the tags of a test case (no new version), and list the tags in use with their counts |
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
| `dashboard` | `GET`: pass rate, execution velocity, burn-down and defect density of a plan or of every run |
| `flaky-tests` | `GET` the flakiness score of test cases, `POST flaky-tests/apply` to flag them in the library |
| `shared-step-groups` | the library of reusable groups of steps: list, get, create, update, delete, usage, and a bulk update of the test cases that are behind |
| `test-cases/{id}/shared-steps` | `POST` copies a group into a test case, `.../{groupId}/refresh` brings the copy up to date, `DELETE .../{groupId}` detaches it |
| `step-suggestions` | `GET status` (is a model configured) and `POST`: proposed steps from a requirement text, nothing saved |
| `attachments` | upload (multipart), list, download and delete files of test cases and of execution attempts |
| `automation/results` | `POST`: a pipeline publishes automated results (API key, or a user with `AutomationResults.Publish`) |

## Permissions

Group `TestCaseManagement`. Each `...Default` permission allows reading; the child permissions allow changes.

| Permission | Children |
|---|---|
| `TestCaseManagement.TestCases` | `Create`, `Update`, `Delete`, `Approve`, `SuggestSteps` (ask the AI model for steps) |
| `TestCaseManagement.TestSuites` | `Manage` |
| `TestCaseManagement.TestPlans` | `Manage` (also required to create, populate, assign and complete runs) |
| `TestCaseManagement.TestRuns` | `Execute` (record executions, manage defect links) |
| `TestCaseManagement.Requirements` | `Manage` |
| `TestCaseManagement.QualityGates` | `Manage` |
| `TestCaseManagement.SignOff` | `Approve` |
| `TestCaseManagement.SharedSteps` | `Manage` (create, change and delete groups, and bulk update test cases) |
| `TestCaseManagement.ApiKeys` | `Manage` (create and revoke keys) |
| `TestCaseManagement.AutomationResults` | `Publish` (the only permission an API key has) |

## Shared steps

A group of steps ("Log in as a customer") is written once in the library and copied into many test cases. The test case keeps **its own copy**, with a
link to the group and the revision copied, so changing a group never changes an approved test case, a version or a run by itself: the test cases that
copied an older revision show as behind, and `POST shared-step-groups/{id}/update-test-cases` (or the refresh of one test case) brings them up to date, with
a new version for the approved ones. Editing a copied step inside a test case, or detaching, makes it the test case's own. A group in use cannot be deleted.
A host that embeds the model in its own DbContext also needs `DbSet<SharedStepGroup>`. The reasoning is in `specs/001-test-case-management/plan.md`, section 4.13.

## Tags

Test cases carry free-form tags (at most 20 of 50 characters, no comma or semicolon, compared ignoring case). Tags are labels: they are not part of a
version, `PUT test-cases/{id}/tags` changes them without publishing one, and they are filtered with `Tags=a&Tags=b` (all required). The import and export have a
`Tags` column with the tags separated by semicolons.

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

> **The screens of this part are off by default.** The Automation page (API keys and the snippets for pipelines), the Automation ID field of a test case and the filter on it are
> hidden, and the Automation route sends the user to the repository. Nothing was removed: the server endpoints below, the API-key authentication and the pages are all in place.
> To turn the part on, tell the Angular host: `{ provide: TCM_FEATURES, useValue: { automation: true } }` (in `app.config.ts`, next to the other providers of the module).
> Without any key the endpoints cannot be used, so leaving them on the server is harmless; a host that wants them closed too can leave out `AddTestCaseManagementApiKeyAuthentication()`.

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
the module's model in its own DbContext needs the new `DbSet`s (`ApiKey`, `AutomationPublication`, `Attachment`, `SharedStepGroup`).

## Flaky tests and the dashboard

`GET dashboard?TestPlanId=&Days=14` returns the pass rate, the execution velocity, the burn-down and the defect density of one plan,
or of every run (runs without a plan, such as CI runs, included). `GET flaky-tests` scores every test case by how often its latest
Passed or Failed outcomes change (the score is changes divided by outcomes minus one, over the last 20 outcomes; 0.15 is Watch and
0.30 is Flaky), and `POST flaky-tests/apply` writes the Flaky flag into the library (`ClearRecovered` also removes it from tests that
became stable). Thresholds, window and lookback are `TestCaseManagementInsightsOptions`. The definitions and the research behind them
are in `specs/001-test-case-management/plan.md`, section 4.10.

## Attachments

Screenshots, logs and videos can be attached to a test case or to an execution attempt (`POST attachments`, multipart with `OwnerType`
0 = test case, 1 = attempt, `OwnerId` and `File`). The bytes live in an ABP blob container, so **the host must configure a provider**; without
one the first upload fails. The sample host keeps them in a folder:

```csharp
Configure<AbpBlobStoringOptions>(options =>
    options.Containers.ConfigureDefault(container => container.UseFileSystem(fileSystem => fileSystem.BasePath = "/data/attachments")));
```

(add `Volo.Abp.BlobStoring.FileSystem`; for S3, Azure or the database use the matching ABP provider.) A host that embeds the model in its own
DbContext also needs `DbSet<Attachment>`. Limits are `TestCaseManagementAttachmentOptions`: 25 MB a file, 25 files an owner, and an extension
whitelist (images, PDF, text and logs, archives, Office files, short videos; no SVG or HTML). A file follows the permission of what it is attached to:
`TestCases` / `TestCases.Update` for a test case, `TestRuns` / `TestRuns.Execute` for an attempt. Downloads are always attachments with a fixed content type.

## Assigning testers

A run item can be assigned to a person, from the run page (a *Tester* column, a filter, and a choice when test cases are added). The module has no users of its own, so the host says who can be
assigned with `TCM_USER_DIRECTORY` (`list(): Observable<{ id, userName, displayName }[]>`). `provideTestCaseManagementForAbp()` registers one over ABP's user lookup, falling back to the Identity user list
(permission `AbpIdentity.Users`; ABP's `AbpIdentity.UserLookup` is not defined in a standard application). Without a directory, or when it fails, the assignment is hidden and nothing else changes.
A host whose testers do not hold those permissions provides its own directory.

## AI step suggestions

In the test case form a person can ask an AI model to propose steps from a requirement text, review them, and add the ones they want. The
button appears only when a model is configured and the user holds `TestCases.SuggestSteps`; nothing is saved until the test case is saved.

**Turn it on** with the host's configuration (appsettings, user secrets, environment variables, a secret store); no code is needed:

```json
"TestCaseManagement": {
  "AiSuggestions": {
    "Endpoint": "https://api.openai.com/v1/chat/completions",
    "ApiKey": "(in user secrets or an environment variable, not in appsettings.json)",
    "Model": "gpt-4o-mini"
  }
}
```

The endpoint is the full URL of a chat completions endpoint. OpenAI and servers that run a model inside the company (Ollama
`http://host:11434/v1/chat/completions`, vLLM, LM Studio) speak it, and then the requirement text does not leave the network; the key may be left out
for them. `ApiKeyHeader` puts the key in another header than `Authorization: Bearer` (Azure OpenAI wants `api-key`; not tried). Other settings:
`TimeoutSeconds` (30), `MaxResponseBytes` (256 KB), `SystemPrompt` (the instruction to the model). Without an `Endpoint` the feature is off.

**Another service**: implement `IStepSuggestionProvider` and register it in place of the built-in one:

```csharp
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IStepSuggestionProvider))]
public class MyProvider : IStepSuggestionProvider, ITransientDependency { /* IsEnabled, SuggestAsync */ }
```

**Safety.** The key is never stored, shown, returned, logged or put in an error; the answer of any provider is cleaned (empty and repeated steps
dropped, control characters removed, lengths and counts capped) and the requirement is passed as data, not as instructions. The permission is separate
because the text leaves the application: the sample host gives it to the QA lead and the tester. There is no quota or rate limit. The audit log of the call does not keep
the requirement text. Details: plan.md, section 4.15.

## Quality gate rules in one place

- **Pass rate** = Passed / (all run items − Skipped), using the current (latest-attempt) status. The gate compares
  the exact ratio with `MinPassRate` (default 95); the figure shown is rounded down to two decimals.
- **P1 executed** – every item whose test case has priority Urgent has a Passed or Failed result.
- **Open Critical / High defects** – counted per distinct issue (system and key, case-insensitive) that still has an
  unresolved link; both limits are fixed at zero.
- A gate has `RequiredApprovals` (default 2). There is deliberately no override: a failing gate blocks sign-off.

## Running on MySQL

The module has been run against MySQL 8.4 with `Volo.Abp.EntityFrameworkCore.MySQL` (the package of the ABP application template): the model creates, and every test of the module passes (plan.md,
section 4.17). To repeat it, start a MySQL server and run the tests with `TCM_TEST_MYSQL` set and the settings file that turns parallel runs off:

```text
docker run -d --name tcm-mysql -e MYSQL_ROOT_PASSWORD=secret -p 3307:3306 mysql:8.4
set TCM_TEST_MYSQL=Server=localhost;Port=3307;User ID=root;Password=secret;
dotnet test --settings test/mysql.runsettings
```

The sample host runs on MySQL with `Host:Database=MySql` and `ConnectionStrings:Default`. On a large data set (100,000 run items) the dashboard takes about 6 seconds and the flaky list about the same, because they
read every run item and attempt of their scope; the lists of test cases stay under a second. `TCM_SCALE=10000` with the same tests runs the measurement (`Scale_Tests`).

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
- The module contains no EF Core migrations. Its UI is Angular only, shared as a source folder (not an npm package), tried with an ABP host on
  Angular 22.0 and the LeptonX Lite side menu; it keeps its own light palette instead of following the host's theme.
- Deleting a test case does not delete its attachments; a pipeline cannot attach files yet; files are held in memory while stored (25 MB limit).
- Flaky detection and the dashboard read the attempts of the lookback window into memory and work on the dates of the server clock.
- A review of the whole module (plan.md, section 4.16) left these on purpose or for later: idempotency keys are per tenant, so build the key from the
  pipeline and the build; lookups by Automation ID lower-case the column and no index serves them (add one on the lowered value on a large table); the code of a test
  case compares as the database does (case sensitive on SQLite and PostgreSQL); the dashboard and the flaky list read the run items and defects of their scope into
  memory; the sample host's tokens last 8 hours and cannot be revoked, and its pipeline has no `UseMultiTenancy`, so a tenant-bound API key would publish into the host's
  tenant (a host with tenants must resolve the tenant first). The locks of sign-off and default gate are in this process unless a distributed lock provider is registered.
- On MySQL the dashboard, the flaky list and the quality gate read every run item and attempt of their scope and take about 6 seconds at 100,000 run items (see "Running on MySQL"); tested with MySQL 8.4 and the Oracle-based provider only, not with Pomelo, MariaDB or 5.7.
- Results published with an API key have no creator, there is no rate limiting on the publish endpoint, stored idempotency records
  are never cleaned up, and publishing was tested on SQLite with a single tenant only. Requests that share an idempotency key are
  serialized by an in-process lock; with several server nodes register a distributed lock provider. The Automation ID is unique by
  the application services, not by a database index.

## Angular front end

`angular/` holds a ready-made UI (Angular 22) for the module, as a library folder (`angular/projects/test-case-management/`) that hosts share
as source, and a small standalone app that runs it with a JWT sign-in: test repository with suite tree, versions and defects;
plans and runs with execution and retest; the traceability matrix; quality gates and two-user sign-off; API keys for pipelines; a dashboard with burn-down, velocity, defect density and flaky tests; attachments with screenshot paste; tags and filters; shared steps; AI step suggestions, in English and
Vietnamese with a language switch. See
[angular/README.md](angular/README.md). It is not packed into the NuGet packages.
