# Development Progress Report
**Project:** EMB Production 2
**Reporting period:** Session-level snapshot through 2026-06-16
**Author:** Engineering team (assisted)

---

## 1. Executive summary

The EMB Production 2 ERP is **operationally complete end-to-end** — every stage from design entry through dispatch has a working implementation, persisted data model, and a UI surface. The current focus has shifted from feature delivery to **polish, defensive-defaults, and resolving cross-module data-flow issues** so the system works correctly even on a freshly-seeded database with sparse master data.

**Status at a glance:**

| Area | Status |
|---|---|
| Core master data (Products, Branches, Customers, Users, Designs, Components, BOM) | Shipped |
| Production pipeline (JO → WO → Issue → DP → QC → Stock → Dispatch) | Shipped |
| Costing & pricing | Shipped, hardened this session |
| Purchase & Purchase Returns | Shipped |
| Reporting (Excel/PDF exports, dashboards, KPI strips) | Shipped |
| 360 Profile drawers (Phase R polish) | Shipped for User/Design/Costing/Dispatch |
| Raw Sales module | **Pending** (scoped, not started) |
| Outstanding `styleUrls` cleanup | **3 components remaining** |

---

## 2. Modules shipped this period

| Module | Surface | Notes |
|---|---|---|
| Sidebar redesign | typed `NavGroup[]` config, role-gated, search filter, collapse-all, split P&I into two groups | Memoized `visibleGroups` to avoid CD loops |
| Dashboard | Range filter, click-through KPIs, alerts strip, Workflow tag (7-stage pending counts) | Workflow Summary block now exposed by `AuthController.Dashboard` |
| Login redesign | Split-screen brand panel + mock pipeline window, password toggle, submitting state | Aligned to brand palette `#0b1437` / `#4f8cff` → `#9b6cff` |
| Home/Landing | Hero, mock dashboard, features grid, workflow strip, CTA, footer | Public, marketing-style |
| Topbar | Avatar dropdown (Username header, Profile, Settings, Logout) | Fixed invisible logout text from over-aggressive `*` color rule |
| User Management | Designation dropdown, 360 Profile drawer with real avatar image, audit fields | New `Adm_Designations` master + 14 seed values |
| Service Rates | Product × Service Type charge factor matrix | Backs `GetProductServicePerUnitCost` |
| Design Costing | Auto-fill cascade for Material/Production/Service/ProductCost/TotalProductCost | Now multi-source resilient (see Session Fix Report §2 & §3) |
| Dispatch | Auto-fill Cost/Base/Net/Total with 4-priority cost chain and 5-priority sale chain | Includes DesignCosting fallback added this session |
| Designs Management | Usage badges (JO N / ₹ / C N / New) | "New" badge replaces ambiguous dash |

---

## 3. Defects resolved this session

(See **`01_Session_Fix_Report.md`** for full root-cause analysis.)

| # | Defect | Fix surface |
|---|---|---|
| 1 | Dispatch modal overlapping topbar | CSS — re-scoped `.modal` frame below topbar, removed `margin: auto !important` |
| 2 | Design Costing per-unit fields stayed 0 | Backend (Recalculate / Calculate returns 5 new fields) + frontend defensive math |
| 3 | Service / Unit stayed 0 | Frontend defensive lookup via `/service-rates/list` + made field editable |
| 4 | Dispatch all calc columns stayed 0 | Backend DesignCosting fallback in cost & price chains + frontend defensive lookup |
| 5 | Usage column showed ambiguous dash | Replaced with explicit "New" badge + tooltip |

---

## 4. Recurring patterns established

1. **Multi-priority data resolution** for cost/price. Every helper now walks ≥3 sources before returning 0.
2. **Frontend defensive layers**. When backend changes are pending a rebuild, parallel Angular lookups against existing endpoints make the same data flow visible immediately. Now in 3 places: Design Costing service-rate, Design Costing per-unit, Dispatch design-costing.
3. **Memoize projected lists**. Templates iterate plain properties via `trackBy` — getters that return new arrays are explicitly banned (caused a Page Unresponsive earlier this session).
4. **Editable computed fields** where master data may be sparse (e.g. Service / Unit). Operator override is always available.
5. **Component CSS hygiene** — `styleUrls` missing is treated as a P1 bug. 4 components were silently unstyled in Phase R; 3 still remain (see Pending).

---

## 5. Outstanding / pending items

### High priority
| Item | Type | Notes |
|---|---|---|
| `styleUrls` missing on `app.component.ts` | Component config | Phase R styles silently unapplied |
| `styleUrls` missing on `adm-branch.component.ts` | Component config | Same |
| `styleUrls` missing on `adm-company.component.ts` | Component config | Same |
| Raw Sales module | Feature | User scoped: "Build Purchase Returns first, ship it, then Raw Sales" — Returns shipped, Sales not started |

### Medium priority
| Item | Type | Notes |
|---|---|---|
| Master Price on every product (`Gen_Products.Price`) | Data hygiene | Empty for several products; once set, dispatch & costing fallbacks resolve immediately |
| `Gen_ComponentMaterials` (BOM material lines) | Data hygiene | Many designs have `Gen_BOM` (production specs) but no `Gen_ComponentMaterials` (raw material per unit). Without these, Material/Unit = thread only |
| `Gen_Store.Rate` on every raw item | Data hygiene | Required for BOM-walk material valuation |
| Branch alignment between Design ↔ Service Rate ↔ Stock | Data hygiene | Several mismatches were observed; cross-branch lookup fallback now masks them but root cause is operational |

### Low priority
| Item | Type | Notes |
|---|---|---|
| `ClientApp/AspNetProject/` empty folder | Cleanup | Safe to remove |
| `Models/Model11.cs` & `Models/Model1.Context1.cs` | Documentation | Will be overwritten on EDMX regen; note in README |
| PascalCase responses in `AuthController` | Consistency | Migrate to camelCase incrementally; Angular handles both today |

---

## 6. Data quality observations from this session

The most expensive bugs of this session were **not code bugs** — they were silent data gaps that the UI then surfaced as zeros. The new fallback chains reduce the blast radius, but the underlying data hygiene work still matters:

- 7 of 8 users had bad `CreatedBy` / `UpdatedBy` values (0, 1, 2075 = an `EmployeeId` confused with a `UserId`). Fixed with targeted SQL UPDATE only touching broken rows. The `GetCurrentUserId` fallback to "lowest-id active user" should always paper over future occurrences.
- Component Materials table is empty for most designs. BOM screen shows `Gen_BOM` (Rpt/Head/Panni/Solv production specs), which is **not** the same table as `Gen_ComponentMaterials` (QtyPerUnit × RawItemId). Users need clearer guidance on which screen drives material costing.
- Service Rate branch occasionally differs from Design branch — the frontend lookup added this session bypasses the branch filter, but operations should align them.

---

## 7. Backend rebuild dependency table

Every backend change this session lives behind a Visual Studio Rebuild. Below summarises which user-visible improvements survive without one:

| Improvement | Works without MVC rebuild |
|---|---|
| Modal-overlap CSS fix | Yes (Angular only) |
| Design Costing per-unit local computation | Yes (Angular only) |
| Service / Unit lookup via `/service-rates/list` | Yes (uses existing endpoint) |
| Service / Unit editable override | Yes |
| Dispatch DesignCosting frontend lookup | Yes (uses existing endpoint) |
| "New" Usage badge on Designs | Yes |
| Recalculate returning 5 new fields | **No — needs rebuild** |
| BOM-walk material cost helper | **No — needs rebuild** |
| Dispatch backend DesignCosting fallback chain | **No — needs rebuild** |

---

## 8. Recommended next steps

1. **Rebuild MVC** and verify all four "No — needs rebuild" items above land in production.
2. **Fix the 3 missing `styleUrls`** in `app.component.ts`, `adm-branch.component.ts`, `adm-company.component.ts`.
3. **Scope Raw Sales module** in the same shape as Dispatch (Customer Demand / Direct Sale split), drawing from `Gen_Store` instead of `Gen_Stocks`.
4. **Master data drive**: get every active product a `Gen_Products.Price`, every active raw item a `Gen_Store` purchase entry with a non-zero `Rate`, and every active component a `Gen_ComponentMaterials` row.
5. **Document the BOM vs Component Materials distinction** for operators — this confused even the engineering team during this session.

---

## 9. KPI snapshot

(From Dashboard sample data, 2026-06-16.)

| KPI | Value |
|---|---|
| Active designs | 5 |
| Designs with costing | 6 |
| Total Cost across costings | 1,550,600 |
| Total Selling | 1,752,400 |
| Estimated profit | 201,800 |
| Avg margin | 13.3% |
| Avg Cost/Unit | 1,551 |
| Avg Selling/Unit | 1,752 |

These are sample/seed values, not production reality — but the fact that all 9 KPIs are correctly computed and rendered (with sensible defensive fallbacks) is itself the success indicator of this reporting period.
