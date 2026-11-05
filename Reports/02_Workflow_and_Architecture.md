# Comprehensive Workflow & Architecture Document
**Project:** EMB Production 2
**Domain:** Garment / embroidery manufacturing ERP
**Generated:** 2026-06-16

---

## 1. System at a glance

EMB Production 2 is a single-tenant manufacturing ERP for an embroidery/garment business. It models the full operational pipeline from **design → BOM → costing → job order → work order → material issue → daily production → QC → finished stock → dispatch**, with parallel branches for **purchase / purchase returns / raw stock** and **service rates / catalog services**.

| Layer | Tech |
|---|---|
| Backend | ASP.NET MVC 5 (.NET Framework 4.7.2) |
| ORM | Entity Framework 6, Database-First (EDMX) |
| Database | SQL Server (`localhost\SQLEXPRESS03` → `EMB_Production`) |
| Frontend | Angular 20 standalone components, Bootstrap 5.3.8, Angular Material |
| Build glue | Angular CLI writes `dist` directly into MVC's `Content/emb-products/`; `HomeController.Angular()` serves the SPA shell |
| Auth | Forms Authentication, server session, cookies sent `withCredentials: true` |

---

## 2. Request routing

`App_Start/RouteConfig.cs` registers four conventional routes **after** `MapMvcAttributeRoutes()`, so attribute-routed endpoints win:

1. `emb-products/api/{controller}/{action}/{id}` — generic API fallback
2. `emb-products/auth/{action}` → `AuthController` — login/logout/dashboard/check-auth
3. `Content/emb-products/{*pathInfo}` → `HomeController.Angular()` — SPA shell for every deep link
4. `{controller}/{action}/{id}` → default → also falls back to `HomeController.Angular()`

Every modern API controller uses `[RoutePrefix("emb-products/<area>")] + [Route("...")]`. Examples:

| Prefix | Controller |
|---|---|
| `emb-products/products` | `Gen_ProductsController` |
| `emb-products/gen-stocks` | `Gen_StocksController` |
| `emb-products/dropdowns` | `DropdownsController` |
| `emb-products/design-costing` | `DesignCostingController` |
| `emb-products/service-rates` | `Gen_ServiceRatesController` |
| `emb-products/dispatch` | `DispatchController` |
| `emb-products/work-orders` | `WorkOrdersController` |

`HomeController.Angular()` reads `~/Content/emb-products/index.html` from disk. If missing, it returns a plain-text "SPA not built" message — the canary for a skipped build step.

---

## 3. Data layer

EF6 Database-First. Two generated `DbContext` instances point at the same database — `EMB_ProductionEntities` and `EMB_ProductionEntities1`. **`BaseController.db` uses `EMB_ProductionEntities1`** and disposes it in `Dispose`. New controllers inherit `BaseController` and use that instance. Do not introduce a third context.

`Global.asax.cs` sets `JsonConvert.DefaultSettings` to camelCase. Large-payload endpoints use `Infrastructure/JsonNetResult` (streaming JSON.NET with `MaxDepth = 5`, `NullValueHandling.Ignore`, `ReferenceLoopHandling.Ignore`).

---

## 4. BaseController — the controller spine

Almost every API controller inherits `Controllers/BaseController.cs`. It supplies:

| Capability | Members |
|---|---|
| Shared DbContext | `db` (disposed in `Dispose`) |
| Identity resolution | `GetCurrentUserId()`, `GetSafeCurrentUserId()`, `GetCurrentEmployeeId()`, `GetCurrentBranchId()`, `GetCurrentRoleId()` |
| Response envelopes | `JsonSuccess(message, data)`, `JsonError(message, ex)` returning `{ success, message, data }` |
| Date helpers | `ParsePurchaseDate` accepting ISO or `/Date(…)/` payloads |
| Stock helpers | `GetFreeCompanyStockBalance`, `GetCustomerReservedStockBalance`, `IsCustomerReservedStock`, `GetThreadStockBalance`, `ValidateFinishedStockBalance`, `ValidateQcApprovalForDispatch`, `CanPostDispatch` |
| Cost helpers | `GetProductMaterialPerUnitCost`, `GetProductServicePerUnitCost`, `GetDesignBomMaterialPerUnitCost` (new), `GetRawItemLatestRate` (new) |

`GetCurrentUserId()` deliberately falls back to **the lowest-id active user** when session/identity can't be resolved — so audit fields (`CreatedBy`/`UpdatedBy`) always reference a valid row. When investigating "wrong user" bugs, check whether this fallback is hiding a real auth issue.

---

## 5. Domain conventions (preserve in every new endpoint)

| Convention | Rule |
|---|---|
| **Soft delete** | Always filter `x.Deleted != true` (not `== false`). `Deleted` is nullable; unset rows must be included. |
| **Posting/reversal** | Transactional entities (Dispatch, WorkOrders, QC_Inspection, DailyProduction, JobOrders) carry `IsPosted`, `IsReversed`, `Active`. Drafts: `IsPosted != true`. Active reservation: `IsPosted == true && IsReversed != true`. |
| **Stock ledger** | `Gen_Stocks` rows are unified in/out keyed by `StockStage`, `ItemType` (`PRODUCT`/`FINISHED`/raw), `TransactionType` (`IN`/`OUT`), `ReferenceType` (`QC_INSPECTION`, `DISPATCH`, …) and `ReferenceId`. Free vs customer-reserved finished stock is **derived**, never stored. |
| **Branch scoping** | Most queries filter by `BranchId`; resolve via `GetCurrentBranchId()` rather than reading session directly. |
| **Audit fields** | `CreatedBy`, `UpdatedBy`, `CreatedDate`, `UpdatedDate` exist on most tables — populate via `GetCurrentUserId()` / `GetSafeCurrentUserId()`. |
| **JSON casing** | Angular DTOs expect camelCase. `Newtonsoft.Json.JsonConvert.DefaultSettings` enforces this globally; new endpoints should rely on the default. |

---

## 6. Production pipeline — the seven workflow stages

| Stage | Module | Backend entity | Key transitions |
|---|---|---|---|
| 1 | **Job Order** | `Gen_JobOrders` | Created from customer demand or internal trigger |
| 2 | **Work Order** | `WorkOrders`, `WorkOrderItems` | Posted → reserves raw material capacity |
| 3 | **Issue Materials** | `Gen_Store` (raw ledger), `Gen_ThreadUsage` | OUT entries against work order |
| 4 | **Daily Production** | `Gen_DailyProduction` | Quantity produced per shift, sent to QC |
| 5 | **QC Inspection** | `QC_Inspection` | Approve / Rework / Reject; on Approve creates finished stock IN |
| 6 | **Finished Stock** | `Gen_Stocks` (StockStage=FINISHED, ItemType=PRODUCT) | Free-company or customer-reserved (derived) |
| 7 | **Dispatch** | `Dispatch`, `DispatchItems` | OUT against finished stock; supports Customer Demand or Direct Sale |

A parallel pipeline handles **Purchase → Purchase Returns → Raw Stock** feeding stage 3.

The Dashboard's **Workflow Summary** rolls up pending counts at each stage: `wosAwaitingMaterialIssue`, `dpDrafts`, `dpAwaitingQcSend`, `qcPendingInspection`, `qcAwaitingRework`, `qcAwaitingReinspection`, `dispatchDrafts`, and `totalPending`.

---

## 7. Polymorphic ledgers — raw vs finished

| Table | Purpose | Discriminators |
|---|---|---|
| `Gen_Store` | Raw material ledger | `ItemId` → `Gen_RawItems`, `TransactionType` (IN/OUT), `ReferenceType` (`PURCHASE`, `MATERIAL_ISSUE`, `RETURN`, …) |
| `Gen_Stocks` | Finished / product ledger | `ItemType` (`PRODUCT`, `FINISHED`, …), `ProdId` / `ItemId`, `StockStage` (`FINISHED`, `WIP`, …), `ReferenceType` (`QC_INSPECTION`, `DISPATCH`, …) |
| `Gen_ThreadUsage` | Thread-specific issuance | scoped to thread items only |

These three are deliberately **not** unified — raw and finished have different valuation lifecycles, and threading has its own colour/spool metadata.

---

## 8. Frontend architecture

- **Standalone components** bootstrapped in `src/main.ts` via `appConfig` (`src/app/app.config.ts`). No NgModules.
- **Routing** in `src/app/app.routes.ts`: public `LoginComponent` and `HomeComponent`, everything else nested under `LayoutComponent` guarded by `AuthGuard`.
- **Auth**: `shared/services/auth.service.ts` posts to `/emb-products/auth/login`, persists user in `localStorage` under `auth_user`, exposes `isLoggedIn$`. Cookies sent with `withCredentials: true`.
- **API services**: `src/app/services/` + `src/app/shared/services/`. Feature-specific DTOs in `src/app/shared/models/`.
- **Reference-data caching**: `DropdownDataService` caches `/emb-products/dropdowns/*` lookups.
- **Exports/printing**: centralised in `shared/services/api-data.service.ts` (xlsx, jspdf-autotable, print window).

### Adding a new feature module

1. `*.dto.ts` in `shared/models/`
2. `*.service.ts` in `services/`
3. Standalone component folder under `src/app/<feature>/`
4. Route inside `LayoutComponent` children in `app.routes.ts`

### Performance pattern (lesson from this session)

Templates must **never** call a getter that returns a fresh array — that causes infinite change-detection loops. Instead:

```typescript
// Bad
get visibleGroups(): NavGroup[] { return this.groups.filter(...); }

// Good
visibleGroups: NavGroup[] = [];
private refreshVisibleGroups(): void { this.visibleGroups = this.groups.filter(...); }
```

Recompute in input handlers, expose as plain properties, iterate with `*ngFor` + `trackBy`.

---

## 9. Pricing & costing data flow

This area gained significant fallback logic in the current session. Two helpers are now the source of truth:

### `DispatchItemsController.GetProductCostPrice(productId, branchId)`

| Priority | Source | When it fires |
|---|---|---|
| 1 | `Gen_Stocks.Rate` (QC finished stock) | Normal flow — QC posted a cost |
| 2 | Last `DispatchItem.CostPrice` (same branch) | Product dispatched before |
| 3 | `DesignCosting.ProductCostPerUnit` | Costing saved on design |
| 4 | `Gen_Products.Price / 1.10` | Master price set, no transaction history |

### `DispatchItemsController.GetCustomerBaseSalePrice(productId, branchId, customerId?)`

| Priority | Source | When it fires |
|---|---|---|
| 1 | Same customer's last sale | Returning customer, sticky pricing |
| 2 | Any customer's last sale (same branch) | First sale to this customer |
| 3 | `DesignCosting.SellingPricePerUnit` | Costing saved on design |
| 4 | `Gen_Products.Price` (master) | Set by Products module |
| 5 | Cost × 1.10 | Original markup default |

### Design Costing per-unit composition

```
materialCostPerUnit   = ThreadCost / batchQty + GetProductMaterialPerUnitCost(prod, branch)
                        ↳ if 0, fall back to GetDesignBomMaterialPerUnitCost(design, branch)

productionCostPerUnit = (MachineCost + LaborCost + ElectricityCost + OtherCost) / batchQty

serviceCostPerUnit    = GetProductServicePerUnitCost(prod, branch)
                        ↳ if 0 and frontend, fall back to /service-rates/list lookup

productCostPerUnit    = material + production + service
totalProductCost      = product × batchQty
sellingPricePerUnit   = product × (1 + margin/100)
```

---

## 10. Defensive design patterns (project-wide guidelines)

1. **Multi-priority fallback chains** for pricing/costing — never let a single missing data source produce a zero.
2. **Frontend defensive math** mirrors backend fallbacks so deploys don't strictly require an MVC rebuild for visible fixes.
3. **Soft-delete-aware filters** everywhere (`Deleted != true`).
4. **Branch scoping** by default; design-level overrides only where the user explicitly chose otherwise.
5. **Audit non-null guarantee** via `GetCurrentUserId()` fallback.
6. **Editable computed fields** where the data source may be empty (e.g. Service / Unit) so the operator can override.

---

## 11. Build & run

### Backend
- Open `EMB_Production 2.sln` in Visual Studio.
- Restore NuGet packages (`packages.config`-based).
- Build & run under IIS Express → binds to `https://localhost:44310/`.

### Frontend dev workflow
```powershell
cd "EMB_Production 2/ClientApp/emb-products"
npm install
npm start              # ng serve on :4200
npm run build:dev      # development build
npm run build:prod     # production (default)
npm run build:deploy   # clean → prod → robocopy into MVC Content/emb-products
npm test               # karma + jasmine
```

`environment.ts` points `apiUrl` at `https://localhost:44310`. `proxy.conf.json` only proxies `/Gen_Products` and is mostly vestigial.

---

## 12. Known sharp edges

- Project & solution names contain a space — **always quote paths in scripts**.
- `ClientApp/AspNetProject/` exists and is empty/unused.
- `Models/Model11.cs` and `Models/Model1.Context1.cs` are siblings of the EDMX-generated files. The **active** context is `EMB_ProductionEntities1`. Regenerating the EDMX overwrites these.
- `Web.config` defines two connection strings (`EMB_ProductionEntities` and `EMB_ProductionEntities1`) pointing at the same database — keep them in sync.
- Several `AuthController` responses still use PascalCase (`Id`, `UserName`). New endpoints should rely on the default camelCase serialization.
