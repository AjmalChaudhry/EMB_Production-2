# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository shape

This is an **ASP.NET MVC 5 (.NET Framework 4.7.2)** backend that hosts an **Angular 20 SPA** as a static asset. There is one solution and one project; both contain a space in their name, so paths must be quoted on the command line.

- Solution: `EMB_Production 2.sln`
- Web project: `EMB_Production 2/EMB_Production 2.csproj` (root namespace `EMB_Production`)
- Angular app: `EMB_Production 2/ClientApp/emb-products/` (Angular CLI workspace, standalone components, Bootstrap 5 + Angular Material)
- Built Angular bundle (committed): `EMB_Production 2/Content/emb-products/` — this is what the running web app actually serves

## Common commands

The Angular dev workflow and the MVC dev workflow are separate processes.

### Backend (ASP.NET MVC, run from Visual Studio or MSBuild)

Built and debugged via Visual Studio / IIS Express. The project is configured to bind to:

- IIS Express URL: `https://localhost:44310/`
- Dev server port: `60600`

DB connection (Web.config): SQL Server at `localhost\SQLEXPRESS03`, database `EMB_Production`, integrated security. The EDMX is Database-First, so schema changes happen in SQL and are then refreshed into `Models/Model1.edmx`.

NuGet packages are restored via `packages.config` (not PackageReference). Run a NuGet restore before the first build.

### Frontend (Angular, run from `EMB_Production 2/ClientApp/emb-products/`)

```powershell
cd "EMB_Production 2/ClientApp/emb-products"
npm install
npm start                    # ng serve, http://localhost:4200 (dev)
npm run build:dev            # development build
npm run build:prod           # production build (default `ng build`)
npm test                     # karma + jasmine
npm run build:deploy         # clean → prod build → robocopy dist into MVC Content/emb-products
```

`angular.json` writes the build output directly to `../../Content/emb-products` (i.e. the MVC project's `Content/emb-products/`), with `baseHref` and `deployUrl` set to `/Content/emb-products/`. That folder is what `HomeController.Angular()` serves. The `deploy:mvc` script's robocopy step assumes a `dist/emb-products` folder — in practice `build:prod` already writes to the final location, so the deploy step is a defensive copy.

Dev workflow against the live MVC backend: run `ng serve` on :4200 and let the MVC site run on :44310; `environment.ts` already points `apiUrl` at `https://localhost:44310`. `proxy.conf.json` only proxies `/Gen_Products` and is mostly vestigial — the SPA calls absolute URLs against `environment.apiUrl`.

## Architecture

### Request routing — how the SPA and the API coexist

`App_Start/RouteConfig.cs` defines four routes in order:

1. `emb-products/api/{controller}/{action}/{id}` — generic API fallback
2. `emb-products/auth/{action}` → `AuthController` — login/logout/dashboard/check-auth
3. `Content/emb-products/{*pathInfo}` → `HomeController.Angular()` — serves the SPA shell for **every** SPA deep link
4. Default `{controller}/{action}/{id}` → also falls back to `HomeController.Angular()`

In addition, **every API controller uses attribute routing** with `[RoutePrefix("emb-products/<area>")]` (e.g. `emb-products/products`, `emb-products/gen-stocks`, `emb-products/dropdowns`). `routes.MapMvcAttributeRoutes()` registers these before the conventional routes, so attribute-routed endpoints win. When adding a new API surface, follow the `[RoutePrefix("emb-products/<area>")]` + `[Route("...")]` pattern — don't rely on the conventional `ApiRoutes` mapping.

`HomeController.Angular()` reads `~/Content/emb-products/index.html` from disk. If the SPA hasn't been built, it returns a plain text message instead — that's the signal that a build step was skipped.

### Data layer

EF6 Database-First, two generated `DbContext`s pointing at the same database:

- `EMB_ProductionEntities` and `EMB_ProductionEntities1` (both in `Models/`, both generated from `Model1.edmx`)
- `BaseController.db` is the **`EMB_ProductionEntities1`** instance — new controllers should inherit `BaseController` and use that one. Don't introduce a third context.

`Global.asax.cs` sets `JsonConvert.DefaultSettings` to camelCase serialization. Several controllers additionally use `Infrastructure/JsonNetResult` (a streaming JSON.NET `JsonResult` with `MaxDepth = 5`, `NullValueHandling.Ignore`, `ReferenceLoopHandling.Ignore`) for large list responses — prefer it for endpoints that may return deep graphs or big payloads.

### `BaseController` is the spine of the API

Almost every controller derives from `Controllers/BaseController.cs`. It supplies:

- `db` — shared `EMB_ProductionEntities1` (disposed in `Dispose`)
- Identity resolution from `Session` with fallbacks to `Adm_Users`: `GetCurrentUserId()`, `GetSafeCurrentUserId()`, `GetCurrentEmployeeId()`, `GetCurrentBranchId()`, `GetCurrentRoleId()`
- `JsonSuccess(message, data)` / `JsonError(message, ex)` — the standard response envelope `{ success, message, data }`
- `ParsePurchaseDate` — handles ISO strings and `/Date(…)/` payloads from older Angular code
- ERP domain helpers: `ValidateFinishedStockBalance`, `ValidateQcApprovalForDispatch`, `GetFreeCompanyStockBalance`, `GetCustomerReservedStockBalance`, `IsCustomerReservedStock`, `CanPostDispatch`

`GetCurrentUserId` will **fall back to the lowest-id active user** if the session/identity can't be resolved, so audit fields (`CreatedBy`/`UpdatedBy`) always reference a valid row. Be aware of this when investigating "wrong user" bugs — the fallback may be masking a real auth issue.

### Domain conventions

These patterns are pervasive and must be preserved when adding/modifying endpoints:

- **Soft delete**: filter with `x.Deleted != true` (not `== false`) — `Deleted` is nullable and unset rows must be included
- **Posting/reversal lifecycle**: most transactional entities (Dispatch, WorkOrders, QC_Inspection, DailyProduction, JobOrders) carry `IsPosted`, `IsReversed`, `Active`. Draft rows have `IsPosted != true`; reserved/consumed semantics only apply when `IsPosted == true && IsReversed != true`
- **Stock ledger semantics** (`Gen_Stocks`): rows are unified in/out ledger entries keyed by `StockStage` (`FINISHED`, …), `ItemType` (`PRODUCT`/`FINISHED`/raw), `TransactionType` (`IN`/`OUT`), `ReferenceType` (`QC_INSPECTION`, `DISPATCH`, …) and `ReferenceId`. **Free-company vs customer-reserved finished stock** is derived, not stored: see `BaseController.GetFreeCompanyStockBalance` / `GetCustomerReservedStockBalance` — a QC_INSPECTION-sourced IN whose `WorkOrders.CustomerId` is set counts as customer-reserved. Don't bypass these helpers; mirror them if you need a new view of the ledger
- **Audit fields**: `CreatedBy`, `UpdatedBy`, `CreatedDate`, `UpdatedDate` exist on most tables — populate via `GetCurrentUserId()` / `GetSafeCurrentUserId()`
- **Branch scoping**: most queries are filtered by `BranchId`; resolve via `GetCurrentBranchId()` rather than reading session directly

### Frontend architecture

- Standalone components bootstrapped in `src/main.ts` via `appConfig` (`src/app/app.config.ts`) — no NgModules
- Routing in `src/app/app.routes.ts`: a public `LoginComponent` and `HomeComponent`, everything else nested under `LayoutComponent` guarded by `AuthGuard`
- Auth: `shared/services/auth.service.ts` posts to `/emb-products/auth/login`, persists the user in `localStorage` under key `auth_user`, exposes `isLoggedIn$`. Cookies are sent with `withCredentials: true` because the MVC backend uses Forms Authentication
- API services live under `src/app/services/` and `src/app/shared/services/`; per-feature DTOs are in `src/app/shared/models/`. `DropdownDataService` caches reference-data lookups against `/emb-products/dropdowns/*`
- Exports/printing centralised in `shared/services/api-data.service.ts` (xlsx, jspdf-autotable, print window)

When adding a new feature module, the convention is: a `*.dto.ts` in `shared/models`, an `*.service.ts` in `services/`, a standalone component folder under `src/app/<feature>/`, and a route inside the `LayoutComponent` children in `app.routes.ts`. `src/app/models/module-template/` + `shared/models/module-template.dto.ts` is a literal scaffold (row/detail view models, create/update DTO, paged API response shapes) — copy it as the starting point for a new module rather than improvising the shape.

## Notes / gotchas

- Project and solution names contain a space — always quote paths in scripts (e.g. `"EMB_Production 2/ClientApp/emb-products"`)
- There is a stray `ClientApp/AspNetProject/` folder which is empty/unused
- `Models/Model11.cs` and `Models/Model1.Context1.cs` are siblings of the primary EDMX-generated files; the active context is `EMB_ProductionEntities1`. Regenerating the EDMX will overwrite these
- `Newtonsoft.Json.JsonConvert.DefaultSettings` is set globally to camelCase — Angular DTOs expect camelCase property names. If a controller returns PascalCase (some `AuthController` responses do, e.g. `Id`, `UserName`), the Angular side handles both — but new endpoints should rely on the default camelCase
- `Web.config` defines two connection strings (`EMB_ProductionEntities` and `EMB_ProductionEntities1`) pointing at the same database — keep them in sync if the server/DB ever changes
