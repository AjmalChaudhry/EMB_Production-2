using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using System.Data.Entity;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text.pdf;
using iTextSharp.text;
using Paragraph = iTextSharp.text.Paragraph;
using PageSize = iTextSharp.text.PageSize;
using Document = iTextSharp.text.Document;


namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical Daily Production status values. Names are
    //  identical to the strings already stored in the DB so this is a
    //  drop-in refactor; the constants stop a typo in one branch from
    //  silently breaking a filter.
    // ─────────────────────────────────────────────────────────────────
    public static class DailyProductionStatus
    {
        public const string DRAFT          = "Draft";
        public const string POSTED_TO_WIP  = "PostedToWIP";
        public const string SENT_TO_QC     = "SentToQC";
        public const string REWORK         = "Rework";
        public const string REWORK_POSTED  = "ReworkPosted";
        public const string REVERSED       = "Reversed";
        public const string COMPLETED      = "Completed";
    }

    [RoutePrefix("emb-products/daily-production")]
    public class DailyProductionController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  WORKFLOW STEPPER
        //  Canonical pipeline rendered as horizontal dots in the detail
        //  drawer header. REVERSED is rendered as a red sibling at the
        //  POSTED slot — when reversed the POSTED dot greys out and the
        //  red sibling lights up.
        // ─────────────────────────────────────────────────────────────
        private static readonly string[] _dpWorkflowStages = new[]
        {
            DailyProductionStatus.DRAFT,
            DailyProductionStatus.POSTED_TO_WIP,
            DailyProductionStatus.SENT_TO_QC,
            DailyProductionStatus.COMPLETED,
            DailyProductionStatus.REVERSED
        };

        private static int ComputeDpWorkflowStageIndex(
            bool isPosted, bool isReversed, bool isSentToQc, bool qcCompleted)
        {
            if (isReversed)   return 4;
            if (qcCompleted)  return 3;
            if (isSentToQc)   return 2;
            if (isPosted)     return 1;
            return 0;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table that records every state-changing event on
        //  a DailyProduction row: CREATED, EDITED, POSTED, SENT_TO_QC,
        //  REVERSED, DELETED. Best-effort — wrapped in try/catch so a
        //  missing table never blocks the user's main action.
        // ─────────────────────────────────────────────────────────────
        private void LogDpAudit(int dpId, string action,
                                string oldValue = null,
                                string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.DailyProductionAuditLog
       (DailyProductionId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@pid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@pid",  dpId),
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
                var entries = db.Database.SqlQuery<DpAuditEntry>(@"
SELECT  a.Id,
        a.DailyProductionId AS DpId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.DailyProductionAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.DailyProductionId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id              = e.Id,
                    dpId            = e.DpId,
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
                return JsonError("Failed to load audit log. " + DescribeDpError(ex));
            }
        }

        private class DpAuditEntry
        {
            public int Id { get; set; }
            public int DpId { get; set; }
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
        private static string DescribeDpError(Exception ex)
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
        //  Pessimistic row lock on a single DailyProduction row, held
        //  until the surrounding transaction commits. Two concurrent
        //  Post or SendToQc calls on the same row will serialise — the
        //  second caller blocks until the first commits, then sees the
        //  flag already flipped and exits cleanly.
        // ─────────────────────────────────────────────────────────────
        private void LockDpRow(int dpId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT 1 FROM dbo.DailyProduction WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                new SqlParameter("@id", dpId));
        }
        // =========================================================
        // LIST
        // =========================================================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string fromDate = null,
            string toDate = null,
            int? workOrderIdFilter = null,
            int? machineIdFilter = null,
            int? operatorIdFilter = null,
            string shift = null,
            string posted = null,
            string sentToQc = null,
            string status = null)
        {
            try
            {
                // Phase Q.0 - parse filters
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string postedUp   = (posted ?? "").Trim().ToUpper();
                string sentQcUp   = (sentToQc ?? "").Trim().ToUpper();
                string statusUp   = (status ?? "").Trim().ToUpper();
                string shiftUp    = (shift ?? "").Trim().ToUpper();

                var prodQ = db.DailyProduction
                    .Include("WorkOrderItems.WorkOrders")
                    .Include("WorkOrderItems.Gen_Design")
                    .Include("WorkOrderItems.Gen_Products")
                    .Include("Gen_Machines")
                    .Where(p => p.Deleted != true);

                if (fromDt.HasValue) prodQ = prodQ.Where(p => p.StartTime >= fromDt.Value);
                if (toDt.HasValue)   prodQ = prodQ.Where(p => p.StartTime <= toDt.Value);
                if (workOrderIdFilter.HasValue && workOrderIdFilter.Value > 0)
                    prodQ = prodQ.Where(p => p.WorkOrderItems != null && p.WorkOrderItems.WorkOrderId == workOrderIdFilter.Value);
                if (machineIdFilter.HasValue && machineIdFilter.Value > 0)
                    prodQ = prodQ.Where(p => p.MachineId == machineIdFilter.Value);
                if (operatorIdFilter.HasValue && operatorIdFilter.Value > 0)
                    prodQ = prodQ.Where(p => p.OperatorId == operatorIdFilter.Value);
                if (!string.IsNullOrEmpty(shiftUp))
                    prodQ = prodQ.Where(p => p.Shift != null && p.Shift.ToUpper() == shiftUp);
                if (postedUp == "POSTED")     prodQ = prodQ.Where(p => p.IsPosted == true);
                else if (postedUp == "DRAFT") prodQ = prodQ.Where(p => p.IsPosted != true);
                else if (postedUp == "REVERSED") prodQ = prodQ.Where(p => p.IsReversed == true);
                if (sentQcUp == "YES")    prodQ = prodQ.Where(p => p.IsSentToQC == true);
                else if (sentQcUp == "NO") prodQ = prodQ.Where(p => p.IsSentToQC != true);
                if (!string.IsNullOrEmpty(statusUp))
                    prodQ = prodQ.Where(p => p.Status != null && p.Status.ToUpper() == statusUp);

                var productionList = prodQ
                    .OrderByDescending(p => p.Id)
                    .ToList();

                // Active QC only (ignore reversed/cancelled history)
                var qcLookup = db.QC_Inspection
                    .Where(q => q.Deleted != true &&
                                q.Status != "Reversed" &&
                                q.Status != "Cancelled")
                    .Select(q => new
                    {
                        q.Id,
                        q.ProductionId,
                        q.WorkOrderId,
                        q.DesignId,
                        q.ProductId,
                        q.IsPosted,
                        q.Status
                    })
                    .ToList();

                var operatorIds = productionList
                    .Where(p => p.OperatorId.HasValue)
                    .Select(p => p.OperatorId.Value)
                    .Distinct()
                    .ToList();

                var operatorLookup = db.Adm_Users
                    .Where(u => operatorIds.Contains(u.Id))
                    .Select(u => new
                    {
                        u.Id,
                        u.EmployeeId,
                        u.UserName
                    })
                    .ToList()
                    .ToDictionary(u => u.Id, u => u);

                var rows = productionList.Select(p =>
                {
                    int workOrderId = p.WorkOrderItems != null ? p.WorkOrderItems.WorkOrderId : 0;
                    int? designId = p.WorkOrderItems != null ? p.WorkOrderItems.DesignId : null;
                    int? productId = p.WorkOrderItems != null ? p.WorkOrderItems.ProductId : null;

                    // -------------------------------------------------------
                    // IMPORTANT:
                    // Prefer exact QC match by ProductionId
                    // Fallback to WO/Design/Product only if production is still sent to QC
                    // -------------------------------------------------------
                    var qcInfo = qcLookup.FirstOrDefault(q =>
                        q.ProductionId.HasValue &&
                        q.ProductionId.Value == p.Id);

                    if (qcInfo == null && p.IsSentToQC == true)
                    {
                        qcInfo = qcLookup.FirstOrDefault(q =>
                            q.WorkOrderId == workOrderId &&
                            q.DesignId == designId &&
                            q.ProductId == productId);
                    }

                    // If production has been reopened after QC reverse,
                    // ignore old QC history completely
                    if (p.IsSentToQC != true)
                    {
                        qcInfo = null;
                    }

                    var operatorUser = p.OperatorId.HasValue && operatorLookup.ContainsKey(p.OperatorId.Value)
                        ? operatorLookup[p.OperatorId.Value]
                        : null;

                    string operatorCode = "-";
                    string operatorName = "-";
                    string operatorLabel = "-";

                    if (operatorUser != null)
                    {
                        operatorCode = operatorUser.EmployeeId.HasValue
                            ? ("EMP-" + operatorUser.EmployeeId.Value)
                            : "-";

                        operatorName = !string.IsNullOrWhiteSpace(operatorUser.UserName)
                            ? operatorUser.UserName
                            : "-";

                        operatorLabel = operatorCode != "-"
                            ? (operatorCode + " - " + operatorName)
                            : operatorName;
                    }

                    bool isPosted = p.IsPosted == true;
                    bool isReversed = p.IsReversed == true;
                    bool isSentToQc = p.IsSentToQC == true;

                    // Rework rows aren't linked to a QC via ProductionId — they
                    // carry the parent QC id in Remarks as "[QC:N]". Resolve it
                    // here so the row's status / Reverse gate matches what the
                    // parent QC says, not just what the DP row remembers.
                    int? sourceQcId = ExtractQcIdFromRemarks(p.Remarks);
                    bool isReworkRow = sourceQcId.HasValue;

                    bool reworkParentCompleted = false;
                    if (isReworkRow)
                    {
                        var parentQc = qcLookup.FirstOrDefault(q => q.Id == sourceQcId.Value);
                        reworkParentCompleted = parentQc != null
                            && parentQc.IsPosted
                            && string.Equals(parentQc.Status, "Completed", StringComparison.OrdinalIgnoreCase);
                    }

                    // When the rework's parent QC has been fully closed,
                    // the rework run is effectively done — render the row
                    // the same as a normal SentToQC row that QC has posted.
                    string displayStatus = reworkParentCompleted ? "SentToQC" : p.Status;
                    bool effectiveSentToQc = isSentToQc || reworkParentCompleted;
                    bool effectiveHasQc = (qcInfo != null) || reworkParentCompleted;
                    bool effectiveQcCompleted = (qcInfo != null && qcInfo.IsPosted == true) || reworkParentCompleted;

                    // Sprint-1: derived flags used by the frontend chips.
                    //   sourceQcId / isReworkRow are already extracted
                    //   above; reuse them so we don't double-call the
                    //   helper. Just compute the recency badge here.
                    bool isRecent = p.CreatedDate.HasValue
                                    && (DateTime.Now - p.CreatedDate.Value).TotalHours < 24;

                    return new DailyProductionRowViewModel
                    {
                        Id = p.Id,

                        WorkOrderItemId = p.WorkOrderItemId,
                        WorkOrderId = p.WorkOrderItems != null
                                      ? p.WorkOrderItems.WorkOrderId
                                      : (int?)null,
                        WorkOrderLabel = p.WorkOrderItems != null
                            ? (
                                p.WorkOrderItems.WorkOrders != null &&
                                !string.IsNullOrWhiteSpace(p.WorkOrderItems.WorkOrders.WorkOrderCode)
                                    ? p.WorkOrderItems.WorkOrders.WorkOrderCode
                                    : ("WO-" + p.WorkOrderItems.WorkOrderId)
                              )
                            : "-",

                        ProductId = productId,
                        ProductName = p.WorkOrderItems != null && p.WorkOrderItems.Gen_Products != null
                            ? p.WorkOrderItems.Gen_Products.Prod_Name
                            : "-",

                        DesignId = designId,
                        DesignName = p.WorkOrderItems != null && p.WorkOrderItems.Gen_Design != null
                            ? p.WorkOrderItems.Gen_Design.DNo
                            : "-",

                        MachineId = p.MachineId,
                        MachineName = p.Gen_Machines != null
                            ? p.Gen_Machines.Serial_Number
                            : "-",

                        OperatorId = p.OperatorId,
                        OperatorCode = operatorCode,
                        OperatorName = operatorName,
                        OperatorLabel = operatorLabel,

                        Shift = p.Shift,

                        StartTime = p.StartTime.HasValue
                            ? p.StartTime.Value.ToString("dd-MMM-yyyy HH:mm")
                            : null,
                        EndTime = p.EndTime.HasValue
                            ? p.EndTime.Value.ToString("dd-MMM-yyyy HH:mm")
                            : null,

                        Stitches = p.Stitches,
                        PlannedQty = p.PlannedQty,
                        ActualQty = p.ActualQty,
                        RejectedQty = p.RejectedQty,
                        AcceptedQty = p.AcceptedQty,

                        Status = displayStatus,

                        IsPosted = isPosted,
                        IsReversed = isReversed,
                        IsSentToQC = effectiveSentToQc,

                        HasQc = effectiveHasQc,
                        QcCompleted = effectiveQcCompleted,

                        // Sprint-1: rework + recency flags drive UI chips.
                        IsRework = isReworkRow,
                        SourceQcInspectionId = sourceQcId,
                        IsRecent = isRecent,

                        // -------------------------------------------------------
                        // Reopened production after QC reverse should become usable again.
                        // A rework row whose parent QC is closed locks the same way as a
                        // normal QC-posted row: no Reverse, no Edit, no Delete.
                        // -------------------------------------------------------
                        CanEdit = !(isPosted || isReversed || isSentToQc || qcInfo != null || reworkParentCompleted),
                        CanDelete = !(isPosted || isReversed || isSentToQc || reworkParentCompleted),
                        CanPost = !(isPosted || isReversed),
                        CanReverse = (isPosted && !isReversed && qcInfo == null && !reworkParentCompleted)
                    };
                }).ToList();

                // Phase S.1 — actual machine cost from MachineCostProfile
                var machineIdsInPage = productionList
                    .Where(p => p.MachineId.HasValue)
                    .Select(p => p.MachineId.Value)
                    .Distinct()
                    .ToList();

                var costProfileMap = db.MachineCostProfile
                    .Where(c => machineIdsInPage.Contains(c.MachineId)
                             && c.Deleted != true
                             && (c.Active ?? true) == true)
                    .OrderByDescending(c => c.Id)
                    .ToList()
                    .GroupBy(c => c.MachineId)
                    .ToDictionary(g => g.Key, g => g.First());

                var prodIdsInPage = productionList.Select(p => p.Id).ToList();
                var stopMap = db.DailyProductionStops
                    .Where(s => prodIdsInPage.Contains(s.ProductionId))
                    .ToList()
                    .GroupBy(s => s.ProductionId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(s =>
                            s.StopEnd.HasValue && s.StopEnd.Value > s.StopStart
                                ? (decimal)(s.StopEnd.Value - s.StopStart).TotalMinutes
                                : 0m));

                decimal sumProductionCost = 0m;
                decimal sumProductiveHours = 0m;
                decimal sumStopHours = 0m;
                int rowsWithCostProfile = 0;
                int rowsMissingCostProfile = 0;

                foreach (var p in productionList)
                {
                    if (!p.MachineId.HasValue) continue;

                    decimal runMin = (p.StartTime.HasValue && p.EndTime.HasValue && p.EndTime.Value > p.StartTime.Value)
                        ? (decimal)(p.EndTime.Value - p.StartTime.Value).TotalMinutes
                        : 0m;
                    decimal stopMin = stopMap.ContainsKey(p.Id) ? stopMap[p.Id] : 0m;
                    decimal productiveMin = Math.Max(0m, runMin - stopMin);
                    decimal productiveH = productiveMin / 60m;

                    sumProductiveHours += productiveH;
                    sumStopHours += stopMin / 60m;

                    if (costProfileMap.ContainsKey(p.MachineId.Value))
                    {
                        rowsWithCostProfile++;
                        var profile = costProfileMap[p.MachineId.Value];
                        decimal mh = profile.HourlyRate ?? 0m;
                        decimal oh = profile.OperatorHourlyRate ?? 0m;
                        decimal eh = (profile.PowerKW ?? 0m) * (profile.ElectricityUnitRate ?? 0m);
                        sumProductionCost += (mh + oh + eh) * productiveH;
                    }
                    else
                    {
                        rowsMissingCostProfile++;
                    }
                }

                // Phase Q.0 - summary aggregates
                int count = rows.Count;
                int postedCount = rows.Count(r => r.IsPosted);
                int draftCount  = rows.Count(r => !r.IsPosted);
                int reversedCount = rows.Count(r => r.IsReversed);
                int sentToQcCount = rows.Count(r => r.IsSentToQC);
                int qcDoneCount   = rows.Count(r => r.QcCompleted);
                int plannedTotal  = rows.Sum(r => r.PlannedQty ?? 0);
                int actualTotal   = rows.Sum(r => r.ActualQty ?? 0);
                int acceptedTotal = rows.Sum(r => r.AcceptedQty ?? 0);
                int rejectedTotal = rows.Sum(r => r.RejectedQty ?? 0);
                long stitchesTotal = rows.Sum(r => (long)(r.Stitches ?? 0));

                decimal yieldPct = plannedTotal > 0 ? Math.Round((decimal)actualTotal / plannedTotal * 100m, 1) : 0m;
                decimal qcPassPct = (acceptedTotal + rejectedTotal) > 0
                    ? Math.Round((decimal)acceptedTotal / (acceptedTotal + rejectedTotal) * 100m, 1)
                    : 0m;

                var byStatus = rows
                    .GroupBy(r => string.IsNullOrWhiteSpace(r.Status) ? "Unknown" : r.Status.Trim())
                    .Select(g => new { status = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToList();
                var byShift = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.Shift))
                    .GroupBy(r => r.Shift.Trim())
                    .Select(g => new { shift = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .ToList();

                decimal totalProductionCost = Math.Round(sumProductionCost, 2);
                decimal costPerAcceptedPc = acceptedTotal > 0 ? Math.Round(totalProductionCost / acceptedTotal, 4) : 0m;

                var summary = new
                {
                    count,
                    postedCount,
                    draftCount,
                    reversedCount,
                    sentToQcCount,
                    qcDoneCount,
                    plannedTotal,
                    actualTotal,
                    acceptedTotal,
                    rejectedTotal,
                    stitchesTotal,
                    yieldPct,
                    qcPassPct,
                    // Phase S.1 cost rollup
                    totalProductionCost,
                    costPerAcceptedPc,
                    totalProductiveHours = Math.Round(sumProductiveHours, 2),
                    totalStopHours = Math.Round(sumStopHours, 2),
                    rowsWithCostProfile,
                    rowsMissingCostProfile,
                    byStatus,
                    byShift
                };

                return JsonSuccess("Loaded successfully.", new { items = rows, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load production list.", ex);
            }
        }

        // =========================================================
        // DETAIL
        // =========================================================
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var entity = db.DailyProduction
                    .Include("WorkOrderItems.WorkOrders")
                    .Include("WorkOrderItems.Gen_Design")
                    .Include("WorkOrderItems.Gen_Products")
                    .Include("Gen_Machines")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (entity == null)
                    return JsonError("Not found.");

                int? GetEmployeeId(int? userId)
                {
                    if (!userId.HasValue) return null;

                    return db.Adm_Users
                        .Where(u => u.Id == userId.Value)
                        .Select(u => (int?)u.EmployeeId)
                        .FirstOrDefault();
                }

                int workOrderId = entity.WorkOrderItems != null ? entity.WorkOrderItems.WorkOrderId : 0;
                int? designId = entity.WorkOrderItems != null ? entity.WorkOrderItems.DesignId : null;
                int? productId = entity.WorkOrderItems != null ? entity.WorkOrderItems.ProductId : null;

                var qcInfo = db.QC_Inspection
                    .Where(q =>
                        q.ProductionId.HasValue &&
                        q.ProductionId.Value == entity.Id &&
                        q.Deleted != true &&
                        q.Status != "Reversed" &&
                        q.Status != "Cancelled")
                    .Select(q => new
                    {
                        q.Id,
                        q.IsPosted
                    })
                    .FirstOrDefault();

                bool hasQc = qcInfo != null;
                int? qcId = qcInfo != null ? (int?)qcInfo.Id : null;
                bool qcCompleted = qcInfo != null && qcInfo.IsPosted;

                var operatorUser = entity.OperatorId.HasValue
                    ? db.Adm_Users
                        .Where(u => u.Id == entity.OperatorId.Value)
                        .Select(u => new
                        {
                            u.Id,
                            u.EmployeeId,
                            u.UserName
                        })
                        .FirstOrDefault()
                    : null;

                string operatorCode = "-";
                string operatorName = "-";
                string operatorLabel = "-";

                if (operatorUser != null)
                {
                    operatorCode = operatorUser.EmployeeId.HasValue
                        ? ("EMP-" + operatorUser.EmployeeId.Value)
                        : "-";

                    operatorName = !string.IsNullOrWhiteSpace(operatorUser.UserName)
                        ? operatorUser.UserName
                        : "-";

                    operatorLabel = operatorCode != "-"
                        ? (operatorCode + " - " + operatorName)
                        : operatorName;
                }

                bool dIsPosted   = entity.IsPosted == true;
                bool dIsReversed = entity.IsReversed == true;
                bool dIsSentToQc = entity.IsSentToQC == true;
                int  dStepIdx    = ComputeDpWorkflowStageIndex(dIsPosted, dIsReversed, dIsSentToQc, qcCompleted);

                var data = new DailyProductionDetailViewModel
                {
                    Id = entity.Id,

                    WorkOrderItemId = entity.WorkOrderItemId,
                    WorkOrderId = workOrderId > 0 ? (int?)workOrderId : null,
                    WorkOrderLabel = entity.WorkOrderItems != null
                        ? (
                            entity.WorkOrderItems.WorkOrders != null &&
                            !string.IsNullOrWhiteSpace(entity.WorkOrderItems.WorkOrders.WorkOrderCode)
                                ? entity.WorkOrderItems.WorkOrders.WorkOrderCode
                                : ("WO-" + entity.WorkOrderItems.WorkOrderId)
                          )
                        : null,

                    // Sprint-2: stepper data for the detail drawer.
                    WorkflowStages = _dpWorkflowStages,
                    WorkflowStageIndex = dStepIdx,

                    ProductId = productId,
                    ProductName = entity.WorkOrderItems != null && entity.WorkOrderItems.Gen_Products != null
                        ? entity.WorkOrderItems.Gen_Products.Prod_Name
                        : null,

                    DesignId = designId,
                    DesignName = entity.WorkOrderItems != null && entity.WorkOrderItems.Gen_Design != null
                        ? entity.WorkOrderItems.Gen_Design.DNo
                        : null,

                    MachineId = entity.MachineId,
                    MachineName = entity.Gen_Machines != null
                        ? entity.Gen_Machines.Serial_Number
                        : null,

                    OperatorId = entity.OperatorId,
                    OperatorCode = operatorCode,
                    OperatorName = operatorName,
                    OperatorLabel = operatorLabel,

                    Shift = entity.Shift,

                    StartTime = entity.StartTime.HasValue
                        ? entity.StartTime.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null,

                    EndTime = entity.EndTime.HasValue
                        ? entity.EndTime.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null,

                    Stitches = entity.Stitches,
                    ActualQty = entity.ActualQty,
                    RejectedQty = entity.RejectedQty,
                    AcceptedQty = entity.AcceptedQty,

                    Status = entity.Status,
                    Remarks = entity.Remarks,
                    Deleted = entity.Deleted,

                    CreatedBy = entity.CreatedBy,
                    CreatedDate = entity.CreatedDate.HasValue
                        ? entity.CreatedDate.Value.ToString("dd-MMM-yyyy hh:mm tt")
                        : null,

                    UpdatedBy = entity.UpdatedBy,
                    UpdatedDate = entity.UpdatedDate.HasValue
                        ? entity.UpdatedDate.Value.ToString("dd-MMM-yyyy hh:mm tt")
                        : null,

                    RecordTimeStamp = entity.RecordTimeStamp.HasValue
                        ? entity.RecordTimeStamp.Value.ToString("dd-MMM-yyyy hh:mm tt")
                        : null,

                    CreatedByEmployeeId = GetEmployeeId(entity.CreatedBy),
                    UpdatedByEmployeeId = GetEmployeeId(entity.UpdatedBy),

                    HasQc = hasQc,
                    QcId = qcId,
                    QcCompleted = qcCompleted,

                    CanEdit = !(entity.IsPosted == true || entity.IsReversed == true || entity.IsSentToQC == true || hasQc),
                    CanDelete = !(entity.IsPosted == true || entity.IsReversed == true || entity.IsSentToQC == true)
                };

                return JsonSuccess("Loaded successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load detail.", ex);
            }
        }

        // =========================================================
        // CREATE
        // =========================================================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(DailyProductionInputViewModel dto)
        {
            if (dto == null) return JsonError("Invalid payload.");

            if (!dto.WorkOrderItemId.HasValue || dto.WorkOrderItemId.Value <= 0)
                return JsonError("Work Order Item is required.");

            if (!dto.MachineId.HasValue || dto.MachineId.Value <= 0)
                return JsonError("Machine is required.");

            if (!dto.OperatorId.HasValue || dto.OperatorId.Value <= 0)
                return JsonError("Operator is required.");

            if (!dto.ActualQty.HasValue || dto.ActualQty.Value <= 0)
                return JsonError("Actual quantity must be greater than zero.");

            if ((dto.RejectedQty ?? 0) < 0)
                return JsonError("Rejected quantity cannot be negative.");

            if ((dto.RejectedQty ?? 0) > dto.ActualQty.Value)
                return JsonError("Rejected quantity cannot be greater than actual quantity.");

            var timeValidation = ValidateProductionTime(dto);
            if (timeValidation != null) return timeValidation;

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var workOrderItem = db.WorkOrderItems.FirstOrDefault(x =>
                        x.Id == dto.WorkOrderItemId.Value &&
                        x.Deleted != true &&
                        x.Active == true);

                    if (workOrderItem == null)
                        return JsonError("Selected Work Order Item not found.");

                    if (workOrderItem.WorkOrderId <= 0)
                        return JsonError("Invalid Work Order reference.");

                    var workOrder = db.WorkOrders.FirstOrDefault(x => x.Id == workOrderItem.WorkOrderId);

                    if (workOrder == null)
                        return JsonError("Parent Work Order not found.");

                    if (workOrder.Deleted == true)
                        return JsonError("Parent Work Order is deleted. Restore Work Order before reprocessing.");

                    if (workOrder.IsPosted != true)
                        return JsonError("Parent Work Order is not posted.");

                    int acceptedQty = (dto.ActualQty ?? 0) - (dto.RejectedQty ?? 0);

                    if (acceptedQty <= 0)
                        return JsonError("Accepted quantity must be greater than zero.");

                    // Rework loop: a DP entry tied to a QC rework task re-does
                    // pieces that were already counted against the WO's plan.
                    // Skip the remaining-qty check (those pieces are not new
                    // production) and validate against the QC's pending rework.
                    bool isRework = dto.SourceQcInspectionId.HasValue && dto.SourceQcInspectionId.Value > 0;
                    QC_Inspection sourceQc = null;
                    if (isRework)
                    {
                        sourceQc = db.QC_Inspection.FirstOrDefault(q =>
                            q.Id == dto.SourceQcInspectionId.Value &&
                            (q.Deleted == false || q.Deleted == null));

                        if (sourceQc == null)
                            return JsonError("Source QC inspection not found.");

                        if (!string.Equals(sourceQc.Status, "AwaitingRework", StringComparison.OrdinalIgnoreCase))
                            return JsonError("Source QC is not awaiting rework.");

                        decimal pendingRework = sourceQc.ReworkQty ?? 0m;
                        if (acceptedQty > pendingRework)
                            return JsonError("Rework qty exceeds pending. Pending: " + pendingRework);
                    }
                    else
                    {
                        int alreadyProduced = db.DailyProduction
                            .Where(x =>
                                x.WorkOrderItemId == workOrderItem.Id &&
                                x.Deleted != true &&
                                x.IsReversed != true)
                            .Sum(x => (int?)x.AcceptedQty) ?? 0;

                        int remainingQty = workOrderItem.Quantity - alreadyProduced;

                        if (remainingQty <= 0)
                            return JsonError("No remaining quantity available for production.");

                        if (acceptedQty > remainingQty)
                            return JsonError("Production exceeds remaining quantity. Remaining: " + remainingQty);
                    }

                    var production = new DailyProduction
                    {
                        WorkOrderItemId = workOrderItem.Id,
                        WorkOrderId = workOrderItem.WorkOrderId,

                        MachineId = dto.MachineId,
                        OperatorId = dto.OperatorId,
                        Shift = string.IsNullOrWhiteSpace(dto.Shift) ? null : dto.Shift.Trim(),

                        StartTime = dto.StartTime,
                        EndTime = dto.EndTime,
                        Stitches = dto.Stitches,

                        PlannedQty = workOrderItem.Quantity,
                        ActualQty = dto.ActualQty,
                        RejectedQty = dto.RejectedQty ?? 0,
                        AcceptedQty = acceptedQty,

                        Status = isRework ? "Rework" : "WIP",
                        Remarks = BuildDpRemarks(dto.Remarks, isRework ? dto.SourceQcInspectionId : (int?)null),

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,
                        RecordTimeStamp = now,

                        Active = true,
                        Deleted = false,
                        IsPosted = false,
                        IsReversed = false,
                        IsSentToQC = false,
                        QCCompleted = false
                    };

                    db.DailyProduction.Add(production);
                    db.SaveChanges();

                    UpdateWorkOrderItemProgress(workOrderItem.Id);

                    if (workOrder.Status == "Material Issued" ||
                        workOrder.Status == "Pending" ||
                        workOrder.Status == "Posted")
                    {
                        workOrder.Status = "In Production";
                        workOrder.UpdatedBy = userId;
                        workOrder.UpdatedDate = now;
                        workOrder.RecordTimeStamp = now;
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogDpAudit(production.Id, "CREATED",
                        oldValue: null,
                        newValue: DailyProductionStatus.DRAFT,
                        detail: "Actual " + (production.ActualQty ?? 0) + " / Rejected " + (production.RejectedQty ?? 0));

                    return JsonSuccess("Production saved successfully.", new
                    {
                        id = production.Id,
                        acceptedQty = production.AcceptedQty,
                        workOrderId = production.WorkOrderId,
                        workOrderItemId = production.WorkOrderItemId
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to save production. " + DescribeDpError(ex));
                }
            }
        }

        // =========================================================
        // UPDATE
        // =========================================================
        [HttpPost]
        [Route("update/{id:int}")]
        public ActionResult Update(int id, DailyProductionInputViewModel dto)
        {
            if (dto == null)
                return JsonError("Invalid payload.");

            if (!dto.MachineId.HasValue || dto.MachineId.Value <= 0)
                return JsonError("Machine is required.");

            if (!dto.OperatorId.HasValue || dto.OperatorId.Value <= 0)
                return JsonError("Operator is required.");

            if (!dto.ActualQty.HasValue || dto.ActualQty.Value <= 0)
                return JsonError("Actual quantity must be greater than zero.");

            if ((dto.RejectedQty ?? 0) < 0)
                return JsonError("Rejected quantity cannot be negative.");

            if ((dto.RejectedQty ?? 0) > dto.ActualQty.Value)
                return JsonError("Rejected quantity cannot exceed actual quantity.");

            var timeValidation = ValidateProductionTime(dto);
            if (timeValidation != null)
                return timeValidation;

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var production = db.DailyProduction.FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (production == null)
                        return JsonError("Production not found.");

                    if (production.IsPosted == true)
                        return JsonError("Posted production cannot be modified.");

                    if (production.IsReversed == true)
                        return JsonError("Reversed production cannot be modified.");

                    if (production.IsSentToQC == true)
                        return JsonError("Production already sent to QC and cannot be modified.");

                    if (dto.WorkOrderItemId != production.WorkOrderItemId)
                        return JsonError("Work Order Item cannot be changed.");

                    if (!production.WorkOrderItemId.HasValue)
                        return JsonError("Invalid Work Order Item.");

                    var item = db.WorkOrderItems.FirstOrDefault(x =>
                        x.Id == production.WorkOrderItemId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (item == null)
                        return JsonError("Work Order Item not found.");

                    int newAccepted = (dto.ActualQty ?? 0) - (dto.RejectedQty ?? 0);

                    if (newAccepted <= 0)
                        return JsonError("Accepted quantity must be greater than zero.");

                    var reversedQcProductionIds = db.QC_Inspection
                        .Where(q =>
                            (q.Deleted == false || q.Deleted == null) &&
                            q.Status == "Reversed" &&
                            q.ProductionId.HasValue)
                        .Select(q => q.ProductionId.Value)
                        .ToList();

                    int totalOtherProduction = db.DailyProduction
                        .Where(x =>
                            x.WorkOrderItemId == production.WorkOrderItemId &&
                            x.Id != production.Id &&
                            (x.Deleted == false || x.Deleted == null) &&
                            (x.IsReversed == false || x.IsReversed == null) &&
                            !reversedQcProductionIds.Contains(x.Id))
                        .Sum(x => (int?)x.AcceptedQty) ?? 0;

                    int planned = item.Quantity;
                    int remaining = planned - totalOtherProduction;

                    if (newAccepted > remaining)
                        return JsonError("Exceeds remaining qty. Remaining: " + remaining);

                    production.MachineId = dto.MachineId;
                    production.OperatorId = dto.OperatorId;
                    production.Shift = string.IsNullOrWhiteSpace(dto.Shift) ? null : dto.Shift.Trim();

                    production.StartTime = dto.StartTime;
                    production.EndTime = dto.EndTime;
                    production.Stitches = dto.Stitches;

                    production.ActualQty = dto.ActualQty;
                    production.RejectedQty = dto.RejectedQty ?? 0;
                    production.AcceptedQty = newAccepted;
                    production.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

                    production.UpdatedBy = userId;
                    production.UpdatedDate = now;
                    production.RecordTimeStamp = now;

                    db.SaveChanges();

                    UpdateWorkOrderItemProgress(production.WorkOrderItemId.Value);

                    db.SaveChanges();
                    tx.Commit();

                    LogDpAudit(production.Id, "EDITED",
                        oldValue: DailyProductionStatus.DRAFT,
                        newValue: DailyProductionStatus.DRAFT,
                        detail: "Actual " + (production.ActualQty ?? 0) + " / Rejected " + (production.RejectedQty ?? 0));

                    return JsonSuccess("Production updated successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Update failed. " + DescribeDpError(ex));
                }
            }
        }

        // =========================================================
        // Phase Q.1 - DAILY PRODUCTION 360 PROFILE
        // =========================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var p = db.DailyProduction
                    .Include("WorkOrderItems.WorkOrders")
                    .Include("WorkOrderItems.Gen_Design")
                    .Include("WorkOrderItems.Gen_Products")
                    .Include("WorkOrderItems.Gen_Components")
                    .Include("Gen_Machines")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (p == null) return JsonError("Daily production not found.");

                var wi = p.WorkOrderItems;
                int? workOrderId = wi != null ? (int?)wi.WorkOrderId : null;
                string operatorCode = "-";
                string operatorName = "-";
                if (p.OperatorId.HasValue)
                {
                    var u = db.Adm_Users.FirstOrDefault(x => x.Id == p.OperatorId.Value);
                    if (u != null)
                    {
                        operatorCode = u.EmployeeId.HasValue ? ("EMP-" + u.EmployeeId.Value) : "-";
                        operatorName = u.UserName ?? "-";
                    }
                }

                int planned  = p.PlannedQty ?? 0;
                int actual   = p.ActualQty ?? 0;
                int accepted = p.AcceptedQty ?? 0;
                int rejected = p.RejectedQty ?? 0;
                long stitches = p.Stitches ?? 0;

                decimal yieldPct = planned > 0 ? Math.Round((decimal)actual / planned * 100m, 1) : 0m;
                decimal qcPct    = (accepted + rejected) > 0 ? Math.Round((decimal)accepted / (accepted + rejected) * 100m, 1) : 0m;

                // Duration + stops
                decimal totalRunMinutes = 0m;
                if (p.StartTime.HasValue && p.EndTime.HasValue && p.EndTime.Value > p.StartTime.Value)
                    totalRunMinutes = (decimal)(p.EndTime.Value - p.StartTime.Value).TotalMinutes;

                var stops = db.DailyProductionStops
                    .Where(s => s.ProductionId == id)
                    .OrderBy(s => s.StopStart)
                    .ToList()
                    .Select(s => new
                    {
                        id = s.Id,
                        stopStartText = s.StopStart.ToString("dd-MMM-yyyy HH:mm"),
                        stopEndText = s.StopEnd.HasValue ? s.StopEnd.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                        durationMinutes = (s.StopStart != null && s.StopEnd.HasValue && s.StopEnd.Value > s.StopStart)
                            ? Math.Round((decimal)(s.StopEnd.Value - s.StopStart).TotalMinutes, 1)
                            : 0m,
                        reason = s.Reason,
                        createdDateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                    })
                    .ToList();

                decimal totalStopMinutes = stops.Sum(s => s.durationMinutes);
                decimal productiveMinutes = Math.Max(0m, totalRunMinutes - totalStopMinutes);
                decimal utilizationPct = totalRunMinutes > 0
                    ? Math.Round(productiveMinutes / totalRunMinutes * 100m, 1)
                    : 0m;

                // QC trail
                var qcEntities = db.QC_Inspection
                    .Where(q => q.ProductionId == id && q.Deleted != true)
                    .OrderByDescending(q => q.InspectionDate).ThenByDescending(q => q.Id)
                    .ToList();

                var qcRows = qcEntities
                    .Select(q => new
                    {
                        id = q.Id,
                        qcNo = q.QCNo,
                        inspectionDateText = q.InspectionDate.ToString("dd-MMM-yyyy"),
                        status = q.Status,
                        inspectionType = q.InspectionType,
                        sourceType = q.SourceType,
                        isPosted = q.IsPosted,
                        batchQty = q.BatchQty ?? 0m,
                        inspectedQty = q.InspectedQty ?? 0m,
                        passedQty = q.PassedQty ?? 0m,
                        failedQty = q.FailedQty ?? 0m,
                        reworkQty = q.ReworkQty ?? 0m
                    })
                    .ToList();

                // Phase Q.3 - Derive QC completion from the QC_Inspection table
                // because DailyProduction.QCCompleted isn't being set when QC posts.
                var qcCompletedEntity = qcEntities
                    .Where(q => q.IsPosted == true
                             && q.Status != "Reversed"
                             && q.Status != "Cancelled")
                    .OrderByDescending(q => q.PostedDate ?? q.InspectionDate)
                    .ThenByDescending(q => q.Id)
                    .FirstOrDefault();
                bool derivedQcCompleted = qcCompletedEntity != null;
                string derivedQcCompletedDateText = qcCompletedEntity == null
                    ? null
                    : (qcCompletedEntity.PostedDate ?? qcCompletedEntity.InspectionDate)
                        .ToString("dd-MMM-yyyy HH:mm");

                // Stock trail: Gen_Stocks IN that flowed from any QC linked to this production
                var qcIds = qcRows.Select(x => x.id).ToList();
                var stockTrailOut = new System.Collections.Generic.List<object>();
                if (qcIds.Any())
                {
                    var stockTrail = db.Gen_Stocks
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
                    foreach (var s in stockTrail)
                    {
                        stockTrailOut.Add(new
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
                        });
                    }
                }

                var header = new
                {
                    id = p.Id,
                    isPosted = p.IsPosted == true,
                    isReversed = p.IsReversed == true,
                    isSentToQc = p.IsSentToQC == true,
                    qcCompleted = derivedQcCompleted,
                    status = p.Status,
                    shift = p.Shift,
                    startTimeText = p.StartTime.HasValue ? p.StartTime.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    endTimeText = p.EndTime.HasValue ? p.EndTime.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    postedDateText = p.PostedDate.HasValue ? p.PostedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    sentToQcDateText = p.SentToQCDate.HasValue ? p.SentToQCDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    qcCompletedDateText = derivedQcCompletedDateText
                        ?? (p.QCCompletedDate.HasValue ? p.QCCompletedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null),

                    workOrderId,
                    workOrderCode = wi != null && wi.WorkOrders != null ? wi.WorkOrders.WorkOrderCode : null,
                    workOrderItemId = p.WorkOrderItemId,
                    designName = wi != null && wi.Gen_Design != null ? wi.Gen_Design.CollectionName : null,
                    designCode = wi != null && wi.Gen_Design != null ? wi.Gen_Design.D_Code : null,
                    productName = wi != null && wi.Gen_Products != null ? wi.Gen_Products.Prod_Name : null,
                    productCode = wi != null && wi.Gen_Products != null ? wi.Gen_Products.Prod_Code : null,
                    componentName = wi != null && wi.Gen_Components != null ? wi.Gen_Components.Name : null,
                    machineId = p.MachineId,
                    machineCode = p.Gen_Machines != null ? p.Gen_Machines.Code : null,
                    machineSerial = p.Gen_Machines != null ? p.Gen_Machines.Serial_Number : null,
                    operatorId = p.OperatorId,
                    operatorCode,
                    operatorName,
                    remarks = p.Remarks,
                    reverseReason = p.ReverseReason
                };

                // Phase S.1 — Actual machine running cost from MachineCostProfile
                decimal totalRunHours = Math.Round(totalRunMinutes / 60m, 4);
                decimal productiveHours = Math.Round(productiveMinutes / 60m, 4);

                decimal machineHourly = 0m;
                decimal operatorHourly = 0m;
                decimal powerKW = 0m;
                decimal electricityUnitRate = 0m;
                decimal stitchPerMinute = 0m;
                bool hasCostProfile = false;
                string costProfileSourceText = null;

                if (p.MachineId.HasValue)
                {
                    var profile = db.MachineCostProfile
                        .Where(c => c.MachineId == p.MachineId.Value
                                 && c.Deleted != true
                                 && (c.Active ?? true) == true)
                        .OrderByDescending(c => c.Id)
                        .FirstOrDefault();

                    if (profile != null)
                    {
                        hasCostProfile = true;
                        machineHourly = profile.HourlyRate ?? 0m;
                        operatorHourly = profile.OperatorHourlyRate ?? 0m;
                        powerKW = profile.PowerKW ?? 0m;
                        electricityUnitRate = profile.ElectricityUnitRate ?? 0m;
                        stitchPerMinute = profile.StitchPerMinute ?? 0m;
                        costProfileSourceText = profile.EffectiveFrom.HasValue
                            ? ("Profile #" + profile.Id + " · effective " + profile.EffectiveFrom.Value.ToString("dd-MMM-yyyy"))
                            : ("Profile #" + profile.Id);
                    }
                }

                decimal electricHourly = Math.Round(powerKW * electricityUnitRate, 4);
                decimal totalHourly = Math.Round(machineHourly + operatorHourly + electricHourly, 2);

                // Productive cost (excludes stop time)
                decimal machineCost = Math.Round(machineHourly * productiveHours, 2);
                decimal operatorCost = Math.Round(operatorHourly * productiveHours, 2);
                decimal electricCost = Math.Round(electricHourly * productiveHours, 2);
                decimal totalProductionCost = Math.Round(machineCost + operatorCost + electricCost, 2);

                // Idle cost (stop time × hourly machine+operator — electricity assumed off)
                decimal idleHours = Math.Max(0m, totalRunHours - productiveHours);
                decimal idleCost = Math.Round((machineHourly + operatorHourly) * idleHours, 2);

                decimal costPerAcceptedPc = accepted > 0 ? Math.Round(totalProductionCost / accepted, 4) : 0m;

                // Expected stitches given the productive minutes + machine speed
                decimal expectedStitches = Math.Round(stitchPerMinute * productiveMinutes, 0);
                decimal stitchEfficiencyPct = expectedStitches > 0
                    ? Math.Round((decimal)stitches / expectedStitches * 100m, 1)
                    : 0m;

                // Rated cost per stitch — what one stitch SHOULD cost at machine spec speed
                // (independent of how long production actually ran)
                decimal stitchSecondsAtSpec = stitchPerMinute > 0 ? 60m / stitchPerMinute : 0m;
                decimal ratedCostPerStitch = stitchSecondsAtSpec > 0
                    ? Math.Round((totalHourly / 3600m) * stitchSecondsAtSpec, 6)
                    : 0m;

                // Stitches cost — cost attributable to actual stitch volume at machine spec
                // (= total production cost only when stitch efficiency = 100%)
                decimal stitchesCost = Math.Round(ratedCostPerStitch * stitches, 2);

                // Actual cost per stitch (allocated from total production cost)
                decimal allocatedCostPerStitch = stitches > 0 ? Math.Round(totalProductionCost / stitches, 6) : 0m;

                var cost = new
                {
                    hasCostProfile,
                    costProfileSourceText,
                    machineHourly = Math.Round(machineHourly, 2),
                    operatorHourly = Math.Round(operatorHourly, 2),
                    powerKW = Math.Round(powerKW, 2),
                    electricityUnitRate = Math.Round(electricityUnitRate, 2),
                    electricHourly,
                    totalHourly,
                    stitchPerMinute,
                    totalRunHours,
                    productiveHours,
                    idleHours = Math.Round(idleHours, 4),
                    machineCost,
                    operatorCost,
                    electricCost,
                    idleCost,
                    totalProductionCost,
                    costPerAcceptedPc,
                    costPerStitch = allocatedCostPerStitch,
                    ratedCostPerStitch,
                    stitchesCost,
                    expectedStitches,
                    stitchEfficiencyPct
                };

                var kpis = new
                {
                    plannedQty = planned,
                    actualQty = actual,
                    acceptedQty = accepted,
                    rejectedQty = rejected,
                    stitches,
                    yieldPct,
                    qcPassPct = qcPct,
                    totalRunMinutes = Math.Round(totalRunMinutes, 1),
                    totalStopMinutes = Math.Round(totalStopMinutes, 1),
                    productiveMinutes = Math.Round(productiveMinutes, 1),
                    utilizationPct,
                    stopCount = stops.Count(),
                    qcCount = qcRows.Count(),
                    stockRowCount = stockTrailOut.Count,
                    totalProductionCost,
                    costPerAcceptedPc
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    cost,
                    stops,
                    qcTrail = qcRows,
                    stockTrail = stockTrailOut
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        // =========================================================
        // DELETE (SOFT)
        // =========================================================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var entity = db.DailyProduction.FirstOrDefault(x =>
                    x.Id == id && x.Deleted != true);

                if (entity == null)
                    return JsonError("Record not found.");

                if (entity.IsPosted == true)
                    return JsonError("Posted production cannot be deleted. Please reverse first.");

                if (entity.IsSentToQC == true)
                    return JsonError("Production already sent to QC and cannot be deleted.");

                entity.Deleted = true;
                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;
                entity.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                if (entity.WorkOrderItemId.HasValue)
                {
                    UpdateWorkOrderItemProgress(entity.WorkOrderItemId.Value);
                    db.SaveChanges();
                }

                LogDpAudit(id, "DELETED",
                    oldValue: DailyProductionStatus.DRAFT,
                    newValue: "DELETED",
                    detail: "Soft-deleted");

                return JsonSuccess("Deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed. " + DescribeDpError(ex));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        //  Each endpoint accepts { ids: int[] } and returns the standard
        //  envelope: { successCount, failureCount, failures }. Per-row
        //  failures don't break the batch — they're reported back so
        //  the toolbar can render "3 posted, 1 failed (reason: …)".
        //  Each row runs in its own action call so the existing per-row
        //  Post / SendToQc / Reverse transactions stay atomic.
        // ─────────────────────────────────────────────────────────────
        public class DpBulkIdsDto { public List<int> Ids { get; set; } }
        public class DpBulkReverseDto
        {
            public List<int> Ids { get; set; }
            public string Reason { get; set; }
        }

        private class DpBulkFailure
        {
            public int id;
            public string label;
            public string reason;
        }

        private object DoBulkDp(List<int> ids, Func<int, DailyProduction, string> runOne)
        {
            var failures = new List<DpBulkFailure>();
            int ok = 0;

            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var dp = db.DailyProduction
                    .Include(x => x.WorkOrderItems.WorkOrders)
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (dp == null)
                {
                    failures.Add(new DpBulkFailure { id = id, label = "(missing)", reason = "Not found." });
                    continue;
                }
                string label = dp.WorkOrderItems != null && dp.WorkOrderItems.WorkOrders != null
                               ? (dp.WorkOrderItems.WorkOrders.WorkOrderCode ?? "DP#" + id)
                               : "DP#" + id;
                try
                {
                    string err = runOne(id, dp);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new DpBulkFailure { id = id, label = label, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new DpBulkFailure { id = id, label = label, reason = DescribeDpError(ex) });
                }
            }

            return new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures = failures.Select(f => new { id = f.id, code = f.label, reason = f.reason })
            };
        }

        [HttpPost, Route("bulk-post")]
        public ActionResult BulkPost(DpBulkIdsDto dto)
        {
            var data = DoBulkDp(dto?.Ids, (id, dp) =>
            {
                var res = PostToWip(id) as JsonResult;
                if (res?.Data == null) return "Post failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk post completed.", data);
        }

        [HttpPost, Route("bulk-send-to-qc")]
        public ActionResult BulkSendToQc(DpBulkIdsDto dto)
        {
            var data = DoBulkDp(dto?.Ids, (id, dp) =>
            {
                var res = SendToQc(id) as JsonResult;
                if (res?.Data == null) return "Send-to-QC failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk send-to-QC completed.", data);
        }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(DpBulkReverseDto dto)
        {
            var data = DoBulkDp(dto?.Ids, (id, dp) =>
            {
                var res = Reverse(id, new ReverseDailyProductionDto { Reason = dto?.Reason }) as JsonResult;
                if (res?.Data == null) return "Reverse failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk reverse completed.", data);
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(DpBulkIdsDto dto)
        {
            var data = DoBulkDp(dto?.Ids, (id, dp) =>
            {
                var res = Delete(id) as JsonResult;
                if (res?.Data == null) return "Delete failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk delete completed.", data);
        }

        // =========================================================
        // POST DAILY PRODUCTION TO WIP
        // =========================================================
        [HttpPost]
        [Route("post/{id:int}")]
        public ActionResult Post(int id)
        {
            return PostToWip(id);
        }

        [HttpPost]
        [Route("post-to-wip/{id:int}")]
        public ActionResult PostToWip(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard. Two operators clicking Post
                    // on the same row simultaneously would each pass the
                    // IsPosted check and both accrue cost to the parent
                    // WorkOrderItem. The row-level UPDLOCK serialises
                    // them — the second caller blocks until the first
                    // commits, then sees IsPosted=true and exits cleanly.
                    LockDpRow(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var dp = db.DailyProduction
                        .Include(x => x.WorkOrderItems)
                        .FirstOrDefault(x =>
                            x.Id == id &&
                            x.Deleted != true);

                    if (dp == null)
                        return JsonError("Daily production record not found.");

                    if (dp.IsPosted == true)
                        return JsonError("Production already posted to WIP.");

                    if (dp.IsReversed == true)
                        return JsonError("Reversed production cannot be posted.");

                    if (dp.WorkOrderItems == null)
                        return JsonError("Work Order Item not found for this production row.");

                    int actualQty = dp.ActualQty ?? 0;
                    int rejectedQty = dp.RejectedQty ?? 0;
                    int acceptedQty = actualQty - rejectedQty;

                    if (actualQty <= 0)
                        return JsonError("Actual quantity must be greater than zero.");

                    if (rejectedQty < 0)
                        return JsonError("Rejected quantity cannot be negative.");

                    if (rejectedQty > actualQty)
                        return JsonError("Rejected quantity cannot exceed actual quantity.");

                    if (acceptedQty <= 0)
                        return JsonError("Accepted quantity must be greater than zero.");

                    // Rework loop: a DP row tagged with [QC:id] is the operator
                    // re-doing pieces QC marked for rework. These pieces are
                    // already counted in the WO's CompletedQty (from the original
                    // production run), so we skip the planned/remaining check
                    // and the cost accrual — instead we flip the parent QC row
                    // to AwaitingReinspection so the inspector can re-check.
                    int? sourceQcId = ExtractQcIdFromRemarks(dp.Remarks);
                    bool isRework = sourceQcId.HasValue;

                    int plannedQty = dp.WorkOrderItems.Quantity;
                    int remainingQty = plannedQty;

                    if (!isRework)
                    {
                        var reversedQcProductionIds = db.QC_Inspection
                            .Where(q =>
                                (q.Deleted == false || q.Deleted == null) &&
                                q.Status == "Reversed" &&
                                q.ProductionId.HasValue)
                            .Select(q => q.ProductionId.Value)
                            .ToList();

                        int totalOtherPosted = db.DailyProduction
                            .Where(x =>
                                x.WorkOrderItemId == dp.WorkOrderItemId &&
                                x.Id != dp.Id &&
                                (x.Deleted == false || x.Deleted == null) &&
                                (x.IsReversed == false || x.IsReversed == null) &&
                                x.Status != "Rework" &&
                                !reversedQcProductionIds.Contains(x.Id))
                            .Sum(x => (int?)x.AcceptedQty) ?? 0;

                        remainingQty = plannedQty - totalOtherPosted;

                        if (remainingQty <= 0)
                            return JsonError("No remaining quantity available for posting.");

                        if (acceptedQty > remainingQty)
                            return JsonError("Posting exceeds remaining quantity. Remaining: " + remainingQty);
                    }

                    dp.AcceptedQty = acceptedQty;
                    dp.PlannedQty = plannedQty;

                    dp.IsPosted = true;
                    dp.IsSentToQC = false;
                    dp.QCCompleted = false;
                    dp.SentToQCBy = null;
                    dp.SentToQCDate = null;

                    dp.Status = isRework ? "ReworkPosted" : "PostedToWIP";
                    dp.PostedBy = userId;
                    dp.PostedDate = now;
                    dp.UpdatedBy = userId;
                    dp.UpdatedDate = now;
                    dp.RecordTimeStamp = now;

                    db.SaveChanges();

                    if (isRework)
                    {
                        // Flip the parent QC row so the inspector can re-check.
                        var parentQc = db.QC_Inspection.FirstOrDefault(q =>
                            q.Id == sourceQcId.Value &&
                            (q.Deleted == false || q.Deleted == null));

                        if (parentQc == null)
                            return JsonError("Source QC inspection not found for rework.");

                        if (!string.Equals(parentQc.Status, "AwaitingRework", StringComparison.OrdinalIgnoreCase))
                            return JsonError("Source QC is no longer awaiting rework.");

                        parentQc.Status = "AwaitingReinspection";
                        parentQc.UpdatedBy = userId;
                        parentQc.UpdatedDate = now;
                        parentQc.RecordTimeStamp = now;
                        parentQc.Remarks = string.IsNullOrWhiteSpace(parentQc.Remarks)
                            ? ("Rework completed in DP#" + dp.Id + ". Ready for re-inspection.")
                            : (parentQc.Remarks + " | Rework done in DP#" + dp.Id + ". Ready for re-inspection.");

                        db.SaveChanges();
                    }
                    else
                    {
                        // ❶ Phase S.1 — accrue ACTUAL production cost from this row
                        // into the parent WorkOrderItem. Adds on top of whatever was
                        // already there (DesignCosting estimate or prior accruals).
                        AccrueActualCostToWorkOrderItem(dp, acceptedQty);
                        db.SaveChanges();

                        if (dp.WorkOrderItemId.HasValue)
                        {
                            UpdateWorkOrderItemProgress(dp.WorkOrderItemId.Value);
                            db.SaveChanges();
                        }
                    }

                    tx.Commit();

                    LogDpAudit(dp.Id, "POSTED",
                        oldValue: DailyProductionStatus.DRAFT,
                        newValue: dp.Status,
                        detail: "Accepted " + acceptedQty + " pcs"
                              + (isRework ? "; rework against QC #" + sourceQcId : ""));

                    string message = isRework
                        ? "Rework posted. Parent QC is now awaiting re-inspection."
                        : "Production posted to WIP successfully.";

                    return JsonSuccess(message, new
                    {
                        id = dp.Id,
                        plannedQty = dp.PlannedQty,
                        actualQty = dp.ActualQty,
                        rejectedQty = dp.RejectedQty,
                        acceptedQty = dp.AcceptedQty,
                        remainingQtyAfterPost = isRework ? (int?)null : (remainingQty - acceptedQty),
                        isRework,
                        sourceQcId,
                        status = dp.Status
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to post production to WIP. " + DescribeDpError(ex));
                }
            }
        }

        // =========================================================
        // SEND TO QC (PRODUCT LEVEL BATCH)
        // =========================================================
        [HttpPost]
        [Route("send-to-qc/{id:int}")]
        public ActionResult SendToQc(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard. Two clicks on Send-to-QC
                    // for the same row would each create a QC_Inspection
                    // batch and double-flip IsSentToQC. Serialise on the
                    // row's UPDLOCK.
                    LockDpRow(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var prod = db.DailyProduction
                        .Include("WorkOrderItems")
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (prod == null)
                        return JsonError("Production record not found.");

                    if (prod.IsReversed == true)
                        return JsonError("Reversed production cannot be sent to QC.");

                    if (prod.IsPosted != true)
                        return JsonError("Production must be posted first.");

                    if (prod.IsSentToQC == true)
                        return JsonError("Production already sent to QC.");

                    // A rework DP entry already updated its parent QC row's
                    // status; it does not create a new inspection.
                    if (ExtractQcIdFromRemarks(prod.Remarks).HasValue)
                        return JsonError("Rework entries do not create new QC inspections — they re-open the original QC for re-inspection.");

                    if (prod.WorkOrderItems == null)
                        return JsonError("Work Order Item not found.");

                    var woi = prod.WorkOrderItems;

                    int workOrderId = woi.WorkOrderId;
                    int? designId = woi.DesignId;
                    int? productId = woi.ProductId;
                    int branchId = woi.BranchId ?? GetCurrentBranchId();

                    if (workOrderId <= 0)
                        return JsonError("Invalid Work Order.");

                    if (!designId.HasValue || designId.Value <= 0)
                        return JsonError("Invalid Design.");

                    if (!productId.HasValue || productId.Value <= 0)
                        return JsonError("Invalid Product.");

                    if (branchId <= 0)
                        return JsonError("Invalid Branch.");

                    int availableForQc = GetAvailableFinishedQtyForQc(workOrderId, designId.Value, productId.Value);
                    if (availableForQc <= 0)
                        return JsonError("No finished quantity is available for QC.");

                    bool openQcExists = db.QC_Inspection.Any(q =>
                        q.WorkOrderId == workOrderId &&
                        q.DesignId == designId &&
                        q.ProductId == productId &&
                        (q.Deleted == false || q.Deleted == null) &&
                        q.IsPosted != true &&
                        q.Status != "Reversed" &&
                        q.Status != "Cancelled");

                    if (openQcExists)
                        return JsonError("A pending QC batch already exists for this product.");

                    var qc = new QC_Inspection
                    {
                        WorkOrderId = workOrderId,
                        DesignId = designId,
                        ProductId = productId,
                        WorkOrderItemId = null,
                        ProductionId = prod.Id,

                        BatchQty = availableForQc,
                        BranchId = branchId,

                        InspectorId = userId,
                        InspectionDate = now,

                        InspectedQty = 0,
                        PassedQty = 0,
                        FailedQty = 0,
                        ReworkQty = 0,

                        Status = "InProgress",
                        QCNo = GenerateQcNo(),
                        Remarks = "Auto-created from finished production batch.",

                        Active = true,
                        Deleted = false,

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,
                        RecordTimeStamp = now,

                        IsPosted = false,
                        PostedBy = null,
                        PostedDate = null
                    };

                    db.QC_Inspection.Add(qc);
                    db.SaveChanges();

                    var componentItems = db.WorkOrderItems
                        .Where(x =>
                            x.WorkOrderId == workOrderId &&
                            x.DesignId == designId &&
                            x.ProductId == productId &&
                            (x.Deleted == false || x.Deleted == null) &&
                            x.CompId.HasValue)
                        .Select(x => new
                        {
                            x.Id,
                            x.CompId
                        })
                        .ToList()
                        .GroupBy(x => x.CompId.Value)
                        .Select(g => g.First())
                        .ToList();

                    foreach (var comp in componentItems)
                    {
                        db.QC_InspectionItems.Add(new QC_InspectionItems
                        {
                            QCInspectionId = qc.Id,
                            WorkOrderItemId = comp.Id,
                            ProductionId = prod.Id,
                            CompId = comp.CompId,

                            Qty = 0,
                            PassedQty = 0,
                            FailedQty = 0,
                            ReworkQty = 0,
                            Result = "Pending",

                            Remarks = "Auto-created from QC batch.",
                            CreatedBy = userId,
                            CreatedDate = now,
                            UpdatedBy = userId,
                            UpdatedDate = now,
                            Active = true,
                            Deleted = false
                        });
                    }

                    var relatedProductionRows = db.DailyProduction
                        .Include("WorkOrderItems")
                        .Where(x =>
                            (x.Deleted == false || x.Deleted == null) &&
                            (x.IsReversed == false || x.IsReversed == null) &&
                            x.IsPosted == true &&
                            (x.IsSentToQC == false || x.IsSentToQC == null) &&
                            x.WorkOrderItems != null &&
                            x.WorkOrderItems.WorkOrderId == workOrderId &&
                            x.WorkOrderItems.DesignId == designId &&
                            x.WorkOrderItems.ProductId == productId)
                        .ToList();

                    foreach (var row in relatedProductionRows)
                    {
                        row.IsSentToQC = true;
                        row.SentToQCBy = userId;
                        row.SentToQCDate = now;
                        row.Status = "SentToQC";
                        row.UpdatedBy = userId;
                        row.UpdatedDate = now;
                        row.RecordTimeStamp = now;
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogDpAudit(id, "SENT_TO_QC",
                        oldValue: DailyProductionStatus.POSTED_TO_WIP,
                        newValue: DailyProductionStatus.SENT_TO_QC,
                        detail: "QC #" + qc.Id + " " + qc.QCNo + " (batch qty " + qc.BatchQty + ")");

                    return JsonSuccess("QC batch created successfully.", new
                    {
                        qcId = qc.Id,
                        qcNo = qc.QCNo,
                        batchQty = qc.BatchQty
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new
                    {
                        success = false,
                        message = "Failed to create QC batch.",
                        detail = DescribeDpError(ex)
                    }, JsonRequestBehavior.AllowGet);
                }
            }
        }

        // =========================================================
        // REVERSE DAILY PRODUCTION
        // =========================================================
        [HttpPost]
        [Route("reverse/{id:int}")]
        public ActionResult Reverse(int id, ReverseDailyProductionDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: serialise on the row UPDLOCK — concurrent
                    // Reverse and Post calls on the same row can otherwise
                    // race against AccrueActualCostToWorkOrderItem and
                    // leave the parent WO with double-counted cost.
                    LockDpRow(id);

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    string reason = dto != null && !string.IsNullOrWhiteSpace(dto.Reason)
                        ? dto.Reason.Trim()
                        : "";

                    var dp = db.DailyProduction
                        .Include(x => x.WorkOrderItems)
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (dp == null)
                        return JsonError("Daily production record not found.");

                    if (dp.IsPosted != true)
                        return JsonError("Only posted production can be reversed.");

                    if (dp.IsReversed == true)
                        return JsonError("Production already reversed.");

                    if (dp.IsSentToQC == true)
                        return JsonError("Production already sent to QC and cannot be reversed.");

                    bool hasQc = false;
                    if (dp.WorkOrderItems != null)
                    {
                        int workOrderId = dp.WorkOrderItems.WorkOrderId;
                        int? designId = dp.WorkOrderItems.DesignId;
                        int? productId = dp.WorkOrderItems.ProductId;

                        hasQc = db.QC_Inspection.Any(q =>
                            q.WorkOrderId == workOrderId &&
                            q.DesignId == designId &&
                            q.ProductId == productId &&
                            q.Deleted != true &&
                            q.Status != "Reversed" &&
                            q.Status != "Cancelled");
                    }

                    if (hasQc)
                        return JsonError("QC batch already exists for this product. Reverse is not allowed.");

                    int reversedAcceptedQty = dp.AcceptedQty ?? 0;

                    dp.IsReversed = true;
                    dp.IsSentToQC = false;
                    dp.QCCompleted = false;
                    dp.SentToQCBy = null;
                    dp.SentToQCDate = null;
                    dp.Status = "Reversed";
                    dp.UpdatedBy = userId;
                    dp.UpdatedDate = now;
                    dp.RecordTimeStamp = now;

                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        dp.Remarks = string.IsNullOrWhiteSpace(dp.Remarks)
                            ? "Reversed: " + reason
                            : dp.Remarks + " | Reversed: " + reason;
                    }

                    db.SaveChanges();

                    // ❶-REV Phase S.1 — deduct the cost that was accrued at PostToWip time.
                    // Recomputes from the same MachineCostProfile + productive hours formula
                    // and subtracts. Works correctly as long as the profile hasn't been
                    // rate-edited between post and reverse.
                    AccrueActualCostToWorkOrderItem(dp, reversedAcceptedQty, /*sign*/ -1);
                    db.SaveChanges();

                    if (dp.WorkOrderItemId.HasValue)
                    {
                        UpdateWorkOrderItemProgress(dp.WorkOrderItemId.Value);
                        db.SaveChanges();
                    }

                    tx.Commit();

                    LogDpAudit(dp.Id, "REVERSED",
                        oldValue: DailyProductionStatus.POSTED_TO_WIP,
                        newValue: DailyProductionStatus.REVERSED,
                        detail: string.IsNullOrWhiteSpace(reason) ? null : ("Reason: " + reason));

                    return JsonSuccess("Production reversed successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to reverse production. " + DescribeDpError(ex));
                }
            }
        }

        // =========================================================
        // HELPERS
        // =========================================================

        // ================================================================
        // Rework loop helpers — the parent QC inspection id is persisted
        // in DailyProduction.Remarks as "[QC:123]". This avoids a schema
        // change while still letting us route Post-time effects back to
        // the right QC row.
        // ================================================================
        private string BuildDpRemarks(string userRemarks, int? sourceQcId)
        {
            string body = string.IsNullOrWhiteSpace(userRemarks) ? null : userRemarks.Trim();
            if (!sourceQcId.HasValue || sourceQcId.Value <= 0) return body;

            string tag = "[QC:" + sourceQcId.Value + "]";
            return string.IsNullOrWhiteSpace(body) ? tag : (body + " " + tag);
        }

        private int? ExtractQcIdFromRemarks(string remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks)) return null;

            const string marker = "[QC:";
            int idx = remarks.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            int start = idx + marker.Length;
            int end = remarks.IndexOf(']', start);
            if (end <= start) return null;

            int parsed;
            if (int.TryParse(remarks.Substring(start, end - start), out parsed) && parsed > 0)
                return parsed;

            return null;
        }

        // ❶ Phase S.1 — accrue (or deduct) ACTUAL production cost from a
        // DailyProduction row to its parent WorkOrderItem.
        //   sign = +1 → add (PostToWip)
        //   sign = -1 → subtract (Reverse)
        // CostPerUnit is recomputed as TotalCost / CompletedQty after.
        // acceptedQtyDelta is the qty being added/removed from CompletedQty
        // (caller passes the row's accepted qty; sign is applied internally).
        private void AccrueActualCostToWorkOrderItem(DailyProduction dp, int acceptedQtyDelta, int sign = 1)
        {
            if (!dp.WorkOrderItemId.HasValue) return;
            var item = db.WorkOrderItems
                .FirstOrDefault(x => x.Id == dp.WorkOrderItemId.Value
                                  && (x.Deleted == false || x.Deleted == null));
            if (item == null) return;
            if (!dp.MachineId.HasValue) return;

            // Latest active cost profile for this machine
            var profile = db.MachineCostProfile
                .Where(c => c.MachineId == dp.MachineId.Value
                         && c.Deleted != true
                         && (c.Active ?? true) == true)
                .OrderByDescending(c => c.Id)
                .FirstOrDefault();
            if (profile == null) return;

            // Productive minutes = (end − start) − sum(stops)
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

            decimal deltaMachine = Math.Round(mh * productiveH, 2) * sign;
            decimal deltaOperator = Math.Round(oh * productiveH, 2) * sign;
            decimal deltaElectric = Math.Round(eh * productiveH, 2) * sign;
            decimal deltaTotal = deltaMachine + deltaOperator + deltaElectric;

            item.MachineCost = Math.Max(0m, (item.MachineCost ?? 0m) + deltaMachine);
            item.LaborCost = Math.Max(0m, (item.LaborCost ?? 0m) + deltaOperator);
            item.ElectricityCost = Math.Max(0m, (item.ElectricityCost ?? 0m) + deltaElectric);
            item.TotalCost = Math.Max(0m, (item.TotalCost ?? 0m) + deltaTotal);

            // Recompute CostPerUnit from running totals over (CompletedQty + qtyDelta × sign)
            int completed = item.CompletedQty + (acceptedQtyDelta * sign);
            item.CostPerUnit = completed > 0
                ? Math.Round((item.TotalCost ?? 0m) / completed, 4)
                : 0m;
        }

        private void UpdateWorkOrderItemProgress(int workOrderItemId)
        {
            var now = DateTime.Now;
            int userId = GetCurrentUserId();

            var item = db.WorkOrderItems
                .FirstOrDefault(x => x.Id == workOrderItemId
                                  && (x.Deleted == false || x.Deleted == null));

            if (item == null)
                return;

            var productionRows = db.DailyProduction
                .Where(x => x.WorkOrderItemId == workOrderItemId
                         && (x.Deleted == false || x.Deleted == null)
                         && (x.IsReversed == false || x.IsReversed == null))
                .ToList();

            var reversedQcProductionIds = db.QC_Inspection
                .Where(q =>
                    (q.Deleted == false || q.Deleted == null) &&
                    q.Status == "Reversed" &&
                    q.ProductionId.HasValue)
                .Select(q => q.ProductionId.Value)
                .ToList();

            productionRows = productionRows
                .Where(x => !reversedQcProductionIds.Contains(x.Id))
                .ToList();

            int completedQty = productionRows.Sum(x => x.AcceptedQty ?? 0);
            int rejectedQty = productionRows.Sum(x => x.RejectedQty ?? 0);

            item.CompletedQty = completedQty;
            item.RejectedQty = rejectedQty;

            if (completedQty <= 0)
                item.Status = "Pending";
            else if (completedQty < item.Quantity)
                item.Status = "In Production";
            else
                item.Status = "Production Complete";

            item.UpdatedBy = userId;
            item.UpdatedDate = now;
        }

        private int GetAvailableFinishedQtyForQc(int workOrderId, int designId, int productId)
        {
            var requiredItems = db.WorkOrderItems
                .Where(x =>
                    x.WorkOrderId == workOrderId &&
                    x.DesignId == designId &&
                    x.ProductId == productId &&
                    (x.Deleted == false || x.Deleted == null))
                .Select(x => new
                {
                    x.CompId,
                    CompletedQty = x.CompletedQty
                })
                .ToList();

            var requiredComponentIds = requiredItems
                .Where(x => x.CompId.HasValue && x.CompId.Value > 0)
                .Select(x => x.CompId.Value)
                .Distinct()
                .ToList();

            if (!requiredComponentIds.Any())
                return 0;

            var completedByComponent = requiredItems
                .Where(x => x.CompId.HasValue && requiredComponentIds.Contains(x.CompId.Value))
                .GroupBy(x => x.CompId.Value)
                .Select(g => new
                {
                    CompId = g.Key,
                    CompletedQty = g.Sum(x => x.CompletedQty)
                })
                .ToList();

            if (completedByComponent.Count != requiredComponentIds.Count)
                return 0;

            int finishedQty = completedByComponent.Min(x => x.CompletedQty);
            if (finishedQty <= 0)
                return 0;

            int alreadySentToQcQty = db.QC_Inspection
                .Where(q =>
                    q.WorkOrderId == workOrderId &&
                    q.DesignId == designId &&
                    q.ProductId == productId &&
                    (q.Deleted == false || q.Deleted == null) &&
                    q.Status != "Reversed" &&
                    q.Status != "Cancelled")
                .Sum(q => (int?)q.BatchQty) ?? 0;

            int availableForQc = finishedQty - alreadySentToQcQty;
            return availableForQc > 0 ? availableForQc : 0;
        }

        private string GenerateQcNo()
        {
            var existingCodes = db.QC_Inspection
                .Where(x => x.QCNo != null && x.QCNo.StartsWith("QC-"))
                .Select(x => x.QCNo)
                .ToList();

            int maxNo = 0;

            foreach (var code in existingCodes)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var parts = code.Split('-');
                if (parts.Length != 2)
                    continue;

                int parsed;
                if (int.TryParse(parts[1], out parsed) && parsed > maxNo)
                    maxNo = parsed;
            }

            return "QC-" + (maxNo + 1).ToString("000000");
        }

        [HttpGet]
        [Route("current-operator")]
        public ActionResult CurrentOperator()
        {
            try
            {
                int userId = GetCurrentUserId();

                var user = db.Adm_Users
                    .FirstOrDefault(x => x.Id == userId && (x.Deleted == false || x.Deleted == null));

                if (user == null)
                    return JsonError("Current operator not found.");

                var data = new
                {
                    id = user.Id,
                    name = user.UserName ?? "",
                    employeeId = user.EmployeeId,
                    label = user.EmployeeId.HasValue
                        ? ("EMP-" + user.EmployeeId.Value + " - " + (user.UserName ?? ""))
                        : (user.UserName ?? "")
                };

                return JsonSuccess("Current operator loaded successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load current operator.", ex);
            }
        }

        private string ToDateTimeLocalString(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-ddTHH:mm")
                : null;
        }

        [HttpGet]
        [Route("ddl-workorders")]
        public ActionResult GetWorkOrdersForProduction(int? branchId = null)
        {
            try
            {
                var q = db.WorkOrders
                    .Where(x =>
                        x.Status == "Material Issued" &&
                        (x.Deleted == false || x.Deleted == null));

                if (branchId.HasValue && branchId.Value > 0)
                    q = q.Where(x => x.BranchId == branchId.Value);

                var list = q
                    .OrderByDescending(x => x.Id)
                    .Select(x => new
                    {
                        id = x.Id,
                        text = x.WorkOrderCode
                    })
                    .ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work orders. " + ex.Message);
            }
        }

        private ActionResult ValidateProductionTime(DailyProductionInputViewModel dto)
        {
            if (dto.StartTime.HasValue && dto.EndTime.HasValue)
            {
                if (dto.EndTime.Value <= dto.StartTime.Value)
                    return JsonError("End Time must be greater than Start Time.");

                var hours = (dto.EndTime.Value - dto.StartTime.Value).TotalHours;

                if (hours > 24)
                    return JsonError("Production duration cannot exceed 24 hours. For multi-day work, add one entry per day.");

                // One row = one shift on one day. Allow same-day or a single
                // shift that crosses midnight (night shift), nothing wider.
                var startDate = dto.StartTime.Value.Date;
                var endDate = dto.EndTime.Value.Date;
                if (endDate > startDate.AddDays(1))
                    return JsonError("A Daily Production entry must be within one day (a single shift, optionally crossing midnight). For multi-day work, add one entry per day.");
            }

            return null;
        }

        // =========================================================
        // EXPORT EXCEL
        // =========================================================
        [HttpGet]
        [Route("export-excel")]
        public FileResult ExportExcel()
        {
            var raw = db.DailyProduction
                .Where(x => x.Deleted == false || x.Deleted == null)
                .Include("WorkOrderItems.Gen_Design")
                .Include("WorkOrderItems.Gen_Products")
                .Include("Gen_Machines")
                .Include("Adm_Users")
                .ToList()
                .Select(x => new
                {
                    Id = x.Id,
                    WorkOrder = x.WorkOrderItems != null ? "WO-" + x.WorkOrderItems.WorkOrderId : "",
                    Product = x.WorkOrderItems != null && x.WorkOrderItems.Gen_Products != null
                        ? x.WorkOrderItems.Gen_Products.Prod_Name
                        : "",
                    Design = x.WorkOrderItems != null && x.WorkOrderItems.Gen_Design != null
                        ? x.WorkOrderItems.Gen_Design.DNo
                        : "",
                    Machine = x.Gen_Machines != null ? x.Gen_Machines.Serial_Number : "",
                    Operator = x.Adm_Users != null ? x.Adm_Users.UserName : "",
                    Shift = x.Shift,
                    StartTime = x.StartTime.HasValue ? x.StartTime.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    EndTime = x.EndTime.HasValue ? x.EndTime.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    PlannedQty = x.PlannedQty,
                    ActualQty = x.ActualQty,
                    RejectedQty = x.RejectedQty,
                    AcceptedQty = x.AcceptedQty,
                    Status = x.Status,
                    IsPosted = x.IsPosted,
                    IsSentToQC = x.IsSentToQC,
                    Remarks = x.Remarks ?? "",
                    CreatedDate = x.CreatedDate.HasValue ? x.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    UpdatedDate = x.UpdatedDate.HasValue ? x.UpdatedDate.Value.ToString("yyyy-MM-dd HH:mm") : ""
                })
                .ToList();

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Daily Production");

                string[] headers = new[]
                {
                    "Id", "WorkOrder", "Product", "Design", "Machine", "Operator", "Shift",
                    "StartTime", "EndTime", "PlannedQty", "ActualQty", "RejectedQty", "AcceptedQty",
                    "Status", "IsPosted", "IsSentToQC", "Remarks", "CreatedDate", "UpdatedDate"
                };
                for (int c = 0; c < headers.Length; c++)
                    ws.Cells[1, c + 1].Value = headers[c];

                using (var hr = ws.Cells[1, 1, 1, headers.Length])
                {
                    hr.Style.Font.Bold = true;
                    hr.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    hr.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                int r = 2;
                foreach (var item in raw)
                {
                    ws.Cells[r,  1].Value = item.Id;
                    ws.Cells[r,  2].Value = item.WorkOrder ?? "";
                    ws.Cells[r,  3].Value = item.Product ?? "";
                    ws.Cells[r,  4].Value = item.Design ?? "";
                    ws.Cells[r,  5].Value = item.Machine ?? "";
                    ws.Cells[r,  6].Value = item.Operator ?? "";
                    ws.Cells[r,  7].Value = item.Shift ?? "";
                    ws.Cells[r,  8].Value = item.StartTime ?? "";
                    ws.Cells[r,  9].Value = item.EndTime ?? "";
                    ws.Cells[r, 10].Value = item.PlannedQty ?? 0;
                    ws.Cells[r, 11].Value = item.ActualQty ?? 0;
                    ws.Cells[r, 12].Value = item.RejectedQty ?? 0;
                    ws.Cells[r, 13].Value = item.AcceptedQty ?? 0;
                    ws.Cells[r, 14].Value = item.Status ?? "";
                    ws.Cells[r, 15].Value = item.IsPosted;
                    ws.Cells[r, 16].Value = item.IsSentToQC;
                    ws.Cells[r, 17].Value = item.Remarks ?? "";
                    ws.Cells[r, 18].Value = item.CreatedDate ?? "";
                    ws.Cells[r, 19].Value = item.UpdatedDate ?? "";
                    r++;
                }

                if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(
                    package.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "DailyProduction.xlsx"
                );
            }
        }

        // =========================================================
        // EXPORT PDF
        // =========================================================
        [HttpGet]
        [Route("export-pdf")]
        public ActionResult ExportPdf()
        {
            var list = db.DailyProduction
                .Where(x => x.Deleted == false || x.Deleted == null)
                .Include("WorkOrderItems.Gen_Design")
                .Include("WorkOrderItems.Gen_Products")
                .Include("Gen_Machines")
                .Include("Adm_Users")
                .ToList();

            using (var ms = new MemoryStream())
            {
                var document = new Document(PageSize.A4.Rotate(), 20, 20, 20, 20);
                PdfWriter.GetInstance(document, ms);

                document.Open();

                var title = new Paragraph("Daily Production Report",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14))
                {
                    Alignment = Element.ALIGN_CENTER,
                    SpacingAfter = 15f
                };

                document.Add(title);

                var table = new PdfPTable(12);
                table.WidthPercentage = 100;

                string[] headers =
                {
                    "ID", "WO", "Product", "Design", "Machine", "Operator",
                    "Shift", "Actual", "Rejected", "Accepted", "Status", "Posted"
                };

                foreach (var h in headers)
                {
                    table.AddCell(new PdfPCell(new Phrase(h))
                    {
                        BackgroundColor = BaseColor.LIGHT_GRAY
                    });
                }

                foreach (var x in list)
                {
                    table.AddCell(x.Id.ToString());
                    table.AddCell(x.WorkOrderItems != null ? "WO-" + x.WorkOrderItems.WorkOrderId : "-");
                    table.AddCell(x.WorkOrderItems?.Gen_Products?.Prod_Name ?? "-");
                    table.AddCell(x.WorkOrderItems?.Gen_Design?.DNo ?? "-");
                    table.AddCell(x.Gen_Machines?.Serial_Number ?? "-");
                    table.AddCell(x.Adm_Users?.UserName ?? "-");
                    table.AddCell(x.Shift ?? "-");
                    table.AddCell((x.ActualQty ?? 0).ToString());
                    table.AddCell((x.RejectedQty ?? 0).ToString());
                    table.AddCell((x.AcceptedQty ?? 0).ToString());
                    table.AddCell(x.Status ?? "-");
                    table.AddCell((x.IsPosted == true ? "Yes" : "No"));
                }

                document.Add(table);
                document.Close();

                return File(ms.ToArray(), "application/pdf", "DailyProduction.pdf");
            }
        }        
        // =========================================================
        // PRINT
        // =========================================================
        [HttpGet]
        [Route("print")]
        public ActionResult Print()
        {
            var list = db.DailyProduction
                .Where(x => x.Deleted == false || x.Deleted == null)
                .Include("WorkOrderItems.Gen_Design")
                .Include("WorkOrderItems.Gen_Products")
                .Include("Gen_Machines")
                .Include("Adm_Users")
                .ToList();

            var html = @"
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Daily Production Print</title>
                    <style>
                        body { font-family: Arial; font-size: 12px; }
                        table { width: 100%; border-collapse: collapse; }
                        th, td { border: 1px solid #333; padding: 6px; text-align: left; }
                        th { background: #f0f0f0; }
                        h2 { text-align: center; }
                    </style>
                </head>
                <body>
                    <h2>Daily Production Report</h2>
                    <table>
                        <thead>
                            <tr>
                                <th>ID</th>
                                <th>Work Order</th>
                                <th>Product</th>
                                <th>Design</th>
                                <th>Machine</th>
                                <th>Operator</th>
                                <th>Shift</th>
                                <th>Actual</th>
                                <th>Rejected</th>
                                <th>Accepted</th>
                                <th>Status</th>
                            </tr>
                        </thead>
                        <tbody>";

                            foreach (var x in list)
                            {
                                html += $@"
                            <tr>
                                <td>{x.Id}</td>
                                <td>{"WO-" + (x.WorkOrderItems != null ? x.WorkOrderItems.WorkOrderId.ToString() : "-")}</td>
                                <td>{(x.WorkOrderItems?.Gen_Products?.Prod_Name ?? "-")}</td>
                                <td>{(x.WorkOrderItems?.Gen_Design?.DNo ?? "-")}</td>
                                <td>{(x.Gen_Machines?.Serial_Number ?? "-")}</td>
                                <td>{(x.Adm_Users?.UserName ?? "-")}</td>
                                <td>{(x.Shift ?? "-")}</td>
                                <td>{(x.ActualQty ?? 0)}</td>
                                <td>{(x.RejectedQty ?? 0)}</td>
                                <td>{(x.AcceptedQty ?? 0)}</td>
                                <td>{(x.Status ?? "-")}</td>
                            </tr>";
                            }

                            html += @"
                        </tbody>
                    </table>
                    <script>window.onload = function(){ window.print(); };</script>
                </body>
            </html>";

            return Content(html, "text/html");
        }

        private DateTime? ParseDate(object value)
        {
            if (value == null) return null;

            var str = value.ToString();
            if (string.IsNullOrWhiteSpace(str)) return null;

            DateTime dt;
            if (DateTime.TryParse(str, out dt))
                return dt;

            return null;
        }
    }
}