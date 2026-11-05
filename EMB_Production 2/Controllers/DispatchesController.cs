using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical Dispatch status / sale-type / dispatch-type
    //  values. Names match the strings already stored in the DB so this
    //  is a drop-in refactor; the constants stop a typo in one branch
    //  from silently breaking a filter.
    // ─────────────────────────────────────────────────────────────────
    public static class DispatchStatus
    {
        public const string DRAFT     = "DRAFT";
        public const string POSTED    = "POSTED";
        public const string REVERSED  = "REVERSED";
        public const string CANCELLED = "CANCELLED";
    }

    public static class DispatchSaleType
    {
        public const string CUSTOMER_DEMAND = "CUSTOMER_DEMAND";
        public const string DIRECT_SALE     = "DIRECT_SALE";

        public static readonly string[] All = new[] { CUSTOMER_DEMAND, DIRECT_SALE };
    }

    public static class DispatchTypeConst
    {
        public const string SALES = "SALES";
    }

    [RoutePrefix("emb-products/dispatch")]
    public class DispatchesController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  WORKFLOW STEPPER
        //  Three-stage canonical pipeline — REVERSED sits as a red
        //  sibling at the POSTED slot.
        // ─────────────────────────────────────────────────────────────
        private static readonly string[] _dispatchWorkflowStages = new[]
        {
            DispatchStatus.DRAFT,
            DispatchStatus.POSTED,
            DispatchStatus.REVERSED
        };

        private static int ComputeDispatchWorkflowStageIndex(bool isPosted, bool isReversed)
        {
            if (isReversed) return 2;
            if (isPosted)   return 1;
            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table for every state-changing event on a
        //  Dispatch row: CREATED, EDITED, POSTED, REVERSED, DELETED,
        //  CLONED. Best-effort writes — wrapped in try/catch so a
        //  missing table never blocks the user's main action.
        // ─────────────────────────────────────────────────────────────
        private void LogDispatchAudit(int dispatchId, string action,
                                      string oldValue = null,
                                      string newValue = null,
                                      string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.DispatchAuditLog
       (DispatchId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@did, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@did",  dispatchId),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@uid",  GetSafeCurrentUserId()),
                    new SqlParameter("@when", DateTime.Now),
                    new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
            }
            catch
            {
                // Observability, not a hard requirement.
            }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var entries = db.Database.SqlQuery<DispatchAuditEntry>(@"
SELECT  a.Id,
        a.DispatchId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.DispatchAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.DispatchId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id              = e.Id,
                    dispatchId      = e.DispatchId,
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
                return JsonError("Failed to load audit log. " + DescribeDispatchError(ex));
            }
        }

        private class DispatchAuditEntry
        {
            public int Id { get; set; }
            public int DispatchId { get; set; }
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
        // ─────────────────────────────────────────────────────────────
        private static string DescribeDispatchError(Exception ex)
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
        //  Sprint-4  TOCTOU LOCK — serialise concurrent Post / Reverse
        //  on the same Dispatch row.
        // ─────────────────────────────────────────────────────────────
        private void LockDispatchRow(int dispatchId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT 1 FROM dbo.Dispatch WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                new SqlParameter("@id", dispatchId));
        }
        [HttpGet, Route("list")]
        public ActionResult List(
            string search = "",
            string activeFilter = "",
            string showDeleted = "",
            int page = 1,
            int pageSize = 10,
            string fromDate = null,
            string toDate = null,
            int? customerId = null,
            int? jobOrderId = null,
            int? branchId = null,
            string posted = null,
            string dispatchType = null,
            string saleType = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 10;

                // Phase O.0 - parse date range filters
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string postedUp       = (posted ?? "").Trim().ToUpper();
                string dispatchTypeUp = (dispatchType ?? "").Trim().ToUpper();
                string saleTypeUp     = (saleType ?? "").Trim().ToUpper();

                var q = db.Dispatch
                    .Include("Hr_Customers")
                    .AsQueryable();

                if (string.IsNullOrEmpty(showDeleted))
                    q = q.Where(x => x.Deleted != true);

                if (activeFilter == "active")
                    q = q.Where(x => x.Active == true);
                else if (activeFilter == "inactive")
                    q = q.Where(x => x.Active == false || x.Active == null);

                // Phase O.0 - new filter dimensions
                if (fromDt.HasValue) q = q.Where(x => x.DispatchDate >= fromDt.Value);
                if (toDt.HasValue)   q = q.Where(x => x.DispatchDate <= toDt.Value);
                if (customerId.HasValue && customerId.Value > 0) q = q.Where(x => x.CustomerId == customerId.Value);
                if (jobOrderId.HasValue && jobOrderId.Value > 0) q = q.Where(x => x.JobOrderId == jobOrderId.Value);
                if (branchId.HasValue && branchId.Value > 0)     q = q.Where(x => x.BranchId == branchId.Value);
                if (postedUp == "POSTED")        q = q.Where(x => x.IsPosted == true && x.IsReversed != true);
                else if (postedUp == "DRAFT")    q = q.Where(x => x.IsPosted != true);
                else if (postedUp == "REVERSED") q = q.Where(x => x.IsReversed == true);
                if (!string.IsNullOrEmpty(dispatchTypeUp))
                    q = q.Where(x => x.DispatchType != null && x.DispatchType.ToUpper() == dispatchTypeUp);
                if (!string.IsNullOrEmpty(saleTypeUp))
                    q = q.Where(x => x.SaleType != null && x.SaleType.ToUpper() == saleTypeUp);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    q = q.Where(d =>
                        (d.VehicleNo ?? "").Contains(search) ||
                        (d.DriverName ?? "").Contains(search) ||
                        (d.DispatchType ?? "").Contains(search) ||
                        (d.SaleType ?? "").Contains(search) ||
                        (d.DirectCustomerName ?? "").Contains(search) ||
                        (d.DirectCustomerMobile ?? "").Contains(search) ||
                        (d.Hr_Customers != null &&
                         d.Hr_Customers.Customer_Name.Contains(search)));
                }

                var total = q.Count();

                var rows = q
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var dispatchIds = rows.Select(x => x.Id).ToList();

                var itemAgg = db.DispatchItems
                    .Where(x =>
                        dispatchIds.Contains(x.DispatchId) &&
                        x.Deleted != true)
                    .GroupBy(x => x.DispatchId)
                    .Select(g => new
                    {
                        DispatchId = g.Key,
                        ItemCount = g.Count(),
                        DispatchQtyTotal = g.Sum(x => (decimal?)x.DispatchQty) ?? 0m,

                        SalePrice = g.Select(x => (decimal?)x.SalePrice)
                            .FirstOrDefault() ?? 0m,

                        TotalAmount = g.Sum(x =>
                            (decimal?)((x.DispatchQty ?? 0m) * (x.SalePrice ?? 0m))
                        ) ?? 0m,

                        // Cost: per-line CostPrice locked at post time;
                        // TotalCost = sum(qty × cost) across all lines.
                        // Margin = TotalAmount - TotalCost. Per-pc cost is
                        // a weighted average for display.
                        TotalCost = g.Sum(x =>
                            (decimal?)((x.DispatchQty ?? 0m) * (x.CostPrice ?? 0m))
                        ) ?? 0m
                    })
                    .ToList();

                var productAgg =
                    (from di in db.DispatchItems
                     join p in db.Gen_Products on di.ProductId equals p.Id
                     where dispatchIds.Contains(di.DispatchId) && di.Deleted != true
                     orderby di.Id
                     select new { di.DispatchId, p.Prod_Name })
                    .ToList()
                    .GroupBy(x => x.DispatchId)
                    .ToDictionary(
                        g => g.Key,
                        g =>
                        {
                            var names = g.Select(x => x.Prod_Name).Distinct().ToList();
                            return new { First = names.FirstOrDefault(), Distinct = names.Count };
                        });

                var items =
                    (
                        from d in rows
                        join ia in itemAgg
                            on d.Id equals ia.DispatchId into iaJoin
                        from ia in iaJoin.DefaultIfEmpty()

                        let reversed = d.IsReversed == true
                        let pa = productAgg.ContainsKey(d.Id) ? productAgg[d.Id] : null
                        let productName = pa == null
                            ? "-"
                            : pa.Distinct > 1
                                ? pa.First + " (+" + (pa.Distinct - 1) + ")"
                                : (pa.First ?? "-")

                        select new
                        {
                            id = d.Id,

                            dispatchDate = d.DispatchDate,
                            dispatchDateText = d.DispatchDate.ToString("dd-MMM-yyyy"),

                            productName = productName,

                            dispatchType = string.IsNullOrWhiteSpace(d.DispatchType)
                                ? "SALES"
                                : d.DispatchType,

                            saleType = string.IsNullOrWhiteSpace(d.SaleType)
                                ? "CUSTOMER_DEMAND"
                                : d.SaleType,

                            saleTypeText =
                                string.IsNullOrWhiteSpace(d.SaleType)
                                    ? "Customer Demand"
                                    : d.SaleType == "DIRECT_SALE"
                                        ? "Direct Sale"
                                        : "Customer Demand",

                            jobOrderId = d.JobOrderId,
                            customerId = d.CustomerId,

                            customerName =
                                !string.IsNullOrWhiteSpace(d.DirectCustomerName)
                                    ? d.DirectCustomerName
                                    : (d.Hr_Customers != null
                                        ? d.Hr_Customers.Customer_Name
                                        : "-"),

                            directCustomerName = d.DirectCustomerName,
                            directCustomerMobile = d.DirectCustomerMobile,
                            directCustomerAddress = d.DirectCustomerAddress,

                            branchId = d.BranchId,

                            vehicleNo = d.VehicleNo,
                            driverName = d.DriverName,

                            itemCount = ia != null ? ia.ItemCount : 0,
                            dispatchQtyTotal = ia != null ? ia.DispatchQtyTotal : 0m,

                            salePrice = reversed ? 0m : (ia != null ? ia.SalePrice : 0m),
                            totalAmount = reversed ? 0m : (ia != null ? ia.TotalAmount : 0m),

                            // Cost + margin (reversed dispatches show zeroes)
                            totalCost = reversed ? 0m : (ia != null ? ia.TotalCost : 0m),
                            costPerPc = reversed || ia == null || ia.DispatchQtyTotal <= 0m
                                ? 0m
                                : Math.Round(ia.TotalCost / ia.DispatchQtyTotal, 2),
                            totalMargin = reversed || ia == null
                                ? 0m
                                : (ia.TotalAmount - ia.TotalCost),
                            marginPct = reversed || ia == null || ia.TotalCost <= 0m
                                ? 0m
                                : Math.Round(((ia.TotalAmount - ia.TotalCost) / ia.TotalCost) * 100m, 2),
                            isLoss = !reversed && ia != null && ia.TotalCost > 0m && ia.TotalAmount < ia.TotalCost,

                            stockStatus = reversed
                                ? "Reversed"
                                : d.IsPosted ? "Posted" : "Pending",

                            active = d.Active == true,
                            isPosted = d.IsPosted,
                            isReversed = d.IsReversed,

                            statusText = reversed
                                ? "Reversed"
                                : d.IsPosted
                                    ? "Posted"
                                    : "Draft",

                            // Sprint-1: row created within last 24h —
                            // drives the lightning bolt badge on the
                            // date cell.
                            isRecent = d.CreatedDate.HasValue
                                       && (DateTime.Now - d.CreatedDate.Value).TotalHours < 24
                        }
                    ).ToList();

                // Phase O.0 - summary aggregates across filtered set
                var summaryHeaders = q.Select(x => new
                {
                    x.Id,
                    x.IsPosted,
                    x.IsReversed,
                    x.DispatchType,
                    x.SaleType
                }).ToList();

                var allSummaryIds = summaryHeaders.Select(x => x.Id).ToList();
                var allItemAggs = db.DispatchItems
                    .Where(i => allSummaryIds.Contains(i.DispatchId) && i.Deleted != true)
                    .GroupBy(i => i.DispatchId)
                    .Select(g => new
                    {
                        DispatchId = g.Key,
                        Qty = g.Sum(i => (decimal?)i.DispatchQty) ?? 0m,
                        Amount = g.Sum(i => (decimal?)((i.DispatchQty ?? 0m) * (i.SalePrice ?? 0m))) ?? 0m
                    })
                    .ToList();

                decimal totalDispatchQty = allItemAggs.Sum(x => x.Qty);
                decimal totalDispatchAmount = allSummaryIds
                    .Where(id => summaryHeaders.First(h => h.Id == id).IsReversed != true)
                    .Sum(id => allItemAggs.Where(a => a.DispatchId == id).Sum(a => a.Amount));

                var summary = new
                {
                    count = summaryHeaders.Count,
                    totalQty = totalDispatchQty,
                    totalAmount = Math.Round(totalDispatchAmount, 2),
                    postedCount   = summaryHeaders.Count(s => s.IsPosted && s.IsReversed != true),
                    draftCount    = summaryHeaders.Count(s => !s.IsPosted),
                    reversedCount = summaryHeaders.Count(s => s.IsReversed),
                    byDispatchType = summaryHeaders
                        .Where(s => !string.IsNullOrWhiteSpace(s.DispatchType))
                        .GroupBy(s => s.DispatchType.Trim())
                        .Select(g => new { type = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList(),
                    bySaleType = summaryHeaders
                        .Where(s => !string.IsNullOrWhiteSpace(s.SaleType))
                        .GroupBy(s => s.SaleType.Trim())
                        .Select(g => new { saleType = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                };

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        summary,
                        page,
                        pageSize,
                        total,
                        totalItems = total,
                        totalPages = (int)Math.Ceiling(total / (double)pageSize)
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load dispatch list: " + ex.Message);
            }
        }

        // ===================== CREATE =====================
        [HttpPost, Route("create")]
        public ActionResult Create(DispatchDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid dispatch data.");

                    if (dto.DispatchDate == default(DateTime))
                        return JsonError("Dispatch date is required.");

                    if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                        dto.BranchId = GetCurrentBranchId();

                    if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                        return JsonError("Branch is required.");

                    string saleType = string.IsNullOrWhiteSpace(dto.SaleType)
                        ? "CUSTOMER_DEMAND"
                        : dto.SaleType.Trim().ToUpper();

                    if (saleType != "CUSTOMER_DEMAND" && saleType != "DIRECT_SALE")
                        return JsonError("Invalid sale type.");

                    if (saleType == "CUSTOMER_DEMAND")
                    {
                        if (!dto.JobOrderId.HasValue || dto.JobOrderId.Value <= 0)
                            return JsonError("Job Order is required for customer demand dispatch.");

                        if (!dto.CustomerId.HasValue || dto.CustomerId.Value <= 0)
                            return JsonError("Customer is required for customer demand dispatch.");

                        bool jobOrderExists = db.Gen_JobOrders.Any(x =>
                            x.Id == dto.JobOrderId.Value &&
                            x.CustomerId == dto.CustomerId.Value &&
                            x.BranchId == dto.BranchId.Value &&
                            x.Deleted != true &&
                            x.Active == true);

                        if (!jobOrderExists)
                            return JsonError("Selected Job Order does not match customer/branch or is inactive.");
                    }

                    if (saleType == "DIRECT_SALE")
                    {
                        dto.JobOrderId = null;

                        if ((!dto.CustomerId.HasValue || dto.CustomerId.Value <= 0) &&
                            string.IsNullOrWhiteSpace(dto.DirectCustomerName))
                        {
                            return JsonError("Customer or walk-in customer name is required for direct sale.");
                        }
                    }

                    var validItems = (dto.Items ?? new List<DispatchItemDto>())
                        .Where(x => x != null && x.ProductId > 0 && x.DispatchQty > 0)
                        .ToList();

                    if (!validItems.Any())
                        return JsonError("At least one product is required.");

                    foreach (var it in validItems)
                    {
                        decimal balance = GetDispatchAvailableQty(
                            it.ProductId,
                            dto.BranchId.Value,
                            saleType,
                            0,
                            dto.CustomerId
                        );

                        if (it.DispatchQty > balance)
                        {
                            return JsonError(
                                saleType == "DIRECT_SALE"
                                    ? "Dispatch quantity exceeds free company stock (pending drafts already reserved)."
                                    : "Dispatch quantity exceeds customer reserved stock (pending drafts already reserved)."
                            );
                        }
                    }

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var entity = new Dispatch
                    {
                        DispatchDate = dto.DispatchDate,
                        DispatchType = "SALES",
                        SaleType = saleType,

                        JobOrderId = saleType == "CUSTOMER_DEMAND" ? dto.JobOrderId : null,
                        CustomerId = dto.CustomerId.HasValue && dto.CustomerId.Value > 0
                            ? dto.CustomerId
                            : null,

                        DirectCustomerName = saleType == "DIRECT_SALE"
                            ? (dto.DirectCustomerName ?? "").Trim()
                            : null,

                        DirectCustomerMobile = saleType == "DIRECT_SALE"
                            ? (dto.DirectCustomerMobile ?? "").Trim()
                            : null,

                        DirectCustomerAddress = saleType == "DIRECT_SALE"
                            ? (dto.DirectCustomerAddress ?? "").Trim()
                            : null,

                        DepartmentId = null,
                        BranchId = dto.BranchId.Value,

                        VehicleNo = dto.VehicleNo ?? "",
                        DriverName = dto.DriverName ?? "",
                        Remarks = dto.Remarks ?? "",

                        Active = true,
                        Deleted = false,
                        IsPosted = false,
                        IsReversed = false,

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,
                        RecordTimeStamp = now
                    };

                    db.Dispatch.Add(entity);
                    db.SaveChanges();

                    foreach (var it in validItems)
                    {
                        decimal balance = GetDispatchAvailableQty(
                            it.ProductId,
                            dto.BranchId.Value,
                            saleType
                        );

                        decimal qty = it.DispatchQty;

                        decimal costPrice = it.CostPrice;
                        decimal baseSalePrice = it.BaseSalePrice;
                        decimal discountPercent = it.DiscountPercent < 0m ? 0m : it.DiscountPercent;
                        decimal discountAmount = it.DiscountAmount < 0m ? 0m : it.DiscountAmount;

                        if (costPrice <= 0m)
                            costPrice = GetProductCostPrice(it.ProductId, dto.BranchId.Value);

                        if (baseSalePrice <= 0m)
                            baseSalePrice = GetCustomerBaseSalePrice(
                                it.ProductId,
                                dto.BranchId.Value,
                                dto.CustomerId
                            );

                        decimal salePrice = it.SalePrice;

                        if (salePrice <= 0m && baseSalePrice > 0m)
                        {
                            salePrice = baseSalePrice;

                            if (discountPercent > 0m)
                                salePrice -= baseSalePrice * discountPercent / 100m;

                            if (discountAmount > 0m)
                                salePrice -= discountAmount;
                        }

                        if (salePrice < 0m)
                            salePrice = 0m;

                        decimal amount = qty * salePrice;

                        db.DispatchItems.Add(new DispatchItems
                        {
                            DispatchId = entity.Id,
                            ProductId = it.ProductId,
                            BranchId = dto.BranchId.Value,

                            DispatchQty = qty,
                            AvailableQtySnapshot = balance,

                            CostPrice = costPrice,
                            BaseSalePrice = baseSalePrice,
                            DiscountPercent = discountPercent,
                            DiscountAmount = discountAmount,
                            SalePrice = salePrice,
                            Amount = amount,

                            Unit = string.IsNullOrWhiteSpace(it.Unit) ? "PCS" : it.Unit,
                            Remarks = it.Remarks ?? "",

                            IsPosted = false,
                            Active = true,
                            Deleted = false,

                            CreatedBy = userId,
                            CreatedDate = now,
                            UpdatedBy = userId,
                            UpdatedDate = now,
                            RecordTimeStamp = now
                        });
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogDispatchAudit(entity.Id, "CREATED",
                        oldValue: null,
                        newValue: DispatchStatus.DRAFT,
                        detail: "Sale type: " + (entity.SaleType ?? "-"));

                    return JsonSuccess("Dispatch created successfully.", new
                    {
                        id = entity.Id,
                        saleType = entity.SaleType
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Dispatch create failed: " + DescribeDispatchError(ex));
                }
            }
        }

        // ===================== UPDATE =====================
        [HttpPost, Route("update")]
        public ActionResult Update(DispatchDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null || dto.Id <= 0)
                        return JsonError("Invalid dispatch data.");

                    var dispatch = db.Dispatch
                        .FirstOrDefault(x => x.Id == dto.Id && x.Deleted != true);

                    if (dispatch == null)
                        return JsonError("Dispatch not found.");

                    if (dispatch.IsPosted)
                        return JsonError("Posted dispatch cannot be modified.");

                    if (dispatch.IsReversed)
                        return JsonError("Reversed dispatch cannot be modified.");

                    if (dto.DispatchDate == default(DateTime))
                        return JsonError("Dispatch date is required.");

                    if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                        dto.BranchId = GetCurrentBranchId();

                    if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                        return JsonError("Branch is required.");

                    string saleType = string.IsNullOrWhiteSpace(dto.SaleType)
                        ? "CUSTOMER_DEMAND"
                        : dto.SaleType.Trim().ToUpper();

                    if (saleType != "CUSTOMER_DEMAND" && saleType != "DIRECT_SALE")
                        return JsonError("Invalid sale type.");

                    if (saleType == "CUSTOMER_DEMAND")
                    {
                        if (!dto.JobOrderId.HasValue || dto.JobOrderId.Value <= 0)
                            return JsonError("Job Order is required for customer demand dispatch.");

                        if (!dto.CustomerId.HasValue || dto.CustomerId.Value <= 0)
                            return JsonError("Customer is required for customer demand dispatch.");

                        bool jobOrderExists = db.Gen_JobOrders.Any(x =>
                            x.Id == dto.JobOrderId.Value &&
                            x.CustomerId == dto.CustomerId.Value &&
                            x.BranchId == dto.BranchId.Value &&
                            x.Deleted != true &&
                            x.Active == true);

                        if (!jobOrderExists)
                            return JsonError("Selected Job Order does not match customer/branch or is inactive.");
                    }

                    if (saleType == "DIRECT_SALE")
                    {
                        dto.JobOrderId = null;

                        if ((!dto.CustomerId.HasValue || dto.CustomerId.Value <= 0) &&
                            string.IsNullOrWhiteSpace(dto.DirectCustomerName))
                        {
                            return JsonError("Customer or walk-in customer name is required for direct sale.");
                        }
                    }

                    var validItems = (dto.Items ?? new List<DispatchItemDto>())
                        .Where(x => x != null && x.ProductId > 0 && x.DispatchQty > 0)
                        .ToList();

                    if (!validItems.Any())
                        return JsonError("At least one product is required.");

                    foreach (var it in validItems)
                    {
                        decimal balance = GetDispatchAvailableQty(
                            it.ProductId,
                            dto.BranchId.Value,
                            saleType,
                            dispatch.Id,
                            dto.CustomerId
                        );

                        if (it.DispatchQty > balance)
                        {
                            return JsonError(
                                saleType == "DIRECT_SALE"
                                    ? "Dispatch quantity exceeds free company stock (pending drafts already reserved)."
                                    : "Dispatch quantity exceeds customer reserved stock (pending drafts already reserved)."
                            );
                        }
                    }

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    dispatch.DispatchDate = dto.DispatchDate;
                    dispatch.DispatchType = "SALES";
                    dispatch.SaleType = saleType;

                    dispatch.JobOrderId = saleType == "CUSTOMER_DEMAND" ? dto.JobOrderId : null;

                    dispatch.CustomerId = dto.CustomerId.HasValue && dto.CustomerId.Value > 0
                        ? dto.CustomerId
                        : null;

                    dispatch.DepartmentId = null;

                    dispatch.DirectCustomerName = saleType == "DIRECT_SALE"
                        ? (dto.DirectCustomerName ?? "").Trim()
                        : null;

                    dispatch.DirectCustomerMobile = saleType == "DIRECT_SALE"
                        ? (dto.DirectCustomerMobile ?? "").Trim()
                        : null;

                    dispatch.DirectCustomerAddress = saleType == "DIRECT_SALE"
                        ? (dto.DirectCustomerAddress ?? "").Trim()
                        : null;

                    dispatch.BranchId = dto.BranchId.Value;
                    dispatch.VehicleNo = dto.VehicleNo ?? "";
                    dispatch.DriverName = dto.DriverName ?? "";
                    dispatch.Remarks = dto.Remarks ?? "";
                    dispatch.Active = dto.Active;

                    dispatch.UpdatedBy = userId;
                    dispatch.UpdatedDate = now;
                    dispatch.RecordTimeStamp = now;

                    var oldItems = db.DispatchItems
                        .Where(x => x.DispatchId == dispatch.Id && x.Deleted != true)
                        .ToList();

                    foreach (var old in oldItems)
                    {
                        old.Deleted = true;
                        old.Active = false;
                        old.UpdatedBy = userId;
                        old.UpdatedDate = now;
                        old.RecordTimeStamp = now;
                    }

                    foreach (var it in validItems)
                    {
                        decimal balance = GetDispatchAvailableQty(
                            it.ProductId,
                            dto.BranchId.Value,
                            saleType
                        );

                        decimal qty = it.DispatchQty;

                        decimal costPrice = it.CostPrice;
                        decimal baseSalePrice = it.BaseSalePrice;
                        decimal discountPercent = it.DiscountPercent < 0m ? 0m : it.DiscountPercent;
                        decimal discountAmount = it.DiscountAmount < 0m ? 0m : it.DiscountAmount;

                        if (costPrice <= 0m)
                            costPrice = GetProductCostPrice(it.ProductId, dto.BranchId.Value);

                        if (baseSalePrice <= 0m)
                            baseSalePrice = GetCustomerBaseSalePrice(
                                it.ProductId,
                                dto.BranchId.Value,
                                dto.CustomerId
                            );

                        decimal salePrice = it.SalePrice;

                        if (salePrice <= 0m && baseSalePrice > 0m)
                        {
                            salePrice = baseSalePrice;

                            if (discountPercent > 0m)
                                salePrice -= baseSalePrice * discountPercent / 100m;

                            if (discountAmount > 0m)
                                salePrice -= discountAmount;
                        }

                        if (salePrice < 0m)
                            salePrice = 0m;

                        decimal amount = qty * salePrice;

                        db.DispatchItems.Add(new DispatchItems
                        {
                            DispatchId = dispatch.Id,
                            ProductId = it.ProductId,
                            BranchId = dto.BranchId.Value,

                            DispatchQty = qty,
                            AvailableQtySnapshot = balance,

                            CostPrice = costPrice,
                            BaseSalePrice = baseSalePrice,
                            DiscountPercent = discountPercent,
                            DiscountAmount = discountAmount,
                            SalePrice = salePrice,
                            Amount = amount,

                            Unit = string.IsNullOrWhiteSpace(it.Unit) ? "PCS" : it.Unit,
                            Remarks = it.Remarks ?? "",

                            IsPosted = false,
                            Active = true,
                            Deleted = false,

                            CreatedBy = userId,
                            CreatedDate = now,
                            UpdatedBy = userId,
                            UpdatedDate = now,
                            RecordTimeStamp = now
                        });
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogDispatchAudit(dto.Id, "EDITED",
                        oldValue: DispatchStatus.DRAFT,
                        newValue: DispatchStatus.DRAFT,
                        detail: (dto.Items?.Count ?? 0) + " line(s)");

                    return JsonSuccess("Dispatch updated successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Dispatch update failed: " + DescribeDispatchError(ex));
                }
            }
        }


        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var row =
                    (from d in db.Dispatch
                     join c in db.Hr_Customers
                         on d.CustomerId equals c.Id into custJoin
                     from c in custJoin.DefaultIfEmpty()

                     join j in db.Gen_JobOrders
                         on d.JobOrderId equals j.Id into jobJoin
                     from j in jobJoin.DefaultIfEmpty()

                     where d.Id == id && (d.Deleted == false || d.Deleted == null)

                     select new
                     {
                         d.Id,
                         d.DispatchDate,
                         d.DispatchType,
                         d.SaleType,

                         d.JobOrderId,
                         JobOrderCode = j != null ? j.JobOrderCode : null,

                         d.CustomerId,
                         CustomerName = c != null ? c.Customer_Name : null,
                         CustomerCode = c != null ? c.Customer_Id : null,

                         d.DirectCustomerName,
                         d.DirectCustomerMobile,
                         d.DirectCustomerAddress,

                         d.DepartmentId,
                         d.BranchId,

                         d.VehicleNo,
                         d.DriverName,
                         d.Remarks,

                         Active = d.Active == true,
                         Deleted = d.Deleted == true,
                         d.IsPosted,
                         d.IsReversed,

                         d.CreatedBy,
                         d.CreatedDate,
                         d.UpdatedBy,
                         d.UpdatedDate,
                         d.RecordTimeStamp,

                         d.PostedBy,
                         d.PostedDate
                     })
                    .FirstOrDefault();

                if (row == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Dispatch not found."
                    }, JsonRequestBehavior.AllowGet);
                }

                string saleType = string.IsNullOrWhiteSpace(row.SaleType)
                    ? "CUSTOMER_DEMAND"
                    : row.SaleType.Trim().ToUpper();

                string saleTypeText = saleType == "DIRECT_SALE"
                    ? "Direct Sale / Walk-in Customer"
                    : "Customer Demand Dispatch";

                string displayCustomerName = !string.IsNullOrWhiteSpace(row.DirectCustomerName)
                    ? row.DirectCustomerName
                    : (!string.IsNullOrWhiteSpace(row.CustomerName) ? row.CustomerName : "-");

                var items = db.DispatchItems
                    .Include("Gen_Products")
                    .Where(x => x.DispatchId == row.Id && x.Deleted != true)
                    .OrderBy(x => x.Id)
                    .ToList()
                    .Select(x => new
                    {
                        id = x.Id,
                        dispatchId = x.DispatchId,

                        productId = x.ProductId ?? 0,
                        productName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : "-",

                        branchId = x.BranchId,
                        availableQtySnapshot = x.AvailableQtySnapshot ?? 0m,

                        dispatchQty = x.DispatchQty ?? 0m,

                        costPrice = x.CostPrice ?? 0m,
                        baseSalePrice = x.BaseSalePrice ?? 0m,
                        discountPercent = x.DiscountPercent ?? 0m,
                        discountAmount = x.DiscountAmount ?? 0m,
                        salePrice = x.SalePrice ?? 0m,
                        amount = x.Amount ?? ((x.DispatchQty ?? 0m) * (x.SalePrice ?? 0m)),

                        unit = string.IsNullOrWhiteSpace(x.Unit) ? "PCS" : x.Unit,
                        remarks = x.Remarks ?? "",

                        active = x.Active == true,
                        deleted = x.Deleted == true,
                        isPosted = x.IsPosted
                    })
                    .ToList();

                var data = new
                {
                    id = row.Id,
                    branchId = row.BranchId,

                    dispatchDate = row.DispatchDate,
                    dispatchDateText = row.DispatchDate.ToString("dd-MMM-yyyy"),
                    dispatchDateInput = row.DispatchDate.ToString("yyyy-MM-dd"),

                    dispatchType = string.IsNullOrWhiteSpace(row.DispatchType)
                        ? "SALES"
                        : row.DispatchType,

                    saleType = saleType,
                    saleTypeText = saleTypeText,

                    jobOrderId = row.JobOrderId,
                    jobOrderCode = row.JobOrderCode,

                    customerId = row.CustomerId,
                    customerName = row.CustomerName,
                    customerCode = row.CustomerCode,

                    directCustomerName = row.DirectCustomerName,
                    directCustomerMobile = row.DirectCustomerMobile,
                    directCustomerAddress = row.DirectCustomerAddress,

                    displayCustomerName = displayCustomerName,

                    departmentId = row.DepartmentId,

                    vehicleNo = row.VehicleNo,
                    driverName = row.DriverName,
                    remarks = row.Remarks,

                    active = row.Active,
                    deleted = row.Deleted,
                    isPosted = row.IsPosted,
                    isReversed = row.IsReversed,

                    itemCount = items.Count,
                    dispatchQtyTotal = items.Sum(x => (decimal)x.dispatchQty),
                    salePrice = items.Select(x => (decimal)x.salePrice).FirstOrDefault(),
                    totalAmount = items.Sum(x => (decimal)x.amount),

                    statusText = row.IsReversed
                        ? "Reversed"
                        : row.IsPosted
                            ? "Posted"
                            : "Draft",

                    // Sprint-2: workflow stepper data for the detail drawer.
                    workflowStages = _dispatchWorkflowStages,
                    workflowStageIndex = ComputeDispatchWorkflowStageIndex(row.IsPosted, row.IsReversed),

                    createdBy = row.CreatedBy,
                    createdDate = row.CreatedDate,

                    updatedBy = row.UpdatedBy,
                    updatedDate = row.UpdatedDate,

                    recordTimeStamp = row.RecordTimeStamp,

                    postedBy = row.PostedBy,
                    postedDate = row.PostedDate,

                    items = items
                };

                return Json(new
                {
                    success = true,
                    data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load dispatch detail: " + ex.Message);
            }
        }


        // ========================= Phase O.1 - DISPATCH 360 PROFILE =========================
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                // 1. Header
                var head = (
                    from d in db.Dispatch
                    join c in db.Hr_Customers on d.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    join b in db.Branches on d.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()
                    join jo in db.Gen_JobOrders on d.JobOrderId equals jo.Id into jo1
                    from jo in jo1.DefaultIfEmpty()
                    where d.Id == id
                    select new
                    {
                        d.Id,
                        d.DispatchDate,
                        d.DispatchType,
                        d.SaleType,
                        d.IsPosted,
                        d.IsReversed,
                        d.Active,
                        d.Deleted,
                        d.JobOrderId,
                        JobOrderCode = jo != null ? jo.JobOrderCode : "",
                        d.CustomerId,
                        CustomerName = c != null ? c.Customer_Name : "",
                        CustomerCode = c != null ? c.Customer_Id : "",
                        d.DirectCustomerName,
                        d.DirectCustomerMobile,
                        d.DirectCustomerAddress,
                        d.BranchId,
                        BranchName = b != null ? b.Name : "",
                        d.VehicleNo,
                        d.DriverName,
                        d.Remarks,
                        d.PostedDate,
                        d.CreatedDate,
                        d.UpdatedDate
                    }
                ).FirstOrDefault();

                if (head == null) return JsonError("Dispatch not found.");

                bool isCustomerDemand = string.Equals((head.SaleType ?? "").Trim(), "CUSTOMER_DEMAND", StringComparison.OrdinalIgnoreCase)
                                       || string.IsNullOrWhiteSpace(head.SaleType);

                var header = new
                {
                    id = head.Id,
                    dispatchDateText = head.DispatchDate.ToString("dd-MMM-yyyy"),
                    dispatchType = head.DispatchType,
                    saleType = head.SaleType,
                    saleTypeText = string.IsNullOrWhiteSpace(head.SaleType) ? "Customer Demand"
                                  : head.SaleType == "DIRECT_SALE" ? "Direct Sale" : "Customer Demand",
                    isPosted = head.IsPosted,
                    isReversed = head.IsReversed,
                    active = head.Active ?? true,
                    deleted = head.Deleted ?? false,
                    jobOrderId = head.JobOrderId,
                    jobOrderCode = head.JobOrderCode,
                    customerId = head.CustomerId,
                    customerName = isCustomerDemand
                                    ? head.CustomerName
                                    : (string.IsNullOrWhiteSpace(head.DirectCustomerName) ? head.CustomerName : head.DirectCustomerName),
                    customerCode = head.CustomerCode,
                    directCustomerName = head.DirectCustomerName,
                    directCustomerMobile = head.DirectCustomerMobile,
                    directCustomerAddress = head.DirectCustomerAddress,
                    branchId = head.BranchId,
                    branchName = head.BranchName,
                    vehicleNo = head.VehicleNo,
                    driverName = head.DriverName,
                    remarks = head.Remarks,
                    postedDateText = head.PostedDate.HasValue ? head.PostedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    createdDate = head.CreatedDate.HasValue ? head.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDate = head.UpdatedDate.HasValue ? head.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // 2. Items
                var items = (
                    from i in db.DispatchItems
                    join p in db.Gen_Products on i.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()
                    where i.DispatchId == id && i.Deleted != true
                    orderby i.Id
                    select new
                    {
                        i.Id,
                        i.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",
                        i.AvailableQtySnapshot,
                        i.DispatchQty,
                        i.CostPrice,
                        i.BaseSalePrice,
                        i.DiscountPercent,
                        i.DiscountAmount,
                        i.SalePrice,
                        i.Amount
                    }
                ).ToList().Select(x => new
                {
                    id = x.Id,
                    productId = x.ProductId,
                    productCode = x.ProductCode,
                    productName = x.ProductName,
                    availableQty = x.AvailableQtySnapshot ?? 0m,
                    dispatchQty = x.DispatchQty ?? 0m,
                    costPrice = x.CostPrice ?? 0m,
                    baseSalePrice = x.BaseSalePrice ?? 0m,
                    discountPercent = x.DiscountPercent ?? 0m,
                    discountAmount = x.DiscountAmount ?? 0m,
                    salePrice = x.SalePrice ?? 0m,
                    amount = x.Amount ?? ((x.DispatchQty ?? 0m) * (x.SalePrice ?? 0m))
                }).ToList();

                decimal totalDispatchQty = items.Sum(i => i.dispatchQty);
                decimal totalAmount = items.Sum(i => i.amount);
                decimal totalCost   = items.Sum(i => i.dispatchQty * i.costPrice);

                // 3. Gen_Stocks OUT trail
                var stockTrail = db.Gen_Stocks
                    .Where(s => s.ReferenceType == "DISPATCH"
                             && s.ReferenceId == id
                             && s.Deleted != true)
                    .OrderByDescending(s => s.CreatedDate).ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.CreatedDate,
                        s.ProdId,
                        s.TransactionType,
                        s.ReferenceType,
                        s.ReferenceNo,
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

                var prodIdsInTrail = stockTrail.Where(s => s.ProdId.HasValue).Select(s => s.ProdId.Value).Distinct().ToList();
                var prodNameMap = db.Gen_Products
                    .Where(p => prodIdsInTrail.Contains(p.Id))
                    .ToDictionary(p => p.Id, p => p.Prod_Name);

                var stockTrailOut = stockTrail.Select(s => new
                {
                    id = s.Id,
                    dateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy") : "-",
                    productName = s.ProdId.HasValue && prodNameMap.ContainsKey(s.ProdId.Value) ? prodNameMap[s.ProdId.Value] : "-",
                    transactionType = s.TransactionType,
                    referenceType = s.ReferenceType,
                    referenceNo = s.ReferenceNo,
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

                decimal stockOutQty = stockTrail.Sum(s => s.OutQty ?? 0m);
                decimal stockAmount = stockTrail.Sum(s => s.Amount ?? 0m);

                // 4. KPIs
                var kpis = new
                {
                    itemCount = items.Count,
                    totalQty = totalDispatchQty,
                    totalAmount = Math.Round(totalAmount, 2),
                    totalCost = Math.Round(totalCost, 2),
                    margin = Math.Round(totalAmount - totalCost, 2),
                    marginPct = totalAmount > 0 ? Math.Round(((totalAmount - totalCost) / totalAmount) * 100m, 1) : 0m,
                    stockOutQty,
                    stockAmount = Math.Round(stockAmount, 2),
                    stockRowCount = stockTrail.Count
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    items,
                    stockTrail = stockTrailOut
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            var row = db.Dispatch.FirstOrDefault(x =>
                x.Id == id && x.Deleted != true);

            if (row == null)
                return Json(new { success = false, message = "Dispatch not found." });

            if (row.IsPosted)
                return Json(new { success = false, message = "Posted dispatch cannot be deleted." });

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            row.Deleted = true;
            row.UpdatedBy = userId;
            row.UpdatedDate = now;
            row.RecordTimeStamp = now;

            // Sprint-2: cascade soft-delete to DispatchItems so the next
            // list / detail / aggregation doesn't see "live" items of a
            // deleted header.
            var liveItems = db.DispatchItems
                .Where(i => i.DispatchId == id && i.Deleted != true)
                .ToList();
            foreach (var it in liveItems)
            {
                it.Deleted = true;
                it.UpdatedBy = userId;
                it.UpdatedDate = now;
                it.RecordTimeStamp = now;
            }

            db.SaveChanges();

            LogDispatchAudit(id, "DELETED",
                oldValue: DispatchStatus.DRAFT,
                newValue: "DELETED",
                detail: liveItems.Count + " line(s) soft-deleted");

            return Json(new { success = true, message = "Dispatch deleted successfully." });
        }

        // ===================== POST =====================
        [HttpPost, Route("post/{id:int}")]
        public ActionResult PostDispatch(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard against concurrent Post / Reverse
                    // calls on the same row. Without the lock two operators
                    // hitting Post simultaneously would each pass the
                    // IsPosted check and both write Gen_Stocks OUT rows.
                    LockDispatchRow(id);

                    var dispatch = db.Dispatch
                        .Include("DispatchItems")
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (dispatch == null)
                        return JsonError("Dispatch not found.");

                    if (dispatch.IsPosted)
                        return JsonError("Dispatch already posted.");

                    if (dispatch.IsReversed)
                        return JsonError("Reversed dispatch cannot be posted.");

                    string saleType = string.IsNullOrWhiteSpace(dispatch.SaleType)
                        ? "CUSTOMER_DEMAND"
                        : dispatch.SaleType.Trim().ToUpper();

                    if (saleType != "CUSTOMER_DEMAND" && saleType != "DIRECT_SALE")
                        return JsonError("Invalid sale type.");

                    if (saleType == "CUSTOMER_DEMAND")
                    {
                        if (!dispatch.JobOrderId.HasValue || dispatch.JobOrderId.Value <= 0)
                            return JsonError("Job Order is required for customer demand dispatch.");

                        if (!dispatch.CustomerId.HasValue || dispatch.CustomerId.Value <= 0)
                            return JsonError("Customer is required for customer demand dispatch.");
                    }

                    if (saleType == "DIRECT_SALE")
                    {
                        if ((!dispatch.CustomerId.HasValue || dispatch.CustomerId.Value <= 0) &&
                            string.IsNullOrWhiteSpace(dispatch.DirectCustomerName))
                        {
                            return JsonError("Customer or walk-in customer name is required for direct sale.");
                        }
                    }

                    var items = dispatch.DispatchItems
                        .Where(x => x.Deleted != true)
                        .ToList();

                    if (!items.Any())
                        return JsonError("No dispatch items found.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    foreach (var it in items)
                    {
                        if (!it.ProductId.HasValue || it.ProductId.Value <= 0)
                            return JsonError("Invalid product found in dispatch items.");

                        decimal qty = it.DispatchQty ?? 0m;

                        if (qty <= 0)
                            return JsonError("Invalid dispatch quantity.");

                        decimal salePrice = it.SalePrice ?? 0m;

                        if (salePrice < 0)
                            return JsonError("Sale price cannot be negative.");

                        decimal balance = GetDispatchAvailableQty(
                            it.ProductId.Value,
                            dispatch.BranchId,
                            saleType,
                            dispatch.Id,
                            dispatch.CustomerId
                        );

                        if (qty > balance)
                        {
                            return JsonError(
                                saleType == "DIRECT_SALE"
                                    ? "Dispatch quantity exceeds free company stock (pending drafts already reserved)."
                                    : "Dispatch quantity exceeds customer reserved stock (pending drafts already reserved)."
                            );
                        }

                        it.Amount = qty * salePrice;
                    }

                    foreach (var it in items)
                    {
                        int productId = it.ProductId.Value;
                        decimal qty = it.DispatchQty ?? 0m;

                        decimal opening = GetDispatchAvailableQty(
                            productId,
                            dispatch.BranchId,
                            saleType
                        );

                        decimal current = opening - qty;
                        if (current < 0)
                            current = 0;

                        bool alreadyPosted = db.Gen_Stocks.Any(x =>
                            x.Deleted != true &&
                            x.ReferenceType == "DISPATCH" &&
                            x.ReferenceId == dispatch.Id &&
                            x.ItemId == productId &&
                            x.StockStage == "FINISHED");

                        if (!alreadyPosted)
                        {
                            decimal salePrice = it.SalePrice ?? 0m;
                            decimal lineAmount = it.Amount ?? (qty * salePrice);

                            db.Gen_Stocks.Add(new Gen_Stocks
                            {
                                ItemType = "PRODUCT",
                                ItemId = productId,
                                ProdId = productId,

                                BranchId = dispatch.BranchId,
                                StockStage = "FINISHED",

                                TransactionType = "OUT",

                                ReferenceType = "DISPATCH",
                                ReferenceId = dispatch.Id,
                                ReferenceNo = "DISPATCH-" + dispatch.Id,

                                OpeningQty = opening,
                                InQty = 0m,
                                OutQty = qty,
                                Qty = qty,
                                CurrentQty = current,

                                Rate = salePrice,
                                LastKnownRate = salePrice,
                                Amount = lineAmount,

                                Unit = string.IsNullOrWhiteSpace(it.Unit) ? "PCS" : it.Unit,

                                StockDescription = saleType == "DIRECT_SALE"
                                    ? "Finished goods direct sale dispatch"
                                    : "Finished goods customer demand dispatch",

                                Remarks = string.IsNullOrWhiteSpace(dispatch.Remarks)
                                    ? "Dispatch posted"
                                    : dispatch.Remarks,

                                Active = true,
                                Deleted = false,
                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            });
                        }

                        it.IsPosted = true;
                        it.AvailableQtySnapshot = opening;
                        it.UpdatedBy = userId;
                        it.UpdatedDate = now;
                        it.RecordTimeStamp = now;
                    }

                    dispatch.DispatchType = "SALES";
                    dispatch.SaleType = saleType;

                    dispatch.IsPosted = true;
                    dispatch.PostedBy = userId;
                    dispatch.PostedDate = now;
                    dispatch.UpdatedBy = userId;
                    dispatch.UpdatedDate = now;
                    dispatch.RecordTimeStamp = now;

                    db.SaveChanges();
                    tx.Commit();

                    LogDispatchAudit(dispatch.Id, "POSTED",
                        oldValue: DispatchStatus.DRAFT,
                        newValue: DispatchStatus.POSTED,
                        detail: "Sale type: " + saleType
                              + ", " + (dispatch.DispatchItems != null ? dispatch.DispatchItems.Count : 0) + " line(s)");

                    return JsonSuccess("Dispatch posted successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Dispatch posting failed: " + DescribeDispatchError(ex));
                }
            }
        }


        // ===================== REVERSE =====================
        [HttpPost, Route("reverse/{id:int}")]
        public ActionResult ReverseDispatch(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard — serialise concurrent Reverse
                    // calls so two operators can't both write Gen_Stocks
                    // IN rows and double-restore stock.
                    LockDispatchRow(id);

                    var dispatch = db.Dispatch
                        .Include("DispatchItems")
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (dispatch == null)
                        return JsonError("Dispatch not found.");

                    if (dispatch.IsPosted != true)
                        return JsonError("Dispatch is not posted.");

                    if (dispatch.IsReversed == true)
                        return JsonError("Dispatch already reversed.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var items = dispatch.DispatchItems
                        .Where(x => x.Deleted != true)
                        .ToList();

                    if (!items.Any())
                        return JsonError("No dispatch items found.");

                    foreach (var item in items)
                    {
                        decimal qty = item.DispatchQty ?? 0m;

                        if (qty <= 0)
                            continue;

                        db.Gen_Stocks.Add(new Gen_Stocks
                        {
                            ItemType = "PRODUCT",
                            ItemId = item.ProductId,
                            ProdId = item.ProductId,

                            BranchId = dispatch.BranchId,
                            StockStage = "FINISHED",

                            // ✅ IMPORTANT: Direction must be IN
                            TransactionType = "IN",
                            ReferenceType = "DISPATCH-REVERSAL",
                            ReferenceId = dispatch.Id,
                            ReferenceNo = "DISPATCH-REV-" + dispatch.Id,

                            OpeningQty = 0m,
                            InQty = qty,
                            OutQty = 0m,
                            Qty = qty,
                            CurrentQty = null,

                            Unit = string.IsNullOrWhiteSpace(item.Unit) ? "PCS" : item.Unit,

                            // Optional but recommended
                            Rate = item.CostPrice ?? 0m,
                            LastKnownRate = item.CostPrice ?? 0m,
                            Amount = qty * (item.CostPrice ?? 0m),

                            StockDescription = "Dispatch reversal",
                            Remarks = "Reversal of dispatch #" + dispatch.Id,

                            Active = true,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now,
                            UpdatedBy = userId,
                            UpdatedDate = now,
                            RecordTimeStamp = now
                        });
                    }

                    // ✅ Update items
                    foreach (var it in items)
                    {
                        it.IsPosted = false;
                        it.UpdatedBy = userId;
                        it.UpdatedDate = now;
                        it.RecordTimeStamp = now;
                    }

                    // ✅ Update dispatch
                    dispatch.IsPosted = false;
                    dispatch.IsReversed = true;
                    dispatch.UpdatedBy = userId;
                    dispatch.UpdatedDate = now;
                    dispatch.RecordTimeStamp = now;

                    db.SaveChanges();
                    tx.Commit();

                    LogDispatchAudit(dispatch.Id, "REVERSED",
                        oldValue: DispatchStatus.POSTED,
                        newValue: DispatchStatus.REVERSED);

                    return JsonSuccess("Dispatch reversed successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Dispatch reverse failed: " + DescribeDispatchError(ex));
                }
            }
        }

        // ===================== LEDGER VIEW =====================
        [HttpGet]
        [Route("ledger/{dispatchId:int}")]
        public ActionResult Ledger(int dispatchId)
        {
            try
            {
                // -------- 1. Find which products + branches THIS dispatch consumed --------
                // The dispatch may consume multiple products. We collect every
                // unique (ProductId, BranchId) pair from this dispatch's Gen_Stocks
                // OUT rows so the IN side is scoped correctly.
                var dispatchStockRows = db.Gen_Stocks
                    .Where(s => s.Deleted != true
                             && s.ReferenceType == "DISPATCH"
                             && s.ReferenceId == dispatchId
                             && s.StockStage == "FINISHED")
                    .ToList();

                var productBranchPairs = dispatchStockRows
                    .Select(s => new
                    {
                        ProductId = s.ProdId ?? s.ItemId ?? 0,
                        BranchId  = s.BranchId ?? 0
                    })
                    .Where(p => p.ProductId > 0)
                    .Distinct()
                    .ToList();

                if (productBranchPairs.Count == 0)
                    return JsonSuccess("Dispatch ledger loaded.", new List<object>());

                var productIds = productBranchPairs.Select(p => p.ProductId).Distinct().ToList();
                var branchIds  = productBranchPairs.Select(p => p.BranchId).Distinct().ToList();

                // -------- 2. Pull ALL FINISHED stock movements for those (product,branch) --------
                // This includes the QC_INSPECTION INs that built up the stock and
                // every DISPATCH OUT against it (including this one). The result
                // is a balanced ledger showing where the stock came from and where
                // it went.
                var allRows = db.Gen_Stocks
                    .Where(s => s.Deleted != true
                             && s.StockStage == "FINISHED"
                             && (s.ProdId.HasValue   && productIds.Contains(s.ProdId.Value)
                              || s.ItemId.HasValue   && productIds.Contains(s.ItemId.Value))
                             && s.BranchId.HasValue
                             && branchIds.Contains(s.BranchId.Value))
                    .OrderBy(s => s.RecordTimeStamp ?? s.CreatedDate)
                    .ThenBy(s => s.Id)
                    .ToList();

                // -------- 3. Cache product names in one query --------
                var productMap = db.Gen_Products
                    .Where(p => productIds.Contains(p.Id) && p.Deleted != true)
                    .ToDictionary(p => p.Id, p => p.Prod_Name);

                // -------- 4. Build the rows, with running balance and a flag for THIS dispatch --------
                decimal running = 0m;
                var rows = allRows.Select(s =>
                {
                    int productId = s.ProdId ?? s.ItemId ?? 0;
                    string productName = productMap.ContainsKey(productId)
                                            ? productMap[productId]
                                            : ("Product ID: " + productId);

                    DateTime? ledgerDate = s.RecordTimeStamp ?? s.CreatedDate ?? s.UpdatedDate;

                    decimal inQ  = s.InQty  ?? 0m;
                    decimal outQ = s.OutQty ?? 0m;
                    running += inQ - outQ;

                    string typeLabel;
                    if (s.ReferenceType == "QC_INSPECTION") typeLabel = "IN — QC";
                    else if (s.ReferenceType == "DISPATCH"
                             && s.ReferenceId == dispatchId) typeLabel = "OUT — THIS DISPATCH";
                    else if (s.ReferenceType == "DISPATCH")  typeLabel = "OUT — Other Dispatch";
                    else typeLabel = (s.ReferenceType ?? s.TransactionType ?? "-");

                    string remarks = !string.IsNullOrWhiteSpace(s.Remarks)
                                        ? s.Remarks
                                        : (s.StockDescription ?? "");
                    if (!string.IsNullOrWhiteSpace(s.ReferenceNo))
                        remarks = string.IsNullOrWhiteSpace(remarks)
                                    ? s.ReferenceNo
                                    : remarks + " (" + s.ReferenceNo + ")";

                    return new
                    {
                        date = ledgerDate.HasValue
                                ? ledgerDate.Value.ToString("dd-MMM-yyyy")
                                : "-",
                        createdDate = ledgerDate,

                        product     = productName,
                        productName = productName,

                        qtyIn  = inQ,
                        qtyOut = outQ,
                        balance = running,

                        type            = typeLabel,
                        transactionType = typeLabel,

                        // True only for rows that belong to THIS dispatch — UI
                        // can highlight these in red/bold.
                        isCurrent = (s.ReferenceType == "DISPATCH" && s.ReferenceId == dispatchId),

                        remarks = remarks
                    };
                }).ToList();

                return JsonSuccess("Dispatch ledger loaded.", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load dispatch ledger: " + ex.Message);
            }
        }

        [HttpGet, Route("items-detail/{dispatchId:int}")]
        public ActionResult GetDispatchItems(int dispatchId)
        {
            try
            {
                var dispatch = db.Dispatch.FirstOrDefault(x =>
                    x.Id == dispatchId &&
                    x.Deleted != true);

                if (dispatch == null)
                    return JsonError("Dispatch not found.");

                string saleType = NormalizeSaleType(dispatch.SaleType);

                var rows = db.DispatchItems
                    .Where(x => x.DispatchId == dispatchId && x.Deleted != true)
                    .ToList()
                    .Select(x =>
                    {
                        int productId = x.ProductId ?? 0;
                        int branchId = x.BranchId ?? dispatch.BranchId;

                        decimal customerDemandRemaining = 0m;
                        decimal reservedStock = 0m;
                        decimal freeCompanyStock = 0m;
                        decimal availableForSaleType = 0m;

                        if (productId > 0)
                        {
                            reservedStock = GetCustomerReservedStockBalance(productId, branchId);
                            freeCompanyStock = GetFreeCompanyStockBalance(productId, branchId);

                            if (saleType == "CUSTOMER_DEMAND" && dispatch.CustomerId.HasValue)
                            {
                                customerDemandRemaining = GetCustomerDemandRemaining(
                                    dispatch.CustomerId.Value,
                                    productId,
                                    branchId
                                );

                                availableForSaleType = Math.Min(customerDemandRemaining, reservedStock);
                            }
                            else if (saleType == "DIRECT_SALE")
                            {
                                availableForSaleType = freeCompanyStock;
                            }
                        }

                        var product = productId > 0
                            ? db.Gen_Products.FirstOrDefault(p =>
                                p.Id == productId &&
                                p.Deleted != true &&
                                p.Active == true)
                            : null;

                        return new
                        {
                            id = x.Id,
                            dispatchId = x.DispatchId,

                            productId = productId,
                            productName = product != null ? product.Prod_Name : "-",

                            branchId = branchId,

                            dispatchedQty = x.DispatchQty ?? 0m,
                            availableQtySnapshot = x.AvailableQtySnapshot ?? 0m,

                    // For UI reserved column in customer demand
                    customerReservedStock = saleType == "CUSTOMER_DEMAND"
                                ? customerDemandRemaining
                                : 0m,

                    // Actual stock values
                    reservedStock = reservedStock,
                            freeCompanyStock = freeCompanyStock,
                            totalFinishedStock = reservedStock + freeCompanyStock,

                            availableQty = availableForSaleType,

                            unit = string.IsNullOrWhiteSpace(x.Unit) ? "PCS" : x.Unit,
                            remarks = x.Remarks ?? "",
                            isPosted = x.IsPosted == true
                        };
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = rows
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load dispatch items: " + ex.Message);
            }
        }

        [HttpGet]
        [Route("~/emb-products/dropdowns/dispatch-customers")]
        public ActionResult DispatchCustomers(int? branchId = null)
        {
            try
            {
                int bId = branchId.HasValue && branchId.Value > 0
                    ? branchId.Value
                    : GetCurrentBranchId();

                var reservedRows =
                    (from gs in db.Gen_Stocks
                     join qc in db.QC_Inspection on gs.ReferenceId equals qc.Id
                     join wo in db.WorkOrders on qc.WorkOrderId equals wo.Id
                     join jo in db.Gen_JobOrders on wo.JobOrderId equals jo.Id
                     join c in db.Hr_Customers on jo.CustomerId equals c.Id
                     where
                         gs.Deleted != true &&
                         gs.StockStage == "FINISHED" &&
                         gs.BranchId == bId &&
                         gs.ReferenceType == "QC_INSPECTION" &&
                         (gs.InQty ?? 0m) > 0 &&

                         qc.Deleted != true &&
                         qc.Status != "Reversed" &&
                         qc.Status != "Cancelled" &&

                         wo.Deleted != true &&
                         wo.JobOrderId.HasValue &&

                         jo.Deleted != true &&
                         jo.Active == true &&
                         jo.CustomerId.HasValue &&
                         jo.CustomerId.Value > 0 &&

                         c.Deleted != true
                     select new
                     {
                         CustomerId = c.Id,
                         CustomerCode = c.Customer_Id,
                         CustomerName = c.Customer_Name,
                         ProductId = gs.ProdId ?? gs.ItemId ?? 0,
                         InQty = gs.InQty ?? 0m,
                         OutQty = gs.OutQty ?? 0m
                     })
                    .ToList()
                    .Where(x => x.ProductId > 0)
                    .GroupBy(x => new
                    {
                        x.CustomerId,
                        x.CustomerCode,
                        x.CustomerName,
                        x.ProductId
                    })
                    .Select(g => new
                    {
                        g.Key.CustomerId,
                        g.Key.CustomerCode,
                        g.Key.CustomerName,
                        g.Key.ProductId,
                        ReservedQty = g.Sum(x => x.InQty) - g.Sum(x => x.OutQty)
                    })
                    .Where(x => x.ReservedQty > 0m)
                    .ToList();

                var reservedWithRemaining = reservedRows.Select(r =>
                {
                    int cid = r.CustomerId;
                    int pid = r.ProductId;

                    decimal dispatched = db.DispatchItems
                        .Where(di =>
                            di.Deleted != true &&
                            di.ProductId == pid &&
                            di.Dispatch.Deleted != true &&
                            di.Dispatch.IsReversed != true &&
                            di.Dispatch.SaleType == "CUSTOMER_DEMAND" &&
                            di.Dispatch.CustomerId == cid &&
                            di.Dispatch.BranchId == bId)
                        .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                    decimal remaining = r.ReservedQty - dispatched;
                    if (remaining < 0m) remaining = 0m;

                    return new
                    {
                        r.CustomerId,
                        r.CustomerCode,
                        r.CustomerName,
                        r.ProductId,
                        Remaining = remaining
                    };
                })
                .Where(x => x.Remaining > 0m)
                .ToList();

                var rows = reservedWithRemaining
                    .GroupBy(x => new
                    {
                        x.CustomerId,
                        x.CustomerCode,
                        x.CustomerName
                    })
                    .Select(g => new
                    {
                        id = g.Key.CustomerId,
                        name = (g.Key.CustomerCode ?? "") + " - " + (g.Key.CustomerName ?? ""),
                        text = (g.Key.CustomerCode ?? "") + " - " + (g.Key.CustomerName ?? ""),
                        customerCode = g.Key.CustomerCode,
                        customerName = g.Key.CustomerName,
                        remainingQty = g.Sum(x => x.Remaining),
                        availableQty = g.Sum(x => x.Remaining)
                    })
                    .Where(x => x.availableQty > 0m)
                    .OrderBy(x => x.customerName)
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = rows
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Failed to load dispatch customers: " + ex.Message,
                    data = new List<object>()
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private decimal GetFinishedStockBalance(int productId, int branchId)
        {
            var q = db.Gen_Stocks
                .Where(x =>
                    x.Deleted != true &&
                    x.StockStage == "FINISHED" &&
                    (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED") &&
                    x.BranchId == branchId &&
                    (
                        x.ProdId == productId ||
                        (!x.ProdId.HasValue && x.ItemId == productId)
                    ));

            decimal inQty = q.Sum(x => (decimal?)x.InQty) ?? 0m;
            decimal outQty = q.Sum(x => (decimal?)x.OutQty) ?? 0m;

            return inQty - outQty;
        }

        private string NormalizeSaleType(string saleType)
        {
            saleType = string.IsNullOrWhiteSpace(saleType)
                ? "CUSTOMER_DEMAND"
                : saleType.Trim().ToUpper();

            return saleType;
        }

        private bool IsValidSaleType(string saleType)
        {
            return saleType == "CUSTOMER_DEMAND" || saleType == "DIRECT_SALE";
        }
        
        private decimal GetDispatchAvailableQty(int productId, int branchId, string saleType)
        {
            saleType = NormalizeSaleType(saleType);

            if (saleType == "DIRECT_SALE")
                return GetFreeCompanyStockBalance(productId, branchId);

            return GetCustomerReservedStockBalance(productId, branchId);
        }

        private decimal GetDispatchAvailableQty(int productId, int branchId, string saleType, int excludeDispatchId, int? customerId)
        {
            string st = NormalizeSaleType(saleType);

            decimal baseBalance;

            if (st == "DIRECT_SALE")
            {
                baseBalance = GetFreeCompanyStockBalance(productId, branchId);
            }
            else
            {
                baseBalance = customerId.HasValue && customerId.Value > 0
                    ? GetCustomerReservedStockBalance(productId, branchId, customerId.Value)
                    : GetCustomerReservedStockBalance(productId, branchId);
            }

            int excludeId = excludeDispatchId;
            int? scopedCustomerId = (st == "CUSTOMER_DEMAND") ? customerId : null;

            decimal otherDrafts = db.DispatchItems
                .Where(di =>
                    di.Deleted != true &&
                    di.ProductId == productId &&
                    di.Dispatch.Deleted != true &&
                    di.Dispatch.IsPosted != true &&
                    di.Dispatch.IsReversed != true &&
                    di.Dispatch.SaleType == st &&
                    di.Dispatch.BranchId == branchId &&
                    (excludeId == 0 || di.DispatchId != excludeId) &&
                    (!scopedCustomerId.HasValue || di.Dispatch.CustomerId == scopedCustomerId.Value))
                .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

            decimal available = baseBalance - otherDrafts;
            return available < 0m ? 0m : available;
        }

        private decimal GetCustomerDemandRemaining(int customerId, int productId, int branchId)
        {
            var demandRows =
                (from j in db.Gen_JobOrders
                 join d in db.Gen_Design on j.DesignId equals d.Id
                 where
                     j.Deleted != true &&
                     j.Active == true &&
                     j.CustomerId == customerId &&
                     j.BranchId == branchId &&
                     d.ProductId == productId
                 select new
                 {
                     JobOrderId = j.Id,
                     DemandQty = j.OrderQty 
                 })
                .ToList();

            decimal totalDemand = demandRows.Sum(x => Convert.ToDecimal(x.DemandQty));

            var jobOrderIds = demandRows.Select(x => x.JobOrderId).ToList();

            decimal dispatched = db.DispatchItems
                .Where(di =>
                    di.Deleted != true &&
                    di.ProductId == productId &&
                    di.Dispatch.Deleted != true &&
                    di.Dispatch.IsPosted == true &&
                    di.Dispatch.IsReversed != true &&
                    di.Dispatch.SaleType == "CUSTOMER_DEMAND" &&
                    di.Dispatch.CustomerId == customerId &&
                    di.Dispatch.BranchId == branchId &&
                    di.Dispatch.JobOrderId.HasValue &&
                    jobOrderIds.Contains(di.Dispatch.JobOrderId.Value))
                .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

            decimal remaining = totalDemand - dispatched;

            return remaining < 0 ? 0m : remaining;
        }

        private decimal GetCustomerReservedStockBalance(int productId, int branchId, int customerId)
        {
            var stockRows = db.Gen_Stocks
                .Where(gs =>
                    gs.Deleted != true &&
                    gs.Active == true &&
                    gs.StockStage == "FINISHED" &&
                    gs.BranchId == branchId &&
                    (gs.ItemType == "PRODUCT" || gs.ItemType == "FINISHED") &&
                    (
                        gs.ProdId == productId ||
                        (!gs.ProdId.HasValue && gs.ItemId == productId)
                    ) &&
                    gs.ReferenceType == "QC_INSPECTION")
                .ToList();

            decimal inQty = 0m;
            decimal outQty = 0m;

            foreach (var s in stockRows)
            {
                var qc = db.QC_Inspection.FirstOrDefault(q =>
                    q.Id == s.ReferenceId &&
                    q.Deleted != true &&
                    q.Status != "Reversed" &&
                    q.Status != "Cancelled" &&
                    q.WorkOrderId.HasValue);

                if (qc == null)
                    continue;

                var wo = db.WorkOrders.FirstOrDefault(w =>
                    w.Id == qc.WorkOrderId.Value &&
                    w.Deleted != true &&
                    w.JobOrderId.HasValue);

                if (wo == null)
                    continue;

                var jo = db.Gen_JobOrders.FirstOrDefault(j =>
                    j.Id == wo.JobOrderId.Value &&
                    j.Deleted != true &&
                    j.Active == true &&
                    j.CustomerId == customerId &&
                    j.BranchId == branchId);

                if (jo == null)
                    continue;

                inQty += s.InQty ?? 0m;
                outQty += s.OutQty ?? 0m;
            }

            decimal balance = inQty - outQty;
            return balance < 0m ? 0m : balance;
        }

        private decimal GetProductCostPrice(int productId, int branchId)
        {
            var q = db.Gen_Stocks.Where(x =>
                x.Deleted != true &&
                x.StockStage == "FINISHED" &&
                x.BranchId == branchId &&
                (
                    (x.ProdId.HasValue && x.ProdId.Value == productId) ||
                    (x.ItemId.HasValue && x.ItemId.Value == productId)
                ));

            decimal rate = q
                .OrderByDescending(x => x.Id)
                .Select(x => (decimal?)x.Rate)
                .FirstOrDefault() ?? 0m;

            if (rate > 0m)
                return rate;

            return 0m;
        }

        private decimal GetBaseSalePrice(decimal costPrice)
        {
            if (costPrice <= 0m)
                return 0m;

            return Math.Round(costPrice * 1.10m, 2);
        }

        private decimal GetCustomerBaseSalePrice(int productId, int branchId, int? customerId)
        {
            if (customerId.HasValue && customerId.Value > 0)
            {
                decimal lastCustomerPrice = db.DispatchItems
                    .Where(x =>
                        x.Deleted != true &&
                        x.ProductId == productId &&
                        x.BaseSalePrice.HasValue &&
                        x.BaseSalePrice.Value > 0 &&
                        x.Dispatch.Deleted != true &&
                        x.Dispatch.CustomerId == customerId.Value &&
                        x.Dispatch.BranchId == branchId)
                    .OrderByDescending(x => x.Dispatch.DispatchDate)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (decimal?)x.BaseSalePrice)
                    .FirstOrDefault() ?? 0m;

                if (lastCustomerPrice > 0m)
                    return lastCustomerPrice;
            }

            decimal costPrice = GetProductCostPrice(productId, branchId);

            if (costPrice > 0m)
                return GetBaseSalePrice(costPrice);

            return 0m;
        }

        [HttpGet, Route("export-excel")]
        public ActionResult ExportExcel(string search = "",string activeFilter = "",string showDeleted = "")
        {
            var query = db.Dispatch.AsQueryable();

            // Filters (same as List)
            if (string.IsNullOrEmpty(showDeleted))
                query = query.Where(x => x.Deleted == false || x.Deleted == null);

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(x =>
                    (x.VehicleNo != null && x.VehicleNo.Contains(search)) ||
                    (x.DriverName != null && x.DriverName.Contains(search)) ||
                    (x.Hr_Customers != null && x.Hr_Customers.Customer_Name.Contains(search))
                );
            }

            var list = query
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.DispatchDate,
                    Customer = x.Hr_Customers.Customer_Name,
                    x.VehicleNo,
                    x.DriverName,
                    Status = x.Active == true ? "Active" : "Inactive"
                })
                .ToList();

            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Dispatch");

                // Header
                ws.Cells[1, 1].Value = "ID";
                ws.Cells[1, 2].Value = "Date";
                ws.Cells[1, 3].Value = "Customer";
                ws.Cells[1, 4].Value = "Vehicle No";
                ws.Cells[1, 5].Value = "Driver";
                ws.Cells[1, 6].Value = "Status";

                int row = 2;
                foreach (var x in list)
                {
                    ws.Cells[row, 1].Value = x.Id;
                    ws.Cells[row, 2].Value = x.DispatchDate.ToString("dd-MMM-yyyy");
                    ws.Cells[row, 3].Value = x.Customer;
                    ws.Cells[row, 4].Value = x.VehicleNo;
                    ws.Cells[row, 5].Value = x.DriverName;
                    ws.Cells[row, 6].Value = x.Status;
                    row++;
                }

                ws.Cells.AutoFitColumns();

                return File(
                    package.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Dispatch.xlsx"
                );
            }
        }

        [HttpGet, Route("export-pdf")]
        public ActionResult ExportPdf(string search = "",string activeFilter = "",string showDeleted = "")
        {
            var query = db.Dispatch.AsQueryable();

            if (string.IsNullOrEmpty(showDeleted))
                query = query.Where(x => x.Deleted == false || x.Deleted == null);

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(x =>
                    (x.VehicleNo != null && x.VehicleNo.Contains(search)) ||
                    (x.DriverName != null && x.DriverName.Contains(search)) ||
                    (x.Hr_Customers != null && x.Hr_Customers.Customer_Name.Contains(search))
                );
            }

            var data = query
                .OrderByDescending(x => x.Id)
                .Select(x => new DispatchRowViewModel
                {
                    Id = x.Id,
                    DispatchDate = x.DispatchDate,
                    CustomerName = x.Hr_Customers.Customer_Name,
                    VehicleNo = x.VehicleNo,
                    DriverName = x.DriverName,
                    Active = x.Active ?? false,
                })
                .ToList();

            data.ForEach(x =>
                x.DispatchDateText = x.DispatchDate.ToString("dd-MMM-yyyy")
            );

            return new Rotativa.ViewAsPdf("DispatchPdf", data)
            {
                FileName = "Dispatch.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait
            };
        }

        [HttpGet, Route("print")]
        public ActionResult Print(string search = "",string activeFilter = "",string showDeleted = "")
        {
            var data = db.Dispatch
                .Where(x => (x.Deleted == false || x.Deleted == null))
                .OrderByDescending(x => x.Id)
                .Select(x => new DispatchRowViewModel
                {
                    Id = x.Id,
                    DispatchDate = x.DispatchDate,
                    CustomerName = x.Hr_Customers.Customer_Name,
                    VehicleNo = x.VehicleNo,
                    DriverName = x.DriverName,
                    Active = x.Active ?? false,
                })
                .ToList();

            data.ForEach(x =>
                x.DispatchDateText = x.DispatchDate.ToString("dd-MMM-yyyy")
            );

            return View("DispatchPrint", data);
        }

        // ====================================================================
        // SALE BILL (Invoice)
        // ---------------------------------------------------------------
        // Generates a customer-facing invoice PDF for one dispatch. Renders
        // inline so the browser shows it in-tab (operator can print or save).
        // The dispatch itself is the proof of sale; the bill is just a
        // formatted view of it with company header + signature lines.
        // ====================================================================
        [HttpGet, Route("sale-bill/{id:int}")]
        public ActionResult SaleBill(int id)
        {
            try
            {
                var d = db.Dispatch
                    .Where(x => x.Id == id && (x.Deleted == false || x.Deleted == null))
                    .Select(x => new {
                        x.Id, x.DispatchDate, x.SaleType,
                        x.CustomerId,
                        CustomerName = x.Hr_Customers != null ? x.Hr_Customers.Customer_Name : null,
                        CustomerMobile = x.Hr_Customers != null ? x.Hr_Customers.Customer_Mobile : null,
                        CustomerAddress = x.Hr_Customers != null ? x.Hr_Customers.Customer_Address : null,
                        x.DirectCustomerName,
                        x.DirectCustomerMobile,
                        x.DirectCustomerAddress,
                        x.BranchId,
                        x.VehicleNo, x.DriverName, x.Remarks,
                        x.IsPosted, x.IsReversed
                    })
                    .FirstOrDefault();

                if (d == null) return HttpNotFound("Dispatch not found.");

                // Line items
                var items = (from di in db.DispatchItems
                             where di.DispatchId == id && di.Deleted != true
                             join p in db.Gen_Products on di.ProductId equals p.Id into pj
                             from p in pj.DefaultIfEmpty()
                             select new {
                                 ProductName = p != null ? p.Prod_Name : "-",
                                 ProductCode = p != null ? p.Prod_Code : "",
                                 Qty       = di.DispatchQty ?? 0m,
                                 Unit      = di.Unit ?? "",
                                 BasePrice = di.BaseSalePrice ?? 0m,
                                 SalePrice = di.SalePrice ?? 0m,
                                 DiscountPct = di.DiscountPercent ?? 0m,
                                 DiscountAmt = di.DiscountAmount ?? 0m,
                                 Amount    = di.Amount ?? 0m,
                                 Remarks   = di.Remarks
                             }).ToList();

                // Company header. PK on Adm_CompanyProfile is CompanyID (not Id).
                var company = db.Adm_CompanyProfile
                    .OrderBy(c => c.CompanyID)
                    .FirstOrDefault();

                // Branch. Dispatch.BranchId is non-nullable int — no .HasValue/.Value.
                var branch = d.BranchId > 0
                    ? db.Branches.FirstOrDefault(b => b.Id == d.BranchId)
                    : null;

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 30f, 30f, 25f, 25f);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var titleFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 22, BaseColor.BLACK);
                    var companyFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.BLACK);
                    var smallFont   = FontFactory.GetFont(FontFactory.HELVETICA, 9, BaseColor.DARK_GRAY);
                    var labelFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, BaseColor.GRAY);
                    var valueFont   = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.BLACK);
                    var headerFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9, BaseColor.WHITE);
                    var bodyFont    = FontFactory.GetFont(FontFactory.HELVETICA, 9.5f, BaseColor.BLACK);
                    var totalFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.BLACK);

                    // ===== HEADER: Company name (left) + INVOICE title (right) =====
                    var headerTable = new PdfPTable(2) { WidthPercentage = 100f };
                    headerTable.SetWidths(new float[] { 2f, 1f });

                    var leftCell = new PdfPCell { Border = Rectangle.NO_BORDER, PaddingBottom = 4f };
                    // Resolve company strings into plain locals — passing complex
                    // ternaries directly into new Paragraph(...) confuses the
                    // compiler's overload resolution (CS1503 against the
                    // Paragraph(string, Chunk) overload).
                    string companyName = (company != null && !string.IsNullOrWhiteSpace(company.CompanyName))
                        ? company.CompanyName
                        : "EMB Production";
                    leftCell.AddElement(new Paragraph(companyName, companyFont));
                    if (company != null)
                    {
                        if (!string.IsNullOrWhiteSpace(company.Address))
                        {
                            string addressLine = company.Address;
                            leftCell.AddElement(new Paragraph(addressLine, smallFont));
                        }
                        if (!string.IsNullOrWhiteSpace(company.ContactNumber))
                        {
                            string telLine = "Tel: " + company.ContactNumber;
                            leftCell.AddElement(new Paragraph(telLine, smallFont));
                        }
                        if (!string.IsNullOrWhiteSpace(company.Email))
                        {
                            string emailLine = company.Email;
                            leftCell.AddElement(new Paragraph(emailLine, smallFont));
                        }
                        if (!string.IsNullOrWhiteSpace(company.NTN))
                        {
                            string ntnLine = "NTN: " + company.NTN;
                            leftCell.AddElement(new Paragraph(ntnLine, smallFont));
                        }
                    }
                    headerTable.AddCell(leftCell);

                    var rightCell = new PdfPCell { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT };
                    var rightStack = new Paragraph();
                    rightStack.Add(new Chunk("SALE BILL\n", titleFont));
                    rightStack.Add(new Chunk("INV-" + d.Id.ToString("D6") + "\n", labelFont));
                    rightStack.Add(new Chunk(d.DispatchDate.ToString("dd MMM yyyy"), smallFont));
                    rightStack.Alignment = Element.ALIGN_RIGHT;
                    rightCell.AddElement(rightStack);
                    headerTable.AddCell(rightCell);
                    doc.Add(headerTable);

                    doc.Add(new Paragraph(" ", smallFont));

                    // Divider
                    var divider = new PdfPTable(1) { WidthPercentage = 100f };
                    var div = new PdfPCell(new Phrase(" ", smallFont))
                    {
                        BackgroundColor = new BaseColor(13, 110, 253),
                        FixedHeight = 3f,
                        Border = Rectangle.NO_BORDER
                    };
                    divider.AddCell(div);
                    doc.Add(divider);
                    doc.Add(new Paragraph(" ", smallFont));

                    // ===== BILL TO / META =====
                    string customerName = !string.IsNullOrWhiteSpace(d.DirectCustomerName)
                        ? d.DirectCustomerName
                        : (d.CustomerName ?? "Walk-in Customer");
                    string customerMobile = !string.IsNullOrWhiteSpace(d.DirectCustomerMobile)
                        ? d.DirectCustomerMobile : d.CustomerMobile;
                    string customerAddress = !string.IsNullOrWhiteSpace(d.DirectCustomerAddress)
                        ? d.DirectCustomerAddress : d.CustomerAddress;

                    var metaTable = new PdfPTable(2) { WidthPercentage = 100f };
                    metaTable.SetWidths(new float[] { 1.4f, 1f });

                    var billTo = new PdfPCell { Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 8f };
                    billTo.AddElement(new Paragraph("BILL TO", labelFont));
                    billTo.AddElement(new Paragraph(customerName, valueFont));
                    if (!string.IsNullOrWhiteSpace(customerAddress))
                        billTo.AddElement(new Paragraph(customerAddress, smallFont));
                    if (!string.IsNullOrWhiteSpace(customerMobile))
                        billTo.AddElement(new Paragraph("Mob: " + customerMobile, smallFont));
                    metaTable.AddCell(billTo);

                    var metaRight = new PdfPCell { Border = Rectangle.BOX, BorderColor = new BaseColor(220, 220, 220), Padding = 8f };
                    metaRight.AddElement(MetaLine("Sale Type",   (d.SaleType ?? "-"),                       labelFont, valueFont));
                    metaRight.AddElement(MetaLine("Branch",      (branch != null ? branch.Name : "-"),      labelFont, valueFont));
                    metaRight.AddElement(MetaLine("Vehicle No",  (d.VehicleNo ?? "-"),                      labelFont, valueFont));
                    metaRight.AddElement(MetaLine("Driver",      (d.DriverName ?? "-"),                     labelFont, valueFont));
                    metaTable.AddCell(metaRight);
                    doc.Add(metaTable);

                    doc.Add(new Paragraph(" ", smallFont));

                    // ===== LINE ITEMS TABLE =====
                    var lineTable = new PdfPTable(7) { WidthPercentage = 100f };
                    lineTable.SetWidths(new float[] { 0.4f, 2.6f, 0.7f, 1f, 1f, 0.9f, 1.2f });

                    void AddHeader(string txt, int align)
                    {
                        lineTable.AddCell(new PdfPCell(new Phrase(txt, headerFont))
                        {
                            BackgroundColor = new BaseColor(13, 110, 253),
                            HorizontalAlignment = align,
                            Padding = 6f
                        });
                    }
                    AddHeader("#",        Element.ALIGN_CENTER);
                    AddHeader("Product",  Element.ALIGN_LEFT);
                    AddHeader("Qty",      Element.ALIGN_RIGHT);
                    AddHeader("Rate",     Element.ALIGN_RIGHT);
                    AddHeader("Discount", Element.ALIGN_RIGHT);
                    AddHeader("Unit",     Element.ALIGN_CENTER);
                    AddHeader("Amount",   Element.ALIGN_RIGHT);

                    int idx = 0;
                    decimal subtotal = 0m, totalDiscount = 0m;
                    foreach (var i in items)
                    {
                        idx++;
                        decimal baseSubtotal = i.Qty * i.BasePrice;
                        decimal lineDiscount = baseSubtotal - i.Amount;
                        subtotal      += baseSubtotal;
                        totalDiscount += lineDiscount;

                        lineTable.AddCell(BodyCell(idx.ToString(),                              Element.ALIGN_CENTER, bodyFont));
                        string prodLabel = (string.IsNullOrEmpty(i.ProductCode) ? "" : i.ProductCode + " - ") + i.ProductName;
                        lineTable.AddCell(BodyCell(prodLabel,                                   Element.ALIGN_LEFT,   bodyFont));
                        lineTable.AddCell(BodyCell(i.Qty.ToString("N0"),                        Element.ALIGN_RIGHT,  bodyFont));
                        lineTable.AddCell(BodyCell(i.BasePrice.ToString("N2"),                  Element.ALIGN_RIGHT,  bodyFont));
                        string discText = (i.DiscountPct > 0 ? i.DiscountPct.ToString("N0") + "%" : "")
                                       + (i.DiscountAmt > 0 ? (i.DiscountPct > 0 ? " + " : "") + i.DiscountAmt.ToString("N0") : "");
                        if (string.IsNullOrWhiteSpace(discText)) discText = "-";
                        lineTable.AddCell(BodyCell(discText,                                    Element.ALIGN_RIGHT,  bodyFont));
                        lineTable.AddCell(BodyCell(i.Unit ?? "",                                Element.ALIGN_CENTER, bodyFont));
                        lineTable.AddCell(BodyCell(i.Amount.ToString("N2"),                     Element.ALIGN_RIGHT,  bodyFont));
                    }

                    if (items.Count == 0)
                    {
                        var empty = new PdfPCell(new Phrase("No items.", bodyFont))
                            { Colspan = 7, HorizontalAlignment = Element.ALIGN_CENTER, Padding = 10f };
                        lineTable.AddCell(empty);
                    }

                    doc.Add(lineTable);

                    // ===== TOTALS =====
                    decimal grandTotal = items.Sum(x => x.Amount);

                    var totalsTable = new PdfPTable(2) { WidthPercentage = 50f, HorizontalAlignment = Element.ALIGN_RIGHT };
                    totalsTable.SetWidths(new float[] { 1f, 1f });
                    totalsTable.SpacingBefore = 8f;

                    void AddTotalRow(string lbl, string val, Font lf, Font vf, BaseColor bg)
                    {
                        var lc = new PdfPCell(new Phrase(lbl, lf)) { Padding = 5f, BorderColor = new BaseColor(220,220,220), BackgroundColor = bg };
                        var vc = new PdfPCell(new Phrase(val, vf)) { Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT, BorderColor = new BaseColor(220,220,220), BackgroundColor = bg };
                        totalsTable.AddCell(lc); totalsTable.AddCell(vc);
                    }

                    AddTotalRow("Subtotal",  subtotal.ToString("N2"),       valueFont, valueFont, BaseColor.WHITE);
                    AddTotalRow("Discount", "-" + totalDiscount.ToString("N2"), valueFont, valueFont, BaseColor.WHITE);
                    AddTotalRow("TOTAL",     grandTotal.ToString("N2"),     totalFont, totalFont, new BaseColor(232, 240, 254));

                    doc.Add(totalsTable);

                    // ===== REMARKS =====
                    if (!string.IsNullOrWhiteSpace(d.Remarks))
                    {
                        doc.Add(new Paragraph(" ", smallFont));
                        var remarksP = new Paragraph();
                        remarksP.Add(new Chunk("Remarks: ", labelFont));
                        remarksP.Add(new Chunk(d.Remarks, valueFont));
                        doc.Add(remarksP);
                    }

                    // ===== SIGNATURE LINES =====
                    doc.Add(new Paragraph(" ", smallFont));
                    doc.Add(new Paragraph(" ", smallFont));
                    doc.Add(new Paragraph(" ", smallFont));

                    var sigTable = new PdfPTable(2) { WidthPercentage = 100f };
                    sigTable.SetWidths(new float[] { 1f, 1f });
                    var sigLeft = new PdfPCell {
                        Border = Rectangle.TOP_BORDER,
                        BorderColorTop = BaseColor.BLACK,
                        Padding = 4f,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    };
                    sigLeft.AddElement(new Paragraph("Customer Signature", smallFont) { Alignment = Element.ALIGN_CENTER });
                    sigTable.AddCell(sigLeft);

                    var sigRight = new PdfPCell {
                        Border = Rectangle.TOP_BORDER,
                        BorderColorTop = BaseColor.BLACK,
                        Padding = 4f,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    };
                    sigRight.AddElement(new Paragraph("Authorized Signature", smallFont) { Alignment = Element.ALIGN_CENTER });
                    sigTable.AddCell(sigRight);
                    doc.Add(sigTable);

                    // ===== FOOTER =====
                    var footer = new Paragraph(
                        "Generated " + DateTime.Now.ToString("dd MMM yyyy HH:mm")
                        + (d.IsReversed == true ? " · ** REVERSED **" : ""),
                        FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY))
                    { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 10f };
                    doc.Add(footer);

                    doc.Close();

                    var bytes = ms.ToArray();
                    Response.Headers.Add("Content-Disposition",
                        "inline; filename=SaleBill-INV-" + d.Id.ToString("D6") + ".pdf");
                    return File(bytes, "application/pdf");
                }
            }
            catch (Exception ex)
            {
                return Content("Sale bill generation failed: " + ex.Message);
            }
        }

        private static Paragraph MetaLine(string label, string value, Font labelFont, Font valueFont)
        {
            var p = new Paragraph();
            p.Add(new Chunk(label + ": ", labelFont));
            p.Add(new Chunk(value ?? "-", valueFont));
            p.Leading = 12f;
            return p;
        }

        private static PdfPCell BodyCell(string text, int align, Font font)
        {
            return new PdfPCell(new Phrase(text ?? "-", font))
            {
                HorizontalAlignment = align,
                Padding = 5f,
                BorderColor = new BaseColor(220, 220, 220)
            };
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        //  Each endpoint accepts { ids: int[] } and returns the standard
        //  envelope: { successCount, failureCount, failures }. Per-row
        //  failures don't break the batch.
        // ─────────────────────────────────────────────────────────────
        public class DispatchBulkIdsDto { public List<int> Ids { get; set; } }

        private class DispatchBulkFailure
        {
            public int id;
            public string code;
            public string reason;
        }

        private object DoBulkDispatch(List<int> ids, Func<int, Dispatch, string> runOne)
        {
            var failures = new List<DispatchBulkFailure>();
            int ok = 0;

            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var dp = db.Dispatch.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (dp == null)
                {
                    failures.Add(new DispatchBulkFailure { id = id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = "DSP#" + id;
                try
                {
                    string err = runOne(id, dp);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new DispatchBulkFailure { id = id, code = code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new DispatchBulkFailure { id = id, code = code, reason = DescribeDispatchError(ex) });
                }
            }

            return new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures = failures.Select(f => new { id = f.id, code = f.code, reason = f.reason })
            };
        }

        [HttpPost, Route("bulk-post")]
        public ActionResult BulkPost(DispatchBulkIdsDto dto)
        {
            var data = DoBulkDispatch(dto?.Ids, (id, dp) =>
            {
                var res = PostDispatch(id) as JsonResult;
                if (res?.Data == null) return "Post failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk post completed.", data);
        }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(DispatchBulkIdsDto dto)
        {
            var data = DoBulkDispatch(dto?.Ids, (id, dp) =>
            {
                var res = ReverseDispatch(id) as JsonResult;
                if (res?.Data == null) return "Reverse failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk reverse completed.", data);
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(DispatchBulkIdsDto dto)
        {
            var data = DoBulkDispatch(dto?.Ids, (id, dp) =>
            {
                var res = Delete(id) as JsonResult;
                if (res?.Data == null) return "Delete failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk delete completed.", data);
        }

    }
}

