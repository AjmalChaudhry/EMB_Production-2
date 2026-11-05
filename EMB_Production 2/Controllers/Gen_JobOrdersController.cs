
using System;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using System.IO;
using OfficeOpenXml;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Collections.Generic;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/joborders")]
    public class Gen_JobOrdersController : BaseController
    {
        // ========================= LIST =========================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            string activeFilter = "",
            int page = 1,
            int pageSize = 10,
            string fromDate = null,
            string toDate = null,
            int? customerId = null,
            int? branchId = null,
            string posted = null,
            string orderType = null,
            string priority = null,
            string status = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 10;

                // Phase M.0 - parse date range filters defensively
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string postedUp     = (posted ?? "").Trim().ToUpper();
                string orderTypeUp  = (orderType ?? "").Trim().ToUpper();
                string priorityUp   = (priority ?? "").Trim().ToUpper();
                string statusUp     = (status ?? "").Trim().ToUpper();

                var q = db.Gen_JobOrders
                    .Include(j => j.Branches)
                    .Include(j => j.Gen_Design)
                    .Include(j => j.Hr_Customers)
                    .Where(j => j.Deleted != true);

                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    if (activeFilter == "true")
                        q = q.Where(x => x.Active == true);
                    else if (activeFilter == "false")
                        q = q.Where(x => x.Active == false);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    q = q.Where(x =>
                        x.JobOrderCode.Contains(search) ||
                        (x.Gen_Design != null && x.Gen_Design.CollectionName.Contains(search)) ||
                        (x.Branches != null && x.Branches.Name.Contains(search)) ||
                        (x.Hr_Customers != null && x.Hr_Customers.Customer_Name.Contains(search)) ||
                        (x.Hr_Customers != null && x.Hr_Customers.Customer_Id.Contains(search))
                    );
                }

                // Phase M.0 - new filters
                if (fromDt.HasValue) q = q.Where(x => x.OrderDate >= fromDt.Value);
                if (toDt.HasValue)   q = q.Where(x => x.OrderDate <= toDt.Value);
                if (customerId.HasValue && customerId.Value > 0) q = q.Where(x => x.CustomerId == customerId.Value);
                if (branchId.HasValue && branchId.Value > 0)     q = q.Where(x => x.BranchId == branchId.Value);
                if (postedUp == "POSTED")     q = q.Where(x => x.IsPosted == true);
                else if (postedUp == "DRAFT") q = q.Where(x => x.IsPosted != true);
                if (orderTypeUp == "CUSTOMER")   q = q.Where(x => x.IsCompanyStockOrder == false);
                else if (orderTypeUp == "STOCK") q = q.Where(x => x.IsCompanyStockOrder == true);
                if (!string.IsNullOrEmpty(priorityUp))
                    q = q.Where(x => x.Priority != null && x.Priority.ToUpper() == priorityUp);
                if (!string.IsNullOrEmpty(statusUp))
                    q = q.Where(x => x.Status != null && x.Status.ToUpper() == statusUp);

                var total = q.Count();

                var rawList = q
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ────────────────────────────────────────────────────────
                //  Batch lookups for the page (Sprint-1 N+1 fix).
                //  Previously each row triggered its own product lookup and
                //  WorkOrders aggregate — 2 round-trips per row, ~40+ DB hits
                //  per 20-row page. Now we issue three queries total: one
                //  product map, one WO-usage map, and one DailyProduction
                //  produced-qty map. The per-row Select then reads from
                //  in-memory dictionaries.
                // ────────────────────────────────────────────────────────
                var pageJoIds = rawList.Select(x => x.Id).ToList();
                var pageProductIds = rawList.Select(x => x.ProductId).Distinct().ToList();

                var productMap = db.Gen_Products
                    .Where(p => pageProductIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Prod_Code, p.Prod_Name })
                    .ToList()
                    .ToDictionary(p => p.Id, p => new { p.Prod_Code, p.Prod_Name });

                // WO usage: sum of OrderQty across non-deleted WorkOrders per JO.
                var woUsageMap = db.WorkOrders
                    .Where(w => w.JobOrderId.HasValue
                             && pageJoIds.Contains(w.JobOrderId.Value)
                             && w.Deleted != true)
                    .GroupBy(w => w.JobOrderId.Value)
                    .Select(g => new { JobOrderId = g.Key, UsedQty = g.Sum(w => (int?)w.OrderQty) ?? 0 })
                    .ToDictionary(x => x.JobOrderId, x => x.UsedQty);

                // HOTFIX: produced qty was summing DailyProduction.AcceptedQty
                // per row, which over-counts by the BOM-item count of each
                // WO (5-component WO of 500 pieces showed 2,500 produced).
                //
                // The canonical signal is WorkOrderItems.CompletedQty —
                // every item of a WO completes together (one embroidery
                // batch produces all components), so take MAX per WO and
                // sum across the JO's WOs.
                var producedMap = (from wi in db.WorkOrderItems
                                   join wo in db.WorkOrders on wi.WorkOrderId equals wo.Id
                                   where wo.JobOrderId.HasValue
                                      && pageJoIds.Contains(wo.JobOrderId.Value)
                                      && wi.Deleted != true
                                      && wo.Deleted != true
                                   group wi by new { wo.JobOrderId, wo.Id } into wog
                                   select new
                                   {
                                       JobOrderId = wog.Key.JobOrderId.Value,
                                       WoId       = wog.Key.Id,
                                       PerWoQty   = wog.Max(x => (int?)x.CompletedQty) ?? 0
                                   })
                                   .ToList()
                                   .GroupBy(x => x.JobOrderId)
                                   .ToDictionary(g => g.Key, g => g.Sum(x => x.PerWoQty));

                // Sprint-2 quick-jump counts. Three batched group-bys, one
                // per related module, so the row can render clickable badges
                // showing N linked WOs / Plans / Dispatches.
                var woCountMap = db.WorkOrders
                    .Where(w => w.JobOrderId.HasValue
                             && pageJoIds.Contains(w.JobOrderId.Value)
                             && w.Deleted != true)
                    .GroupBy(w => w.JobOrderId.Value)
                    .Select(g => new { JobOrderId = g.Key, Count = g.Count() })
                    .ToDictionary(x => x.JobOrderId, x => x.Count);

                // Plans: count DISTINCT PlanId per JO so a plan with multiple
                // lines for the same JO still counts as one plan.
                var planCountMap = db.ProductionPlanItems
                    .Where(pi => pi.JobOrderId.HasValue
                              && pageJoIds.Contains(pi.JobOrderId.Value)
                              && pi.Deleted != true)
                    .GroupBy(pi => pi.JobOrderId.Value)
                    .Select(g => new { JobOrderId = g.Key, Count = g.Select(x => x.PlanId).Distinct().Count() })
                    .ToDictionary(x => x.JobOrderId, x => x.Count);

                var dispatchCountMap = db.Dispatch
                    .Where(d => d.JobOrderId.HasValue
                             && pageJoIds.Contains(d.JobOrderId.Value)
                             && d.Deleted != true)
                    .GroupBy(d => d.JobOrderId.Value)
                    .Select(g => new { JobOrderId = g.Key, Count = g.Count() })
                    .ToDictionary(x => x.JobOrderId, x => x.Count);

                var list = rawList.Select(x =>
                {
                    int usedQty   = woUsageMap.ContainsKey(x.Id) ? woUsageMap[x.Id] : 0;
                    int producedQty = producedMap.ContainsKey(x.Id) ? producedMap[x.Id] : 0;

                    var remainingQty = x.OrderQty - usedQty;
                    if (remainingQty < 0) remainingQty = 0;

                    decimal completionPercent = x.OrderQty > 0
                        ? Math.Round((decimal)producedQty / x.OrderQty * 100m, 1)
                        : 0m;
                    if (completionPercent > 100m) completionPercent = 100m;

                    string prodCode = "", prodName = "";
                    if (productMap.ContainsKey(x.ProductId))
                    {
                        prodCode = productMap[x.ProductId].Prod_Code;
                        prodName = productMap[x.ProductId].Prod_Name;
                    }

                    return new JobOrderRowViewModel
                    {
                        Id = x.Id,
                        JobOrderCode = x.JobOrderCode,

                        OrderType = x.IsCompanyStockOrder ? "CompanyStock" : (x.OrderType ?? "Customer"),
                        IsCompanyStockOrder = x.IsCompanyStockOrder,

                        CustomerId = x.IsCompanyStockOrder ? (int?)null : x.CustomerId,
                        CustomerName = x.IsCompanyStockOrder ? "Stock" : (x.Hr_Customers != null ? x.Hr_Customers.Customer_Name : ""),
                        CustomerCode = x.IsCompanyStockOrder ? "STOCK" : (x.Hr_Customers != null ? x.Hr_Customers.Customer_Id : ""),

                        DesignId = x.DesignId,
                        DesignName = x.Gen_Design != null ? x.Gen_Design.CollectionName : "",

                        ProductId = x.ProductId,
                        ProductCode = prodCode,
                        ProductName = prodName,

                        BranchId = x.BranchId,
                        BranchName = x.Branches != null ? x.Branches.Name : "",

                        OrderQty = x.OrderQty,
                        RemainingQty = remainingQty,

                        ProducedQty = producedQty,
                        CompletionPercent = completionPercent,

                        WoCount             = woCountMap.ContainsKey(x.Id)       ? woCountMap[x.Id]       : 0,
                        ProductionPlanCount = planCountMap.ContainsKey(x.Id)     ? planCountMap[x.Id]     : 0,
                        DispatchCount       = dispatchCountMap.ContainsKey(x.Id) ? dispatchCountMap[x.Id] : 0,

                        OrderDate = x.OrderDate,
                        OrderDateText = x.OrderDate.ToString("yyyy-MM-dd"),
                        DueDate = x.DueDate,
                        DueDateText = x.DueDate.HasValue
                                        ? x.DueDate.Value.ToString("yyyy-MM-dd")
                                        : null,

                        Priority = x.Priority,
                        Status = x.Status,

                        Active = x.Active,
                        Deleted = x.Deleted,

                        IsPosted = x.IsPosted,
                        PostedBy = x.PostedBy,
                        PostedDate = x.PostedDate,
                        PostedDateText = x.PostedDate.HasValue
                            ? x.PostedDate.Value.ToString("yyyy-MM-dd HH:mm")
                            : "",

                        CreatedBy = x.CreatedBy,
                        CreatedDate = x.CreatedDate,

                        UpdatedBy = x.UpdatedBy,
                        UpdatedDate = x.UpdatedDate
                    };
                }).ToList();

                // Phase M.0 - summary aggregates across the filtered set
                var summaryRows = q.Select(x => new
                {
                    x.OrderQty,
                    x.IsPosted,
                    x.IsCompanyStockOrder,
                    x.Status,
                    x.Priority
                }).ToList();

                var summary = new
                {
                    count       = summaryRows.Count,
                    totalQty    = summaryRows.Sum(s => s.OrderQty),
                    postedCount = summaryRows.Count(s => s.IsPosted),
                    draftCount  = summaryRows.Count(s => !s.IsPosted),
                    customerOrderCount = summaryRows.Count(s => !s.IsCompanyStockOrder),
                    stockOrderCount    = summaryRows.Count(s => s.IsCompanyStockOrder),
                    byStatus = summaryRows
                        .GroupBy(s => string.IsNullOrWhiteSpace(s.Status) ? "Unknown" : s.Status.Trim())
                        .Select(g => new { status = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList(),
                    byPriority = summaryRows
                        .Where(s => !string.IsNullOrWhiteSpace(s.Priority))
                        .GroupBy(s => s.Priority.Trim())
                        .Select(g => new { priority = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                };

                return Json(new
                {
                    success = true,
                    message = "Job orders loaded successfully.",
                    data = new
                    {
                        items = list,
                        summary,
                        page,
                        pageSize,
                        totalItems = total,
                        totalPages = (int)Math.Ceiling((double)total / pageSize)
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Failed to load job orders.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ========================= DETAIL =========================
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var raw = (
                    from jo in db.Gen_JobOrders

                    join d in db.Gen_Design on jo.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join p in db.Gen_Products on jo.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()

                    join b in db.Branches on jo.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()

                    join c1 in db.Hr_Customers on jo.CustomerId equals c1.Id into cust1
                    from c1 in cust1.DefaultIfEmpty()

                    join c2 in db.Hr_Customers on d.CustomerId equals c2.Id into cust2
                    from c2 in cust2.DefaultIfEmpty()

                    join cu in db.Adm_Users on jo.CreatedBy equals cu.Id into cu1
                    from cu in cu1.DefaultIfEmpty()

                    join uu in db.Adm_Users on jo.UpdatedBy equals uu.Id into uu1
                    from uu in uu1.DefaultIfEmpty()

                    where jo.Id == id && jo.Deleted != true

                    select new
                    {
                        jo.Id,
                        jo.JobOrderCode,

                        jo.OrderType,
                        jo.IsCompanyStockOrder,

                        jo.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",

                        ProductId = jo.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",

                        jo.BranchId,
                        BranchName = b != null ? b.Name : "",

                        CustomerId = jo.IsCompanyStockOrder ? null : jo.CustomerId,
                        CustomerCode = jo.IsCompanyStockOrder ? "STOCK" : (c1 != null ? c1.Customer_Id : ""),
                        CustomerName = jo.IsCompanyStockOrder ? "Stock" : (c1 != null ? c1.Customer_Name : ""),

                        jo.OrderQty,
                        jo.OrderDate,
                        jo.DueDate,
                        jo.Priority,
                        jo.Status,
                        jo.Active,

                        jo.IsPosted,
                        jo.PostedBy,
                        jo.PostedDate,

                        jo.CreatedBy,
                        CreatedByEmployeeId = cu != null ? (int?)cu.EmployeeId : null,
                        jo.CreatedDate,

                        jo.UpdatedBy,
                        UpdatedByEmployeeId = uu != null ? (int?)uu.EmployeeId : null,
                        jo.UpdatedDate
                    }
                ).FirstOrDefault();

                if (raw == null)
                    return JsonError("Job order not found.");

                var consumedQty = db.WorkOrders
                    .Where(x => x.JobOrderId == raw.Id && x.Deleted != true)
                    .Select(x => (int?)x.OrderQty)
                    .DefaultIfEmpty(0)
                    .Sum() ?? 0;

                var remainingQty = raw.OrderQty - consumedQty;
                if (remainingQty < 0) remainingQty = 0;

                var items = (
                    from i in db.Gen_JobOrderItems
                    join comp in db.Gen_Components on i.CompId equals comp.Id into comp1
                    from comp in comp1.DefaultIfEmpty()
                    where i.JobOrderId == raw.Id && i.Deleted != true
                    select new
                    {
                        i.Id,
                        ComponentCodeValue = comp != null ? comp.Code : null,
                        ComponentName = comp != null ? comp.Name : "",
                        i.ReqRepeats,
                        i.ReqHead,
                        i.Panni,
                        i.Solving
                    }
                )
                .ToList()
                .Select(x => new
                {
                    x.Id,
                    ComponentCode = x.ComponentCodeValue.HasValue ? x.ComponentCodeValue.Value.ToString() : "",
                    x.ComponentName,
                    x.ReqRepeats,
                    x.ReqHead,
                    x.Panni,
                    x.Solving
                })
                .ToList();

                var model = new
                {
                    raw.Id,
                    raw.JobOrderCode,

                    raw.OrderType,
                    raw.IsCompanyStockOrder,

                    raw.CustomerId,
                    raw.CustomerCode,
                    raw.CustomerName,

                    raw.DesignId,
                    raw.DesignCode,
                    DesignName = ((raw.DesignCode ?? "") + " - " + (raw.DesignName ?? "")).Trim(),

                    raw.ProductId,
                    raw.ProductCode,
                    raw.ProductName,

                    raw.BranchId,
                    raw.BranchName,

                    raw.OrderQty,
                    RemainingQty = remainingQty,

                    raw.OrderDate,
                    raw.DueDate,

                    raw.Priority,
                    raw.Status,
                    raw.Active,

                    raw.IsPosted,
                    raw.PostedBy,
                    raw.PostedDate,
                    PostedDateText = raw.PostedDate.HasValue
                    ? raw.PostedDate.Value.ToString("yyyy-MM-dd HH:mm")
                    : "",

                    raw.CreatedBy,
                    raw.CreatedByEmployeeId,
                    raw.CreatedDate,
                    CreatedDateText = raw.CreatedDate.ToString("yyyy-MM-dd"),

                    raw.UpdatedBy,
                    raw.UpdatedByEmployeeId,
                    raw.UpdatedDate,
                    UpdatedDateText = raw.UpdatedDate.HasValue
                        ? raw.UpdatedDate.Value.ToString("yyyy-MM-dd")
                        : "",

                    Items = items
                };

                return JsonSuccess("Job order detail loaded successfully.", model);
            }
            catch (Exception ex)
            {
                return JsonError(ex.Message);
            }
        }
                
        // ──────────────────────────────────────────────────────────────────
        //  SPRINT-5  SHARED VALIDATION HELPER
        //  Used by both Create and Update to enforce the same field /
        //  referential checks (customer, product, design, branch, components).
        //  Previously these ~50 lines were copy-pasted into both endpoints, so
        //  a bug fixed in one would not propagate to the other.
        // ──────────────────────────────────────────────────────────────────
        private class JobOrderValidationResult
        {
            public string ErrorMessage     { get; set; }   // null on success
            public bool   IsCompanyStock   { get; set; }
            public string NormalizedOrderType { get; set; }
        }

        private JobOrderValidationResult ValidateJobOrderDto(JobOrderDto dto)
        {
            if (dto == null)
                return new JobOrderValidationResult { ErrorMessage = "Invalid payload." };

            bool isCompanyStock =
                dto.IsCompanyStockOrder ||
                string.Equals(dto.OrderType, "CompanyStock", StringComparison.OrdinalIgnoreCase);
            string orderType = isCompanyStock ? "CompanyStock" : "Customer";

            if (!isCompanyStock && (!dto.CustomerId.HasValue || dto.CustomerId.Value <= 0))
                return new JobOrderValidationResult { ErrorMessage = "Customer is required." };
            if (dto.ProductId <= 0)
                return new JobOrderValidationResult { ErrorMessage = "Product is required." };
            if (dto.DesignId <= 0)
                return new JobOrderValidationResult { ErrorMessage = "Design is required." };
            if (!dto.BranchId.HasValue || dto.BranchId.Value <= 0)
                return new JobOrderValidationResult { ErrorMessage = "Branch is required." };
            if (dto.OrderQty <= 0)
                return new JobOrderValidationResult { ErrorMessage = "Order quantity must be greater than zero." };

            if (!isCompanyStock)
            {
                var customerExists = db.Hr_Customers.Any(x =>
                    x.Id == dto.CustomerId.Value && x.Deleted != true && x.Active == true);
                if (!customerExists)
                    return new JobOrderValidationResult { ErrorMessage = "Selected customer is invalid or inactive." };
            }

            var productExists = db.Gen_Products.Any(x =>
                x.Id == dto.ProductId && x.Deleted != true && x.Active == true);
            if (!productExists)
                return new JobOrderValidationResult { ErrorMessage = "Selected product is invalid or inactive." };

            var design = db.Gen_Design.FirstOrDefault(x =>
                x.Id == dto.DesignId && x.Deleted != true && x.Active == true);
            if (design == null)
                return new JobOrderValidationResult { ErrorMessage = "Selected design is invalid or inactive." };
            if (design.ProductId != dto.ProductId)
                return new JobOrderValidationResult { ErrorMessage = "Selected design does not belong to selected product." };

            // Job orders must inherit components from the design. Block create
            // and update if the design has none — otherwise the JO is unusable
            // for production (no items to track).
            bool designHasComponents = db.Gen_Components.Any(c =>
                c.D_Code == dto.DesignId && c.Deleted != true && c.Active == true);
            if (!designHasComponents)
                return new JobOrderValidationResult {
                    ErrorMessage = "This design has no components. Add components on the Design Profile page before creating a Job Order."
                };

            var branchExists = db.Branches.Any(x =>
                x.Id == dto.BranchId.Value && x.Deleted != true && x.IsActive == true);
            if (!branchExists)
                return new JobOrderValidationResult { ErrorMessage = "Selected branch is invalid or inactive." };

            return new JobOrderValidationResult {
                ErrorMessage         = null,
                IsCompanyStock       = isCompanyStock,
                NormalizedOrderType  = orderType
            };
        }

        [HttpPost]
        [Route("create")]
        public ActionResult Create(JobOrderDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-5: shared validator replaces the inlined ~50 lines
                    // that used to live here AND in Update().
                    var validation = ValidateJobOrderDto(dto);
                    if (validation.ErrorMessage != null)
                        return JsonError(validation.ErrorMessage);

                    bool   isCompanyStock = validation.IsCompanyStock;
                    string orderType      = validation.NormalizedOrderType;

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var entity = new Gen_JobOrders
                    {
                        JobOrderCode = GenerateJobOrderCode(),

                        OrderType = orderType,
                        IsCompanyStockOrder = isCompanyStock,

                        CustomerId = isCompanyStock ? (int?)null : dto.CustomerId.Value,

                        ProductId = dto.ProductId,
                        DesignId = dto.DesignId,
                        BranchId = dto.BranchId.Value,

                        OrderQty = dto.OrderQty,
                        Priority = string.IsNullOrWhiteSpace(dto.Priority) ? "Normal" : dto.Priority.Trim(),
                        Status = string.IsNullOrWhiteSpace(dto.Status) ? "Draft" : dto.Status.Trim(),

                        OrderDate = now,
                        DueDate = now.AddDays(7),

                        IsPosted = false,
                        PostedBy = null,
                        PostedDate = null,

                        Active = dto.Active,
                        Deleted = false,

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = null,
                        UpdatedDate = null
                    };

                    db.Gen_JobOrders.Add(entity);
                    // Sprint-5: race-safe save with code-collision retry.
                    SaveNewJobOrderWithCodeRetry(entity);

                    GenerateJobOrderItemsFromBom(entity.Id, dto.DesignId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    // Sprint-4 audit
                    LogAudit(entity.Id, "Created", userId,
                        detail: "Code " + entity.JobOrderCode + ", Qty " + entity.OrderQty);

                    return Json(new
                    {
                        success = true,
                        message = "Job order created successfully.",
                        data = new
                        {
                            id = entity.Id,
                            code = entity.JobOrderCode
                        }
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();

                    return Json(new
                    {
                        success = false,
                        message = ex.InnerException != null ? ex.InnerException.Message : ex.Message
                    });
                }
            }
        }

        [HttpPost]
        [Route("update")]
        public ActionResult Update(JobOrderDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null || dto.Id <= 0)
                        return JsonError("Invalid payload.");

                    var j = db.Gen_JobOrders
                        .FirstOrDefault(x => x.Id == dto.Id && x.Deleted != true);

                    if (j == null)
                        return JsonError("Job order not found.");

                    if (j.IsPosted)
                        return JsonError("Posted job order cannot be edited. Reverse post first.");

                    // Sprint-5: shared validator replaces the inlined ~50 lines
                    // that used to live here AND in Create().
                    var validation = ValidateJobOrderDto(dto);
                    if (validation.ErrorMessage != null)
                        return JsonError(validation.ErrorMessage);

                    bool   isCompanyStock = validation.IsCompanyStock;
                    string orderType      = validation.NormalizedOrderType;

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    // Sprint-4 audit: snapshot key fields BEFORE mutation so the
                    // log entry can show the actual delta.
                    string oldSnapshot =
                        "Qty=" + j.OrderQty +
                        ", Priority=" + (j.Priority ?? "") +
                        ", Status=" + (j.Status ?? "") +
                        ", DesignId=" + j.DesignId +
                        ", BranchId=" + j.BranchId;

                    j.OrderType = orderType;
                    j.IsCompanyStockOrder = isCompanyStock;
                    j.CustomerId = isCompanyStock ? (int?)null : dto.CustomerId.Value;

                    j.ProductId = dto.ProductId;
                    j.DesignId = dto.DesignId;
                    j.BranchId = dto.BranchId.Value;

                    j.OrderQty = dto.OrderQty;
                    j.Priority = string.IsNullOrWhiteSpace(dto.Priority) ? "Normal" : dto.Priority.Trim();
                    j.Status = string.IsNullOrWhiteSpace(dto.Status) ? "Draft" : dto.Status.Trim();
                    j.Active = dto.Active;

                    j.UpdatedBy = userId;
                    j.UpdatedDate = now;

                    db.SaveChanges();

                    GenerateJobOrderItemsFromBom(j.Id, j.DesignId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    // Sprint-4 audit (after commit so it reflects the persisted state)
                    string newSnapshot =
                        "Qty=" + j.OrderQty +
                        ", Priority=" + (j.Priority ?? "") +
                        ", Status=" + (j.Status ?? "") +
                        ", DesignId=" + j.DesignId +
                        ", BranchId=" + j.BranchId;
                    LogAudit(j.Id, "Updated", userId, oldValue: oldSnapshot, newValue: newSnapshot);

                    return Json(new
                    {
                        success = true,
                        message = "Job order updated successfully. Components synced successfully."
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();

                    return Json(new
                    {
                        success = false,
                        message = "Error updating job order.",
                        detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message
                    });
                }
            }
        }

        // ========================= Phase M.1 - JOB ORDER 360 PROFILE =========================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                // 1. Header
                var head = (
                    from jo in db.Gen_JobOrders
                    join d in db.Gen_Design on jo.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()
                    join b in db.Branches on jo.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()
                    join p in db.Gen_Products on jo.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()
                    join c in db.Hr_Customers on jo.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    where jo.Id == id && jo.Deleted != true
                    select new
                    {
                        jo.Id,
                        jo.JobOrderCode,
                        jo.OrderType,
                        jo.IsCompanyStockOrder,
                        jo.IsPosted,
                        jo.Active,
                        jo.Status,
                        jo.Priority,
                        jo.OrderQty,
                        jo.OrderDate,
                        jo.DueDate,
                        jo.BranchId,
                        BranchName = b != null ? b.Name : "",
                        jo.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",
                        jo.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",
                        jo.CustomerId,
                        CustomerName = c != null ? c.Customer_Name : "",
                        CustomerCode = c != null ? c.Customer_Id : "",
                        jo.CreatedBy,
                        jo.CreatedDate,
                        jo.UpdatedBy,
                        jo.UpdatedDate,
                        jo.PostedDate
                    }
                ).FirstOrDefault();

                if (head == null) return JsonError("Job order not found.");

                var header = new
                {
                    id = head.Id,
                    jobOrderCode = head.JobOrderCode,
                    orderType = head.OrderType,
                    isCompanyStockOrder = head.IsCompanyStockOrder,
                    isPosted = head.IsPosted,
                    active = head.Active,
                    status = head.Status,
                    priority = head.Priority,
                    orderQty = head.OrderQty,
                    orderDateText = head.OrderDate.ToString("dd-MMM-yyyy"),
                    dueDateText = head.DueDate.HasValue ? head.DueDate.Value.ToString("dd-MMM-yyyy") : null,
                    branchId = head.BranchId,
                    branchName = head.BranchName,
                    designId = head.DesignId,
                    designCode = head.DesignCode,
                    designName = head.DesignName,
                    productId = head.ProductId,
                    productCode = head.ProductCode,
                    productName = head.ProductName,
                    customerId = head.CustomerId,
                    customerName = head.IsCompanyStockOrder ? "Stock" : head.CustomerName,
                    customerCode = head.IsCompanyStockOrder ? "STOCK" : head.CustomerCode,
                    createdDate = head.CreatedDate.ToString("dd-MMM-yyyy HH:mm"),
                    updatedDate = head.UpdatedDate.HasValue ? head.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    postedDateText = head.PostedDate.HasValue ? head.PostedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // 2. JobOrder Items
                var joItems = db.Gen_JobOrderItems
                    .Where(x => x.JobOrderId == id)
                    .ToList()
                    .Select(x => new
                    {
                        id = x.Id,
                        productId = (int?)null,
                        qty = (decimal?)null,
                        remarks = (string)null
                    })
                    .ToList();

                // 3. Linked WorkOrders
                var linkedWOs = (
                    from w in db.WorkOrders
                    join b in db.Branches on w.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()
                    join c in db.Hr_Customers on w.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    where w.JobOrderId == id && w.Deleted != true
                    orderby w.Id descending
                    select new
                    {
                        w.Id,
                        w.WorkOrderCode,
                        w.OrderDate,
                        w.DueDate,
                        w.OrderQty,
                        w.Status,
                        w.IsPosted,
                        w.IsCompanyStockOrder,
                        BranchName = b != null ? b.Name : "",
                        CustomerName = c != null ? c.Customer_Name : ""
                    }
                ).ToList();

                int woCount = linkedWOs.Count;
                decimal woTotalQty = linkedWOs.Sum(w => (decimal)(w.OrderQty ?? 0));
                int woPostedCount = linkedWOs.Count(w => w.IsPosted);

                var workOrdersOut = linkedWOs.Select(w => new
                {
                    id = w.Id,
                    workOrderCode = w.WorkOrderCode,
                    orderDateText = w.OrderDate.ToString("dd-MMM-yyyy"),
                    dueDateText = w.DueDate.HasValue ? w.DueDate.Value.ToString("dd-MMM-yyyy") : null,
                    orderQty = w.OrderQty ?? 0,
                    status = w.Status,
                    isPosted = w.IsPosted,
                    isCompanyStockOrder = w.IsCompanyStockOrder,
                    customerName = w.IsCompanyStockOrder ? "Stock" : w.CustomerName,
                    branchName = w.BranchName
                }).ToList();

                // 4. Production aggregates from this JO's WorkOrderItems.
                //
                // HOTFIX: CompletedQty and RejectedQty live on every BOM-derived
                // WorkOrderItem row of a WO, and production typically posts the
                // SAME number against each (one embroidery batch consumes every
                // component together). Summing across all items multiplied the
                // real qty by the BOM-item count — a 500-piece WO with 5 items
                // showed "Completed 2,500". Fix: take MAX per WorkOrderId
                // first (production-level signal), then sum across WOs.
                //
                // totalPrice / totalCost legitimately stay as per-item sums —
                // each item is a separate component cost contribution.
                var woiPerWo = db.WorkOrderItems
                    .Where(wi => wi.JobOrderId == id && wi.Deleted != true)
                    .GroupBy(wi => wi.WorkOrderId)
                    .Select(g => new
                    {
                        WoId            = g.Key,
                        ItemCount       = g.Count(),
                        ItemsTotalQty   = g.Sum(x => (decimal)x.Quantity),
                        ItemsCompletedMax = g.Max(x => (decimal?)x.CompletedQty) ?? 0m,
                        ItemsRejectedMax  = g.Max(x => (decimal?)x.RejectedQty) ?? 0m,
                        ItemsPriceSum   = g.Sum(x => x.Quantity * (x.UnitPrice ?? 0m)),
                        ItemsCostSum    = g.Sum(x => x.TotalCost ?? 0m)
                    })
                    .ToList();

                var woiAgg = woiPerWo.Count == 0
                    ? null
                    : new
                    {
                        itemCount    = woiPerWo.Sum(w => w.ItemCount),
                        totalQty     = woiPerWo.Sum(w => w.ItemsTotalQty),
                        completedQty = woiPerWo.Sum(w => w.ItemsCompletedMax),
                        rejectedQty  = woiPerWo.Sum(w => w.ItemsRejectedMax),
                        totalPrice   = woiPerWo.Sum(w => w.ItemsPriceSum),
                        totalCost    = woiPerWo.Sum(w => w.ItemsCostSum)
                    };

                var woStatusBreakdown = db.WorkOrderItems
                    .Where(wi => wi.JobOrderId == id && wi.Deleted != true)
                    .GroupBy(x => x.Status)
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .ToList()
                    .OrderByDescending(x => x.count)
                    .ToList();

                // 5. Dispatch trail by JobOrderId
                var dispatches = (
                    from d in db.Dispatch
                    join c in db.Hr_Customers on d.CustomerId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    where d.JobOrderId == id && d.Deleted != true
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
                var dispAggs = db.DispatchItems
                    .Where(i => dispIds.Contains(i.DispatchId) && i.Deleted != true)
                    .GroupBy(i => i.DispatchId)
                    .Select(g => new
                    {
                        dispatchId = g.Key,
                        qty = g.Sum(i => i.DispatchQty ?? 0m),
                        amount = g.Sum(i => i.Amount ?? 0m)
                    })
                    .ToList()
                    .ToDictionary(x => x.dispatchId, x => new { x.qty, x.amount });

                var dispatchTrail = dispatches.Select(d => new
                {
                    id = d.Id,
                    dispatchDateText = d.DispatchDate.ToString("dd-MMM-yyyy"),
                    dispatchType = d.DispatchType,
                    customerName = d.CustomerName,
                    vehicleNo = d.VehicleNo,
                    isPosted = d.IsPosted,
                    isReversed = d.IsReversed,
                    qty = dispAggs.ContainsKey(d.Id) ? dispAggs[d.Id].qty : 0m,
                    amount = dispAggs.ContainsKey(d.Id) ? dispAggs[d.Id].amount : 0m,
                    remarks = d.Remarks
                }).ToList();

                decimal dispatchedQty = dispAggs.Values.Sum(x => x.qty);

                // 6. KPIs
                var kpis = new
                {
                    orderQty = head.OrderQty,
                    woCount,
                    woTotalQty,
                    woPostedCount,
                    remainingQty = Math.Max(0, head.OrderQty - (int)woTotalQty),
                    itemCount = woiAgg != null ? woiAgg.itemCount : 0,
                    totalQty = woiAgg != null ? woiAgg.totalQty : 0m,
                    completedQty = woiAgg != null ? woiAgg.completedQty : 0m,
                    rejectedQty = woiAgg != null ? woiAgg.rejectedQty : 0m,
                    totalAmount = Math.Round(woiAgg != null ? woiAgg.totalPrice : 0m, 2),
                    totalCost = Math.Round(woiAgg != null ? woiAgg.totalCost : 0m, 2),
                    dispatchCount = dispatches.Count,
                    dispatchedQty
                };

                // Sprint-4: derive the workflow stage from data + the manual
                // Status override. Front-end stepper renders this against the
                // canonical WorkflowStages list.
                string workflowStage = ComputeWorkflowStage(
                    head.Id, head.IsPosted, head.Status, head.OrderQty);

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    items = joItems,
                    workOrders = workOrdersOut,
                    woStatusBreakdown,
                    dispatchTrail,
                    workflowStage,
                    workflowStages = WorkflowStages
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        // ========================= DELETE =========================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var j = db.Gen_JobOrders
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (j == null)
                    return JsonError("Job order not found.");

                if (j.IsPosted)
                    return JsonError("Posted job order cannot be deleted. Reverse post first.");

                int userId = GetCurrentUserId();
                j.Deleted = true;
                j.Active = false;
                j.UpdatedBy = userId;
                j.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                // Sprint-4 audit
                LogAudit(j.Id, "Deleted", userId, detail: "Code " + j.JobOrderCode);

                return Json(new
                {
                    success = true,
                    message = "Job order deleted successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Delete failed.",
                    detail = ex.Message
                });
            }
        }

        // ========================= NEXT CODE =========================
        [HttpGet]
        [Route("next-code")]
        public ActionResult GetNextCode()
        {
            int year = DateTime.Now.Year;
            string prefix = "JO-" + year + "-";

            var lastCode = db.Gen_JobOrders
                .Where(x => x.JobOrderCode.StartsWith(prefix))
                .OrderByDescending(x => x.Id)
                .Select(x => x.JobOrderCode)
                .FirstOrDefault();

            int nextNumber = 1;

            if (!string.IsNullOrWhiteSpace(lastCode))
            {
                var parts = lastCode.Split('-');
                int parsed;

                if (parts.Length == 3 && int.TryParse(parts[2], out parsed))
                    nextNumber = parsed + 1;
            }

            string code = prefix + nextNumber.ToString("D4");

            return JsonSuccess("Next job order code generated.", new
            {
                code
            });
        }

        // ========================= DDL =========================
        [HttpGet]
        [Route("ddl")]
        public ActionResult Ddl()
        {
            var items = db.Gen_JobOrders
                .Include(x => x.Gen_Design)
                .Include(x => x.Hr_Customers)
                .Where(x => x.Deleted != true && x.Active == true)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    text = x.JobOrderCode,

                    jobOrderCode = x.JobOrderCode,

                    customerId = x.CustomerId,
                    customerName = x.Hr_Customers != null ? x.Hr_Customers.Customer_Name : "",
                    customerCode = x.Hr_Customers != null ? x.Hr_Customers.Customer_Id : "",

                    designId = x.DesignId,
                    designName = x.Gen_Design != null ? x.Gen_Design.CollectionName : "",

                    orderQty = x.OrderQty,
                    priority = x.Priority,
                    status = x.Status
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = items
            }, JsonRequestBehavior.AllowGet);
        }

        // ========================= PRIVATE: CODE GENERATOR =========================
        // Computes the next free JO code based on MAX(JobOrderCode) for the
        // current year. Augmented with an optional skip offset so the
        // collision-retry loop in the caller can ask for "the next-next" code
        // without re-querying the database with the same answer.
        //
        // ────────────────────────────────────────────────────────────────────
        //  Concurrency note (Sprint-5 bug-fix sweep):
        //  This helper is NOT race-safe on its own — two concurrent Creates
        //  can read the same max value and propose the same next code. The
        //  collision is defended at two layers:
        //    1. UX_Gen_JobOrders_Code unique index (DDL provided separately)
        //       — DB rejects the second insert with a UNIQUE constraint
        //       violation.
        //    2. GenerateUniqueJobOrderCode() below retries via DbUpdateException
        //       handling, asking for the next free slot on every collision.
        //  Together they guarantee no duplicate code ever lands in the table.
        // ────────────────────────────────────────────────────────────────────
        private string GenerateJobOrderCode(int skip = 0)
        {
            int year = DateTime.Now.Year;
            string prefix = "JO-" + year + "-";

            var lastCode = db.Gen_JobOrders
                .Where(x => x.JobOrderCode.StartsWith(prefix))
                .OrderByDescending(x => x.Id)
                .Select(x => x.JobOrderCode)
                .FirstOrDefault();

            int nextNumber = 1;

            if (!string.IsNullOrEmpty(lastCode))
            {
                var parts = lastCode.Split('-');
                int lastNumber;

                if (parts.Length == 3 && int.TryParse(parts[2], out lastNumber))
                    nextNumber = lastNumber + 1;
            }

            return prefix + (nextNumber + skip).ToString("D4");
        }

        // True when the EF exception chain reveals a unique-constraint
        // violation on UX_Gen_JobOrders_Code (or any unique index on the
        // Code column). Used by the Create / Clone retry loops to decide
        // whether to bump the code and try again vs surface the error.
        private static bool IsJobOrderCodeUniqueViolation(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                var sql = e as SqlException;
                if (sql != null && (sql.Number == 2601 || sql.Number == 2627))
                {
                    string msg = sql.Message ?? "";
                    // SQL Server's unique-violation messages name the index;
                    // narrow to ours so an unrelated unique index doesn't
                    // accidentally trigger the retry loop.
                    if (msg.IndexOf("JobOrderCode", StringComparison.OrdinalIgnoreCase) >= 0
                     || msg.IndexOf("UX_Gen_JobOrders_Code", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        // Save a brand-new Gen_JobOrders entity, retrying with a bumped Code
        // if the DB's unique constraint rejects the first attempt. Caps at
        // maxRetries so a genuinely broken index doesn't spin forever.
        // Throws the original exception if the retry budget is exhausted or
        // the error isn't a code-collision.
        private void SaveNewJobOrderWithCodeRetry(Gen_JobOrders entity, int maxRetries = 5)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    db.SaveChanges();
                    return;
                }
                catch (DbUpdateException ex) when (attempt < maxRetries && IsJobOrderCodeUniqueViolation(ex))
                {
                    attempt++;
                    entity.JobOrderCode = GenerateJobOrderCode(skip: attempt);
                    // Loop and re-attempt SaveChanges with the bumped code.
                    // EF6 keeps the entity tracked in Added state, so just
                    // changing the property is enough for the next try.
                }
            }
        }

        // ========================= PRIVATE: BOM TO JOB ORDER ITEMS =========================
        private void GenerateJobOrderItemsFromBom(int jobOrderId, int designId, int userId, DateTime now)
        {
            var jobOrder = db.Gen_JobOrders.FirstOrDefault(x => x.Id == jobOrderId && x.Deleted != true);
            if (jobOrder == null) return;

            var designComponents = db.Gen_Components
                .Where(x =>
                    x.D_Code == designId &&
                    x.Deleted != true &&
                    x.Active == true)
                .ToList();

            var existingItems = db.Gen_JobOrderItems
                .Where(x => x.JobOrderId == jobOrderId)
                .ToList();

            // Phase 3.2: spec data lives in Gen_ComponentSpecs (latest PRODUCTION).
            var compIds = designComponents.Select(c => c.Id).ToList();
            var specs = db.Gen_ComponentSpecs
                .Where(s => compIds.Contains(s.ComponentId)
                         && s.Variant == "PRODUCTION"
                         && s.IsLatest
                         && !s.Deleted)
                .ToList();

            foreach (var comp in designComponents)
            {
                var item = existingItems.FirstOrDefault(x => x.CompId == comp.Id);
                var spec = specs.FirstOrDefault(s => s.ComponentId == comp.Id);

                var reqRepeats = 1;
                var reqHead = spec?.HeadToHead ?? 1;
                var panni = spec?.Tilla ?? 0;
                var solving = spec?.Stitch ?? 0;

                // Optional quantity-based calculation
                panni = panni * jobOrder.OrderQty;
                solving = solving * jobOrder.OrderQty;

                if (item == null)
                {
                    db.Gen_JobOrderItems.Add(new Gen_JobOrderItems
                    {
                        JobOrderId = jobOrderId,
                        CompId = comp.Id,

                        ReqRepeats = reqRepeats,
                        ReqHead = reqHead,
                        Panni = panni,
                        Solving = solving,

                        BranchId = jobOrder.BranchId,
                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now
                    });
                }
                else
                {
                    item.ReqRepeats = reqRepeats;
                    item.ReqHead = reqHead;
                    item.Panni = panni;
                    item.Solving = solving;

                    item.BranchId = jobOrder.BranchId;
                    item.Active = true;
                    item.Deleted = false;
                    item.UpdatedBy = userId;
                    item.UpdatedDate = now;
                }
            }

            foreach (var oldItem in existingItems)
            {
                bool stillExists = designComponents.Any(x => x.Id == oldItem.CompId);

                if (!stillExists)
                {
                    oldItem.Deleted = true;
                    oldItem.Active = false;
                    oldItem.UpdatedBy = userId;
                    oldItem.UpdatedDate = now;
                }
            }
        }

        // Sprint-5: SyncJobOrderItemsFromDesign() removed — it was a duplicate
        // of GenerateJobOrderItemsFromBom() (above) that no caller ever
        // referenced. Dead duplicates rot at different rates from the live
        // path, so the safest thing to do is delete them.

        [HttpGet]
        [Route("component-preview")]
        public ActionResult ComponentPreview(int designId, int orderQty = 0)
        {
            try
            {
                if (designId <= 0 || orderQty <= 0)
                    return Json(new { success = true, data = new object[] { } }, JsonRequestBehavior.AllowGet);

                // Source components directly from Gen_Components (single source of truth).
                // Spec numerics come from Gen_ComponentSpecs (latest PRODUCTION). Mirrors
                // the formula used by GenerateJobOrderItemsFromBom so the preview matches
                // what gets persisted on save.
                var comps = db.Gen_Components
                    .Where(c => c.D_Code == designId
                             && c.Deleted != true
                             && c.Active == true)
                    .OrderBy(c => c.Id)
                    .Select(c => new { c.Id, c.Code, c.Name })
                    .ToList();

                if (comps.Count == 0)
                    return Json(new { success = true, data = new object[] { } }, JsonRequestBehavior.AllowGet);

                var compIds = comps.Select(c => c.Id).ToList();
                var specs = db.Gen_ComponentSpecs
                    .Where(s => compIds.Contains(s.ComponentId)
                             && s.Variant == "PRODUCTION"
                             && s.IsLatest
                             && !s.Deleted)
                    .ToList();

                var list = comps.Select(c =>
                {
                    var spec = specs.FirstOrDefault(s => s.ComponentId == c.Id);
                    int tilla = spec?.Tilla ?? 0;
                    int stitch = spec?.Stitch ?? 0;
                    return new
                    {
                        code = c.Code,
                        componentName = c.Name,
                        reqRepeats = 1,
                        reqHead = spec?.HeadToHead ?? 1,
                        panni = tilla * orderQty,
                        solving = stitch * orderQty
                    };
                }).ToList();

                return Json(new { success = true, data = list }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    data = new object[] { }
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        [Route("customers")]
        public ActionResult Customers()
        {
            try
            {
                var items = db.Database.SqlQuery<CustomerDropdownVm>(@"
            SELECT
                Id,
                ISNULL(Customer_Id, '') AS CustomerCode,
                ISNULL(Customer_Name, '') AS CustomerName
            FROM dbo.Hr_Customers
            WHERE ISNULL(Deleted, 0) = 0
            ORDER BY Customer_Name
        ")
                .Select(x => new
                {
                    id = x.Id,
                    text = (x.CustomerCode + " - " + x.CustomerName).Trim(),
                    customerCode = x.CustomerCode,
                    customerName = x.CustomerName
                })
                .ToList();

                return Json(new
                {
                    success = true,
                    data = items
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    detail = ex.InnerException != null ? ex.InnerException.Message : ""
                }, JsonRequestBehavior.AllowGet);
            }
        }

        public class CustomerDropdownVm
        {
            public int Id { get; set; }
            public string CustomerCode { get; set; }
            public string CustomerName { get; set; }
        }

        [HttpGet]
        [Route("design-detail/{designId:int}")]
        public ActionResult DesignDetail(int designId)
        {
            var data = (
                from d in db.Gen_Design
                join p in db.Gen_Products on d.ProductId equals p.Id into p1
                from p in p1.DefaultIfEmpty()
                where d.Id == designId
                select new
                {
                    productId = d.ProductId,
                    productCode = p != null ? p.Prod_Code : "",
                    productName = p != null ? p.Prod_Name : ""
                }
            ).FirstOrDefault();

            return JsonSuccess("ok", data);
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

                    var jo = db.Gen_JobOrders.FirstOrDefault(x =>
                        x.Id == id &&
                        x.Deleted != true);

                    if (jo == null)
                        return JsonError("Job order not found.");

                    if (jo.IsPosted == true)
                        return JsonError("Job order already posted.");

                    if (jo.DesignId <= 0)   // ✅ FIXED (removed duplicate)
                        return JsonError("Design is required before posting.");

                    if (jo.OrderQty <= 0)   // ✅ FIXED (removed duplicate)
                        return JsonError("Order quantity is required before posting.");

                    // Auto create missing components before post
                    EnsureJobOrderComponents(jo, userId, now);

                    bool hasComponents = db.Gen_JobOrderItems.Any(x =>
                        x.JobOrderId == jo.Id &&
                        x.Deleted != true);

                    if (!hasComponents)
                        return JsonError("Cannot post. Job order components are missing.");

                    jo.IsPosted = true;
                    jo.PostedBy = userId;
                    jo.PostedDate = now;
                    jo.Status = "Posted";
                    jo.UpdatedBy = userId;
                    jo.UpdatedDate = now;

                    db.SaveChanges();
                    tx.Commit();

                    // Sprint-4 audit
                    LogAudit(jo.Id, "Posted", userId, detail: "Code " + jo.JobOrderCode);

                    return JsonSuccess("Job order posted successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Post failed: " + ex.Message);
                }
            }
        }

        private void EnsureJobOrderComponents(Gen_JobOrders jo, int userId, DateTime now)
        {
            bool alreadyExists = db.Gen_JobOrderItems.Any(x =>
                x.JobOrderId == jo.Id &&
                x.Deleted != true);

            if (alreadyExists)
                return;

            var bomItems = db.Gen_BOM
                .Where(x =>
                    x.DesignId == jo.DesignId &&
                    x.Deleted != true &&
                    (x.Active == true || x.Active == null))
                .OrderBy(x => x.Id)
                .ToList();

            foreach (var b in bomItems)
            {
                var item = new Gen_JobOrderItems
                {
                    JobOrderId = jo.Id,
                    CompId = b.CompId,

                    ReqRepeats = b.ReqRepeats ?? 0,
                    ReqHead = b.ReqHead ?? 0,
                    Panni = b.Panni ?? 0,
                    Solving = b.Solving ?? 0,

                    Active = true,
                    Deleted = false,
                    CreatedBy = userId,
                    CreatedDate = now
                };

                db.Gen_JobOrderItems.Add(item);
            }

            db.SaveChanges();
        }

        [HttpPost]
        [Route("reverse-post/{id:int}")]
        public ActionResult ReversePost(int id)
        {
            try
            {
                var entity = db.Gen_JobOrders
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (entity == null)
                    return JsonError("Job order not found.");

                if (!entity.IsPosted)
                    return JsonError("Job order is not posted.");

                var hasWorkOrder = db.WorkOrders.Any(x =>
                    x.JobOrderId == id &&
                    x.Deleted != true);

                if (hasWorkOrder)
                    return JsonError("Cannot reverse post. Work Order already exists for this Job Order.");

                int userId = GetCurrentUserId();
                entity.IsPosted = false;
                entity.PostedBy = null;
                entity.PostedDate = null;
                entity.Status = "Draft";
                entity.UpdatedBy = userId;
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                // Sprint-4 audit
                LogAudit(entity.Id, "Reversed", userId, detail: "Code " + entity.JobOrderCode);

                return JsonSuccess("Job order post reversed successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Reverse post failed.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-4  AUDIT TIMELINE
        // --------------------------------------------------------------------
        //  Append-only event log per Job Order. Written via raw SQL so the
        //  EDMX doesn't need to know about dbo.JobOrderAuditLog (avoiding the
        //  whole EDMX regeneration loop). Reads via SqlQuery<T> into a flat
        //  shape that already includes the changed-by user's name.
        //
        //  CRITICAL: every LogAudit() call is wrapped in try/catch and uses
        //  its own connection/command. An audit-table outage MUST NEVER tank
        //  the main operation — the operator's Post / Update / Clone has to
        //  succeed even if logging temporarily can't write.
        // ====================================================================

        public class JobOrderAuditEntryVm
        {
            public int Id { get; set; }
            public int JobOrderId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private void LogAudit(int jobOrderId, string action, int? userId,
                              string oldValue = null, string newValue = null, string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(
                    @"INSERT INTO dbo.JobOrderAuditLog
                        (JobOrderId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
                      VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6)",
                    jobOrderId,
                    action ?? "Unknown",
                    (object)userId ?? DBNull.Value,
                    DateTime.Now,
                    (object)oldValue ?? DBNull.Value,
                    (object)newValue ?? DBNull.Value,
                    (object)detail ?? DBNull.Value);
            }
            catch
            {
                // Audit failure is non-fatal by design — the operator's
                // primary action (Post / Update / Delete / Clone) should
                // never be blocked because the audit table is unhappy.
                // If you need monitoring on this, log to your APM/error
                // sink here instead of swallowing silently.
            }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                // Joined with Adm_Users so the timeline shows a display name
                // instead of just a foreign key. LEFT JOIN tolerates an
                // orphaned ChangedBy (e.g. user later deleted) — UI falls
                // back to "User #N" in that case.
                var rows = db.Database.SqlQuery<JobOrderAuditEntryVm>(
                    @"SELECT a.Id, a.JobOrderId, a.Action, a.ChangedBy,
                             ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName,'') + ' ' + ISNULL(u.LastName,''))), ''),
                                    ISNULL(u.UserName, '')) AS ChangedByName,
                             a.ChangedDate, a.OldValue, a.NewValue, a.Detail
                      FROM dbo.JobOrderAuditLog a
                      LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
                      WHERE a.JobOrderId = @p0
                      ORDER BY a.ChangedDate DESC, a.Id DESC",
                    id).ToList();

                return JsonSuccess("ok", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load audit log.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-4  WORKFLOW STAGE
        // --------------------------------------------------------------------
        //  Derives where a Job Order is in its lifecycle by looking at the
        //  related WorkOrders / DailyProduction / QC / Dispatch rows, plus the
        //  Status field for the manual "Closed" override. The stage is
        //  computed on-demand (no extra column on Gen_JobOrders) so it is
        //  always in sync with the data — no risk of a state column drifting
        //  away from the truth.
        //
        //  Stages, in order:
        //    Draft           — IsPosted = false
        //    Posted          — posted, no Work Orders yet
        //    InProduction    — Work Order exists, no QC posted yet
        //    QC              — a QC inspection has been posted against a
        //                      production for one of this JO's WOs
        //    Dispatched      — sum of posted DispatchItems.DispatchQty for this
        //                      JO has reached OrderQty
        //    Closed          — Status manually set to "Closed" (terminal)
        // ====================================================================

        // Canonical order of stages. Kept in sync with the Angular stepper.
        public static readonly string[] WorkflowStages = new[]
        {
            "Draft", "Posted", "InProduction", "QC", "Dispatched", "Closed"
        };

        private string ComputeWorkflowStage(int joId, bool isPosted, string statusField, int orderQty)
        {
            // Closed is a terminal manual state — once set, the timeline ends.
            if (string.Equals((statusField ?? "").Trim(), "Closed", StringComparison.OrdinalIgnoreCase))
                return "Closed";

            if (!isPosted) return "Draft";

            // Dispatched: have all units left the building?
            decimal dispatched = (from d  in db.Dispatch
                                  join di in db.DispatchItems on d.Id equals di.DispatchId
                                  where d.JobOrderId == joId
                                     && d.IsPosted == true
                                     && d.Deleted != true
                                     && di.Deleted != true
                                  select (decimal?)di.DispatchQty)
                                 .DefaultIfEmpty(0m).Sum() ?? 0m;

            if (orderQty > 0 && dispatched >= orderQty) return "Dispatched";

            // QC: any posted QC inspection on any of this JO's productions?
            bool hasQc = (from qc in db.QC_Inspection
                          join dp in db.DailyProduction on qc.ProductionId equals dp.Id
                          join wo in db.WorkOrders     on dp.WorkOrderId  equals wo.Id
                          where wo.JobOrderId == joId
                             && qc.IsPosted == true
                             && qc.Deleted != true
                          select qc.Id).Any();
            if (hasQc) return "QC";

            // In Production: any non-deleted WO exists.
            bool hasWo = db.WorkOrders.Any(w =>
                w.JobOrderId == joId && w.Deleted != true);
            if (hasWo) return "InProduction";

            return "Posted";
        }

        [HttpPost, Route("mark-closed/{id:int}")]
        public ActionResult MarkClosed(int id)
        {
            try
            {
                var jo = db.Gen_JobOrders.FirstOrDefault(x =>
                    x.Id == id && x.Deleted != true);

                if (jo == null)
                    return JsonError("Job order not found.");

                if (!jo.IsPosted)
                    return JsonError("Only Posted job orders can be closed. Post first.");

                // Guard: caller can only close once the data backs the move.
                // The stepper UI disables the button until then, but a savvy
                // user could still POST directly — server-side check prevents
                // premature closure.
                string currentStage = ComputeWorkflowStage(jo.Id, jo.IsPosted, jo.Status, jo.OrderQty);
                if (currentStage != "Dispatched" && currentStage != "Closed")
                    return JsonError("Job order cannot be closed yet. Current stage: " + currentStage +
                                     ". All units must be dispatched first.");

                if (currentStage == "Closed")
                    return JsonError("Job order is already closed.");

                int userId = GetCurrentUserId();
                string oldStatus = jo.Status ?? "";
                jo.Status      = "Closed";
                jo.UpdatedBy   = userId;
                jo.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                LogAudit(jo.Id, "Closed", userId,
                    oldValue: oldStatus, newValue: "Closed",
                    detail: "Job order marked as closed (lifecycle complete)");

                return JsonSuccess("Job order closed.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to mark closed.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-3  BULK ACTIONS
        // --------------------------------------------------------------------
        //  Each bulk endpoint loops over the supplied id list, applies the same
        //  per-row checks as its single-action equivalent (Post / Delete /
        //  Update-Priority), and reports per-id success/failure. We deliberately
        //  do NOT roll back the whole batch on a single failure — partial
        //  success is the expected UX (e.g. five of seven post; two fail because
        //  their components are missing — the user gets a list of the two so
        //  they can fix and retry).
        // ====================================================================

        public class BulkIdsDto
        {
            public List<int> Ids { get; set; }
        }

        public class BulkPriorityDto
        {
            public List<int> Ids { get; set; }
            public string Priority { get; set; }
        }

        public class BulkRowResult
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Reason { get; set; }
        }

        [HttpPost]
        [Route("bulk-post")]
        public ActionResult BulkPost(BulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No job orders selected.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            var failures = new List<BulkRowResult>();
            int successCount = 0;

            // Distinct + order helps the failure report read sensibly.
            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        var jo = db.Gen_JobOrders.FirstOrDefault(x =>
                            x.Id == id && x.Deleted != true);

                        if (jo == null)
                        { failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = "Not found." }); tx.Rollback(); continue; }
                        if (jo.IsPosted)
                        { failures.Add(new BulkRowResult { Id = id, Code = jo.JobOrderCode, Reason = "Already posted." }); tx.Rollback(); continue; }
                        if (jo.DesignId <= 0)
                        { failures.Add(new BulkRowResult { Id = id, Code = jo.JobOrderCode, Reason = "Design missing." }); tx.Rollback(); continue; }
                        if (jo.OrderQty <= 0)
                        { failures.Add(new BulkRowResult { Id = id, Code = jo.JobOrderCode, Reason = "Order qty is zero." }); tx.Rollback(); continue; }

                        EnsureJobOrderComponents(jo, userId, now);

                        bool hasComponents = db.Gen_JobOrderItems.Any(x =>
                            x.JobOrderId == jo.Id &&
                            x.Deleted != true);

                        if (!hasComponents)
                        { failures.Add(new BulkRowResult { Id = id, Code = jo.JobOrderCode, Reason = "No components in BOM." }); tx.Rollback(); continue; }

                        jo.IsPosted    = true;
                        jo.PostedBy    = userId;
                        jo.PostedDate  = now;
                        jo.Status      = "Posted";
                        jo.UpdatedBy   = userId;
                        jo.UpdatedDate = now;

                        db.SaveChanges();
                        tx.Commit();
                        successCount++;

                        // Sprint-4 audit: per-row, marked as a bulk action so
                        // the operator can grep "Bulk" later for a daily report.
                        LogAudit(jo.Id, "Posted", userId, detail: "via Bulk Post");
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* connection may already be closed */ }
                        failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = ex.Message });
                    }
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
                    failures
                }
            });
        }

        [HttpPost]
        [Route("bulk-delete")]
        public ActionResult BulkDelete(BulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No job orders selected.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            var failures = new List<BulkRowResult>();
            int successCount = 0;

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var jo = db.Gen_JobOrders.FirstOrDefault(x =>
                        x.Id == id && x.Deleted != true);

                    if (jo == null)
                    { failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = "Not found." }); continue; }

                    if (jo.IsPosted)
                    { failures.Add(new BulkRowResult { Id = id, Code = jo.JobOrderCode, Reason = "Posted — reverse first." }); continue; }

                    jo.Deleted     = true;
                    jo.Active      = false;
                    jo.UpdatedBy   = userId;
                    jo.UpdatedDate = now;

                    db.SaveChanges();
                    successCount++;

                    // Sprint-4 audit
                    LogAudit(jo.Id, "Deleted", userId, detail: "via Bulk Delete");
                }
                catch (Exception ex)
                {
                    failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = ex.Message });
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
                    failures
                }
            });
        }

        [HttpPost]
        [Route("bulk-change-priority")]
        public ActionResult BulkChangePriority(BulkPriorityDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No job orders selected.");

            // Free-text priority allowed, but we normalise the four canonical
            // values so the badge picker keeps matching. Anything else is
            // stored as-is (legacy values).
            string p = (dto.Priority ?? "").Trim();
            if (string.IsNullOrEmpty(p))
                return JsonError("Priority is required.");
            string up = p.ToUpper();
            if (up == "HIGH" || up == "URGENT") p = "High";
            else if (up == "MEDIUM" || up == "NORMAL") p = "Normal";
            else if (up == "LOW") p = "Low";

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            var failures = new List<BulkRowResult>();
            int successCount = 0;

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var jo = db.Gen_JobOrders.FirstOrDefault(x =>
                        x.Id == id && x.Deleted != true);

                    if (jo == null)
                    { failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = "Not found." }); continue; }

                    string oldPriority = jo.Priority ?? "";
                    jo.Priority    = p;
                    jo.UpdatedBy   = userId;
                    jo.UpdatedDate = now;

                    db.SaveChanges();
                    successCount++;

                    // Sprint-4 audit: old/new captured so the timeline shows
                    // the actual priority delta (e.g. Normal → High).
                    LogAudit(jo.Id, "PriorityChanged", userId,
                        oldValue: oldPriority, newValue: p,
                        detail: "via Bulk Set-Priority");
                }
                catch (Exception ex)
                {
                    failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = ex.Message });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " updated, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures
                }
            });
        }

        // ====================================================================
        //  SPRINT-3  CLONE
        // --------------------------------------------------------------------
        //  Deep-copies an existing job order into a new Draft. The new JO gets
        //  a fresh JobOrderCode, today's OrderDate, no DueDate, and is reset
        //  to IsPosted=false / Status="Draft". Customer / Product / Design /
        //  Branch / OrderQty / Priority are carried over so a recurring order
        //  is one click to set up and ready to edit.
        //
        //  Components (Gen_JobOrderItems) are cloned too — if the source had
        //  manual overrides of ReqRepeats / ReqHead / Panni / Solving, those
        //  carry forward. If the source had no components yet (very rare for
        //  a JO worth cloning), the EnsureJobOrderComponents() call at post
        //  time will still BOM-sync the new one.
        // ====================================================================
        [HttpPost]
        [Route("clone/{id:int}")]
        public ActionResult Clone(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var src = db.Gen_JobOrders.FirstOrDefault(x =>
                        x.Id == id && x.Deleted != true);

                    if (src == null)
                        return JsonError("Source job order not found.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var clone = new Gen_JobOrders
                    {
                        JobOrderCode = GenerateJobOrderCode(),

                        OrderType = src.OrderType,
                        IsCompanyStockOrder = src.IsCompanyStockOrder,

                        CustomerId = src.IsCompanyStockOrder ? (int?)null : src.CustomerId,

                        ProductId = src.ProductId,
                        DesignId  = src.DesignId,
                        BranchId  = src.BranchId,

                        OrderQty = src.OrderQty,
                        Priority = string.IsNullOrWhiteSpace(src.Priority) ? "Normal" : src.Priority,

                        // New draft — operator can edit before posting.
                        Status     = "Draft",
                        OrderDate  = now,
                        DueDate    = null,
                        IsPosted   = false,
                        PostedBy   = null,
                        PostedDate = null,

                        Active  = true,
                        Deleted = false,

                        CreatedBy   = userId,
                        CreatedDate = now,
                        UpdatedBy   = userId,
                        UpdatedDate = now
                    };

                    db.Gen_JobOrders.Add(clone);
                    // Sprint-5: race-safe save with code-collision retry.
                    SaveNewJobOrderWithCodeRetry(clone);

                    // Clone components (with manual overrides preserved). The
                    // SaveChanges above must run first so we have the new JO id.
                    var srcItems = db.Gen_JobOrderItems
                        .Where(x => x.JobOrderId == src.Id && x.Deleted != true)
                        .ToList();

                    foreach (var s in srcItems)
                    {
                        db.Gen_JobOrderItems.Add(new Gen_JobOrderItems
                        {
                            JobOrderId  = clone.Id,
                            CompId      = s.CompId,
                            ReqRepeats  = s.ReqRepeats,
                            ReqHead     = s.ReqHead,
                            Panni       = s.Panni,
                            Solving     = s.Solving,
                            Active      = true,
                            Deleted     = false,
                            CreatedBy   = userId,
                            CreatedDate = now
                        });
                    }

                    if (srcItems.Count > 0)
                        db.SaveChanges();

                    tx.Commit();

                    // Sprint-4 audit: one entry on the source ("Cloned to ...")
                    // and one on the new row ("Cloned from ...") so either end
                    // of the relationship can trace the other.
                    LogAudit(src.Id,   "Cloned", userId, detail: "Cloned to "   + clone.JobOrderCode);
                    LogAudit(clone.Id, "Created", userId,
                        detail: "Cloned from " + src.JobOrderCode + ", " + srcItems.Count + " items copied");

                    return JsonSuccess("Job order cloned successfully.", new
                    {
                        id = clone.Id,
                        jobOrderCode = clone.JobOrderCode,
                        sourceId = src.Id,
                        sourceCode = src.JobOrderCode,
                        itemsCloned = srcItems.Count
                    });
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    return JsonError("Clone failed: " + ex.Message);
                }
            }
        }

        [HttpPost]
        [Route("sync-components/{id:int}")]
        public ActionResult SyncComponents(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var j = db.Gen_JobOrders
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (j == null)
                        return JsonError("Job order not found.");

                    if (j.IsPosted)
                        return JsonError("Reverse post first, then sync components.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    GenerateJobOrderItemsFromBom(j.Id, j.DesignId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    // Sprint-4 audit
                    LogAudit(j.Id, "ComponentsSynced", userId,
                        detail: "Re-synced from BOM for design " + j.DesignId);

                    return JsonSuccess("Components synced successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to sync components.", ex);
                }
            }
        }


        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(JobOrdersPageViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["CreateErrors"] = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToArray();

                return RedirectToAction("Index", new { showCreate = true });
            }

            int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var now = DateTime.UtcNow;

            var ord = vm.NewOrder;

            ord.CreatedBy = uid;
            ord.CreatedDate = now;
            ord.UpdatedBy = uid;
            ord.UpdatedDate = now;

            ord.Active = true;
            ord.Deleted = false;

            // Default status
            if (string.IsNullOrEmpty(ord.Status))
                ord.Status = "Draft";

            db.Gen_JobOrders.Add(ord);
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(
             JobOrdersPageViewModel vm,
             string search = null,
             string activeFilter = null,
             int page = 1,
             int pageSize = 20)
        {
            if (!ModelState.IsValid)
            {
                return RedirectToAction("Index",
                    new { showEdit = true, editId = vm.EditOrder.Id, search, activeFilter, page, pageSize });
            }

            var ord = db.Gen_JobOrders.Find(vm.EditOrder.Id);
            if (ord == null)
                return HttpNotFound();

            int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            // Map header fields only
            ord.JobOrderCode = vm.EditOrder.JobOrderCode;
            ord.DesignId = vm.EditOrder.DesignId;
            ord.BranchId = vm.EditOrder.BranchId;

            ord.OrderQty = vm.EditOrder.OrderQty;
            ord.Priority = vm.EditOrder.Priority;
            ord.Status = vm.EditOrder.Status;

            ord.Active = vm.EditOrder.Active;
            ord.Deleted = vm.EditOrder.Deleted;

            ord.UpdatedBy = uid;
            ord.UpdatedDate = DateTime.UtcNow;

            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateJobOrderField(int jobOrderId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // 1) Load the order
            var order = db.Gen_JobOrders.Find(jobOrderId);
            if (order == null)
                return Json(new { success = false, message = "Job order not found." });

            // 2) Reflect to find the property
            var prop = typeof(Gen_JobOrders).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                // 3) Convert string to the correct target type
                object converted;
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (targetType == typeof(string))
                {
                    converted = newValue;
                }
                else if (targetType == typeof(int))
                {
                    converted = int.TryParse(newValue, out var i) ? i : (int?)null;
                }
                else if (targetType == typeof(decimal))
                {
                    converted = decimal.TryParse(newValue, out var d) ? d : (decimal?)null;
                }
                else if (targetType == typeof(bool))
                {
                    converted = string.Equals(newValue, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (targetType == typeof(DateTime))
                {
                    converted = DateTime.TryParse(newValue, out var dt) ? dt : (DateTime?)null;
                }
                else
                {
                    return Json(new { success = false, message = $"Unsupported type {targetType.Name}" });
                }

                // 4) Apply the new value
                prop.SetValue(order, converted);

                // 5) Stamp update info
                order.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                order.UpdatedDate = DateTime.UtcNow;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Gen_JobOrders/Delete/5
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var ord = db.Gen_JobOrders.Find(id.Value);
            if (ord == null) return HttpNotFound();
            return View(ord);
        }

        // POST: Gen_JobOrders/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var ord = db.Gen_JobOrders.Find(id);
            db.Gen_JobOrders.Remove(ord);
            db.SaveChanges();
            return RedirectToAction("Index");
        }
       
        // GET: Gen_JobOrders/Details/5
        public ActionResult Details(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var ord = db.Gen_JobOrders
                        .Include(j => j.Gen_Design)
                        .Include(j => j.Branches)
                        .Include(j => j.Gen_JobOrderItems.Select(i => i.Gen_Components))
                        .SingleOrDefault(j => j.Id == id.Value);

            if (ord == null)
                return HttpNotFound();

            return View(ord);
        }

        [HttpGet]
        [Route("exporttoexcel")]
        public ActionResult ExportToExcel(int? id = null, string search = null, string activeFilter = null)
        {
            if (id.HasValue)
            {
                var order = db.Gen_JobOrders
                    .Include(o => o.Gen_Design)
                    .Include(o => o.Branches)
                    .Include(o => o.Gen_JobOrderItems.Select(i => i.Gen_Components))
                    .FirstOrDefault(o => o.Id == id.Value);

                if (order == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("JobOrderDetail");

                    ws.Cells[1, 1].Value = "Field";
                    ws.Cells[1, 2].Value = "Value";
                    ws.Cells[1, 1, 1, 2].Style.Font.Bold = true;

                    int row = 1;

                    void Add(string f, object v)
                    {
                        row++;
                        ws.Cells[row, 1].Value = f;
                        ws.Cells[row, 2].Value = v ?? "(none)";
                    }

                    Add("ID", order.Id);
                    Add("Code", order.JobOrderCode);
                    Add("Design", order.Gen_Design?.CollectionName);
                    Add("Branch", order.Branches?.Name);
                    Add("Order Qty", order.OrderQty);
                    Add("Priority", order.Priority);
                    Add("Status", order.Status);
                    Add("Active", order.Active ? "Yes" : "No");
                    Add("Created By", order.CreatedBy);
                    Add("Created Date", order.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));

                    // Components section
                    row += 2;
                    ws.Cells[row, 1].Value = "Components";
                    ws.Cells[row, 1].Style.Font.Bold = true;

                    row++;

                    ws.Cells[row, 1].Value = "Component Code";
                    ws.Cells[row, 2].Value = "Component Name";
                    ws.Cells[row, 1, row, 2].Style.Font.Bold = true;

                    foreach (var item in order.Gen_JobOrderItems)
                    {
                        row++;
                        ws.Cells[row, 1].Value = item.Gen_Components?.Code;
                        ws.Cells[row, 2].Value = item.Gen_Components?.Name;
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    return File(pkg.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"JobOrder_{order.Id}.xlsx");
                }
            }
            else
            {
                var query = db.Gen_JobOrders
                    .Include(o => o.Gen_Design)
                    .Include(o => o.Branches)
                    .Where(o => o.Deleted != true);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(o => o.JobOrderCode.Contains(search));

                if (activeFilter == "Yes")
                    query = query.Where(o => o.Active);
                else if (activeFilter == "No")
                    query = query.Where(o => !o.Active);

                var list = query.OrderBy(o => o.Id).ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("JobOrders");

                    var headers = new[]
                    {
                "ID","Code","Design","Branch","OrderQty","Priority","Status","Active"
            };

                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        var o = list[i];
                        int r = i + 2;

                        ws.Cells[r, 1].Value = o.Id;
                        ws.Cells[r, 2].Value = o.JobOrderCode;
                        ws.Cells[r, 3].Value = o.Gen_Design?.CollectionName;
                        ws.Cells[r, 4].Value = o.Branches?.Name;
                        ws.Cells[r, 5].Value = o.OrderQty;
                        ws.Cells[r, 6].Value = o.Priority;
                        ws.Cells[r, 7].Value = o.Status;
                        ws.Cells[r, 8].Value = o.Active ? "Yes" : "No";
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    return File(pkg.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "JobOrders.xlsx");
                }
            }
        }

        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(int? id = null, string search = null, string activeFilter = null)
        {
            if (id.HasValue)
            {
                var order = db.Gen_JobOrders
                    .Include(o => o.Gen_Design)
                    .Include(o => o.Branches)
                    .Include(o => o.Gen_JobOrderItems.Select(i => i.Gen_Components))
                    .FirstOrDefault(o => o.Id == id.Value);

                if (order == null)
                    return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 80 };

                    void Cell(string txt, bool header = false)
                    {
                        var cell = new PdfPCell(new Phrase(txt));
                        if (header) cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        table.AddCell(cell);
                    }

                    void Row(string field, object value)
                    {
                        Cell(field);
                        Cell(value?.ToString() ?? "(none)");
                    }

                    // Header
                    Cell("Field", true);
                    Cell("Value", true);

                    Row("ID", order.Id);
                    Row("Code", order.JobOrderCode);
                    Row("Design", order.Gen_Design?.CollectionName);
                    Row("Branch", order.Branches?.Name);
                    Row("Order Qty", order.OrderQty);
                    Row("Priority", order.Priority);
                    Row("Status", order.Status);
                    Row("Active", order.Active ? "Yes" : "No");
                    Row("Created By", order.CreatedBy);
                    Row("Created Date", order.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(table);

                    // ---------- Components Section ----------
                    var compTable = new PdfPTable(2) { WidthPercentage = 80, SpacingBefore = 15 };

                    compTable.AddCell(new PdfPCell(new Phrase("Component Code"))
                    { BackgroundColor = BaseColor.LIGHT_GRAY });

                    compTable.AddCell(new PdfPCell(new Phrase("Component Name"))
                    { BackgroundColor = BaseColor.LIGHT_GRAY });

                    foreach (var item in order.Gen_JobOrderItems)
                    {
                        // Sprint-5: PdfPTable.AddCell(null) throws in iTextSharp;
                        // coalesce nullable nav-property values to "".
                        compTable.AddCell((item.Gen_Components?.Code)?.ToString() ?? "");
                        compTable.AddCell(item.Gen_Components?.Name ?? "");
                    }

                    doc.Add(compTable);

                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"JobOrder_{order.Id}.pdf");
                }
            }
            else
            {
                var query = db.Gen_JobOrders
                    .Include(o => o.Gen_Design)
                    .Include(o => o.Branches)
                    .Where(o => o.Deleted != true);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(o => o.JobOrderCode.Contains(search));

                if (activeFilter == "Yes")
                    query = query.Where(o => o.Active);
                else if (activeFilter == "No")
                    query = query.Where(o => !o.Active);

                var list = query.OrderBy(o => o.Id).ToList();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(7) { WidthPercentage = 100 };

                    string[] headers =
                    {
                "ID","Code","Design","Branch","Order Qty","Status","Active"
            };

                    foreach (var h in headers)
                    {
                        table.AddCell(new PdfPCell(new Phrase(h))
                        {
                            BackgroundColor = BaseColor.LIGHT_GRAY
                        });
                    }

                    foreach (var o in list)
                    {
                        // Sprint-5: PdfPTable.AddCell rejects nulls; coalesce
                        // every nullable nav-property and string to "" so a
                        // soft/hard-deleted Design/Branch can't crash the export.
                        table.AddCell(o.Id.ToString());
                        table.AddCell(o.JobOrderCode ?? "");
                        table.AddCell(o.Gen_Design?.CollectionName ?? "");
                        table.AddCell(o.Branches?.Name ?? "");
                        table.AddCell(o.OrderQty.ToString());
                        table.AddCell(o.Status ?? "");
                        table.AddCell(o.Active ? "Yes" : "No");
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", "JobOrders.pdf");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
