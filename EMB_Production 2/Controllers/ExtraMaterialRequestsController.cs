using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;

namespace EMB_Production.Controllers
{
    // ============================================================
    // Extra Material Request (EMR)
    // ------------------------------------------------------------
    // Records material issued to a Work Order BEYOND the original
    // BOM allocation — waste, rework, damage, design change, etc.
    //
    // Lifecycle:
    //   DRAFT     – operator builds the request
    //   SUBMITTED – operator submits for approval
    //   APPROVED  – approval also issues stock & folds cost into WO
    //               (single-step approval, auto-issue per user spec)
    //   REJECTED  – approver rejected; no stock movement
    //
    // Data:
    //   ExtraMaterialRequest        – header
    //   ExtraMaterialRequestItems   – line items (RAW or THREAD)
    //   Gen_Store                   – ledger writes on approval
    //                                 (ReferenceType='EMR_ISSUE',
    //                                  ReferenceId = WorkOrders.Id)
    //
    // The tables are read/written via raw SQL so the build does not
    // require an EDMX refresh.
    // ============================================================
    [RoutePrefix("emb-products/extra-material-request")]
    public class ExtraMaterialRequestsController : BaseController
    {
        // --------------------------------------------------------
        // EmrRow — flat projection used by /list, /detail, /profile
        // --------------------------------------------------------
        public class EmrRow
        {
            public int    Id { get; set; }
            public string EmrCode { get; set; }
            public int    WorkOrderId { get; set; }
            public string WorkOrderCode { get; set; }
            public int?   BranchId { get; set; }
            public string BranchName { get; set; }
            public DateTime RequestDate { get; set; }
            public string Reason { get; set; }
            public string Remarks { get; set; }
            public string Status { get; set; }
            public int?   RequestedBy { get; set; }
            public string RequestedByName { get; set; }
            public DateTime? SubmittedDate { get; set; }
            public int?   ApprovedBy { get; set; }
            public string ApprovedByName { get; set; }
            public DateTime? ApprovedDate { get; set; }
            public string RejectionReason { get; set; }
            public string IssueReferenceNo { get; set; }
            public bool?  Active { get; set; }
            public DateTime? CreatedDate { get; set; }
            public DateTime? UpdatedDate { get; set; }
        }

        public class EmrItemRow
        {
            public int     Id { get; set; }
            public int     EmrId { get; set; }
            public string  ItemType { get; set; }
            public int?    RawItemId { get; set; }
            public int?    ThreadId { get; set; }
            public string  ItemName { get; set; }
            public string  ItemCode { get; set; }
            public string  UnitOfMeasure { get; set; }
            public decimal Qty { get; set; }
            public decimal? Rate { get; set; }
            public decimal? Amount { get; set; }
            public string  Remarks { get; set; }
        }

        // ====================================================================
        // LIST
        // ====================================================================
        [HttpGet, Route("list")]
        public ActionResult List(
            string status = "",
            int? workOrderId = null,
            int? branchId = null,
            string search = "",
            string showDeleted = "")
        {
            try
            {
                string deletedClause = string.Equals(showDeleted, "yes", StringComparison.OrdinalIgnoreCase)
                    ? " (e.Deleted = 1) "
                    : " (e.Deleted = 0 OR e.Deleted IS NULL) ";

                var parameters = new List<SqlParameter>();
                string where = " WHERE " + deletedClause;

                if (!string.IsNullOrWhiteSpace(status))
                {
                    where += " AND e.Status = @status ";
                    parameters.Add(new SqlParameter("@status", status.Trim().ToUpper()));
                }
                if (workOrderId.HasValue && workOrderId.Value > 0)
                {
                    where += " AND e.WorkOrderId = @woId ";
                    parameters.Add(new SqlParameter("@woId", workOrderId.Value));
                }
                if (branchId.HasValue && branchId.Value > 0)
                {
                    where += " AND e.BranchId = @brId ";
                    parameters.Add(new SqlParameter("@brId", branchId.Value));
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    where += " AND (e.EmrCode LIKE @s OR wo.WorkOrderCode LIKE @s OR e.Reason LIKE @s) ";
                    parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
                }

                string sql = @"
SELECT  e.Id, e.EmrCode, e.WorkOrderId,
        wo.WorkOrderCode    AS WorkOrderCode,
        e.BranchId,
        b.Name              AS BranchName,
        e.RequestDate, e.Reason, e.Remarks, e.Status,
        e.RequestedBy,
        ur.UserName         AS RequestedByName,
        e.SubmittedDate,
        e.ApprovedBy,
        ua.UserName         AS ApprovedByName,
        e.ApprovedDate,
        e.RejectionReason,
        e.IssueReferenceNo,
        e.Active, e.CreatedDate, e.UpdatedDate
FROM    dbo.ExtraMaterialRequest e
LEFT JOIN dbo.WorkOrders   wo ON wo.Id = e.WorkOrderId
LEFT JOIN dbo.Branches     b  ON b.Id  = e.BranchId
LEFT JOIN dbo.Adm_Users    ur ON ur.Id = e.RequestedBy
LEFT JOIN dbo.Adm_Users    ua ON ua.Id = e.ApprovedBy
" + where + " ORDER BY e.Id DESC ";

                var rows = db.Database.SqlQuery<EmrRow>(sql, parameters.ToArray()).ToList();

                var summary = new
                {
                    count        = rows.Count,
                    draftCount   = rows.Count(r => r.Status == "DRAFT"),
                    pendingCount = rows.Count(r => r.Status == "SUBMITTED"),
                    approvedCount= rows.Count(r => r.Status == "APPROVED"),
                    rejectedCount= rows.Count(r => r.Status == "REJECTED")
                };

                return JsonSuccess("OK", new { items = rows, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load EMR list.", ex);
            }
        }

        // ====================================================================
        // DETAIL — single header + items
        // ====================================================================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var header = LoadHeader(id);
                if (header == null) return JsonError("Record not found.");

                var items = LoadItems(id);

                return JsonSuccess("OK", new { header, items });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load EMR detail.", ex);
            }
        }

        // ====================================================================
        // NEXT CODE — for the Create modal
        // ====================================================================
        [HttpGet, Route("next-code")]
        public ActionResult NextCode()
        {
            try
            {
                string year = DateTime.Now.Year.ToString();
                string sql = @"
SELECT TOP 1 EmrCode
FROM   dbo.ExtraMaterialRequest
WHERE  EmrCode LIKE 'EMR-' + @year + '-%'
ORDER BY Id DESC";

                string last = db.Database
                    .SqlQuery<string>(sql, new SqlParameter("@year", year))
                    .FirstOrDefault();

                int next = 1;
                if (!string.IsNullOrWhiteSpace(last))
                {
                    var parts = last.Split('-');
                    int parsed;
                    if (parts.Length == 3 && int.TryParse(parts[2], out parsed))
                        next = parsed + 1;
                }

                string code = "EMR-" + year + "-" + next.ToString("D4");
                return JsonSuccess("OK", new { code });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to generate next EMR code.", ex);
            }
        }

        // ====================================================================
        // CREATE (Draft)
        // ====================================================================
        public class EmrSaveDto
        {
            public int Id { get; set; }
            public string EmrCode { get; set; }
            public int WorkOrderId { get; set; }
            public string Reason { get; set; }
            public string Remarks { get; set; }
            public List<EmrItemSaveDto> Items { get; set; }
        }
        public class EmrItemSaveDto
        {
            public int Id { get; set; }
            public string ItemType { get; set; } // RAW or THREAD
            public int? RawItemId { get; set; }
            public int? ThreadId { get; set; }
            public string UnitOfMeasure { get; set; }
            public decimal Qty { get; set; }
            public decimal? Rate { get; set; }
            public string Remarks { get; set; }
        }

        [HttpPost, Route("create")]
        public ActionResult Create(EmrSaveDto dto)
        {
            if (dto == null) return JsonError("Invalid request.");
            if (dto.WorkOrderId <= 0) return JsonError("Work Order is required.");
            if (string.IsNullOrWhiteSpace(dto.Reason)) return JsonError("Reason is required.");
            if (dto.Items == null || dto.Items.Count == 0)
                return JsonError("At least one item is required.");

            try
            {
                var wo = db.WorkOrders.FirstOrDefault(w => w.Id == dto.WorkOrderId && w.Deleted != true);
                if (wo == null) return JsonError("Work Order not found.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;
                int branchId = wo.BranchId ?? GetCurrentBranchId();

                string code = string.IsNullOrWhiteSpace(dto.EmrCode) ? GenerateCode() : dto.EmrCode.Trim();

                string insertSql = @"
INSERT INTO dbo.ExtraMaterialRequest
       (EmrCode, WorkOrderId, BranchId, RequestDate, Reason, Remarks, Status,
        RequestedBy, Active, Deleted, CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code,   @woId,       @brId,    @now,        @reason, @rmk,   'DRAFT',
        @uid,    1,      0,       @uid,      @now,        @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int newId = db.Database.SqlQuery<int>(insertSql,
                    new SqlParameter("@code", code),
                    new SqlParameter("@woId", dto.WorkOrderId),
                    new SqlParameter("@brId", (object)branchId ?? DBNull.Value),
                    new SqlParameter("@now",  now),
                    new SqlParameter("@reason", dto.Reason.Trim().ToUpper()),
                    new SqlParameter("@rmk",  (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",  userId)
                ).First();

                InsertItems(newId, dto.Items, userId, now);

                return JsonSuccess("Draft created.", new { id = newId, emrCode = code });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to create EMR.", ex);
            }
        }

        // ====================================================================
        // UPDATE (Draft only)
        // ====================================================================
        [HttpPost, Route("update")]
        public ActionResult Update(EmrSaveDto dto)
        {
            if (dto == null || dto.Id <= 0) return JsonError("Invalid request.");
            if (string.IsNullOrWhiteSpace(dto.Reason)) return JsonError("Reason is required.");
            if (dto.Items == null || dto.Items.Count == 0)
                return JsonError("At least one item is required.");

            try
            {
                var hdr = LoadHeader(dto.Id);
                if (hdr == null) return JsonError("Record not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT requests can be edited.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                string updateSql = @"
UPDATE dbo.ExtraMaterialRequest
SET    Reason         = @reason,
       Remarks        = @rmk,
       UpdatedBy      = @uid,
       UpdatedDate    = @now,
       RecordTimeStamp= @now
WHERE  Id = @id";

                db.Database.ExecuteSqlCommand(updateSql,
                    new SqlParameter("@reason", dto.Reason.Trim().ToUpper()),
                    new SqlParameter("@rmk",    (object)(dto.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",    userId),
                    new SqlParameter("@now",    now),
                    new SqlParameter("@id",     dto.Id));

                // Replace items wholesale — simpler than diff
                db.Database.ExecuteSqlCommand(
                    "DELETE FROM dbo.ExtraMaterialRequestItems WHERE EmrId = @id",
                    new SqlParameter("@id", dto.Id));

                InsertItems(dto.Id, dto.Items, userId, now);

                return JsonSuccess("Updated.", new { id = dto.Id });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update EMR.", ex);
            }
        }

        // ====================================================================
        // SUBMIT (Draft → Submitted)
        // ---------------------------------------------------------------
        // Server-side guard rail: each line item is checked against the
        // current Gen_Store balance for the resolved raw item + branch.
        // If ANY line requests more than what's on hand, the submit is
        // rejected and the response lists every short item so the operator
        // can fix the request rather than guess.
        // ====================================================================
        [HttpPost, Route("submit/{id:int}")]
        public ActionResult Submit(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("Record not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT requests can be submitted.");

                var items = LoadItems(id);
                if (items.Count == 0) return JsonError("Request has no items.");

                int branchId = hdr.BranchId ?? GetCurrentBranchId();

                // Aggregate by resolved raw item — two lines hitting the same
                // raw material must share the same on-hand pool.
                var demandByRaw = new Dictionary<int, decimal>();
                var labelByRaw  = new Dictionary<int, string>();
                foreach (var i in items)
                {
                    int rawId = ResolveRawItemId(i);
                    if (rawId <= 0)
                    {
                        return JsonError("Line is not linked to a raw item: " + (i.ItemName ?? "?"));
                    }
                    if (!demandByRaw.ContainsKey(rawId))
                    {
                        demandByRaw[rawId] = 0m;
                        labelByRaw[rawId]  = i.ItemName ?? ("Item #" + rawId);
                    }
                    demandByRaw[rawId] += i.Qty;
                }

                var shortages = new List<object>();
                foreach (var kv in demandByRaw)
                {
                    decimal onHand = GetCurrentStoreBalance(kv.Key, branchId);
                    if (onHand < kv.Value)
                    {
                        shortages.Add(new
                        {
                            rawItemId = kv.Key,
                            itemName  = labelByRaw[kv.Key],
                            requested = Math.Round(kv.Value, 2),
                            onHand    = Math.Round(onHand,   2),
                            shortBy   = Math.Round(kv.Value - onHand, 2)
                        });
                    }
                }

                if (shortages.Count > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Insufficient stock for "
                                + shortages.Count
                                + " item(s). Adjust qty or replenish stock before submitting.",
                        shortages
                    }, JsonRequestBehavior.AllowGet);
                }

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ExtraMaterialRequest
SET    Status='SUBMITTED', SubmittedDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                    new SqlParameter("@now", now),
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@id",  id));

                return JsonSuccess("Submitted for approval.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to submit EMR.", ex);
            }
        }

        // ====================================================================
        // APPROVE (Submitted → Approved + auto-issue to Gen_Store)
        // ====================================================================
        [HttpPost, Route("approve/{id:int}")]
        public ActionResult Approve(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("Record not found.");
                if (!string.Equals(hdr.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only SUBMITTED requests can be approved.");

                var items = LoadItems(id);
                if (items.Count == 0) return JsonError("No items to issue.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;
                int branchId = hdr.BranchId ?? GetCurrentBranchId();
                string issueRef = hdr.EmrCode; // Gen_Store.ReferenceNo links back to the EMR

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // For each line, write a Gen_Store OUT entry.
                        foreach (var i in items)
                        {
                            int rawItemId = ResolveRawItemId(i);
                            if (rawItemId <= 0)
                                throw new InvalidOperationException(
                                    "Item line missing a raw item linkage (line id " + i.Id + ").");

                            decimal opening = GetCurrentStoreBalance(rawItemId, branchId);

                            // Same guard Submit() already performs — re-checked here
                            // because stock can move between Submit and Approve, and
                            // this is the step that actually writes the Gen_Store OUT
                            // row. Without it, approval could drive CurrentQty negative.
                            if (i.Qty > opening)
                            {
                                throw new InvalidOperationException(
                                    "Insufficient stock for " + (i.ItemName ?? ("Item #" + rawItemId))
                                    + ". Available: " + Math.Round(opening, 2)
                                    + ", Requested: " + Math.Round(i.Qty, 2) + ".");
                            }

                            decimal rate    = i.Rate ?? GetRawItemLatestRate(rawItemId, branchId);
                            decimal amount  = i.Qty * rate;
                            string unit     = string.IsNullOrWhiteSpace(i.UnitOfMeasure) ? "" : i.UnitOfMeasure;

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId          = rawItemId,
                                BranchId        = branchId,
                                WarehouseId     = null,
                                TransactionDate = now,

                                TransactionType = "OUT",
                                ReferenceType   = "EMR_ISSUE",
                                ReferenceId     = hdr.WorkOrderId, // ties consumption back to the WO
                                ReferenceNo     = issueRef,

                                OpeningQty   = opening,
                                InQty        = 0m,
                                OutQty       = i.Qty,
                                CurrentQty   = opening - i.Qty,

                                Unit         = unit,
                                Rate         = rate,
                                Amount       = amount,

                                Remarks      = "EMR (" + (hdr.Reason ?? "") + ") "
                                             + (i.Remarks ?? ""),

                                Active       = true,
                                Deleted      = false,
                                CreatedBy    = userId,
                                CreatedDate  = now,
                                UpdatedBy    = userId,
                                UpdatedDate  = now,
                                RecordTimeStamp = now
                            });
                        }

                        db.SaveChanges();

                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ExtraMaterialRequest
SET    Status='APPROVED', ApprovedBy=@uid, ApprovedDate=@now,
       IssueReferenceNo=@ref,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@ref", issueRef),
                            new SqlParameter("@id",  id));

                        tx.Commit();

                        return JsonSuccess("Approved and issued.", new
                        {
                            id,
                            workOrderId = hdr.WorkOrderId,
                            itemCount   = items.Count,
                            issueReferenceNo = issueRef
                        });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return JsonError("Approval/issue failed. " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to approve EMR.", ex);
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
                if (hdr == null) return JsonError("Record not found.");
                if (!string.Equals(hdr.Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only SUBMITTED requests can be rejected.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ExtraMaterialRequest
SET    Status='REJECTED', ApprovedBy=@uid, ApprovedDate=@now,
       RejectionReason=@reason,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id",
                    new SqlParameter("@uid",   userId),
                    new SqlParameter("@now",   now),
                    new SqlParameter("@reason", dto.Reason.Trim()),
                    new SqlParameter("@id",    id));

                return JsonSuccess("Rejected.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to reject EMR.", ex);
            }
        }

        // ====================================================================
        // DELETE (soft) — only Drafts and Rejects
        // ====================================================================
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("Record not found.");
                if (string.Equals(hdr.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Approved requests cannot be deleted — they have already issued stock.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.ExtraMaterialRequest
SET    Deleted=1, Active=0,
       UpdatedBy=@uid, UpdatedDate=@now
WHERE  Id=@id",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));

                return JsonSuccess("Deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete EMR.", ex);
            }
        }

        // ====================================================================
        // DROPDOWNS — convenience endpoints scoped to this module
        // ====================================================================
        [HttpGet, Route("workorders-dropdown")]
        public ActionResult WorkOrdersDropdown()
        {
            try
            {
                int branchId = GetCurrentBranchId();
                var list = db.WorkOrders
                    .Where(w => w.Deleted != true && w.IsPosted == true)
                    .OrderByDescending(w => w.Id)
                    .Select(w => new
                    {
                        id = w.Id,
                        code = w.WorkOrderCode,
                        productName = w.Gen_Products != null ? w.Gen_Products.Prod_Name : "",
                        designCode = w.Gen_Design != null ? w.Gen_Design.D_Code : "",
                        branchId = w.BranchId,
                        display = (w.WorkOrderCode ?? "")
                                + " - "
                                + (w.Gen_Products != null ? w.Gen_Products.Prod_Name : "")
                    })
                    .Take(500)
                    .ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load Work Orders.", ex);
            }
        }

        [HttpGet, Route("raw-items-dropdown")]
        public ActionResult RawItemsDropdown()
        {
            try
            {
                var list = db.Gen_RawItems
                    .Where(r => r.Deleted == false)
                    .OrderBy(r => r.ItemName)
                    .Select(r => new
                    {
                        id = r.Id,
                        name = r.ItemName,
                        type = r.Type,
                        unitId = r.UnitId
                    })
                    .ToList();
                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load raw items.", ex);
            }
        }

        [HttpGet, Route("threads-dropdown")]
        public ActionResult ThreadsDropdown()
        {
            try
            {
                var list = db.Gen_Threads
                    .Where(t => t.Deleted != true)
                    .OrderBy(t => t.Code)
                    .Select(t => new
                    {
                        id = t.Id,
                        code = t.Code,
                        name = t.Name,
                        color = t.Color,
                        unit = t.UnitOfMeasure,
                        rate = t.UnitCost,
                        rawItemId = t.RawItemId
                    })
                    .ToList();
                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load threads.", ex);
            }
        }

        // ====================================================================
        // ROLLUP BY WORK ORDERS
        // ---------------------------------------------------------------
        // Called by the Work Orders screen to overlay an "extras" badge
        // per row. Aggregates Gen_Store EMR_ISSUE entries by ReferenceId
        // (= WorkOrders.Id) into { extraCount, extraValue } per WO.
        // ====================================================================
        public class RollupRequestDto { public List<int> Ids { get; set; } }

        [HttpPost, Route("rollup-by-workorders")]
        public ActionResult RollupByWorkOrders(RollupRequestDto dto)
        {
            try
            {
                var ids = (dto != null && dto.Ids != null)
                    ? dto.Ids.Where(n => n > 0).Distinct().ToList()
                    : new List<int>();

                if (ids.Count == 0) return JsonSuccess("OK", new List<object>());

                var rollups = db.Gen_Store
                    .Where(s => s.ReferenceType == "EMR_ISSUE"
                             && s.Deleted == false
                             && s.ReferenceId.HasValue
                             && ids.Contains(s.ReferenceId.Value))
                    .GroupBy(s => s.ReferenceId.Value)
                    .Select(g => new
                    {
                        workOrderId = g.Key,
                        extraCount  = g.Count(),
                        extraValue  = g.Sum(x => (decimal?)x.Amount) ?? 0m
                    })
                    .ToList();

                return JsonSuccess("OK", rollups);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load EMR rollup.", ex);
            }
        }

        // ====================================================================
        // PROFILE (360° view) — used by the side drawer
        // ====================================================================
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var header = LoadHeader(id);
                if (header == null) return JsonError("Record not found.");

                var items = LoadItems(id);

                // Work Order summary — quantity, status, customer, design, etc.
                var wo = db.WorkOrders
                    .Include("Hr_Customers")
                    .Include("Gen_Products")
                    .Include("Gen_Design")
                    .FirstOrDefault(w => w.Id == header.WorkOrderId);

                var workOrder = wo == null ? null : new
                {
                    id = wo.Id,
                    code = wo.WorkOrderCode,
                    orderType = wo.OrderType,
                    customer = wo.Hr_Customers != null ? wo.Hr_Customers.Customer_Name : "Stock",
                    product = wo.Gen_Products != null ? wo.Gen_Products.Prod_Name : "-",
                    design = wo.Gen_Design != null
                        ? ((wo.Gen_Design.D_Code ?? "") + " - " + (wo.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "-",
                    orderQty = wo.OrderQty ?? 0,
                    orderDate = wo.OrderDate.ToString("yyyy-MM-dd"),
                    status = wo.IsPosted ? "Posted" : "Draft"
                };

                // Roll-up KPIs across line items
                decimal totalValue = items.Sum(i => (i.Rate ?? 0m) * i.Qty);
                int rawCount       = items.Count(i => string.Equals(i.ItemType, "RAW",    StringComparison.OrdinalIgnoreCase));
                int threadCount    = items.Count(i => string.Equals(i.ItemType, "THREAD", StringComparison.OrdinalIgnoreCase));

                // Status timeline
                var timeline = new List<object>
                {
                    new {
                        step = "Created",
                        date = header.CreatedDate,
                        by   = header.RequestedByName,
                        done = true
                    },
                    new {
                        step = "Submitted",
                        date = header.SubmittedDate,
                        by   = header.RequestedByName,
                        done = header.SubmittedDate.HasValue
                    },
                    new {
                        step = string.Equals(header.Status, "REJECTED", StringComparison.OrdinalIgnoreCase) ? "Rejected" : "Approved & Issued",
                        date = header.ApprovedDate,
                        by   = header.ApprovedByName,
                        done = header.ApprovedDate.HasValue
                    }
                };

                // Total qty actually issued (verified from Gen_Store, not just trusting EMR header)
                decimal issuedQty = 0m;
                decimal issuedAmount = 0m;
                if (string.Equals(header.Status, "APPROVED", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(header.IssueReferenceNo))
                {
                    var ledger = db.Gen_Store
                        .Where(s => s.ReferenceType == "EMR_ISSUE"
                                 && s.ReferenceNo   == header.IssueReferenceNo
                                 && s.Deleted == false)
                        .ToList();
                    issuedQty    = ledger.Sum(s => s.OutQty ?? 0m);
                    issuedAmount = ledger.Sum(s => s.Amount ?? 0m);
                }

                var kpis = new
                {
                    itemCount    = items.Count,
                    rawCount,
                    threadCount,
                    totalValue   = Math.Round(totalValue, 2),
                    issuedQty    = Math.Round(issuedQty, 2),
                    issuedAmount = Math.Round(issuedAmount, 2)
                };

                return JsonSuccess("OK", new { header, items, workOrder, kpis, timeline });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load profile.", ex);
            }
        }

        // ====================================================================
        // EXPORT — Excel (EPPlus)
        // ====================================================================
        [HttpGet, Route("export-excel")]
        public ActionResult ExportExcel(
            string status = "", int? workOrderId = null, int? branchId = null,
            string search = "", string showDeleted = "")
        {
            try
            {
                var rows = FetchListRows(status, workOrderId, branchId, search, showDeleted);

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("EMR");

                    var headers = new[] {
                        "EMR Code", "Work Order", "Branch", "Date",
                        "Reason", "Status", "Requested By",
                        "Submitted Date", "Approved By", "Approved Date",
                        "Issue Ref", "Rejection Reason", "Remarks"
                    };
                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }

                    for (int i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i];
                        int row = i + 2;
                        ws.Cells[row,  1].Value = r.EmrCode;
                        ws.Cells[row,  2].Value = r.WorkOrderCode;
                        ws.Cells[row,  3].Value = r.BranchName;
                        ws.Cells[row,  4].Value = r.RequestDate.ToString("yyyy-MM-dd");
                        ws.Cells[row,  5].Value = r.Reason;
                        ws.Cells[row,  6].Value = r.Status;
                        ws.Cells[row,  7].Value = r.RequestedByName;
                        ws.Cells[row,  8].Value = r.SubmittedDate.HasValue ? r.SubmittedDate.Value.ToString("yyyy-MM-dd HH:mm") : "";
                        ws.Cells[row,  9].Value = r.ApprovedByName;
                        ws.Cells[row, 10].Value = r.ApprovedDate.HasValue ? r.ApprovedDate.Value.ToString("yyyy-MM-dd HH:mm") : "";
                        ws.Cells[row, 11].Value = r.IssueReferenceNo;
                        ws.Cells[row, 12].Value = r.RejectionReason;
                        ws.Cells[row, 13].Value = r.Remarks;
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    return File(pkg.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "ExtraMaterialRequests.xlsx");
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to export Excel.", ex);
            }
        }

        // ====================================================================
        // EXPORT — PDF (iTextSharp)
        // ====================================================================
        [HttpGet, Route("export-pdf")]
        public ActionResult ExportPdf(
            string status = "", int? workOrderId = null, int? branchId = null,
            string search = "", string showDeleted = "")
        {
            try
            {
                var rows = FetchListRows(status, workOrderId, branchId, search, showDeleted);

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4.Rotate(), 20f, 20f, 30f, 30f);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.BLACK);
                    var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  BaseColor.WHITE);
                    var bodyFont   = FontFactory.GetFont(FontFactory.HELVETICA,      8.5f, BaseColor.BLACK);

                    doc.Add(new Paragraph("Extra Material Requests", titleFont) { Alignment = Element.ALIGN_CENTER });
                    doc.Add(new Paragraph(" "));

                    var table = new PdfPTable(8) { WidthPercentage = 100f };
                    table.SetWidths(new float[] { 1.2f, 1.2f, 1.2f, 0.9f, 1.1f, 0.9f, 1.2f, 1.4f });

                    void AddHeader(string text)
                    {
                        table.AddCell(new PdfPCell(new Phrase(text, headerFont))
                        {
                            BackgroundColor = new BaseColor(13, 110, 253),
                            HorizontalAlignment = Element.ALIGN_CENTER,
                            Padding = 5f
                        });
                    }

                    AddHeader("EMR Code");
                    AddHeader("Work Order");
                    AddHeader("Branch");
                    AddHeader("Date");
                    AddHeader("Reason");
                    AddHeader("Status");
                    AddHeader("Requested By");
                    AddHeader("Approved By");

                    foreach (var r in rows)
                    {
                        table.AddCell(new PdfPCell(new Phrase(r.EmrCode ?? "-",         bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(r.WorkOrderCode ?? "-",   bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(r.BranchName ?? "-",      bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(r.RequestDate.ToString("yyyy-MM-dd"), bodyFont))
                            { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                        table.AddCell(new PdfPCell(new Phrase(r.Reason ?? "-",          bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(r.Status ?? "-",          bodyFont))
                            { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                        table.AddCell(new PdfPCell(new Phrase(r.RequestedByName ?? "-", bodyFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(r.ApprovedByName ?? "-",  bodyFont)) { Padding = 4f });
                    }

                    doc.Add(table);

                    var footer = new Paragraph(
                        $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | Rows: {rows.Count}",
                        FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY))
                    { Alignment = Element.ALIGN_RIGHT, SpacingBefore = 10f };
                    doc.Add(footer);

                    doc.Close();
                    return File(ms.ToArray(), "application/pdf", "ExtraMaterialRequests.pdf");
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to export PDF.", ex);
            }
        }

        // Shared filter+SQL — used by List, ExportExcel, ExportPdf.
        private List<EmrRow> FetchListRows(
            string status, int? workOrderId, int? branchId, string search, string showDeleted)
        {
            string deletedClause = string.Equals(showDeleted, "yes", StringComparison.OrdinalIgnoreCase)
                ? " (e.Deleted = 1) "
                : " (e.Deleted = 0 OR e.Deleted IS NULL) ";

            var parameters = new List<SqlParameter>();
            string where = " WHERE " + deletedClause;

            if (!string.IsNullOrWhiteSpace(status))
            {
                where += " AND e.Status = @status ";
                parameters.Add(new SqlParameter("@status", status.Trim().ToUpper()));
            }
            if (workOrderId.HasValue && workOrderId.Value > 0)
            {
                where += " AND e.WorkOrderId = @woId ";
                parameters.Add(new SqlParameter("@woId", workOrderId.Value));
            }
            if (branchId.HasValue && branchId.Value > 0)
            {
                where += " AND e.BranchId = @brId ";
                parameters.Add(new SqlParameter("@brId", branchId.Value));
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                where += " AND (e.EmrCode LIKE @s OR wo.WorkOrderCode LIKE @s OR e.Reason LIKE @s) ";
                parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
            }

            string sql = @"
SELECT  e.Id, e.EmrCode, e.WorkOrderId,
        wo.WorkOrderCode    AS WorkOrderCode,
        e.BranchId,
        b.Name              AS BranchName,
        e.RequestDate, e.Reason, e.Remarks, e.Status,
        e.RequestedBy,
        ur.UserName         AS RequestedByName,
        e.SubmittedDate,
        e.ApprovedBy,
        ua.UserName         AS ApprovedByName,
        e.ApprovedDate,
        e.RejectionReason,
        e.IssueReferenceNo,
        e.Active, e.CreatedDate, e.UpdatedDate
FROM    dbo.ExtraMaterialRequest e
LEFT JOIN dbo.WorkOrders   wo ON wo.Id = e.WorkOrderId
LEFT JOIN dbo.Branches     b  ON b.Id  = e.BranchId
LEFT JOIN dbo.Adm_Users    ur ON ur.Id = e.RequestedBy
LEFT JOIN dbo.Adm_Users    ua ON ua.Id = e.ApprovedBy
" + where + " ORDER BY e.Id DESC ";

            return db.Database.SqlQuery<EmrRow>(sql, parameters.ToArray()).ToList();
        }

        // ====================================================================
        // INTERNAL HELPERS
        // ====================================================================

        private string GenerateCode()
        {
            string year = DateTime.Now.Year.ToString();
            string sql = @"
SELECT TOP 1 EmrCode
FROM   dbo.ExtraMaterialRequest
WHERE  EmrCode LIKE 'EMR-' + @year + '-%'
ORDER BY Id DESC";
            string last = db.Database
                .SqlQuery<string>(sql, new SqlParameter("@year", year))
                .FirstOrDefault();

            int next = 1;
            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                int parsed;
                if (parts.Length == 3 && int.TryParse(parts[2], out parsed))
                    next = parsed + 1;
            }
            return "EMR-" + year + "-" + next.ToString("D4");
        }

        private EmrRow LoadHeader(int id)
        {
            string sql = @"
SELECT  e.Id, e.EmrCode, e.WorkOrderId,
        wo.WorkOrderCode    AS WorkOrderCode,
        e.BranchId,
        b.Name              AS BranchName,
        e.RequestDate, e.Reason, e.Remarks, e.Status,
        e.RequestedBy,
        ur.UserName         AS RequestedByName,
        e.SubmittedDate,
        e.ApprovedBy,
        ua.UserName         AS ApprovedByName,
        e.ApprovedDate,
        e.RejectionReason,
        e.IssueReferenceNo,
        e.Active, e.CreatedDate, e.UpdatedDate
FROM    dbo.ExtraMaterialRequest e
LEFT JOIN dbo.WorkOrders   wo ON wo.Id = e.WorkOrderId
LEFT JOIN dbo.Branches     b  ON b.Id  = e.BranchId
LEFT JOIN dbo.Adm_Users    ur ON ur.Id = e.RequestedBy
LEFT JOIN dbo.Adm_Users    ua ON ua.Id = e.ApprovedBy
WHERE   e.Id = @id";
            return db.Database.SqlQuery<EmrRow>(sql, new SqlParameter("@id", id)).FirstOrDefault();
        }

        private List<EmrItemRow> LoadItems(int emrId)
        {
            string sql = @"
SELECT  i.Id, i.EmrId, i.ItemType, i.RawItemId, i.ThreadId,
        COALESCE(t.Name, r.ItemName)            AS ItemName,
        COALESCE(t.Code, CAST(r.Id AS NVARCHAR)) AS ItemCode,
        COALESCE(i.UnitOfMeasure, t.UnitOfMeasure, '') AS UnitOfMeasure,
        i.Qty, i.Rate, i.Amount, i.Remarks
FROM    dbo.ExtraMaterialRequestItems i
LEFT JOIN dbo.Gen_RawItems r ON r.Id = i.RawItemId
LEFT JOIN dbo.Gen_Threads  t ON t.Id = i.ThreadId
WHERE   i.EmrId = @id AND (i.Deleted = 0 OR i.Deleted IS NULL)
ORDER BY i.Id";
            return db.Database.SqlQuery<EmrItemRow>(sql, new SqlParameter("@id", emrId)).ToList();
        }

        private void InsertItems(int emrId, List<EmrItemSaveDto> items, int userId, DateTime now)
        {
            foreach (var i in items)
            {
                if (string.IsNullOrWhiteSpace(i.ItemType)) continue;
                if (i.Qty <= 0) continue;

                string type = i.ItemType.Trim().ToUpper();
                if (type != "RAW" && type != "THREAD") continue;
                if (type == "RAW"    && !(i.RawItemId.HasValue && i.RawItemId.Value > 0)) continue;
                if (type == "THREAD" && !(i.ThreadId.HasValue  && i.ThreadId.Value  > 0)) continue;

                decimal? rate = i.Rate;
                decimal? amount = (rate.HasValue) ? (decimal?)(i.Qty * rate.Value) : null;

                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.ExtraMaterialRequestItems
       (EmrId, ItemType, RawItemId, ThreadId, UnitOfMeasure, Qty, Rate, Amount, Remarks,
        Active, Deleted, CreatedBy, CreatedDate)
VALUES (@emrId, @itype, @raw, @thr, @uom, @qty, @rate, @amt, @rmk,
        1,      0,       @uid,      @now)",
                    new SqlParameter("@emrId", emrId),
                    new SqlParameter("@itype", type),
                    new SqlParameter("@raw",   (object)i.RawItemId ?? DBNull.Value),
                    new SqlParameter("@thr",   (object)i.ThreadId  ?? DBNull.Value),
                    new SqlParameter("@uom",   (object)(i.UnitOfMeasure ?? "") ?? DBNull.Value),
                    new SqlParameter("@qty",   i.Qty),
                    new SqlParameter("@rate",  (object)rate ?? DBNull.Value),
                    new SqlParameter("@amt",   (object)amount ?? DBNull.Value),
                    new SqlParameter("@rmk",   (object)(i.Remarks ?? "") ?? DBNull.Value),
                    new SqlParameter("@uid",   userId),
                    new SqlParameter("@now",   now)
                );
            }
        }

        private int ResolveRawItemId(EmrItemRow item)
        {
            // RAW items reference Gen_RawItems directly.
            // THREAD items consume their backing RawItem via Gen_Threads.RawItemId.
            if (string.Equals(item.ItemType, "RAW", StringComparison.OrdinalIgnoreCase))
                return item.RawItemId ?? 0;

            if (string.Equals(item.ItemType, "THREAD", StringComparison.OrdinalIgnoreCase)
                && item.ThreadId.HasValue)
            {
                var thread = db.Gen_Threads.FirstOrDefault(t => t.Id == item.ThreadId.Value);
                return thread != null ? (thread.RawItemId ?? 0) : 0;
            }

            return 0;
        }

        // Sum InQty - OutQty over non-deleted rows — matches
        // IssueMaterialsController.GetCurrentStoreBalance so both
        // controllers agree on the same Gen_Store ledger balance.
        // (Previously this read the last inserted row's CurrentQty,
        // a different and more fragile algorithm, and filtered with
        // `Deleted == false` instead of the nullable-safe `!= true`.)
        private decimal GetCurrentStoreBalance(int rawItemId, int branchId)
        {
            var q = db.Gen_Store.Where(s => s.ItemId == rawItemId
                                          && s.BranchId == branchId
                                          && s.Deleted != true);

            decimal totalIn = q.Sum(s => (decimal?)s.InQty) ?? 0m;
            decimal totalOut = q.Sum(s => (decimal?)s.OutQty) ?? 0m;
            return totalIn - totalOut;
        }
    }
}
