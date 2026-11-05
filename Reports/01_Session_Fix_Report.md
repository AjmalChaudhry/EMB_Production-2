# Session Fix Report
**Project:** EMB Production 2
**Date:** 2026-06-16
**Stack:** ASP.NET MVC 5 (.NET 4.7.2) + Angular 20 + EF6 Database-First + SQL Server

This report covers every defect addressed in the current working session, the root cause of each, and the change(s) shipped.

---

## 1. Dispatch modal overlapping the topbar

**File:** `ClientApp/emb-products/src/app/dispatch/dispatch.component.css`

**Symptom:** The "Create Dispatch" modal header butted up against / under the sticky topbar; closing `X` and title were squashed together at the top edge.

**Root cause:** Two leftover global rules at the bottom of the component's CSS file were overriding the topbar offset:

| Rule | What it did |
|---|---|
| `.modal-dialog { margin: auto !important; }` | `!important` killed the `margin-top: calc(72px + 8px)` rule above it |
| `.modal.show .modal-dialog { min-height: calc(100vh - 1rem); }` | Used the **full viewport** for vertical centering, so the dialog midpoint sat under the topbar |

**Fix:** Shifted the **Bootstrap modal frame itself** down by `var(--topbar-height)` and right by `var(--sidebar-width)`. Removed the `margin: auto !important` for `.modal-dialog` (kept it for SweetAlert popups only). Updated `min-height` to subtract the topbar height.

```css
.page-wrap .modal { top: var(--topbar-height); }
@media (min-width: 992px) { .page-wrap .modal { left: var(--sidebar-width); } }
.page-wrap .modal.show .modal-dialog-centered {
  min-height: calc(100vh - var(--topbar-height) - 1rem);
}
```

---

## 2. Design Costing — Material/Production/Service/ProductCost/TotalProductCost stayed at 0

**Files:**
- `Controllers/DesignCostingsController.cs`
- `Controllers/BaseController.cs`
- `ClientApp/emb-products/src/app/services/design-costing.service.ts`
- `ClientApp/emb-products/src/app/designcosting/design-costing.ts`

**Symptom:** Clicking **Recalculate Cost** populated the Cost Breakdown columns (Thread, Machine, Labor, Electricity, Other, Total Cost, Cost/Unit, Selling/Unit) but the five product-cost fields stayed zero.

**Root causes — three compounding:**

1. The `Recalculate` POST endpoint **never returned** `materialCostPerUnit`, `productionCostPerUnit`, `serviceCostPerUnit`, `productCostPerUnit`, `totalProductCost`. The Angular form had bindings for these fields, but the response payload had no such keys.
2. `BaseController.GetProductMaterialPerUnitCost` reads only `Gen_Stocks` where `StockStage == "FINISHED"`. That table is empty before any QC has posted finished stock — so the material per-unit was returning 0 even with a fully-populated BOM.
3. The controller resolved productId from `design.ProductId`, ignoring the form's product dropdown. If a costing was being calculated for a different product than the design's stored one, lookups missed.

**Fixes applied:**

| # | Change |
|---|---|
| 2.1 | `Recalculate` and `Calculate` endpoints now compute and return all 5 product-cost fields using the same formulas `Save` uses (`material = thread/qty + stocksMaterialUnit`, `production = (machine+labor+elec+other)/qty`, `service = GetProductServicePerUnitCost`, `product = sum`, `total = product × qty`). |
| 2.2 | New helper `BaseController.GetDesignBomMaterialPerUnitCost(designId, branchId)` walks Components → `Gen_ComponentMaterials` BOM lines → values each line at `GetRawItemLatestRate` (most recent non-zero `Gen_Store.Rate` per branch, falling back to any branch). Used as a fallback when `GetProductMaterialPerUnitCost` returns 0. |
| 2.3 | `RecalculateCostingVm` already had an optional `ProductId`. The controller now resolves `linkedProductId = vm.ProductId ?? design.ProductId`. Angular's `design-costing.service.recalculate(...)` accepts a new `productId` argument and the component now passes `selectedProductId`. |
| 2.4 | **Defensive frontend math** — even without an MVC rebuild, the form locally computes `productionCostPerUnit = (machine+labor+elec+other)/qty` and `materialCostPerUnit = max(backend, thread/qty)`, then `productCostPerUnit = sum` and `totalProductCost = product × qty`. Guarantees no zeros once Cost Breakdown has populated. |

---

## 3. Design Costing — Service / Unit stayed at 0 even when a Service Rate existed

**Files:**
- `ClientApp/emb-products/src/app/designcosting/design-costing.ts`
- `ClientApp/emb-products/src/app/designcosting/design-costing.html`

**Symptom:** PRD-0003 Casual Shirt Cotton had a Service Rate (Double Stitch / 300 / Bahawalpur Branch) but Service / Unit on the costing form remained 0.

**Root causes:**
- The MVC backend's `GetProductServicePerUnitCost` only returns a rate when `productId + branchId` match exactly. Design branch and Service Rate branch sometimes diverge.
- The field was `readonly` — no manual override path existed.

**Fixes:**

| # | Change |
|---|---|
| 3.1 | After `Recalculate`, if `serviceCostPerUnit === 0`, the frontend calls `GET /emb-products/service-rates/list?productFilter={id}&activeFilter=active` (an endpoint that **already exists** — no MVC rebuild needed) and patches the field with the sum of matching Charge Factors. |
| 3.2 | Made the Service / Unit input editable: `[(ngModel)]="form.serviceCostPerUnit"` with `(ngModelChange)="recomputeProductCost()"`. Label shows a `(editable)` hint. |
| 3.3 | New method `recomputeProductCost()` recalculates `productCostPerUnit = material + production + service` and `totalProductCost = product × batchQty` on every Service edit. |

---

## 4. Dispatch — Cost / Base Price / Net Sale / Total stayed at 0

**Files:**
- `Controllers/DispatchItemsController.cs`
- `ClientApp/emb-products/src/app/dispatch/dispatch.component.ts`

**Symptom:** Creating a dispatch for Casual Shirt Cotton showed `Cost / PC = 0`, `Base Price = 0`, every calculation downstream zero — even though Design Costing for that product was fully populated (ProductCostPerUnit = 2,188, SellingPricePerUnit = 2,626).

**Root cause:** The dispatch backend's price-resolution chain didn't consult `DesignCosting` at all. It only looked at QC-posted stock (empty), prior DispatchItem rows (empty), and `Gen_Products.Price` (empty).

**Fixes — new fallback chain:**

`GetProductCostPrice` (for Cost / PC):
1. `Gen_Stocks.Rate` (QC finished stock)
2. Last `DispatchItem.CostPrice`
3. **NEW** — `DesignCosting.ProductCostPerUnit`
4. `Gen_Products.Price ÷ 1.10`

`GetCustomerBaseSalePrice` (for Base Price):
1. This customer's last paid price
2. Any customer's last paid price on this branch
3. **NEW** — `DesignCosting.SellingPricePerUnit`
4. `Gen_Products.Price`
5. Cost × 1.10

**Defensive frontend layer:** After `loadAvailableProducts` populates rows, any row with `costPrice = 0` and `baseSalePrice = 0` triggers a call to `GET /emb-products/design-costing/list?productId={id}` and patches the row from the saved costing. Works without MVC rebuild.

---

## 5. Designs Management — `DES-2026-009` Usage cell showed a bare dash

**File:** `ClientApp/emb-products/src/app/design/gen-design.component.html`

**Symptom:** The Usage column showed `JO N`, `₹`, and `C N` badges for designs in active use, but `—` for new designs. Unclear meaning.

**Root cause:** The fallback `<span class="text-muted small">—</span>` had no context; users assumed something was broken.

**Fix:** Replaced the bare dash with a clear `<span class="badge bg-light text-muted border">New</span>` carrying a tooltip: *"No components, costing, or job orders yet — define them to start using this design."*

---

## 6. Recurring lesson — frontend defensive layers

A theme across this session: the user's local environment makes MVC rebuilds friction-heavy, so any backend-only fix delays feedback. The pragmatic pattern that ended up working:

- Land the backend fix (correct, but invisible until next rebuild).
- Land a **parallel frontend defensive layer** that hits existing endpoints to recover the same data without needing the new backend code.
- Document the behaviour so once MVC is rebuilt, the backend value takes precedence and the frontend lookup becomes a no-op.

This pattern is now in three places: Design Costing service-rate lookup, Design Costing per-unit math, and Dispatch design-costing lookup.

---

## Build & Deploy Steps

1. **Angular** — from `EMB_Production 2/ClientApp/emb-products`:
   ```powershell
   npm run build:deploy
   ```
2. **MVC backend** — Visual Studio → Build → Rebuild Solution → restart IIS Express.
3. Hard refresh affected pages (Ctrl+F5).

After both deploys, all six fix areas above are active. Without the MVC rebuild, items 1, 3.2, 3.3, 5, and the defensive layers in 2.4 / 4 still apply via Angular alone.
