using EMB_Production.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/production-ledger")]
    public class ProductionLedgerController : BaseController
    {
        [HttpGet, Route("by-production/{productionId:int}")]
        public ActionResult ByProduction(int productionId)
        {
            var prod = db.DailyProduction
                .Include("WorkOrderItems")
                .Include("WorkOrderItems.WorkOrders")
                .FirstOrDefault(x => x.Id == productionId);

            if (prod == null || prod.WorkOrderItems == null)
                return JsonError("Production not found.");

            int? productId = prod.WorkOrderItems.ProductId;
            int? designId  = prod.WorkOrderItems.DesignId;
            int? branchId  = prod.WorkOrderItems.WorkOrders != null
                                ? prod.WorkOrderItems.WorkOrders.BranchId
                                : (int?)null;

            // Production lifecycle starts at the posted date (or created date
            // as fallback). Dispatches before this can't have consumed THIS
            // production's stock, so we only surface dispatches from this point
            // forward.
            DateTime fromTime = prod.PostedDate ?? prod.CreatedDate ?? DateTime.MinValue;

            // -------- 1. QC_INSPECTION rows pinned to THIS production --------
            // QC writes Gen_Stocks rows with ReferenceType='QC_INSPECTION' and
            // ReferenceId = QC.Id. Filtering on this chain ties the ledger to
            // this production exactly.
            var qcIds = db.QC_Inspection
                .Where(x => x.ProductionId == productionId
                         && (x.Deleted == false || x.Deleted == null))
                .Select(x => x.Id)
                .ToList();

            var qcRows = qcIds.Count > 0
                ? db.Gen_Stocks
                    .Where(s => (s.Deleted == false || s.Deleted == null)
                             && s.ReferenceType == "QC_INSPECTION"
                             && s.ReferenceId.HasValue
                             && qcIds.Contains(s.ReferenceId.Value))
                    .ToList()
                : new List<Models.Gen_Stocks>();

            // -------- 2. DISPATCH rows for the same product / branch --------
            // Dispatches consume from the product's pooled stock, not from a
            // specific production batch — so we can't pin them perfectly to one
            // production. We approximate by showing every DISPATCH OUT row for
            // this product+branch from the production's post date forward, so
            // the user has a useful "what shipped after this batch landed" view.
            var dispatchRows = (productId.HasValue && productId.Value > 0)
                ? db.Gen_Stocks
                    .Where(s => (s.Deleted == false || s.Deleted == null)
                             && s.ReferenceType == "DISPATCH"
                             && s.ProdId == productId.Value
                             && (!branchId.HasValue || s.BranchId == branchId.Value)
                             && s.RecordTimeStamp >= fromTime
                             && (s.OutQty ?? 0m) > 0m)
                    .ToList()
                : new List<Models.Gen_Stocks>();

            // -------- 3. Merge, normalize, and ISO-format dates --------
            // Dispatch reference comes from Gen_Stocks.ReferenceNo (the
            // dispatch flow stores its display code there at write time).
            // Falls back to "Dispatch #<id>" if ReferenceNo wasn't populated.
            //
            // ISO 8601 (".ToString(\"o\")") so the Angular date pipe accepts
            // them. MVC's default Json() emits /Date(ms)/ which Angular 17+
            // rejects with NG02100.
            var merged = qcRows
                .Select(s => new
                {
                    date = s.RecordTimeStamp.HasValue
                            ? s.RecordTimeStamp.Value.ToString("o") : null,
                    itemType = string.IsNullOrWhiteSpace(s.ItemType) ? "QC" : s.ItemType,
                    inQty = s.InQty ?? 0m,
                    outQty = s.OutQty ?? 0m,
                    description = s.StockDescription
                })
                .Concat(dispatchRows.Select(s =>
                {
                    string dispatchRef = !string.IsNullOrWhiteSpace(s.ReferenceNo)
                                            ? s.ReferenceNo
                                            : ("Dispatch #" + (s.ReferenceId ?? 0));
                    string baseDesc = string.IsNullOrWhiteSpace(s.StockDescription)
                                        ? "Dispatched"
                                        : s.StockDescription;
                    return new
                    {
                        date = s.RecordTimeStamp.HasValue
                                ? s.RecordTimeStamp.Value.ToString("o") : null,
                        itemType = string.IsNullOrWhiteSpace(s.ItemType) ? "DISPATCH" : s.ItemType,
                        inQty = s.InQty ?? 0m,
                        outQty = s.OutQty ?? 0m,
                        description = baseDesc + " (" + dispatchRef + ")"
                    };
                }))
                .OrderBy(x => x.date)
                .ToList<object>();

            return JsonSuccess("Loaded successfully.", new
            {
                productionId  = productionId,
                productId     = productId,
                designId      = designId,
                qcCount       = qcIds.Count,
                dispatchCount = dispatchRows.Count,
                rows          = merged
            });
        }

        // ============================================================
        // RECENT — list recent posted DailyProduction entries so the
        // standalone /production-ledger page has something to pick from.
        //
        // Only productions that have at least one QC inspection are returned,
        // because productions without QC produce empty ledgers (no stock
        // movements yet) — surfacing them as pickable just confuses the user.
        //
        // Dates are returned as ISO-8601 strings (".ToString(\"o\")") so the
        // Angular date pipe accepts them. MVC's default Json() would emit
        // /Date(ms)/ which Angular 17+ rejects with NG02100.
        // ============================================================
        [HttpGet, Route("recent")]
        public ActionResult Recent(int top = 50)
        {
            if (top <= 0) top = 50;
            if (top > 200) top = 200;

            // Pre-compute the set of ProductionIds that have at least one QC
            // inspection, so the main list can filter to only useful rows.
            var qcProductionIds = db.QC_Inspection
                .Where(q => q.ProductionId.HasValue
                         && (q.Deleted == false || q.Deleted == null))
                .Select(q => q.ProductionId.Value)
                .Distinct()
                .ToList();

            var rows = db.DailyProduction
                .Include("WorkOrderItems")
                .Include("WorkOrderItems.WorkOrders")
                .Include("WorkOrderItems.WorkOrders.Gen_Products")
                .Include("WorkOrderItems.WorkOrders.Gen_Design")
                .Where(p => p.IsPosted == true
                         && (p.Deleted == false || p.Deleted == null)
                         && qcProductionIds.Contains(p.Id))
                .OrderByDescending(p => p.PostedDate ?? p.CreatedDate)
                .Take(top)
                .ToList()
                .Select(p =>
                {
                    DateTime? posted = p.PostedDate ?? p.CreatedDate;
                    return new
                    {
                        productionId = p.Id,
                        postedDate = posted.HasValue ? posted.Value.ToString("o") : null,
                        acceptedQty = p.AcceptedQty ?? 0,
                        rejectedQty = p.RejectedQty ?? 0,
                        workOrderCode = p.WorkOrderItems != null && p.WorkOrderItems.WorkOrders != null
                                        ? p.WorkOrderItems.WorkOrders.WorkOrderCode : "-",
                        productName   = p.WorkOrderItems != null && p.WorkOrderItems.WorkOrders != null
                                        && p.WorkOrderItems.WorkOrders.Gen_Products != null
                                        ? p.WorkOrderItems.WorkOrders.Gen_Products.Prod_Name : "-",
                        designCode    = p.WorkOrderItems != null && p.WorkOrderItems.WorkOrders != null
                                        && p.WorkOrderItems.WorkOrders.Gen_Design != null
                                        ? p.WorkOrderItems.WorkOrders.Gen_Design.D_Code : "-"
                    };
                })
                .ToList();

            return JsonSuccess("OK", rows);
        }
    }

}