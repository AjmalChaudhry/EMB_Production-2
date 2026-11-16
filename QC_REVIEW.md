# QC Review — Full Project

Full review and QC pass across every module, done against the conventions documented in `CLAUDE.md` (soft delete, posting/reversal lifecycle, branch scoping, stock-ledger semantics, audit fields). Review was read-only — no fixes applied yet. Findings below are grouped by module; fixes should land as follow-up commits on this branch, ideally High severity first.

## Modules reviewed

- [x] Work Orders & Job Orders
- [x] Daily Production
- [x] QC Inspection
- [x] Inventory & Stock
- [x] Purchasing
- [x] Dispatch
- [x] Admin & Auth
- [x] Angular frontend

## Cross-cutting themes (appear in multiple modules — fix once, verify everywhere)

1. **Unwhitelisted reflection-based `Update<X>Field` endpoints.** At least 9 controllers (`Gen_JobOrdersController`, `Gen_DesignController`, `Gen_ComponentsController`, `Gen_MachinesController`, `Gen_ThreadsController`, `Gen_ServiceRatesController`, `Gen_TheServicesController`, `Gen_ServiceTypeController`, `Adm_UsersController`-adjacent pattern) expose a generic "set this field" action that resolves a client-supplied `fieldName` string via reflection and calls `SetValue` with no property whitelist. Any of them lets a caller overwrite `Deleted`, `IsPosted`, `BranchId`, `CreatedBy`/`UpdatedBy`, or `Id` directly, bypassing every validated Create/Update/Delete path and the soft-delete/audit/branch-scoping invariants those paths enforce.
2. **Missing `[Authorize]` with no global fallback filter.** `App_Start/FilterConfig.cs` registers only `HandleErrorAttribute` — there is no app-wide authorization filter. `WorkOrdersController`, `Adm_UsersController`, `Adm_RolesController`, `Adm_CompanyProfileController`, the Branches controller, and `Gen_ComponentsController`'s `Create`/`Update` (explicitly `[AllowAnonymous]`) all lack `[Authorize]`, so unauthenticated or under-privileged requests can reach destructive/sensitive actions.
3. **Hard deletes contradicting the documented soft-delete convention.** `Gen_MachinesController`, `Gen_MachineParametersController`, `Gen_ProductsController`, `Gen_CatagoriesController`, `Gen_BrandsController`, `Gen_ComponentsController`, `Gen_JobOrdersController`, and one action in `Adm_UsersController` call `.Remove()` directly (several with no null-check, so a missing id throws unhandled) instead of setting `Deleted = true`.
4. **`CreatedBy`/`UpdatedBy` populated with `GetCurrentEmployeeId()` instead of `GetCurrentUserId()`.** Seen in `Gen_MachinesController`, `Gen_SuppliersController`, `Gen_ServiceRatesController` (Delete only), `Gen_TheServicesController` (Delete only), `Gen_ProductsController`, `Gen_ThreadsController`, `Hr_CustomersController`. Anywhere else in the app that joins `CreatedBy`/`UpdatedBy` against `Adm_Users.Id` resolves to the wrong user or nothing.
5. **Stored/reflected XSS via unescaped print/export HTML.** Backend (`Gen_BOMController.Print`, `JobTrackingHistoriesController.ExportPdf`/`Print`) and frontend (`api-data.service.ts printTable`, used by Users/Customers/Suppliers/Categories/Brands components) build HTML by string-concatenating user-entered fields with no encoding.
6. **Missing branch scoping (`GetCurrentBranchId()`) on single-record lookups and lists.** Seen across `QC_InspectionController`, `ProductionPlansController`, `IssueMaterialsController`, `ExtraMaterialRequestsController`, `DailyProductionsController`/`DailyProductionStopsController`, `Gen_StocksController`, `StockLedgerController`. A user scoped to one branch can often view/edit/post/reverse another branch's record just by supplying its id.

## Module findings

### Admin & Auth — most severe module, fix first

- **High** `AuthController.cs:365,517-520` — login and change-password compare/store passwords in **plain text**, no hashing at all.
- **High** `Adm_UsersController.cs:595,643,677` — `Detail(id)` returns the plaintext password for any user id, no auth, no ownership check (IDOR + credential leak).
- **High** `Adm_UsersController.cs` Excel/PDF exports — plaintext passwords embedded in exported files.
- **High** `Adm_UsersController.cs`, `Adm_RolesController.cs`, `Adm_CompanyProfileController.cs`, Branches controller — no `[Authorize]` anywhere, no global filter (see cross-cutting #2); anonymous requests can create/edit/delete users, roles, branches, company settings.
- **High** `Adm_CompanyProfileController.cs:287,432,622` — company `MasterPassword` read/written in plaintext, reachable anonymously.
- **Medium/High** `Adm_UsersController.cs:920-930` — `GetUserPhoto` path-traversal: filename taken from the route with no sanitization.
- **High** `BaseController.cs:94-108` — the documented `GetCurrentUserId()` lowest-id fallback is reachable by actual anonymous callers (not just a session edge case) given finding above — writes get attributed to an arbitrary real user.
- **Medium** `AuthController.cs:429-440` — login error handler leaks exception message/stack trace to the client.
- **Medium** `Adm_UsersController.cs:1109-1123` — hard delete inconsistent with the soft-delete `Delete(id)` action in the same controller.
- **Medium** `Web.config:17` — `debug="true"` left enabled.
- Not flagged: SQL parameterization is correct throughout; most soft-delete filters are correct; connection strings use integrated security (no embedded secret).

### Work Orders & Job Orders

- **High** `Gen_JobOrdersController.cs:2270-2332` — `UpdateJobOrderField` reflection mass-assignment (cross-cutting #1).
- **High** `Gen_JobOrdersController.cs:2344-2351` — hard delete, no null check, no `IsPosted` gate.
- **High** `Gen_JobOrderItemsController.cs:52,88` — `[Bind(Include=...)]` whitelists include `CreatedBy/CreatedDate/UpdatedBy/UpdatedDate/Deleted/BranchId`, so a client can set them directly in the request body (over-posting).
- **High** `WorkOrdersController.cs:2113` — `ReversePost`'s "has production" guard misses `DailyProduction` rows linked via `WorkOrderId` directly (only checks the `WorkOrderItemId` link) — a WO with real production can still be reverse-posted.
- **High** `WorkOrdersController.cs` — no `[Authorize]` on the controller (cross-cutting #2).
- **High** `WorkOrderItemsController.cs:566-589` — `Delete` has no "has production" guard (unlike `Update`), so an item with QC/production history can be soft-deleted and orphan that history.
- **High** `Gen_DesignController.cs:1398-1450` — same reflection mass-assignment pattern.
- **High** `Gen_ComponentsController.cs:899-945` — same reflection mass-assignment pattern; `:986-988` hard delete, no null check; `:511-512,578-579` — `Create`/`Update` marked `[AllowAnonymous]`.
- **High** `ProductionPlansController.cs:2042` — plan lookup by `Id` alone, no branch filter — cross-branch approve/reverse/close/delete.
- **High** `Gen_BOMController.cs:1303-1316` — `Print` builds HTML by string-concatenating `Remarks`/`Design`/`Component` with no encoding (stored XSS).
- **High** `JobTrackingHistoriesController.cs:387-394` — same unescaped-HTML export pattern (stored XSS).
- **Medium** `ProductionPlansController.cs:442-469` — `EligibleJobOrders` has no branch filter, unlike its neighbor `MachinesDropdown`.
- **Medium** `Services/DesignCostingService.cs:187,202-208` — for machines with `heads > 1`, `machineCost` is divided by `heads` twice relative to labor/electricity cost, understating machine cost and `costPerUnit`.
- Lower-confidence items noted but not verified to High: nullable `Deleted == false` undercounting in `Gen_BOMController`; Close/BulkClose not transactional in `ProductionPlansController`; a design-code generation edge case past 4-digit yearly counts; costing save not sharing a transaction with dependent WorkOrderItems refresh.

### Daily Production

- **High** `IssueMaterialsController.cs:503` (+`:252,318,402-403`) — `Deleted == false` instead of `!= true`, silently drops null-`Deleted` rows from the stock balance/list/detail/reverse.
- **High** `IssueMaterialsController.cs:156-216` — `Save()` checks balance then writes OUT rows with no row lock and no re-check at write time — classic double-issue race, can drive quantity negative.
- **High** `ExtraMaterialRequestsController.cs:467-511` — `Approve()` never checks requested qty against current balance (only `Submit()` does) — can issue into negative stock.
- **High** `ExtraMaterialRequestsController.cs:1162-1172` — a second, different (and more fragile) balance algorithm for the same ledger table than the rest of the app uses.
- **High** `Gen_MachinesController.cs:954-961` — hard delete, no null check, contradicts the controller's own soft-delete `Delete` action.
- **High** `Gen_MachinesController.cs:868-930` — `UpdateMachineField` reflection mass-assignment.
- **Medium** `IssueMaterialsController.cs:245-302`, `ExtraMaterialRequestsController.cs:88-95`, `DailyProductionsController.cs:220-247,584-593`, `DailyProductionStopsController.cs:23-194` — no branch scoping on list/detail (cross-cutting #6).
- **Medium** `Gen_MachinesController.cs` — audit fields via `GetCurrentEmployeeId()` not `GetCurrentUserId()` (cross-cutting #4), inconsistent with the sibling `Gen_MachineParametersController`'s own code comment calling this out.
- **Medium** `ExtraMaterialRequestsController.cs:592-620` — a `SUBMITTED` (pending) request can be soft-deleted directly, bypassing `Reject()`'s audit trail.
- **Medium** `DailyProductionsController.cs:1037,2091-2108,1619-1658` — rework/QC linkage encoded as a free-text `"[QC:n]"` tag inside the user-editable `Remarks` field; editing `Remarks` can flip rework detection and skip a quantity check.
- **Medium** `DailyProductionStopsController.cs` — stops can be added/edited/deleted after the parent production is posted, desyncing frozen cost figures from the underlying stop records.
- **Medium** `Gen_MachineParametersController.cs:655-661` — hard delete with no null check; `:410-412` unhandled NRE on a malformed POST body; `:142-182` bulk actions don't stamp `UpdatedBy`/`UpdatedDate`.

### QC Inspection

- **High** `QC_InspectionController.cs:1862-1929` — `AddStock` writes non-standard `TransactionType` values (`"QC-POST-FINISHED"` etc.) instead of `"IN"`/`"OUT"`, so `BaseController.ValidateQcApprovalForDispatch` (which filters on `"IN"`) can never find QC-sourced finished stock — permanently blocks dispatch of legitimately QC-approved goods (balance math itself still works since it doesn't filter on this field).
- **High** `QC_InspectionController.cs:829-923,1148-1282` — `ProcessQc` commits its own transaction then calls `Post()` directly inside the same `using(tx)` block, which opens a second transaction on the same context — throws, and the resulting rollback-of-a-committed-transaction compounds into an unhandled 500 on one-click "Process + Post".
- **High** `QC_InspectionController.cs:1759-1810` — `ResetProductionsForQc` (used by Reverse/Cancel) matches `DailyProduction` rows only by WorkOrder/Design/Product, not the specific QC batch — reversing one QC batch can incorrectly reverse a different, unrelated completed batch for the same combination.
- **High** `QC_InspectionItemsController.cs:695-789` — `SaveItem` checks only `Status == "Reversed"`, never `IsPosted`, so a posted QC's items can be silently edited with no corresponding stock adjustment.
- **Medium** `QC_InspectionController.cs:646-656` — duplicate-open-batch check has no row lock, unlike Post/Reverse/Process — concurrent `Create` calls can double-count one physical batch.
- **Medium** `QC_InspectionController.cs:726-803,1438-1502` — `Update`/`ContinueQc` don't take the row lock that Post/Reverse/Process do, so a concurrent post can read quantities mid-edit.
- **Medium** `QC_InspectionController.cs` — single-record lookups (Detail/Post/Reverse/ContinueQc/Reinspect) have no branch filter (cross-cutting #6).

### Inventory & Stock

- **High** `StockLedgerController.cs:21-34` — no branch filter at all; running balance merges every branch's ledger rows for an item into one number.
- **High** `Gen_FinishedStocksController.cs` (whole file) — reads/writes a separate stored `Qty` column instead of deriving from the `Gen_Stocks` ledger as documented; nothing keeps it synchronized, so screens built on it can disagree with `GetFreeCompanyStockBalance`/`GetCustomerReservedStockBalance`. `:117-137` `Adjust` also has no non-negative guard and no locking (lost-update race).
- **High** `Gen_StoreController.cs` `IssueWorkOrder` (~971-1060) and `Transfer` (~1893-1926) — balance is checked without the controller's own `LockBalanceSlice` helper despite running in a transaction — same TOCTOU double-issue/over-transfer race as the Daily Production module.
- **High** `Gen_ThreadsController.cs:542-604` — `UpdateThreadField` reflection mass-assignment, gated only by `Session["UserID"] != null`.
- **High** `Gen_ProductsController.cs:724-729`, `Gen_CatagoriesController.cs:917-923`, `Gen_BrandsController.cs:684-685` — hard deletes, no null check, contradicting soft delete used elsewhere in the same controllers.
- **Medium** `Gen_StocksController.cs` — re-implements the customer-reserved/free-company split on top of a query with extra filters (`Active`, `Prod_Code` prefix) the `BaseController` helpers don't apply, so the UI and dispatch-validation can disagree on available quantity; also no branch default/check on `List`/`Detail`.
- **Medium** `StockLedgerController.cs:24-25` — filters only `ItemId`, never `ProdId`, so calling it for a finished product returns an empty ledger.
- **Medium** `Gen_ProductsController.cs:510-519`, `Gen_ThreadsController.cs:533,594` — audit fields via `GetCurrentEmployeeId()` not `GetCurrentUserId()` (cross-cutting #4).

### Purchasing

- **High** `PurchasesController.cs:2274-2278` — `GetRawItemUnit` always returns the hard-coded `"PCS"`, ignoring the item's real unit — stamps the wrong unit onto every RAW/FABRIC GRN posted to the stock ledger.
- **High** `PurchasesController.cs:771-857` + `PurchaseOrdersController.cs:710-727` — a GRN line's quantity is never capped against the PO's outstanding quantity; over-receiving drives "outstanding" negative and causes it to be silently hidden rather than flagged.
- **Medium** `PurchasesController.cs:1523-1643` — `ReturnToSupplier` correctly adjusts the stock ledger but never updates the originating PO's `ReceivedQty`/`ReceivedAmount` or re-syncs it, so the PO's status/KPIs stay stale after a return.
- **Medium** `PurchasesController.cs:1723-1730` — soft-deleting a draft purchase marks its (also-deleted) line items `IsPosted = true`, even though they were never actually posted to stock.
- **Medium/High** `Gen_ServiceRatesController.cs:678` and `Gen_TheServicesController.cs:585` — `Delete()` stamps `UpdatedBy` with `GetCurrentEmployeeId()` while `Create`/`Update` use `GetCurrentUserId()` — breaks the `Adm_Users` join used by `Detail()`'s "updated by" display after any delete.
- **Medium** `Gen_ServiceRatesController.cs:628,672`, `Gen_TheServicesController.cs:531`, `Gen_ServiceTypeController.cs:472` — `Update`/`Delete` load the entity with no `Deleted != true` filter, allowing edits to a soft-deleted row.
- **Medium** `Gen_ServiceRatesController.cs:814`, `Gen_TheServicesController.cs:739`, `Gen_ServiceTypeController.cs:645` — same unwhitelisted reflection field-setter pattern (cross-cutting #1).
- **Medium** `Gen_SuppliersController.cs:539,599,628` — audit fields via `GetCurrentEmployeeId()` not `GetCurrentUserId()`.
- Not flagged: PO/PR/Purchase posting-reversal lifecycle, row locking on Post/Reverse, and SQL parameterization were all checked and found correct.

### Dispatch

- **High** `DispatchesController.cs` `PostDispatch` — never calls `CanPostDispatch()`, the role-gate helper that exists specifically for this; any authenticated user can post a dispatch regardless of role. `ValidateQcApprovalForDispatch`/`ValidateFinishedStockBalance` are also never invoked — both documented safety helpers are dead code on the live posting path.
- **High** `DispatchesController.cs:2182-2237` (duplicated at `DispatchItemsController.cs:851-906`) — the customer-scoped reserved-balance helper sums off the QC row's own `OutQty`, which never decreases because dispatch OUT rows are separate ledger entries — a customer's reserved stock balance never actually decreases after a dispatch posts, allowing repeat over-dispatch against the same reservation.
- **High** `DispatchesController.cs:1399,1604` — the UPDLOCK/row lock only covers the single dispatch row being posted, not the aggregate stock balance — two concurrent postings against *different* dispatch headers drawing on the same stock pool can both pass validation and both post, over-dispatching the shared pool.
- **High** `Hr_CustomersController.cs:650-714` — `UpdateCustomerField` reflection mass-assignment, bypasses the dedicated audit logger entirely.
- **Medium** `Hr_CustomersController.cs:704` (+`573,634,743`) — audit fields pulled from raw `Session["EmployeeId"]`/`Session["UserID"]` instead of `GetCurrentUserId()`/`GetSafeCurrentUserId()`, can write `UpdatedBy = 0`.
- **Medium** `Hr_CustomersController.cs:372-420` — `Update` loads via `Find(id)` with no `Deleted != true` guard, unlike `Detail`/`List`.
- **Medium** `DispatchItemsController.cs:303-512` — balance-check-then-write not wrapped in a transaction (unlike the parent `DispatchesController`'s Create/Update).
- Not flagged: soft-delete filters correct; Dispatch Create/Update/Post/Reverse are properly transactional at the header+items+stock level; no raw/unparameterized SQL.

### Angular frontend

- **High** `shared/services/api-data.service.ts:62-96` (`printTable`) — builds HTML by directly interpolating row data into a template literal and injecting via `document.write` with no escaping — any user-entered field (name, remarks, etc.) containing a script tag executes for whoever clicks Print. Confirmed reachable from Users, Customers, Suppliers, Categories, and Brands components.
- **High** `purchase/purchase.component.ts:722-744` — the save handler never checks `response.success`; a backend validation failure (HTTP 200, `{success:false}`) is shown to the user as "Saved." and the unsaved edits are discarded.
- **High** `dispatch/dispatch.component.ts:919-950` (`postDispatch`) — same missing `success` check; if the backend's post gate rejects the post, the UI still says "Dispatch posted successfully." and opens an invoice PDF for a dispatch that was never posted. (The sibling `confirmPost()` in the same file does this correctly — fix should mirror that.)
- **Medium** `dispatch/dispatch.component.ts:1027-1053` — delete/reverse subscriptions have no `error` callback; a backend rejection is swallowed with no user feedback.
- **Medium** `purchase.component.ts`, `purchase-requisition.component.ts`, `qc-inspection-items.ts` — subscribe to `route.queryParams` in `ngOnInit` with no `ngOnDestroy`, stacking subscriptions on repeated navigation and causing duplicate modal opens over time.
- **Medium** `environments/environment.prod.ts` — `apiUrl` hardcoded to `https://localhost:44310`; the production build actually served from `Content/emb-products` ships pointing at localhost.
- **Medium** `main.ts` vs `app.config.ts` — `main.ts` bootstraps with its own provider array and never imports `appConfig`; `app.config.ts`'s `appConfig` (with `provideAnimations()`) is dead code and separately references a non-existent `appRoutes` export — currently harmless only because nothing imports it, but a landmine for the next person who "cleans this up."
- **Medium** services across the app (`qc-inspection.service.ts`, `work-order.service.ts`, `gen-stocks.service.ts`, etc.) — HTTP responses typed `any`, with financial-quantity fields pulled through long `??` fallback chains guessing camelCase/PascalCase instead of using the real DTOs — a backend field rename silently defaults to `0` instead of failing loudly.
- **Low** `core/guards/auth.guard.ts` — dead/unreferenced guard calls a method (`isLoggedIn()`) that doesn't exist on `AuthService`; the real, correctly-wired guard (`auth/auth.guard.ts`) only checks `localStorage`, which is informational-only since the backend's Forms Auth cookie remains the real gate per `CLAUDE.md`.

## Suggested fix order

1. Admin & Auth plaintext passwords + missing `[Authorize]` (account takeover / full data exposure).
2. The reflection-based `Update<X>Field` endpoints across every module (one shared fix pattern: whitelist allowed field names, or remove the endpoints in favor of the validated Update actions).
3. Dispatch's missing `CanPostDispatch`/validation calls and the customer-reserved-balance bug (financial/stock correctness).
4. QC Inspection's `TransactionType` mismatch and double-transaction bug (blocks valid dispatches / crashes a common action).
5. Remaining High-severity race conditions and hard-deletes module by module.
6. Medium-severity branch-scoping and audit-field (`EmployeeId` vs `UserId`) fixes.
7. Frontend `success` check fixes on save/post handlers, then the XSS in `printTable`.

## Test plan

- [ ] Backend builds clean (Visual Studio / MSBuild, NuGet restore)
- [ ] Angular app builds (`npm run build:prod`) and SPA serves correctly from `Content/emb-products`
- [ ] Manual smoke test of each module's CRUD + posting/reversal flow
- [ ] Re-test each High-severity finding above after its fix lands
