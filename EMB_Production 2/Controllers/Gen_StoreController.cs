using EMB_Production.Models;
using EMB_Production.Models.ViewModels;
using EMB_Production_2.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical Gen_Store reference-type values + the
    //  module slug each one deep-links to. Replaces the scattered
    //  string literals so a typo can't silently break a filter.
    //  `ModuleSlugFor()` returns the Angular route-prefix slug used to
    //  build a clickable Reference badge in the ledger; null when the
    //  ref-type has no source module (OPENING, OPENING_IN).
    // ─────────────────────────────────────────────────────────────────
    public static class StoreReferenceType
    {
        public const string OPENING               = "OPENING";
        public const string OPENING_IN            = "OPENING_IN";
        public const string PURCHASE              = "PURCHASE";
        public const string PURCHASE_RETURN       = "PURCHASE_RETURN";
        public const string MATERIAL_ISSUE        = "MATERIAL_ISSUE";
        public const string MATERIAL_ISSUE_REVERSE= "MATERIAL_ISSUE_REVERSE";
        public const string STOCK_ADJUSTMENT      = "STOCK_ADJUSTMENT";
        public const string BRANCH_TRANSFER       = "BRANCH_TRANSFER";
        public const string QC_INSPECTION         = "QC_INSPECTION";
        public const string DISPATCH              = "DISPATCH";
        public const string EMR_ISSUE             = "EMR_ISSUE";

        public static readonly string[] All = new[]
        {
            OPENING, OPENING_IN, PURCHASE, PURCHASE_RETURN,
            MATERIAL_ISSUE, MATERIAL_ISSUE_REVERSE,
            STOCK_ADJUSTMENT, BRANCH_TRANSFER,
            QC_INSPECTION, DISPATCH, EMR_ISSUE
        };

        /// <summary>Map ref-type → the Angular route slug used to
        /// build the clickable badge in the ledger list. Returns null
        /// when the ref-type is an internal-only marker (OPENING,
        /// OPENING_IN) with no separate source module.</summary>
        public static string ModuleSlugFor(string referenceType)
        {
            switch ((referenceType ?? "").Trim().ToUpper())
            {
                case PURCHASE:
                case PURCHASE_RETURN:        return "purchase";
                case MATERIAL_ISSUE:
                case MATERIAL_ISSUE_REVERSE: return "issue-materials";
                case STOCK_ADJUSTMENT:
                case BRANCH_TRANSFER:        return "store"; // in-module
                case QC_INSPECTION:          return "qc-inspection";
                case DISPATCH:               return "dispatch";
                case EMR_ISSUE:              return "extra-material-requests";
                default:                     return null;     // OPENING, OPENING_IN, …
            }
        }
    }

    [RoutePrefix("emb-products/gen-store")]
    public class Gen_StoreController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table that records every state-changing event on
        //  a Gen_Store row: CREATED (when written by Purchase, MI, etc),
        //  REVERSED (when a MI is reversed and a compensating IN is
        //  written), EDITED (rate/qty correction), SOFT_DELETED. Best-
        //  effort — wrapped in try/catch so a missing table never blocks
        //  the user's main action.
        // ─────────────────────────────────────────────────────────────
        private void LogStoreAudit(int storeId, string action,
                                   string oldValue = null,
                                   string newValue = null,
                                   string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_StoreAuditLog
       (StoreId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@sid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@sid",  storeId),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@uid",  GetSafeCurrentUserId()),
                    new SqlParameter("@when", DateTime.Now),
                    new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
            }
            catch
            {
                // Audit is observability, not a hard requirement.
            }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var entries = db.Database.SqlQuery<StoreAuditEntry>(@"
SELECT  a.Id,
        a.StoreId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.Gen_StoreAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.StoreId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id              = e.Id,
                    storeId         = e.StoreId,
                    action          = e.Action,
                    changedBy       = e.ChangedBy,
                    changedByName   = e.ChangedByName,
                    changedDate     = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue        = e.OldValue,
                    newValue        = e.NewValue,
                    detail          = e.Detail
                }).ToList();

                return JsonSuccess("OK", dto);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load audit log. " + DescribeStoreError(ex));
            }
        }

        private class StoreAuditEntry
        {
            public int Id { get; set; }
            public int StoreId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  ERROR UNWRAP
        //  EF6 + SqlClient wrap the real diagnostic inside two layers
        //  of generic exceptions. Surface the deepest message so the
        //  operator sees the SQL constraint that fired, not "An error
        //  occurred while saving the entity".
        // ─────────────────────────────────────────────────────────────
        private static string DescribeStoreError(Exception ex)
        {
            if (ex == null) return "Unknown error.";
            var dbValEx = ex as DbEntityValidationException;
            if (dbValEx != null)
            {
                var firstError = dbValEx.EntityValidationErrors
                    .SelectMany(e => e.ValidationErrors)
                    .FirstOrDefault();
                if (firstError != null)
                    return firstError.PropertyName + ": " + firstError.ErrorMessage;
            }
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-4  TOCTOU LOCK on (ItemId, BranchId)
        //  Holds a row-range UPDLOCK on the item+branch slice of the
        //  Gen_Store ledger until the surrounding transaction commits.
        //  Without this two concurrent posts can both read balance =
        //  100 and each write a row whose CurrentQty = 150 — losing
        //  100 units. Call this immediately BEFORE GetCurrentBalance
        //  inside any transaction that writes a new ledger row.
        // ─────────────────────────────────────────────────────────────
        private void LockBalanceSlice(int itemId, int? branchId)
        {
            // The HOLDLOCK + UPDLOCK pair gives a key-range lock so new
            // rows for the same (item, branch) by a concurrent caller
            // block until we commit. The query reads no rows back; it
            // exists purely to acquire the lock.
            if (branchId.HasValue && branchId.Value > 0)
            {
                db.Database.ExecuteSqlCommand(
                    "SELECT TOP 1 1 FROM dbo.Gen_Store WITH (UPDLOCK, HOLDLOCK) " +
                    "WHERE ItemId = @itemId AND BranchId = @branchId",
                    new SqlParameter("@itemId",   itemId),
                    new SqlParameter("@branchId", branchId.Value));
            }
            else
            {
                db.Database.ExecuteSqlCommand(
                    "SELECT TOP 1 1 FROM dbo.Gen_Store WITH (UPDLOCK, HOLDLOCK) " +
                    "WHERE ItemId = @itemId",
                    new SqlParameter("@itemId", itemId));
            }
        }
        [HttpGet]
        [Route("list")]
        public ActionResult List(string search = "",int? branchId = null,int? workOrderId = null,
        int page = 1,int pageSize = 20,string activeFilter = "",
        string fromDate = null, string toDate = null, string referenceType = null,
        string stockStatus = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 20;

                search = (search ?? "").Trim().ToLower();
                activeFilter = (activeFilter ?? "").Trim().ToLower();

                // Phase G.0 — parse date range filters defensively
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);

                // ── BASE query — applies to both the Item Balance Summary
                //    and the Transactions ledger below it. Only the dimensions
                //    that should always scope both views go here.
                var baseQ = db.Gen_Store
                    .Include(x => x.Gen_RawItems)
                    .Include(x => x.Branches)
                    .Where(x => x.Deleted == false);

                if (branchId.HasValue && branchId.Value > 0)
                    baseQ = baseQ.Where(x => x.BranchId == branchId.Value);

                if (workOrderId.HasValue && workOrderId.Value > 0)
                {
                    baseQ = baseQ.Where(x =>
                        x.ReferenceType == StoreReferenceType.MATERIAL_ISSUE &&
                        x.TransactionType == "OUT" &&
                        x.ReferenceId == workOrderId.Value);
                }

                if (activeFilter == "active")
                    baseQ = baseQ.Where(x => x.Active == true);
                else if (activeFilter == "inactive")
                    baseQ = baseQ.Where(x => x.Active == false);

                // ── BALANCE query — current stock state. Date range,
                //    Reference Type, and search filters MUST NOT change
                //    this; otherwise "Balance" no longer means "what's
                //    in stock right now" and Stock Value lies.
                var balanceRows = baseQ.ToList().Select(MapRow).ToList();

                // ── TRANSACTION query — narrowed by the user's date /
                //    reference / search filters. This drives ONLY the
                //    ledger table below the summary, not the summary.
                var q = baseQ;
                if (fromDt.HasValue) q = q.Where(x => x.TransactionDate >= fromDt.Value);
                if (toDt.HasValue)   q = q.Where(x => x.TransactionDate <= toDt.Value);

                if (!string.IsNullOrWhiteSpace(referenceType))
                {
                    // Sprint-4: whitelist guard so a typo in the URL
                    // doesn't silently render an empty ledger. Unknown
                    // ref types fail fast with a friendly message.
                    var rt = referenceType.Trim().ToUpper();
                    if (!StoreReferenceType.All.Contains(rt))
                        return JsonError("Unknown reference type: " + referenceType
                                       + ". Allowed: " + string.Join(", ", StoreReferenceType.All));
                    q = q.Where(x => x.ReferenceType.ToUpper() == rt);
                }

                var allRows = q.ToList().Select(MapRow).ToList();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    allRows = allRows.Where(x =>
                        (!string.IsNullOrWhiteSpace(x.ItemName) && x.ItemName.ToLower().Contains(search)) ||
                        (!string.IsNullOrWhiteSpace(x.TransactionType) && x.TransactionType.ToLower().Contains(search)) ||
                        (!string.IsNullOrWhiteSpace(x.ReferenceType) && x.ReferenceType.ToLower().Contains(search)) ||
                        (!string.IsNullOrWhiteSpace(x.ReferenceNo) && x.ReferenceNo.ToLower().Contains(search)) ||
                        (!string.IsNullOrWhiteSpace(x.Remarks) && x.Remarks.ToLower().Contains(search))
                    ).ToList();
                }

                // Item Balance Summary groups by item over the UNFILTERED
                // balanceRows so Balance always reflects true current stock.
                var groupedBalances = balanceRows
                    .Where(x => x.ItemId > 0)
                    .GroupBy(x => new
                    {
                        x.ItemId,
                        x.BranchId,
                        Unit = NormalizeUnit(x.Unit)
                    })
                    .Select(g =>
                    {
                        var latest = g
                            .OrderByDescending(x => x.TransactionDate)
                            .ThenByDescending(x => x.Id)
                            .FirstOrDefault();

                        decimal openingQty = workOrderId.HasValue ? 0m : g.Sum(x => x.OpeningQty);
                        decimal totalIn = workOrderId.HasValue ? 0m : g.Sum(x => x.InQty);
                        decimal totalOut = g.Sum(x => x.OutQty);
                        decimal currentBalance = workOrderId.HasValue
                            ? totalOut
                            : openingQty + totalIn - totalOut;

                        string status = workOrderId.HasValue
                            ? "Issued"
                            : currentBalance < 0 ? "Negative" :
                              currentBalance == 0 ? "Empty" :
                              "Available";

                        return new
                        {
                            ItemId = g.Key.ItemId,
                            ItemName = latest != null ? latest.ItemName : "-",
                            BranchId = g.Key.BranchId,
                            BranchName = latest != null ? latest.BranchName : "-",
                            OpeningQty = Math.Round(openingQty, 0),
                            TotalIn = Math.Round(totalIn, 0),
                            TotalOut = Math.Round(totalOut, 0),
                            CurrentBalance = Math.Round(currentBalance, 0),
                            Unit = g.Key.Unit,
                            Status = status
                        };
                    })
                    .ToList();

                // Phase G.1 - pull reorder thresholds per item in a single query
                var balanceItemIds = groupedBalances.Select(x => x.ItemId).Distinct().ToList();
                var reorderMap = db.Gen_RawItems
                    .Where(r => balanceItemIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.ReorderLevel, r.MaxLevel, r.ReorderQty })
                    .ToList()
                    .ToDictionary(r => r.Id, r => new { r.ReorderLevel, r.MaxLevel, r.ReorderQty });

                var itemBalances = groupedBalances
                    .Select(b =>
                    {
                        decimal? rLevel = null, mLevel = null, rQty = null;
                        if (reorderMap.ContainsKey(b.ItemId))
                        {
                            rLevel = reorderMap[b.ItemId].ReorderLevel;
                            mLevel = reorderMap[b.ItemId].MaxLevel;
                            rQty   = reorderMap[b.ItemId].ReorderQty;
                        }

                        string ss;
                        if (!rLevel.HasValue && !mLevel.HasValue)
                            ss = "NO_LEVELS";
                        else if (rLevel.HasValue && b.CurrentBalance < rLevel.Value)
                            ss = "BELOW_REORDER";
                        else if (mLevel.HasValue && b.CurrentBalance > mLevel.Value)
                            ss = "ABOVE_MAX";
                        else
                            ss = "OK";

                        return new
                        {
                            itemId = b.ItemId,
                            ItemId = b.ItemId,
                            itemName = b.ItemName,
                            ItemName = b.ItemName,
                            branchId = b.BranchId,
                            BranchId = b.BranchId,
                            branchName = b.BranchName,
                            BranchName = b.BranchName,
                            openingQty = b.OpeningQty,
                            OpeningQty = b.OpeningQty,
                            totalIn = b.TotalIn,
                            TotalIn = b.TotalIn,
                            totalOut = b.TotalOut,
                            TotalOut = b.TotalOut,
                            currentBalance = b.CurrentBalance,
                            CurrentBalance = b.CurrentBalance,
                            unit = b.Unit,
                            Unit = b.Unit,
                            status = b.Status,
                            Status = b.Status,
                            reorderLevel = rLevel,
                            ReorderLevel = rLevel,
                            maxLevel = mLevel,
                            MaxLevel = mLevel,
                            reorderQty = rQty,
                            ReorderQty = rQty,
                            stockStatus = ss,
                            StockStatus = ss
                        };
                    })
                    .OrderBy(x => x.itemName)
                    .ThenBy(x => x.branchName)
                    .ToList();

                // Phase G.1 - stock-status filter applied after enrichment
                if (!string.IsNullOrWhiteSpace(stockStatus))
                {
                    var ss = stockStatus.Trim().ToUpper();
                    itemBalances = itemBalances.Where(x => x.stockStatus == ss).ToList();
                }

                int totalItems = allRows.Count;

                var transactions = allRows
                    .OrderByDescending(x => x.TransactionDate)
                    .ThenByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                // Phase G.0 — KPI aggregates computed across the filtered set (not paginated).
                //
                // IMPORTANT: balanceRows is *unfiltered* by date / reference /
                // search (only branch + workOrder + activeFilter scope it), so
                // every IN / OUT / Opening / Balance number below is lifetime
                // for the chosen branch. Date filters only narrow the
                // transactions table below the KPI strip.
                //
                // The Opening line was previously missing from the chip row,
                // which made the OUT > IN case look like a bug. The math is
                // actually:  Net Balance = Opening + In - Out  per item.
                int negativeCount = itemBalances.Count(x => x.currentBalance < 0);
                int zeroCount     = itemBalances.Count(x => x.currentBalance == 0);
                int activeItemCount = itemBalances.Count(x => x.currentBalance > 0);
                decimal totalOpening = itemBalances.Sum(x => x.openingQty);
                decimal totalInQty  = itemBalances.Sum(x => x.totalIn);
                decimal totalOutQty = itemBalances.Sum(x => x.totalOut);
                decimal netBalance  = itemBalances.Sum(x => x.currentBalance);

                // Stock value = sum of (balance * lastRate) per (itemId, branchId).
                // Pull lastRate from the full ledger (balanceRows) so the value
                // reflects the most recent transaction, not whatever happens
                // to be in the user's date / reference filter.
                decimal stockValue = 0m;
                var perItemKeys = itemBalances.Select(x => new { x.itemId, x.branchId }).ToList();
                foreach (var k in perItemKeys)
                {
                    var latestRate = balanceRows
                        .Where(r => r.ItemId == k.itemId && r.BranchId == k.branchId)
                        .OrderByDescending(r => r.TransactionDate).ThenByDescending(r => r.Id)
                        .Select(r => (decimal?)r.Rate)
                        .FirstOrDefault() ?? 0m;
                    var balQty = itemBalances
                        .Where(x => x.itemId == k.itemId && x.branchId == k.branchId)
                        .Sum(x => x.currentBalance);
                    stockValue += balQty * latestRate;
                }

                // Phase G.1 - reorder/max KPI counts
                int belowReorderCount = itemBalances.Count(x => x.stockStatus == "BELOW_REORDER");
                int aboveMaxCount     = itemBalances.Count(x => x.stockStatus == "ABOVE_MAX");

                var kpis = new
                {
                    distinctItems = itemBalances.Count,
                    activeItemCount,
                    negativeCount,
                    zeroCount,
                    totalOpening = Math.Round(totalOpening, 2),
                    totalIn = Math.Round(totalInQty, 2),
                    totalOut = Math.Round(totalOutQty, 2),
                    netBalance = Math.Round(netBalance, 2),
                    stockValue = Math.Round(stockValue, 2),
                    belowReorderCount,
                    aboveMaxCount,
                    // Echo back the applied filters so the UI can label scope
                    // ("All time" vs "From 2026-06-01 to 2026-06-23"). KPIs
                    // themselves stay lifetime; this is purely a display hint.
                    appliedFromDate = fromDt.HasValue ? fromDt.Value.ToString("yyyy-MM-dd") : null,
                    appliedToDate   = toDt.HasValue   ? toDt.Value.ToString("yyyy-MM-dd")   : null,
                    appliedBranchId = branchId,
                    appliedWorkOrderId = workOrderId
                };

                var data = new
                {
                    kpis,
                    Kpis = kpis,
                    itemBalances,
                    ItemBalances = itemBalances,
                    transactions,
                    Transactions = transactions,
                    items = transactions,
                    Items = transactions,
                    page,
                    Page = page,
                    pageSize,
                    PageSize = pageSize,
                    totalItems,
                    TotalItems = totalItems,
                    totalPages,
                    TotalPages = totalPages
                };

                return Json(new
                {
                    success = true,
                    Success = true,
                    message = "Store list loaded successfully.",
                    Message = "Store list loaded successfully.",
                    data,
                    Data = data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load store list. " + ex.Message);
            }
        }

        // ============================================================
        // ITEM HISTORY — full ledger for ONE item, ignoring pagination.
        // Returns every IN/OUT row for the given itemId (and branchId
        // if provided), sorted oldest -> newest with a running balance.
        // ============================================================
        [HttpGet]
        [Route("item-history/{itemId:int}")]
        public ActionResult ItemHistory(int itemId, int? branchId = null)
        {
            try
            {
                if (itemId <= 0)
                    return JsonError("Item id is required.");

                var q = db.Gen_Store
                    .Include(x => x.Gen_RawItems)
                    .Include(x => x.Branches)
                    .Where(x => x.Deleted == false && x.ItemId == itemId);

                if (branchId.HasValue && branchId.Value > 0)
                    q = q.Where(x => x.BranchId == branchId.Value);

                var rows = q
                    .OrderBy(x => x.TransactionDate)
                    .ThenBy(x => x.Id)
                    .ToList();

                string itemName = rows.FirstOrDefault()?.Gen_RawItems?.ItemName ?? "-";
                string branchName = rows.FirstOrDefault()?.Branches?.Name ?? "-";

                decimal running = 0m;
                var ledger = rows.Select(r =>
                {
                    decimal inq = r.InQty ?? 0m;
                    decimal outq = r.OutQty ?? 0m;
                    running += inq - outq;

                    return new
                    {
                        id = r.Id,
                        transactionDate = r.TransactionDate,
                        dateText = r.TransactionDate.ToString("dd-MMM-yyyy"),
                        transactionType = r.TransactionType ?? "",
                        referenceType = r.ReferenceType ?? "",
                        referenceNo = r.ReferenceNo ?? "",
                        referenceId = r.ReferenceId,
                        inQty = inq,
                        outQty = outq,
                        balance = running,
                        unit = r.Unit ?? "",
                        rate = r.Rate ?? 0m,
                        amount = r.Amount ?? 0m,
                        remarks = r.Remarks ?? ""
                    };
                }).ToList();

                var data = new
                {
                    itemId,
                    itemName,
                    branchId = branchId ?? 0,
                    branchName,
                    totalIn = ledger.Sum(x => x.inQty),
                    totalOut = ledger.Sum(x => x.outQty),
                    balance = running,
                    rowCount = ledger.Count,
                    ledger
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load item history. " + ex.Message);
            }
        }

        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var entity = db.Gen_Store
                    .Include(x => x.Gen_RawItems)
                    .Include(x => x.Branches)
                    .FirstOrDefault(x => x.Id == id && x.Deleted == false);

                if (entity == null)
                    return JsonError("Store entry not found.");

                var model = new StoreDetailViewModel
                {
                    Id = entity.Id,
                    ItemId = entity.ItemId,
                    ItemName = entity.Gen_RawItems != null ? entity.Gen_RawItems.ItemName : "",
                    BranchId = entity.BranchId,
                    BranchName = entity.Branches != null ? entity.Branches.Name : "",
                    WarehouseId = entity.WarehouseId,
                    WarehouseName = "",
                    TransactionDate = entity.TransactionDate,
                    TransactionType = entity.TransactionType,
                    ReferenceType = entity.ReferenceType,
                    ReferenceId = entity.ReferenceId,
                    ReferenceNo = entity.ReferenceNo,
                    OpeningQty = entity.OpeningQty ?? 0,
                    InQty = entity.InQty ?? 0,
                    OutQty = entity.OutQty ?? 0,
                    CurrentQty = entity.CurrentQty ?? 0,
                    Unit = entity.Unit ?? "",
                    Rate = entity.Rate ?? 0,
                    Amount = entity.Amount ?? 0,
                    Remarks = entity.Remarks,
                    Active = entity.Active,
                    Deleted = entity.Deleted,
                    CreatedBy = entity.CreatedBy,
                    CreatedDate = entity.CreatedDate,
                    UpdatedBy = entity.UpdatedBy,
                    UpdatedDate = entity.UpdatedDate
                };

                return Json(new
                {
                    success = true,
                    message = "Detail loaded successfully.",
                    data = model
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load detail. " + ex.Message);
            }
        }
        
        [HttpGet]
        [Route("dashboard-summary")]
        public ActionResult DashboardSummary(int? branchId = null, string search = "")
        {
            try
            {
                var q = db.Gen_Store
                    .Include("Gen_RawItems")
                    .Include("Branches")
                    .Where(x => x.Deleted == false);

                if (branchId.HasValue && branchId.Value > 0)
                    q = q.Where(x => x.BranchId == branchId.Value);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    q = q.Where(x =>
                        (x.Gen_RawItems != null && x.Gen_RawItems.ItemName.Contains(search)) ||
                        (x.ReferenceType != null && x.ReferenceType.Contains(search)) ||
                        (x.ReferenceNo != null && x.ReferenceNo.Contains(search)) ||
                        (x.Remarks != null && x.Remarks.Contains(search))
                    );
                }

                var rows = q.ToList();

                decimal totalOpening = rows.Sum(x => x.OpeningQty ?? 0m);
                decimal totalIn = rows.Sum(x => x.InQty ?? 0m);
                decimal totalOut = rows.Sum(x => x.OutQty ?? 0m);

                decimal currentBalance = totalOpening + totalIn - totalOut;

                decimal totalStockValue = rows
                    .GroupBy(x => new { x.ItemId, x.BranchId })
                    .Sum(g =>
                    {
                        decimal balance =
                            g.Sum(x => x.OpeningQty ?? 0m) +
                            g.Sum(x => x.InQty ?? 0m) -
                            g.Sum(x => x.OutQty ?? 0m);

                        decimal rate = g
                            .OrderByDescending(x => x.TransactionDate)
                            .ThenByDescending(x => x.Id)
                            .Select(x => x.Rate ?? 0m)
                            .FirstOrDefault();

                        return balance * rate;
                    });

                var data = new StoreDashboardViewModel
                {
                    TotalOpeningQty = totalOpening,
                    TotalInQty = totalIn,
                    TotalOutQty = totalOut,
                    TotalCurrentQty = currentBalance,
                    TotalStockValue = totalStockValue,

                    DistinctItems = rows
                        .Where(x => x.ItemId > 0)
                        .Select(x => x.ItemId)
                        .Distinct()
                        .Count(),

                    DistinctBranches = rows
                        .Where(x => x.BranchId.HasValue)
                        .Select(x => x.BranchId.Value)
                        .Distinct()
                        .Count()
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Dashboard summary failed. " + ex.Message);
            }
        }
        
        [HttpPost]
        [Route("opening-in")]
        public ActionResult OpeningIn(StoreEntryDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid request.");

                if (dto.ItemId <= 0)
                    return JsonError("Raw item is required.");

                decimal qty = dto.OpeningQty ?? 0m;
                if (qty <= 0)
                    return JsonError("Opening quantity must be greater than zero.");

                var item = db.Gen_RawItems.FirstOrDefault(x =>
                    x.Id == dto.ItemId &&
                    x.Deleted == false);

                if (item == null)
                    return JsonError("Raw item not found.");

                string fixedUnit = GetFixedRawItemUnit(item);

                decimal previousBalance = GetCurrentBalance(
                    dto.ItemId,
                    dto.BranchId,
                    dto.WarehouseId,
                    fixedUnit
                );

                decimal newBalance = previousBalance + qty;

                DateTime now = DateTime.Now;

                var entity = new Gen_Store
                {
                    ItemId = dto.ItemId,
                    BranchId = dto.BranchId,
                    WarehouseId = dto.WarehouseId,

                    TransactionDate = dto.TransactionDate ?? now,
                    TransactionType = "IN",
                    ReferenceType = string.IsNullOrWhiteSpace(dto.ReferenceType) ? "OPENING" : dto.ReferenceType,
                    ReferenceId = dto.ReferenceId,
                    ReferenceNo = dto.ReferenceNo,

                    OpeningQty = qty,
                    InQty = 0m,
                    OutQty = 0m,
                    CurrentQty = newBalance,

                    Unit = fixedUnit,
                    Rate = dto.Rate ?? 0m,
                    Amount = qty * (dto.Rate ?? 0m),

                    Remarks = dto.Remarks,

                    Active = true,
                    Deleted = false,
                    CreatedBy = GetCurrentUserId(),
                    CreatedDate = now,
                    UpdatedBy = GetCurrentUserId(),
                    UpdatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Store.Add(entity);
                db.SaveChanges();

                return JsonSuccess("Opening stock saved successfully.", new
                {
                    id = entity.Id,
                    currentQty = entity.CurrentQty,
                    unit = entity.Unit
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to save opening stock. " + ex.Message);
            }
        }
       
        [HttpPost]
        [Route("purchase-in")]
        public ActionResult PurchaseIn(StoreEntryDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid request.");

                if (dto.ItemId <= 0)
                    return JsonError("Raw item is required.");

                decimal qty = dto.InQty ?? 0m;
                if (qty <= 0)
                    return JsonError("Receive quantity must be greater than zero.");

                var item = db.Gen_RawItems.FirstOrDefault(x =>
                    x.Id == dto.ItemId &&
                    x.Deleted == false);

                if (item == null)
                    return JsonError("Raw item not found.");

                string fixedUnit = GetFixedRawItemUnit(item);

                decimal previousBalance = GetCurrentBalance(
                    dto.ItemId,
                    dto.BranchId,
                    dto.WarehouseId,
                    fixedUnit
                );

                decimal newBalance = previousBalance + qty;

                DateTime now = DateTime.Now;

                var entity = new Gen_Store
                {
                    ItemId = dto.ItemId,
                    BranchId = dto.BranchId,
                    WarehouseId = dto.WarehouseId,

                    TransactionDate = dto.TransactionDate ?? now,
                    TransactionType = "IN",
                    ReferenceType = string.IsNullOrWhiteSpace(dto.ReferenceType) ? "PURCHASE" : dto.ReferenceType,
                    ReferenceId = dto.ReferenceId,
                    ReferenceNo = dto.ReferenceNo,

                    OpeningQty = 0m,
                    InQty = qty,
                    OutQty = 0m,
                    CurrentQty = newBalance,

                    Unit = fixedUnit,
                    Rate = dto.Rate ?? 0m,
                    Amount = qty * (dto.Rate ?? 0m),

                    Remarks = dto.Remarks,

                    Active = true,
                    Deleted = false,
                    CreatedBy = GetCurrentUserId(),
                    CreatedDate = now,
                    UpdatedBy = GetCurrentUserId(),
                    UpdatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Store.Add(entity);
                db.SaveChanges();

                return JsonSuccess("Purchase receive saved successfully.", new
                {
                    id = entity.Id,
                    currentQty = entity.CurrentQty,
                    unit = entity.Unit
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to save purchase receive. " + ex.Message);
            }
        }

        [HttpPost]
        [Route("issue-work-order")]
        public ActionResult IssueWorkOrder(MaterialIssueDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid request.");

                    if (dto.WorkOrderId <= 0)
                        return JsonError("Work Order is required.");

                    if (dto.BranchId <= 0)
                        return JsonError("Branch is required.");

                    if (dto.Items == null || !dto.Items.Any())
                        return JsonError("Issue items are required.");

                    var workOrder = db.WorkOrders.FirstOrDefault(x =>
                        x.Id == dto.WorkOrderId &&
                        x.Deleted == false);

                    if (workOrder == null)
                        return JsonError("Work Order not found.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    DateTime txDate = dto.TransactionDate ?? now;

                    // Auto-generate a unique IssueCode so each save operation is
                    // independently identifiable. The user's typed Reference No
                    // (if any) becomes a Remarks suffix — it's free-text, not the
                    // ledger key, so it can't make multiple issues indistinguishable.
                    string referenceNo = NextStoreIssueCode(now.Year);
                    string userTypedRef = string.IsNullOrWhiteSpace(dto.ReferenceNo)
                        ? null
                        : dto.ReferenceNo.Trim();

                    var validItems = dto.Items
                        .Where(x => x.ItemId > 0 && x.IssueQty > 0)
                        .ToList();

                    if (!validItems.Any())
                        return JsonError("Issue quantity must be greater than zero.");

                    var issueGroups = validItems
                        .GroupBy(x => new
                        {
                            x.ItemId,
                            WarehouseId = dto.WarehouseId
                        })
                        .Select(g => new
                        {
                            ItemId = g.Key.ItemId,
                            WarehouseId = g.Key.WarehouseId,
                            TotalIssueQty = g.Sum(x => x.IssueQty),
                            Lines = g.ToList()
                        })
                        .ToList();

                    foreach (var group in issueGroups)
                    {
                        var rawItem = db.Gen_RawItems.FirstOrDefault(x =>
                            x.Id == group.ItemId &&
                            x.Deleted == false);

                        if (rawItem == null)
                            return JsonError("Raw item not found.");

                        string fixedUnit = GetFixedRawItemUnit(rawItem);

                        decimal availableStock = GetCurrentBalance(
                            group.ItemId,
                            dto.BranchId,
                            group.WarehouseId,
                            fixedUnit
                        );

                        if (group.TotalIssueQty > availableStock)
                        {
                            tx.Rollback();
                            return JsonError(
                                "Insufficient stock for " + rawItem.ItemName +
                                ". Available: " + availableStock.ToString("N0") + " " + fixedUnit +
                                ", Required: " + group.TotalIssueQty.ToString("N0") + " " + fixedUnit
                            );
                        }
                    }

                    foreach (var group in issueGroups)
                    {
                        var rawItem = db.Gen_RawItems.FirstOrDefault(x =>
                            x.Id == group.ItemId &&
                            x.Deleted == false);

                        string fixedUnit = GetFixedRawItemUnit(rawItem);

                        decimal runningBalance = GetCurrentBalance(
                            group.ItemId,
                            dto.BranchId,
                            group.WarehouseId,
                            fixedUnit
                        );

                        foreach (var item in group.Lines)
                        {
                            decimal openingBeforeThisLine = runningBalance;
                            runningBalance -= item.IssueQty;

                            string lineRemarks =
                                (string.IsNullOrWhiteSpace(item.Remarks)
                                    ? ("Issued to " + item.Department + " against Work Order #" + dto.WorkOrderId)
                                    : item.Remarks)
                                + (string.IsNullOrWhiteSpace(userTypedRef) ? "" : (" | Ref: " + userTypedRef));

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = item.ItemId,
                                BranchId = dto.BranchId,
                                WarehouseId = dto.WarehouseId,

                                TransactionDate = txDate,
                                TransactionType = "OUT",
                                ReferenceType = "MATERIAL_ISSUE",
                                ReferenceId = dto.WorkOrderId,
                                ReferenceNo = referenceNo,

                                OpeningQty = openingBeforeThisLine,
                                InQty = 0m,
                                OutQty = item.IssueQty,
                                CurrentQty = runningBalance,

                                Unit = fixedUnit,

                                Rate = item.Rate,
                                Amount = item.IssueQty * item.Rate,

                                Remarks = lineRemarks,

                                Active = true,
                                Deleted = false,

                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            });
                        }
                    }

                    workOrder.Status = "Material Issued";
                    workOrder.UpdatedBy = userId;
                    workOrder.UpdatedDate = now;

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("Material issued against Work Order successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to issue material. " + ex.Message);
                }
            }
        }

        [HttpGet]
        [Route("work-order-materials/{workOrderId:int}")]
        public ActionResult WorkOrderMaterials(int workOrderId, int? branchId = null)
        {
            try
            {
                var woItems = db.WorkOrderItems
                    .Where(x =>
                        x.WorkOrderId == workOrderId &&
                        (x.Deleted == false || x.Deleted == null))
                    .ToList();

                if (!woItems.Any())
                    return JsonError("No work order items found.");

                int? effectiveBranchId = branchId;

                if (!effectiveBranchId.HasValue || effectiveBranchId.Value <= 0)
                {
                    effectiveBranchId = woItems
                        .Where(x => x.BranchId.HasValue && x.BranchId.Value > 0)
                        .Select(x => x.BranchId)
                        .FirstOrDefault();
                }

                var componentIds = woItems
                    .Where(x => x.CompId.HasValue && x.CompId.Value > 0)
                    .Select(x => x.CompId.Value)
                    .Distinct()
                    .ToList();

                if (!componentIds.Any())
                    return JsonError("No components found in selected work order.");

                var materials = db.Gen_ComponentMaterials
                    .Include("Gen_RawItems")
                    .Where(x =>
                        x.RawItemId > 0 &&
                        componentIds.Contains(x.CompId) &&
                        x.Deleted == false &&
                        x.Active == true)
                    .ToList();

                if (!materials.Any())
                    return JsonError("No raw material requirements found for this work order.");

                var rows = materials
                    .GroupBy(x => new
                    {
                        RawItemId = x.RawItemId,
                        ItemName = x.Gen_RawItems != null ? x.Gen_RawItems.ItemName : "",
                        ItemType = x.Gen_RawItems != null ? x.Gen_RawItems.Type : "RAW"
                    })
                    .Select(g =>
                    {
                        int itemId = g.Key.RawItemId;

                        decimal requiredQty = g.Sum(m =>
                        {
                            decimal relatedQty = woItems
                                .Where(w => w.CompId == m.CompId)
                                .Sum(w => (decimal?)w.Quantity) ?? 0m;

                            return m.QtyPerUnit * relatedQty;
                        });

                        var rawItem = g.FirstOrDefault() != null ? g.FirstOrDefault().Gen_RawItems : null;
                        string fixedUnit = GetFixedRawItemUnit(rawItem);

                        decimal availableQty = GetCurrentBalance(
                            itemId,
                            effectiveBranchId,
                            null,
                            fixedUnit
                        );

                        decimal alreadyIssuedQty = db.Gen_Store
                            .Where(s =>
                                s.ItemId == itemId &&
                                s.ReferenceType == "MATERIAL_ISSUE" &&
                                s.ReferenceId == workOrderId &&
                                s.Deleted == false)
                            .Sum(s => (decimal?)s.OutQty) ?? 0m;

                        decimal pendingQty = Math.Max(requiredQty - alreadyIssuedQty, 0m);

                        return new WorkOrderMaterialRequirementViewModel
                        {
                            ItemId = itemId,
                            ItemType = g.Key.ItemType,
                            ItemName = g.Key.ItemName,

                            RequiredQty = requiredQty,
                            AvailableQty = availableQty,
                            AlreadyIssuedQty = alreadyIssuedQty,
                            PendingQty = pendingQty,

                            IssueQty = pendingQty <= availableQty ? pendingQty : availableQty,

                            Department = GetDefaultDepartment(g.Key.ItemType),

                            Unit = fixedUnit,
                            Rate = GetLastRate(itemId, effectiveBranchId, fixedUnit),

                            Remarks = ""
                        };
                    })
                    .Where(x => x.PendingQty > 0)
                    .OrderBy(x => x.ItemType)
                    .ThenBy(x => x.ItemName)
                    .ToList();

                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work order materials. " + ex.Message);
            }
        }
       
        [HttpGet]
        [Route("pending-workorders")]
        public ActionResult PendingWorkOrders(int? branchId = null)
        {
            try
            {
                var workOrders = db.WorkOrders
                    .Where(x => x.Deleted != true)
                    .OrderByDescending(x => x.Id)
                    .ToList();

                var result = new List<object>();

                foreach (var wo in workOrders)
                {
                    var woItems = db.WorkOrderItems
                        .Where(x =>
                            x.WorkOrderId == wo.Id &&
                            x.Deleted != true)
                        .ToList();

                    if (!woItems.Any())
                        continue;

                    var componentIds = woItems
                        .Where(x => x.CompId.HasValue && x.CompId.Value > 0)
                        .Select(x => x.CompId.Value)
                        .Distinct()
                        .ToList();

                    if (!componentIds.Any())
                        continue;

                    var materials = db.Gen_ComponentMaterials
                        .Where(x =>
                            x.RawItemId > 0 &&
                            componentIds.Contains(x.CompId) &&
                            x.Active == true &&
                            x.Deleted == false)
                        .ToList();

                    if (!materials.Any())
                        continue;

                    bool hasPending = false;

                    var grouped = materials
                        .GroupBy(x => x.RawItemId)
                        .ToList();

                    foreach (var g in grouped)
                    {
                        int itemId = g.Key;

                        decimal requiredQty = 0m;

                        foreach (var m in g)
                        {
                            decimal woQty = woItems
                                .Where(w => w.CompId == m.CompId)
                                .Sum(w => (decimal?)w.Quantity) ?? 0m;

                            requiredQty += (m.QtyPerUnit * woQty);
                        }

                        decimal issuedQty = db.Gen_Store
                            .Where(s =>
                                s.ItemId == itemId &&
                                s.ReferenceType == "MATERIAL_ISSUE" &&
                                s.ReferenceId == wo.Id &&
                                s.Deleted != true)
                            .Sum(s => (decimal?)s.OutQty) ?? 0m;

                        decimal pendingQty = requiredQty - issuedQty;

                        if (pendingQty > 0)
                        {
                            hasPending = true;
                            break;
                        }
                    }

                    if (hasPending)
                    {
                        result.Add(new
                        {
                            id = wo.Id,
                            text = wo.WorkOrderCode
                        });
                    }
                }

                return JsonSuccess("OK", result);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load pending work orders. " + ex.Message);
            }
        }

        [HttpGet]
        [Route("balance")]
        public ActionResult Balance(int? branchId = null, string search = "", string type = "")
        {
            try
            {
                var q = db.Gen_Store
                    .Include("Gen_RawItems")
                    .Include("Branches")
                    .Where(x => x.Deleted == false);

                if (branchId.HasValue && branchId.Value > 0)
                    q = q.Where(x => x.BranchId == branchId.Value);

                if (!string.IsNullOrWhiteSpace(type))
                {
                    type = type.Trim().ToUpper();
                    q = q.Where(x => x.Gen_RawItems.Type.ToUpper() == type);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    q = q.Where(x => x.Gen_RawItems.ItemName.Contains(search));
                }

                var rows = q
                    .ToList()
                    .GroupBy(x => new
                    {
                        x.ItemId,
                        ItemName = x.Gen_RawItems != null ? x.Gen_RawItems.ItemName : "",
                        Type = x.Gen_RawItems != null ? x.Gen_RawItems.Type : "RAW",
                        x.BranchId,
                        BranchName = x.Branches != null ? x.Branches.Name : "",
                        Unit = NormalizeUnit(x.Unit)
                    })
                    .Select(g =>
                    {
                        decimal opening = g.Sum(x => x.OpeningQty ?? 0m);
                        decimal totalIn = g.Sum(x => x.InQty ?? 0m);
                        decimal totalOut = g.Sum(x => x.OutQty ?? 0m);
                        decimal balanceQty = opening + totalIn - totalOut;

                        return new
                        {
                            itemId = g.Key.ItemId,
                            itemName = g.Key.ItemName,
                            type = g.Key.Type,
                            branchId = g.Key.BranchId,
                            branchName = g.Key.BranchName,

                            openingQty = opening,
                            totalIn = totalIn,
                            totalOut = totalOut,
                            balanceQty = balanceQty,

                            unit = g.Key.Unit,
                            status = balanceQty < 0m ? "Negative" : balanceQty == 0m ? "Empty" : "Available",

                            lastRate = g
                                .OrderByDescending(x => x.TransactionDate)
                                .ThenByDescending(x => x.Id)
                                .Select(x => x.Rate ?? 0m)
                                .FirstOrDefault()
                        };
                    })
                    .OrderBy(x => x.type)
                    .ThenBy(x => x.itemName)
                    .ToList();

                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load balance. " + ex.Message);
            }
        }

        [HttpGet]
        [Route("issued-materials-by-workorder/{workOrderId:int}")]
        public ActionResult IssuedMaterialsByWorkOrder(int workOrderId)
        {
            try
            {
                if (workOrderId <= 0)
                    return JsonError("Work Order is required.");

                var rows = db.Gen_Store
                    .Include("Gen_RawItems")
                    .Include("Branches")
                    .Where(x =>
                        x.ReferenceType == "MATERIAL_ISSUE" &&
                        x.ReferenceId == workOrderId &&
                        x.TransactionType == "OUT" &&
                        x.Deleted == false)
                    .ToList()
                    .GroupBy(x => new
                    {
                        x.ItemId,
                        ItemName = x.Gen_RawItems != null ? x.Gen_RawItems.ItemName : "-",
                        Type = x.Gen_RawItems != null ? x.Gen_RawItems.Type : "RAW",
                        Unit = x.Unit,
                        x.BranchId,
                        BranchName = x.Branches != null ? x.Branches.Name : "-"
                    })
                    .Select(g => new
                    {
                        itemId = g.Key.ItemId,
                        itemName = g.Key.ItemName,
                        itemType = g.Key.Type,
                        branchId = g.Key.BranchId,
                        branchName = g.Key.BranchName,
                        issuedQty = g.Sum(x => x.OutQty ?? 0m),
                        unit = g.Key.Unit,
                        amount = g.Sum(x => x.Amount ?? 0m),
                        lastIssueDate = g.Max(x => x.TransactionDate)
                    })
                    .OrderBy(x => x.itemType)
                    .ThenBy(x => x.itemName)
                    .ToList();

                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load issued materials. " + ex.Message);
            }
        }

        // =========================================================
        // HELPERS
        // =========================================================
        private StoreRowViewModel MapRow(Gen_Store x)
        {
            decimal openingQty = x.OpeningQty ?? 0m;
            decimal inQty = x.InQty ?? 0m;
            decimal outQty = x.OutQty ?? 0m;

            decimal currentQty = x.CurrentQty ?? (openingQty + inQty - outQty);

            // Sprint-1: derived fields used by the frontend to render
            // clickable Reference badges + the "received recently"
            // marker on the ledger table.
            string moduleSlug = StoreReferenceType.ModuleSlugFor(x.ReferenceType);
            bool isRecent = x.CreatedDate.HasValue
                            && (DateTime.Now - x.CreatedDate.Value).TotalHours < 24;

            return new StoreRowViewModel
            {
                Id = x.Id,

                ItemId = x.ItemId,
                ItemName = x.Gen_RawItems != null
                    ? x.Gen_RawItems.ItemName
                    : "-",

                BranchId = x.BranchId ?? 0,
                BranchName = x.Branches != null
                    ? x.Branches.Name
                    : "-",

                WarehouseId = x.WarehouseId,
                WarehouseName = "",

                TransactionDate = x.TransactionDate,

                TransactionType = x.TransactionType ?? "",
                ReferenceType = x.ReferenceType ?? "",
                ReferenceId = x.ReferenceId,
                ReferenceNo = x.ReferenceNo ?? "",

                OpeningQty = openingQty,
                InQty = inQty,
                OutQty = outQty,
                CurrentQty = currentQty,

                Unit = x.Gen_RawItems != null
                    ? GetFixedRawItemUnit(x.Gen_RawItems)
                    : NormalizeUnit(x.Unit),

                Rate = x.Rate ?? 0m,
                Amount = x.Amount ?? 0m,

                Remarks = x.Remarks ?? "",

                Active = x.Active,
                Deleted = x.Deleted,

                CreatedDate = x.CreatedDate,
                UpdatedDate = x.UpdatedDate,

                // Sprint-1: deep-link + recency for the ledger list.
                ReferenceModule = moduleSlug,
                IsRecent = isRecent
            };
        }

        private decimal GetRawStoreBalance(int itemId, int branchId)
        {
            return db.Gen_Store
                .Where(x =>
                    x.ItemId == itemId &&
                    x.BranchId == branchId &&
                    x.Deleted == false &&
                    x.Active == true)
                .Select(x => (decimal?)(
                    (x.OpeningQty ?? 0) +
                    (x.InQty ?? 0) -
                    (x.OutQty ?? 0)
                ))
                .Sum() ?? 0m;
        }

        // Generates a unique IssueCode for Material Issue Out operations
        // initiated from the Store page. Format: MI-YYYY-NNNN.
        private string NextStoreIssueCode(int year)
        {
            string prefix = "MI-" + year + "-";
            int next = 1;

            var lastRef = db.Gen_Store
                .Where(s => s.ReferenceType == "MATERIAL_ISSUE"
                            && s.ReferenceNo != null
                            && s.ReferenceNo.StartsWith(prefix))
                .OrderByDescending(s => s.Id)
                .Select(s => s.ReferenceNo)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(lastRef))
            {
                var tail = lastRef.Substring(prefix.Length);
                if (int.TryParse(tail, out int parsed))
                    next = parsed + 1;
            }

            return prefix + next.ToString("D4");
        }

        private decimal GetCurrentBalance(int itemId, int? branchId, int? warehouseId, string unit = "")
        {
            var q = db.Gen_Store.Where(x =>
                x.ItemId == itemId &&
                x.Deleted == false &&
                x.Active == true);

            if (branchId.HasValue && branchId.Value > 0)
                q = q.Where(x => x.BranchId == branchId.Value);

            if (warehouseId.HasValue && warehouseId.Value > 0)
                q = q.Where(x => x.WarehouseId == warehouseId.Value);

            return q.Sum(x =>
                (decimal?)(
                    (x.OpeningQty ?? 0m) +
                    (x.InQty ?? 0m) -
                    (x.OutQty ?? 0m)
                )
            ) ?? 0m;
        }

        private string NormalizeUnit(string unit)
        {
            return string.IsNullOrWhiteSpace(unit)
                ? "PCS"
                : unit.Trim().ToUpper();
        }

        private string GetFixedRawItemUnit(Gen_RawItems rawItem)
        {
            var unit = GetRawItemUnit(rawItem);
            return NormalizeUnit(unit);
        }
              
        private string GetRawItemUnit(Gen_RawItems rawItem)
        {
            if (rawItem == null)
                return "";

            var unit = db.Gen_Units.FirstOrDefault(u => u.Id == rawItem.UnitId);
            return unit != null ? unit.UnitCode : "";
        }

        private string GetDefaultDepartment(string itemType)
        {
            itemType = (itemType ?? "").Trim().ToUpper();

            if (itemType == "FABRIC")
                return "CUTTING";

            if (itemType == "THREAD")
                return "EMBROIDERY";

            if (itemType == "ACCESSORY")
                return "STITCHING";

            if (itemType == "PACKING")
                return "PACKING";

            return "GENERAL";
        }

        private decimal GetLastRate(int itemId, int? branchId, string unit = "")
        {
            var q = db.Gen_Store.Where(x =>
                x.ItemId == itemId &&
                x.Deleted == false &&
                x.Active == true);

            if (branchId.HasValue && branchId.Value > 0)
                q = q.Where(x => x.BranchId == branchId.Value);

            return q
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.Id)
                .Select(x => (decimal?)x.Rate)
                .FirstOrDefault() ?? 0m;
        }

        // ============================================================
        // Phase G.1 - Set reorder thresholds for a raw item
        // ============================================================
        public class SetLevelsDto
        {
            public decimal? ReorderLevel { get; set; }
            public decimal? MaxLevel { get; set; }
            public decimal? ReorderQty { get; set; }
        }

        [HttpPost]
        [Route("set-levels/{itemId:int}")]
        public ActionResult SetLevels(int itemId, SetLevelsDto dto)
        {
            if (itemId <= 0) return JsonError("Item id is required.");
            if (dto == null) return JsonError("Invalid request.");

            var item = db.Gen_RawItems.FirstOrDefault(x => x.Id == itemId && x.Deleted == false);
            if (item == null) return JsonError("Raw item not found.");

            if (dto.ReorderLevel.HasValue && dto.ReorderLevel.Value < 0)
                return JsonError("Reorder level cannot be negative.");
            if (dto.MaxLevel.HasValue && dto.MaxLevel.Value < 0)
                return JsonError("Max level cannot be negative.");
            if (dto.ReorderQty.HasValue && dto.ReorderQty.Value < 0)
                return JsonError("Reorder qty cannot be negative.");
            if (dto.ReorderLevel.HasValue && dto.MaxLevel.HasValue
                && dto.MaxLevel.Value < dto.ReorderLevel.Value)
                return JsonError("Max level cannot be below the reorder level.");

            try
            {
                item.ReorderLevel = dto.ReorderLevel;
                item.MaxLevel = dto.MaxLevel;
                item.ReorderQty = dto.ReorderQty;
                db.SaveChanges();

                return JsonSuccess("Levels saved.", new
                {
                    itemId = item.Id,
                    reorderLevel = item.ReorderLevel,
                    maxLevel = item.MaxLevel,
                    reorderQty = item.ReorderQty
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to save levels. " + ex.Message);
            }
        }

        // ============================================================
        // Phase G.3 - Stock Adjustment
        // Single-row IN or OUT with a reason code (DAMAGE/LOSS/GAIN/
        // RECOUNT/OPENING_CORRECTION/OTHER). Writes one Gen_Store row
        // with ReferenceType="STOCK_ADJUSTMENT".
        // ============================================================
        public class StockAdjustmentDto
        {
            public int ItemId { get; set; }
            public int BranchId { get; set; }
            public string Sign { get; set; }        // "IN" or "OUT"
            public decimal Qty { get; set; }
            public string Reason { get; set; }      // DAMAGE/LOSS/GAIN/RECOUNT/OPENING_CORRECTION/OTHER
            public string TransactionDate { get; set; }
            public decimal? Rate { get; set; }
            public string Remarks { get; set; }
        }

        private static readonly string[] AllowedAdjustmentReasons = new[]
        {
            "DAMAGE", "LOSS", "GAIN", "RECOUNT", "OPENING_CORRECTION", "OTHER"
        };

        [HttpPost]
        [Route("adjustment")]
        public ActionResult Adjustment(StockAdjustmentDto dto)
        {
            if (dto == null) return JsonError("Invalid request.");
            if (dto.ItemId <= 0) return JsonError("Item is required.");
            if (dto.BranchId <= 0) return JsonError("Branch is required.");

            string sign = (dto.Sign ?? "").Trim().ToUpper();
            if (sign != "IN" && sign != "OUT") return JsonError("Sign must be IN or OUT.");

            if (dto.Qty <= 0) return JsonError("Quantity must be greater than zero.");

            string reason = (dto.Reason ?? "").Trim().ToUpper();
            if (string.IsNullOrEmpty(reason)) reason = "OTHER";
            if (!AllowedAdjustmentReasons.Contains(reason))
                return JsonError("Unknown reason code.");

            DateTime trxDate;
            if (string.IsNullOrWhiteSpace(dto.TransactionDate) || !DateTime.TryParse(dto.TransactionDate, out trxDate))
                trxDate = DateTime.Now.Date;

            var item = db.Gen_RawItems.FirstOrDefault(x => x.Id == dto.ItemId && x.Deleted == false);
            if (item == null) return JsonError("Raw item not found.");

            var branch = db.Branches.FirstOrDefault(x => x.Id == dto.BranchId);
            if (branch == null) return JsonError("Branch not found.");

            string fixedUnit = GetFixedRawItemUnit(item);

            using (var tx = db.Database.BeginTransaction())
            {
            try
            {
                // Sprint-4: serialise concurrent posts on the same item-
                // branch slice. Without this, two adjustments running in
                // parallel can both read balance = 100 and each post a
                // row with CurrentQty = 150, losing 100 units of stock.
                LockBalanceSlice(dto.ItemId, dto.BranchId);

                decimal previousBalance = GetCurrentBalance(dto.ItemId, dto.BranchId, null, fixedUnit);

                if (sign == "OUT" && previousBalance < dto.Qty)
                {
                    tx.Rollback();
                    return JsonError(
                        "Insufficient balance at " + branch.Name + ". Available: " +
                        Math.Round(previousBalance, 2) + " " + fixedUnit + ", requested: " +
                        Math.Round(dto.Qty, 2) + " " + fixedUnit + ".");
                }

                decimal rate = dto.Rate ?? GetLastRate(dto.ItemId, dto.BranchId, fixedUnit);

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Generate ADJ-YYYY-NNNN sequentially within the current year
                int year = trxDate.Year;
                string yearPrefix = "ADJ-" + year + "-";
                int lastNum = db.Gen_Store
                    .Where(x => x.ReferenceType == "STOCK_ADJUSTMENT"
                             && x.ReferenceNo != null
                             && x.ReferenceNo.StartsWith(yearPrefix))
                    .ToList()
                    .Select(x =>
                    {
                        int n;
                        return int.TryParse(x.ReferenceNo.Substring(yearPrefix.Length), out n) ? n : 0;
                    })
                    .DefaultIfEmpty(0)
                    .Max();
                string adjustmentNo = yearPrefix + (lastNum + 1).ToString("D4");

                string finalRemarks = "(" + reason + ")"
                    + (string.IsNullOrWhiteSpace(dto.Remarks) ? "" : " " + dto.Remarks.Trim());

                decimal inQty  = sign == "IN"  ? dto.Qty : 0m;
                decimal outQty = sign == "OUT" ? dto.Qty : 0m;
                decimal newBalance = previousBalance + inQty - outQty;

                var row = new Gen_Store
                {
                    ItemId = dto.ItemId,
                    BranchId = dto.BranchId,
                    WarehouseId = null,
                    TransactionDate = trxDate,

                    TransactionType = sign,
                    ReferenceType = "STOCK_ADJUSTMENT",
                    ReferenceId = 0,
                    ReferenceNo = adjustmentNo,

                    OpeningQty = previousBalance,
                    InQty = inQty,
                    OutQty = outQty,
                    CurrentQty = newBalance,

                    Unit = fixedUnit,
                    Rate = rate,
                    Amount = dto.Qty * rate,

                    Remarks = finalRemarks,

                    Active = true,
                    Deleted = false,
                    CreatedBy = userId,
                    CreatedDate = now,
                    UpdatedBy = userId,
                    UpdatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Store.Add(row);
                db.SaveChanges();

                // Self-reference so audit/joins can navigate
                row.ReferenceId = row.Id;
                db.SaveChanges();

                tx.Commit();

                LogStoreAudit(row.Id, "CREATED",
                    oldValue: null,
                    newValue: sign + " " + dto.Qty + " " + fixedUnit,
                    detail: "STOCK_ADJUSTMENT " + adjustmentNo + " (" + reason + ")");

                return JsonSuccess("Stock adjustment posted.", new
                {
                    adjustmentNo,
                    id = row.Id,
                    sign,
                    reason,
                    qty = dto.Qty,
                    unit = fixedUnit,
                    previousBalance,
                    newBalance,
                    branch = branch.Name
                });
            }
            catch (Exception ex)
            {
                tx.Rollback();
                return JsonError("Adjustment failed. " + DescribeStoreError(ex));
            }
            }
        }

        // ============================================================
        // Phase G.2 - Branch Transfer
        // Moves stock between two branches. Writes paired Gen_Store rows:
        //   - OUT at source branch
        //   - IN  at destination branch
        // Both rows share ReferenceType="BRANCH_TRANSFER" and a single
        // TRF-YYYY-NNNN ReferenceNo. The IN row's ReferenceId points to
        // the OUT row's Id so the two can be navigated as a pair.
        // ============================================================
        public class BranchTransferDto
        {
            public int ItemId { get; set; }
            public int FromBranchId { get; set; }
            public int ToBranchId { get; set; }
            public decimal Qty { get; set; }
            public string TransactionDate { get; set; }
            public decimal? Rate { get; set; }
            public string Remarks { get; set; }
        }

        [HttpPost]
        [Route("transfer")]
        public ActionResult Transfer(BranchTransferDto dto)
        {
            if (dto == null)
                return JsonError("Invalid request.");

            if (dto.ItemId <= 0)
                return JsonError("Item is required.");

            if (dto.FromBranchId <= 0 || dto.ToBranchId <= 0)
                return JsonError("Source and destination branch are required.");

            if (dto.FromBranchId == dto.ToBranchId)
                return JsonError("Source and destination branch must be different.");

            if (dto.Qty <= 0)
                return JsonError("Transfer quantity must be greater than zero.");

            DateTime trxDate;
            if (string.IsNullOrWhiteSpace(dto.TransactionDate) || !DateTime.TryParse(dto.TransactionDate, out trxDate))
                trxDate = DateTime.Now.Date;

            var item = db.Gen_RawItems.FirstOrDefault(x => x.Id == dto.ItemId && x.Deleted == false);
            if (item == null)
                return JsonError("Raw item not found.");

            var fromBranch = db.Branches.FirstOrDefault(x => x.Id == dto.FromBranchId);
            var toBranch   = db.Branches.FirstOrDefault(x => x.Id == dto.ToBranchId);
            if (fromBranch == null) return JsonError("Source branch not found.");
            if (toBranch == null)   return JsonError("Destination branch not found.");

            string fixedUnit = GetFixedRawItemUnit(item);

            decimal sourceBalance = GetCurrentBalance(dto.ItemId, dto.FromBranchId, null, fixedUnit);
            if (sourceBalance < dto.Qty)
                return JsonError(
                    "Insufficient balance at " + fromBranch.Name + ". Available: " +
                    Math.Round(sourceBalance, 2) + " " + fixedUnit + ", requested: " +
                    Math.Round(dto.Qty, 2) + " " + fixedUnit + ".");

            decimal rate = dto.Rate ?? GetLastRate(dto.ItemId, dto.FromBranchId, fixedUnit);

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    // Generate TRF-YYYY-NNNN sequentially within the current year
                    int year = trxDate.Year;
                    string yearPrefix = "TRF-" + year + "-";
                    int lastNum = db.Gen_Store
                        .Where(x => x.ReferenceType == "BRANCH_TRANSFER"
                                 && x.ReferenceNo != null
                                 && x.ReferenceNo.StartsWith(yearPrefix))
                        .ToList()
                        .Select(x =>
                        {
                            int n;
                            return int.TryParse(x.ReferenceNo.Substring(yearPrefix.Length), out n) ? n : 0;
                        })
                        .DefaultIfEmpty(0)
                        .Max();
                    string transferNo = yearPrefix + (lastNum + 1).ToString("D4");

                    decimal destBalance = GetCurrentBalance(dto.ItemId, dto.ToBranchId, null, fixedUnit);

                    var outRow = new Gen_Store
                    {
                        ItemId = dto.ItemId,
                        BranchId = dto.FromBranchId,
                        WarehouseId = null,
                        TransactionDate = trxDate,

                        TransactionType = "OUT",
                        ReferenceType = "BRANCH_TRANSFER",
                        ReferenceId = 0,
                        ReferenceNo = transferNo,

                        OpeningQty = sourceBalance,
                        InQty = 0m,
                        OutQty = dto.Qty,
                        CurrentQty = sourceBalance - dto.Qty,

                        Unit = fixedUnit,
                        Rate = rate,
                        Amount = dto.Qty * rate,

                        Remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                                  ? ("Transfer to " + toBranch.Name)
                                  : dto.Remarks,

                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,
                        RecordTimeStamp = now
                    };

                    db.Gen_Store.Add(outRow);
                    db.SaveChanges();

                    // Pair the OUT row to itself so both rows can navigate via ReferenceId.
                    outRow.ReferenceId = outRow.Id;

                    var inRow = new Gen_Store
                    {
                        ItemId = dto.ItemId,
                        BranchId = dto.ToBranchId,
                        WarehouseId = null,
                        TransactionDate = trxDate,

                        TransactionType = "IN",
                        ReferenceType = "BRANCH_TRANSFER",
                        ReferenceId = outRow.Id,
                        ReferenceNo = transferNo,

                        OpeningQty = destBalance,
                        InQty = dto.Qty,
                        OutQty = 0m,
                        CurrentQty = destBalance + dto.Qty,

                        Unit = fixedUnit,
                        Rate = rate,
                        Amount = dto.Qty * rate,

                        Remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                                  ? ("Transfer from " + fromBranch.Name)
                                  : dto.Remarks,

                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,
                        RecordTimeStamp = now
                    };

                    db.Gen_Store.Add(inRow);
                    db.SaveChanges();

                    tx.Commit();

                    return JsonSuccess("Branch transfer posted.", new
                    {
                        transferNo,
                        outId = outRow.Id,
                        inId = inRow.Id,
                        qty = dto.Qty,
                        unit = fixedUnit,
                        fromBranch = fromBranch.Name,
                        toBranch = toBranch.Name
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Transfer failed. " + DescribeStoreError(ex));
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK REVERSE
        //  Accepts a list of Gen_Store row ids; for each MATERIAL_ISSUE
        //  OUT row, looks up its ReferenceNo (the MI-YYYY-NNNN code)
        //  and POSTS to the existing per-issue reverse endpoint by
        //  delegating to the issue-materials controller. Per-row
        //  failures are reported in the envelope so the operator can
        //  see which ones survived a stock-balance guard and which
        //  didn't, without retrying one at a time.
        // ─────────────────────────────────────────────────────────────
        public class BulkIdsDto { public List<int> Ids { get; set; } }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(BulkIdsDto dto)
        {
            var failures = new List<object>();
            int ok = 0;

            foreach (var id in (dto?.Ids ?? new List<int>()).Distinct())
            {
                try
                {
                    var row = db.Gen_Store.FirstOrDefault(x => x.Id == id && x.Deleted == false);
                    if (row == null)
                    {
                        failures.Add(new { id, code = "(missing)", reason = "Row not found." });
                        continue;
                    }
                    if ((row.ReferenceType ?? "").Trim().ToUpper() != StoreReferenceType.MATERIAL_ISSUE
                     || (row.TransactionType ?? "").Trim().ToUpper() != "OUT")
                    {
                        failures.Add(new { id, code = row.ReferenceNo ?? "-", reason = "Only MATERIAL_ISSUE OUT rows are reversible from the ledger." });
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(row.ReferenceNo))
                    {
                        failures.Add(new { id, code = "(no code)", reason = "Row has no issue code to reverse." });
                        continue;
                    }

                    // Delegate to the existing per-issue endpoint so the
                    // reversal logic stays in one place. Reflection on
                    // the JsonResult lets us pluck out success/message
                    // without going through HTTP.
                    var ctl = new IssueMaterialsController();
                    var res = ctl.Reverse(row.ReferenceNo) as JsonResult;
                    if (res?.Data == null)
                    {
                        failures.Add(new { id, code = row.ReferenceNo, reason = "Reverse returned no payload." });
                        continue;
                    }
                    bool success = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                    if (success)
                    {
                        ok++;
                        LogStoreAudit(id, "REVERSED",
                            oldValue: row.TransactionType + " " + (row.OutQty ?? 0m).ToString(),
                            newValue: "REVERSED",
                            detail: "Issue " + row.ReferenceNo + " reversed via bulk action");
                    }
                    else
                    {
                        string msg = (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
                        failures.Add(new { id, code = row.ReferenceNo, reason = msg ?? "Reverse failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code = "-", reason = DescribeStoreError(ex) });
                }
            }

            return JsonSuccess("Bulk reverse completed.", new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures
            });
        }

    }
}