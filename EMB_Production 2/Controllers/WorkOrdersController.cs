using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using iTextSharp.text;
using iTextSharp.text.pdf;

using EMB_Production.Models;
using EMB_Production.ViewModels;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/work-orders")]
    public class WorkOrdersController : BaseController
    {
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            string status = "",
            int page = 1,
            int pageSize = 10,
            string fromDate = null,
            string toDate = null,
            int? customerId = null,
            int? branchId = null,
            string posted = null,
            string orderType = null,
            string workOrderStatus = null,
            // Quick-jump filter from the Job Orders list page. When present,
            // the response is narrowed to WOs whose parent JobOrderId matches.
            int? jobOrderId = null)
        {
            try
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 10 : pageSize;

                // Phase L.0 - parse date range filters defensively
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate)   && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string postedUp     = (posted ?? "").Trim().ToUpper();
                string orderTypeUp  = (orderType ?? "").Trim().ToUpper();
                string woStatusUp   = (workOrderStatus ?? "").Trim().ToUpper();

                var query =
                    from wo in db.WorkOrders

                    join jo in db.Gen_JobOrders on wo.JobOrderId equals jo.Id into jo1
                    from jo in jo1.DefaultIfEmpty()

                    join d in db.Gen_Design on wo.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join b in db.Branches on wo.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()

                    join fp in db.Gen_Products on wo.ProductId equals fp.Id into fp1
                    from fp in fp1.DefaultIfEmpty()

                    join c in db.Hr_Customers on wo.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    select new
                    {
                        wo.Id,
                        wo.WorkOrderCode,

                        wo.OrderType,
                        wo.IsCompanyStockOrder,

                        wo.JobOrderId,
                        JobOrderCode = jo != null ? jo.JobOrderCode : "",

                        wo.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",

                        wo.BranchId,
                        BranchName = b != null ? b.Name : "",

                        wo.CustomerId,
                        CustomerName = c != null ? c.Customer_Name : "",

                        wo.OrderQty,
                        wo.OrderDate,
                        wo.DueDate,

                        wo.ProductId,
                        ProductName = fp != null ? fp.Prod_Name : "",

                        wo.IsPosted,
                        wo.PostedBy,
                        wo.PostedDate,

                        wo.ReferenceNo,
                        wo.Remarks,
                        wo.Status,
                        wo.Active,
                        wo.Deleted,
                        wo.CreatedDate,
                        wo.UpdatedDate
                    };

                if (string.IsNullOrWhiteSpace(status))
                {
                    query = query.Where(x => x.Deleted == false || x.Deleted == null);
                }
                else if (status == "deleted")
                {
                    query = query.Where(x => x.Deleted == true);
                }
                else if (status == "active")
                {
                    query = query.Where(x => (x.Deleted == false || x.Deleted == null) && x.Active == true);
                }
                else if (status == "inactive")
                {
                    query = query.Where(x => (x.Deleted == false || x.Deleted == null) && x.Active == false);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    query = query.Where(x =>
                        (x.WorkOrderCode != null && x.WorkOrderCode.Contains(search)) ||
                        (x.JobOrderCode != null && x.JobOrderCode.Contains(search)) ||
                        (x.DesignCode != null && x.DesignCode.Contains(search)) ||
                        (x.DesignName != null && x.DesignName.Contains(search)) ||
                        (x.BranchName != null && x.BranchName.Contains(search)) ||
                        (x.CustomerName != null && x.CustomerName.Contains(search)) ||
                        (x.ProductName != null && x.ProductName.Contains(search)) ||
                        (x.ReferenceNo != null && x.ReferenceNo.Contains(search)) ||
                        (x.Remarks != null && x.Remarks.Contains(search)) ||
                        (x.Status != null && x.Status.Contains(search))
                    );
                }

                // Phase L.0 - new filter dimensions
                if (fromDt.HasValue) query = query.Where(x => x.OrderDate >= fromDt.Value);
                if (toDt.HasValue)   query = query.Where(x => x.OrderDate <= toDt.Value);
                if (customerId.HasValue && customerId.Value > 0) query = query.Where(x => x.CustomerId == customerId.Value);
                if (branchId.HasValue && branchId.Value > 0)     query = query.Where(x => x.BranchId == branchId.Value);
                if (jobOrderId.HasValue && jobOrderId.Value > 0) query = query.Where(x => x.JobOrderId == jobOrderId.Value);

                if (postedUp == "POSTED")     query = query.Where(x => x.IsPosted == true);
                else if (postedUp == "DRAFT") query = query.Where(x => x.IsPosted != true);

                if (orderTypeUp == "CUSTOMER")    query = query.Where(x => x.IsCompanyStockOrder == false);
                else if (orderTypeUp == "STOCK")  query = query.Where(x => x.IsCompanyStockOrder == true);

                if (!string.IsNullOrEmpty(woStatusUp))
                    query = query.Where(x => x.Status != null && x.Status.ToUpper() == woStatusUp);

                int totalItems = query.Count();

                var rawItems = query
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ---- AUTO-PR LOOKUP (Enhancement 4) -----------------------
                // Find the auto-generated PR(s) tied to each WO so the list
                // can show an "Auto PR" tag/link next to the WO code. We
                // look up both source types — WO_POST (direct) and the
                // PLAN_APPROVE PR that the WO's parent plan generated.
                var pageWoIds = rawItems.Select(x => x.Id).ToList();
                var pageJoIds = rawItems
                    .Where(x => x.JobOrderId.HasValue)
                    .Select(x => x.JobOrderId.Value)
                    .Distinct()
                    .ToList();

                // Wrapped in try/catch: the auto-PR feature is optional —
                // its tables (PurchaseRequisition, ProductionPlanItems) may
                // not exist in older deployments. When missing, the list
                // still loads; the auto-PR badge just doesn't appear.
                var prByWo = new Dictionary<int, string>();
                var prByPlanByWo = new Dictionary<int, string>();
                try
                {
                    // (a) Direct WO_POST PRs keyed by WO id
                    if (pageWoIds.Count > 0)
                    {
                        prByWo = db.Database.SqlQuery<WoPrRef>(@"
SELECT SourceReferenceId AS WoId, PrCode
FROM   dbo.PurchaseRequisition
WHERE  SourceType = 'WO_POST'
   AND (Deleted = 0 OR Deleted IS NULL)
   AND SourceReferenceId IN (" + string.Join(",", pageWoIds) + ")")
                            .ToList()
                            .GroupBy(r => r.WoId)
                            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PrCode).First().PrCode);
                    }

                    // (b) PLAN_APPROVE PRs — keyed by plan id, then map plan→WO via
                    //     the ProductionPlanItems table (each WO is linked back
                    //     to the plan item that generated it via
                    //     GeneratedWorkOrderId).
                    if (pageWoIds.Count > 0)
                    {
                        // WHERE-clause ORDER matters: put the inlined IN(...)
                        // last so the concatenated string tail is just `")"` —
                        // a single-line regular string. If IN comes first the
                        // trailing `AND (Deleted = 0 ...)` would have to span
                        // a newline inside a regular `"..."`, which CS1010
                        // rejects ("Newline in constant").
                        var planWoMap = db.Database.SqlQuery<PlanWoMapRow>(@"
SELECT GeneratedWorkOrderId AS WoId, PlanId
FROM   dbo.ProductionPlanItems
WHERE  (Deleted = 0 OR Deleted IS NULL)
   AND GeneratedWorkOrderId IS NOT NULL
   AND GeneratedWorkOrderId IN (" + string.Join(",", pageWoIds) + ")").ToList();

                        var planIds = planWoMap.Select(m => m.PlanId).Distinct().ToList();
                        if (planIds.Count > 0)
                        {
                            var prByPlan = db.Database.SqlQuery<WoPrRef>(@"
SELECT SourceReferenceId AS WoId, PrCode
FROM   dbo.PurchaseRequisition
WHERE  SourceType = 'PLAN_APPROVE'
   AND (Deleted = 0 OR Deleted IS NULL)
   AND SourceReferenceId IN (" + string.Join(",", planIds) + ")")
                                .ToList()
                                .GroupBy(r => r.WoId)
                                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PrCode).First().PrCode);

                            foreach (var m in planWoMap)
                            {
                                if (prByPlan.ContainsKey(m.PlanId)
                                    && !prByPlanByWo.ContainsKey(m.WoId))
                                    prByPlanByWo[m.WoId] = prByPlan[m.PlanId];
                                }
                        }
                    }
                }
                catch (System.Data.SqlClient.SqlException)
                {
                    // Tables don't exist yet (auto-PR feature not deployed).
                    // Leave both dictionaries empty so the list still loads.
                }

                // ─────────────────────────────────────────────────────────
                //  SPRINT-1  ENRICHMENT for the row badges + progress bar
                //  Every aggregate below uses a single batched query so the
                //  N+1 fan-out never kicks in even at pageSize=200. Each
                //  block is wrapped so a missing table (e.g. legacy DB
                //  without DailyProduction) degrades gracefully — the list
                //  still loads, the missing badge just doesn't show.
                // ─────────────────────────────────────────────────────────
                var producedByWo  = new Dictionary<int, int>();
                var dpCountByWo   = new Dictionary<int, int>();
                var qcCountByWo   = new Dictionary<int, int>();
                var dispCountByJo = new Dictionary<int, int>();
                var planByWo      = new Dictionary<int, PlanLinkRow>();

                if (pageWoIds.Count > 0)
                {
                    // HOTFIX: WorkOrderItems.CompletedQty is the canonical
                    // production signal used by the JO Profile. Shops that
                    // skip the Daily Production log entirely (and just mark
                    // CompletedQty on the WO items as production happens)
                    // would otherwise show 0% forever. Take the MAX across
                    // a WO's items — every item of an embroidery WO completes
                    // together (one batch produces all components), so MAX
                    // is the per-batch qty rather than the per-item sum.
                    try
                    {
                        var woiRows = db.Database.SqlQuery<CountByIdRow>(@"
SELECT  WorkOrderId AS Id,
        ISNULL(MAX(CompletedQty), 0) AS Cnt
FROM    dbo.WorkOrderItems
WHERE   (Deleted = 0 OR Deleted IS NULL)
   AND  WorkOrderId IN (" + string.Join(",", pageWoIds) + @")
GROUP BY WorkOrderId").ToList();
                        foreach (var r in woiRows)
                            if (r.Cnt > 0) producedByWo[r.Id] = r.Cnt;
                    }
                    catch (System.Data.SqlClient.SqlException) { }

                    // Sprint-3 polish: combine every available production
                    // signal into one ProducedQty per WO. Different shops
                    // record progress in different places — some use
                    // DailyProduction.AcceptedQty, some use ActualQty, some
                    // skip Daily Production entirely and rely on
                    // QC_Inspection.PassedQty. Taking the MAX across all
                    // three means the progress bar lights up no matter which
                    // path the operator follows.
                    //
                    // DailyProduction is also UNION'd by direct WorkOrderId
                    // (line 59 of the model) plus the WorkOrderItems join so
                    // rows added either way both count.
                    try
                    {
                        var dpRows = db.Database.SqlQuery<DpAggRow>(@"
;WITH dp_via_item AS (
    SELECT  wi.WorkOrderId,
            ISNULL(dp.AcceptedQty, 0) AS AcceptedQty,
            ISNULL(dp.ActualQty,   0) AS ActualQty
    FROM    dbo.DailyProduction dp
    JOIN    dbo.WorkOrderItems  wi ON wi.Id = dp.WorkOrderItemId
    WHERE   (dp.Deleted = 0 OR dp.Deleted IS NULL)
       AND  (wi.Deleted = 0 OR wi.Deleted IS NULL)
       AND  wi.WorkOrderId IN (" + string.Join(",", pageWoIds) + @")
), dp_via_direct AS (
    SELECT  dp.WorkOrderId,
            ISNULL(dp.AcceptedQty, 0) AS AcceptedQty,
            ISNULL(dp.ActualQty,   0) AS ActualQty
    FROM    dbo.DailyProduction dp
    WHERE   (dp.Deleted = 0 OR dp.Deleted IS NULL)
       AND  dp.WorkOrderId IS NOT NULL
       AND  dp.WorkOrderItemId IS NULL
       AND  dp.WorkOrderId IN (" + string.Join(",", pageWoIds) + @")
), dp_all AS (
    SELECT * FROM dp_via_item
    UNION ALL
    SELECT * FROM dp_via_direct
)
SELECT  WorkOrderId AS WoId,
        -- Take the larger of Accepted vs Actual per row, then sum.
        SUM(CASE WHEN AcceptedQty > ActualQty THEN AcceptedQty ELSE ActualQty END) AS ProducedQty,
        COUNT(*) AS RowCnt
FROM    dp_all
GROUP BY WorkOrderId").ToList();
                        foreach (var r in dpRows)
                        {
                            // HOTFIX: max-check vs WorkOrderItems.CompletedQty
                            // signal set earlier. DP zeros must never overwrite
                            // a real production qty from the WO items.
                            int existing = producedByWo.ContainsKey(r.WoId) ? producedByWo[r.WoId] : 0;
                            if (r.ProducedQty > existing) producedByWo[r.WoId] = r.ProducedQty;
                            dpCountByWo[r.WoId] = r.RowCnt;
                        }
                    }
                    catch (System.Data.SqlClient.SqlException) { }

                    // QC inspection: count + sum of PassedQty per WO. We
                    // gate to posted, non-deleted, non-cancelled rows so a
                    // draft QC doesn't inflate the progress signal.
                    try
                    {
                        var qcRows = db.Database.SqlQuery<QcAggRow>(@"
SELECT  WorkOrderId AS WoId,
        COUNT(*) AS RowCnt,
        ISNULL(SUM(PassedQty), 0) AS PassedQty
FROM    dbo.QC_Inspection
WHERE   (Deleted = 0 OR Deleted IS NULL)
   AND  WorkOrderId IN (" + string.Join(",", pageWoIds) + @")
   AND  (Status IS NULL OR Status NOT IN ('CANCELLED','Cancelled','Draft','DRAFT'))
GROUP BY WorkOrderId").ToList();
                        foreach (var r in qcRows)
                        {
                            qcCountByWo[r.WoId] = r.RowCnt;
                            // Take the MAX between existing DP-based produced
                            // and QC-passed. Whichever is higher reflects
                            // reality better — you can't pass QC for stock
                            // you didn't make.
                            int qcProduced = (int)Math.Round(r.PassedQty);
                            int existing   = producedByWo.ContainsKey(r.WoId) ? producedByWo[r.WoId] : 0;
                            if (qcProduced > existing) producedByWo[r.WoId] = qcProduced;
                        }
                    }
                    catch (System.Data.SqlClient.SqlException) { }

                    // Plan link per WO (PlanId + PlanCode) so the JO badge
                    // can also expose "from plan PP-2026-…".
                    try
                    {
                        var planRows = db.Database.SqlQuery<PlanLinkRow>(@"
SELECT  ppi.GeneratedWorkOrderId AS WoId, ppi.PlanId AS PlanId, p.PlanCode AS PlanCode
FROM    dbo.ProductionPlanItems ppi
JOIN    dbo.ProductionPlan      p   ON p.Id = ppi.PlanId
WHERE   (ppi.Deleted = 0 OR ppi.Deleted IS NULL)
   AND  ppi.GeneratedWorkOrderId IS NOT NULL
   AND  ppi.GeneratedWorkOrderId IN (" + string.Join(",", pageWoIds) + @")").ToList();
                        foreach (var r in planRows)
                            if (!planByWo.ContainsKey(r.WoId)) planByWo[r.WoId] = r;
                    }
                    catch (System.Data.SqlClient.SqlException) { }
                }

                // Dispatch count is keyed by JobOrderId (not WorkOrderId) —
                // dispatches are product-snapshot rows tied to the parent JO.
                if (pageJoIds.Count > 0)
                {
                    try
                    {
                        var dispRows = db.Database.SqlQuery<CountByIdRow>(@"
SELECT  JobOrderId AS Id, COUNT(*) AS Cnt
FROM    dbo.Dispatch
WHERE   (Deleted = 0 OR Deleted IS NULL)
   AND  JobOrderId IN (" + string.Join(",", pageJoIds) + @")
GROUP BY JobOrderId").ToList();
                        foreach (var r in dispRows) dispCountByJo[r.Id] = r.Cnt;
                    }
                    catch (System.Data.SqlClient.SqlException) { }
                }

                var items = rawItems.Select(x => new
                {
                    x.Id,
                    x.WorkOrderCode,

                    x.OrderType,
                    x.IsCompanyStockOrder,

                    x.JobOrderId,
                    x.JobOrderCode,

                    x.DesignId,
                    x.DesignCode,
                    x.DesignName,

                    x.BranchId,
                    x.BranchName,

                    CustomerId = x.IsCompanyStockOrder ? null : x.CustomerId,

                    CustomerName = x.IsCompanyStockOrder
                                ? "Stock"
                                : x.CustomerName,

                    x.OrderQty,

                    x.OrderDate,
                    OrderDateText = x.OrderDate.ToString("yyyy-MM-dd"),

                    x.DueDate,
                    DueDateText = x.DueDate.HasValue
                        ? x.DueDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    x.ProductId,
                    x.ProductName,

                    x.IsPosted,
                    x.PostedBy,
                    x.PostedDate,
                    PostedDateText = x.PostedDate.HasValue
                    ? x.PostedDate.Value.ToString("yyyy-MM-dd HH:mm") : "",

                    x.ReferenceNo,
                    x.Remarks,
                    x.Status,
                    x.Active,
                    x.Deleted,

                    x.CreatedDate,
                    CreatedDateText = x.CreatedDate.HasValue
                        ? x.CreatedDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    x.UpdatedDate,
                    UpdatedDateText = x.UpdatedDate.HasValue
                        ? x.UpdatedDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    // ---- Auto-PR (Enhancement 4) -----------------------------
                    // Either source applies; WO_POST wins when both exist.
                    AutoPrCode = prByWo.ContainsKey(x.Id)
                                    ? prByWo[x.Id]
                                    : (prByPlanByWo.ContainsKey(x.Id) ? prByPlanByWo[x.Id] : null),
                    AutoPrSource = prByWo.ContainsKey(x.Id)
                                    ? "WO_POST"
                                    : (prByPlanByWo.ContainsKey(x.Id) ? "PLAN_APPROVE" : null),

                    // ---- Sprint-1 enrichments --------------------------------
                    // ProducedQty: sum of posted DailyProduction.AcceptedQty.
                    // CompletionPercent: capped at 100 to avoid >100% display
                    // when a WO is over-produced (which the QC rework cycle
                    // CAN cause). Order-of-magnitude guard against div-by-zero
                    // via the ?? 0 + ternary.
                    ProducedQty       = producedByWo.ContainsKey(x.Id) ? producedByWo[x.Id] : 0,
                    CompletionPercent = (x.OrderQty ?? 0) > 0
                                        ? Math.Min(100, (decimal)(producedByWo.ContainsKey(x.Id) ? producedByWo[x.Id] : 0) * 100m / (x.OrderQty ?? 1))
                                        : 0m,

                    // Linked-plan info — null when WO was created directly
                    // (not generated from a plan).
                    PlanId   = planByWo.ContainsKey(x.Id) ? (int?)planByWo[x.Id].PlanId  : null,
                    PlanCode = planByWo.ContainsKey(x.Id) ? planByWo[x.Id].PlanCode      : null,

                    // Quick-jump counters for the row badges.
                    DailyProductionCount = dpCountByWo.ContainsKey(x.Id) ? dpCountByWo[x.Id] : 0,
                    QcCount              = qcCountByWo.ContainsKey(x.Id) ? qcCountByWo[x.Id] : 0,
                    DispatchCount        = (x.JobOrderId.HasValue && dispCountByJo.ContainsKey(x.JobOrderId.Value))
                                            ? dispCountByJo[x.JobOrderId.Value] : 0
                }).ToList();

                // Phase L.0 - summary aggregates across the entire FILTERED set (not paginated)
                var summaryRows = query.Select(x => new
                {
                    x.OrderQty,
                    x.IsPosted,
                    x.IsCompanyStockOrder,
                    x.Status
                }).ToList();

                var summary = new
                {
                    count        = summaryRows.Count,
                    totalQty     = summaryRows.Sum(s => s.OrderQty ?? 0),
                    postedCount  = summaryRows.Count(s => s.IsPosted),
                    draftCount   = summaryRows.Count(s => !s.IsPosted),
                    customerOrderCount = summaryRows.Count(s => !s.IsCompanyStockOrder),
                    stockOrderCount    = summaryRows.Count(s => s.IsCompanyStockOrder),
                    byStatus = summaryRows
                        .GroupBy(s => string.IsNullOrWhiteSpace(s.Status) ? "Unknown" : s.Status.Trim())
                        .Select(g => new { status = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                };

                return JsonSuccess("Work orders loaded successfully.", new
                {
                    items,
                    summary,
                    page,
                    pageSize,
                    totalItems,
                    totalPages = (int)Math.Ceiling((double)totalItems / pageSize)
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work orders.", ex);
            }
        }
        /* --------------------------------------------------------
           DETAIL
        --------------------------------------------------------*/
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var raw = (
                    from wo in db.WorkOrders

                    join jo in db.Gen_JobOrders on wo.JobOrderId equals jo.Id into jo1
                    from jo in jo1.DefaultIfEmpty()

                    join d in db.Gen_Design on wo.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join b in db.Branches on wo.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()

                    join p in db.Gen_Products on wo.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()

                    join c in db.Hr_Customers on wo.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    join cu in db.Adm_Users on wo.CreatedBy equals cu.Id into cu1
                    from cu in cu1.DefaultIfEmpty()

                    join uu in db.Adm_Users on wo.UpdatedBy equals uu.Id into uu1
                    from uu in uu1.DefaultIfEmpty()

                    where wo.Id == id

                    select new
                    {
                        wo.Id,
                        wo.WorkOrderCode,

                        wo.OrderType,
                        wo.IsCompanyStockOrder,

                        wo.JobOrderId,
                        JobOrderCode = jo != null ? jo.JobOrderCode : "",

                        wo.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",

                        wo.BranchId,
                        BranchName = b != null ? b.Name : "",

                        wo.OrderQty,
                        wo.OrderDate,
                        wo.DueDate,

                        wo.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",

                        wo.CustomerId,
                        CustomerCode = c != null ? c.Customer_Id : "",
                        CustomerName = c != null ? c.Customer_Name : "",

                        wo.IsPosted,
                        wo.PostedBy,
                        wo.PostedDate,

                        wo.ReferenceNo,
                        wo.Remarks,
                        wo.Status,
                        wo.Active,
                        wo.Deleted,

                        wo.RecordTimeStamp,

                        wo.CreatedBy,
                        CreatedByEmployeeId = cu != null ? (int?)cu.EmployeeId : null,
                        wo.CreatedDate,

                        wo.UpdatedBy,
                        UpdatedByEmployeeId = uu != null ? (int?)uu.EmployeeId : null,
                        wo.UpdatedDate
                    }
                ).FirstOrDefault();

                if (raw == null)
                    return JsonError("Work order not found.");

                var data = new
                {
                    raw.Id,
                    raw.WorkOrderCode,

                    raw.OrderType,
                    raw.IsCompanyStockOrder,

                    raw.JobOrderId,
                    raw.JobOrderCode,

                    raw.DesignId,
                    raw.DesignCode,
                    raw.DesignName,

                    raw.BranchId,
                    raw.BranchName,

                    raw.OrderQty,
                    raw.OrderDate,
                    OrderDateText = raw.OrderDate.ToString("yyyy-MM-dd"),

                    raw.DueDate,
                    DueDateText = raw.DueDate.HasValue
                        ? raw.DueDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    raw.ProductId,
                    raw.ProductCode,
                    raw.ProductName,

                    CustomerId = raw.IsCompanyStockOrder? null: raw.CustomerId,
                    CustomerCode = raw.IsCompanyStockOrder? "STOCK": raw.CustomerCode,
                    CustomerName = raw.IsCompanyStockOrder? "Stock": raw.CustomerName,

                    raw.IsPosted,
                    raw.PostedBy,
                    raw.PostedDate,
                    PostedDateText = raw.PostedDate.HasValue
                    ? raw.PostedDate.Value.ToString("yyyy-MM-dd HH:mm") : "",

                    raw.ReferenceNo,
                    raw.Remarks,
                    raw.Status,
                    raw.Active,
                    raw.Deleted,

                    raw.RecordTimeStamp,
                    RecordTimeStampText = raw.RecordTimeStamp.HasValue
                        ? raw.RecordTimeStamp.Value.ToString("yyyy-MM-dd HH:mm")
                        : "",

                    raw.CreatedBy,
                    raw.CreatedByEmployeeId,
                    raw.CreatedDate,
                    CreatedDateText = raw.CreatedDate.HasValue
                        ? raw.CreatedDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    raw.UpdatedBy,
                    raw.UpdatedByEmployeeId,
                    raw.UpdatedDate,
                    UpdatedDateText = raw.UpdatedDate.HasValue
                        ? raw.UpdatedDate.Value.ToString("yyyy-MM-dd")
                        : ""
                };

                return JsonSuccess("Work order loaded successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work order detail.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-2  AUDIT TIMELINE + WORKFLOW STAGE
        // --------------------------------------------------------------------
        //  Append-only audit log keyed by WorkOrderId. Writes via raw SQL so
        //  no EDMX regeneration is needed (mirrors what JobOrderAuditLog and
        //  ProductionPlanAuditLog do). Every state-changing endpoint calls
        //  LogAudit() inside its happy path. Failures to write the log are
        //  swallowed — audit is never allowed to break a real save.
        //
        //  WORKFLOW STAGE: derives the current step from IsPosted, Status,
        //  and the presence of downstream events (DailyProduction, QC,
        //  Dispatch). Surfaces the canonical 6-stage list so the stepper
        //  UI never has to map strings to positions itself.
        // ====================================================================

        // ====================================================================
        //  SPRINT-4  WO STATUS CONSTANTS
        // --------------------------------------------------------------------
        //  Centralised so a typo in one endpoint never silently breaks
        //  another. The mixed Title-Case (entity column) vs UPPER_SNAKE
        //  (workflow / audit) values are an EMB convention — Title-Case
        //  lives on WO.Status (display-facing); the workflow stepper and
        //  audit log use UPPER_SNAKE for machine-readability.
        //
        //  Add new statuses here, not as inline literals.
        // ====================================================================
        private static class WoStatus
        {
            // Title-Case — values stored in WO.Status (and surfaced in UI)
            public const string Draft         = "Draft";
            public const string Posted        = "Posted";
            public const string InProduction  = "In Production";
            public const string Completed     = "Completed";
            public const string Closed        = "Closed";
            public const string Cancelled     = "Cancelled";

            // Workflow / audit-log values — UPPER_SNAKE
            public const string StageDraft        = "DRAFT";
            public const string StagePosted       = "POSTED";
            public const string StageInProduction = "IN_PRODUCTION";
            public const string StageQc           = "QC";
            public const string StageDispatched   = "DISPATCHED";
            public const string StageClosed       = "CLOSED";
        }

        private static readonly string[] _woWorkflowStages = new[] {
            WoStatus.StageDraft, WoStatus.StagePosted, WoStatus.StageInProduction,
            WoStatus.StageQc,    WoStatus.StageDispatched, WoStatus.StageClosed
        };

        /** Compute the current workflow step from the WO's data plus a
         *  few cheap aggregate flags. Higher index always wins so a WO
         *  that's been dispatched but later marked Closed sits at CLOSED. */
        private int ComputeWoWorkflowStageIndex(
            int id, bool isPosted, string status,
            bool hasProduction, bool hasQc, bool hasDispatch)
        {
            string s = (status ?? "").Trim().ToUpper();
            if (s == "CLOSED" || s == "COMPLETED" || s == "DONE") return 5;
            if (hasDispatch) return 4;
            if (hasQc) return 3;
            // Sprint-3 polish: the DB stores "In Production" (PRODUCTION,
            // not PROGRESS) so the old `Contains("PROGRESS")` check never
            // matched. Now we look for either substring so any flavour of
            // in-flight status maps to stage 2.
            if (hasProduction || s.Contains("PRODUCTION") || s.Contains("PROGRESS")) return 2;
            if (isPosted) return 1;
            return 0;
        }

        private void LogAudit(int workOrderId, string action, string oldValue, string newValue, string detail)
        {
            try
            {
                int userId = GetSafeCurrentUserId();
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.WorkOrderAuditLog (WorkOrderId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@wid, @act, @uid, @now, @ov, @nv, @det)",
                    new SqlParameter("@wid", workOrderId),
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
                    "LogAudit failed for WO " + workOrderId + ": " + ex.Message);
            }
        }

        private class WoAuditRow
        {
            public int      Id            { get; set; }
            public int      WorkOrderId   { get; set; }
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
                var rows = db.Database.SqlQuery<WoAuditRow>(@"
SELECT  a.Id, a.WorkOrderId, a.Action,
        a.ChangedBy, u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
FROM    dbo.WorkOrderAuditLog a
LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
WHERE   a.WorkOrderId = @wid
ORDER BY a.ChangedDate DESC, a.Id DESC",
                    new SqlParameter("@wid", id)).ToList();

                var data = rows.Select(r => new
                {
                    id              = r.Id,
                    workOrderId     = r.WorkOrderId,
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

        /** Workflow stage for a single WO. Cheap two-existence-checks +
         *  one count query. Used by the Profile endpoint to power the
         *  stepper without forcing the caller to recompute stage on
         *  every Detail load. */
        [HttpGet, Route("workflow-stage/{id:int}")]
        public ActionResult WorkflowStage(int id)
        {
            try
            {
                var wo = db.WorkOrders.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (wo == null) return JsonError("Work Order not found.");

                bool hasProduction = db.DailyProduction.Any(dp =>
                    dp.Deleted != true &&
                    dp.WorkOrderItemId.HasValue &&
                    db.WorkOrderItems.Any(wi =>
                        wi.Id == dp.WorkOrderItemId.Value &&
                        wi.WorkOrderId == id &&
                        wi.Deleted != true));

                bool hasQc = db.QC_Inspection.Any(qc =>
                    qc.WorkOrderId == id && qc.Deleted != true);

                bool hasDispatch = wo.JobOrderId.HasValue && db.Dispatch.Any(dp =>
                    dp.JobOrderId == wo.JobOrderId.Value && dp.Deleted != true);

                int idx = ComputeWoWorkflowStageIndex(
                    id, wo.IsPosted, wo.Status, hasProduction, hasQc, hasDispatch);

                return JsonSuccess("OK", new
                {
                    workflowStages     = _woWorkflowStages,
                    workflowStageIndex = idx,
                    hasProduction,
                    hasQc,
                    hasDispatch
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to compute workflow stage.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-4 RACE-SAFE WO CODE GENERATION
        // --------------------------------------------------------------------
        //  Two concurrent Create / Clone calls could compute the same "next"
        //  WorkOrderCode and both make it past the existence check. The unique
        //  constraint on WorkOrderCode then raises a 2601/2627 on the slower
        //  insert and the user sees a 500. Same skip+retry pattern as the JO
        //  and Plan modules: `skip` lets the caller step N codes ahead in a
        //  loop after catching the unique-violation. ~3 attempts is plenty
        //  for any realistic concurrency on this endpoint.
        // ====================================================================
        private string GenerateWorkOrderCode(int skip = 0)
        {
            int year = DateTime.Now.Year;
            string prefix = "WO-" + year + "-";

            var codes = db.WorkOrders
                .Where(x => x.WorkOrderCode != null && x.WorkOrderCode.StartsWith(prefix))
                .Select(x => x.WorkOrderCode)
                .ToList();

            int maxNo = 0;

            foreach (var code in codes)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var parts = code.Split('-');
                if (parts.Length != 3)
                    continue;

                int n;
                if (int.TryParse(parts[2], out n) && n > maxNo)
                    maxNo = n;
            }

            return prefix + (maxNo + 1 + skip).ToString("D4");
        }

        /** SQL Server uses error 2601 (duplicate key in unique index) and
         *  2627 (duplicate key in unique constraint) for the collision that
         *  the WorkOrderCode uniqueness check would surface. Walks the inner
         *  exception chain since EF wraps SqlException inside DbUpdateException. */
        private bool IsWoCodeUniqueViolation(Exception ex)
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

        // Stamps a "[BYPASS: <reason> by user <id> at <utc>]" prefix into
        // the Work Order's Remarks when the Production Plan gate is
        // skipped. Keeps the audit trail on the row itself — no separate
        // table to query — and a SELECT * over WorkOrders surfaces every
        // bypass via a Remarks LIKE '%[BYPASS%' filter.
        private string BuildRemarksWithBypassTrail(
            string baseRemarks,
            bool bypassRequested,
            string bypassReason)
        {
            string clean = string.IsNullOrWhiteSpace(baseRemarks) ? null : baseRemarks.Trim();
            if (!bypassRequested)
                return clean;

            string stamp = "[BYPASS PLAN: " + bypassReason.Trim()
                         + " | by userId " + GetCurrentUserId().ToString()
                         + " at " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                         + "]";

            return string.IsNullOrEmpty(clean) ? stamp : (stamp + " " + clean);
        }

        /* --------------------------------------------------------
           CREATE
        --------------------------------------------------------*/
        [HttpPost]
        [Route("create")]
        public ActionResult Create(WorkOrderDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid data submitted.");

                if (!dto.JobOrderId.HasValue || dto.JobOrderId.Value <= 0)
                    return JsonError("Job Order is required.");

                if (!dto.DesignId.HasValue || dto.DesignId.Value <= 0)
                    return JsonError("Design is required.");

                if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                    return JsonError("Branch is required.");

                if (!dto.OrderQty.HasValue || dto.OrderQty.Value <= 0)
                    return JsonError("Order quantity must be greater than zero.");

                if (string.IsNullOrWhiteSpace(dto.OrderDate))
                    return JsonError("Order Date is required.");

                DateTime orderDate;
                if (!DateTime.TryParse(dto.OrderDate, out orderDate))
                    return JsonError("Invalid Order Date.");

                DateTime? dueDate = null;
                if (!string.IsNullOrWhiteSpace(dto.DueDate))
                {
                    DateTime dd;
                    if (DateTime.TryParse(dto.DueDate, out dd))
                        dueDate = dd;
                    else
                        return JsonError("Invalid Due Date.");
                }

                var jobOrder = db.Gen_JobOrders.FirstOrDefault(x =>
                    x.Id == dto.JobOrderId.Value &&
                    x.Deleted != true &&
                    x.Active == true &&
                    x.IsPosted == true);

                if (jobOrder == null)
                    return JsonError("Only posted active Job Orders can be converted to Work Order.");

                bool isCompanyStock = jobOrder.IsCompanyStockOrder ||
                    string.Equals(jobOrder.OrderType, "CompanyStock", StringComparison.OrdinalIgnoreCase);

                if (jobOrder.ProductId <= 0)
                    return JsonError("Product is missing on selected Job Order.");

                if (dto.DesignId.Value != jobOrder.DesignId)
                    return JsonError("Design does not match selected Job Order.");

                if (dto.BranchId.Value != jobOrder.BranchId)
                    return JsonError("Branch does not match selected Job Order.");

                if (!isCompanyStock)
                {
                    if (!jobOrder.CustomerId.HasValue || jobOrder.CustomerId.Value <= 0)
                        return JsonError("Customer is missing on selected Job Order.");

                    if (dto.CustomerId.HasValue &&
                        dto.CustomerId.Value > 0 &&
                        dto.CustomerId.Value != jobOrder.CustomerId.Value)
                    {
                        return JsonError("Customer does not match selected Job Order.");
                    }
                }

                // =====================================================
                // PRODUCTION PLAN GATE
                // =====================================================
                // A Work Order must be backed by an APPROVED Production
                // Plan entry that references this Job Order. Authorized
                // roles (Administrator, Manager) may override the gate
                // by passing BypassPlan = true plus a non-empty
                // BypassReason — used for rework, samples, R&D, or any
                // case where no plan was scheduled.
                //
                // Production Manager intentionally CANNOT bypass —
                // they're expected to schedule through the plan. Bypass
                // is for exception flows only, not normal scheduling.
                bool bypassRequested = dto.BypassPlan;
                string bypassReason  = (dto.BypassReason ?? "").Trim();

                if (!bypassRequested)
                {
                    int approvedPlanItemCount = db.Database.SqlQuery<int>(
                        @"SELECT COUNT(1)
                          FROM dbo.ProductionPlanItems pi
                          INNER JOIN dbo.ProductionPlan p ON pi.PlanId = p.Id
                          WHERE pi.JobOrderId = @p0
                            AND UPPER(LTRIM(RTRIM(p.Status))) = 'APPROVED'
                            AND (p.Deleted IS NULL OR p.Deleted = 0)",
                        dto.JobOrderId.Value
                    ).FirstOrDefault();

                    if (approvedPlanItemCount <= 0)
                    {
                        return JsonError(
                            "This Job Order is not on any approved Production Plan. " +
                            "Either approve a Production Plan that includes it, " +
                            "or have a Manager/Administrator submit this Work Order " +
                            "with the 'Bypass plan' option and a reason."
                        );
                    }
                }
                else
                {
                    int currentRoleId = GetCurrentRoleId();
                    string currentRoleName = db.Adm_Roles
                        .Where(r => r.Id == currentRoleId)
                        .Select(r => r.Name)
                        .FirstOrDefault() ?? "";

                    bool isAuthorized =
                        string.Equals(currentRoleName, "Administrator", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(currentRoleName, "Manager",       StringComparison.OrdinalIgnoreCase);

                    if (!isAuthorized)
                        return JsonError(
                            "Only Administrator or Manager roles can bypass the " +
                            "Production Plan requirement.");

                    if (string.IsNullOrWhiteSpace(bypassReason))
                        return JsonError(
                            "A reason is required when bypassing the Production Plan " +
                            "(rework, sample, R&D, etc.).");
                }

                int usedQty = db.WorkOrders
                    .Where(x => x.JobOrderId == dto.JobOrderId.Value && x.Deleted != true)
                    .Select(x => (int?)x.OrderQty)
                    .DefaultIfEmpty(0)
                    .Sum() ?? 0;

                int remainingQty = jobOrder.OrderQty - usedQty;

                if (remainingQty <= 0)
                    return JsonError("This Job Order is already fully consumed.");

                if (dto.OrderQty.Value > remainingQty)
                    return JsonError("Work Order quantity cannot be greater than remaining Job Order quantity.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Sprint-4: race-safe code generation. The TOCTOU race is
                // closed by the SaveChanges retry below — the existence
                // check here is just an optimistic shortcut.
                string workOrderCode = GenerateWorkOrderCode();

                var entity = new WorkOrders
                {
                    WorkOrderCode = workOrderCode,

                    JobOrderId = jobOrder.Id,
                    BranchId = jobOrder.BranchId,
                    DesignId = jobOrder.DesignId,
                    ProductId = jobOrder.ProductId,

                    OrderType = isCompanyStock ? "CompanyStock" : "Customer",
                    IsCompanyStockOrder = isCompanyStock,
                    CustomerId = isCompanyStock ? (int?)null : jobOrder.CustomerId,

                    OrderQty = dto.OrderQty,
                    OrderDate = orderDate,
                    DueDate = dueDate,

                    IsPosted = false,
                    PostedBy = null,
                    PostedDate = null,

                    ReferenceNo = string.IsNullOrWhiteSpace(dto.ReferenceNo) ? null : dto.ReferenceNo.Trim(),
                    // Bypass reason (if any) is prepended to Remarks so the audit
                    // trail lives on the WO row itself — no separate audit table.
                    Remarks = BuildRemarksWithBypassTrail(
                        baseRemarks:    dto.Remarks,
                        bypassRequested: bypassRequested,
                        bypassReason:    bypassReason),

                    Status = string.IsNullOrWhiteSpace(dto.Status) ? WoStatus.Draft : dto.Status.Trim(),
                    Active = dto.Active,
                    Deleted = false,

                    CreatedBy = userId,
                    CreatedDate = now,
                    UpdatedBy = null,
                    UpdatedDate = null,
                    RecordTimeStamp = now
                };

                db.WorkOrders.Add(entity);

                // Sprint-4: retry on unique-violation. The candidate code is
                // re-stamped with `GenerateWorkOrderCode(skip + 1)` so EF's
                // tracked entity gets the new code before re-attempting the
                // INSERT. 3 attempts is plenty under any realistic load.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { db.SaveChanges(); break; }
                    catch (Exception saveEx) when (IsWoCodeUniqueViolation(saveEx))
                    {
                        if (attempt + 1 >= 3) throw;
                        entity.WorkOrderCode = GenerateWorkOrderCode(attempt + 1);
                    }
                }

                LogAudit(entity.Id, "Created", null, WoStatus.StageDraft,
                    "WO " + entity.WorkOrderCode + " (qty " + dto.OrderQty.Value + ")");

                return JsonSuccess("Work order created successfully.", new
                {
                    entity.Id,
                    entity.WorkOrderCode,
                    entity.OrderType,
                    entity.IsCompanyStockOrder,
                    entity.CustomerId,
                    entity.ProductId,
                    remainingQtyAfterSave = remainingQty - dto.OrderQty.Value
                });
            }
            catch (Exception ex)
            {
                return JsonError("Create failed.", ex);
            }
        }

        /* --------------------------------------------------------
           UPDATE
        --------------------------------------------------------*/
        [HttpPost]
        [Route("update/{id:int}")]
        public ActionResult Update(int id, WorkOrderDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid data submitted.");

                var entity = db.WorkOrders.FirstOrDefault(x =>
                    x.Id == id &&
                    x.Deleted != true);

                if (entity == null)
                    return JsonError("Work order not found.");

                if (entity.IsPosted)
                    return JsonError("Posted Work Order cannot be edited. Reverse post first.");

                // Sprint-2: snapshot the qty + status so the audit log can
                // report deltas. Anything else the user can fix-then-reverse
                // is less interesting; qty change matters because it cascades
                // into BOM demand at Post time.
                int    oldQty    = entity.OrderQty ?? 0;
                string oldStatus = entity.Status;

                if (!dto.JobOrderId.HasValue || dto.JobOrderId.Value <= 0)
                    return JsonError("Job Order is required.");

                if (!dto.DesignId.HasValue || dto.DesignId.Value <= 0)
                    return JsonError("Design is required.");

                if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                    return JsonError("Branch is required.");

                if (!dto.OrderQty.HasValue || dto.OrderQty.Value <= 0)
                    return JsonError("Order quantity must be greater than zero.");

                if (string.IsNullOrWhiteSpace(dto.OrderDate))
                    return JsonError("Order Date is required.");

                DateTime orderDate;
                if (!DateTime.TryParse(dto.OrderDate, out orderDate))
                    return JsonError("Invalid Order Date.");

                DateTime? dueDate = null;
                if (!string.IsNullOrWhiteSpace(dto.DueDate))
                {
                    DateTime dd;
                    if (DateTime.TryParse(dto.DueDate, out dd))
                        dueDate = dd;
                    else
                        return JsonError("Invalid Due Date.");
                }

                var jobOrder = db.Gen_JobOrders.FirstOrDefault(x =>
                    x.Id == dto.JobOrderId.Value &&
                    x.Deleted != true &&
                    x.Active == true &&
                    x.IsPosted == true);

                if (jobOrder == null)
                    return JsonError("Only posted active Job Orders can be used.");

                bool isCompanyStock = jobOrder.IsCompanyStockOrder ||
                    string.Equals(jobOrder.OrderType, "CompanyStock", StringComparison.OrdinalIgnoreCase);

                if (jobOrder.ProductId <= 0)
                    return JsonError("Product is missing on selected Job Order.");

                if (dto.DesignId.Value != jobOrder.DesignId)
                    return JsonError("Design does not match selected Job Order.");

                if (dto.BranchId.Value != jobOrder.BranchId)
                    return JsonError("Branch does not match selected Job Order.");

                if (!isCompanyStock)
                {
                    if (!jobOrder.CustomerId.HasValue || jobOrder.CustomerId.Value <= 0)
                        return JsonError("Customer is missing on selected Job Order.");

                    if (dto.CustomerId.HasValue &&
                        dto.CustomerId.Value > 0 &&
                        dto.CustomerId.Value != jobOrder.CustomerId.Value)
                    {
                        return JsonError("Customer does not match selected Job Order.");
                    }
                }

                int usedQtyOtherWorkOrders = db.WorkOrders
                    .Where(x =>
                        x.JobOrderId == jobOrder.Id &&
                        x.Id != entity.Id &&
                        x.Deleted != true)
                    .Select(x => (int?)x.OrderQty)
                    .DefaultIfEmpty(0)
                    .Sum() ?? 0;

                int remainingAllowedQty = jobOrder.OrderQty - usedQtyOtherWorkOrders;

                if (dto.OrderQty.Value > remainingAllowedQty)
                    return JsonError("Work Order quantity cannot be greater than remaining Job Order quantity.");

                entity.JobOrderId = jobOrder.Id;
                entity.BranchId = jobOrder.BranchId;
                entity.DesignId = jobOrder.DesignId;
                entity.ProductId = jobOrder.ProductId;

                entity.OrderType = isCompanyStock ? "CompanyStock" : "Customer";
                entity.IsCompanyStockOrder = isCompanyStock;
                entity.CustomerId = isCompanyStock ? (int?)null : jobOrder.CustomerId;

                entity.OrderQty = dto.OrderQty;
                entity.OrderDate = orderDate;
                entity.DueDate = dueDate;

                entity.ReferenceNo = string.IsNullOrWhiteSpace(dto.ReferenceNo) ? null : dto.ReferenceNo.Trim();
                entity.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

                entity.Status = string.IsNullOrWhiteSpace(dto.Status) ? WoStatus.Draft : dto.Status.Trim();
                entity.Active = dto.Active;

                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;
                entity.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                // Sprint-2: log only the noteworthy diffs so the timeline
                // doesn't drown in noise. Qty change cascades into BOM demand;
                // status change drives the workflow stepper.
                if (oldQty != (entity.OrderQty ?? 0))
                {
                    LogAudit(entity.Id, "QtyChanged",
                        oldQty.ToString(), (entity.OrderQty ?? 0).ToString(),
                        "Qty " + oldQty + " → " + (entity.OrderQty ?? 0));
                }
                if (!string.Equals(oldStatus ?? "", entity.Status ?? "", StringComparison.Ordinal))
                {
                    LogAudit(entity.Id, "StatusChanged",
                        oldStatus, entity.Status, null);
                }
                LogAudit(entity.Id, "Updated", null, null, "Header edited");

                return JsonSuccess("Work order updated successfully.", new
                {
                    entity.Id,
                    entity.WorkOrderCode,
                    entity.OrderType,
                    entity.IsCompanyStockOrder,
                    entity.CustomerId,
                    entity.ProductId
                });
            }
            catch (Exception ex)
            {
                return JsonError("Update failed.", ex);
            }
        }

        /* --------------------------------------------------------
           Phase L.1 - WORK ORDER 360 PROFILE
        --------------------------------------------------------*/
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                // 1. Header
                var head = (
                    from w in db.WorkOrders
                    join jo in db.Gen_JobOrders on w.JobOrderId equals jo.Id into jo1
                    from jo in jo1.DefaultIfEmpty()
                    join d  in db.Gen_Design   on w.DesignId   equals d.Id  into d1
                    from d  in d1.DefaultIfEmpty()
                    join b  in db.Branches     on w.BranchId   equals b.Id  into b1
                    from b  in b1.DefaultIfEmpty()
                    join p  in db.Gen_Products on w.ProductId  equals p.Id  into p1
                    from p  in p1.DefaultIfEmpty()
                    join c  in db.Hr_Customers on w.CustomerId equals c.Id  into c1
                    from c  in c1.DefaultIfEmpty()
                    where w.Id == id
                    select new
                    {
                        w.Id,
                        w.WorkOrderCode,
                        w.OrderType,
                        w.IsCompanyStockOrder,
                        w.IsPosted,
                        w.Active,
                        w.Deleted,
                        w.Status,
                        w.OrderDate,
                        w.DueDate,
                        w.OrderQty,
                        w.ReferenceNo,
                        w.Remarks,
                        w.JobOrderId,
                        JobOrderCode = jo != null ? jo.JobOrderCode : "",
                        w.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",
                        w.BranchId,
                        BranchName = b != null ? b.Name : "",
                        w.ProductId,
                        ProductName = p != null ? p.Prod_Name : "",
                        w.CustomerId,
                        CustomerName = c != null ? c.Customer_Name : "",
                        w.CreatedBy,
                        w.CreatedDate,
                        w.UpdatedBy,
                        w.UpdatedDate
                    }
                ).FirstOrDefault();

                if (head == null) return JsonError("Work order not found.");

                var header = new
                {
                    id = head.Id,
                    workOrderCode = head.WorkOrderCode,
                    orderType = head.OrderType,
                    isCompanyStockOrder = head.IsCompanyStockOrder,
                    isPosted = head.IsPosted,
                    active = head.Active,
                    deleted = head.Deleted ?? false,
                    status = head.Status,
                    orderDateText = head.OrderDate.ToString("dd-MMM-yyyy"),
                    dueDateText = head.DueDate.HasValue ? head.DueDate.Value.ToString("dd-MMM-yyyy") : null,
                    orderQty = head.OrderQty ?? 0,
                    referenceNo = head.ReferenceNo,
                    remarks = head.Remarks,
                    jobOrderId = head.JobOrderId,
                    jobOrderCode = head.JobOrderCode,
                    designId = head.DesignId,
                    designCode = head.DesignCode,
                    designName = head.DesignName,
                    branchId = head.BranchId,
                    branchName = head.BranchName,
                    productId = head.ProductId,
                    productName = head.ProductName,
                    customerId = head.CustomerId,
                    customerName = head.IsCompanyStockOrder ? "Stock" : head.CustomerName,
                    createdDate = head.CreatedDate.HasValue ? head.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDate = head.UpdatedDate.HasValue ? head.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // 2. Items
                var items = (
                    from wi in db.WorkOrderItems
                    join cm in db.Gen_Components on wi.CompId equals cm.Id into cm1
                    from cm in cm1.DefaultIfEmpty()
                    join m  in db.Gen_Machines   on wi.MachineId equals m.Id into m1
                    from m  in m1.DefaultIfEmpty()
                    where wi.WorkOrderId == id && wi.Deleted != true
                    orderby wi.Id
                    select new
                    {
                        wi.Id,
                        wi.CompId,
                        ComponentName = cm != null ? cm.Name : "",
                        ComponentCode = cm != null ? (cm.Code == null ? "" : cm.Code.ToString()) : "",
                        wi.MachineId,
                        MachineCode = m != null ? m.Code : "",
                        wi.Quantity,
                        wi.CompletedQty,
                        wi.RejectedQty,
                        wi.UnitPrice,
                        wi.TotalCost,
                        wi.CostPerUnit,
                        wi.Status,
                        wi.Remarks
                    }
                ).ToList();

                decimal itemsTotalQty       = items.Sum(i => i.Quantity);
                decimal itemsCompletedQty   = items.Sum(i => (decimal)i.CompletedQty);
                decimal itemsRejectedQty    = items.Sum(i => (decimal)i.RejectedQty);
                decimal itemsTotalAmount    = items.Sum(i => i.Quantity * (i.UnitPrice ?? 0m));
                decimal itemsTotalCost      = items.Sum(i => i.TotalCost ?? 0m);

                // 3. QC trail
                var qcTrail = (
                    from q in db.QC_Inspection
                    join u in db.Adm_Users on q.InspectorId equals u.Id into u1
                    from u in u1.DefaultIfEmpty()
                    where q.WorkOrderId == id && q.Deleted != true
                    orderby q.InspectionDate descending, q.Id descending
                    select new
                    {
                        q.Id,
                        q.InspectionDate,
                        q.InspectedQty,
                        q.PassedQty,
                        q.FailedQty,
                        q.ReworkQty,
                        q.BatchQty,
                        q.Status,
                        InspectorEmployeeId = u != null ? u.EmployeeId : (int?)null,
                        q.WorkOrderItemId,
                        q.ProductionId
                    }
                ).ToList();

                decimal qcInspectedQty = qcTrail.Sum(q => q.InspectedQty ?? 0m);
                decimal qcPassedQty    = qcTrail.Sum(q => q.PassedQty    ?? 0m);
                decimal qcFailedQty    = qcTrail.Sum(q => q.FailedQty    ?? 0m);

                var qcTrailOut = qcTrail.Select(q => new
                {
                    id = q.Id,
                    inspectionDateText = q.InspectionDate.ToString("dd-MMM-yyyy"),
                    inspectedQty = q.InspectedQty ?? 0m,
                    passedQty    = q.PassedQty    ?? 0m,
                    failedQty    = q.FailedQty    ?? 0m,
                    reworkQty    = q.ReworkQty    ?? 0m,
                    batchQty     = q.BatchQty     ?? 0m,
                    status = q.Status,
                    inspectorEmployeeCode = q.InspectorEmployeeId.HasValue ? ("EMP-" + q.InspectorEmployeeId.Value) : "-",
                    workOrderItemId = q.WorkOrderItemId,
                    productionId = q.ProductionId
                }).ToList();

                // 4. Production trail (Gen_Stocks IN from QC inspections of this WO)
                var qcIds = qcTrail.Select(q => q.Id).ToList();
                var productionTrail = db.Gen_Stocks
                    .Where(s => s.ReferenceType == "QC_INSPECTION"
                             && s.ReferenceId.HasValue
                             && qcIds.Contains(s.ReferenceId.Value)
                             && s.Deleted != true)
                    .OrderByDescending(s => s.CreatedDate).ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.CreatedDate,
                        s.TransactionType,
                        s.ReferenceType,
                        s.ReferenceNo,
                        s.ReferenceId,
                        s.OpeningQty,
                        s.InQty,
                        s.OutQty,
                        s.CurrentQty,
                        s.Unit,
                        s.Rate,
                        s.Amount,
                        s.StockStage,
                        s.Remarks
                    })
                    .ToList();

                var productionTrailOut = productionTrail.Select(s => new
                {
                    id = s.Id,
                    dateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy") : "-",
                    transactionType = s.TransactionType,
                    referenceType = s.ReferenceType,
                    referenceNo = s.ReferenceNo,
                    referenceId = s.ReferenceId,
                    openingQty = s.OpeningQty ?? 0m,
                    inQty = s.InQty ?? 0m,
                    outQty = s.OutQty ?? 0m,
                    currentQty = s.CurrentQty ?? 0m,
                    unit = s.Unit,
                    rate = s.Rate ?? 0m,
                    amount = s.Amount ?? 0m,
                    stockStage = s.StockStage,
                    remarks = s.Remarks
                }).ToList();

                decimal producedInQty = productionTrail.Sum(s => s.InQty ?? 0m);

                // 5. Dispatch trail (match by JobOrderId; product-level snapshot when WO.ProductId set)
                var dispatchTrailOut = new List<object>();
                decimal dispatchedQty = 0m;
                if (head.JobOrderId.HasValue && head.JobOrderId.Value > 0)
                {
                    var joIdVal = head.JobOrderId.Value;
                    var dispatches = (
                        from d in db.Dispatch
                        join c in db.Hr_Customers on d.CustomerId equals c.Id into c1
                        from c in c1.DefaultIfEmpty()
                        where d.JobOrderId == joIdVal && d.Deleted != true
                        orderby d.DispatchDate descending, d.Id descending
                        select new
                        {
                            d.Id,
                            d.DispatchDate,
                            d.IsPosted,
                            d.IsReversed,
                            d.DispatchType,
                            CustomerName = c != null ? c.Customer_Name : d.DirectCustomerName,
                            d.VehicleNo,
                            d.Remarks
                        }
                    ).ToList();

                    var dispIds = dispatches.Select(d => d.Id).ToList();
                    var dispItemAggs = db.DispatchItems
                        .Where(i => dispIds.Contains(i.DispatchId)
                                 && i.Deleted != true
                                 && (!head.ProductId.HasValue || i.ProductId == head.ProductId.Value))
                        .GroupBy(i => i.DispatchId)
                        .Select(g => new
                        {
                            dispatchId = g.Key,
                            qty = g.Sum(i => i.DispatchQty ?? 0m),
                            amount = g.Sum(i => i.Amount ?? 0m)
                        })
                        .ToList()
                        .ToDictionary(x => x.dispatchId, x => new { x.qty, x.amount });

                    dispatchedQty = dispItemAggs.Values.Sum(x => x.qty);

                    dispatchTrailOut = dispatches.Select(d => (object)new
                    {
                        id = d.Id,
                        dispatchDateText = d.DispatchDate.ToString("dd-MMM-yyyy"),
                        dispatchType = d.DispatchType,
                        customerName = d.CustomerName,
                        vehicleNo = d.VehicleNo,
                        isPosted = d.IsPosted,
                        isReversed = d.IsReversed,
                        qty = dispItemAggs.ContainsKey(d.Id) ? dispItemAggs[d.Id].qty : 0m,
                        amount = dispItemAggs.ContainsKey(d.Id) ? dispItemAggs[d.Id].amount : 0m,
                        remarks = d.Remarks
                    }).ToList();
                }

                // 6. KPIs
                var kpis = new
                {
                    itemCount = items.Count,
                    totalQty = itemsTotalQty,
                    completedQty = itemsCompletedQty,
                    rejectedQty = itemsRejectedQty,
                    pendingQty = Math.Max(0, itemsTotalQty - itemsCompletedQty - itemsRejectedQty),
                    qcInspectedQty,
                    qcPassedQty,
                    qcFailedQty,
                    producedInQty,
                    dispatchedQty,
                    totalAmount = Math.Round(itemsTotalAmount, 2),
                    totalCost   = Math.Round(itemsTotalCost, 2)
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    items = items.Select(i => new
                    {
                        id = i.Id,
                        componentId = i.CompId,
                        componentName = i.ComponentName,
                        componentCode = i.ComponentCode,
                        machineId = i.MachineId,
                        machineCode = i.MachineCode,
                        quantity = i.Quantity,
                        completedQty = i.CompletedQty,
                        rejectedQty = i.RejectedQty,
                        unitPrice = i.UnitPrice ?? 0m,
                        totalCost = i.TotalCost ?? 0m,
                        costPerUnit = i.CostPerUnit ?? 0m,
                        status = i.Status,
                        remarks = i.Remarks
                    }).ToList(),
                    qcTrail = qcTrailOut,
                    productionTrail = productionTrailOut,
                    dispatchTrail = dispatchTrailOut
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        /* --------------------------------------------------------
           DELETE
        --------------------------------------------------------*/
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var wo = db.WorkOrders.FirstOrDefault(x => x.Id == id && (x.Deleted == false || x.Deleted == null));
                if (wo == null)
                    return JsonError("Work order not found.");

                if (wo.IsPosted)
                    return JsonError("Posted Work Order cannot be deleted. Reverse post first.");

                wo.Deleted = true;
                wo.UpdatedBy = GetCurrentUserId();
                wo.UpdatedDate = DateTime.Now;
                wo.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogAudit(wo.Id, "Deleted", wo.Status, null, "Soft-deleted (Deleted=1)");
                return JsonSuccess("Work order deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed.", ex);
            }
        }

        /* --------------------------------------------------------
           NEXT CODE
        --------------------------------------------------------*/
        [HttpGet]
        [Route("next-code")]
        public ActionResult GetNextCode()
        {
            try
            {
                string code = GenerateWorkOrderCode();

                return JsonSuccess("Next work order code generated.", new
                {
                    code
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to generate work order code.", ex);
            }
        }
                        
        [HttpGet]
        [Route("joborders-dropdown")]
        public ActionResult JobOrdersDropdown()
        {
            try
            {
                var rows = db.Gen_JobOrders
                .Where(x =>
                    x.Deleted != true &&    // Sprint-1: nullable bool — covers legacy NULL rows
                    x.Active == true &&
                    x.IsPosted == true)
                .Select(x => new
                {
                    x.Id,
                    x.JobOrderCode,
                    x.OrderQty,

                    UsedQty = db.WorkOrders
                        .Where(w => w.JobOrderId == x.Id && w.Deleted != true)
                        .Sum(w => (int?)w.OrderQty) ?? 0
                })
                .ToList()
                .Select(x => new
                {
                    id = x.Id,
                    text = x.JobOrderCode,
                    jobOrderCode = x.JobOrderCode,
                    remainingQty = x.OrderQty - x.UsedQty
                })
                .Where(x => x.remainingQty > 0)
                .OrderBy(x => x.text)
                .ToList();

                return JsonSuccess("Job orders loaded.", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load job orders.", ex);
            }
        }

        private void UpdateJobOrderStatus(int jobOrderId)
        {
            // Sprint-1: nullable bool — `!= true` covers both `false` AND
            // legacy NULL rows. Matches the rest of the controller's
            // soft-delete predicate convention.
            var jobOrder = db.Gen_JobOrders.FirstOrDefault(x =>
                x.Id == jobOrderId &&
                x.Deleted != true);

            if (jobOrder == null)
                return;

            int orderQty = jobOrder.OrderQty ;

            int usedQty = db.WorkOrders
                .Where(w =>
                    w.JobOrderId == jobOrderId &&
                    w.Deleted != true)
                .Sum(w => (int?)w.OrderQty) ?? 0;

            if (usedQty <= 0)
                jobOrder.Status = "Pending";
            else if (usedQty < orderQty)
                jobOrder.Status = "Partially Converted";
            else
                jobOrder.Status = "Completed";

            jobOrder.UpdatedBy = GetCurrentUserId();
            jobOrder.UpdatedDate = DateTime.Now;
        }

        [HttpPost]
        [Route("post/{id:int}")]
        public ActionResult Post(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var wo = db.WorkOrders.FirstOrDefault(x =>
                        x.Id == id &&
                        x.Deleted != true);

                    if (wo == null)
                        return JsonError("Work Order not found.");

                    if (wo.IsPosted == true)
                        return JsonError("Work Order already posted.");

                    if (!wo.JobOrderId.HasValue || wo.JobOrderId.Value <= 0)
                        return JsonError("Job Order is required before posting.");

                    if (!wo.DesignId.HasValue || wo.DesignId.Value <= 0)
                        return JsonError("Design is required before posting.");

                    if (!wo.BranchId.HasValue || wo.BranchId.Value <= 0)
                        return JsonError("Branch is required before posting.");

                    if (!wo.ProductId.HasValue || wo.ProductId.Value <= 0)
                        return JsonError("Product is required before posting.");

                    if (!wo.OrderQty.HasValue || wo.OrderQty.Value <= 0)
                        return JsonError("Order quantity must be greater than zero.");

                    var jobOrder = db.Gen_JobOrders.FirstOrDefault(x =>
                        x.Id == wo.JobOrderId.Value &&
                        x.Deleted != true &&
                        x.Active == true &&
                        x.IsPosted == true);

                    if (jobOrder == null)
                        return JsonError("Only posted Job Orders can be used for Work Order posting.");

                    bool itemsAlreadyExist = db.WorkOrderItems.Any(x =>
                        x.WorkOrderId == wo.Id &&
                        x.Deleted != true);

                    if (!itemsAlreadyExist)
                    {
                        var bomItems = db.Gen_BOM
                            .Where(x =>
                                x.DesignId == wo.DesignId.Value &&
                                x.Deleted != true &&
                                x.Active == true &&
                                x.CompId > 0)
                            .OrderBy(x => x.Id)
                            .ToList();

                        if (!bomItems.Any())
                            return JsonError("No BOM/components found for DesignId " + wo.DesignId.Value + ". Cannot generate Work Order Items.");

                        // Cost the design once for this Work Order's quantity (20% margin),
                        // then stamp the same numbers on every generated item — same shape
                        // ApplyDynamicCosting uses on the manual edit path.
                        EMB_Production.ViewModels.DesignCostCalculationResult costing = null;
                        try
                        {
                            var costingService = new DesignCostingService(db);
                            costing = costingService.CalculateDesignCost(
                                wo.DesignId.Value, wo.OrderQty.Value, 20);
                        }
                        catch
                        {
                            // Leave costing null — items will be saved with NULL costs.
                            // Manual recalc from the UI can backfill them later.
                        }

                        // Machine is recorded on the design, not the work order, so each
                        // generated item inherits it from there.
                        int? designMachineId = db.Gen_Design
                            .Where(x => x.Id == wo.DesignId.Value)
                            .Select(x => x.MachineId)
                            .FirstOrDefault();

                        foreach (var b in bomItems)
                        {
                            var item = new WorkOrderItems
                            {
                                WorkOrderId = wo.Id,
                                JobOrderId = wo.JobOrderId,
                                DesignId = wo.DesignId,
                                ProductId = wo.ProductId,
                                BranchId = wo.BranchId,

                                CompId = b.CompId,

                                Quantity = wo.OrderQty.Value,
                                CompletedQty = 0,
                                RejectedQty = 0,

                                MachineId = designMachineId,
                                Status = "Pending",

                                Remarks = "Auto-generated from BOM",

                                Active = true,
                                Deleted = false,
                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            };

                            if (costing != null)
                            {
                                item.ThreadCost = costing.ThreadCost;
                                item.MachineCost = costing.MachineCost;
                                item.LaborCost = costing.LaborCost;
                                item.ElectricityCost = costing.ElectricityCost;
                                item.OtherCost = costing.OtherCost;
                                item.CostPerUnit = costing.CostPerUnit;
                                item.TotalCost = costing.TotalCost;
                                item.UnitPrice = costing.SellingPricePerUnit;
                            }

                            db.WorkOrderItems.Add(item);
                        }

                        db.SaveChanges();
                    }

                    bool hasItems = db.WorkOrderItems.Any(x =>
                        x.WorkOrderId == wo.Id &&
                        x.Deleted != true);

                    if (!hasItems)
                        return JsonError("Work Order Items could not be generated.");

                    wo.IsPosted = true;
                    wo.PostedBy = userId;
                    wo.PostedDate = now;
                    wo.Status = WoStatus.Posted;
                    wo.UpdatedBy = userId;
                    wo.UpdatedDate = now;
                    wo.RecordTimeStamp = now;

                    db.SaveChanges();
                    tx.Commit();

                    // =========================================================
                    // AUTO PURCHASE REQUISITION
                    // ---------------------------------------------------------
                    // After the WO post is committed, compute the BOM × qty
                    // demand for this WO's design, compare against current
                    // on-hand stock in Gen_Store, and create a DRAFT PR for
                    // any raw-item shortfall. The PR then flows through the
                    // existing buyer-approve → PO auto-create chain.
                    //
                    // Done OUTSIDE the post transaction on purpose — if the
                    // PR creation fails (e.g. transient DB issue) the WO
                    // post should still stand. We just won't get the auto
                    // procurement signal on this WO; the buyer can raise
                    // a manual PR.
                    // =========================================================
                    AutoPrCore.GenResult autoResult = null;
                    try
                    {
                        autoResult = AutoGeneratePrFromWorkOrder(wo, userId, now);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Auto-PR generation failed for WO " + wo.Id + ": " + ex.Message);
                        // Swallow — WO post is the success contract; PR is a bonus.
                    }

                    string baseMsg = "Work Order posted and items generated successfully.";
                    string fullMsg;
                    if (autoResult == null || string.IsNullOrEmpty(autoResult.PrCode))
                    {
                        fullMsg = baseMsg + " (No procurement needed — all materials on hand or already on order.)";
                    }
                    else
                    {
                        // Auto-PR always lands as DRAFT now; the frontend will
                        // redirect to its Edit modal so the buyer can review,
                        // adjust supplier, then Submit + Approve.
                        fullMsg = baseMsg + " Purchase Requisition " + autoResult.PrCode
                                          + " created as DRAFT for " + autoResult.ItemCount
                                          + " material shortfall line(s) — review and approve to issue the PO.";
                    }

                    LogAudit(wo.Id, "Posted", WoStatus.StageDraft, WoStatus.StagePosted,
                        "Items + materials generated"
                      + (autoResult?.PrId > 0 ? "; auto-PR " + autoResult.PrCode : ""));

                    return JsonSuccess(fullMsg, new {
                        workOrderId = wo.Id,
                        autoPrId    = autoResult?.PrId ?? 0,
                        autoPrCode  = autoResult?.PrCode,
                        autoPrItems = autoResult?.ItemCount ?? 0
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    // Sprint-4: drill into EF's wrapper to surface the real
                    // SQL error instead of "An error occurred while updating
                    // the entries. See the inner exception for details."
                    return JsonError("Post failed: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        // ROW TYPES used by the WO list's auto-PR lookup
        // ====================================================================
        private class WoPrRef     { public int WoId { get; set; } public string PrCode { get; set; } }
        private class PlanWoMapRow{ public int WoId { get; set; } public int PlanId { get; set; } }

        // Sprint-1: aggregate row shapes for the batched lookups in List().
        private class DpAggRow      { public int WoId { get; set; } public int ProducedQty { get; set; } public int RowCnt { get; set; } }
        private class CountByIdRow  { public int Id   { get; set; } public int Cnt { get; set; } }
        private class PlanLinkRow   { public int WoId { get; set; } public int PlanId { get; set; } public string PlanCode { get; set; } }

        // Sprint-3 polish: QC aggregate shape — combines count + total
        // passed qty so the same query feeds both the QC badge and the
        // progress-bar fallback signal.
        private class QcAggRow      { public int WoId { get; set; } public int RowCnt { get; set; } public decimal PassedQty { get; set; } }

        // ====================================================================
        // AUTO-PR INSTANCE WRAPPERS
        // ====================================================================
        // Thin instance wrappers around the static AutoPrCore (defined
        // below). Keeping the heavy logic static lets ProductionPlansController
        // call the exact same code path on Plan-Approve without us having
        // to instantiate this controller.

        private AutoPrCore.GenResult AutoGeneratePrFromWorkOrder(
            WorkOrders wo, int userId, DateTime now)
        {
            return AutoPrCore.Generate(
                db,
                new List<int> { wo.Id },
                sourceType:        "WO_POST",
                sourceReferenceId: wo.Id,
                sourceLabel:       wo.WorkOrderCode,
                userId:            userId,
                canAutoApprove:    CanCurrentRoleAutoApprovePr(),
                now:               now);
        }

        /// <summary>
        /// Returns true when the current user's role is Administrator or
        /// Manager — those roles can have their auto-PRs auto-approved
        /// (Status APPROVED + a PO auto-created) when a single resolvable
        /// supplier covers every shortfall line. Falls back to false on
        /// any role-lookup failure so the safer DRAFT-only path is used.
        /// </summary>
        private bool CanCurrentRoleAutoApprovePr()
        {
            try
            {
                int roleId = GetCurrentRoleId();
                string roleName = db.Adm_Roles
                    .Where(r => r.Id == roleId)
                    .Select(r => r.Name).FirstOrDefault() ?? "";
                return string.Equals(roleName, "Administrator", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(roleName, "Manager",       StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        [HttpPost]
        [Route("reverse-post/{id:int}")]
        public ActionResult ReversePost(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var wo = db.WorkOrders.FirstOrDefault(x =>
                        x.Id == id &&
                        (x.Deleted == false || x.Deleted == null));

                    if (wo == null)
                        return JsonError("Work Order not found.");

                    if (wo.IsPosted != true)
                        return JsonError("Work Order is not posted.");

                    // ---- BLOCK CHECKS ---------------------------------------------------
                    // The cascade rule is: a WO can only be reversed if every downstream
                    // commit can also be reversed safely. Production posted to QC and
                    // POs with goods already received are point-of-no-return events; we
                    // refuse the reversal and tell the user which commit blocks it.

                    bool hasProduction = db.DailyProduction.Any(dp =>
                        dp.Deleted != true &&
                        dp.WorkOrderItemId.HasValue &&
                        db.WorkOrderItems.Any(wi =>
                            wi.Id == dp.WorkOrderItemId.Value &&
                            wi.WorkOrderId == id &&
                            wi.Deleted != true));

                    if (hasProduction)
                        return JsonError("Cannot reverse. Daily Production already exists against this Work Order.");

                    bool hasQc = db.QC_Inspection.Any(qc =>
                        qc.WorkOrderId == id &&
                        qc.Deleted != true);

                    if (hasQc)
                        return JsonError("Cannot reverse. QC Inspection already exists against this Work Order.");

                    // Find the plan this WO came from (null for ad-hoc WOs).
                    int? planId = db.Database.SqlQuery<int?>(@"
SELECT TOP 1 PlanId FROM dbo.ProductionPlanItems
WHERE GeneratedWorkOrderId = @id
  AND (Deleted = 0 OR Deleted IS NULL)",
                        new SqlParameter("@id", id)).FirstOrDefault();

                    // Collect linked PR ids in two buckets:
                    //   directPrIds  — WO_POST PRs (auto-created when this WO posted)
                    //   planPrIds    — PLAN_APPROVE PRs (shared across the plan's WOs)
                    var directPrIds = SafePrIds(@"
SELECT Id FROM dbo.PurchaseRequisition
WHERE SourceType = 'WO_POST' AND SourceReferenceId = @id
  AND (Deleted = 0 OR Deleted IS NULL)",
                        new SqlParameter("@id", id));

                    var planPrIds = planId.HasValue
                        ? SafePrIds(@"
SELECT Id FROM dbo.PurchaseRequisition
WHERE SourceType = 'PLAN_APPROVE' AND SourceReferenceId = @pid
  AND (Deleted = 0 OR Deleted IS NULL)",
                            new SqlParameter("@pid", planId.Value))
                        : new List<int>();

                    // Block if any linked PO has actually received goods. Receipts post
                    // to Gen_Store as IN rows; cancelling the PO header alone wouldn't
                    // undo that. The user has to cancel the GRN first.
                    var allPrIds = directPrIds.Concat(planPrIds).Distinct().ToList();
                    if (allPrIds.Count > 0)
                    {
                        string prList = string.Join(",", allPrIds);
                        var receivedPos = SafeList<string>(@"
SELECT po.PoCode
FROM   dbo.PurchaseOrder po
WHERE  po.PrId IN (" + prList + @")
   AND (po.Deleted = 0 OR po.Deleted IS NULL)
   AND (po.ReceivedAmount > 0
     OR po.Status IN ('PARTIALLY_RECEIVED', 'FULLY_RECEIVED', 'CLOSED'))");

                        if (receivedPos.Count > 0)
                        {
                            return JsonError("Cannot reverse. Materials already received against "
                                + string.Join(", ", receivedPos) + ". Cancel the GRN first.");
                        }
                    }

                    // ---- CASCADE REVERSALS ----------------------------------------------
                    // From here on, every block is recoverable. If anything throws, the
                    // outer transaction rolls all of it back.

                    // 1) Work Order Items — soft-delete the auto-generated breakdown.
                    var items = db.WorkOrderItems
                        .Where(x => x.WorkOrderId == id && x.Deleted != true)
                        .ToList();
                    foreach (var item in items)
                    {
                        item.Deleted = true;
                        item.Active = false;
                        item.UpdatedBy = userId;
                        item.UpdatedDate = now;
                        item.RecordTimeStamp = now;
                    }

                    // 2) Material Issues — soft-delete Gen_Store OUT rows tagged to this
                    //    WO. The running balance is computed from the ledger so soft-
                    //    deleting these rows automatically returns the stock to store.
                    //
                    //    Sprint-4 hardening: gate on TransactionType='OUT' AND BranchId
                    //    matches the WO's branch. The previous query only matched on
                    //    (ReferenceType, ReferenceId) which is correct under the write
                    //    convention but offers no defence against a stray row that
                    //    happens to share the ReferenceId. Two extra equality checks
                    //    cost nothing on a small Gen_Store and close the gap.
                    int issueRowsReversed = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.Gen_Store
SET    Deleted = 1, Active = 0,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  ReferenceType = 'MATERIAL_ISSUE'
   AND ReferenceId = @woid
   AND TransactionType = 'OUT'
   AND (@brId IS NULL OR BranchId = @brId)
   AND (Deleted = 0 OR Deleted IS NULL)",
                        new SqlParameter("@uid",  userId),
                        new SqlParameter("@now",  now),
                        new SqlParameter("@woid", id),
                        new SqlParameter("@brId", (object)(wo.BranchId) ?? DBNull.Value));

                    // 3) Production Plan Items — set the plan row back to CANCELLED and
                    //    null its WO link so the planner can re-generate later if needed.
                    int planRowsCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    Status = 'CANCELLED', GeneratedWorkOrderId = NULL,
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  GeneratedWorkOrderId = @woid
   AND (Deleted = 0 OR Deleted IS NULL)",
                        new SqlParameter("@uid",  userId),
                        new SqlParameter("@now",  now),
                        new SqlParameter("@woid", id));

                    // 4) Direct WO_POST PRs — these belong solely to this WO. Cancel
                    //    them and any DRAFT POs they spawned. (Received POs were already
                    //    blocked above; remaining POs are safe to cancel.)
                    int directPrsCancelled = 0;
                    int directPosCancelled = 0;
                    if (directPrIds.Count > 0)
                    {
                        string list = string.Join(",", directPrIds);
                        directPosCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Status = 'CANCELLED',
       CancelledBy = @uid, CancelledDate = @now,
       CancellationReason = 'Auto-cancelled: source Work Order was reversed',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  PrId IN (" + list + @")
   AND (Deleted = 0 OR Deleted IS NULL)
   AND Status NOT IN ('CANCELLED', 'CLOSED')",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now));

                        directPrsCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status = 'REJECTED',
       RejectionReason = 'Auto-cancelled: source Work Order was reversed',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  Id IN (" + list + @")
   AND (Deleted = 0 OR Deleted IS NULL)
   AND Status NOT IN ('REJECTED')",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now));
                    }

                    // 5) PLAN_APPROVE PRs — these are shared across every WO of the plan.
                    //    Only cancel when the WO being reversed was the LAST posted WO of
                    //    the plan. Otherwise leave the PR alone — other WOs still need
                    //    the materials. (Note: planRowsCancelled above already unlinked
                    //    this WO from its plan item, so the count below correctly
                    //    excludes it.)
                    int planPrsCancelled = 0;
                    int planPosCancelled = 0;
                    if (planId.HasValue && planPrIds.Count > 0)
                    {
                        int remainingPostedPlanWos = db.Database.SqlQuery<int>(@"
SELECT COUNT(*)
FROM   dbo.ProductionPlanItems ppi
JOIN   dbo.WorkOrders wo ON wo.Id = ppi.GeneratedWorkOrderId
WHERE  ppi.PlanId = @pid
   AND ppi.GeneratedWorkOrderId IS NOT NULL
   AND (ppi.Deleted = 0 OR ppi.Deleted IS NULL)
   AND (wo.Deleted  = 0 OR wo.Deleted  IS NULL)
   AND wo.IsPosted = 1",
                            new SqlParameter("@pid", planId.Value)).FirstOrDefault();

                        if (remainingPostedPlanWos == 0)
                        {
                            string list = string.Join(",", planPrIds);
                            planPosCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Status = 'CANCELLED',
       CancelledBy = @uid, CancelledDate = @now,
       CancellationReason = 'Auto-cancelled: every Work Order of the source plan was reversed',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  PrId IN (" + list + @")
   AND (Deleted = 0 OR Deleted IS NULL)
   AND Status NOT IN ('CANCELLED', 'CLOSED')",
                                new SqlParameter("@uid", userId),
                                new SqlParameter("@now", now));

                            planPrsCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status = 'REJECTED',
       RejectionReason = 'Auto-cancelled: every Work Order of the source plan was reversed',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  Id IN (" + list + @")
   AND (Deleted = 0 OR Deleted IS NULL)
   AND Status NOT IN ('REJECTED')",
                                new SqlParameter("@uid", userId),
                                new SqlParameter("@now", now));
                        }
                    }

                    // 6) Reset the WO header back to Draft.
                    wo.IsPosted = false;
                    wo.PostedBy = null;
                    wo.PostedDate = null;
                    wo.Status = WoStatus.Draft;
                    wo.UpdatedBy = userId;
                    wo.UpdatedDate = now;
                    wo.RecordTimeStamp = now;

                    db.SaveChanges();
                    tx.Commit();

                    // Build a human summary so the toast tells the user exactly what
                    // unwound. Silent cascades feel like bugs even when they're correct.
                    var parts = new List<string> { "Work Order reverse posted." };
                    if (items.Count > 0)         parts.Add(items.Count + " breakdown item(s) removed");
                    if (issueRowsReversed > 0)   parts.Add(issueRowsReversed + " material issue line(s) returned to store");
                    if (planRowsCancelled > 0)   parts.Add("Plan item set to CANCELLED");
                    if (directPrsCancelled > 0)  parts.Add(directPrsCancelled + " PR (WO_POST) cancelled");
                    if (directPosCancelled > 0)  parts.Add(directPosCancelled + " linked PO cancelled");
                    if (planPrsCancelled > 0)    parts.Add(planPrsCancelled + " PR (PLAN_APPROVE) cancelled");
                    if (planPosCancelled > 0)    parts.Add(planPosCancelled + " plan-linked PO cancelled");

                    LogAudit(wo.Id, "Reversed", WoStatus.StagePosted, WoStatus.StageDraft,
                        string.Join(", ", parts));

                    return JsonSuccess(string.Join(". ", parts) + ".");
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    // Sprint-4: surface the real SQL error from the chain.
                    return JsonError("Reverse post failed: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        //  SPRINT-3  BULK ACTIONS  +  CLONE
        // --------------------------------------------------------------------
        //  Same per-row try/catch + failures-array shape as the JO and Plan
        //  bulk endpoints so the Angular afterBulk helper is reusable. Each
        //  row gets its own transaction wherever the underlying single-row
        //  endpoint needed one. A bad row never aborts the whole batch.
        // ====================================================================

        public class WoBulkIdsDto      { public List<int> Ids { get; set; } }
        private class WoBulkRowResult  { public int Id { get; set; } public string Code { get; set; } public string Reason { get; set; } }

        /** Sprint-3: drill into EF/SQL exception chain to extract the
         *  real SQL Server error text. EF6 wraps SqlException inside
         *  DbUpdateException ("An error occurred while updating the
         *  entries.") which is useless on its own. Special-cases the
         *  validation exception for per-property messages. */
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

        [HttpPost, Route("bulk-post")]
        public ActionResult BulkPost(WoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No work orders selected.");

            int successCount = 0;
            var failures = new List<WoBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    // Reuse the single-row Post — it owns its own transaction
                    // and the full BOM-pull + auto-PR pipeline. We inspect the
                    // JsonResult to know what went wrong. The same pattern as
                    // BulkReverse below.
                    var result = Post(id) as System.Web.Mvc.JsonResult;
                    var payload = result?.Data;
                    bool ok = false;
                    string msg = null;
                    if (payload != null)
                    {
                        var t = payload.GetType();
                        var sp = t.GetProperty("success"); if (sp != null) ok = (bool)(sp.GetValue(payload) ?? false);
                        var mp = t.GetProperty("message"); if (mp != null) msg = (string)mp.GetValue(payload);
                    }

                    if (ok) successCount++;
                    else
                    {
                        var wo = db.WorkOrders.FirstOrDefault(x => x.Id == id);
                        failures.Add(new WoBulkRowResult { Id = id, Code = wo?.WorkOrderCode ?? "?", Reason = msg ?? "Post failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new WoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " posted, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(WoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No work orders selected.");

            int successCount = 0;
            var failures = new List<WoBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    // Reuse the single-row endpoint's logic by invoking it.
                    // It returns ActionResult; we inspect to know what went
                    // wrong. Per-row uses its own transaction inside.
                    var result = ReversePost(id) as System.Web.Mvc.JsonResult;
                    var payload = result?.Data;
                    bool ok = false;
                    string msg = null;

                    if (payload != null)
                    {
                        var t = payload.GetType();
                        var sp = t.GetProperty("success"); if (sp != null) ok = (bool)(sp.GetValue(payload) ?? false);
                        var mp = t.GetProperty("message"); if (mp != null) msg = (string)mp.GetValue(payload);
                    }

                    if (ok) successCount++;
                    else
                    {
                        var wo = db.WorkOrders.FirstOrDefault(x => x.Id == id);
                        failures.Add(new WoBulkRowResult { Id = id, Code = wo?.WorkOrderCode ?? "?", Reason = msg ?? "Reverse failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new WoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " reversed, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(WoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No work orders selected.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            int successCount = 0;
            var failures = new List<WoBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var wo = db.WorkOrders.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                    if (wo == null)
                    { failures.Add(new WoBulkRowResult { Id = id, Code = "?", Reason = "Not found." }); continue; }
                    if (wo.IsPosted)
                    { failures.Add(new WoBulkRowResult { Id = id, Code = wo.WorkOrderCode, Reason = "Posted — reverse first." }); continue; }

                    wo.Deleted = true;
                    wo.UpdatedBy = userId;
                    wo.UpdatedDate = now;
                    wo.RecordTimeStamp = now;
                    db.SaveChanges();

                    LogAudit(wo.Id, "Deleted", wo.Status, null, "(bulk) Soft-deleted");
                    successCount++;
                }
                catch (Exception ex)
                {
                    failures.Add(new WoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
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
        //  CLONE  —  deep-copy a WO header into a fresh DRAFT
        // --------------------------------------------------------------------
        //  Same product / design / JO / branch / customer. Qty is capped to
        //  the JO's remaining qty so we never over-allocate. Items are NOT
        //  copied (they're regenerated at Post from the BOM anyway). Code
        //  is generated fresh.
        // ====================================================================
        [HttpPost, Route("clone/{id:int}")]
        public ActionResult Clone(int id)
        {
            try
            {
                var src = db.WorkOrders.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (src == null) return JsonError("Source Work Order not found.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // If the source had a JO link, verify the JO still has
                // remaining qty. Cap clone qty if needed.
                int? qty = src.OrderQty;
                if (src.JobOrderId.HasValue)
                {
                    var jo = db.Gen_JobOrders.FirstOrDefault(j => j.Id == src.JobOrderId.Value && j.Deleted != true);
                    if (jo == null)
                        return JsonError("Source Work Order's Job Order has been deleted; cannot clone with the same JO link.");

                    int used = db.WorkOrders
                        .Where(w => w.JobOrderId == jo.Id && w.Deleted != true)
                        .Select(w => (int?)w.OrderQty)
                        .DefaultIfEmpty(0)
                        .Sum() ?? 0;
                    int remaining = jo.OrderQty - used;
                    if (remaining <= 0)
                        return JsonError("Job Order " + jo.JobOrderCode + " has no remaining quantity. Create a fresh Job Order, or clear the JO link before cloning.");

                    if (qty.GetValueOrDefault(0) > remaining) qty = remaining;
                    if (qty.GetValueOrDefault(0) <= 0)         qty = remaining;
                }

                // Sprint-4: optimistic candidate; the actual race is closed
                // by the SaveChanges retry on the clone insert below.
                string newCode = GenerateWorkOrderCode();

                var clone = new WorkOrders
                {
                    WorkOrderCode       = newCode,
                    OrderType           = src.OrderType,
                    IsCompanyStockOrder = src.IsCompanyStockOrder,
                    JobOrderId          = src.JobOrderId,
                    ProductId           = src.ProductId,
                    DesignId            = src.DesignId,
                    BranchId            = src.BranchId,
                    CustomerId          = src.CustomerId,
                    OrderQty            = qty,
                    OrderDate           = now,                // today, not source date
                    DueDate             = src.DueDate,         // same target due date
                    ReferenceNo         = src.ReferenceNo,
                    Remarks             = string.IsNullOrWhiteSpace(src.Remarks)
                                            ? "Cloned from " + src.WorkOrderCode
                                            : "Cloned from " + src.WorkOrderCode + " — " + src.Remarks,
                    Status              = WoStatus.Draft,
                    Active              = true,
                    Deleted             = false,
                    IsPosted            = false,
                    PostedBy            = null,
                    PostedDate          = null,
                    CreatedBy           = userId,
                    CreatedDate         = now,
                    UpdatedBy           = null,
                    UpdatedDate         = null,
                    RecordTimeStamp     = now
                };

                db.WorkOrders.Add(clone);

                // Sprint-4: retry on code collision (same race as Create).
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { db.SaveChanges(); break; }
                    catch (Exception saveEx) when (IsWoCodeUniqueViolation(saveEx))
                    {
                        if (attempt + 1 >= 3) throw;
                        clone.WorkOrderCode = GenerateWorkOrderCode(attempt + 1);
                    }
                }

                LogAudit(clone.Id, "Cloned", null, WoStatus.StageDraft,
                    "Cloned from " + src.WorkOrderCode + " (id " + src.Id + ")"
                  + (qty != src.OrderQty ? "; qty capped to " + qty : ""));

                return JsonSuccess("Cloned.", new
                {
                    id            = clone.Id,
                    workOrderCode = clone.WorkOrderCode,
                    sourceId      = src.Id,
                    sourceCode    = src.WorkOrderCode,
                    orderQty      = clone.OrderQty,
                    qtyCapped     = qty != src.OrderQty
                });
            }
            catch (Exception ex)
            {
                return JsonError("Clone failed: " + DescribeError(ex));
            }
        }

        // Small helpers used by ReversePost's cascade — keep the LINQ-to-SQL
        // surface narrow and the parent method readable.
        private List<int> SafePrIds(string sql, params SqlParameter[] ps)
        {
            return db.Database.SqlQuery<int>(sql, ps).ToList();
        }
        private List<T> SafeList<T>(string sql, params SqlParameter[] ps)
        {
            return db.Database.SqlQuery<T>(sql, ps).ToList();
        }

        [HttpGet, Route("export-excel")]
        public ActionResult ExportExcel()
        {
            var list = db.WorkOrders
                .Where(x => x.Deleted == false || x.Deleted == null)
                .Select(x => new
                {
                    x.OrderDate,    // DateTime (not nullable)
            x.DueDate,      // DateTime? (nullable)
            x.ReferenceNo,
                    Customer = x.IsCompanyStockOrder? "Stock": (x.Hr_Customers != null? x.Hr_Customers.Customer_Name: ""),
                    Product = x.Gen_Products.Prod_Name,
                    Status = (x.Deleted == true ? "Deleted" : "Active")
                })
                .ToList();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("OrderDate,Customer,Product,Reference,DueDate,Status");

            foreach (var x in list)
            {
                string orderDateStr = x.OrderDate.ToString("yyyy-MM-dd");
                string dueDateStr = x.DueDate.HasValue
                    ? x.DueDate.Value.ToString("yyyy-MM-dd")
                    : "";

                sb.AppendLine(
                    $"{orderDateStr}," +
                    $"{(x.Customer ?? "")}," +
                    $"{(x.Product ?? "")}," +
                    $"{(x.ReferenceNo ?? "")}," +
                    $"{dueDateStr}," +
                    $"{(x.Status ?? "")}"
                );
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "WorkOrders.csv");
        }
      
        [HttpGet, Route("export-pdf")]
        public ActionResult ExportPdf()
        {
            try
            {
                // Materialize first, then project — keeps null-safe lookups simple
                // for stock orders (no customer) and products that may be missing.
                var raw = db.WorkOrders
                    .Include("Hr_Customers")
                    .Include("Gen_Products")
                    .Include("Gen_Design")
                    .Where(x => x.Deleted == false || x.Deleted == null)
                    .OrderByDescending(x => x.Id)
                    .ToList();

                using (var ms = new System.IO.MemoryStream())
                {
                    var doc = new Document(PageSize.A4.Rotate(), 25f, 25f, 30f, 30f);
                    var writer = PdfWriter.GetInstance(doc, ms);
                    writer.PageEvent = new PdfFooter();
                    doc.Open();

                    var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.BLACK);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  BaseColor.WHITE);
                    var bodyFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8.5f,    BaseColor.BLACK);

                    doc.Add(new Paragraph("Work Orders — List", titleFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph(" "));

                    var table = new PdfPTable(8) { WidthPercentage = 100f };
                    table.SetWidths(new float[] { 1.2f, 0.9f, 1.4f, 1.6f, 1.6f, 0.8f, 1.0f, 1.0f });

                    void AddHeader(string text)
                    {
                        table.AddCell(new PdfPCell(new Phrase(text, headerFont))
                        {
                            BackgroundColor = new BaseColor(13, 110, 253),
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5f
                        });
                    }

                    AddHeader("Code");
                    AddHeader("Type");
                    AddHeader("Customer");
                    AddHeader("Product");
                    AddHeader("Design");
                    AddHeader("Qty");
                    AddHeader("Order Date");
                    AddHeader("Status");

                    foreach (var x in raw)
                    {
                        string code     = x.WorkOrderCode ?? ("WO-" + x.Id);
                        string type     = (x.OrderType ?? "").Trim();
                        string customer = x.Hr_Customers != null ? (x.Hr_Customers.Customer_Name ?? "-") : "Stock";
                        string product  = x.Gen_Products  != null ? (x.Gen_Products.Prod_Name    ?? "-") : "-";
                        string design   = x.Gen_Design    != null
                            ? ((x.Gen_Design.D_Code ?? "") + " - " + (x.Gen_Design.DNo ?? "")).Trim(' ', '-')
                            : "-";
                        string qty      = (x.OrderQty ?? 0).ToString("N0");
                        string orderDt  = x.OrderDate.ToString("yyyy-MM-dd");
                        string status   = (x.IsPosted == true) ? "Posted" : "Draft";

                        table.AddCell(new PdfPCell(new Phrase(code,     bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(type,     bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(customer, bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(product,  bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(design,   bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(qty,      bodyFont))
                            { Padding = 4f, HorizontalAlignment = Element.ALIGN_RIGHT });
                        table.AddCell(new PdfPCell(new Phrase(orderDt,  bodyFont))
                            { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                        table.AddCell(new PdfPCell(new Phrase(status,   bodyFont))
                            { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                    }

                    doc.Add(table);

                    var footer = new Paragraph(
                        $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | Rows: {raw.Count}",
                        FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY));
                    footer.Alignment = Element.ALIGN_RIGHT;
                    footer.SpacingBefore = 10f;
                    doc.Add(footer);

                    doc.Close();
                    return File(ms.ToArray(), "application/pdf", "WorkOrders.pdf");
                }
            }
            catch (Exception ex)
            {
                // Sprint-1: consistent envelope — JsonError matches every
                // other endpoint's { success, message, data } shape so the
                // frontend's error handler doesn't have to branch.
                return JsonError("Failed to generate PDF.", ex);
            }
        }


        public class PdfFooter : iTextSharp.text.pdf.PdfPageEventHelper
        {
            public override void OnEndPage(iTextSharp.text.pdf.PdfWriter writer, iTextSharp.text.Document document)
            {
                // Use the built-in HELVETICA constant — "Arial" isn't registered
                // by FontFactory by default, which made font.BaseFont null and
                // SetFontAndSize threw "Value cannot be null. Parameter name: key".
                var font = iTextSharp.text.FontFactory.GetFont(iTextSharp.text.FontFactory.HELVETICA, 9);
                if (font == null || font.BaseFont == null) return;

                var pageText = $"Page {writer.PageNumber}";
                var cb = writer.DirectContent;

                cb.BeginText();
                cb.SetFontAndSize(font.BaseFont, 9);
                cb.ShowTextAligned(
                    iTextSharp.text.Element.ALIGN_RIGHT,
                    pageText,
                    document.Right - 10,
                    document.Bottom - 20,
                    0
                );
                cb.EndText();
            }
        }



        /* --------------------------------------------------------
           PRINT VIEW
        --------------------------------------------------------*/
        [HttpGet, Route("print")]
        public ActionResult Print()
        {
            var list = db.WorkOrders.ToList();
            return View("PrintWorkOrders", list);
        }

        /* --------------------------------------------------------
           DISPOSE
        --------------------------------------------------------*/
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();
            base.Dispose(disposing);
        }
    }

    // ========================================================================
    // AutoPrCore
    // ------------------------------------------------------------------------
    // Shared auto-Purchase-Requisition generator used by both
    // WorkOrdersController.Post (post-WO procurement signal) and
    // ProductionPlansController.Approve (consolidated procurement signal
    // for an entire weekly plan).
    //
    // Responsibilities:
    //   1. Aggregate BOM demand across the supplied Work Order Ids.
    //      (For a single WO: same as a per-WO PR. For a whole plan:
    //       one consolidated PR per plan, lines summed across WOs.)
    //   2. Subtract pending procurement (open PR DRAFT/SUBMITTED qty +
    //      open PO DRAFT/SENT/PARTIALLY_RECEIVED unreceived qty) so we
    //      don't re-order what's already coming.
    //   3. Subtract current Gen_Store on-hand.
    //   4. For each raw-item shortfall, pick an estimated rate from
    //      Gen_Threads.UnitCost (weighted average across threads that
    //      back the same raw item).
    //   5. Identify a "best" supplier per raw item from
    //      Gen_Threads.SupplierId (mode — most common across backing
    //      threads). If every shortfall line shares the same supplier
    //      AND the caller authorized auto-approve, jump straight to
    //      APPROVED (status = APPROVED) and chain into PO creation.
    //   6. Insert PR header + items. If auto-approved, also insert the
    //      PO header + items and mark the PR as CONVERTED_TO_PO.
    //
    // Returns a GenResult describing what happened. Caller decides how
    // to present it to the user. Throws on hard SQL failure — caller
    // should swallow to keep WO post / Plan approve resilient.
    // ========================================================================
    internal static class AutoPrCore
    {
        public class GenResult
        {
            public int    PrId;          // 0 when no shortfall (no PR created); otherwise the new PR id
            public string PrCode;        // null when no shortfall
            public int    ItemCount;     // # of shortfall lines on the PR
            public bool   AutoApproved = false;  // legacy flag — always false now (PRs are always DRAFT)
            public string PoCode = null;         // legacy field — always null now
            public string SkipReason;    // diagnostic: why PrCode is null
        }

        public static GenResult Generate(
            EMB_Production.Models.EMB_ProductionEntities1 db,
            List<int>  woIds,
            string     sourceType,        // "WO_POST" or "PLAN_APPROVE"
            int        sourceReferenceId,
            string     sourceLabel,       // WO code / Plan code — used in remarks
            int        userId,
            bool       canAutoApprove,
            DateTime   now)
        {
            var result = new GenResult();

            if (woIds == null || woIds.Count == 0)
            { result.SkipReason = "No Work Order ids supplied."; return result; }

            // ---- (1) Fetch WO design + qty + branch for each id --------------
            var woRows = db.WorkOrders
                .Where(w => woIds.Contains(w.Id) && w.Deleted != true)
                .Select(w => new { w.Id, w.DesignId, w.OrderQty, w.BranchId })
                .ToList();

            int? sharedBranchId = woRows.Select(w => (int?)w.BranchId).FirstOrDefault();

            // ---- (2) Aggregate BOM × qty demand across all WOs ----------------
            // Source: Gen_BOM × Gen_ComponentMaterials. This unified table
            // covers raw items, fabrics, AND threads (a thread is just a
            // Gen_RawItems row with Type='THREAD' whose CompMaterials line
            // records consumption per stitched unit).
            //
            // Gen_ThreadUsage used to be a parallel thread-only BOM, but
            // it carried stale/inflated Usage values (e.g. 35/unit where
            // production actually consumed 10/unit). We dropped that path
            // — comparing two sources made every estimate drift.
            //
            // UnitCost + SupplierId come from Gen_Threads when the raw item
            // is thread-backed; otherwise UnitCost is left at 0 and the
            // buyer fills in the rate on the PR (or it's pulled from the
            // last purchase rate later).
            var perThreadDemand = new List<RawThreadDemand>();
            foreach (var w in woRows)
            {
                if (!w.DesignId.HasValue || (w.OrderQty ?? 0) <= 0) continue;
                int designId = w.DesignId.Value;
                decimal woQty = (decimal)(w.OrderQty ?? 0);

                var slice =
                   (from bom in db.Gen_BOM
                    join cm in db.Gen_ComponentMaterials on bom.CompId equals cm.CompId
                    join t in db.Gen_Threads on cm.RawItemId equals t.RawItemId into tj
                    from t in tj.DefaultIfEmpty()
                    where bom.DesignId == designId
                       && (bom.Deleted == false || bom.Deleted == null)
                       && (cm.Deleted  == false)
                       && (cm.Active   == true)
                       && (t == null || (t.Deleted != true && (t.Active == true || t.Active == null)))
                    select new {
                        RawItemId  = cm.RawItemId,
                        Usage      = cm.QtyPerUnit,
                        UnitCost   = t != null ? (t.UnitCost ?? 0m) : 0m,
                        SupplierId = t != null ? t.SupplierId : (int?)null
                    }).ToList();

                foreach (var s in slice)
                {
                    perThreadDemand.Add(new RawThreadDemand {
                        RawItemId  = s.RawItemId,
                        Qty        = s.Usage * woQty,
                        UnitCost   = s.UnitCost,
                        SupplierId = s.SupplierId
                    });
                }
            }

            if (perThreadDemand.Count == 0)
            { result.SkipReason = "No BOM demand resolved for these WOs."; return result; }

            // ---- (3) Roll up to raw-item granularity --------------------------
            // For each raw item: total required qty, weighted-avg unit cost,
            // most common supplier id (mode).
            var perRawItem = perThreadDemand
                .GroupBy(x => x.RawItemId)
                .Select(g => new {
                    RawItemId  = g.Key,
                    Required   = Math.Round(g.Sum(x => x.Qty), 2),
                    AvgRate    = g.Sum(x => x.Qty) > 0m
                                   ? Math.Round(g.Sum(x => x.Qty * x.UnitCost) / g.Sum(x => x.Qty), 2)
                                   : 0m,
                    ModeSupId  = ModeSupplier(g.Select(x => x.SupplierId))
                })
                .Where(x => x.Required > 0m)
                .ToList();

            // ---- (4) Subtract on-hand + pending procurement -------------------
            int branchForLookup = sharedBranchId ?? 0;
            var shortfalls = new List<ShortfallLine>();

            // Resolve UnitId per raw item up front so the PR-item INSERT below
            // can carry it through. Without this the PR profile shows a blank
            // Unit column until the buyer manually edits each row.
            var rawIdsForUnit = perRawItem.Select(x => x.RawItemId).Distinct().ToList();
            // UnitId on Gen_RawItems is non-nullable on the EF model, so
            // the dictionary value type is int (not int?). The shortfall
            // line carries int? so we cast on the way out.
            Dictionary<int, int> unitByRaw;
            if (rawIdsForUnit.Count == 0)
                unitByRaw = new Dictionary<int, int>();
            else
                unitByRaw = db.Gen_RawItems
                    .Where(r => rawIdsForUnit.Contains(r.Id))
                    .Select(r => new { r.Id, r.UnitId })
                    .ToDictionary(x => x.Id, x => x.UnitId);

            foreach (var d in perRawItem)
            {
                decimal onHand    = OnHand(db, d.RawItemId, branchForLookup);
                decimal pending   = PendingProcurement(db, d.RawItemId);
                decimal shortQty  = d.Required - onHand - pending;
                if (shortQty > 0m)
                {
                    // BOM thread cost first; if that's 0 (typical for fabric
                    // and label raw items that aren't Gen_Threads-backed),
                    // fall back to the most recent stock IN cost, then the
                    // last PO rate. 0 ultimately means "buyer keys it."
                    decimal rate = d.AvgRate > 0m ? d.AvgRate : FallbackRate(db, d.RawItemId);

                    shortfalls.Add(new ShortfallLine {
                        RawItemId  = d.RawItemId,
                        Qty        = Math.Round(shortQty, 2),
                        Rate       = rate,
                        SupplierId = d.ModeSupId,
                        UnitId     = unitByRaw.ContainsKey(d.RawItemId) ? (int?)unitByRaw[d.RawItemId] : null
                    });
                }
            }

            if (shortfalls.Count == 0)
            { result.SkipReason = "No shortfall after on-hand and pending procurement subtracted."; return result; }

            // ---- (5) Decide single-supplier vs mixed --------------------------
            // Auto-approval requires every line to share the same supplier
            // and rate > 0 (so a valid PO can be created in one shot).
            int? unifiedSupplierId = null;
            var distinctSuppliers = shortfalls
                .Select(s => s.SupplierId)
                .Where(s => s.HasValue)
                .Distinct()
                .ToList();
            if (distinctSuppliers.Count == 1
                && shortfalls.All(s => s.SupplierId == distinctSuppliers[0]))
            {
                unifiedSupplierId = distinctSuppliers[0];
            }

            // NOTE: auto-approve is intentionally disabled.
            // Previous behavior auto-flipped the PR to APPROVED and even
            // created a PO when supplier+rate conditions were met. Users
            // asked for full review/edit control — every auto-generated PR
            // now lands as DRAFT and the buyer drives Submit + Approve.
            // The `canAutoApprove` parameter is kept on the signature for
            // back-compat but is no longer consulted.

            // ---- (6) INSERT PR header (always DRAFT) -------------------------
            string prCode = NextPrCode(db);
            string headerRemarks =
                (sourceType == "PLAN_APPROVE" ? "Auto-created on Plan Approve: " : "Auto-created on Work Order post: ")
                + (sourceLabel ?? "");

            int prId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.PurchaseRequisition
       (PrCode, BranchId, RequestDate, SupplierId, SourceType, SourceReferenceId,
        Status, Remarks, RequestedBy,
        Active, Deleted, CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code,   @br,     @now,        @sup,       @srcType,   @srcRefId,
        'DRAFT', @rmk,    @uid,
        1,      0,        @uid,      @now,        @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new SqlParameter("@code",     prCode),
                new SqlParameter("@br",       (object)sharedBranchId ?? DBNull.Value),
                new SqlParameter("@now",      now),
                new SqlParameter("@sup",      (object)unifiedSupplierId ?? DBNull.Value),
                new SqlParameter("@srcType",  sourceType ?? "AUTO"),
                new SqlParameter("@srcRefId", sourceReferenceId),
                new SqlParameter("@rmk",      (object)headerRemarks ?? DBNull.Value),
                new SqlParameter("@uid",      userId)
            ).First();

            // INSERT PR items with unit + rate + amount
            foreach (var s in shortfalls)
            {
                decimal amount = s.Qty * s.Rate;
                string lineRemarks = "Auto: shortfall for " + (sourceLabel ?? "");
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseRequisitionItems
       (PrId, RawItemId, UnitId, Qty, EstimatedRate, EstimatedAmount, Remarks,
        Active, Deleted, CreatedBy, CreatedDate)
VALUES (@prId, @raw,    @unit,  @qty, @rate,         @amt,            @rmk,
        1,    0,        @uid,   @now)",
                    new SqlParameter("@prId", prId),
                    new SqlParameter("@raw",  s.RawItemId),
                    new SqlParameter("@unit", (object)s.UnitId ?? DBNull.Value),
                    new SqlParameter("@qty",  s.Qty),
                    new SqlParameter("@rate", s.Rate),
                    new SqlParameter("@amt",  amount),
                    new SqlParameter("@rmk",  lineRemarks),
                    new SqlParameter("@uid",  userId),
                    new SqlParameter("@now",  now));
            }

            result.PrId      = prId;
            result.PrCode    = prCode;
            result.ItemCount = shortfalls.Count;
            // AutoApproved / PoCode are left at default (false / null) —
            // the buyer drives the Submit + Approve cycle from the PR Edit
            // modal, where the PO is created on the manual Approve action.

            return result;
        }

        // ---- Pending procurement: open PR + open PO unreceived qty -----------
        // Counts every PR item still in DRAFT/SUBMITTED (not yet approved
        // into a PO) plus every PO item that hasn't been fully received yet
        // (OrderedQty - ReceivedQty). Together: "what's already in motion
        // for this raw item." Subtracted from required so we don't double-
        // order what suppliers will be delivering soon.
        private static decimal PendingProcurement(
            EMB_Production.Models.EMB_ProductionEntities1 db,
            int rawItemId)
        {
            decimal openPrQty = db.Database.SqlQuery<decimal?>(@"
SELECT ISNULL(SUM(pri.Qty), 0)
FROM   dbo.PurchaseRequisitionItems pri
JOIN   dbo.PurchaseRequisition pr ON pr.Id = pri.PrId
WHERE  pri.RawItemId = @raw
   AND (pri.Deleted = 0 OR pri.Deleted IS NULL)
   AND (pr.Deleted  = 0 OR pr.Deleted  IS NULL)
   AND pr.Status IN ('DRAFT', 'SUBMITTED')",
                new SqlParameter("@raw", rawItemId)).FirstOrDefault() ?? 0m;

            decimal openPoUnreceived = db.Database.SqlQuery<decimal?>(@"
SELECT ISNULL(SUM(poi.OrderedQty - ISNULL(poi.ReceivedQty, 0)), 0)
FROM   dbo.PurchaseOrderItems poi
JOIN   dbo.PurchaseOrder po ON po.Id = poi.PoId
WHERE  poi.RawItemId = @raw
   AND (poi.Deleted = 0 OR poi.Deleted IS NULL)
   AND (po.Deleted  = 0 OR po.Deleted  IS NULL)
   AND po.Status IN ('DRAFT', 'SENT', 'PARTIALLY_RECEIVED')",
                new SqlParameter("@raw", rawItemId)).FirstOrDefault() ?? 0m;

            return Math.Max(0m, openPrQty) + Math.Max(0m, openPoUnreceived);
        }

        private static decimal OnHand(
            EMB_Production.Models.EMB_ProductionEntities1 db,
            int rawItemId, int branchId)
        {
            var last = db.Gen_Store
                .Where(s => s.ItemId == rawItemId
                         && s.BranchId == branchId
                         && s.Deleted == false)
                .OrderByDescending(s => s.Id)
                .Select(s => (decimal?)s.CurrentQty)
                .FirstOrDefault();
            return last ?? 0m;
        }

        private static int? ModeSupplier(IEnumerable<int?> supplierIds)
        {
            var groups = supplierIds
                .Where(x => x.HasValue)
                .GroupBy(x => x.Value)
                .OrderByDescending(g => g.Count())
                .ToList();
            return groups.Count == 0 ? (int?)null : (int?)groups[0].Key;
        }

        private static string NextPrCode(EMB_Production.Models.EMB_ProductionEntities1 db)
        {
            string year = DateTime.Now.Year.ToString();
            string last = db.Database.SqlQuery<string>(@"
SELECT TOP 1 PrCode FROM dbo.PurchaseRequisition
WHERE PrCode LIKE 'PR-' + @year + '-%'
ORDER BY Id DESC",
                new SqlParameter("@year", year)).FirstOrDefault();
            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int parsed))
                    next = parsed + 1;
            }
            return "PR-" + year + "-" + next.ToString("D4");
        }

        private static string NextPoCode(EMB_Production.Models.EMB_ProductionEntities1 db)
        {
            string year = DateTime.Now.Year.ToString();
            string last = db.Database.SqlQuery<string>(@"
SELECT TOP 1 PoCode FROM dbo.PurchaseOrder
WHERE PoCode LIKE 'PO-' + @year + '-%'
ORDER BY Id DESC",
                new SqlParameter("@year", year)).FirstOrDefault();
            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int parsed))
                    next = parsed + 1;
            }
            return "PO-" + year + "-" + next.ToString("D4");
        }

        private class RawThreadDemand
        {
            public int RawItemId;
            public decimal Qty;
            public decimal UnitCost;
            public int? SupplierId;
        }

        private class ShortfallLine
        {
            public int RawItemId;
            public decimal Qty;
            public decimal Rate;
            public int? SupplierId;
            public int? UnitId;          // populated from Gen_RawItems.UnitId so PR items get the right Unit
        }

        /** Fallback rate when the BOM-derived UnitCost is 0 (typical
         *  for non-thread raw items — fabrics, labels). Walks four
         *  historical sources in order of authority:
         *   1. PurchaseItems.Rate     — last actually-paid (GRN) rate
         *   2. PurchaseOrderItems.Rate — last PO line rate
         *   3. Gen_Stocks.Rate (IN)    — last internal stock receipt rate
         *   4. Gen_Stocks.LastKnownRate — denormalized standard, survives
         *      even after the source IN row is purged.
         *  Returns 0 when nothing exists — buyer keys it manually. */
        private static decimal FallbackRate(
            EMB_Production.Models.EMB_ProductionEntities1 db,
            int rawItemId)
        {
            decimal r;

            r = SafeRateLookup(db, @"
SELECT TOP 1 Rate FROM dbo.PurchaseItems
WHERE  ItemId = @id AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            r = SafeRateLookup(db, @"
SELECT TOP 1 Rate FROM dbo.PurchaseOrderItems
WHERE  RawItemId = @id AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            r = SafeRateLookup(db, @"
SELECT TOP 1 Rate FROM dbo.Gen_Stocks
WHERE  ItemId = @id AND TransactionType = 'IN' AND ISNULL(Rate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            if (r > 0m) return r;

            r = SafeRateLookup(db, @"
SELECT TOP 1 LastKnownRate FROM dbo.Gen_Stocks
WHERE  ItemId = @id AND ISNULL(LastKnownRate, 0) > 0
   AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC", rawItemId);
            return r;
        }

        private static decimal SafeRateLookup(
            EMB_Production.Models.EMB_ProductionEntities1 db,
            string sql, int rawItemId)
        {
            try
            {
                var v = db.Database.SqlQuery<decimal?>(sql,
                    new SqlParameter("@id", rawItemId)).FirstOrDefault();
                return v.HasValue && v.Value > 0m ? v.Value : 0m;
            }
            catch { return 0m; }
        }
    }
}


