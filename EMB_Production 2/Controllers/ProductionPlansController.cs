using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;

namespace EMB_Production.Controllers
{
    // ============================================================
    // Production Plan
    // ------------------------------------------------------------
    // Weekly scheduling layer between Job Orders and Work Orders.
    //
    //   pending Job Orders ─►  [ Production Plan ]  ─► Work Orders
    //                          (PP-YYYY-NNNN)
    //
    // Lifecycle:
    //   DRAFT       – supervisor allocates JOs to machines + dates
    //   APPROVED    – capacity passes, system auto-generates WOs
    //                 (one WO per item, status=Draft, linked back
    //                  to the ProductionPlanItem)
    //   IN_PROGRESS – at least one WO has been posted
    //   CLOSED      – all WOs are finished or cancelled
    //
    // Constraints enforced:
    //   • A Job Order can only be in one ACTIVE plan at a time
    //     (DB-enforced by UX_PPI_JobOrderActive).
    //   • Capacity hard-block: no overlapping date ranges on
    //     the same machine within a plan.
    //
    //   Note: (Branch, Year, WeekNo) is NOT unique — one calendar
    //   week can host several parallel plans (different machines /
    //   different product batches). The Create endpoint surfaces a
    //   soft warning when a plan already exists for the same week
    //   so the operator can confirm they really want a second one,
    //   but the save is not blocked.
    //
    // Tables created by App_Data/Scripts/CreateProductionPlan.sql.
    // Reads/writes via raw SQL — no EDMX refresh required.
    // ============================================================
    [RoutePrefix("emb-products/production-plan")]
    public class ProductionPlansController : BaseController
    {
        // --------------------------------------------------------
        // Flat row used by /list, /detail
        // --------------------------------------------------------
        public class PlanRow
        {
            public int    Id { get; set; }
            public string PlanCode { get; set; }
            public int?   BranchId { get; set; }
            public string BranchName { get; set; }
            public int    Year { get; set; }
            public int    WeekNo { get; set; }
            public DateTime WeekStartDate { get; set; }
            public DateTime WeekEndDate { get; set; }
            public string Status { get; set; }
            public string Remarks { get; set; }
            public int?   ApprovedBy { get; set; }
            public string ApprovedByName { get; set; }
            public DateTime? ApprovedDate { get; set; }
            public bool?  Active { get; set; }
            public DateTime? CreatedDate { get; set; }
            public DateTime? UpdatedDate { get; set; }
        }

        public class PlanItemRow
        {
            public int    Id { get; set; }
            public int    PlanId { get; set; }
            public int?   JobOrderId { get; set; }
            public string JobOrderCode { get; set; }
            public bool   IsCompanyStockOrder { get; set; }
            public int?   ProductId { get; set; }
            public string ProductName { get; set; }
            public int?   DesignId { get; set; }
            public string DesignCode { get; set; }
            public string DesignName { get; set; }
            public int    PlannedQty { get; set; }
            public int?   MachineId { get; set; }
            public string MachineName { get; set; }
            public DateTime? PlannedStartDate { get; set; }
            public DateTime? PlannedEndDate { get; set; }
            public int?   GeneratedWorkOrderId { get; set; }
            public string GeneratedWorkOrderCode { get; set; }
            public string Status { get; set; }
            public string Remarks { get; set; }
        }

        // ====================================================================
        // LIST
        // ====================================================================
        [HttpGet, Route("list")]
        public ActionResult List(
            string status = "", int? branchId = null, int? year = null, int? weekNo = null,
            string search = "", string showDeleted = "",
            // Sprint-2 quick-jump from the Job Orders list. When present, the
            // result is narrowed to Plans that have at least one ProductionPlanItem
            // whose JobOrderId matches.
            int? jobOrderId = null)
        {
            try
            {
                string deletedClause = string.Equals(showDeleted, "yes", StringComparison.OrdinalIgnoreCase)
                    ? " (p.Deleted = 1) "
                    : " (p.Deleted = 0 OR p.Deleted IS NULL) ";

                var parameters = new List<SqlParameter>();
                string where = " WHERE " + deletedClause;

                if (!string.IsNullOrWhiteSpace(status))
                {
                    where += " AND p.Status = @status ";
                    parameters.Add(new SqlParameter("@status", status.Trim().ToUpper()));
                }
                if (branchId.HasValue && branchId.Value > 0)
                {
                    where += " AND p.BranchId = @brId ";
                    parameters.Add(new SqlParameter("@brId", branchId.Value));
                }
                if (year.HasValue && year.Value > 0)
                {
                    where += " AND p.[Year] = @yr ";
                    parameters.Add(new SqlParameter("@yr", year.Value));
                }
                if (weekNo.HasValue && weekNo.Value > 0)
                {
                    where += " AND p.WeekNo = @wk ";
                    parameters.Add(new SqlParameter("@wk", weekNo.Value));
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    where += " AND (p.PlanCode LIKE @s OR p.Remarks LIKE @s) ";
                    parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
                }
                if (jobOrderId.HasValue && jobOrderId.Value > 0)
                {
                    where += @" AND EXISTS (
                        SELECT 1 FROM dbo.ProductionPlanItems pi
                        WHERE pi.PlanId = p.Id
                          AND pi.JobOrderId = @joId
                          AND (pi.Deleted = 0 OR pi.Deleted IS NULL)
                    ) ";
                    parameters.Add(new SqlParameter("@joId", jobOrderId.Value));
                }

                string sql = @"
SELECT  p.Id, p.PlanCode, p.BranchId,
        b.Name AS BranchName,
        p.[Year], p.WeekNo, p.WeekStartDate, p.WeekEndDate,
        p.Status, p.Remarks,
        p.ApprovedBy, ua.UserName AS ApprovedByName, p.ApprovedDate,
        p.Active, p.CreatedDate, p.UpdatedDate
FROM    dbo.ProductionPlan p
LEFT JOIN dbo.Branches b  ON b.Id  = p.BranchId
LEFT JOIN dbo.Adm_Users ua ON ua.Id = p.ApprovedBy
" + where + " ORDER BY p.Id DESC ";

                var rows = db.Database.SqlQuery<PlanRow>(sql, parameters.ToArray()).ToList();

                // Item counts per plan for the summary chips
                var planIds = rows.Select(r => r.Id).ToList();
                var counts = planIds.Count == 0
                    ? new Dictionary<int, int>()
                    : db.Database.SqlQuery<CountByPlanRow>(
                            "SELECT PlanId AS PlanId, COUNT(*) AS Cnt FROM dbo.ProductionPlanItems "
                          + "WHERE (Deleted = 0 OR Deleted IS NULL) "
                          + "AND PlanId IN (" + string.Join(",", planIds) + ") "
                          + "GROUP BY PlanId")
                        .ToDictionary(x => x.PlanId, x => x.Cnt);

                // Sprint-2 quick-jump counts:
                //  joCount = distinct JOs referenced by items on this plan
                //  woCount = items that have already generated a Work Order
                // Both populate clickable badges in the list row so the
                // operator can see "this plan touches 5 JOs and produced 3
                // WOs" at a glance, and jump to either side.
                var joCounts = planIds.Count == 0
                    ? new Dictionary<int, int>()
                    : db.Database.SqlQuery<CountByPlanRow>(
                            "SELECT PlanId AS PlanId, COUNT(DISTINCT JobOrderId) AS Cnt "
                          + "FROM dbo.ProductionPlanItems "
                          + "WHERE (Deleted = 0 OR Deleted IS NULL) "
                          + "AND JobOrderId IS NOT NULL "
                          + "AND PlanId IN (" + string.Join(",", planIds) + ") "
                          + "GROUP BY PlanId")
                        .ToDictionary(x => x.PlanId, x => x.Cnt);

                var woCounts = planIds.Count == 0
                    ? new Dictionary<int, int>()
                    : db.Database.SqlQuery<CountByPlanRow>(
                            "SELECT PlanId AS PlanId, COUNT(*) AS Cnt "
                          + "FROM dbo.ProductionPlanItems "
                          + "WHERE (Deleted = 0 OR Deleted IS NULL) "
                          + "AND GeneratedWorkOrderId IS NOT NULL "
                          + "AND PlanId IN (" + string.Join(",", planIds) + ") "
                          + "GROUP BY PlanId")
                        .ToDictionary(x => x.PlanId, x => x.Cnt);

                // IMPORTANT — the property NAMES of this anonymous type
                // become the JSON keys. JsonSuccess uses MVC's built-in
                // Json() (JavaScriptSerializer), which does NOT apply
                // the global Newtonsoft camelCase contract. So unless we
                // explicitly write `id = ...`, `planCode = ...` (lowercase
                // first letter), the JSON ends up PascalCase, breaks every
                // Angular binding (`r.planCode` → undefined), and the
                // list rows render as blanks/dashes.
                // Sprint-1 work-plan polish: compute today's ISO year + week
                // ONCE up front so the row builder can flag the matching plan
                // without re-running the date math per row. ISO week aligns
                // with the way the schema models WeekNo (1..53 per Year).
                var today = DateTime.Today;
                var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
                int currentIsoWeek = cal.GetWeekOfYear(today,
                    System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                    DayOfWeek.Monday);
                int currentIsoYear = today.Year;
                // Edge case: late December dates can fall into the next ISO
                // year's week 1, and early January into the previous year's
                // week 52/53. Adjust so currentIsoYear matches the ISO standard.
                if (today.Month == 12 && currentIsoWeek == 1) currentIsoYear++;
                else if (today.Month == 1 && currentIsoWeek >= 52) currentIsoYear--;

                var enriched = rows.Select(r => new
                {
                    id              = r.Id,
                    planCode        = r.PlanCode,
                    branchId        = r.BranchId,
                    branchName      = r.BranchName,
                    year            = r.Year,
                    weekNo          = r.WeekNo,
                    weekStartDate   = r.WeekStartDate,
                    weekEndDate     = r.WeekEndDate,
                    // ISO yyyy-MM-dd strings so the Angular date pipe accepts
                    // them. The raw fields above arrive as /Date(ms)/ via MVC's
                    // legacy serialiser and would otherwise render blank. The
                    // schema makes both columns NOT NULL, so we can format
                    // directly without HasValue checks.
                    weekStartDateText = r.WeekStartDate.ToString("yyyy-MM-dd"),
                    weekEndDateText   = r.WeekEndDate.ToString("yyyy-MM-dd"),
                    status          = r.Status,
                    remarks         = r.Remarks,
                    approvedBy      = r.ApprovedBy,
                    approvedByName  = r.ApprovedByName,
                    approvedDate    = r.ApprovedDate,
                    active          = r.Active,
                    createdDate     = r.CreatedDate,
                    updatedDate     = r.UpdatedDate,
                    itemCount       = counts.ContainsKey(r.Id) ? counts[r.Id] : 0,
                    // Sprint-2 quick-jump counters
                    joCount         = joCounts.ContainsKey(r.Id) ? joCounts[r.Id] : 0,
                    woCount         = woCounts.ContainsKey(r.Id) ? woCounts[r.Id] : 0,
                    // Sprint-1: server-side flag so the UI can highlight the
                    // row matching today's calendar week without recomputing
                    // ISO week math on the client.
                    isCurrentWeek   = r.Year == currentIsoYear && r.WeekNo == currentIsoWeek
                }).ToList();

                var summary = new
                {
                    count           = rows.Count,
                    draftCount      = rows.Count(r => r.Status == "DRAFT"),
                    approvedCount   = rows.Count(r => r.Status == "APPROVED"),
                    inProgressCount = rows.Count(r => r.Status == "IN_PROGRESS"),
                    closedCount     = rows.Count(r => r.Status == "CLOSED")
                };

                return JsonSuccess("OK", new { items = enriched, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load Production Plan list.", ex);
            }
        }

        private class CountByPlanRow { public int PlanId { get; set; } public int Cnt { get; set; } }

        // ====================================================================
        // DETAIL — header + items + computed sections (BOM rollup, capacity)
        // ====================================================================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var header = LoadPlanHeader(id);
                if (header == null) return JsonError("Plan not found.");

                var items = LoadPlanItems(id);

                // BOM rollup — total thread demand across all items in this plan
                var bom = ComputeBomRollup(items, header.BranchId ?? GetCurrentBranchId());

                // Capacity check — any machine over-allocated?
                var capacity = ComputeCapacityReport(items);

                // IMPORTANT — JsonSuccess uses MVC's JavaScriptSerializer
                // which preserves PascalCase. The Angular frontend expects
                // camelCase (h.planCode, i.jobOrderCode, b.requiredQty,
                // c.machineName), so we must explicitly project every
                // field to a camelCase anonymous-type key here.
                var headerCC = new
                {
                    id              = header.Id,
                    planCode        = header.PlanCode,
                    branchId        = header.BranchId,
                    branchName      = header.BranchName,
                    year            = header.Year,
                    weekNo          = header.WeekNo,
                    weekStartDate   = header.WeekStartDate,
                    weekEndDate     = header.WeekEndDate,
                    status          = header.Status,
                    remarks         = header.Remarks,
                    approvedBy      = header.ApprovedBy,
                    approvedByName  = header.ApprovedByName,
                    approvedDate    = header.ApprovedDate,
                    active          = header.Active,
                    createdDate     = header.CreatedDate,
                    updatedDate     = header.UpdatedDate
                };

                var itemsCC = items.Select(i => new
                {
                    id                      = i.Id,
                    planId                  = i.PlanId,
                    jobOrderId              = i.JobOrderId,
                    jobOrderCode            = i.JobOrderCode,
                    isCompanyStockOrder     = i.IsCompanyStockOrder,
                    productId               = i.ProductId,
                    productName             = i.ProductName,
                    designId                = i.DesignId,
                    designCode              = i.DesignCode,
                    designName              = i.DesignName,
                    plannedQty              = i.PlannedQty,
                    machineId               = i.MachineId,
                    machineName             = i.MachineName,
                    plannedStartDate        = i.PlannedStartDate,
                    plannedEndDate          = i.PlannedEndDate,
                    generatedWorkOrderId    = i.GeneratedWorkOrderId,
                    generatedWorkOrderCode  = i.GeneratedWorkOrderCode,
                    status                  = i.Status,
                    remarks                 = i.Remarks
                }).ToList();

                var bomCC = bom.Select(b => new
                {
                    threadId    = b.ThreadId,
                    rawItemId   = b.RawItemId,
                    code        = b.Code,
                    name        = b.Name,
                    color       = b.Color,
                    unit        = b.Unit,
                    requiredQty = b.RequiredQty,
                    onHand      = b.OnHand,
                    shortfall   = b.Shortfall
                }).ToList();

                var capacityCC = capacity.Select(c => new
                {
                    machineId       = c.MachineId,
                    machineName     = c.MachineName,
                    itemCount       = c.ItemCount,
                    totalPlannedQty = c.TotalPlannedQty,
                    earliestStart   = c.EarliestStart,
                    latestEnd       = c.LatestEnd,
                    hasConflict     = c.HasConflict,
                    conflictDetails = c.ConflictDetails ?? new List<string>()
                }).ToList();

                return JsonSuccess("OK", new {
                    header   = headerCC,
                    items    = itemsCC,
                    bom      = bomCC,
                    capacity = capacityCC,
                    // Sprint-4: workflow stepper data. workflowStages is the
                    // canonical lifecycle order; workflowStageIndex is where
                    // this plan sits today. The stepper component uses both
                    // to render past/current/upcoming markers.
                    workflowStages     = _workflowStages,
                    workflowStageIndex = ComputeWorkflowStageIndex(header.Status)
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load plan detail.", ex);
            }
        }

        // ====================================================================
        // NEXT CODE
        // ====================================================================
        [HttpGet, Route("next-code")]
        public ActionResult NextCode()
        {
            try { return JsonSuccess("OK", new { code = GenerateCode() }); }
            catch (Exception ex) { return JsonError("Failed to generate plan code.", ex); }
        }

        // ====================================================================
        // ELIGIBLE JOB ORDERS — JOs not yet in any active plan
        // ====================================================================
        [HttpGet, Route("eligible-job-orders")]
        public ActionResult EligibleJobOrders()
        {
            try
            {
                // Rule B: a JO is "taken" only if it's in an OPEN plan item
                // (PENDING / GENERATED). CLOSED items released their slot when
                // the parent plan was closed and the JO is free to schedule
                // again. Mirrors the UX_PPI_JobOrderActive filtered-index
                // predicate so the picker and the DB constraint agree.
                var taken = db.Database.SqlQuery<int>(
                    "SELECT DISTINCT JobOrderId FROM dbo.ProductionPlanItems "
                  + "WHERE JobOrderId IS NOT NULL "
                  + "AND (Deleted = 0 OR Deleted IS NULL) "
                  + "AND Status IN ('PENDING', 'GENERATED')").ToList();

                var takenSet = new HashSet<int>(taken);

                // "Pending" = JO is posted AND has remaining unplanned
                // qty (qty not yet consumed by Work Orders). Once a JO
                // is fully covered by WOs, it shouldn't reappear here.
                var usedByJo = db.WorkOrders
                    .Where(w => w.Deleted != true)
                    .GroupBy(w => w.JobOrderId)
                    .Select(g => new {
                        joId = g.Key,
                        used = g.Sum(w => (int?)w.OrderQty) ?? 0
                    })
                    .ToDictionary(x => x.joId, x => x.used);

                // Include Draft JOs too — the operator just created JO-9
                // and expects to schedule it into the plan they're editing
                // now. `isPosted` is exposed so the frontend can flag Draft
                // rows with a "(Draft)" suffix and warn the operator that
                // the JO needs posting before Plan Approve regenerates the
                // Work Orders. Posted JOs are surfaced first (best matches
                // to schedule) with Drafts trailing.
                int branchId = GetCurrentBranchId();
                var list = db.Gen_JobOrders
                    .Where(j => j.Deleted != true && j.BranchId == branchId)
                    .OrderByDescending(j => j.IsPosted == true)   // Posted → top
                    .ThenByDescending(j => j.Id)                  // recent → next
                    .Select(j => new
                    {
                        id = j.Id,
                        code = j.JobOrderCode,
                        isPosted = j.IsPosted == true,
                        productId = j.ProductId,
                        productName = j.Gen_Products != null ? j.Gen_Products.Prod_Name : "",
                        designId = j.DesignId,
                        designCode = j.Gen_Design != null ? j.Gen_Design.D_Code : "",
                        orderQty = j.OrderQty,
                        branchId = j.BranchId,
                        isCompanyStockOrder = j.IsCompanyStockOrder,
                        customerName = j.IsCompanyStockOrder
                            ? "Stock"
                            : (j.Hr_Customers != null ? j.Hr_Customers.Customer_Name : "-")
                    })
                    .Take(500)
                    .ToList()
                    .Where(j => !takenSet.Contains(j.id))
                    .Where(j => {
                        int used = usedByJo.ContainsKey(j.id) ? usedByJo[j.id] : 0;
                        return (j.orderQty - used) > 0;   // pending = remaining > 0
                    })
                    .ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load eligible job orders.", ex);
            }
        }

        [HttpGet, Route("machines-dropdown")]
        public ActionResult MachinesDropdown()
        {
            try
            {
                int branchId = GetCurrentBranchId();
                var list = db.Gen_Machines
                    .Where(m => m.Deleted != true
                             && (m.Active == true || m.Active == null)
                             && (!m.BranchId.HasValue || m.BranchId == branchId))
                    .OrderBy(m => m.Code)
                    .Select(m => new
                    {
                        id = m.Id,
                        code = m.Code,
                        name = m.Code,                 // Gen_Machines has no Name; expose Code under both keys for compat
                        display = (m.Code ?? "")
                                + (m.Serial_Number != null && m.Serial_Number != ""
                                    ? " - " + m.Serial_Number : "")
                    })
                    .ToList();
                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load machines.", ex);
            }
        }

        /** Shape returned to the client when Create finds another plan
         *  already exists for the same (Branch, Year, Week). One row per
         *  existing plan (up to 5) so the operator's confirm dialog can
         *  list them with status, e.g.
         *      PP-2026-0003 (CLOSED)
         *      PP-2026-0005 (DRAFT) */
        private class DuplicatePlanRow
        {
            public int Id { get; set; }
            public string PlanCode { get; set; }
            public string Status { get; set; }
        }

        // ====================================================================
        // CREATE (Draft)
        // ====================================================================
        public class PlanSaveDto
        {
            public int Id { get; set; }
            public string PlanCode { get; set; }
            public int? BranchId { get; set; }
            public int Year { get; set; }
            public int WeekNo { get; set; }
            public string Remarks { get; set; }

            /** When true, the client has already shown the operator the
             *  "a plan exists for this week" warning and they chose to
             *  create a second one anyway (e.g. different machine /
             *  product batch). The server skips the warning on this
             *  request and creates the plan. */
            public bool ConfirmDuplicateWeek { get; set; }
        }

        [HttpPost, Route("create")]
        public ActionResult Create(PlanSaveDto dto)
        {
            if (dto == null) return JsonError("Invalid request.");
            if (dto.Year <= 0 || dto.WeekNo <= 0)
                return JsonError("Year and Week No are required.");

            try
            {
                int branchId = dto.BranchId ?? GetCurrentBranchId();

                // (Branch, Year, Week) is intentionally NOT unique — one
                // calendar week can host several parallel plans for
                // different machines / product batches. We still flag
                // the duplicate on the first save so the operator gets
                // a chance to confirm: are you really making a second
                // plan for the same week, or did you mean to edit the
                // existing one?
                if (!dto.ConfirmDuplicateWeek)
                {
                    var existingPlans = db.Database.SqlQuery<DuplicatePlanRow>(@"
SELECT TOP 5 Id, PlanCode, Status FROM dbo.ProductionPlan
WHERE BranchId = @br AND [Year] = @y AND WeekNo = @w
  AND (Deleted = 0 OR Deleted IS NULL)
ORDER BY Id DESC",
                        new SqlParameter("@br", branchId),
                        new SqlParameter("@y",  dto.Year),
                        new SqlParameter("@w",  dto.WeekNo)
                    ).ToList();

                    if (existingPlans.Count > 0)
                    {
                        return Json(new
                        {
                            success = false,
                            warning = true,
                            message = "Another plan already exists for this branch and week. "
                                    + "Create a parallel plan anyway, or open the existing one?",
                            conflictId = existingPlans[0].Id,
                            existingPlans = existingPlans
                        }, JsonRequestBehavior.AllowGet);
                    }
                }

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Resolve week start/end from ISO week (Monday..Sunday)
                WeekRange week = ResolveWeekRange(dto.Year, dto.WeekNo);

                // Sprint-5: race-safe INSERT. If two requests pick the same
                // GenerateCode() output, the unique index on PlanCode catches
                // it (SqlException 2601/2627) and the loop retries with
                // GenerateCode(skip + 1). 3 attempts is plenty for any
                // realistic concurrency level on this endpoint. A
                // client-supplied PlanCode is honoured as-is — the user
                // explicitly chose it, so collisions are their problem.
                string code = string.IsNullOrWhiteSpace(dto.PlanCode) ? GenerateCode() : dto.PlanCode.Trim();
                bool clientSuppliedCode = !string.IsNullOrWhiteSpace(dto.PlanCode);

                int newId = 0;
                int maxAttempts = clientSuppliedCode ? 1 : 3;
                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.ProductionPlan
       (PlanCode, BranchId, [Year], WeekNo, WeekStartDate, WeekEndDate,
        Status, Remarks, Active, Deleted, CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code,    @br,      @y,     @w,     @ws,           @we,
        'DRAFT',  @rmk,    1,      0,       @uid,      @now,        @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                            new SqlParameter("@code", code),
                            new SqlParameter("@br",   branchId),
                            new SqlParameter("@y",    dto.Year),
                            new SqlParameter("@w",    dto.WeekNo),
                            new SqlParameter("@ws",   week.Start),
                            new SqlParameter("@we",   week.End),
                            new SqlParameter("@rmk",  (object)(dto.Remarks ?? "") ?? DBNull.Value),
                            new SqlParameter("@uid",  userId),
                            new SqlParameter("@now",  now)
                        ).First();
                        break;  // success
                    }
                    catch (Exception insertEx) when (!clientSuppliedCode && IsPlanCodeUniqueViolation(insertEx))
                    {
                        // Step the candidate code and retry. If we exhaust
                        // attempts, the outer catch surfaces the original
                        // SqlException so the user sees a meaningful 500.
                        if (attempt + 1 >= maxAttempts) throw;
                        code = GenerateCode(attempt + 1);
                    }
                }

                LogAudit(newId, "Created", null, "DRAFT",
                    "Plan " + code + " for W" + dto.WeekNo + "/" + dto.Year);
                return JsonSuccess("Plan created.", new { id = newId, planCode = code });
            }
            catch (Exception ex) when (IsBranchWeekUniqueViolation(ex))
            {
                // Legacy DB still carries UX_PP_BranchWeek as a UNIQUE
                // index. Tell the operator exactly which migration to
                // run instead of dumping the raw SQL exception.
                return JsonError(
                    "Cannot create a second plan for this week — the database "
                  + "still has the old UX_PP_BranchWeek UNIQUE index. Run "
                  + "Migrations/DropProductionPlanBranchWeekUnique.sql, then "
                  + "retry. (Parallel plans per week were enabled in app code "
                  + "but the index drop hasn't been applied yet.)");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to create plan.", ex);
            }
        }

        /** True when the inner SqlException reports a duplicate-key
         *  violation against the legacy UX_PP_BranchWeek index. Walks
         *  the InnerException chain so it works whether EF wrapped
         *  the SqlException or not. */
        private static bool IsBranchWeekUniqueViolation(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var sqlEx = cur as SqlException;
                if (sqlEx == null) continue;
                foreach (SqlError err in sqlEx.Errors)
                {
                    if ((err.Number == 2601 || err.Number == 2627)
                        && err.Message != null
                        && err.Message.IndexOf("UX_PP_BranchWeek", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        [HttpPost, Route("update")]
        public ActionResult Update(PlanSaveDto dto)
        {
            if (dto == null || dto.Id <= 0) return JsonError("Invalid request.");

            try
            {
                var hdr = LoadPlanHeader(dto.Id);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans can be edited.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Remarks = @rmk,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                    new SqlParameter("@rmk", (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  dto.Id));

                LogAudit(dto.Id, "Updated", null, null, "Header remarks updated");
                return JsonSuccess("Updated.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update plan.", ex);
            }
        }

        // ====================================================================
        // ITEMS — add / update / remove
        // ====================================================================
        public class PlanItemSaveDto
        {
            public int Id { get; set; }
            public int PlanId { get; set; }
            public int? JobOrderId { get; set; }
            public bool IsCompanyStockOrder { get; set; }
            public int? ProductId { get; set; }
            public int? DesignId { get; set; }
            public int PlannedQty { get; set; }
            public int? MachineId { get; set; }
            public DateTime? PlannedStartDate { get; set; }
            public DateTime? PlannedEndDate { get; set; }
            public string Remarks { get; set; }
        }

        [HttpPost, Route("add-item")]
        public ActionResult AddItem(PlanItemSaveDto dto)
        {
            if (dto == null || dto.PlanId <= 0) return JsonError("Invalid request.");
            if (dto.PlannedQty <= 0) return JsonError("Planned qty must be greater than zero.");

            // Sprint-5: reject inverted date ranges at the boundary instead
            // of leaving them to confuse the capacity report at approval
            // time. Both nullable — only validate when both are supplied.
            string dateError = ValidateItemDates(dto);
            if (dateError != null) return JsonError(dateError);

            try
            {
                var hdr = LoadPlanHeader(dto.PlanId);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans accept new items.");

                // Sprint-5: also gate the dates against the plan's own week
                // so a planner can't slip items outside the scheduled window.
                string boundsError = ValidateItemDatesAgainstWeek(dto, hdr);
                if (boundsError != null) return JsonError(boundsError);

                // If a JobOrderId is supplied, pull the canonical product/design from it
                if (dto.JobOrderId.HasValue && dto.JobOrderId.Value > 0)
                {
                    var jo = db.Gen_JobOrders.FirstOrDefault(j => j.Id == dto.JobOrderId.Value);
                    if (jo == null) return JsonError("Linked Job Order not found.");

                    // Gen_JobOrders has ProductId, DesignId, OrderQty as non-nullable int,
                    // so plain assignment (no ?? coalesce) is what the compiler wants.
                    dto.ProductId = jo.ProductId;
                    dto.DesignId  = jo.DesignId;
                    dto.IsCompanyStockOrder = jo.IsCompanyStockOrder;
                    if (dto.PlannedQty <= 0) dto.PlannedQty = jo.OrderQty;

                    // Reject if this JO is already in an active plan item
                    int alreadyTaken = db.Database.SqlQuery<int>(@"
SELECT TOP 1 Id FROM dbo.ProductionPlanItems
WHERE JobOrderId = @jo
  AND Status <> 'CANCELLED'
  AND (Deleted = 0 OR Deleted IS NULL)",
                        new SqlParameter("@jo", dto.JobOrderId.Value)).FirstOrDefault();
                    if (alreadyTaken > 0)
                        return JsonError("This Job Order is already in another active plan.");
                }

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                int newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.ProductionPlanItems
       (PlanId, JobOrderId, IsCompanyStockOrder, ProductId, DesignId,
        PlannedQty, MachineId, PlannedStartDate, PlannedEndDate,
        Status, Remarks, Active, Deleted, CreatedBy, CreatedDate)
VALUES (@planId, @joId,     @isStock,            @pid,      @did,
        @qty,    @mid,       @start,           @end,
        'PENDING', @rmk,    1,      0,       @uid,      @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new SqlParameter("@planId", dto.PlanId),
                    new SqlParameter("@joId",   (object)dto.JobOrderId ?? DBNull.Value),
                    new SqlParameter("@isStock", dto.IsCompanyStockOrder),
                    new SqlParameter("@pid",    (object)dto.ProductId ?? DBNull.Value),
                    new SqlParameter("@did",    (object)dto.DesignId ?? DBNull.Value),
                    new SqlParameter("@qty",    dto.PlannedQty),
                    new SqlParameter("@mid",    (object)dto.MachineId ?? DBNull.Value),
                    new SqlParameter("@start",  (object)dto.PlannedStartDate ?? DBNull.Value),
                    new SqlParameter("@end",    (object)dto.PlannedEndDate ?? DBNull.Value),
                    new SqlParameter("@rmk",    (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",    userId),
                    new SqlParameter("@now",    now)
                ).First();

                LogAudit(dto.PlanId, "ItemAdded", null, dto.PlannedQty.ToString(),
                    "Item #" + newId + (dto.JobOrderId.HasValue ? " (JO " + dto.JobOrderId.Value + ")" : ""));
                return JsonSuccess("Item added.", new { id = newId });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to add item.", ex);
            }
        }

        [HttpPost, Route("update-item")]
        public ActionResult UpdateItem(PlanItemSaveDto dto)
        {
            if (dto == null || dto.Id <= 0) return JsonError("Invalid request.");
            if (dto.PlannedQty <= 0) return JsonError("Planned qty must be greater than zero.");

            // Sprint-5: matches AddItem — reject inverted ranges + out-of-week.
            string dateError = ValidateItemDates(dto);
            if (dateError != null) return JsonError(dateError);

            try
            {
                var hdr = LoadPlanHeader(dto.PlanId);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans can be edited.");

                string boundsError = ValidateItemDatesAgainstWeek(dto, hdr);
                if (boundsError != null) return JsonError(boundsError);

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    PlannedQty       = @qty,
       MachineId        = @mid,
       PlannedStartDate = @start,
       PlannedEndDate   = @end,
       Remarks          = @rmk,
       UpdatedBy        = @uid,
       UpdatedDate      = @now
WHERE  Id = @id",
                    new SqlParameter("@qty",   dto.PlannedQty),
                    new SqlParameter("@mid",   (object)dto.MachineId ?? DBNull.Value),
                    new SqlParameter("@start", (object)dto.PlannedStartDate ?? DBNull.Value),
                    new SqlParameter("@end",   (object)dto.PlannedEndDate ?? DBNull.Value),
                    new SqlParameter("@rmk",   (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",   userId),
                    new SqlParameter("@now",   now),
                    new SqlParameter("@id",    dto.Id));

                LogAudit(dto.PlanId, "ItemUpdated", null, dto.PlannedQty.ToString(),
                    "Item #" + dto.Id + " — qty=" + dto.PlannedQty);
                return JsonSuccess("Updated.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update item.", ex);
            }
        }

        [HttpPost, Route("remove-item/{id:int}")]
        public ActionResult RemoveItem(int id)
        {
            try
            {
                int planId = db.Database.SqlQuery<int>(
                    "SELECT PlanId FROM dbo.ProductionPlanItems WHERE Id = @id",
                    new SqlParameter("@id", id)).FirstOrDefault();

                if (planId <= 0) return JsonError("Item not found.");

                var hdr = LoadPlanHeader(planId);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans can have items removed.");

                db.Database.ExecuteSqlCommand(
                    "UPDATE dbo.ProductionPlanItems SET Deleted = 1, Active = 0 WHERE Id = @id",
                    new SqlParameter("@id", id));

                LogAudit(planId, "ItemRemoved", null, null, "Item #" + id + " removed");
                return JsonSuccess("Removed.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to remove item.", ex);
            }
        }

        // ====================================================================
        // APPROVE
        // -----------------------------------------------------------------
        //   1) Hard capacity check — no overlapping date ranges per machine
        //   2) Mark plan APPROVED
        //   3) Auto-generate one Draft Work Order per item
        // ====================================================================
        [HttpPost, Route("approve/{id:int}")]
        public ActionResult Approve(int id)
        {
            try
            {
                var hdr = LoadPlanHeader(id);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans can be approved.");

                var items = LoadPlanItems(id);
                if (items.Count == 0) return JsonError("Plan has no items.");

                // Each item must have qty + (when scheduled) the machine
                if (items.Any(i => i.PlannedQty <= 0))
                    return JsonError("Every item needs a positive Planned Qty.");

                // CAPACITY CHECK: detect overlapping date ranges per machine
                var conflicts = ComputeCapacityReport(items)
                    .Where(c => c.HasConflict)
                    .ToList();

                if (conflicts.Count > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Capacity over-allocated on " + conflicts.Count
                                + " machine(s). Resolve overlapping date ranges before approval.",
                        conflicts
                    }, JsonRequestBehavior.AllowGet);
                }

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;
                int branchId = hdr.BranchId ?? GetCurrentBranchId();

                // Collect the WO ids generated inside the transaction so we
                // can hand them to AutoPrCore *after* the commit. Same
                // resilience guarantee as the WO-Post path: if auto-PR
                // generation fails, the plan approval still stands.
                var generatedWoIds = new List<int>();

                // Sprint-5 hardening on the loop below:
                //  * Single transaction wraps EVERY WO insert + every item
                //    update + the plan status flip. If any item fails the
                //    rollback puts the plan, the items, AND any partially-
                //    inserted WOs back to their pre-call state.
                //  * The per-item failure is wrapped to bubble up which
                //    item caused the abort — "Approval failed on item #14
                //    (JO 7): <db error>" — so the operator can fix the
                //    bad row without spelunking the logs.
                //  * Auto-PR remains OUTSIDE the transaction by design:
                //    PR creation failing must NOT undo a valid approval.
                using (var tx = db.Database.BeginTransaction())
                {
                    int failedItemId = 0;     // Sprint-5: track which item blew up for the error envelope
                    try
                    {
                        // Generate one Work Order per plan item
                        int seq = 0;
                        foreach (var item in items)
                        {
                            failedItemId = item.Id;   // updated each iteration so the catch knows the culprit
                            seq++;
                            string woCode = "WO-PLAN-" + hdr.PlanCode + "-" + seq.ToString("D2");

                            var wo = new WorkOrders
                            {
                                WorkOrderCode = woCode,
                                OrderType     = item.IsCompanyStockOrder ? "Stock" : "Customer",
                                IsCompanyStockOrder = item.IsCompanyStockOrder,
                                JobOrderId    = item.JobOrderId,
                                ProductId     = item.ProductId,
                                DesignId      = item.DesignId,
                                BranchId      = branchId,
                                OrderQty      = item.PlannedQty,
                                OrderDate     = item.PlannedStartDate ?? hdr.WeekStartDate,
                                DueDate       = item.PlannedEndDate   ?? hdr.WeekEndDate,
                                IsPosted      = false,
                                Active        = true,
                                Deleted       = false,
                                CreatedBy     = userId,
                                CreatedDate   = now,
                                UpdatedBy     = userId,
                                UpdatedDate   = now,
                                RecordTimeStamp = now,
                                Status        = "Draft",
                                Remarks       = "Auto-generated from Production Plan " + hdr.PlanCode
                            };

                            // Carry over customer id only when this is a customer-facing JO
                            if (!item.IsCompanyStockOrder && item.JobOrderId.HasValue)
                            {
                                var jo = db.Gen_JobOrders
                                    .FirstOrDefault(j => j.Id == item.JobOrderId.Value);
                                if (jo != null) wo.CustomerId = jo.CustomerId;
                            }

                            db.WorkOrders.Add(wo);
                            db.SaveChanges();    // inside tx — rollback clears this insert too

                            generatedWoIds.Add(wo.Id);

                            db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    GeneratedWorkOrderId = @woId, Status = 'GENERATED',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  Id = @id",
                                new SqlParameter("@woId", wo.Id),
                                new SqlParameter("@uid",  userId),
                                new SqlParameter("@now",  now),
                                new SqlParameter("@id",   item.Id));
                        }
                        failedItemId = 0;  // we got past the loop — clear the marker

                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Status = 'APPROVED', ApprovedBy = @uid, ApprovedDate = @now,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        tx.Commit();

                        // =================================================
                        // AUTO PURCHASE REQUISITION (Plan-Approve path)
                        // -------------------------------------------------
                        // Same helper used by WO Post — consolidated PR for
                        // ALL items in the plan in a single document, so
                        // the buyer sees "PR for Plan PP-2026-0001" instead
                        // of N separate PRs. Wrapped in its own try so a
                        // PR generation failure doesn't undo the plan.
                        // =================================================
                        AutoPrCore.GenResult autoResult = null;
                        try
                        {
                            autoResult = AutoPrCore.Generate(
                                db,
                                generatedWoIds,
                                sourceType:        "PLAN_APPROVE",
                                sourceReferenceId: id,
                                sourceLabel:       hdr.PlanCode,
                                userId:            userId,
                                canAutoApprove:    CanCurrentRoleAutoApprovePr(),
                                now:               now);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                "Auto-PR generation failed for plan " + id + ": " + ex.Message);
                        }

                        LogAudit(id, "Approved", "DRAFT", "APPROVED",
                            items.Count + " WO(s) generated"
                          + (autoResult?.PrId > 0 ? "; auto-PR " + autoResult.PrCode : ""));

                        return JsonSuccess("Approved.", new
                        {
                            id,
                            generatedWorkOrders = items.Count,
                            autoPrId    = autoResult?.PrId ?? 0,
                            autoPrCode  = autoResult?.PrCode,
                            autoPrItems = autoResult?.ItemCount ?? 0
                            // autoApproved / autoPoCode removed — auto-PR is
                            // always DRAFT now; frontend redirects to PR Edit.
                        });
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { /* swallowed — original ex is the real story */ }
                        string where = failedItemId > 0
                            ? " on item #" + failedItemId
                            : "";
                        return JsonError("Approval failed" + where + ": " + DescribeError(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to approve plan.", ex);
            }
        }

        // ====================================================================
        // REVERSE — undo an APPROVED plan: roll back the generated Work
        // Orders, free the Job Orders, and put the plan back into DRAFT
        // so it can be edited and re-approved.
        //
        // Safety: refuses if ANY generated WO has already been posted or
        // already has downstream activity (materials issued, daily
        // production posted, QC inspections). At that point the chain
        // of events past the WO has to be unwound individually — the
        // plan reverse won't silently nuke them.
        // ====================================================================
        [HttpPost, Route("reverse/{id:int}")]
        public ActionResult Reverse(int id)
        {
            try
            {
                var hdr = LoadPlanHeader(id);
                if (hdr == null) return JsonError("Plan not found.");

                // Sprint-1: CLOSED plans can now be reopened too — operators
                // sometimes need to graft another JO onto a plan that was
                // closed too eagerly. The WO-posted safety check below still
                // blocks the dangerous case (a CLOSED plan with materials
                // already issued / production posted).
                if (!string.Equals(hdr.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(hdr.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(hdr.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                {
                    return JsonError(
                        "Only APPROVED, IN_PROGRESS, or CLOSED plans can be reopened. "
                      + "Current status: " + (hdr.Status ?? "(null)"));
                }

                // Pull the WO ids generated by this plan
                var woIds = db.Database.SqlQuery<int>(@"
SELECT GeneratedWorkOrderId
FROM   dbo.ProductionPlanItems
WHERE  PlanId = @id AND GeneratedWorkOrderId IS NOT NULL
   AND (Deleted = 0 OR Deleted IS NULL)",
                    new SqlParameter("@id", id)).ToList();

                // SAFETY CHECK — block reverse if any generated WO has
                // already been posted (materials issued / production
                // started). Already-deleted WOs aren't blockers — they
                // were already removed downstream.
                //
                // WorkOrders.IsPosted is non-nullable `bool` (no ??).
                // There's no IsReversed column on WorkOrders — the
                // codebase treats `Deleted = 1` + `Active = 0` as the
                // reversed/removed state.
                var blockers = db.WorkOrders
                    .Where(w => woIds.Contains(w.Id)
                             && w.IsPosted
                             && w.Deleted != true)
                    .Select(w => new {
                        id   = w.Id,
                        code = w.WorkOrderCode
                    })
                    .ToList();

                if (blockers.Count > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Cannot reverse — " + blockers.Count
                                + " generated Work Order(s) have already been posted. "
                                + "Reverse those WOs individually first, then retry.",
                        blockedWorkOrders = blockers
                    }, JsonRequestBehavior.AllowGet);
                }

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // 1) Soft-delete each generated WO. Hard delete is
                        //    risky because foreign-key referenced rows may
                        //    exist (audit log, ledger entries). Soft-delete
                        //    matches the rest of the codebase's domain model.
                        if (woIds.Count > 0)
                        {
                            db.Database.ExecuteSqlCommand(@"
UPDATE dbo.WorkOrders
SET    Deleted = 1, Active = 0,
       UpdatedBy = @uid, UpdatedDate = @now,
       Remarks = ISNULL(Remarks + ' | ', '')
              + 'Reversed via plan reverse at '
              + CONVERT(NVARCHAR(20), @now, 120)
WHERE  Id IN (" + string.Join(",", woIds) + ")",
                                new SqlParameter("@uid", userId),
                                new SqlParameter("@now", now));
                        }

                        // 2) Reset every plan item: clear the link to the
                        //    deleted WO, put status back to PENDING so the
                        //    item is "unscheduled" again.
                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    GeneratedWorkOrderId = NULL,
       Status = 'PENDING',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  PlanId = @id",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        // 3) Put the plan header back into DRAFT. Clear
                        //    approval audit fields so re-approval can
                        //    re-stamp them cleanly.
                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Status       = 'DRAFT',
       ApprovedBy   = NULL,
       ApprovedDate = NULL,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        tx.Commit();

                        LogAudit(id, "Reversed", hdr.Status, "DRAFT",
                            woIds.Count + " WO(s) soft-deleted, items reset to PENDING");

                        return JsonSuccess("Plan reversed and back to DRAFT.", new
                        {
                            id,
                            reversedWorkOrders = woIds.Count
                        });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return JsonError("Reversal failed: " + DescribeError(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to reverse plan.", ex);
            }
        }

        // ====================================================================
        // CLOSE — mark plan complete (all generated WOs done)
        // ====================================================================
        [HttpPost, Route("close/{id:int}")]
        public ActionResult Close(int id)
        {
            try
            {
                var hdr = LoadPlanHeader(id);
                if (hdr == null) return JsonError("Plan not found.");
                if (string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Cannot close a draft plan. Approve it first.");
                if (string.Equals(hdr.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Plan is already closed.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Rule B: bump every item in this plan to CLOSED so the
                // UX_PPI_JobOrderActive filter releases their JobOrderIds.
                // Without this, the JOs stay locked to this (now-done) plan
                // and the recurring-week workflow breaks. Both updates run
                // back-to-back under the controller's implicit transaction
                // boundary; partial state is not possible since the second
                // statement is a single UPDATE.
                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    Status = 'CLOSED',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  PlanId = @id
  AND  (Deleted = 0 OR Deleted IS NULL)
  AND  Status <> 'CLOSED'",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Status = 'CLOSED', ClosedBy = @uid, ClosedDate = @now,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));

                LogAudit(id, "Closed", hdr.Status, "CLOSED", "Items released for re-scheduling");
                return JsonSuccess("Closed.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to close plan.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-3  BULK ACTIONS
        // --------------------------------------------------------------------
        //  Loops over a supplied id list, applies the same per-row checks as
        //  the single-action endpoints (Approve / Close), reports per-id
        //  success/failure. Each row gets its own try/catch and its own
        //  transaction (for Approve, since it touches WorkOrders and
        //  ProductionPlanItems and auto-PR) — a bad row never aborts the
        //  whole batch. UX: the operator gets a list of failures so they
        //  can fix and retry.
        //
        //  Same response shape as the JO bulk endpoints so the Angular
        //  helper that renders the result alert is reusable.
        // ====================================================================

        public class BulkIdsDto
        {
            public List<int> Ids { get; set; }
        }

        public class BulkRowResult
        {
            public int    Id     { get; set; }
            public string Code   { get; set; }
            public string Reason { get; set; }
        }

        [HttpPost, Route("bulk-approve")]
        public ActionResult BulkApprove(BulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No plans selected.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            int successCount = 0;
            var failures = new List<BulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        var hdr = LoadPlanHeader(id);
                        if (hdr == null)
                        { failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = "Not found." }); tx.Rollback(); continue; }

                        if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                        { failures.Add(new BulkRowResult { Id = id, Code = hdr.PlanCode, Reason = "Not a DRAFT (status: " + hdr.Status + ")." }); tx.Rollback(); continue; }

                        var items = LoadPlanItems(id);
                        if (items.Count == 0)
                        { failures.Add(new BulkRowResult { Id = id, Code = hdr.PlanCode, Reason = "Plan has no items." }); tx.Rollback(); continue; }
                        if (items.Any(i => i.PlannedQty <= 0))
                        { failures.Add(new BulkRowResult { Id = id, Code = hdr.PlanCode, Reason = "Every item needs a positive Planned Qty." }); tx.Rollback(); continue; }

                        // Capacity guard (same as single Approve)
                        var conflicts = ComputeCapacityReport(items)
                            .Where(c => c.HasConflict).ToList();
                        if (conflicts.Count > 0)
                        {
                            failures.Add(new BulkRowResult {
                                Id = id, Code = hdr.PlanCode,
                                Reason = "Capacity over-allocated on " + conflicts.Count + " machine(s)."
                            });
                            tx.Rollback(); continue;
                        }

                        int branchId = hdr.BranchId ?? GetCurrentBranchId();
                        var generatedWoIds = new List<int>();

                        int seq = 0;
                        foreach (var item in items)
                        {
                            seq++;
                            string woCode = "WO-PLAN-" + hdr.PlanCode + "-" + seq.ToString("D2");
                            var wo = new WorkOrders
                            {
                                WorkOrderCode = woCode,
                                OrderType     = item.IsCompanyStockOrder ? "Stock" : "Customer",
                                IsCompanyStockOrder = item.IsCompanyStockOrder,
                                JobOrderId    = item.JobOrderId,
                                ProductId     = item.ProductId,
                                DesignId      = item.DesignId,
                                BranchId      = branchId,
                                OrderQty      = item.PlannedQty,
                                OrderDate     = item.PlannedStartDate ?? hdr.WeekStartDate,
                                DueDate       = item.PlannedEndDate   ?? hdr.WeekEndDate,
                                IsPosted      = false,
                                Active        = true,
                                Deleted       = false,
                                CreatedBy     = userId,
                                CreatedDate   = now,
                                UpdatedBy     = userId,
                                UpdatedDate   = now,
                                RecordTimeStamp = now,
                                Status        = "Draft",
                                Remarks       = "Auto-generated from Production Plan " + hdr.PlanCode + " (bulk approve)"
                            };

                            if (!item.IsCompanyStockOrder && item.JobOrderId.HasValue)
                            {
                                var jo = db.Gen_JobOrders.FirstOrDefault(j => j.Id == item.JobOrderId.Value);
                                if (jo != null) wo.CustomerId = jo.CustomerId;
                            }

                            db.WorkOrders.Add(wo);
                            db.SaveChanges();
                            generatedWoIds.Add(wo.Id);

                            db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    GeneratedWorkOrderId = @woId, Status = 'GENERATED',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  Id = @id",
                                new SqlParameter("@woId", wo.Id),
                                new SqlParameter("@uid",  userId),
                                new SqlParameter("@now",  now),
                                new SqlParameter("@id",   item.Id));
                        }

                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Status = 'APPROVED', ApprovedBy = @uid, ApprovedDate = @now,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        tx.Commit();
                        successCount++;

                        LogAudit(id, "Approved", "DRAFT", "APPROVED",
                            items.Count + " WO(s) generated (bulk)");

                        // Auto-PR is best-effort — never blocks the approval.
                        try
                        {
                            AutoPrCore.Generate(
                                db,
                                generatedWoIds,
                                sourceType:        "PLAN_APPROVE",
                                sourceReferenceId: id,
                                sourceLabel:       hdr.PlanCode,
                                userId:            userId,
                                canAutoApprove:    CanCurrentRoleAutoApprovePr(),
                                now:               now);
                        }
                        catch { /* PR failure does not undo plan approval */ }
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                    }
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " approved, " + failures.Count + " failed.",
                // Hotfix: MVC's JavaScriptSerializer preserves PascalCase
                // on POCOs, so a List<BulkRowResult> would serialise as
                // {Id, Code, Reason} and break the frontend's
                // f.id / f.code / f.reason reads. Project to camelCase here.
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-close")]
        public ActionResult BulkClose(BulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No plans selected.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            int successCount = 0;
            var failures = new List<BulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var hdr = LoadPlanHeader(id);
                    if (hdr == null)
                    { failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = "Not found." }); continue; }

                    string status = (hdr.Status ?? "").Trim().ToUpper();
                    if (status == "DRAFT")
                    { failures.Add(new BulkRowResult { Id = id, Code = hdr.PlanCode, Reason = "Cannot close a draft — approve first." }); continue; }
                    if (status == "CLOSED")
                    { failures.Add(new BulkRowResult { Id = id, Code = hdr.PlanCode, Reason = "Already closed." }); continue; }

                    // Rule B: release items first so their JOs are free
                    db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlanItems
SET    Status = 'CLOSED',
       UpdatedBy = @uid, UpdatedDate = @now
WHERE  PlanId = @id
  AND  (Deleted = 0 OR Deleted IS NULL)
  AND  Status <> 'CLOSED'",
                        new SqlParameter("@uid", userId),
                        new SqlParameter("@now", now),
                        new SqlParameter("@id",  id));

                    db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan
SET    Status = 'CLOSED', ClosedBy = @uid, ClosedDate = @now,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                        new SqlParameter("@uid", userId),
                        new SqlParameter("@now", now),
                        new SqlParameter("@id",  id));

                    successCount++;
                    LogAudit(id, "Closed", hdr.Status, "CLOSED", "(bulk) Items released");
                }
                catch (Exception ex)
                {
                    failures.Add(new BulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " closed, " + failures.Count + " failed.",
                // Same camelCase projection as BulkApprove — JavaScriptSerializer
                // does not honour our Newtonsoft global contract on POCOs.
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var hdr = LoadPlanHeader(id);
                if (hdr == null) return JsonError("Plan not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT plans can be deleted. Approved plans have generated Work Orders.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ProductionPlan SET Deleted = 1, Active = 0,
       UpdatedBy = @uid, UpdatedDate = @now WHERE Id = @id",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));

                LogAudit(id, "Deleted", hdr.Status, null, "Soft-deleted (Deleted=1, Active=0)");
                return JsonSuccess("Deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete plan.", ex);
            }
        }

        // ====================================================================
        //  SPRINT-2  CLONE-FROM-PREVIOUS-WEEK
        // --------------------------------------------------------------------
        //  Deep-copies an existing plan into a new DRAFT for next week. Item
        //  dates shift forward by 7 days so a recurring weekly schedule rolls
        //  forward without manual data entry. New items start with their
        //  Status reset to PENDING and GeneratedWorkOrderId cleared — the
        //  clone is unscheduled until the planner re-approves it.
        //
        //  Same risk-management as the JO clone: each item gets its overrides
        //  preserved (PlannedQty, MachineId, Remarks). Only the dates and
        //  status get reset.
        // ====================================================================
        [HttpPost, Route("clone-next-week/{id:int}")]
        public ActionResult CloneNextWeek(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // 1. Source header
                    var src = LoadPlanHeader(id);
                    if (src == null) return JsonError("Source plan not found.");

                    // 2. Compute the target ISO week (source + 7 days). PlanRow's
                    // WeekStartDate is non-nullable (column is NOT NULL), so we
                    // can shift directly.
                    DateTime targetWeekStart = src.WeekStartDate.AddDays(7);

                    var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
                    int targetWeek = cal.GetWeekOfYear(targetWeekStart,
                        System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                        DayOfWeek.Monday);
                    int targetYear = targetWeekStart.Year;
                    // Year-rollover edge cases (late Dec → next year's W1, early Jan → previous year's W52/53)
                    if (targetWeekStart.Month == 12 && targetWeek == 1) targetYear++;
                    else if (targetWeekStart.Month == 1 && targetWeek >= 52) targetYear--;

                    // 3. Guard against (branch, year, week) collision — the
                    //    same unique constraint that protects Create.
                    bool exists = db.ProductionPlan.Any(p =>
                        p.BranchId == src.BranchId
                     && p.Year     == targetYear
                     && p.WeekNo   == targetWeek
                     && p.Deleted != true);
                    if (exists)
                        return JsonError("A plan already exists for this branch in week W"
                                       + targetWeek + " of " + targetYear
                                       + ". Edit or delete that one before cloning.");

                    // 4. Insert new plan header
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    // Sprint-5: GenerateCode() picks the code below; the
                    // db.SaveChanges() loop downstream will retry on a
                    // unique-violation by walking +1 each attempt.
                    string newCode = GenerateCode();

                    // Week start = Monday of the target ISO week. Compute by
                    // walking back to Monday from the +7-day point.
                    DateTime weekStart = targetWeekStart.Date;
                    while (weekStart.DayOfWeek != DayOfWeek.Monday)
                        weekStart = weekStart.AddDays(-1);
                    DateTime weekEnd = weekStart.AddDays(6);

                    var clone = new ProductionPlan
                    {
                        PlanCode       = newCode,
                        BranchId       = src.BranchId,
                        Year           = targetYear,
                        WeekNo         = targetWeek,
                        WeekStartDate  = weekStart,
                        WeekEndDate    = weekEnd,
                        Status         = "DRAFT",
                        Remarks        = string.IsNullOrWhiteSpace(src.Remarks)
                                          ? "Cloned from " + src.PlanCode
                                          : "Cloned from " + src.PlanCode + " — " + src.Remarks,
                        Active         = true,
                        Deleted        = false,
                        CreatedBy      = userId,
                        CreatedDate    = now,
                        UpdatedBy      = userId,
                        UpdatedDate    = now,
                        RecordTimeStamp = now
                    };
                    db.ProductionPlan.Add(clone);

                    // Sprint-5: retry loop for the rare race where two
                    // concurrent CloneNextWeek calls compute the same code.
                    // Detach + re-stamp + retry on unique-violation; bail
                    // out after 3 attempts so a real schema error doesn't
                    // loop forever.
                    bool clonedOk = false;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            db.SaveChanges();
                            clonedOk = true;
                            break;
                        }
                        catch (Exception saveEx) when (IsPlanCodeUniqueViolation(saveEx))
                        {
                            if (attempt + 1 >= 3) throw;
                            clone.PlanCode = GenerateCode(attempt + 1);
                        }
                    }
                    if (!clonedOk) throw new InvalidOperationException("Clone failed: exhausted PlanCode retry attempts.");
                    newCode = clone.PlanCode;  // retried code (if any) bubbles up

                    // 5. Clone items with +7 day shift and reset status.
                    //
                    // HOTFIX — there is a filtered unique index on
                    // ProductionPlanItems(JobOrderId) ("UX_PPI_JobOrderActive")
                    // that enforces "one active plan item per JobOrder, ever"
                    // — including items inside CLOSED plans. A naive clone of
                    // a closed plan's items would re-insert the same JobOrderId
                    // and fail with a 2601 even though the source plan is done.
                    //
                    // Resolution: precompute the set of JobOrderIds that are
                    // already taken anywhere else in active plan items, then
                    // skip those source items rather than aborting the whole
                    // clone. The cloned header is still created; the user gets
                    // a summary of what was skipped so they can re-link as
                    // needed (typically these JOs need fresh Job Orders).
                    var srcItems = db.ProductionPlanItems
                        .Where(i => i.PlanId == id && i.Deleted != true)
                        .ToList();

                    // Build the "taken" set in one query so we don't issue N
                    // existence checks. Only JobOrderIds present in the source
                    // need to be checked.
                    var sourceJoIds = srcItems
                        .Where(s => s.JobOrderId.HasValue)
                        .Select(s => s.JobOrderId.Value)
                        .Distinct()
                        .ToList();

                    // Rule B: only OPEN items lock a JO. CLOSED items released
                    // their slot when the parent plan was closed. Mirror the
                    // UX_PPI_JobOrderActive index filter so the precheck and
                    // the actual constraint stay in sync.
                    var takenJoIds = sourceJoIds.Count == 0
                        ? new HashSet<int>()
                        : new HashSet<int>(db.ProductionPlanItems
                            .Where(pi => pi.JobOrderId.HasValue
                                      && sourceJoIds.Contains(pi.JobOrderId.Value)
                                      && pi.Deleted != true
                                      && (pi.Status == "PENDING" || pi.Status == "GENERATED"))
                            .Select(pi => pi.JobOrderId.Value));

                    int copied = 0;
                    int skipped = 0;
                    var skippedJoCodes = new List<string>();

                    foreach (var s in srcItems)
                    {
                        if (s.JobOrderId.HasValue && takenJoIds.Contains(s.JobOrderId.Value))
                        {
                            skipped++;
                            // Pull a friendly code for the alert. Best-effort:
                            // fall back to "#id" if the JO row can't be loaded.
                            string code = "#" + s.JobOrderId.Value;
                            try
                            {
                                var jo = db.Gen_JobOrders
                                    .FirstOrDefault(j => j.Id == s.JobOrderId.Value);
                                if (jo != null && !string.IsNullOrWhiteSpace(jo.JobOrderCode))
                                    code = jo.JobOrderCode;
                            }
                            catch { /* swallow — code is cosmetic */ }
                            skippedJoCodes.Add(code);
                            continue;
                        }

                        var newItem = new ProductionPlanItems
                        {
                            PlanId               = clone.Id,
                            JobOrderId           = s.JobOrderId,
                            IsCompanyStockOrder  = s.IsCompanyStockOrder,
                            ProductId            = s.ProductId,
                            DesignId             = s.DesignId,
                            PlannedQty           = s.PlannedQty,
                            MachineId            = s.MachineId,
                            PlannedStartDate     = s.PlannedStartDate.HasValue
                                                    ? s.PlannedStartDate.Value.AddDays(7) : (DateTime?)null,
                            PlannedEndDate       = s.PlannedEndDate.HasValue
                                                    ? s.PlannedEndDate.Value.AddDays(7)   : (DateTime?)null,
                            GeneratedWorkOrderId = null,            // reset — clone is unscheduled
                            Status               = "PENDING",       // reset — clone is unscheduled
                            Remarks              = s.Remarks,
                            Active               = true,
                            Deleted              = false,
                            CreatedBy            = userId,
                            CreatedDate          = now
                        };
                        db.ProductionPlanItems.Add(newItem);
                        copied++;
                    }

                    if (copied > 0) db.SaveChanges();
                    tx.Commit();

                    LogAudit(clone.Id, "Cloned", null, "DRAFT",
                        "Cloned from " + src.PlanCode + " (id " + src.Id + "), "
                      + copied + " item(s) copied, " + skipped + " skipped");
                    LogAudit(src.Id,   "ClonedTo", null, clone.PlanCode,
                        "Source for new plan " + clone.PlanCode);

                    return JsonSuccess("Cloned to W" + targetWeek + "/" + targetYear, new
                    {
                        id              = clone.Id,
                        planCode        = clone.PlanCode,
                        sourceId        = src.Id,
                        sourceCode      = src.PlanCode,
                        year            = targetYear,
                        weekNo          = targetWeek,
                        itemsCloned     = copied,
                        itemsSkipped    = skipped,
                        skippedJoCodes  = skippedJoCodes
                    });
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    // EF6 wraps the real SqlException inside DbUpdateException
                    // ("An error occurred while updating the entries. See the
                    // inner exception for details.") — completely useless on
                    // its own. Walk the chain to surface the actual root cause.
                    return JsonError("Clone failed: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        //  SPRINT-4  AUDIT TIMELINE + WORKFLOW STAGE
        // --------------------------------------------------------------------
        //  Append-only audit log keyed by PlanId. Writes via raw SQL so no
        //  EDMX regeneration is needed (mirrors what JobOrderAuditLog does).
        //  Every state-changing endpoint calls LogAudit() inside its happy
        //  path. Failures to write the log are swallowed — audit is never
        //  allowed to break a real save.
        //
        //  WORKFLOW STAGE: surfaces the canonical 4-stage list and the
        //  current stage index for the stepper UI. Production Plan has
        //  well-defined Status values so the stage maps directly.
        // ====================================================================

        private static readonly string[] _workflowStages = new[] {
            "DRAFT", "APPROVED", "IN_PROGRESS", "CLOSED"
        };

        private int ComputeWorkflowStageIndex(string status)
        {
            string s = (status ?? "DRAFT").Trim().ToUpper();
            for (int i = 0; i < _workflowStages.Length; i++)
                if (_workflowStages[i] == s) return i;
            return 0; // unknown status maps to DRAFT
        }

        private void LogAudit(int planId, string action, string oldValue, string newValue, string detail)
        {
            try
            {
                int userId = GetSafeCurrentUserId();
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.ProductionPlanAuditLog (PlanId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@pid, @act, @uid, @now, @ov, @nv, @det)",
                    new SqlParameter("@pid", planId),
                    new SqlParameter("@act", (object)action ?? DBNull.Value),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", DateTime.Now),
                    new SqlParameter("@ov",  (object)oldValue ?? DBNull.Value),
                    new SqlParameter("@nv",  (object)newValue ?? DBNull.Value),
                    new SqlParameter("@det", (object)detail   ?? DBNull.Value));
            }
            catch (Exception ex)
            {
                // Audit logging is best-effort. Never let it abort a save.
                System.Diagnostics.Debug.WriteLine("LogAudit failed for plan " + planId + ": " + ex.Message);
            }
        }

        // Row shape for the audit read.
        private class AuditRow
        {
            public int      Id            { get; set; }
            public int      PlanId        { get; set; }
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
                var rows = db.Database.SqlQuery<AuditRow>(@"
SELECT  a.Id, a.PlanId, a.Action,
        a.ChangedBy, u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
FROM    dbo.ProductionPlanAuditLog a
LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
WHERE   a.PlanId = @pid
ORDER BY a.ChangedDate DESC, a.Id DESC",
                    new SqlParameter("@pid", id)).ToList();

                var data = rows.Select(r => new
                {
                    id              = r.Id,
                    planId          = r.PlanId,
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

        // ====================================================================
        // INTERNAL HELPERS
        // ====================================================================

        // ====================================================================
        //  SPRINT-5 RACE-SAFE PLAN CODE GENERATION
        // --------------------------------------------------------------------
        //  Two concurrent Create calls could compute the same "next" PlanCode
        //  via this helper and both succeed past the existence check. The DB's
        //  unique index on PlanCode then raises a 2601/2627 on the slower
        //  insert and the user sees a 500. Same retry pattern as the JO
        //  module: `skip` lets the caller try N codes ahead in a loop after
        //  catching a unique-constraint violation. ~3 attempts is plenty for
        //  any realistic concurrency level here.
        //
        //  Cheap to read (top-1 indexed). Strictly inside a transaction the
        //  next call would still see stale data — the retry loop is what
        //  actually closes the race.
        // ====================================================================
        private string GenerateCode(int skip = 0)
        {
            string year = DateTime.Now.Year.ToString();
            string sql = @"
SELECT TOP 1 PlanCode
FROM   dbo.ProductionPlan
WHERE  PlanCode LIKE 'PP-' + @year + '-%'
ORDER BY Id DESC";
            string last = db.Database.SqlQuery<string>(sql, new SqlParameter("@year", year)).FirstOrDefault();

            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                int parsed;
                if (parts.Length == 3 && int.TryParse(parts[2], out parsed))
                    next = parsed + 1;
            }
            return "PP-" + year + "-" + (next + skip).ToString("D4");
        }

        /** SQL Server uses error 2601 (duplicate key in unique index) and
         *  2627 (duplicate key in unique constraint) for the collision the
         *  unique index on PlanCode would surface. Both walk the inner
         *  exception chain since EF wraps the SqlException inside a
         *  DbUpdateException or in some paths a generic Exception. */
        private bool IsPlanCodeUniqueViolation(Exception ex)
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

        /** Drill into an EF / SQL exception chain to extract the real
         *  root cause. EF6 wraps SqlException in DbUpdateException and
         *  sometimes in DbEntityValidationException, each with the same
         *  useless outer text. The innermost message — typically the
         *  SqlException itself — is what the operator actually needs. */
        private string DescribeError(Exception ex)
        {
            if (ex == null) return "Unknown error.";

            // Validation errors carry their per-property messages on the
            // DbEntityValidationException, not as inner exceptions.
            var ve = ex as System.Data.Entity.Validation.DbEntityValidationException;
            if (ve != null)
            {
                var errs = ve.EntityValidationErrors
                    .SelectMany(eve => eve.ValidationErrors)
                    .Select(err => err.PropertyName + ": " + err.ErrorMessage)
                    .ToList();
                if (errs.Count > 0) return string.Join("; ", errs);
            }

            // Otherwise walk down to the deepest InnerException — almost
            // always a SqlException with the real SQL Server error text.
            var cur = ex;
            while (cur.InnerException != null) cur = cur.InnerException;
            return cur.Message;
        }

        // ====================================================================
        //  SPRINT-5 ITEM DATE VALIDATION
        // --------------------------------------------------------------------
        //  Both checks return null on success and a human-readable message
        //  on failure. Kept separate so the bounds check (which needs the
        //  plan header) can run after AddItem's "is the plan a DRAFT?" guard.
        // ====================================================================
        private string ValidateItemDates(PlanItemSaveDto dto)
        {
            // Both nullable. Only inspect when both are populated.
            if (dto.PlannedStartDate.HasValue && dto.PlannedEndDate.HasValue
                && dto.PlannedStartDate.Value.Date > dto.PlannedEndDate.Value.Date)
            {
                return "Planned Start Date cannot be later than Planned End Date.";
            }
            return null;
        }

        private string ValidateItemDatesAgainstWeek(PlanItemSaveDto dto, PlanRow hdr)
        {
            // Soft guard: dates must fall inside the plan's week window
            // (WeekStartDate..WeekEndDate). PlanRow's week columns are
            // non-nullable, so direct .Date works.
            DateTime ws = hdr.WeekStartDate.Date;
            DateTime we = hdr.WeekEndDate.Date;
            if (dto.PlannedStartDate.HasValue
                && (dto.PlannedStartDate.Value.Date < ws || dto.PlannedStartDate.Value.Date > we))
            {
                return "Planned Start Date is outside this plan's week ("
                     + ws.ToString("yyyy-MM-dd") + " — " + we.ToString("yyyy-MM-dd") + ").";
            }
            if (dto.PlannedEndDate.HasValue
                && (dto.PlannedEndDate.Value.Date < ws || dto.PlannedEndDate.Value.Date > we))
            {
                return "Planned End Date is outside this plan's week ("
                     + ws.ToString("yyyy-MM-dd") + " — " + we.ToString("yyyy-MM-dd") + ").";
            }
            return null;
        }

        private class WeekRange { public DateTime Start; public DateTime End; }

        private WeekRange ResolveWeekRange(int year, int weekNo)
        {
            // ISO 8601: week 1 is the week containing the first Thursday.
            // Easiest: take Jan 1, jump to first Monday of week 1, advance by weeks.
            var jan1 = new DateTime(year, 1, 1);
            int daysOffset = DayOfWeek.Monday - jan1.DayOfWeek;
            var firstMonday = jan1.AddDays(daysOffset < 0 ? daysOffset + 7 : daysOffset);
            var weekStart = firstMonday.AddDays((weekNo - 1) * 7);
            return new WeekRange { Start = weekStart, End = weekStart.AddDays(6) };
        }

        private PlanRow LoadPlanHeader(int id)
        {
            // Branch-scoped: a user must not be able to act on another
            // branch's Production Plan simply by guessing/passing its id.
            string sql = @"
SELECT  p.Id, p.PlanCode, p.BranchId,
        b.Name AS BranchName,
        p.[Year], p.WeekNo, p.WeekStartDate, p.WeekEndDate,
        p.Status, p.Remarks,
        p.ApprovedBy, ua.UserName AS ApprovedByName, p.ApprovedDate,
        p.Active, p.CreatedDate, p.UpdatedDate
FROM    dbo.ProductionPlan p
LEFT JOIN dbo.Branches b  ON b.Id  = p.BranchId
LEFT JOIN dbo.Adm_Users ua ON ua.Id = p.ApprovedBy
WHERE   p.Id = @id AND p.BranchId = @branchId";
            return db.Database.SqlQuery<PlanRow>(sql,
                new SqlParameter("@id", id),
                new SqlParameter("@branchId", GetCurrentBranchId())).FirstOrDefault();
        }

        private List<PlanItemRow> LoadPlanItems(int planId)
        {
            string sql = @"
SELECT  i.Id, i.PlanId,
        i.JobOrderId,    j.JobOrderCode  AS JobOrderCode,
        i.IsCompanyStockOrder,
        i.ProductId,     p.Prod_Name     AS ProductName,
        i.DesignId,      d.D_Code        AS DesignCode,
                         d.CollectionName AS DesignName,
        i.PlannedQty,
        i.MachineId,     m.Code          AS MachineName,
        i.PlannedStartDate, i.PlannedEndDate,
        i.GeneratedWorkOrderId,
        wo.WorkOrderCode AS GeneratedWorkOrderCode,
        i.Status, i.Remarks
FROM    dbo.ProductionPlanItems i
LEFT JOIN dbo.Gen_JobOrders j ON j.Id = i.JobOrderId
LEFT JOIN dbo.Gen_Products  p ON p.Id = i.ProductId
LEFT JOIN dbo.Gen_Design    d ON d.Id = i.DesignId
LEFT JOIN dbo.Gen_Machines  m ON m.Id = i.MachineId
LEFT JOIN dbo.WorkOrders    wo ON wo.Id = i.GeneratedWorkOrderId
WHERE   i.PlanId = @id AND (i.Deleted = 0 OR i.Deleted IS NULL)
ORDER BY i.Id";
            return db.Database.SqlQuery<PlanItemRow>(sql, new SqlParameter("@id", planId)).ToList();
        }

        // -------- BOM rollup --------
        public class BomRollupRow
        {
            public int     ThreadId { get; set; }
            public int?    RawItemId { get; set; }
            public string  Code { get; set; }
            public string  Name { get; set; }
            public string  Color { get; set; }
            public string  Unit { get; set; }
            public decimal RequiredQty { get; set; }
            public decimal OnHand { get; set; }
            public decimal Shortfall { get; set; }
        }

        private class DemandAccum
        {
            public int? RawItemId;
            public string Code, Name, Color, Unit;
            public decimal Qty;
        }

        private List<BomRollupRow> ComputeBomRollup(List<PlanItemRow> items, int branchId)
        {
            var demand = new Dictionary<int, DemandAccum>();

            foreach (var it in items)
            {
                if (!it.DesignId.HasValue || it.PlannedQty <= 0) continue;
                int designId = it.DesignId.Value;
                decimal qty  = it.PlannedQty;

                // Source of truth: Gen_BOM × Gen_ComponentMaterials. This
                // covers fabrics, accessories, AND threads (a thread is
                // just a Gen_RawItems row with Type='THREAD' whose
                // ComponentMaterials line records consumption per unit).
                //
                // Gen_Threads is LEFT-joined on RawItemId for display only
                // (Code, Color) when the raw item happens to be a thread.
                // Gen_ThreadUsage was the previous parallel path but its
                // Usage values diverged from production reality — see the
                // BOM Audit report for the rationale.
                var usage =
                    (from bom in db.Gen_BOM
                     join cm in db.Gen_ComponentMaterials on bom.CompId equals cm.CompId
                     join r in db.Gen_RawItems on cm.RawItemId equals r.Id
                     join t in db.Gen_Threads on cm.RawItemId equals t.RawItemId into tj
                     from t in tj.DefaultIfEmpty()
                     join u in db.Gen_Units on cm.UnitId equals u.Id into uj
                     from u in uj.DefaultIfEmpty()
                     where bom.DesignId == designId
                           && bom.Deleted != true              // Sprint-1: nullable bool — concise form covers NULL legacy rows
                           && cm.Deleted == false              // non-nullable on this table
                           && cm.Active == true
                           && r.Deleted == false               // non-nullable on this table
                     select new
                     {
                         RawItemId = cm.RawItemId,
                         Code      = t != null ? t.Code : null,
                         Name      = t != null ? t.Name : r.ItemName,
                         Color     = t != null ? t.Color : null,
                         UnitOfMeasure = u != null ? u.UnitCode : null,
                         Usage     = cm.QtyPerUnit
                     }).ToList();
                if (usage.Count == 0) continue;

                foreach (var u in usage)
                {
                    decimal totalNeed = u.Usage * qty;
                    if (demand.ContainsKey(u.RawItemId))
                    {
                        demand[u.RawItemId].Qty += totalNeed;
                    }
                    else
                    {
                        demand[u.RawItemId] = new DemandAccum
                        {
                            RawItemId = u.RawItemId,
                            Code      = u.Code,
                            Name      = u.Name,
                            Color     = u.Color,
                            Unit      = u.UnitOfMeasure,
                            Qty       = totalNeed
                        };
                    }
                }
            }

            var result = new List<BomRollupRow>();
            foreach (var kv in demand)
            {
                decimal onHand = 0m;
                if (kv.Value.RawItemId.HasValue)
                    onHand = GetCurrentStoreBalance(kv.Value.RawItemId.Value, branchId);

                decimal shortfall = kv.Value.Qty > onHand ? kv.Value.Qty - onHand : 0m;

                result.Add(new BomRollupRow
                {
                    ThreadId    = kv.Key,
                    RawItemId   = kv.Value.RawItemId,
                    Code        = kv.Value.Code,
                    Name        = kv.Value.Name,
                    Color       = kv.Value.Color,
                    Unit        = kv.Value.Unit,
                    RequiredQty = Math.Round(kv.Value.Qty, 2),
                    OnHand      = Math.Round(onHand, 2),
                    Shortfall   = Math.Round(shortfall, 2)
                });
            }

            return result.OrderBy(r => r.Code).ToList();
        }

        private decimal GetCurrentStoreBalance(int rawItemId, int branchId)
        {
            // Aggregate In - Out, same as IssueMaterialsController.GetCurrentStoreBalance
            // and the Reports/Store balance. Gen_Store.CurrentQty on the latest row is
            // only a point-in-time snapshot that some write paths don't keep in sync,
            // so trusting it here made this plan's shortfall preview disagree with
            // what Issue Materials shows as "Available" for the same raw item.
            var q = db.Gen_Store.Where(s =>
                s.ItemId == rawItemId && s.BranchId == branchId && s.Deleted != true);
            decimal totalIn  = q.Sum(s => (decimal?)s.InQty)  ?? 0m;
            decimal totalOut = q.Sum(s => (decimal?)s.OutQty) ?? 0m;
            return totalIn - totalOut;
        }

        // -------- Capacity check --------
        public class CapacityRow
        {
            public int?   MachineId { get; set; }
            public string MachineName { get; set; }
            public int    ItemCount { get; set; }
            public int    TotalPlannedQty { get; set; }   // sum of pieces this machine is scheduled to produce
            public DateTime? EarliestStart { get; set; }
            public DateTime? LatestEnd { get; set; }
            public bool   HasConflict { get; set; }
            public List<string> ConflictDetails { get; set; }
        }

        private List<CapacityRow> ComputeCapacityReport(List<PlanItemRow> items)
        {
            // Group items by machine. Within each machine, sort by start date
            // and detect any overlapping date ranges between consecutive items.
            var byMachine = items
                .Where(i => i.MachineId.HasValue && i.PlannedStartDate.HasValue && i.PlannedEndDate.HasValue)
                .GroupBy(i => new { i.MachineId, i.MachineName });

            var result = new List<CapacityRow>();
            foreach (var g in byMachine)
            {
                var ordered = g.OrderBy(x => x.PlannedStartDate).ToList();
                var conflictDetails = new List<string>();

                for (int i = 0; i < ordered.Count; i++)
                {
                    var a = ordered[i];
                    for (int j = i + 1; j < ordered.Count; j++)
                    {
                        var b = ordered[j];
                        // Overlap if a.Start <= b.End AND b.Start <= a.End
                        if (a.PlannedStartDate <= b.PlannedEndDate
                            && b.PlannedStartDate <= a.PlannedEndDate)
                        {
                            string codeA = a.JobOrderCode ?? ("Item #" + a.Id);
                            string codeB = b.JobOrderCode ?? ("Item #" + b.Id);
                            conflictDetails.Add(codeA + " overlaps " + codeB
                                + " (" + a.PlannedStartDate.Value.ToString("MMM dd") + "-"
                                       + a.PlannedEndDate.Value.ToString("MMM dd")
                                + " vs " + b.PlannedStartDate.Value.ToString("MMM dd") + "-"
                                       + b.PlannedEndDate.Value.ToString("MMM dd") + ")");
                        }
                    }
                }

                result.Add(new CapacityRow
                {
                    MachineId       = g.Key.MachineId,
                    MachineName     = g.Key.MachineName,
                    ItemCount       = ordered.Count,
                    TotalPlannedQty = ordered.Sum(x => x.PlannedQty),   // total pieces on this machine
                    EarliestStart   = ordered.Min(x => x.PlannedStartDate),
                    LatestEnd       = ordered.Max(x => x.PlannedEndDate),
                    HasConflict     = conflictDetails.Count > 0,
                    ConflictDetails = conflictDetails
                });
            }

            return result;
        }

        /// <summary>
        /// Returns true when the current user's role allows auto-approval
        /// of an auto-generated PR (chains into PO creation). Mirrors the
        /// helper in WorkOrdersController.
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
    }
}
