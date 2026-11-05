using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical QC Inspection status values. Stops a typo
    //  in one branch from silently breaking a filter. Names are the
    //  same strings already stored in the DB.
    // ─────────────────────────────────────────────────────────────────
    public static class QCInspectionStatus
    {
        public const string PENDING                 = "Pending";
        public const string IN_PROGRESS             = "InProgress";
        public const string AWAITING_REWORK         = "AwaitingRework";
        public const string AWAITING_REINSPECTION   = "AwaitingReinspection";
        public const string COMPLETED               = "Completed";
        public const string POSTED                  = "Posted";
        public const string REVERSED                = "Reversed";
        public const string CANCELLED               = "Cancelled";
    }

    [RoutePrefix("emb-products/qc-inspection")]
    public class QC_InspectionController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  WORKFLOW STEPPER
        //  Canonical pipeline rendered in the detail drawer. REVERSED
        //  and CANCELLED sit as red siblings at the end of the chain;
        //  when one is active the main chain greys out.
        // ─────────────────────────────────────────────────────────────
        private static readonly string[] _qcWorkflowStages = new[]
        {
            QCInspectionStatus.PENDING,
            QCInspectionStatus.IN_PROGRESS,
            QCInspectionStatus.AWAITING_REWORK,
            QCInspectionStatus.AWAITING_REINSPECTION,
            QCInspectionStatus.COMPLETED,
            QCInspectionStatus.REVERSED
        };

        private static int ComputeQcWorkflowStageIndex(string status, bool isPosted)
        {
            var s = (status ?? "").Trim();
            if (string.Equals(s, QCInspectionStatus.REVERSED,           StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(s, QCInspectionStatus.CANCELLED,          StringComparison.OrdinalIgnoreCase)) return 5;
            if (isPosted || string.Equals(s, QCInspectionStatus.COMPLETED, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(s, QCInspectionStatus.POSTED,    StringComparison.OrdinalIgnoreCase)) return 4;
            if (string.Equals(s, QCInspectionStatus.AWAITING_REINSPECTION, StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(s, QCInspectionStatus.AWAITING_REWORK,       StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(s, QCInspectionStatus.IN_PROGRESS,           StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table that records every state-changing event:
        //  CREATED, EDITED, PROCESSED, POSTED, REVERSED, CANCELLED,
        //  REINSPECT. Best-effort writes — wrapped in try/catch so a
        //  missing table never blocks the user's main action.
        // ─────────────────────────────────────────────────────────────
        private void LogQcAudit(int qcId, string action,
                                string oldValue = null,
                                string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.QC_InspectionAuditLog
       (QcInspectionId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@qid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@qid",  qcId),
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
                var entries = db.Database.SqlQuery<QcAuditEntry>(@"
SELECT  a.Id,
        a.QcInspectionId AS QcId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.QC_InspectionAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.QcInspectionId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id              = e.Id,
                    qcId            = e.QcId,
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
                return JsonError("Failed to load audit log. " + DescribeQcError(ex));
            }
        }

        private class QcAuditEntry
        {
            public int Id { get; set; }
            public int QcId { get; set; }
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
        //  of generic exceptions. Surface the deepest message.
        // ─────────────────────────────────────────────────────────────
        private static string DescribeQcError(Exception ex)
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
        //  Sprint-4  TOCTOU LOCK
        //  Pessimistic row lock on a single QC_Inspection row, held
        //  until the surrounding transaction commits. Two concurrent
        //  Post or Reverse calls on the same row will serialise.
        // ─────────────────────────────────────────────────────────────
        private void LockQcRow(int qcId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT 1 FROM dbo.QC_Inspection WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                new SqlParameter("@id", qcId));
        }
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            string status = "",
            bool showDeleted = false,
            int page = 1,
            int pageSize = 10,
            string fromDate = null,
            string toDate = null,
            int? workOrderId = null,
            int? productId = null,
            int? branchId = null,
            string posted = null,
            string inspectionType = null,
            string sourceType = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 10;

                // Phase N.0 - parse date range filters defensively
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string postedUp     = (posted ?? "").Trim().ToUpper();
                string inspTypeUp   = (inspectionType ?? "").Trim().ToUpper();
                string sourceTypeUp = (sourceType ?? "").Trim().ToUpper();

                var query = db.QC_Inspection
                    .Include(x => x.WorkOrders)
                    .Include(x => x.Gen_Design)
                    .Include(x => x.Gen_Products)
                    .Include(x => x.WorkOrderItems)
                    .Include(x => x.QC_InspectionItems)
                    .Where(x => showDeleted || (x.Deleted == false || x.Deleted == null));

                if (!showDeleted)
                {
                    query = query.Where(x =>
                        x.Status != "Reversed" &&
                        x.Status != "Cancelled");
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    query = query.Where(x =>
                        (x.QCNo != null && x.QCNo.Contains(search)) ||
                        (x.Status != null && x.Status.Contains(search)) ||
                        (x.Remarks != null && x.Remarks.Contains(search)) ||
                        (x.IssuesFound != null && x.IssuesFound.Contains(search)) ||
                        (x.WorkOrders != null && x.WorkOrders.WorkOrderCode.Contains(search)) ||
                        (x.Gen_Design != null &&
                            (
                                (x.Gen_Design.DNo != null && x.Gen_Design.DNo.Contains(search)) ||
                                (x.Gen_Design.D_Code != null && x.Gen_Design.D_Code.Contains(search))
                            )) ||
                        (x.Gen_Products != null && x.Gen_Products.Prod_Name.Contains(search))
                    );
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    status = status.Trim();
                    query = query.Where(x => x.Status == status);
                }

                // Phase N.0 - new filter dimensions
                if (fromDt.HasValue) query = query.Where(x => x.InspectionDate >= fromDt.Value);
                if (toDt.HasValue)   query = query.Where(x => x.InspectionDate <= toDt.Value);
                if (workOrderId.HasValue && workOrderId.Value > 0) query = query.Where(x => x.WorkOrderId == workOrderId.Value);
                if (productId.HasValue && productId.Value > 0)     query = query.Where(x => x.ProductId == productId.Value);
                if (branchId.HasValue && branchId.Value > 0)       query = query.Where(x => x.BranchId == branchId.Value);
                if (postedUp == "POSTED")        query = query.Where(x => x.IsPosted == true);
                else if (postedUp == "DRAFT")    query = query.Where(x => x.IsPosted != true);
                if (!string.IsNullOrEmpty(inspTypeUp))
                    query = query.Where(x => x.InspectionType != null && x.InspectionType.ToUpper() == inspTypeUp);
                if (!string.IsNullOrEmpty(sourceTypeUp))
                    query = query.Where(x => x.SourceType != null && x.SourceType.ToUpper() == sourceTypeUp);

                // Hide legacy REINSPECTION child rows by default — under the
                // new rework-loop flow the parent QC row carries all rounds
                // itself; old auto-created child rows are now redundant.
                // The user can still find them explicitly by setting
                // inspectionType=REINSPECTION or by toggling Show deleted.
                if (string.IsNullOrEmpty(inspTypeUp) && string.IsNullOrEmpty(sourceTypeUp) && !showDeleted)
                {
                    query = query.Where(x =>
                        x.InspectionType == null ||
                        x.InspectionType.ToUpper() != "REINSPECTION");
                }

                var filteredEntities = query.ToList();

                var allReworkRefs = db.QC_Inspection
                    .Where(x =>
                        (x.Deleted == false || x.Deleted == null) &&
                        x.Status != "Cancelled" &&
                        x.Status != "Reversed" &&
                        x.SourceType == "REWORK")
                    .Select(x => new
                    {
                        x.Id,
                        x.WorkOrderId,
                        x.DesignId,
                        x.ProductId,
                        x.BatchQty
                    })
                    .ToList();

                var summaryRows = filteredEntities.Select(x =>
                {
                    var row = MapRow(x);
                    ApplyReworkButtonState(row, allReworkRefs);
                    return row;
                }).ToList();

                decimal batchSummary = summaryRows.Sum(x => x.BatchQty);
                decimal inspectedSummary = summaryRows.Sum(x => x.InspectedQty);
                decimal passedSummary = summaryRows.Sum(x => x.PassedQty);
                decimal failedSummary = summaryRows.Sum(x => x.FailedQty);

                decimal reworkSummary = summaryRows
                    .Where(x =>
                        x.SourceType != "REWORK" &&
                        x.ReworkQty > 0 &&
                        x.HasReworkReference == false)
                    .Sum(x => x.ReworkQty);

                int totalItems = filteredEntities.Count;

                var entities = filteredEntities
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var rows = entities.Select(x =>
                {
                    var row = MapRow(x);
                    ApplyReworkButtonState(row, allReworkRefs);
                    return row;
                }).ToList();

                // Phase N.0 - beefed up summary aggregates
                int postedCount = filteredEntities.Count(x => x.IsPosted);
                int draftCount  = filteredEntities.Count(x => !x.IsPosted);
                var byStatus = filteredEntities
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "Unknown" : x.Status.Trim())
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToList();
                var byInspectionType = filteredEntities
                    .Where(x => !string.IsNullOrWhiteSpace(x.InspectionType))
                    .GroupBy(x => x.InspectionType.Trim())
                    .Select(g => new { type = g.Key, count = g.Count() })
                    .ToList();
                var bySourceType = filteredEntities
                    .Where(x => !string.IsNullOrWhiteSpace(x.SourceType))
                    .GroupBy(x => x.SourceType.Trim())
                    .Select(g => new { source = g.Key, count = g.Count() })
                    .ToList();

                return JsonSuccess("Loaded successfully.", new
                {
                    items = rows,
                    page,
                    pageSize,
                    totalItems,
                    totalPages = (int)Math.Ceiling((double)totalItems / pageSize),
                    summary = new
                    {
                        count = filteredEntities.Count,
                        batchQty = batchSummary,
                        inspectedQty = inspectedSummary,
                        passedQty = passedSummary,
                        failedQty = failedSummary,
                        reworkQty = reworkSummary,
                        postedCount,
                        draftCount,
                        byStatus,
                        byInspectionType,
                        bySourceType,
                        rows = summaryRows.Count()
                    }
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load QC inspections.", ex);
            }
        }

        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var qc = db.QC_Inspection
                    .Include(x => x.WorkOrders)
                    .Include(x => x.Gen_Design)
                    .Include(x => x.Gen_Products)
                    .Include(x => x.DailyProduction)
                    .Include(x => x.WorkOrderItems)
                    .Include(x => x.QC_InspectionItems)
                    .Include(x => x.QC_InspectionItems.Select(i => i.WorkOrderItems))
                    .FirstOrDefault(x => x.Id == id && (x.Deleted == false || x.Deleted == null));

                if (qc == null)
                    return JsonError("QC record not found.");

                string inspectorName = "";
                if (qc.InspectorId.HasValue)
                {
                    var inspector = db.Adm_Users.FirstOrDefault(x => x.Id == qc.InspectorId.Value);
                    if (inspector != null)
                        inspectorName = inspector.UserName ?? "";
                }

                var items = qc.QC_InspectionItems
                    .Where(i => i.Deleted == false || i.Deleted == null)
                    .OrderBy(i => i.Id)
                    .Select(i => new QCInspectionItemRowViewModel
                    {
                        Id = i.Id,
                        QCInspectionId = i.QCInspectionId,
                        QCInspectionNo = qc.QCNo,

                        WorkOrderItemId = i.WorkOrderItemId,
                        WorkOrderCode = i.WorkOrderItems != null
                            ? (i.WorkOrderItems.WorkOrders != null
                                ? i.WorkOrderItems.WorkOrders.WorkOrderCode
                                : "WO-" + i.WorkOrderItems.WorkOrderId)
                            : "-",

                        ProductionId = i.ProductionId,
                        ProductionNo = i.ProductionId.HasValue ? ("DP-" + i.ProductionId.Value) : "",

                        CompId = i.CompId,
                        ComponentName = i.CompId.HasValue
                            ? (GetComponentName(i.CompId.Value) ?? ("Component #" + i.CompId.Value))
                            : "",

                        Qty = i.Qty,
                        PassedQty = i.PassedQty,
                        FailedQty = i.FailedQty,
                        ReworkQty = i.ReworkQty,
                        Result = i.Result,
                        Remarks = i.Remarks,

                        Active = i.Active ?? false,
                        Deleted = i.Deleted ?? false,

                        CreatedBy = i.CreatedBy,
                        CreatedByEmployeeId = GetEmployeeId(i.CreatedBy)?.ToString(),
                        CreatedDate = i.CreatedDate,
                        CreatedDateText = i.CreatedDate.ToString("dd-MMM-yyyy HH:mm")
                    })
                    .ToList();

                decimal batchQty = qc.BatchQty ?? 0m;
                decimal inspectedQty = qc.InspectedQty ?? 0m;
                decimal passedQty = qc.PassedQty ?? 0m;
                decimal failedQty = qc.FailedQty ?? 0m;
                decimal reworkQty = qc.ReworkQty ?? 0m;

                bool isPosted = qc.IsPosted == true;
                bool isReversed = string.Equals(qc.Status, "Reversed", StringComparison.OrdinalIgnoreCase);

                decimal itemQtyTotal = items.Sum(i => i.Qty);
                decimal itemPassedTotal = items.Sum(i => i.PassedQty);
                decimal itemFailedTotal = items.Sum(i => i.FailedQty);
                decimal itemReworkTotal = items.Sum(i => i.ReworkQty);

                bool hasItems = items.Any();

                bool itemBreakupMatchesHeader =
                    itemPassedTotal == passedQty &&
                    itemFailedTotal == failedQty &&
                    itemReworkTotal == reworkQty;

                bool itemQtyMatchesInspection =
                    itemQtyTotal == inspectedQty ||
                    (itemPassedTotal + itemFailedTotal + itemReworkTotal) == inspectedQty;

                bool itemsCompleted =
                    hasItems &&
                    itemBreakupMatchesHeader &&
                    itemQtyMatchesInspection;

                bool itemsPending = !itemsCompleted;
                bool qtyDistributedCorrectly = inspectedQty == (passedQty + failedQty + reworkQty);
                bool inspectionFullyDone = batchQty > 0m && inspectedQty >= batchQty;

                string statusValue;
                if (isReversed)
                {
                    statusValue = "Reversed";
                }
                else if (isPosted && inspectionFullyDone && itemsCompleted && qtyDistributedCorrectly)
                {
                    statusValue = "Completed";
                }
                else if (isPosted)
                {
                    statusValue = "Posted";
                }
                else if (inspectedQty > 0m)
                {
                    statusValue = "InProgress";
                }
                else
                {
                    statusValue = string.IsNullOrWhiteSpace(qc.Status) ? "Pending" : qc.Status;
                }

                var detail = new QCInspectionDetailViewModel
                {
                    Id = qc.Id,
                    QCNo = qc.QCNo,

                    WorkOrderId = qc.WorkOrderId,
                    DesignId = qc.DesignId,
                    ProductId = qc.ProductId,
                    WorkOrderItemId = qc.WorkOrderItemId,
                    ProductionId = qc.ProductionId,

                    WorkOrderLabel = qc.WorkOrders != null
                        ? qc.WorkOrders.WorkOrderCode
                        : (qc.WorkOrderId.HasValue ? "WO-" + qc.WorkOrderId.Value : "-"),

                    DesignName = qc.Gen_Design != null
                        ? (!string.IsNullOrWhiteSpace(qc.Gen_Design.DNo)
                            ? qc.Gen_Design.DNo
                            : qc.Gen_Design.D_Code)
                        : "-",

                    ProductName = qc.Gen_Products != null
                        ? qc.Gen_Products.Prod_Name
                        : "-",

                    BatchQty = batchQty,
                    InspectedQty = inspectedQty,
                    PassedQty = passedQty,
                    FailedQty = failedQty,
                    ReworkQty = reworkQty,

                    IssuesFound = qc.IssuesFound,
                    Remarks = qc.Remarks,
                    Status = statusValue,

                    InspectorId = qc.InspectorId,
                    InspectorName = inspectorName,

                    InspectionType = qc.InspectionType,
                    SourceType = qc.SourceType,

                    IsPosted = isPosted,
                    IsReversed = isReversed,
                    PostedDate = qc.PostedDate.HasValue
                        ? qc.PostedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : "",

                    InspectionDate = qc.InspectionDate.ToString("dd-MMM-yyyy HH:mm"),
                    CreatedDate = qc.CreatedDate.HasValue
                        ? qc.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : "",
                    UpdatedDate = qc.UpdatedDate.HasValue
                        ? qc.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : "",

                    ItemsPending = itemsPending,
                    ItemsCompleted = itemsCompleted,
                    ItemsStatusText = itemsCompleted ? "Items Complete" : "Items Pending",
                    InspectionProgressText = inspectedQty.ToString("0.##") + " / " + batchQty.ToString("0.##"),

                    CanEdit = !isReversed && !isPosted,
                    CanDelete = !isReversed && !isPosted,
                    CanPost = !isReversed && !isPosted && batchQty > 0m && inspectedQty >= batchQty && qtyDistributedCorrectly,
                    CanReverse = isPosted && !isReversed,
                    CanCompleteItems = isPosted && !isReversed && itemsPending,
                    CanContinueQc = isPosted && !isReversed && inspectedQty < batchQty,
                    CanFinalize = isPosted
                                  && !isReversed
                                  && inspectionFullyDone
                                  && qtyDistributedCorrectly
                                  && itemsCompleted,

                    // Sprint-2: workflow stepper data for the detail drawer.
                    WorkflowStages = _qcWorkflowStages,
                    WorkflowStageIndex = ComputeQcWorkflowStageIndex(qc.Status, isPosted),

                    Items = items
                };

                return JsonSuccess("Loaded successfully.", detail);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load QC detail. " + DescribeQcError(ex));
            }
        }

        [HttpPost]
        [Route("create")]
        public ActionResult Create(QCInspectionInputViewModel dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid payload.");

                    if (!dto.WorkOrderId.HasValue || dto.WorkOrderId.Value <= 0)
                        return JsonError("Work Order is required.");

                    if (!dto.DesignId.HasValue || dto.DesignId.Value <= 0)
                        return JsonError("Design is required.");

                    if (!dto.ProductId.HasValue || dto.ProductId.Value <= 0)
                        return JsonError("Product is required.");

                    if (dto.BatchQty <= 0)
                        return JsonError("Batch quantity must be greater than zero.");

                    if (dto.InspectedQty < 0 || dto.PassedQty < 0 || dto.FailedQty < 0 || dto.ReworkQty < 0)
                        return JsonError("Quantities cannot be negative.");

                    if (dto.InspectedQty > dto.BatchQty)
                        return JsonError("Inspected quantity cannot exceed batch quantity.");

                    if ((dto.PassedQty + dto.FailedQty + dto.ReworkQty) > dto.BatchQty)
                        return JsonError("Passed + Failed + Rework cannot exceed batch quantity.");

                    if (dto.InspectedQty > 0 &&
                        (dto.PassedQty + dto.FailedQty + dto.ReworkQty) != dto.InspectedQty)
                        return JsonError("Passed + Failed + Rework must equal inspected quantity.");

                    bool workOrderExists = db.WorkOrders.Any(x =>
                        x.Id == dto.WorkOrderId.Value &&
                        (x.Deleted == false || x.Deleted == null));
                    if (!workOrderExists)
                        return JsonError("Work Order not found.");

                    bool designExists = db.Gen_Design.Any(x =>
                        x.Id == dto.DesignId.Value &&
                        (x.Deleted == false || x.Deleted == null));
                    if (!designExists)
                        return JsonError("Design not found.");

                    bool productExists = db.Gen_Products.Any(x =>
                        x.Id == dto.ProductId.Value &&
                        (x.Deleted == false || x.Deleted == null));
                    if (!productExists)
                        return JsonError("Product not found.");

                    bool alreadyOpen = db.QC_Inspection.Any(x =>
                        x.WorkOrderId == dto.WorkOrderId &&
                        x.DesignId == dto.DesignId &&
                        x.ProductId == dto.ProductId &&
                        x.IsPosted != true &&
                        (x.Deleted == false || x.Deleted == null) &&
                        x.Status != "Reversed" &&
                        x.Status != "Cancelled");

                    if (alreadyOpen)
                        return JsonError("An open QC batch already exists for this product.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var entity = new QC_Inspection
                    {
                        QCNo = GenerateQcNo(),

                        WorkOrderId = dto.WorkOrderId,
                        DesignId = dto.DesignId,
                        ProductId = dto.ProductId,
                        WorkOrderItemId = dto.WorkOrderItemId,
                        ProductionId = dto.ProductionId,

                        BatchQty = dto.BatchQty,
                        InspectedQty = dto.InspectedQty,
                        PassedQty = dto.PassedQty,
                        FailedQty = dto.FailedQty,
                        ReworkQty = dto.ReworkQty,

                        BranchId = dto.BranchId ?? GetCurrentBranchId(),
                        IssuesFound = string.IsNullOrWhiteSpace(dto.IssuesFound) ? null : dto.IssuesFound.Trim(),
                        Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim(),
                        InspectorId = dto.InspectorId,
                        InspectionDate = dto.InspectionDate ?? now,

                        InspectionType = string.IsNullOrWhiteSpace(dto.InspectionType) ? "FINAL" : dto.InspectionType.Trim(),
                        SourceType = string.IsNullOrWhiteSpace(dto.SourceType) ? "WIP" : dto.SourceType.Trim(),

                        Status = dto.InspectedQty > 0 ? "InProgress" : "Pending",
                        IsPosted = false,
                        PostedBy = null,
                        PostedDate = null,

                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = null,
                        UpdatedDate = null,
                        RecordTimeStamp = now
                    };

                    db.QC_Inspection.Add(entity);
                    db.SaveChanges();

                    tx.Commit();

                    LogQcAudit(entity.Id, "CREATED",
                        oldValue: null,
                        newValue: QCInspectionStatus.PENDING,
                        detail: entity.QCNo + " (batch " + (entity.BatchQty ?? 0m) + ")");

                    return JsonSuccess("QC inspection created successfully.", new
                    {
                        entity.Id,
                        entity.QCNo
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to create QC inspection. " + DescribeQcError(ex));
                }
            }
        }
        
        [HttpPost]
        [Route("update")]
        public ActionResult Update(QCInspectionUpdateViewModel dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid payload.");

                var qc = db.QC_Inspection.FirstOrDefault(x =>
                    x.Id == dto.Id &&
                    (x.Deleted == false || x.Deleted == null));

                if (qc == null)
                    return JsonError("QC record not found.");

                if (qc.IsPosted)
                    return JsonError("Posted QC inspection cannot be updated.");

                decimal batchQty = qc.BatchQty ?? 0m;
                if (batchQty <= 0)
                    return JsonError("QC batch quantity is invalid.");

                if (dto.InspectedQty < 0 || dto.PassedQty < 0 || dto.FailedQty < 0 || dto.ReworkQty < 0)
                    return JsonError("Quantities cannot be negative.");

                if (dto.InspectedQty > batchQty)
                    return JsonError("Inspected quantity cannot exceed batch quantity.");

                if ((dto.PassedQty + dto.FailedQty + dto.ReworkQty) > batchQty)
                    return JsonError("Passed + Failed + Rework cannot exceed batch quantity.");

                if (dto.InspectedQty > 0 &&
                    (dto.PassedQty + dto.FailedQty + dto.ReworkQty) != dto.InspectedQty)
                    return JsonError("Passed + Failed + Rework must equal inspected quantity.");

                qc.InspectedQty = dto.InspectedQty;
                qc.PassedQty = dto.PassedQty;
                qc.FailedQty = dto.FailedQty;
                qc.ReworkQty = dto.ReworkQty;

                qc.IssuesFound = string.IsNullOrWhiteSpace(dto.IssuesFound) ? null : dto.IssuesFound.Trim();
                qc.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

                qc.Status = string.IsNullOrWhiteSpace(dto.Status)
                    ? (dto.InspectedQty > 0 ? "InProgress" : "Pending")
                    : dto.Status.Trim();

                qc.InspectorId = dto.InspectorId;

                if (dto.InspectionDate.HasValue)
                    qc.InspectionDate = dto.InspectionDate.Value;

                qc.InspectionType = string.IsNullOrWhiteSpace(dto.InspectionType)
                    ? qc.InspectionType
                    : dto.InspectionType.Trim();

                qc.SourceType = string.IsNullOrWhiteSpace(dto.SourceType)
                    ? qc.SourceType
                    : dto.SourceType.Trim();

                qc.UpdatedBy = GetCurrentUserId();
                qc.UpdatedDate = DateTime.Now;
                qc.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogQcAudit(qc.Id, "EDITED",
                    oldValue: null,
                    newValue: qc.Status,
                    detail: "Inspected " + qc.InspectedQty + " / Pass " + qc.PassedQty
                          + " / Fail " + qc.FailedQty + " / Rework " + qc.ReworkQty);

                return JsonSuccess("QC inspection updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update QC inspection. " + DescribeQcError(ex));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-1  PROCESS QC — quick-action endpoint
        //  Auto-fills the inspect / pass / fail / rework counts so the
        //  operator doesn't have to walk through Edit. Defaults:
        //     Inspected = BatchQty   (assume the full batch is reviewed)
        //     Passed    = Inspected  (assume all pass unless the user
        //                            overrides via the modal sliders)
        //     Failed    = 0
        //     Rework    = 0
        //  The override DTO lets the modal post the user's adjusted
        //  values in the same call. When `andPost == true` the row is
        //  also pushed through Post() in a single round-trip.
        // ─────────────────────────────────────────────────────────────
        public class QcProcessDto
        {
            public decimal? InspectedQty { get; set; }
            public decimal? PassedQty    { get; set; }
            public decimal? FailedQty    { get; set; }
            public decimal? ReworkQty    { get; set; }
            public string   IssuesFound  { get; set; }
            public string   Remarks      { get; set; }
            public bool     AndPost      { get; set; }
        }

        [HttpPost, Route("process/{id:int}")]
        public ActionResult ProcessQc(int id, QcProcessDto dto = null)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard against concurrent Process /
                    // Edit / Post calls on the same row.
                    LockQcRow(id);

                    var qc = db.QC_Inspection
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);
                    if (qc == null) return JsonError("QC record not found.");

                    if (qc.IsPosted)
                        return JsonError("Posted QC cannot be re-processed. Use Reverse first.");
                    if (string.Equals(qc.Status, QCInspectionStatus.REVERSED, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(qc.Status, QCInspectionStatus.CANCELLED, StringComparison.OrdinalIgnoreCase))
                        return JsonError("Reversed or cancelled QC cannot be processed.");

                    decimal batchQty = qc.BatchQty ?? 0m;
                    if (batchQty <= 0) return JsonError("QC batch quantity is invalid.");

                    // Resolve defaults — auto-fill when the buyer didn't
                    // override (the quick-action button posts an empty
                    // body which means "accept all defaults").
                    decimal inspected = dto?.InspectedQty ?? batchQty;
                    decimal failed    = dto?.FailedQty    ?? 0m;
                    decimal rework    = dto?.ReworkQty    ?? 0m;
                    decimal passed    = dto?.PassedQty    ?? Math.Max(0m, inspected - failed - rework);

                    if (inspected < 0 || passed < 0 || failed < 0 || rework < 0)
                        return JsonError("Quantities cannot be negative.");
                    if (inspected > batchQty)
                        return JsonError("Inspected quantity cannot exceed batch quantity.");
                    if ((passed + failed + rework) > batchQty)
                        return JsonError("Passed + Failed + Rework cannot exceed batch quantity.");
                    if (inspected > 0 && (passed + failed + rework) != inspected)
                        return JsonError("Passed + Failed + Rework must equal inspected quantity.");

                    qc.InspectedQty = inspected;
                    qc.PassedQty    = passed;
                    qc.FailedQty    = failed;
                    qc.ReworkQty    = rework;
                    if (!string.IsNullOrWhiteSpace(dto?.IssuesFound)) qc.IssuesFound = dto.IssuesFound.Trim();
                    if (!string.IsNullOrWhiteSpace(dto?.Remarks))     qc.Remarks     = dto.Remarks.Trim();

                    // Transition: if any rework was logged, the QC waits
                    // for the operator's rework run before posting. Else
                    // we mark it InProgress so Post can pick it up.
                    qc.Status = rework > 0
                                ? QCInspectionStatus.AWAITING_REWORK
                                : QCInspectionStatus.IN_PROGRESS;

                    qc.UpdatedBy = GetCurrentUserId();
                    qc.UpdatedDate = DateTime.Now;
                    qc.RecordTimeStamp = DateTime.Now;
                    db.SaveChanges();

                    LogQcAudit(qc.Id, "PROCESSED",
                        oldValue: QCInspectionStatus.PENDING,
                        newValue: qc.Status,
                        detail: "Inspected " + inspected + " | Pass " + passed
                              + " / Fail " + failed + " / Rework " + rework);

                    tx.Commit();

                    // If the operator asked for a one-shot Process+Post,
                    // chain into Post() now that the QC has the right
                    // counts. Reuse the same transaction-aware endpoint
                    // so the stock posting + DP rework loop still fire.
                    if (dto != null && dto.AndPost && rework == 0)
                    {
                        var postRes = Post(qc.Id) as JsonResult;
                        return postRes ?? JsonSuccess("QC processed.", new { id = qc.Id, status = qc.Status });
                    }

                    return JsonSuccess("QC processed.", new
                    {
                        id = qc.Id,
                        status = qc.Status,
                        inspectedQty = inspected,
                        passedQty = passed,
                        failedQty = failed,
                        reworkQty = rework
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to process QC. " + DescribeQcError(ex));
                }
            }
        }

        // ========================= Phase N.1 - QC 360 PROFILE =========================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                // 1. Header
                var head = (
                    from q in db.QC_Inspection
                    join w in db.WorkOrders on q.WorkOrderId equals w.Id into w1
                    from w in w1.DefaultIfEmpty()
                    join d in db.Gen_Design on q.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()
                    join p in db.Gen_Products on q.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()
                    join b in db.Branches on q.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()
                    join u in db.Adm_Users on q.InspectorId equals u.Id into u1
                    from u in u1.DefaultIfEmpty()
                    where q.Id == id
                    select new
                    {
                        q.Id,
                        q.QCNo,
                        q.InspectionDate,
                        q.InspectionType,
                        q.SourceType,
                        q.Status,
                        q.IsPosted,
                        q.PostedDate,
                        q.Active,
                        q.Deleted,
                        q.BatchQty,
                        q.InspectedQty,
                        q.PassedQty,
                        q.FailedQty,
                        q.ReworkQty,
                        q.WorkOrderId,
                        WorkOrderCode = w != null ? w.WorkOrderCode : "",
                        q.WorkOrderItemId,
                        q.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",
                        q.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",
                        q.BranchId,
                        BranchName = b != null ? b.Name : "",
                        InspectorEmployeeId = u != null ? (int?)u.EmployeeId : null,
                        q.IssuesFound,
                        q.Remarks,
                        q.CreatedDate,
                        q.UpdatedDate,
                        q.ProductionId
                    }
                ).FirstOrDefault();

                if (head == null) return JsonError("QC inspection not found.");

                var header = new
                {
                    id = head.Id,
                    qcNo = head.QCNo,
                    inspectionDateText = head.InspectionDate.ToString("dd-MMM-yyyy"),
                    inspectionType = head.InspectionType,
                    sourceType = head.SourceType,
                    status = head.Status,
                    isPosted = head.IsPosted,
                    postedDateText = head.PostedDate.HasValue ? head.PostedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    active = head.Active ?? true,
                    deleted = head.Deleted ?? false,
                    batchQty = head.BatchQty ?? 0m,
                    inspectedQty = head.InspectedQty ?? 0m,
                    passedQty = head.PassedQty ?? 0m,
                    failedQty = head.FailedQty ?? 0m,
                    reworkQty = head.ReworkQty ?? 0m,
                    workOrderId = head.WorkOrderId,
                    workOrderCode = head.WorkOrderCode,
                    workOrderItemId = head.WorkOrderItemId,
                    designId = head.DesignId,
                    designCode = head.DesignCode,
                    designName = head.DesignName,
                    productId = head.ProductId,
                    productCode = head.ProductCode,
                    productName = head.ProductName,
                    branchId = head.BranchId,
                    branchName = head.BranchName,
                    inspectorEmployeeCode = head.InspectorEmployeeId.HasValue ? ("EMP-" + head.InspectorEmployeeId.Value) : "-",
                    issuesFound = head.IssuesFound,
                    remarks = head.Remarks,
                    createdDate = head.CreatedDate.HasValue ? head.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDate = head.UpdatedDate.HasValue ? head.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    productionId = head.ProductionId
                };

                // 2. Items
                var items = (
                    from x in db.QC_InspectionItems
                    join wi in db.WorkOrderItems on x.WorkOrderItemId equals wi.Id into wi1
                    from wi in wi1.DefaultIfEmpty()
                    join cm in db.Gen_Components on x.CompId equals cm.Id into cm1
                    from cm in cm1.DefaultIfEmpty()
                    where x.QCInspectionId == id && (x.Deleted == false || x.Deleted == null)
                    select new
                    {
                        x.Id,
                        x.WorkOrderItemId,
                        x.CompId,
                        ComponentName = cm != null ? cm.Name : "",
                        ComponentCode = cm != null ? (cm.Code == null ? "" : cm.Code.ToString()) : "",
                        x.Qty,
                        x.PassedQty,
                        x.FailedQty,
                        x.ReworkQty,
                        x.Result,
                        x.Remarks,
                        x.CreatedDate
                    }
                ).ToList()
                .Select(x => new
                {
                    id = x.Id,
                    workOrderItemId = x.WorkOrderItemId,
                    compId = x.CompId,
                    componentName = x.ComponentName,
                    componentCode = x.ComponentCode,
                    qty = x.Qty,
                    passedQty = x.PassedQty,
                    failedQty = x.FailedQty,
                    reworkQty = x.ReworkQty,
                    result = x.Result,
                    remarks = x.Remarks,
                    createdDateText = x.CreatedDate.ToString("dd-MMM-yyyy")
                })
                .ToList();

                // 3. Posting trail (Gen_Stocks rows from this QC)
                var stockTrail = db.Gen_Stocks
                    .Where(s => s.ReferenceType == "QC_INSPECTION"
                             && s.ReferenceId == id
                             && s.Deleted != true)
                    .OrderByDescending(s => s.CreatedDate).ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.Id,
                        s.CreatedDate,
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

                var stockTrailOut = stockTrail.Select(s => new
                {
                    id = s.Id,
                    dateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy") : "-",
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

                decimal stockInQty  = stockTrail.Sum(s => s.InQty ?? 0m);
                decimal stockOutQty = stockTrail.Sum(s => s.OutQty ?? 0m);
                decimal stockAmount = stockTrail.Sum(s => s.Amount ?? 0m);

                // 4. KPIs
                decimal passPct = (head.InspectedQty ?? 0m) > 0
                    ? Math.Round(((head.PassedQty ?? 0m) / head.InspectedQty.Value) * 100m, 1)
                    : 0m;
                decimal failPct = (head.InspectedQty ?? 0m) > 0
                    ? Math.Round(((head.FailedQty ?? 0m) / head.InspectedQty.Value) * 100m, 1)
                    : 0m;

                var kpis = new
                {
                    batchQty = head.BatchQty ?? 0m,
                    inspectedQty = head.InspectedQty ?? 0m,
                    passedQty = head.PassedQty ?? 0m,
                    failedQty = head.FailedQty ?? 0m,
                    reworkQty = head.ReworkQty ?? 0m,
                    passPct,
                    failPct,
                    defectCount = items.Count,
                    stockInQty,
                    stockOutQty,
                    stockAmount = Math.Round(stockAmount, 2)
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

        [HttpPost]
        [Route("post/{id:int}")]
        public ActionResult Post(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard. Two operators clicking Post
                    // on the same QC would each pass the IsPosted check
                    // and both write Gen_Stocks IN rows. The row-level
                    // UPDLOCK serialises them.
                    LockQcRow(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var qc = db.QC_Inspection
                        .Include(x => x.QC_InspectionItems)
                        .FirstOrDefault(x =>
                            x.Id == id &&
                            x.Deleted != true);

                    if (qc == null)
                        return JsonError("QC record not found.");

                    if (qc.IsPosted == true)
                        return JsonError("QC already posted.");

                    if (string.Equals(qc.Status, QCInspectionStatus.REVERSED, StringComparison.OrdinalIgnoreCase))
                        return JsonError("Reversed QC cannot be posted.");

                    int productId = qc.ProductId ?? 0;
                    int branchId = qc.BranchId ?? GetCurrentBranchId();

                    if (productId <= 0)
                        return JsonError("Product is missing on QC.");

                    if (branchId <= 0)
                        return JsonError("Branch is missing on QC.");

                    decimal batchQty = qc.BatchQty ?? 0m;
                    decimal inspectedQty = qc.InspectedQty ?? 0m;
                    decimal passedQty = qc.PassedQty ?? 0m;
                    decimal failedQty = qc.FailedQty ?? 0m;
                    decimal reworkQty = qc.ReworkQty ?? 0m;

                    if (batchQty <= 0)
                        return JsonError("Invalid batch quantity.");

                    if (inspectedQty <= 0)
                        return JsonError("Invalid inspected quantity.");

                    if (inspectedQty > batchQty)
                        return JsonError("Inspected quantity cannot exceed batch quantity.");

                    if (inspectedQty < batchQty)
                        return JsonError("Inspection is not complete yet.");

                    if ((passedQty + failedQty + reworkQty) != inspectedQty)
                        return JsonError("Passed + Failed + Rework must equal inspected quantity.");

                    var items = qc.QC_InspectionItems
                        .Where(i => i.Deleted == false || i.Deleted == null)
                        .ToList();

                    // Product-level QC: child rows are only reference rows.
                    // They are auto-synced from parent QC header.
                    foreach (var item in items)
                    {
                        item.Qty = inspectedQty;
                        item.PassedQty = passedQty;
                        item.FailedQty = failedQty;
                        item.ReworkQty = reworkQty;

                        item.Result = failedQty > 0 || reworkQty > 0
                            ? "Hold"
                            : "Pass";

                        item.Remarks = string.IsNullOrWhiteSpace(item.Remarks)
                            ? "Auto-synced from product-level QC."
                            : item.Remarks;

                        item.Active = true;
                        item.Deleted = false;
                        item.UpdatedBy = userId;
                        item.UpdatedDate = now;
                    }

                    qc.IsPosted = true;
                    qc.PostedBy = userId;
                    qc.PostedDate = now;
                    qc.UpdatedBy = userId;
                    qc.UpdatedDate = now;
                    qc.RecordTimeStamp = now;

                    // Rework loop gate: when this round produced any rework,
                    // hold the row as AwaitingRework so it (a) shows up in
                    // Daily Production as a rework task and (b) blocks
                    // re-inspection until the operator actually re-does the
                    // pieces. Only when rework == 0 does the row close.
                    int currentRound = GetCurrentInspectionRound(qc);
                    if (reworkQty > 0m)
                    {
                        qc.Status = "AwaitingRework";
                        qc.Remarks = AppendRoundLog(qc.Remarks, currentRound, passedQty, failedQty, reworkQty);
                    }
                    else
                    {
                        qc.Status = "Completed";
                        qc.Remarks = AppendRoundLog(qc.Remarks, currentRound, passedQty, failedQty, reworkQty);
                    }

                    PostStockFromQc(qc, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    LogQcAudit(qc.Id, "POSTED",
                        oldValue: QCInspectionStatus.IN_PROGRESS,
                        newValue: qc.Status,
                        detail: "Inspected " + (qc.InspectedQty ?? 0m) + " | Pass " + (qc.PassedQty ?? 0m)
                              + " / Fail " + (qc.FailedQty ?? 0m) + " / Rework " + (qc.ReworkQty ?? 0m));

                    string message = reworkQty > 0m
                        ? "QC posted. " + reworkQty + " pcs awaiting rework in Daily Production."
                        : "QC posted and finalized successfully.";

                    return JsonSuccess(message);
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to post QC. " + DescribeQcError(ex));
                }
            }
        }

        [HttpPost]
        [Route("reverse/{id:int}")]
        public ActionResult Reverse(int id, QcReverseInputViewModel dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: same TOCTOU guard as Post.
                    LockQcRow(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var qc = db.QC_Inspection
                        .Include(x => x.QC_InspectionItems)
                        .FirstOrDefault(x =>
                            x.Id == id &&
                            x.Deleted != true);

                    if (qc == null)
                        return JsonError("QC record not found.");

                    if (qc.IsPosted != true)
                        return JsonError("Only posted QC can be reversed.");

                    if (string.Equals(qc.Status, QCInspectionStatus.REVERSED, StringComparison.OrdinalIgnoreCase))
                        return JsonError("QC is already reversed.");

                    if (string.IsNullOrWhiteSpace(dto?.Reason))
                        return JsonError("Reverse reason is required.");

                    string reason = dto.Reason.Trim();

                    ReverseStockFromQc(qc, userId, now, reason);

                    ResetProductionsForQc(qc, userId, now, reason);

                    qc.IsPosted = false;
                    qc.Status = "Reversed";
                    qc.UpdatedBy = userId;
                    qc.UpdatedDate = now;
                    qc.RecordTimeStamp = now;

                    qc.Remarks = string.IsNullOrWhiteSpace(qc.Remarks)
                        ? "Reversed: " + reason
                        : qc.Remarks + " | Reversed: " + reason;

                    foreach (var item in qc.QC_InspectionItems.Where(i => i.Deleted != true))
                    {
                        item.Result = "Reversed";
                        item.Remarks = string.IsNullOrWhiteSpace(item.Remarks)
                            ? "Reversed: " + reason
                            : item.Remarks + " | Reversed: " + reason;

                        item.UpdatedBy = userId;
                        item.UpdatedDate = now;
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogQcAudit(qc.Id, "REVERSED",
                        oldValue: QCInspectionStatus.POSTED,
                        newValue: QCInspectionStatus.REVERSED,
                        detail: string.IsNullOrWhiteSpace(dto?.Reason) ? null : ("Reason: " + dto.Reason));

                    return JsonSuccess("QC reversed successfully and returned to Daily Production.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to reverse QC. " + DescribeQcError(ex));
                }
            }
        }

        [HttpPost]
        [Route("cancel-pending/{id:int}")]
        public ActionResult CancelPendingQc(int id, QcCancelInputViewModel dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var qc = db.QC_Inspection
                        .Include(x => x.QC_InspectionItems)
                        .FirstOrDefault(x =>
                            x.Id == id &&
                            (x.Deleted == false || x.Deleted == null));

                    if (qc == null)
                        return JsonError("QC record not found.");

                    if (qc.IsPosted == true)
                        return JsonError("Posted QC cannot be cancelled. Reverse QC first.");

                    string reason = dto != null && !string.IsNullOrWhiteSpace(dto.Reason)
                        ? dto.Reason.Trim()
                        : "Pending QC cancelled.";

                    foreach (var item in qc.QC_InspectionItems.ToList())
                    {
                        item.Deleted = true;
                        item.Active = false;
                        item.UpdatedBy = userId;
                        item.UpdatedDate = now;
                    }

                    qc.Deleted = true;
                    qc.Active = false;
                    qc.Status = "Cancelled";
                    qc.Remarks = string.IsNullOrWhiteSpace(qc.Remarks)
                        ? reason
                        : (qc.Remarks + " | " + reason);
                    qc.UpdatedBy = userId;
                    qc.UpdatedDate = now;
                    qc.RecordTimeStamp = now;

                    ResetProductionsForQc(qc, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    LogQcAudit(id, "CANCELLED",
                        oldValue: QCInspectionStatus.PENDING,
                        newValue: QCInspectionStatus.CANCELLED,
                        detail: string.IsNullOrWhiteSpace(dto?.Reason) ? null : ("Reason: " + dto.Reason));

                    return JsonSuccess("Pending QC cancelled successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        message = "Failed to cancel pending QC.",
                        detail = DescribeQcError(ex)
                    }, JsonRequestBehavior.AllowGet);
                }
            }
        }

        [HttpPost]
        [Route("finalize/{id:int}")]
        public ActionResult FinalizeQc(int id, QcFinalizeInputViewModel dto)
        {
            return JsonError("Separate finalize is disabled. Complete QC items, then use Post QC.");
        }

        [HttpPost]
        [Route("continue/{id:int}")]
        public ActionResult ContinueQc(int id, QcContinueInputViewModel dto)
        {
            try
            {
                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                var qc = db.QC_Inspection.FirstOrDefault(x =>
                    x.Id == id &&
                    (x.Deleted == false || x.Deleted == null));

                if (qc == null)
                    return JsonError("QC record not found.");

                if (qc.IsPosted == true)
                    return JsonError("Posted QC cannot be continued. Reverse it first if changes are required.");

                if (string.Equals(qc.Status, "Reversed", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Reversed QC cannot be continued.");

                if (dto == null)
                    return JsonError("Invalid payload.");

                decimal batchQty = qc.BatchQty ?? 0m;

                if (batchQty <= 0)
                    return JsonError("Invalid batch quantity.");

                if (dto.InspectedQty < 0 || dto.PassedQty < 0 || dto.FailedQty < 0 || dto.ReworkQty < 0)
                    return JsonError("Quantities cannot be negative.");

                if (dto.InspectedQty > batchQty)
                    return JsonError("Inspected quantity cannot exceed batch quantity.");

                if ((dto.PassedQty + dto.FailedQty + dto.ReworkQty) != dto.InspectedQty)
                    return JsonError("Passed + Failed + Rework must equal inspected quantity.");

                qc.InspectedQty = dto.InspectedQty;
                qc.PassedQty = dto.PassedQty;
                qc.FailedQty = dto.FailedQty;
                qc.ReworkQty = dto.ReworkQty;

                qc.IssuesFound = string.IsNullOrWhiteSpace(dto.IssuesFound) ? null : dto.IssuesFound.Trim();
                qc.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? qc.Remarks : dto.Remarks.Trim();

                if (dto.InspectionDate.HasValue)
                    qc.InspectionDate = dto.InspectionDate.Value;

                qc.Status = dto.InspectedQty > 0 ? "InProgress" : "Pending";

                qc.UpdatedBy = userId;
                qc.UpdatedDate = now;
                qc.RecordTimeStamp = now;

                db.SaveChanges();

                return JsonSuccess("QC updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to continue QC.", ex);
            }
        }

        // ================================================================
        // REINSPECT — Open the SAME QC row for the next inspection round.
        // Only allowed once the operator has actually re-done the pieces
        // (DailyProduction posts a rework entry → Status = AwaitingReinspection).
        // After this call the row reverts to InProgress, the inspector
        // enters this round's pass/fail/rework into the existing row, and
        // Post is invoked again. No new QC row is created.
        // ================================================================
        [HttpPost]
        [Route("reinspect/{id:int}")]
        public ActionResult Reinspect(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var qc = db.QC_Inspection.FirstOrDefault(x =>
                        x.Id == id &&
                        (x.Deleted == false || x.Deleted == null));

                    if (qc == null)
                        return JsonError("QC record not found.");

                    if (string.Equals(qc.Status, "Reversed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(qc.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                        return JsonError("Reversed or cancelled QC cannot be reinspected.");

                    if (!string.Equals(qc.Status, "AwaitingReinspection", StringComparison.OrdinalIgnoreCase))
                        return JsonError("This QC is not awaiting re-inspection. The rework entry must first be posted from Daily Production.");

                    int nextRound = GetCurrentInspectionRound(qc);

                    // Re-open the row for editing. The inspector will adjust
                    // pass/fail/rework cumulative totals and call Post again.
                    // Stock posting is delta-aware (PostStockFromQc), so a
                    // bump from 450→495 passed only pushes 45 new pcs to stock.
                    qc.IsPosted = false;
                    qc.PostedBy = null;
                    qc.PostedDate = null;

                    qc.Status = "InProgress";
                    qc.UpdatedBy = userId;
                    qc.UpdatedDate = now;
                    qc.RecordTimeStamp = now;

                    qc.Remarks = string.IsNullOrWhiteSpace(qc.Remarks)
                        ? ("Re-inspection round " + nextRound + " opened.")
                        : (qc.Remarks + " | Round " + nextRound + " opened.");

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("QC re-opened for inspection round " + nextRound + ".", new
                    {
                        id = qc.Id,
                        qcNo = qc.QCNo,
                        round = nextRound,
                        status = qc.Status
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to open re-inspection.", ex);
                }
            }
        }

        private QcInspectionRowViewModel MapRow(QC_Inspection x)
        {
            var items = x.QC_InspectionItems?
                .Where(i => i.Deleted == false || i.Deleted == null)
                .ToList() ?? new List<QC_InspectionItems>();

            decimal batchQty = x.BatchQty ?? 0m;
            decimal inspectedQty = x.InspectedQty ?? 0m;
            decimal passedQty = x.PassedQty ?? 0m;
            decimal failedQty = x.FailedQty ?? 0m;
            decimal reworkQty = x.ReworkQty ?? 0m;

            bool isPosted = x.IsPosted == true;
            bool isReversed = string.Equals(x.Status, "Reversed", StringComparison.OrdinalIgnoreCase);

            bool hasItems = items.Any();

            decimal itemQtyTotal = items.Sum(i => i.Qty);
            decimal itemPassedTotal = items.Sum(i => i.PassedQty);
            decimal itemFailedTotal = items.Sum(i => i.FailedQty);
            decimal itemReworkTotal = items.Sum(i => i.ReworkQty);

            bool itemBreakupMatchesHeader =
                itemPassedTotal == passedQty &&
                itemFailedTotal == failedQty &&
                itemReworkTotal == reworkQty;

            bool itemQtyMatchesInspection =
                itemQtyTotal == inspectedQty ||
                (itemPassedTotal + itemFailedTotal + itemReworkTotal) == inspectedQty;

            bool itemsCompleted =
                hasItems &&
                itemBreakupMatchesHeader &&
                itemQtyMatchesInspection;

            bool itemsPending = !itemsCompleted;

            if (isReversed)
            {
                itemsCompleted = false;
                itemsPending = false;
            }

            bool qtyDistributedCorrectly = inspectedQty == (passedQty + failedQty + reworkQty);
            bool inspectionFullyDone = batchQty > 0m && inspectedQty >= batchQty;

            bool hasReinspection = false;

            if (isPosted && reworkQty > 0m)
            {
                hasReinspection = db.QC_Inspection.Any(q =>
                    q.Id != x.Id &&
                    q.WorkOrderId == x.WorkOrderId &&
                    q.DesignId == x.DesignId &&
                    q.ProductId == x.ProductId &&
                    q.SourceType == "REWORK" &&
                    (q.Deleted == false || q.Deleted == null) &&
                    q.Status != "Cancelled" &&
                    q.Status != "Reversed" &&
                    (q.BatchQty ?? 0m) == reworkQty
                );
            }

            string status;
            // Rework-loop states are authoritative — surface them verbatim
            // so the row badge, the Reinspect gate, and the summary chips
            // all agree on the row's workflow position.
            if (string.Equals(x.Status, "AwaitingRework", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Status, "AwaitingReinspection", StringComparison.OrdinalIgnoreCase))
            {
                status = x.Status;
            }
            else if (isReversed)
            {
                status = "Reversed";
            }
            else if (isPosted && inspectionFullyDone && qtyDistributedCorrectly && itemsCompleted)
            {
                status = "Completed";
            }
            else if (isPosted)
            {
                status = "Posted";
            }
            else if (inspectedQty > 0m)
            {
                status = "InProgress";
            }
            else
            {
                status = string.IsNullOrWhiteSpace(x.Status) ? "Pending" : x.Status;
            }

            return new QcInspectionRowViewModel
            {
                Id = x.Id,
                QCNo = x.QCNo ?? "",

                WorkOrderId = x.WorkOrderId,
                DesignId = x.DesignId,
                ProductId = x.ProductId,
                WorkOrderItemId = x.WorkOrderItemId,

                // IMPORTANT: use direct parent navigation, not WorkOrderItems
                WorkOrderLabel = x.WorkOrders != null
                    ? (x.WorkOrders.WorkOrderCode ?? "")
                    : "",

                ProductName = x.Gen_Products != null
                    ? (x.Gen_Products.Prod_Name ?? "")
                    : "",

                DesignName = x.Gen_Design != null
                    ? (!string.IsNullOrWhiteSpace(x.Gen_Design.DNo)
                        ? x.Gen_Design.DNo
                        : (x.Gen_Design.D_Code ?? ""))
                    : "",

                BatchQty = batchQty,
                InspectedQty = inspectedQty,
                PassedQty = passedQty,
                FailedQty = failedQty,
                ReworkQty = reworkQty,

                Status = status,

                IsPosted = isPosted,
                IsReversed = isReversed,

                ItemsPending = itemsPending,
                ItemsCompleted = itemsCompleted,
                ItemsStatusText = isReversed
                    ? "Reversed"
                    : (itemsCompleted ? "Items Complete" : "Items Pending"),

                InspectionProgressText = inspectedQty.ToString("0.##") + " / " + batchQty.ToString("0.##"),

                CanEdit = !isReversed && !isPosted,
                CanDelete = !isReversed && !isPosted,
                CanPost = !isReversed && !isPosted && batchQty > 0m && inspectedQty >= batchQty && qtyDistributedCorrectly,
                CanReverse = isPosted && !isReversed,
                CanCompleteItems = isPosted && !isReversed && itemsPending,
                CanContinueQc = isPosted && !isReversed && inspectedQty < batchQty,
                CanFinalize = isPosted
                              && !isReversed
                              && inspectionFullyDone
                              && qtyDistributedCorrectly
                              && itemsCompleted,
                CanReinspect = isPosted
                               && !isReversed
                               && reworkQty > 0m
                               && inspectionFullyDone
                               && !hasReinspection,

                InspectionType = x.InspectionType,
                SourceType = x.SourceType,

                InspectionDate = x.InspectionDate.ToString("yyyy-MM-ddTHH:mm"),
                CreatedDate = x.CreatedDate.HasValue
                    ? x.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm")
                    : null,

                InspectionDateText = x.InspectionDate.ToString("dd-MMM-yyyy hh:mm tt"),
                CreatedDateText = x.CreatedDate.HasValue
                    ? x.CreatedDate.Value.ToString("dd-MMM-yyyy hh:mm tt")
                    : "",

                // Sprint-1: Process QC quick action gate. Available when
                // the QC is still in Pending status (never touched) and
                // hasn't been reversed/cancelled. After Process the
                // status flips to InProgress / AwaitingRework and the
                // button hides itself.
                CanProcess = !isPosted
                             && !isReversed
                             && batchQty > 0m
                             && (string.IsNullOrWhiteSpace(x.Status)
                                 || string.Equals(x.Status, QCInspectionStatus.PENDING, StringComparison.OrdinalIgnoreCase)),

                IsRecent = x.CreatedDate.HasValue
                           && (DateTime.Now - x.CreatedDate.Value).TotalHours < 24
            };
        }

        private void ResetProductionsForQc(QC_Inspection qc, int userId, DateTime now, string reason = null)
        {
            if (qc == null || !qc.WorkOrderId.HasValue || !qc.DesignId.HasValue || !qc.ProductId.HasValue)
                return;

            var productions = db.DailyProduction
                .Include(x => x.WorkOrderItems)
                .Where(x =>
                    (x.Deleted == false || x.Deleted == null) &&
                    x.WorkOrderItems != null &&
                    x.WorkOrderItems.WorkOrderId == qc.WorkOrderId.Value &&
                    x.WorkOrderItems.DesignId == qc.DesignId.Value &&
                    x.WorkOrderItems.ProductId == qc.ProductId.Value)
                .ToList();

            var affectedItemIds = new List<int>();

            foreach (var p in productions)
            {
                // ❶-REV (QC path) Phase S.1 — only deduct cost if this row was previously
                // posted and not already reversed (otherwise the cost was never accrued
                // or has already been deducted via DailyProduction.Reverse).
                if (p.IsPosted == true && p.IsReversed != true)
                {
                    DeductActualCostFromWorkOrderItem(p, p.AcceptedQty ?? 0);
                }

                // Important: mark old production as reversed/history
                p.IsSentToQC = false;
                p.IsReversed = true;
                p.Status = "QC Reversed";

                if (p.WorkOrderItemId.HasValue)
                    affectedItemIds.Add(p.WorkOrderItemId.Value);

                p.UpdatedBy = userId;
                p.UpdatedDate = now;
                p.RecordTimeStamp = now;

                p.Remarks = string.IsNullOrWhiteSpace(p.Remarks)
                    ? "QC reversed: " + (qc.QCNo ?? ("QC-" + qc.Id))
                    : p.Remarks + " | QC reversed: " + (qc.QCNo ?? ("QC-" + qc.Id));

                if (!string.IsNullOrWhiteSpace(reason))
                    p.Remarks += " | Reason: " + reason;
            }

            foreach (var itemId in affectedItemIds.Distinct())
            {
                UpdateWorkOrderItemProgress(itemId);
            }
        }

        // ❶-REV (QC path) Phase S.1 — mirror of DailyProductionsController helper.
        // Subtracts the cost this production row added to the parent WorkOrderItem.
        private void DeductActualCostFromWorkOrderItem(DailyProduction dp, int acceptedQty)
        {
            if (!dp.WorkOrderItemId.HasValue) return;
            var item = db.WorkOrderItems
                .FirstOrDefault(x => x.Id == dp.WorkOrderItemId.Value
                                  && (x.Deleted == false || x.Deleted == null));
            if (item == null) return;
            if (!dp.MachineId.HasValue) return;

            var profile = db.MachineCostProfile
                .Where(c => c.MachineId == dp.MachineId.Value
                         && c.Deleted != true
                         && (c.Active ?? true) == true)
                .OrderByDescending(c => c.Id)
                .FirstOrDefault();
            if (profile == null) return;

            decimal runMin = (dp.StartTime.HasValue && dp.EndTime.HasValue && dp.EndTime.Value > dp.StartTime.Value)
                ? (decimal)(dp.EndTime.Value - dp.StartTime.Value).TotalMinutes
                : 0m;
            decimal stopMin = db.DailyProductionStops
                .Where(s => s.ProductionId == dp.Id)
                .ToList()
                .Sum(s => s.StopEnd.HasValue && s.StopEnd.Value > s.StopStart
                    ? (decimal)(s.StopEnd.Value - s.StopStart).TotalMinutes
                    : 0m);
            decimal productiveH = Math.Max(0m, runMin - stopMin) / 60m;

            decimal mh = profile.HourlyRate ?? 0m;
            decimal oh = profile.OperatorHourlyRate ?? 0m;
            decimal eh = (profile.PowerKW ?? 0m) * (profile.ElectricityUnitRate ?? 0m);

            decimal subMachine = Math.Round(mh * productiveH, 2);
            decimal subOperator = Math.Round(oh * productiveH, 2);
            decimal subElectric = Math.Round(eh * productiveH, 2);
            decimal subTotal = subMachine + subOperator + subElectric;

            item.MachineCost = Math.Max(0m, (item.MachineCost ?? 0m) - subMachine);
            item.LaborCost = Math.Max(0m, (item.LaborCost ?? 0m) - subOperator);
            item.ElectricityCost = Math.Max(0m, (item.ElectricityCost ?? 0m) - subElectric);
            item.TotalCost = Math.Max(0m, (item.TotalCost ?? 0m) - subTotal);

            int completed = item.CompletedQty - acceptedQty;
            item.CostPerUnit = completed > 0
                ? Math.Round((item.TotalCost ?? 0m) / completed, 4)
                : 0m;
        }
       
        private void PostStockFromQc(QC_Inspection qc, int userId, DateTime now)
        {
            int productId = qc.ProductId ?? 0;
            int branchId = qc.BranchId ?? GetCurrentBranchId();

            if (productId <= 0)
                throw new Exception("Product is missing for QC stock posting.");

            if (branchId <= 0)
                throw new Exception("Branch is missing for QC stock posting.");

            decimal passedQtyCumulative = qc.PassedQty ?? 0m;

            // Rework rounds: PassedQty is cumulative across all rounds. The
            // first Post pushed N1 pieces; this Post should only push the
            // delta (N2 - N1). If the inspector revised pass→fail in this
            // round (delta < 0), we pull stock back via an OUT row.
            decimal alreadyPosted = db.Gen_Stocks
                .Where(s =>
                    s.ReferenceType == "QC_INSPECTION" &&
                    s.ReferenceId == qc.Id &&
                    s.StockStage == "FINISHED" &&
                    (s.Deleted == false || s.Deleted == null) &&
                    (s.TransactionType == "QC-POST-FINISHED" ||
                     s.TransactionType == "QC-POST-FINISHED-DELTA"))
                .Sum(s => (decimal?)(s.InQty - s.OutQty)) ?? 0m;

            decimal delta = passedQtyCumulative - alreadyPosted;

            if (delta == 0m)
                return;

            if (delta > 0m)
            {
                AddStock(
                    qc,
                    productId,
                    branchId,
                    delta,
                    delta,
                    0,
                    "FINISHED",
                    alreadyPosted > 0m ? "QC-POST-FINISHED-DELTA" : "QC-POST-FINISHED",
                    alreadyPosted > 0m
                        ? "QC re-inspection delta: +" + delta + " passed"
                        : "QC passed quantity posted to finished stock",
                    userId,
                    now
                );
            }
            else
            {
                decimal pullBack = -delta;
                AddStock(
                    qc,
                    productId,
                    branchId,
                    pullBack,
                    0,
                    pullBack,
                    "FINISHED",
                    "QC-POST-FINISHED-DELTA",
                    "QC re-inspection delta: -" + pullBack + " (downgraded)",
                    userId,
                    now
                );
            }
        }

        private void ReverseStockFromQc(QC_Inspection qc, int userId, DateTime now, string reason)
        {
            int productId = qc.ProductId ?? 0;
            int branchId = qc.BranchId ?? GetCurrentBranchId();

            if (productId <= 0)
                throw new Exception("Product is missing for QC reverse.");

            if (branchId <= 0)
                throw new Exception("Branch is missing for QC reverse.");

            // Reverse the net amount actually pushed by all post rounds
            // (initial + any delta rounds). Using the cumulative net IN-OUT
            // here keeps reversal symmetric with PostStockFromQc.
            decimal netPosted = db.Gen_Stocks
                .Where(s =>
                    s.ReferenceType == "QC_INSPECTION" &&
                    s.ReferenceId == qc.Id &&
                    s.StockStage == "FINISHED" &&
                    (s.Deleted == false || s.Deleted == null) &&
                    (s.TransactionType == "QC-POST-FINISHED" ||
                     s.TransactionType == "QC-POST-FINISHED-DELTA"))
                .Sum(s => (decimal?)(s.InQty - s.OutQty)) ?? 0m;

            if (netPosted <= 0)
                return;

            bool alreadyReversed = db.Gen_Stocks.Any(s =>
                s.ReferenceType == "QC_INSPECTION" &&
                s.ReferenceId == qc.Id &&
                s.TransactionType == "QC-REV-FINISHED" &&
                s.StockStage == "FINISHED" &&
                (s.Deleted == false || s.Deleted == null));

            if (alreadyReversed)
                throw new Exception("QC finished stock already reversed.");

            AddStock(
                qc,
                productId,
                branchId,
                netPosted,
                0,
                netPosted,
                "FINISHED",
                "QC-REV-FINISHED",
                "QC finished stock reversed",
                userId,
                now,
                reason
            );
        }

        private void AddStock(QC_Inspection qc, int productId, int branchId, decimal qty, decimal inQty,
        decimal outQty, string stage, string trxType, string description, int userId, DateTime now,
        string remarks = null)
        {
            // ❸ Phase S.1 — prefer ACTUAL production cost over DesignCosting estimate.
            // Resolution order:
            //   1) WorkOrderItem.CostPerUnit (running actuals accrued in PostToWip)
            //   2) WorkOrderItem.TotalCost / CompletedQty (fallback if CostPerUnit unset)
            //   3) DesignCosting estimate via GetFinishedProductUnitCost (legacy fallback)
            decimal? actualUnitCost = null;
            string costSource = "estimate";

            if (qc.WorkOrderItemId.HasValue)
            {
                var woItem = db.WorkOrderItems
                    .FirstOrDefault(x => x.Id == qc.WorkOrderItemId.Value
                                      && (x.Deleted == false || x.Deleted == null));
                if (woItem != null)
                {
                    if (woItem.CostPerUnit.HasValue && woItem.CostPerUnit.Value > 0m)
                    {
                        actualUnitCost = woItem.CostPerUnit.Value;
                        costSource = "actual (WO item cost/unit)";
                    }
                    else if (woItem.TotalCost.HasValue && woItem.TotalCost.Value > 0m && woItem.CompletedQty > 0)
                    {
                        actualUnitCost = Math.Round(woItem.TotalCost.Value / woItem.CompletedQty, 4);
                        costSource = "actual (WO total/completed)";
                    }
                }
            }

            decimal unitCost = actualUnitCost ?? GetFinishedProductUnitCost(productId, qc.DesignId ?? 0);
            decimal amount = (inQty > 0 ? inQty : outQty) * unitCost;
            string costTag = " [cost: " + costSource + " · Rs " + unitCost.ToString("0.0000") + "]";

            db.Gen_Stocks.Add(new Gen_Stocks
            {
                ProdId = productId,
                BranchId = branchId,
                ItemId = productId,
                ItemType = "PRODUCT",

                StockStage = stage,

                Qty = qty,
                InQty = inQty,
                OutQty = outQty,
                Unit = "PCS",

                Rate = unitCost,
                LastKnownRate = unitCost,
                Amount = amount,

                TransactionType = trxType,
                ReferenceType = "QC_INSPECTION",
                ReferenceId = qc.Id,
                ReferenceNo = qc.QCNo,

                StockDescription = description,
                Remarks = (remarks ?? description) + costTag,

                CreatedBy = userId,
                CreatedDate = now,
                UpdatedBy = userId,
                UpdatedDate = now,
                RecordTimeStamp = now,

                Active = true,
                Deleted = false
            });
        }

        private string GetComponentName(int compId)
        {
            return db.Gen_Components
                .Where(c => c.Id == compId)
                .Select(c => c.Name)
                .FirstOrDefault();
        }

        private decimal GetFinishedProductUnitCost(int productId, int designId)
        {
            if (designId <= 0)
                return 0m;

            var cost = db.DesignCosting
                .Where(x =>
                    x.DesignId == designId &&
                    x.Deleted != true &&
                    x.Active == true)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            if (cost == null)
                return 0m;

            decimal totalCost = cost.TotalCost ?? 0m;
            decimal batchQty = cost.BatchQty;

            return batchQty > 0 ? totalCost / batchQty : 0m;
        }

        private void UpdateWorkOrderItemProgress(int workOrderItemId)
        {
            var item = db.WorkOrderItems.FirstOrDefault(x =>
                x.Id == workOrderItemId &&
                (x.Deleted == false || x.Deleted == null));

            if (item == null)
                return;

            var productions = db.DailyProduction
                .Where(x =>
                    x.WorkOrderItemId == workOrderItemId &&
                    (x.Deleted == false || x.Deleted == null) &&
                    (x.IsReversed == false || x.IsReversed == null))
                .ToList();

            int completedQty = productions.Sum(x => (int?)(x.AcceptedQty ?? 0)) ?? 0;
            int rejectedQty = productions.Sum(x => (int?)(x.RejectedQty ?? 0)) ?? 0;

            item.CompletedQty = completedQty;
            item.RejectedQty = rejectedQty;

            int plannedQty = item.Quantity;

            if (completedQty <= 0)
                item.Status = "Pending";
            else if (completedQty < plannedQty)
                item.Status = "In Production";
            else
                item.Status = "Production Complete";

            item.UpdatedBy = GetCurrentUserId();
            item.UpdatedDate = DateTime.Now;
            item.RecordTimeStamp = DateTime.Now;
        }

        private string GenerateQcNo()
        {
            var year = DateTime.Now.Year;
            var prefix = "QC-" + year + "-";

            var last = db.QC_Inspection
                .Where(x => x.QCNo != null && x.QCNo.StartsWith(prefix))
                .OrderByDescending(x => x.Id)
                .Select(x => x.QCNo)
                .FirstOrDefault();

            int next = 1;

            if (!string.IsNullOrWhiteSpace(last))
            {
                var parts = last.Split('-');
                int parsed;
                if (parts.Length == 3 && int.TryParse(parts[2], out parsed))
                    next = parsed + 1;
            }

            return prefix + next.ToString("0000");
        }

        // ================================================================
        // Rework round helpers — InspectionRound is derived from the
        // count of "[Round N: ...]" log entries persisted in Remarks.
        // This avoids a schema change while still giving the UI a round
        // count and a full audit trail.
        // ================================================================
        private int GetCurrentInspectionRound(QC_Inspection qc)
        {
            if (qc == null || string.IsNullOrWhiteSpace(qc.Remarks)) return 1;

            int count = 0;
            int idx = 0;
            const string marker = "[Round ";
            while ((idx = qc.Remarks.IndexOf(marker, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += marker.Length;
            }

            return count + 1;
        }

        private string AppendRoundLog(string remarks, int round, decimal passed, decimal failed, decimal rework)
        {
            string entry = string.Format("[Round {0}: pass={1}, fail={2}, rework={3}]",
                round, passed, failed, rework);

            return string.IsNullOrWhiteSpace(remarks) ? entry : (remarks + " | " + entry);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }

        private void ApplyReworkButtonState(QcInspectionRowViewModel row,IEnumerable<dynamic> allReworkRefs)
        {
            if (row == null)
                return;

            bool hasReworkReference = false;

            foreach (var q in allReworkRefs)
            {
                bool match =
                    q.Id != row.Id &&
                    q.WorkOrderId == row.WorkOrderId &&
                    q.DesignId == row.DesignId &&
                    q.ProductId == row.ProductId &&
                    (q.BatchQty ?? 0m) == row.ReworkQty;

                if (match)
                {
                    hasReworkReference = true;
                    break;
                }
            }

            row.HasReworkReference = hasReworkReference;

            // After the rework-loop refactor the gate is: the row's status
            // must be AwaitingReinspection — meaning the operator has
            // already posted the rework run from Daily Production.
            row.CanReinspectRework =
                row.IsReversed != true &&
                string.Equals(row.Status, "AwaitingReinspection", StringComparison.OrdinalIgnoreCase);
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        //  Each endpoint accepts { ids: int[] } and returns the standard
        //  envelope: { successCount, failureCount, failures }. Per-row
        //  failures don't break the batch — they're reported back so
        //  the toolbar can render "3 posted, 1 failed (reason: …)".
        // ─────────────────────────────────────────────────────────────
        public class QcBulkIdsDto { public List<int> Ids { get; set; } }
        public class QcBulkReverseDto
        {
            public List<int> Ids { get; set; }
            public string Reason { get; set; }
        }
        public class QcBulkProcessDto
        {
            public List<int> Ids { get; set; }
            public bool AndPost { get; set; }
        }

        private class QcBulkFailure
        {
            public int id;
            public string code;
            public string reason;
        }

        private object DoBulkQc(List<int> ids, Func<int, QC_Inspection, string> runOne)
        {
            var failures = new List<QcBulkFailure>();
            int ok = 0;

            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var qc = db.QC_Inspection.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (qc == null)
                {
                    failures.Add(new QcBulkFailure { id = id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = qc.QCNo ?? ("QC#" + id);
                try
                {
                    string err = runOne(id, qc);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new QcBulkFailure { id = id, code = code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new QcBulkFailure { id = id, code = code, reason = DescribeQcError(ex) });
                }
            }

            return new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures = failures.Select(f => new { id = f.id, code = f.code, reason = f.reason })
            };
        }

        [HttpPost, Route("bulk-process")]
        public ActionResult BulkProcess(QcBulkProcessDto dto)
        {
            var data = DoBulkQc(dto?.Ids, (id, qc) =>
            {
                var res = ProcessQc(id, new QcProcessDto { AndPost = dto?.AndPost ?? false }) as JsonResult;
                if (res?.Data == null) return "Process failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk process completed.", data);
        }

        [HttpPost, Route("bulk-post")]
        public ActionResult BulkPost(QcBulkIdsDto dto)
        {
            var data = DoBulkQc(dto?.Ids, (id, qc) =>
            {
                var res = Post(id) as JsonResult;
                if (res?.Data == null) return "Post failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk post completed.", data);
        }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(QcBulkReverseDto dto)
        {
            var data = DoBulkQc(dto?.Ids, (id, qc) =>
            {
                var res = Reverse(id, new QcReverseInputViewModel { Reason = dto?.Reason }) as JsonResult;
                if (res?.Data == null) return "Reverse failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk reverse completed.", data);
        }

        [HttpPost, Route("bulk-cancel")]
        public ActionResult BulkCancel(QcBulkReverseDto dto)
        {
            var data = DoBulkQc(dto?.Ids, (id, qc) =>
            {
                var res = CancelPendingQc(id, new QcCancelInputViewModel { Reason = dto?.Reason }) as JsonResult;
                if (res?.Data == null) return "Cancel failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk cancel completed.", data);
        }
    }
}