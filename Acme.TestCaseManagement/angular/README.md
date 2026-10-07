# TCM web (Angular)

Angular 22 front end of the Acme.TestCaseManagement module. Standalone components, signals, lazy routes.

```text
projects/test-case-management/   the library: pages and services, shared by every host
  src/lib/proxy/      typed DTOs, enums and one service per API area (what `abp generate-proxy` would produce)
  src/lib/core/       what the pages need from a host (auth.ts, host.ts), toasts, error mapping, modal, helpers
  src/lib/core/i18n/  English and Vietnamese texts (en.ts, vi.ts), language service, `t` pipe, language switch
  src/lib/features/   repository, runs, traceability, quality, dashboard, automation, shared steps (lazy loaded)
  src/lib/styles/     the stylesheet, scoped under `.tcm`, and its colour tokens
  src/public-api.ts   what a host imports as `test-case-management`
  abp/                the adapter for an ABP Angular application, imported as `test-case-management/abp`
src/app/              the standalone host: login page, top bar, LocalAuthService (JWT sign-in), interceptors
```

A host provides `AuthService` (and optionally the language, the notifications and the API address) and mounts
`createTestCaseManagementRoutes()`; the README of the module explains it for an ABP application.

## Run

1. Start the API: `dotnet run --project ../host/Acme.TestCaseManagement.HttpApi.Host --urls http://localhost:5080`
2. `npm install` then `npm start` and open http://localhost:4200

`proxy.conf.json` forwards `/api` to port 5080, so no CORS setup is needed.

Every page except `/login` needs a signed-in user. Sign in with a demo account of the sample host (`qa.lead`,
`product.owner` or `tester`, password `Tcm!Demo123` from `appsettings.Development.json`). The token is kept in
`localStorage`; the API's 401 signs the user out. The buttons shown follow the permissions of the user's roles, read from
`/api/abp/application-configuration` (a hidden button is a convenience, the API enforces the permission). To sign off a
release two different users must approve, so sign out and in as the second user.

The host issues the tokens itself (`POST /api/auth/login`). With an external identity provider, replace `LocalAuthService`
(`src/app/core/local-auth.ts`) with an `AuthService` of your own; the pages only read who is signed in and what they may do.

## Import and export

The repository page exports the test cases that the filters show (all pages) as Excel or CSV, and imports a file of test cases: the
dialog first **checks** the file (a dry run on the server) and lists every row with its outcome, and the import button works only
after a check without errors. The run page exports every attempt and imports results into an open run the same way. "Download a
template" gives a CSV with the columns the import reads. The file layout is described in the README of the module.

## Languages

The EN / VI switch (login page and header) changes every text at once, without a reload. The choice is stored in `localStorage`;
the first visit follows the browser language. The language is also sent as `Accept-Language`, so the sample host answers errors of
the module in Vietnamese or English. To add a text, add the key to `core/i18n/en.ts` and `vi.ts` (the compiler refuses a missing
key) and use `{{ 'key' | t }}` in a template or `I18nService.t()` in code. Another language needs a new dictionary, an entry in
`LANGUAGES` and a registered Angular locale for dates.

`npm test` runs the unit tests, `npm run build` makes a production bundle.
