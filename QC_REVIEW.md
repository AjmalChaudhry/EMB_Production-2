# QC Review — Full Project

Tracking checklist for a full review and QC pass across every module before this codebase is considered production-ready. Review findings and fixes land as commits on the `review/full-qc` branch; check items off as each module clears review.

## Modules

- [ ] Work Orders & Job Orders (order intake → production planning)
- [ ] Daily Production (shop-floor output, machine/operator tracking, stoppage logging)
- [ ] QC Inspection (batch-level inspection, rework/re-inspection loop)
- [ ] Inventory & Stock (`Gen_Stocks` ledger — raw materials & finished goods)
- [ ] Purchasing (requisitions, purchase orders, supplier management)
- [ ] Dispatch (finished-goods dispatch against customer orders)
- [ ] Admin (users, roles, branches, company profile)
- [ ] Angular frontend (`ClientApp/emb-products`) — build, routing, auth guard
- [ ] Cross-cutting: soft-delete conventions, posting/reversal lifecycle, branch scoping, audit fields

## Test plan

- [ ] Backend builds clean (Visual Studio / MSBuild, NuGet restore)
- [ ] Angular app builds (`npm run build:prod`) and SPA serves correctly from `Content/emb-products`
- [ ] Manual smoke test of each module's CRUD + posting/reversal flow
