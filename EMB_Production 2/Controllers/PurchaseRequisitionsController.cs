using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    // ============================================================
    // Purchase Requisition (PR)
    // ------------------------------------------------------------
    // Internal request for materials. Single-step approval.
    // On Approve: status → CONVERTED_TO_PO and one PurchaseOrder
    // is auto-created from the PR (1:1 mapping per user spec).
    //
    // Lifecycle:
    //   DRAFT → SUBMITTED → APPROVED → CONVERTED_TO_PO
    //                                ↘ REJECTED
    //
    // Tables created by App_Data/Scripts/CreatePurchaseRequisitionAndOrder.sql.
    // Reads/writes via raw SQL — no EDMX refresh required.
    // ============================================================
    [RoutePrefix("emb-products/purchase-requisition")]
    public class PurchaseRequisitionsController : BaseController
    {
        // --------------------------------------------------------
        // Flat projections used by /list, /detail
        // --------------------------------------------------------
        public class PrRow
        {
            public int    Id { get; set; }
            public string PrCode { get; set; }
            public int?   BranchId { get; set; }
            public string BranchName { get; set; }
            public DateTime RequestDate { get; set; }
            public int?   SupplierId { get; set; }
            public string SupplierName { get; set; }
            public string SourceType { get; set; }
            public int?   SourceReferenceId { get; set; }
            public string Status { get; set; }
            public string Remarks { get; set; }
            public int?   RequestedBy { get; set; }
            public string RequestedByName { get; set; }
            public DateTime? SubmittedDate { get; set; }
            public int?   ApprovedBy { get; set; }
            public string ApprovedByName { get; set; }
            public DateTime? ApprovedDate { get; set; }
            public string RejectionReason { get; set; }
            public int?   ConvertedToPoId { get; set; }
            public string ConvertedToPoCode { get; set; }
            public bool?  Active { get; set; }
            public DateTime? CreatedDate { get; set; }
            public DateTime? UpdatedDate { get; set; }
        }

        public class PrItemRow
        {
            public int     Id { get; set; }
            public int     PrId { get; set; }
            public int     RawItemId { get; set; }
            public string  RawItemName { get; set; }
            public int?    UnitId { get; set; }
            public string  UnitCode { get; set; }
            public decimal Qty { get; set; }
            public decimal? EstimatedRate { get; set; }
            public decimal? EstimatedAmount { get; set; }
            public string  Remarks { get; set; }
        }

        // Sprint-1: per-PR aggregate shape for the List endpoint's batched
        // items query. ItemCount feeds the "lines" chip; TotalValue feeds
        // the Value column right-aligned with currency formatting.
        private class PrAggRow
        {
            public int     Id         { get; set; }
            public int     ItemCount  { get; set; }
            public decimal TotalValue { get; set; }
        }

        // ====================================================================
        //  SPRINT-1  STATUS + SOURCE TYPE CONSTANTS
        // --------------------------------------------------------------------
        //  Centralised so a typo in one endpoint never silently breaks
        //  another. Values match the DB exactly (UPPER_SNAKE — the convention
        //  matches existing rows in Status / SourceType columns).
        // ====================================================================
        private static class PrStatus
        {
            public const string Draft         = "DRAFT";
            public const string Submitted     = "SUBMITTED";
            public const string Approved      = "APPROVED";
            public const string ConvertedToPo = "CONVERTED_TO_PO";
            public const string Rejected      = "REJECTED";
        }

        private static class PrSource
        {
            public const string AdHoc          = "AD_HOC";
            public const string EmrShortfall   = "EMR_SHORTFALL";
            public const string PlanShortfall  = "PLAN_SHORTFALL";
            public const string PlanApprove    = "PLAN_APPROVE";
            public const string WoPost         = "WO_POST";
        }

        // ====================================================================
        // LIST
        // ====================================================================
        [HttpGet, Route("list")]
        public ActionResult List(
            string status = "", int? branchId = null, int? supplierId = null,
            string search = "", string showDeleted = "",
            // Sprint-3: source-type filter (WO_POST / PLAN_APPROVE / etc.)
            // and date range filter for the RequestDate column.
            string sourceType = null, string fromDate = null, string toDate = null)
        {
            try
            {
                string deletedClause = string.Equals(showDeleted, "yes", StringComparison.OrdinalIgnoreCase)
                    ? " (pr.Deleted = 1) "
                    : " (pr.Deleted = 0 OR pr.Deleted IS NULL) ";

                var parameters = new List<SqlParameter>();
                string where = " WHERE " + deletedClause;

                if (!string.IsNullOrWhiteSpace(status))
                {
                    where += " AND pr.Status = @status ";
                    parameters.Add(new SqlParameter("@status", status.Trim().ToUpper()));
                }
                if (branchId.HasValue && branchId.Value > 0)
                {
                    where += " AND pr.BranchId = @brId ";
                    parameters.Add(new SqlParameter("@brId", branchId.Value));
                }
                if (supplierId.HasValue && supplierId.Value > 0)
                {
                    where += " AND pr.SupplierId = @supId ";
                    parameters.Add(new SqlParameter("@supId", supplierId.Value));
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    where += " AND (pr.PrCode LIKE @s OR pr.Remarks LIKE @s OR s.SupplierName LIKE @s) ";
                    parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
                }

                // Sprint-3: source-type filter
                if (!string.IsNullOrWhiteSpace(sourceType))
                {
                    where += " AND pr.SourceType = @srcType ";
                    parameters.Add(new SqlParameter("@srcType", sourceType.Trim().ToUpper()));
                }

                // Sprint-3: date range filter. fromDate is inclusive,
                // toDate is end-of-day inclusive (add 1 day - 1 tick).
                DateTime fromDt, toDt;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out fromDt))
                {
                    where += " AND pr.RequestDate >= @fromDt ";
                    parameters.Add(new SqlParameter("@fromDt", fromDt.Date));
                }
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out toDt))
                {
                    where += " AND pr.RequestDate <= @toDt ";
                    parameters.Add(new SqlParameter("@toDt", toDt.Date.AddDays(1).AddTicks(-1)));
                }

                string sql = @"
SELECT  pr.Id, pr.PrCode, pr.BranchId,
        b.Name AS BranchName,
        pr.RequestDate,
        pr.SupplierId, s.SupplierName,
        pr.SourceType, pr.SourceReferenceId,
        pr.Status, pr.Remarks,
        pr.RequestedBy, ur.UserName AS RequestedByName,
        pr.SubmittedDate,
        pr.ApprovedBy,  ua.UserName AS ApprovedByName,
        pr.ApprovedDate, pr.RejectionReason,
        pr.ConvertedToPoId,
        po.PoCode AS ConvertedToPoCode,
        pr.Active, pr.CreatedDate, pr.UpdatedDate
FROM    dbo.PurchaseRequisition pr
LEFT JOIN dbo.Branches      b  ON b.Id  = pr.BranchId
LEFT JOIN dbo.Gen_Suppliers s  ON s.Id  = pr.SupplierId
LEFT JOIN dbo.Adm_Users     ur ON ur.Id = pr.RequestedBy
LEFT JOIN dbo.Adm_Users     ua ON ua.Id = pr.ApprovedBy
LEFT JOIN dbo.PurchaseOrder po ON po.Id = pr.ConvertedToPoId
" + where + " ORDER BY pr.Id DESC ";

                var rows = db.Database.SqlQuery<PrRow>(sql, parameters.ToArray()).ToList();

                // ─────────────────────────────────────────────────────────
                //  SPRINT-1  ENRICHMENT: items count + total value per PR
                //  One batched query covers every row on the page so the
                //  N+1 fan-out never kicks in. Wrapped in try/catch so the
                //  list still loads if the items table is missing on a
                //  legacy deployment — the badges just default to 0.
                // ─────────────────────────────────────────────────────────
                var itemCountByPr  = new Dictionary<int, int>();
                var totalValueByPr = new Dictionary<int, decimal>();
                var prIds = rows.Select(r => r.Id).ToList();
                if (prIds.Count > 0)
                {
                    try
                    {
                        var aggRows = db.Database.SqlQuery<PrAggRow>(@"
SELECT  PrId AS Id,
        COUNT(*) AS ItemCount,
        ISNULL(SUM(ISNULL(EstimatedAmount, Qty * ISNULL(EstimatedRate, 0))), 0) AS TotalValue
FROM    dbo.PurchaseRequisitionItems
WHERE   (Deleted = 0 OR Deleted IS NULL)
   AND  PrId IN (" + string.Join(",", prIds) + @")
GROUP BY PrId").ToList();
                        foreach (var a in aggRows)
                        {
                            itemCountByPr[a.Id]  = a.ItemCount;
                            totalValueByPr[a.Id] = a.TotalValue;
                        }
                    }
                    catch (System.Data.SqlClient.SqlException) { }
                }

                // Re-project to camelCase keys. JsonSuccess serializes via
                // JavaScriptSerializer which preserves PascalCase property names —
                // but the Angular DTOs (PrRow in purchase-requisition.dto.ts) are
                // camelCase. Without this projection every field on the list page
                // renders as "undefined" / "—" because the keys don't match.
                // Dates: JavaScriptSerializer emits DateTime as /Date(ms)/ which
                // Angular's `| date` pipe can't read. Format as ISO 8601 strings
                // (yyyy-MM-ddTHH:mm:ss for timestamps, yyyy-MM-dd for the request
                // date which is treated as a date-only on the list).
                var items = rows.Select(r => new
                {
                    id                 = r.Id,
                    prCode             = r.PrCode,
                    branchId           = r.BranchId,
                    branchName         = r.BranchName,
                    requestDate        = r.RequestDate.ToString("yyyy-MM-dd"),
                    supplierId         = r.SupplierId,
                    supplierName       = r.SupplierName,
                    sourceType         = r.SourceType,
                    sourceReferenceId  = r.SourceReferenceId,
                    status             = r.Status,
                    remarks            = r.Remarks,
                    requestedBy        = r.RequestedBy,
                    requestedByName    = r.RequestedByName,
                    submittedDate      = r.SubmittedDate.HasValue
                                              ? r.SubmittedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    approvedBy         = r.ApprovedBy,
                    approvedByName     = r.ApprovedByName,
                    approvedDate       = r.ApprovedDate.HasValue
                                              ? r.ApprovedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    rejectionReason    = r.RejectionReason,
                    convertedToPoId    = r.ConvertedToPoId,
                    convertedToPoCode  = r.ConvertedToPoCode,
                    active             = r.Active,
                    createdDate        = r.CreatedDate.HasValue
                                              ? r.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    updatedDate        = r.UpdatedDate.HasValue
                                              ? r.UpdatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,

                    // Sprint-1 enrichments
                    itemCount          = itemCountByPr.ContainsKey(r.Id)  ? itemCountByPr[r.Id]  : 0,
                    totalValue         = totalValueByPr.ContainsKey(r.Id) ? totalValueByPr[r.Id] : 0m
                }).ToList();

                var summary = new
                {
                    count          = rows.Count,
                    draftCount     = rows.Count(r => r.Status == "DRAFT"),
                    submittedCount = rows.Count(r => r.Status == "SUBMITTED"),
                    approvedCount  = rows.Count(r => r.Status == "APPROVED"
                                                  || r.Status == "CONVERTED_TO_PO"),
                    rejectedCount  = rows.Count(r => r.Status == "REJECTED")
                };

                return JsonSuccess("OK", new { items, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load PR list.", ex);
            }
        }

        // ====================================================================
        // DETAIL
        // ====================================================================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var h = LoadHeader(id);
                if (h == null) return JsonError("PR not found.");

                var rawItems = LoadItems(id);

                // Same camelCase re-projection as /list — keep this in sync if
                // either side changes.
                var header = new
                {
                    id                 = h.Id,
                    prCode             = h.PrCode,
                    branchId           = h.BranchId,
                    branchName         = h.BranchName,
                    requestDate        = h.RequestDate.ToString("yyyy-MM-dd"),
                    supplierId         = h.SupplierId,
                    supplierName       = h.SupplierName,
                    sourceType         = h.SourceType,
                    sourceReferenceId  = h.SourceReferenceId,
                    status             = h.Status,
                    remarks            = h.Remarks,
                    requestedBy        = h.RequestedBy,
                    requestedByName    = h.RequestedByName,
                    submittedDate      = h.SubmittedDate.HasValue
                                              ? h.SubmittedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    approvedBy         = h.ApprovedBy,
                    approvedByName     = h.ApprovedByName,
                    approvedDate       = h.ApprovedDate.HasValue
                                              ? h.ApprovedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    rejectionReason    = h.RejectionReason,
                    convertedToPoId    = h.ConvertedToPoId,
                    convertedToPoCode  = h.ConvertedToPoCode,
                    active             = h.Active,
                    createdDate        = h.CreatedDate.HasValue
                                              ? h.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    updatedDate        = h.UpdatedDate.HasValue
                                              ? h.UpdatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null
                };

                var items = rawItems.Select(i => new
                {
                    id              = i.Id,
                    prId            = i.PrId,
                    rawItemId       = i.RawItemId,
                    rawItemName     = i.RawItemName,
                    unitId          = i.UnitId,
                    unitCode        = i.UnitCode,
                    qty             = i.Qty,
                    estimatedRate   = i.EstimatedRate,
                    estimatedAmount = i.EstimatedAmount,
                    remarks         = i.Remarks
                }).ToList();

                return JsonSuccess("OK", new
                {
                    header,
                    items,
                    // Sprint-2: workflow stepper data. workflowStages is the
                    // canonical lifecycle order; workflowStageIndex is where
                    // this PR sits today. The stepper component uses both to
                    // render past/current/upcoming markers.
                    workflowStages     = _prWorkflowStages,
                    workflowStageIndex = ComputePrWorkflowStageIndex(h.Status)
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load PR detail.", ex);
            }
        }

        [HttpGet, Route("next-code")]
        public ActionResult NextCode()
        {
            try { return JsonSuccess("OK", new { code = GenerateCode() }); }
            catch (Exception ex) { return JsonError("Failed to generate PR code.", ex); }
        }

        [HttpGet, Route("suppliers-dropdown")]
        public ActionResult SuppliersDropdown()
        {
            try
            {
                var list = db.Gen_Suppliers
                    .Where(s => s.Deleted != true)
                    .OrderBy(s => s.SupplierName)
                    .Select(s => new { id = s.Id, name = s.SupplierName })
                    .ToList();
                return JsonSuccess("OK", list);
            }
            catch (Exception ex) { return JsonError("Failed to load suppliers.", ex); }
        }

        [HttpGet, Route("raw-items-dropdown")]
        public ActionResult RawItemsDropdown()
        {
            try
            {
                // Three procurement categories surface in one searchable dropdown.
                // Every option resolves to a Gen_RawItems.Id (id field) so the PR
                // schema and the downstream PO/GRN flow stay unchanged. Threads
                // resolve via Gen_Threads.RawItemId — their backing raw item.
                var result = new List<object>();

                // 1) Raw materials (everything in Gen_RawItems that isn't FABRIC)
                var raw = db.Gen_RawItems
                    .Where(r => r.Deleted == false
                             && (r.Type == null || r.Type.ToUpper() != "FABRIC"))
                    .OrderBy(r => r.ItemName)
                    .Select(r => new {
                        r.Id, r.ItemName, r.Type, r.UnitId
                    })
                    .ToList();
                foreach (var r in raw)
                {
                    result.Add(new {
                        id       = r.Id,
                        name     = "[RAW] " + r.ItemName,
                        type     = "RAW",
                        unitId   = r.UnitId,
                        category = "Raw Materials"
                    });
                }

                // 2) Fabrics (Gen_RawItems where Type = FABRIC)
                var fabrics = db.Gen_RawItems
                    .Where(r => r.Deleted == false
                             && r.Type != null && r.Type.ToUpper() == "FABRIC")
                    .OrderBy(r => r.ItemName)
                    .Select(r => new { r.Id, r.ItemName, r.UnitId })
                    .ToList();
                foreach (var f in fabrics)
                {
                    result.Add(new {
                        id       = f.Id,
                        name     = "[FABRIC] " + f.ItemName,
                        type     = "FABRIC",
                        unitId   = f.UnitId,
                        category = "Fabrics"
                    });
                }

                // 3) Threads (Gen_Threads). Each thread has a backing raw item
                //    that owns the actual store ledger. We expose the thread
                //    by code+name+color but submit the backing RawItemId so
                //    GRNs and Gen_Store entries land on the right ledger key.
                var threads = db.Gen_Threads
                    .Where(t => t.Deleted != true && t.RawItemId.HasValue)
                    .OrderBy(t => t.Code)
                    .Select(t => new {
                        t.RawItemId, t.Code, t.Name, t.Color, t.UnitOfMeasure, t.UnitCost
                    })
                    .ToList();
                foreach (var t in threads)
                {
                    string display = "[THREAD] " + (t.Code ?? "")
                                   + (string.IsNullOrEmpty(t.Code) ? "" : " - ")
                                   + (t.Name ?? "")
                                   + (string.IsNullOrEmpty(t.Color) ? "" : " (" + t.Color + ")");
                    result.Add(new {
                        id       = t.RawItemId.Value,    // backing raw item id
                        name     = display,
                        type     = "THREAD",
                        unitId   = (int?)null,           // thread unit comes from UnitOfMeasure string
                        category = "Threads"
                    });
                }

                return JsonSuccess("OK", result);
            }
            catch (Exception ex) { return JsonError("Failed to load raw items.", ex); }
        }

        // ====================================================================
        // SHORTFALL SUGGESTIONS
        // --------------------------------------------------------------------
        // Powers the "auto-fill from store status" behavior on New PR. Returns
        // every active raw item whose summed Gen_Store balance is below the
        // configured ReorderLevel. The PR modal pre-populates its items grid
        // with these rows; the user can edit qty, delete rows, or add others.
        //
        // Suggested order qty:
        //   ReorderQty if explicitly set on Gen_RawItems
        //   else (ReorderLevel - CurrentBalance)  — restock to the reorder line
        //
        // Estimated rate:
        //   weighted-average Gen_Threads.UnitCost for thread-backed items,
        //   otherwise null (buyer fills in manually before posting).
        // ====================================================================
        [HttpGet, Route("shortfall-suggestions")]
        public ActionResult ShortfallSuggestions()
        {
            try
            {
                // 1) Aggregate Gen_Store balances per raw item (across all
                //    branches — PRs are procurement-level, not branch-level).
                //    OpeningQty is overloaded: on a genuine OpeningIn() row
                //    (ReferenceType OPENING) it's a one-time stock injection
                //    that has to be added in; on every ordinary Purchase/Issue/
                //    Adjustment row it's instead a "balance right before this
                //    transaction" audit snapshot that's already reflected by
                //    every prior row's own In/Out, so summing it there
                //    double-counted the balance. Only real OPENING rows'
                //    OpeningQty counts toward the total (matches
                //    Gen_StoreController's balance list/GetCurrentBalance).
                var balances = db.Gen_Store
                    .Where(s => s.Deleted == false)
                    .GroupBy(s => s.ItemId)
                    .Select(g => new
                    {
                        ItemId = g.Key,
                        CurrentBalance = g.Sum(x => (x.InQty ?? 0m) - (x.OutQty ?? 0m)
                                                   + (x.ReferenceType == "OPENING" ? (x.OpeningQty ?? 0m) : 0m))
                    })
                    .ToList()
                    .ToDictionary(x => x.ItemId, x => x.CurrentBalance);

                // 2) Pull all raw items with a non-null ReorderLevel.
                //    Items never stocked (no Gen_Store row) still count —
                //    their balance defaults to 0.
                var raw = (from r in db.Gen_RawItems
                           where r.Deleted == false
                                 && r.Active == true
                                 && r.ReorderLevel != null
                           join u in db.Gen_Units on r.UnitId equals u.Id into u1
                           from u in u1.DefaultIfEmpty()
                           select new
                           {
                               r.Id,
                               r.ItemName,
                               r.Type,
                               r.UnitId,
                               UnitCode = u != null ? u.UnitCode : null,
                               r.ReorderLevel,
                               r.MaxLevel,
                               r.ReorderQty
                           }).ToList();

                // 3) Weighted-average thread unit cost per backing raw item.
                //    Threads are the only items with a system-known rate; for
                //    fabric/raw items the buyer keys the rate manually.
                var threadRates = db.Gen_Threads
                    .Where(t => t.Deleted != true && t.RawItemId != null && t.UnitCost != null && t.UnitCost > 0)
                    .GroupBy(t => t.RawItemId.Value)
                    .Select(g => new
                    {
                        RawItemId = g.Key,
                        AvgCost   = g.Average(x => x.UnitCost.Value)
                    })
                    .ToList()
                    .ToDictionary(x => x.RawItemId, x => x.AvgCost);

                var suggestions = raw
                    .Select(r =>
                    {
                        decimal balance = balances.ContainsKey(r.Id) ? balances[r.Id] : 0m;
                        decimal shortfall = r.ReorderLevel.Value - balance;
                        if (shortfall <= 0) return null;

                        decimal suggested = r.ReorderQty.HasValue && r.ReorderQty.Value > 0
                            ? r.ReorderQty.Value
                            : shortfall;

                        decimal? rate = threadRates.ContainsKey(r.Id) ? (decimal?)threadRates[r.Id] : null;

                        return new
                        {
                            rawItemId       = r.Id,
                            rawItemName     = r.ItemName,
                            itemType        = r.Type,
                            unitId          = r.UnitId,
                            unitCode        = r.UnitCode ?? "",
                            currentBalance  = Math.Round(balance, 2),
                            reorderLevel    = r.ReorderLevel.Value,
                            reorderQty      = r.ReorderQty,
                            suggestedQty    = Math.Round(suggested, 2),
                            estimatedRate   = rate,
                            estimatedAmount = rate.HasValue ? Math.Round(rate.Value * suggested, 2) : (decimal?)null,
                            shortfall       = Math.Round(shortfall, 2)
                        };
                    })
                    .Where(x => x != null)
                    .OrderByDescending(x => x.shortfall)
                    .ToList();

                return JsonSuccess("OK", new
                {
                    count = suggestions.Count,
                    items = suggestions
                });
            }
            catch (Exception ex) { return JsonError("Failed to compute shortfall suggestions.", ex); }
        }

        // ====================================================================
        // CREATE / UPDATE
        // ====================================================================
        public class PrSaveDto
        {
            public int Id { get; set; }
            public string PrCode { get; set; }
            public int? BranchId { get; set; }
            public int? SupplierId { get; set; }
            public string SourceType { get; set; }       // AD_HOC / EMR_SHORTFALL / PLAN_SHORTFALL
            public int? SourceReferenceId { get; set; }
            public string Remarks { get; set; }
            public List<PrItemSaveDto> Items { get; set; }
        }
        public class PrItemSaveDto
        {
            public int Id { get; set; }
            public int RawItemId { get; set; }
            public int? UnitId { get; set; }
            public decimal Qty { get; set; }
            public decimal? EstimatedRate { get; set; }
            public string Remarks { get; set; }
        }

        [HttpPost, Route("create")]
        public ActionResult Create(PrSaveDto dto)
        {
            if (dto == null) return JsonError("Invalid request.");
            // Sprint-4: per-row validation so bad lines surface with their
            // row number instead of being silently dropped by InsertItems.
            string itemErr = ValidateItems(dto?.Items);
            if (itemErr != null) return JsonError(itemErr);

            try
            {
                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;
                int branchId = dto.BranchId ?? GetCurrentBranchId();
                string code = string.IsNullOrWhiteSpace(dto.PrCode) ? GenerateCode() : dto.PrCode.Trim();
                bool clientSuppliedCode = !string.IsNullOrWhiteSpace(dto.PrCode);
                string sourceType = string.IsNullOrWhiteSpace(dto.SourceType) ? "AD_HOC" : dto.SourceType.Trim().ToUpper();

                // Sprint-4: race-safe INSERT. If two simultaneous Creates pick
                // the same GenerateCode() output, the unique index on PrCode
                // catches the slower one and we retry with GenerateCode(skip+1).
                // 3 attempts is plenty for any realistic concurrency on this
                // endpoint. A client-supplied PrCode is honoured as-is.
                int newId = 0;
                int maxAttempts = clientSuppliedCode ? 1 : 3;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.PurchaseRequisition
       (PrCode, BranchId, RequestDate, SupplierId, SourceType, SourceReferenceId,
        Status, Remarks, RequestedBy,
        Active, Deleted, CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code,   @br,     @now,        @sup,       @srcT,      @srcId,
        'DRAFT', @rmk,    @uid,
        1,      0,        @uid,      @now,        @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                            new SqlParameter("@code",  code),
                            new SqlParameter("@br",    (object)branchId ?? DBNull.Value),
                            new SqlParameter("@now",   now),
                            new SqlParameter("@sup",   (object)dto.SupplierId ?? DBNull.Value),
                            new SqlParameter("@srcT",  sourceType),
                            new SqlParameter("@srcId", (object)dto.SourceReferenceId ?? DBNull.Value),
                            new SqlParameter("@rmk",   (object)(dto.Remarks ?? "") ?? DBNull.Value),
                            new SqlParameter("@uid",   userId)
                        ).First();
                        break;
                    }
                    catch (Exception insertEx) when (!clientSuppliedCode && IsPrCodeUniqueViolation(insertEx))
                    {
                        if (attempt + 1 >= maxAttempts) throw;
                        code = GenerateCode(attempt + 1);
                    }
                }

                InsertItems(newId, dto.Items, userId, now);

                LogAudit(newId, "Created", null, PrStatus.Draft,
                    "PR " + code + " (" + dto.Items.Count + " line(s))");

                return JsonSuccess("Draft created.", new { id = newId, prCode = code });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to create PR.", ex);
            }
        }

        [HttpPost, Route("update")]
        public ActionResult Update(PrSaveDto dto)
        {
            if (dto == null || dto.Id <= 0) return JsonError("Invalid request.");
            // Sprint-4: same per-row validation as Create.
            string itemErr = ValidateItems(dto?.Items);
            if (itemErr != null) return JsonError(itemErr);

            try
            {
                var hdr = LoadHeader(dto.Id);
                if (hdr == null) return JsonError("PR not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT PRs can be edited.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    SupplierId    = @sup,
       Remarks       = @rmk,
       UpdatedBy     = @uid,
       UpdatedDate   = @now,
       RecordTimeStamp = @now
WHERE  Id = @id",
                    new SqlParameter("@sup", (object)dto.SupplierId ?? DBNull.Value),
                    new SqlParameter("@rmk", (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  dto.Id));

                // Replace items wholesale
                db.Database.ExecuteSqlCommand(
                    "DELETE FROM dbo.PurchaseRequisitionItems WHERE PrId = @id",
                    new SqlParameter("@id", dto.Id));
                InsertItems(dto.Id, dto.Items, userId, now);

                LogAudit(dto.Id, "Updated", null, null,
                    "Items replaced (" + dto.Items.Count + " line(s))");
                return JsonSuccess("Updated.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update PR.", ex);
            }
        }

        // ====================================================================
        // SUBMIT (Draft → Submitted)
        // ====================================================================
        [HttpPost, Route("submit/{id:int}")]
        public ActionResult Submit(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PR not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT PRs can be submitted.");

                var items = LoadItems(id);
                if (items.Count == 0) return JsonError("PR has no items.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status='SUBMITTED', SubmittedDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                    new SqlParameter("@now", now),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@id",  id));

                LogAudit(id, "Submitted", PrStatus.Draft, PrStatus.Submitted, null);
                return JsonSuccess("Submitted for approval.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to submit PR.", ex);
            }
        }

        // ====================================================================
        // APPROVE (Submitted → Approved → auto-create PO → CONVERTED_TO_PO)
        // ====================================================================
        [HttpPost, Route("approve/{id:int}")]
        public ActionResult Approve(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PR not found.");
                if (!string.Equals(hdr.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only SUBMITTED PRs can be approved.");

                var items = LoadItems(id);
                if (items.Count == 0) return JsonError("PR has no items.");

                if (!hdr.SupplierId.HasValue || hdr.SupplierId.Value <= 0)
                    return JsonError("Supplier must be set on the PR before approval (so a PO can be generated).");

                if (items.Any(i => !i.EstimatedRate.HasValue || i.EstimatedRate.Value <= 0))
                    return JsonError("Every item needs an Estimated Rate before approval (becomes the PO rate).");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;
                int branchId = hdr.BranchId ?? GetCurrentBranchId();

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Sprint-4 TOCTOU lock: the initial status check at
                        // line 768 ran OUTSIDE this transaction. Two
                        // simultaneous Approves of the same PR could both
                        // pass that check and both create POs. Re-stamp the
                        // status INSIDE the tx with a guarded UPDATE — if a
                        // racing call beat us to it, row count = 0 and we
                        // bail out with a clear message instead of creating
                        // a duplicate PO.
                        int statusRowsUpdated = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status='APPROVED', ApprovedBy=@uid, ApprovedDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id AND Status='SUBMITTED'",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));
                        if (statusRowsUpdated == 0)
                        {
                            tx.Rollback();
                            return JsonError("This PR was already processed by another action — refresh and try again.");
                        }

                        // 1) Mark PR APPROVED first (kept briefly so audit trail is clean)
                        // NB: the actual UPDATE happened in the guarded statement
                        // above; the legacy block below is a no-op kept here to
                        // preserve the audit-trail wording. Skipped intentionally.
                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status='APPROVED', ApprovedBy=@uid, ApprovedDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id AND Status='APPROVED'",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        // 2) Generate the PO header
                        // Sprint-4: race-safe code with skip+retry on
                        // unique-violation. 3 attempts is plenty.
                        string poCode = GeneratePoCode();
                        decimal total = items.Sum(i => i.Qty * (i.EstimatedRate ?? 0m));

                        int poId = 0;
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            try
                            {
                                poId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.PurchaseOrder
       (PoCode, PrId, BranchId, SupplierId, OrderDate, Status, TotalAmount, ReceivedAmount,
        Remarks, Active, Deleted, CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code,  @prId,@br,      @sup,       @now,      'DRAFT', @total,    0,
        @rmk,   1,    0,        @uid,       @now,      @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                                    new SqlParameter("@code",  poCode),
                                    new SqlParameter("@prId",  id),
                                    new SqlParameter("@br",    (object)branchId ?? DBNull.Value),
                                    new SqlParameter("@sup",   hdr.SupplierId.Value),
                                    new SqlParameter("@now",   now),
                                    new SqlParameter("@total", total),
                                    new SqlParameter("@rmk",   (object)("Auto-generated from " + hdr.PrCode) ?? DBNull.Value),
                                    new SqlParameter("@uid",   userId)
                                ).First();
                                break;
                            }
                            catch (Exception insertEx) when (IsPrCodeUniqueViolation(insertEx))
                            {
                                // Reuse IsPrCodeUniqueViolation — SqlException 2601/2627
                                // is the same regardless of which constraint name.
                                if (attempt + 1 >= 3) throw;
                                poCode = GeneratePoCode(attempt + 1);
                            }
                        }

                        // 3) Copy lines to PO items
                        foreach (var i in items)
                        {
                            decimal rate = i.EstimatedRate ?? 0m;
                            decimal amount = i.Qty * rate;

                            db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseOrderItems
       (PoId, PrItemId, RawItemId, UnitId, OrderedQty, Rate, Amount, ReceivedQty,
        Remarks, Active, Deleted, CreatedBy, CreatedDate)
VALUES (@poId, @prItem, @raw,      @unit,  @qty,       @rate, @amt, 0,
        @rmk,  1,        0,         @uid,    @now)",
                                new SqlParameter("@poId",   poId),
                                new SqlParameter("@prItem", i.Id),
                                new SqlParameter("@raw",    i.RawItemId),
                                new SqlParameter("@unit",   (object)i.UnitId ?? DBNull.Value),
                                new SqlParameter("@qty",    i.Qty),
                                new SqlParameter("@rate",   rate),
                                new SqlParameter("@amt",    amount),
                                new SqlParameter("@rmk",    (object)(i.Remarks ?? "") ?? DBNull.Value),
                                new SqlParameter("@uid",    userId),
                                new SqlParameter("@now",    now));
                        }

                        // 4) Mark PR CONVERTED_TO_PO and stamp the FK
                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status='CONVERTED_TO_PO', ConvertedToPoId=@poId,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                            new SqlParameter("@poId", poId),
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        tx.Commit();

                        LogAudit(id, "Approved", PrStatus.Submitted, PrStatus.ConvertedToPo,
                            "PO " + poCode + " generated; " + items.Count + " line(s), total "
                          + Math.Round(total, 2));

                        return JsonSuccess("Approved and PO generated.", new
                        {
                            prId  = id,
                            poId,
                            poCode,
                            itemCount = items.Count,
                            totalAmount = Math.Round(total, 2)
                        });
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        // Sprint-2: unwrap EF wrapper to surface the real SQL error.
                        return JsonError("Approval/PO-generation failed: " + DescribeError(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to approve PR.", ex);
            }
        }

        // ====================================================================
        // REJECT
        // ====================================================================
        public class RejectDto { public string Reason { get; set; } }

        [HttpPost, Route("reject/{id:int}")]
        public ActionResult Reject(int id, RejectDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
                return JsonError("Rejection reason is required.");

            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PR not found.");
                if (!string.Equals(hdr.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only SUBMITTED PRs can be rejected.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status='REJECTED', ApprovedBy=@uid, ApprovedDate=@now,
       RejectionReason=@reason,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                    new SqlParameter("@uid",   userId),
                    new SqlParameter("@now",   now),
                    new SqlParameter("@reason", dto.Reason.Trim()),
                    new SqlParameter("@id",    id));

                LogAudit(id, "Rejected", PrStatus.Submitted, PrStatus.Rejected, dto.Reason.Trim());
                return JsonSuccess("Rejected.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to reject PR.", ex);
            }
        }

        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PR not found.");
                if (string.Equals(hdr.Status, "CONVERTED_TO_PO", StringComparison.OrdinalIgnoreCase))
                    return JsonError("This PR has been converted to a PO and cannot be deleted. Cancel the PO instead.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition SET Deleted=1, Active=0,
       UpdatedBy=@uid, UpdatedDate=@now WHERE Id=@id",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));

                LogAudit(id, "Deleted", hdr.Status, null, "Soft-deleted (Deleted=1, Active=0)");
                return JsonSuccess("Deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete PR.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-3  BULK ACTIONS + CLONE
        // --------------------------------------------------------------------
        //  Same per-row try/catch + failures-array shape as the JO / Plan /
        //  WO bulk endpoints so the Angular afterBulk helper is reusable.
        //  Each row reuses the single-row endpoint, which already wraps its
        //  own transaction for Approve. A bad row never aborts the batch.
        // ====================================================================

        public class PrBulkIdsDto      { public List<int> Ids { get; set; } }
        public class PrBulkRejectDto   { public List<int> Ids { get; set; } public string Reason { get; set; } }
        private class PrBulkRowResult  { public int Id { get; set; } public string Code { get; set; } public string Reason { get; set; } }

        [HttpPost, Route("bulk-approve")]
        public ActionResult BulkApprove(PrBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No PRs selected.");

            int successCount = 0;
            var failures = new List<PrBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Approve(id) as System.Web.Mvc.JsonResult;
                    var payload = result?.Data;
                    bool ok = false; string msg = null;
                    if (payload != null)
                    {
                        var t = payload.GetType();
                        var sp = t.GetProperty("success"); if (sp != null) ok = (bool)(sp.GetValue(payload) ?? false);
                        var mp = t.GetProperty("message"); if (mp != null) msg = (string)mp.GetValue(payload);
                    }
                    if (ok) successCount++;
                    else
                    {
                        var hdr = LoadHeader(id);
                        failures.Add(new PrBulkRowResult { Id = id, Code = hdr?.PrCode ?? "?", Reason = msg ?? "Approve failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PrBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " approved, " + failures.Count + " failed.",
                // camelCase projection so the frontend's afterBulk helper
                // can read failures[].id/code/reason.
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-reject")]
        public ActionResult BulkReject(PrBulkRejectDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No PRs selected.");
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return JsonError("Rejection reason is required.");

            int successCount = 0;
            var failures = new List<PrBulkRowResult>();
            var reasonDto = new RejectDto { Reason = dto.Reason };

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Reject(id, reasonDto) as System.Web.Mvc.JsonResult;
                    var payload = result?.Data;
                    bool ok = false; string msg = null;
                    if (payload != null)
                    {
                        var t = payload.GetType();
                        var sp = t.GetProperty("success"); if (sp != null) ok = (bool)(sp.GetValue(payload) ?? false);
                        var mp = t.GetProperty("message"); if (mp != null) msg = (string)mp.GetValue(payload);
                    }
                    if (ok) successCount++;
                    else
                    {
                        var hdr = LoadHeader(id);
                        failures.Add(new PrBulkRowResult { Id = id, Code = hdr?.PrCode ?? "?", Reason = msg ?? "Reject failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PrBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " rejected, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(PrBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No PRs selected.");

            int successCount = 0;
            var failures = new List<PrBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Delete(id) as System.Web.Mvc.JsonResult;
                    var payload = result?.Data;
                    bool ok = false; string msg = null;
                    if (payload != null)
                    {
                        var t = payload.GetType();
                        var sp = t.GetProperty("success"); if (sp != null) ok = (bool)(sp.GetValue(payload) ?? false);
                        var mp = t.GetProperty("message"); if (mp != null) msg = (string)mp.GetValue(payload);
                    }
                    if (ok) successCount++;
                    else
                    {
                        var hdr = LoadHeader(id);
                        failures.Add(new PrBulkRowResult { Id = id, Code = hdr?.PrCode ?? "?", Reason = msg ?? "Delete failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PrBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " deleted, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        // ====================================================================
        //  CLONE — deep-copy a PR into a fresh DRAFT
        // --------------------------------------------------------------------
        //  Same supplier / items as the source. Source-type defaults to
        //  AD_HOC so the cloned PR doesn't pretend to be a fresh shortfall
        //  or auto-PR — the buyer chose to copy it manually.
        // ====================================================================
        [HttpPost, Route("clone/{id:int}")]
        public ActionResult Clone(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var src = LoadHeader(id);
                    if (src == null) return JsonError("Source PR not found.");

                    var srcItems = LoadItems(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    int branchId = src.BranchId ?? GetCurrentBranchId();
                    string newCode = GenerateCode();

                    // Sprint-4: race-safe retry. The bare existence-check
                    // before insert is a TOCTOU race; skip+retry on the
                    // SaveChanges throw is what actually closes the gap.
                    int newId = 0;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.PurchaseRequisition
       (PrCode, BranchId, RequestDate, SupplierId, SourceType, SourceReferenceId,
        Status, Remarks, RequestedBy, Active, Deleted,
        CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code, @br,    @now,         @sup,       'AD_HOC',   NULL,
        'DRAFT', @rmk, @uid,         1,      0,
        @uid,   @now,        @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                                new SqlParameter("@code", newCode),
                                new SqlParameter("@br",   branchId),
                                new SqlParameter("@now",  now),
                                new SqlParameter("@sup",  (object)src.SupplierId ?? DBNull.Value),
                                new SqlParameter("@rmk",  "Cloned from " + src.PrCode
                                                        + (string.IsNullOrWhiteSpace(src.Remarks) ? "" : " — " + src.Remarks)),
                                new SqlParameter("@uid",  userId)
                            ).First();
                            break;
                        }
                        catch (Exception cloneEx) when (IsPrCodeUniqueViolation(cloneEx))
                        {
                            if (attempt + 1 >= 3) throw;
                            newCode = GenerateCode(attempt + 1);
                        }
                    }

                    // Reuse the existing InsertItems helper — it already
                    // handles validation, Qty × Rate amount calc, and
                    // skip-invalid-rows semantics.
                    var cloneItems = srcItems.Select(s => new PrItemSaveDto
                    {
                        RawItemId       = s.RawItemId,
                        UnitId          = s.UnitId,
                        Qty             = s.Qty,
                        EstimatedRate   = s.EstimatedRate,
                        Remarks         = s.Remarks
                    }).ToList();
                    InsertItems(newId, cloneItems, userId, now);

                    tx.Commit();

                    LogAudit(newId, "Created", null, PrStatus.Draft,
                        "Cloned from " + src.PrCode + " (" + cloneItems.Count + " line(s))");
                    LogAudit(id, "ClonedTo", null, newCode,
                        "Source for new PR " + newCode);

                    return JsonSuccess("Cloned.", new
                    {
                        id          = newId,
                        prCode      = newCode,
                        sourceId    = src.Id,
                        sourceCode  = src.PrCode,
                        itemsCloned = cloneItems.Count
                    });
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    return JsonError("Clone failed: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        // CHANGE SUPPLIER
        // --------------------------------------------------------------------
        // Auto-PR picks the mode supplier of the requested threads; that may
        // not be the supplier the buyer wants. This endpoint lets the buyer
        // change it after the fact:
        //   - PR.SupplierId is updated regardless of PR status (except
        //     REJECTED, where the PR is dead anyway).
        //   - If the PR has been converted to a PO and that PO hasn't
        //     received any goods yet (ReceivedAmount = 0, status DRAFT or
        //     SENT), the PO's SupplierId is updated too — same atomic tx.
        //   - If the PO has receipts (PARTIALLY_RECEIVED+), the change is
        //     refused with a clear error. Cancel the GRN first.
        // ====================================================================
        public class ChangeSupplierDto
        {
            public int? SupplierId { get; set; }
        }

        private class PoSupplierState
        {
            public int      Id { get; set; }
            public string   PoCode { get; set; }
            public string   Status { get; set; }
            public decimal  ReceivedAmount { get; set; }
        }

        [HttpPost, Route("change-supplier/{id:int}")]
        public ActionResult ChangeSupplier(int id, ChangeSupplierDto dto)
        {
            if (dto == null || !dto.SupplierId.HasValue || dto.SupplierId.Value <= 0)
                return JsonError("Supplier is required.");

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var hdr = LoadHeader(id);
                    if (hdr == null) return JsonError("PR not found.");
                    if (string.Equals(hdr.Status, "REJECTED", StringComparison.OrdinalIgnoreCase))
                        return JsonError("PR is REJECTED — supplier cannot be changed.");

                    // Verify the new supplier exists and isn't deleted
                    bool supExists = db.Gen_Suppliers.Any(s =>
                        s.Id == dto.SupplierId.Value && s.Deleted != true);
                    if (!supExists)
                        return JsonError("Selected supplier was not found.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    // If there's a linked PO, check it can take the change BEFORE
                    // we touch either row. Atomic-or-nothing UX.
                    int poRowsUpdated = 0;
                    string linkedPoCode = null;
                    if (hdr.ConvertedToPoId.HasValue && hdr.ConvertedToPoId.Value > 0)
                    {
                        var po = db.Database.SqlQuery<PoSupplierState>(@"
SELECT TOP 1 Id, PoCode, Status, ReceivedAmount
FROM   dbo.PurchaseOrder
WHERE  Id = @poId AND (Deleted = 0 OR Deleted IS NULL)",
                            new SqlParameter("@poId", hdr.ConvertedToPoId.Value)).FirstOrDefault();

                        if (po != null)
                        {
                            linkedPoCode = po.PoCode;

                            if (po.ReceivedAmount > 0
                                || string.Equals(po.Status, "PARTIALLY_RECEIVED", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(po.Status, "FULLY_RECEIVED",     StringComparison.OrdinalIgnoreCase)
                                || string.Equals(po.Status, "CLOSED",             StringComparison.OrdinalIgnoreCase))
                            {
                                return JsonError("Cannot change supplier — goods already received on " + po.PoCode + ". Cancel the GRN first.");
                            }

                            if (!string.Equals(po.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                            {
                                poRowsUpdated = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    SupplierId = @sup,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                                    new SqlParameter("@sup", dto.SupplierId.Value),
                                    new SqlParameter("@uid", userId),
                                    new SqlParameter("@now", now),
                                    new SqlParameter("@id",  po.Id));
                            }
                        }
                    }

                    // Update the PR header
                    db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    SupplierId = @sup,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                        new SqlParameter("@sup", dto.SupplierId.Value),
                        new SqlParameter("@uid", userId),
                        new SqlParameter("@now", now),
                        new SqlParameter("@id",  id));

                    tx.Commit();

                    LogAudit(id, "SupplierChanged",
                        hdr.SupplierId.HasValue ? hdr.SupplierId.Value.ToString() : null,
                        dto.SupplierId.Value.ToString(),
                        poRowsUpdated > 0 ? "Linked PO " + (linkedPoCode ?? "") + " supplier updated too" : null);

                    string msg = "PR supplier updated.";
                    if (poRowsUpdated > 0) msg += " Linked PO " + (linkedPoCode ?? "") + " supplier updated as well.";
                    return JsonSuccess(msg);
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    // Sprint-2: surface inner SQL exception via DescribeError.
                    return JsonError("Failed to change supplier: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        //  SPRINT-2  AUDIT TIMELINE + WORKFLOW STAGE + DescribeError
        // --------------------------------------------------------------------
        //  Append-only audit log keyed by PrId. Writes via raw SQL so no
        //  EDMX regeneration is needed. Every state-changing endpoint calls
        //  LogAudit() inside its happy path. Failures to write the log are
        //  swallowed — audit is never allowed to break a real save.
        //
        //  WORKFLOW STAGE: derives the current step from the PR's Status.
        //  Canonical stage list lives in one place so the stepper UI never
        //  has to map strings to positions.
        // ====================================================================

        private static readonly string[] _prWorkflowStages = new[] {
            PrStatus.Draft, PrStatus.Submitted, PrStatus.Approved,
            PrStatus.ConvertedToPo, PrStatus.Rejected
        };

        private int ComputePrWorkflowStageIndex(string status)
        {
            string s = (status ?? "").Trim().ToUpper();
            // Rejected lives off to the side — surface as a terminal stage.
            if (s == PrStatus.Rejected)      return 4;
            if (s == PrStatus.ConvertedToPo) return 3;
            if (s == PrStatus.Approved)      return 2;
            if (s == PrStatus.Submitted)     return 1;
            return 0; // Draft / unknown
        }

        private void LogAudit(int prId, string action, string oldValue, string newValue, string detail)
        {
            try
            {
                int userId = GetSafeCurrentUserId();
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseRequisitionAuditLog (PrId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@pid, @act, @uid, @now, @ov, @nv, @det)",
                    new SqlParameter("@pid", prId),
                    new SqlParameter("@act", (object)action ?? DBNull.Value),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", DateTime.Now),
                    new SqlParameter("@ov",  (object)oldValue ?? DBNull.Value),
                    new SqlParameter("@nv",  (object)newValue ?? DBNull.Value),
                    new SqlParameter("@det", (object)detail   ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "LogAudit failed for PR " + prId + ": " + ex.Message);
            }
        }

        private class PrAuditRow
        {
            public int      Id            { get; set; }
            public int      PrId          { get; set; }
            public string   Action        { get; set; }
            public int?     ChangedBy     { get; set; }
            public string   ChangedByName { get; set; }
            public DateTime ChangedDate   { get; set; }
            public string   OldValue      { get; set; }
            public string   NewValue      { get; set; }
            public string   Detail        { get; set; }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var rows = db.Database.SqlQuery<PrAuditRow>(@"
SELECT  a.Id, a.PrId, a.Action,
        a.ChangedBy, u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
FROM    dbo.PurchaseRequisitionAuditLog a
LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
WHERE   a.PrId = @pid
ORDER BY a.ChangedDate DESC, a.Id DESC",
                    new SqlParameter("@pid", id)).ToList();

                var data = rows.Select(r => new
                {
                    id              = r.Id,
                    prId            = r.PrId,
                    action          = r.Action,
                    changedBy       = r.ChangedBy,
                    changedByName   = r.ChangedByName,
                    changedDate     = r.ChangedDate,
                    changedDateText = r.ChangedDate.ToString("yyyy-MM-dd HH:mm"),
                    oldValue        = r.OldValue,
                    newValue        = r.NewValue,
                    detail          = r.Detail
                }).ToList();

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load audit log.", ex);
            }
        }

        /** Drill into an EF / SQL exception chain to extract the real
         *  root cause. EF6 wraps SqlException in DbUpdateException with
         *  the useless "An error occurred while updating the entries"
         *  outer text; the SqlException inside has the actual SQL error
         *  the operator needs. */
        private string DescribeError(Exception ex)
        {
            if (ex == null) return "Unknown error.";
            var ve = ex as System.Data.Entity.Validation.DbEntityValidationException;
            if (ve != null)
            {
                var errs = ve.EntityValidationErrors
                    .SelectMany(eve => eve.ValidationErrors)
                    .Select(err => err.PropertyName + ": " + err.ErrorMessage)
                    .ToList();
                if (errs.Count > 0) return string.Join("; ", errs);
            }
            var cur = ex;
            while (cur.InnerException != null) cur = cur.InnerException;
            return cur.Message;
        }

        // ====================================================================
        // INTERNAL HELPERS
        // ====================================================================
        // ====================================================================
        //  SPRINT-4  RACE-SAFE PR CODE GENERATION
        // --------------------------------------------------------------------
        //  Two concurrent Create / Clone calls could compute the same "next"
        //  PrCode and both make it past the existence check. The unique
        //  constraint on PrCode then raises a 2601/2627 on the slower insert
        //  and the user sees a 500. Same skip+retry pattern as the JO /
        //  Plan / WO modules: `skip` lets the caller try N codes ahead.
        // ====================================================================
        private string GenerateCode(int skip = 0)
        {
            string year = DateTime.Now.Year.ToString();
            string sql = @"
SELECT TOP 1 PrCode FROM dbo.PurchaseRequisition
WHERE PrCode LIKE 'PR-' + @year + '-%'
ORDER BY Id DESC";
            string last = db.Database.SqlQuery<string>(sql, new SqlParameter("@year", year)).FirstOrDefault();
            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                int parsed;
                if (parts.Length == 3 && int.TryParse(parts[2], out parsed)) next = parsed + 1;
            }
            return "PR-" + year + "-" + (next + skip).ToString("D4");
        }

        /** SQL Server uses error 2601 (duplicate key in unique index) and
         *  2627 (duplicate key in unique constraint) for the PrCode collision.
         *  Walk the inner exception chain since EF wraps SqlException inside
         *  DbUpdateException. */
        private bool IsPrCodeUniqueViolation(Exception ex)
        {
            var cur = ex;
            while (cur != null)
            {
                var se = cur as System.Data.SqlClient.SqlException;
                if (se != null && (se.Number == 2601 || se.Number == 2627)) return true;
                cur = cur.InnerException;
            }
            return false;
        }

        // Sprint-4: race-safe PoCode generation. Skip lets the caller try
        // N codes ahead in a loop after catching a unique-violation. Same
        // pattern PR / WO / Plan / JO use. The PO controller has its own
        // mirror of this helper so it can clone without a cross-controller
        // reference.
        private string GeneratePoCode(int skip = 0)
        {
            string year = DateTime.Now.Year.ToString();
            string sql = @"
SELECT TOP 1 PoCode FROM dbo.PurchaseOrder
WHERE PoCode LIKE 'PO-' + @year + '-%'
ORDER BY Id DESC";
            string last = db.Database.SqlQuery<string>(sql, new SqlParameter("@year", year)).FirstOrDefault();
            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                int parsed;
                if (parts.Length == 3 && int.TryParse(parts[2], out parsed)) next = parsed + 1;
            }
            return "PO-" + year + "-" + (next + skip).ToString("D4");
        }

        private PrRow LoadHeader(int id)
        {
            string sql = @"
SELECT  pr.Id, pr.PrCode, pr.BranchId,
        b.Name AS BranchName,
        pr.RequestDate,
        pr.SupplierId, s.SupplierName,
        pr.SourceType, pr.SourceReferenceId,
        pr.Status, pr.Remarks,
        pr.RequestedBy, ur.UserName AS RequestedByName,
        pr.SubmittedDate,
        pr.ApprovedBy,  ua.UserName AS ApprovedByName,
        pr.ApprovedDate, pr.RejectionReason,
        pr.ConvertedToPoId,
        po.PoCode AS ConvertedToPoCode,
        pr.Active, pr.CreatedDate, pr.UpdatedDate
FROM    dbo.PurchaseRequisition pr
LEFT JOIN dbo.Branches      b  ON b.Id  = pr.BranchId
LEFT JOIN dbo.Gen_Suppliers s  ON s.Id  = pr.SupplierId
LEFT JOIN dbo.Adm_Users     ur ON ur.Id = pr.RequestedBy
LEFT JOIN dbo.Adm_Users     ua ON ua.Id = pr.ApprovedBy
LEFT JOIN dbo.PurchaseOrder po ON po.Id = pr.ConvertedToPoId
WHERE   pr.Id = @id";
            return db.Database.SqlQuery<PrRow>(sql, new SqlParameter("@id", id)).FirstOrDefault();
        }

        private List<PrItemRow> LoadItems(int prId)
        {
            // Unit fallback: AutoPrCore historically inserted PR items with
            // a NULL UnitId. New inserts carry it, but legacy rows still
            // have NULL. ISNULL(i.UnitId, r.UnitId) lets the join surface
            // the raw item's master Unit so the dialog never shows blank.
            string sql = @"
SELECT  i.Id, i.PrId, i.RawItemId,
        r.ItemName AS RawItemName,
        ISNULL(i.UnitId, r.UnitId) AS UnitId,
        u.UnitCode AS UnitCode,
        i.Qty, i.EstimatedRate, i.EstimatedAmount, i.Remarks
FROM    dbo.PurchaseRequisitionItems i
LEFT JOIN dbo.Gen_RawItems r ON r.Id = i.RawItemId
LEFT JOIN dbo.Gen_Units    u ON u.Id = ISNULL(i.UnitId, r.UnitId)
WHERE   i.PrId = @id AND (i.Deleted = 0 OR i.Deleted IS NULL)
ORDER BY i.Id";
            var rows = db.Database.SqlQuery<PrItemRow>(sql, new SqlParameter("@id", prId)).ToList();

            // Rate fallback for legacy rows that landed with 0 (auto-PR
            // created before the FallbackRate chain existed in AutoPrCore).
            // Suggests the latest known cost so the buyer sees a reasonable
            // value; if they click Update, the suggested rate gets persisted
            // and the row is no longer "legacy" on next load. Display only,
            // not a backfill UPDATE — preserves the operator's ability to
            // override before saving.
            foreach (var row in rows)
            {
                if (row.EstimatedRate.HasValue && row.EstimatedRate.Value > 0m) continue;
                decimal suggested = SuggestRawItemRate(row.RawItemId);
                if (suggested <= 0m) continue;
                row.EstimatedRate   = suggested;
                row.EstimatedAmount = Math.Round(suggested * row.Qty, 2);
            }

            return rows;
        }

        /** Display-time rate suggestion for a raw item. Walks four
         *  historical sources in order of authority — most recent paid
         *  rate first, falling back to the denormalized standard if
         *  there's no transaction history at all. Mirrors the chain
         *  used by AutoPrCore.FallbackRate so freshly-created auto-PRs
         *  and legacy ones surface the same suggestion logic.
         *
         *  Order:
         *   1. PurchaseItems.Rate     — last actually-paid (GRN) rate
         *   2. PurchaseOrderItems.Rate — last PO line rate
         *   3. Gen_Stocks.Rate (IN)    — last internal stock receipt rate
         *   4. Gen_Stocks.LastKnownRate (any row) — denormalized standard
         *   5. 0 → buyer keys manually
         *
         *  Each source is wrapped in its own try so a missing table /
         *  column never breaks the whole chain — diagnostic noise stays
         *  out of the operator's path. */
        private decimal SuggestRawItemRate(int rawItemId)
        {
            decimal r;

            r = SafeScalar(@"
SELECT TOP 1 Rate FROM dbo.PurchaseItems
WHERE  ItemId = @id AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            r = SafeScalar(@"
SELECT TOP 1 Rate FROM dbo.PurchaseOrderItems
WHERE  RawItemId = @id AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            r = SafeScalar(@"
SELECT TOP 1 Rate FROM dbo.Gen_Stocks
WHERE  ItemId = @id AND TransactionType = 'IN' AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            // Standard-rate fallback — Gen_Stocks denormalizes the last
            // known rate per item so even an OUT row carries it.
            r = SafeScalar(@"
SELECT TOP 1 LastKnownRate FROM dbo.Gen_Stocks
WHERE  ItemId = @id AND ISNULL(LastKnownRate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            return r;
        }

        private decimal SafeScalar(string sql, int rawItemId)
        {
            try
            {
                var v = db.Database.SqlQuery<decimal?>(sql,
                    new SqlParameter("@id", rawItemId)).FirstOrDefault();
                return v.HasValue && v.Value > 0m ? v.Value : 0m;
            }
            catch { return 0m; }
        }

        // Sprint-4: per-row item validation. Returns null on success, a
        // human-readable summary on failure. Used by Create + Update so an
        // invalid row no longer silently disappears — the operator gets a
        // specific reason for each broken line.
        private string ValidateItems(List<PrItemSaveDto> items)
        {
            if (items == null || items.Count == 0) return "At least one item is required.";

            var problems = new List<string>();
            for (int idx = 0; idx < items.Count; idx++)
            {
                var i = items[idx];
                int n = idx + 1;
                if (i.RawItemId <= 0)               problems.Add("Row " + n + ": raw item is required.");
                else if (i.Qty <= 0)                problems.Add("Row " + n + ": qty must be greater than zero.");
                else if (i.EstimatedRate.HasValue && i.EstimatedRate.Value < 0)
                                                    problems.Add("Row " + n + ": estimated rate cannot be negative.");
            }
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        private void InsertItems(int prId, List<PrItemSaveDto> items, int userId, DateTime now)
        {
            foreach (var i in items)
            {
                if (i.RawItemId <= 0 || i.Qty <= 0) continue;
                decimal? amount = i.EstimatedRate.HasValue ? (decimal?)(i.Qty * i.EstimatedRate.Value) : null;

                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseRequisitionItems
       (PrId, RawItemId, UnitId, Qty, EstimatedRate, EstimatedAmount, Remarks,
        Active, Deleted, CreatedBy, CreatedDate)
VALUES (@prId, @raw,     @unit,  @qty, @rate,         @amt,           @rmk,
        1,    0,         @uid,   @now)",
                    new SqlParameter("@prId", prId),
                    new SqlParameter("@raw",  i.RawItemId),
                    new SqlParameter("@unit", (object)i.UnitId ?? DBNull.Value),
                    new SqlParameter("@qty",  i.Qty),
                    new SqlParameter("@rate", (object)i.EstimatedRate ?? DBNull.Value),
                    new SqlParameter("@amt",  (object)amount ?? DBNull.Value),
                    new SqlParameter("@rmk",  (object)(i.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",  userId),
                    new SqlParameter("@now",  now));
            }
        }
    }
}
