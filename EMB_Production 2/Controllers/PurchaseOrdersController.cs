using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    // ============================================================
    // Purchase Order (PO)
    // ------------------------------------------------------------
    // Issued to suppliers after PR approval. Supports partial
    // receipts: one PO can have many GRNs (existing Purchases
    // rows tagged with PurchaseOrderId).
    //
    // Lifecycle:
    //   DRAFT            – auto-created from PR; rates/terms editable
    //   SENT             – formally sent to supplier
    //   PARTIALLY_RECEIVED – at least one GRN posted, less than full
    //   FULLY_RECEIVED   – all line ReceivedQty >= OrderedQty
    //   CLOSED           – marked closed by Purchase Manager
    //   CANCELLED        – revoked with reason
    //
    // Tables created by App_Data/Scripts/CreatePurchaseRequisitionAndOrder.sql.
    // ============================================================
    [RoutePrefix("emb-products/purchase-order")]
    public class PurchaseOrdersController : BaseController
    {
        public class PoRow
        {
            public int    Id { get; set; }
            public string PoCode { get; set; }
            public int?   PrId { get; set; }
            public string PrCode { get; set; }
            public int?   BranchId { get; set; }
            public string BranchName { get; set; }
            public int    SupplierId { get; set; }
            public string SupplierName { get; set; }
            public DateTime OrderDate { get; set; }
            public DateTime? ExpectedDeliveryDate { get; set; }
            public string Status { get; set; }
            public decimal TotalAmount { get; set; }
            public decimal ReceivedAmount { get; set; }
            public string PaymentTerms { get; set; }
            public string DeliveryTerms { get; set; }
            public string Remarks { get; set; }
            public int?   SentBy { get; set; }
            public DateTime? SentDate { get; set; }
            public DateTime? ClosedDate { get; set; }
            public DateTime? CancelledDate { get; set; }
            public string CancellationReason { get; set; }
            public bool?  Active { get; set; }
            public DateTime? CreatedDate { get; set; }
            public DateTime? UpdatedDate { get; set; }
        }

        public class PoItemRow
        {
            public int     Id { get; set; }
            public int     PoId { get; set; }
            public int?    PrItemId { get; set; }
            public int     RawItemId { get; set; }
            public string  RawItemName { get; set; }
            public int?    UnitId { get; set; }
            public string  UnitCode { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal ReceivedQty { get; set; }
            public string  Remarks { get; set; }
        }

        // ====================================================================
        // LIST
        // ====================================================================
        // ====================================================================
        //  SPRINT-1  STATUS CONSTANTS
        // --------------------------------------------------------------------
        //  Centralised so a typo in one endpoint never silently breaks
        //  another. Values match the DB exactly (UPPER_SNAKE).
        // ====================================================================
        private static class PoStatus
        {
            public const string Draft             = "DRAFT";
            public const string Sent              = "SENT";
            public const string PartiallyReceived = "PARTIALLY_RECEIVED";
            public const string FullyReceived     = "FULLY_RECEIVED";
            public const string Closed            = "CLOSED";
            public const string Cancelled         = "CANCELLED";
        }

        [HttpGet, Route("list")]
        public ActionResult List(
            string status = "", int? branchId = null, int? supplierId = null,
            string search = "", string showDeleted = "",
            // Sprint-1: date range filter on OrderDate. fromDate inclusive,
            // toDate end-of-day inclusive.
            string fromDate = null, string toDate = null,
            // Sprint-3: exact PR-code filter (used by deep-links from the
            // PR module). The free-text search already includes pr.PrCode,
            // but an exact filter is cleaner for share-able URLs.
            string prCode = null)
        {
            try
            {
                string deletedClause = string.Equals(showDeleted, "yes", StringComparison.OrdinalIgnoreCase)
                    ? " (po.Deleted = 1) "
                    : " (po.Deleted = 0 OR po.Deleted IS NULL) ";

                var parameters = new List<SqlParameter>();
                string where = " WHERE " + deletedClause;

                if (!string.IsNullOrWhiteSpace(status))
                {
                    where += " AND po.Status = @status ";
                    parameters.Add(new SqlParameter("@status", status.Trim().ToUpper()));
                }
                if (branchId.HasValue && branchId.Value > 0)
                {
                    where += " AND po.BranchId = @brId ";
                    parameters.Add(new SqlParameter("@brId", branchId.Value));
                }
                if (supplierId.HasValue && supplierId.Value > 0)
                {
                    where += " AND po.SupplierId = @supId ";
                    parameters.Add(new SqlParameter("@supId", supplierId.Value));
                }
                if (!string.IsNullOrWhiteSpace(search))
                {
                    where += " AND (po.PoCode LIKE @s OR po.Remarks LIKE @s OR s.SupplierName LIKE @s OR pr.PrCode LIKE @s) ";
                    parameters.Add(new SqlParameter("@s", "%" + search.Trim() + "%"));
                }

                // Sprint-1: date range filter on OrderDate.
                DateTime fromDt, toDt;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out fromDt))
                {
                    where += " AND po.OrderDate >= @fromDt ";
                    parameters.Add(new SqlParameter("@fromDt", fromDt.Date));
                }
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out toDt))
                {
                    where += " AND po.OrderDate <= @toDt ";
                    parameters.Add(new SqlParameter("@toDt", toDt.Date.AddDays(1).AddTicks(-1)));
                }

                // Sprint-3: exact PR-code match (deep-link friendly).
                if (!string.IsNullOrWhiteSpace(prCode))
                {
                    where += " AND pr.PrCode = @prCode ";
                    parameters.Add(new SqlParameter("@prCode", prCode.Trim()));
                }

                string sql = @"
SELECT  po.Id, po.PoCode,
        po.PrId, pr.PrCode AS PrCode,
        po.BranchId, b.Name AS BranchName,
        po.SupplierId, s.SupplierName,
        po.OrderDate, po.ExpectedDeliveryDate,
        po.Status, po.TotalAmount, po.ReceivedAmount,
        po.PaymentTerms, po.DeliveryTerms, po.Remarks,
        po.SentBy, po.SentDate, po.ClosedDate, po.CancelledDate, po.CancellationReason,
        po.Active, po.CreatedDate, po.UpdatedDate
FROM    dbo.PurchaseOrder po
LEFT JOIN dbo.PurchaseRequisition pr ON pr.Id = po.PrId
LEFT JOIN dbo.Branches             b  ON b.Id  = po.BranchId
LEFT JOIN dbo.Gen_Suppliers        s  ON s.Id  = po.SupplierId
" + where + " ORDER BY po.Id DESC ";

                var rows = db.Database.SqlQuery<PoRow>(sql, parameters.ToArray()).ToList();

                // Sprint-1: compute "today" once for the overdue calc so
                // every row uses the same reference date.
                DateTime today = DateTime.Today;

                // Re-project to camelCase. JsonSuccess uses JavaScriptSerializer
                // which preserves PascalCase, but the Angular DTOs are camelCase
                // — without this projection every field renders as undefined.
                var items = rows.Select(r => new
                {
                    id                   = r.Id,
                    poCode               = r.PoCode,
                    prId                 = r.PrId,
                    prCode               = r.PrCode,
                    branchId             = r.BranchId,
                    branchName           = r.BranchName,
                    supplierId           = r.SupplierId,
                    supplierName         = r.SupplierName,
                    orderDate            = r.OrderDate.ToString("yyyy-MM-dd"),
                    expectedDeliveryDate = r.ExpectedDeliveryDate.HasValue
                                              ? r.ExpectedDeliveryDate.Value.ToString("yyyy-MM-dd")
                                              : null,
                    status               = r.Status,
                    totalAmount          = r.TotalAmount,
                    receivedAmount       = r.ReceivedAmount,

                    // Sprint-1 enrichments:
                    //  receivedPercent — capped at 100; 0 when total is 0.
                    //  overdueDays     — positive integer when Expected is in
                    //                    the past AND the PO is still open
                    //                    (not Fully Received / Closed / Cancelled).
                    //                    Null when on track or no expected date.
                    receivedPercent = (r.TotalAmount > 0)
                                        ? Math.Min(100m, Math.Round(r.ReceivedAmount * 100m / r.TotalAmount, 0))
                                        : 0m,
                    overdueDays    = (r.ExpectedDeliveryDate.HasValue
                                       && r.ExpectedDeliveryDate.Value.Date < today
                                       && r.Status != PoStatus.FullyReceived
                                       && r.Status != PoStatus.Closed
                                       && r.Status != PoStatus.Cancelled)
                                        ? (int?)(today - r.ExpectedDeliveryDate.Value.Date).TotalDays
                                        : null,
                    paymentTerms         = r.PaymentTerms,
                    deliveryTerms        = r.DeliveryTerms,
                    remarks              = r.Remarks,
                    sentBy               = r.SentBy,
                    sentDate             = r.SentDate.HasValue
                                              ? r.SentDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    closedDate           = r.ClosedDate.HasValue
                                              ? r.ClosedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    cancelledDate        = r.CancelledDate.HasValue
                                              ? r.CancelledDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    cancellationReason   = r.CancellationReason,
                    active               = r.Active,
                    createdDate          = r.CreatedDate.HasValue
                                              ? r.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    updatedDate          = r.UpdatedDate.HasValue
                                              ? r.UpdatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null
                }).ToList();

                var summary = new
                {
                    count               = rows.Count,
                    draftCount          = rows.Count(r => r.Status == PoStatus.Draft),
                    sentCount           = rows.Count(r => r.Status == PoStatus.Sent),
                    partialCount        = rows.Count(r => r.Status == PoStatus.PartiallyReceived),
                    fullyReceivedCount  = rows.Count(r => r.Status == PoStatus.FullyReceived),
                    closedCount         = rows.Count(r => r.Status == PoStatus.Closed),
                    cancelledCount      = rows.Count(r => r.Status == PoStatus.Cancelled),
                    totalValue          = rows.Sum(r => r.TotalAmount),
                    receivedValue       = rows.Sum(r => r.ReceivedAmount)
                };

                return JsonSuccess("OK", new { items, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load PO list.", ex);
            }
        }

        // ====================================================================
        // DETAIL — header + items + linked GRNs (Purchases)
        // ====================================================================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var h = LoadHeader(id);
                if (h == null) return JsonError("PO not found.");

                var rawItems = LoadItems(id);

                // Linked GRNs — Purchases.PurchaseOrderId = this PO
                // Each line gets a running shipped qty so the UI can show
                // received-by-line. The PO-level ReceivedAmount/Status
                // are maintained on every GRN save (see /sync-receipts).
                string grnSql = @"
SELECT  p.Id,
        p.PurchaseDate AS GrnDate,
        p.InvoiceNo,
        p.IsPosted,
        (SELECT COUNT(*) FROM dbo.PurchaseItems pi WHERE pi.PurchaseId = p.Id AND pi.Deleted = 0) AS ItemCount,
        (SELECT SUM(pi.Qty * ISNULL(pi.Rate,0)) FROM dbo.PurchaseItems pi WHERE pi.PurchaseId = p.Id AND pi.Deleted = 0) AS Amount
FROM    dbo.Purchase p
WHERE   p.PurchaseOrderId = @id
  AND   (p.Deleted = 0 OR p.Deleted IS NULL)
ORDER BY p.Id DESC";

                var rawGrns = db.Database.SqlQuery<GrnSummaryRow>(grnSql, new SqlParameter("@id", id)).ToList();

                // Re-project all three payloads to camelCase. See /list for why.
                //
                // Dates: JavaScriptSerializer emits DateTime as /Date(ms)/, which
                // Angular's `| date` pipe and <input type="date"> can't read.
                // Pre-format to ISO yyyy-MM-dd (for date-only fields wired to
                // input[type=date] or the date pipe) and ISO 8601 (for stamps).
                var header = new
                {
                    id                   = h.Id,
                    poCode               = h.PoCode,
                    prId                 = h.PrId,
                    prCode               = h.PrCode,
                    branchId             = h.BranchId,
                    branchName           = h.BranchName,
                    supplierId           = h.SupplierId,
                    supplierName         = h.SupplierName,
                    orderDate            = h.OrderDate.ToString("yyyy-MM-dd"),
                    expectedDeliveryDate = h.ExpectedDeliveryDate.HasValue
                                              ? h.ExpectedDeliveryDate.Value.ToString("yyyy-MM-dd")
                                              : null,
                    status               = h.Status,
                    totalAmount          = h.TotalAmount,
                    receivedAmount       = h.ReceivedAmount,
                    paymentTerms         = h.PaymentTerms,
                    deliveryTerms        = h.DeliveryTerms,
                    remarks              = h.Remarks,
                    sentBy               = h.SentBy,
                    sentDate             = h.SentDate.HasValue
                                              ? h.SentDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    closedDate           = h.ClosedDate.HasValue
                                              ? h.ClosedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    cancelledDate        = h.CancelledDate.HasValue
                                              ? h.CancelledDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    cancellationReason   = h.CancellationReason,
                    active               = h.Active,
                    createdDate          = h.CreatedDate.HasValue
                                              ? h.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null,
                    updatedDate          = h.UpdatedDate.HasValue
                                              ? h.UpdatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss")
                                              : null
                };

                var items = rawItems.Select(i => new
                {
                    id          = i.Id,
                    poId        = i.PoId,
                    prItemId    = i.PrItemId,
                    rawItemId   = i.RawItemId,
                    rawItemName = i.RawItemName,
                    unitId      = i.UnitId,
                    unitCode    = i.UnitCode,
                    orderedQty  = i.OrderedQty,
                    rate        = i.Rate,
                    amount      = i.Amount,
                    receivedQty = i.ReceivedQty,
                    outstanding = i.OrderedQty - i.ReceivedQty,
                    remarks     = i.Remarks
                }).ToList();

                var grns = rawGrns.Select(g => new
                {
                    id        = g.Id,
                    grnDate   = g.GrnDate.ToString("yyyy-MM-dd"),
                    invoiceNo = g.InvoiceNo,
                    isPosted  = g.IsPosted,
                    itemCount = g.ItemCount,
                    amount    = g.Amount
                }).ToList();

                return JsonSuccess("OK", new
                {
                    header, items, grns,
                    // Sprint-2: workflow stepper data. workflowStages is
                    // the canonical lifecycle list (Cancelled lives off
                    // to the side as the terminal red sibling); the index
                    // tells the stepper UI which dot to fill.
                    workflowStages     = _poWorkflowStages,
                    workflowStageIndex = ComputePoWorkflowStageIndex(h.Status)
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load PO detail.", ex);
            }
        }

        public class GrnSummaryRow
        {
            public int      Id { get; set; }
            public DateTime GrnDate { get; set; }
            public string   InvoiceNo { get; set; }
            public bool     IsPosted { get; set; }
            public int      ItemCount { get; set; }
            public decimal? Amount { get; set; }
        }

        // ====================================================================
        // UPDATE — edit terms / rates while still DRAFT
        // ====================================================================
        public class PoUpdateDto
        {
            public int Id { get; set; }
            public DateTime? ExpectedDeliveryDate { get; set; }
            public string PaymentTerms { get; set; }
            public string DeliveryTerms { get; set; }
            public string Remarks { get; set; }
            public List<PoLineUpdateDto> Items { get; set; }
        }
        public class PoLineUpdateDto
        {
            public int Id { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal Rate { get; set; }
            public string Remarks { get; set; }
        }

        [HttpPost, Route("update")]
        public ActionResult Update(PoUpdateDto dto)
        {
            if (dto == null || dto.Id <= 0) return JsonError("Invalid request.");

            // Sprint-4: per-row item validation upfront so bad lines surface
            // with their row number instead of being silently dropped by the
            // existing `if (i.Id <= 0 || i.OrderedQty <= 0 || i.Rate < 0) continue;`
            // skip in the update loop. Same pattern PR Update got in PR Sprint 4.
            if (dto.Items != null)
            {
                var problems = new List<string>();
                for (int idx = 0; idx < dto.Items.Count; idx++)
                {
                    var i = dto.Items[idx];
                    int n = idx + 1;
                    if (i.Id <= 0)              problems.Add("Row " + n + ": item id is required.");
                    else if (i.OrderedQty <= 0) problems.Add("Row " + n + ": ordered qty must be greater than zero.");
                    else if (i.Rate < 0)        problems.Add("Row " + n + ": rate cannot be negative.");
                }
                if (problems.Count > 0) return JsonError(string.Join("; ", problems));
            }

            try
            {
                var hdr = LoadHeader(dto.Id);
                if (hdr == null) return JsonError("PO not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT POs can be edited.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Update line rates / qty
                        if (dto.Items != null)
                        {
                            foreach (var i in dto.Items)
                            {
                                if (i.Id <= 0 || i.OrderedQty <= 0 || i.Rate < 0) continue;
                                decimal amount = i.OrderedQty * i.Rate;

                                db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrderItems
SET    OrderedQty = @qty, Rate = @rate, Amount = @amt,
       Remarks    = @rmk,
       UpdatedBy  = @uid, UpdatedDate = @now
WHERE  Id = @id",
                                    new SqlParameter("@qty",  i.OrderedQty),
                                    new SqlParameter("@rate", i.Rate),
                                    new SqlParameter("@amt",  amount),
                                    new SqlParameter("@rmk",  (object)(i.Remarks ?? "") ?? DBNull.Value),
                                    new SqlParameter("@uid",  userId),
                                    new SqlParameter("@now",  now),
                                    new SqlParameter("@id",   i.Id));
                            }
                        }

                        // Recompute header total from lines
                        decimal total = db.Database.SqlQuery<decimal?>(
                            "SELECT SUM(Amount) FROM dbo.PurchaseOrderItems WHERE PoId=@id AND (Deleted=0 OR Deleted IS NULL)",
                            new SqlParameter("@id", dto.Id)).FirstOrDefault() ?? 0m;

                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    ExpectedDeliveryDate = @eta,
       PaymentTerms  = @pay,
       DeliveryTerms = @del,
       Remarks       = @rmk,
       TotalAmount   = @total,
       UpdatedBy     = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                            new SqlParameter("@eta",   (object)dto.ExpectedDeliveryDate ?? DBNull.Value),
                            new SqlParameter("@pay",   (object)(dto.PaymentTerms ?? "") ?? DBNull.Value),
                            new SqlParameter("@del",   (object)(dto.DeliveryTerms ?? "") ?? DBNull.Value),
                            new SqlParameter("@rmk",   (object)(dto.Remarks ?? "") ?? DBNull.Value),
                            new SqlParameter("@total", total),
                            new SqlParameter("@uid",   userId),
                            new SqlParameter("@now",   now),
                            new SqlParameter("@id",    dto.Id));

                        tx.Commit();

                        LogAudit(dto.Id, "Updated", null, null,
                            "Header + " + (dto.Items?.Count ?? 0) + " line(s) edited; total " + Math.Round(total, 2));
                        return JsonSuccess("Updated.");
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        // Sprint-2: surface real SQL error via DescribeError.
                        return JsonError("PO update failed: " + DescribeError(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update PO.", ex);
            }
        }

        // ====================================================================
        // SEND — mark as formally sent to supplier
        // ====================================================================
        [HttpPost, Route("send/{id:int}")]
        public ActionResult Send(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PO not found.");
                if (!string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Only DRAFT POs can be sent.");

                // Expected Delivery Date is a contract with the supplier —
                // every PO leaving DRAFT must carry one. The PO Edit modal
                // already exposes the field; this gate makes sure the
                // buyer fills it in before the PO is dispatched.
                if (!hdr.ExpectedDeliveryDate.HasValue)
                    return JsonError("Expected Delivery Date is required before sending the PO. Set it on the PO edit form first.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Sprint-4 TOCTOU guard: the initial DRAFT check ran outside
                // any transaction. A guarded UPDATE that matches on
                // Status='DRAFT' makes a racing call fail-safe — only one
                // sender wins; the other gets a clear "already processed".
                int rowsUpdated = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Status='SENT', SentBy=@uid, SentDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id AND Status='DRAFT'",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));
                if (rowsUpdated == 0)
                    return JsonError("This PO was already processed by another action — refresh and try again.");

                LogAudit(id, "Sent", PoStatus.Draft, PoStatus.Sent,
                    "Expected: " + hdr.ExpectedDeliveryDate.Value.ToString("yyyy-MM-dd"));
                return JsonSuccess("PO sent to supplier.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to send PO: " + DescribeError(ex));
            }
        }

        // ====================================================================
        // CLOSE — manually close (e.g. after final receipt)
        // ====================================================================
        [HttpPost, Route("close/{id:int}")]
        public ActionResult Close(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PO not found.");
                if (string.Equals(hdr.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Cannot close a DRAFT PO. Send it first.");
                if (string.Equals(hdr.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("PO is already closed.");
                if (string.Equals(hdr.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("Cancelled POs cannot be closed.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Sprint-4 TOCTOU guard: only close rows that are still in
                // a closable state. If another action beat us to it, fail fast.
                int rowsClosed = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Status='CLOSED', ClosedBy=@uid, ClosedDate=@now,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id AND Status IN ('SENT', 'PARTIALLY_RECEIVED', 'FULLY_RECEIVED')",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));
                if (rowsClosed == 0)
                    return JsonError("This PO was already processed by another action — refresh and try again.");

                LogAudit(id, "Closed", hdr.Status, PoStatus.Closed, null);
                return JsonSuccess("Closed.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to close PO: " + DescribeError(ex));
            }
        }

        [HttpPost, Route("cancel/{id:int}")]
        public ActionResult Cancel(int id, PurchaseRequisitionsController.RejectDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
                return JsonError("Cancellation reason is required.");
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var hdr = LoadHeader(id);
                    if (hdr == null) return JsonError("PO not found.");
                    if (string.Equals(hdr.Status, "CLOSED", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(hdr.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                        return JsonError("PO is already terminal (" + hdr.Status + ").");
                    if (hdr.ReceivedAmount > 0)
                        return JsonError("Cannot cancel — receipts have already been posted. Close instead.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    string reason = dto.Reason.Trim();

                    // Sprint-4 TOCTOU guard: only cancel rows that aren't
                    // already cancelled / closed. A racing call gets a clear
                    // "already processed" instead of double-stamping.
                    int rowsCancelled = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Status='CANCELLED', CancelledBy=@uid, CancelledDate=@now,
       CancellationReason=@reason,
       UpdatedBy=@uid, UpdatedDate=@now, RecordTimeStamp=@now
WHERE  Id=@id AND Status NOT IN ('CANCELLED', 'CLOSED')",
                        new SqlParameter("@uid",    userId),
                        new SqlParameter("@now",    now),
                        new SqlParameter("@reason", reason),
                        new SqlParameter("@id",     id));
                    if (rowsCancelled == 0)
                    {
                        tx.Rollback();
                        return JsonError("This PO was already processed by another action — refresh and try again.");
                    }

                    // Back-cascade to source PR: a PR that converted into a now-
                    // cancelled PO has no live conversion to point at. Flip it to
                    // REJECTED so it stops showing up as CONVERTED_TO_PO. PR is
                    // 1:1 with PO (PurchaseRequisition.ConvertedToPoId is the
                    // singular link), so cancelling the PO orphans the PR cleanly.
                    int prCascaded = 0;
                    if (hdr.PrId.HasValue && hdr.PrId.Value > 0)
                    {
                        prCascaded = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseRequisition
SET    Status = 'REJECTED',
       RejectionReason = @reason,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @prId
   AND (Deleted = 0 OR Deleted IS NULL)
   AND Status NOT IN ('REJECTED', 'DRAFT')",
                            new SqlParameter("@uid",    userId),
                            new SqlParameter("@now",    now),
                            new SqlParameter("@reason", "Linked PO " + (hdr.PoCode ?? "") + " was cancelled: " + reason),
                            new SqlParameter("@prId",   hdr.PrId.Value));
                    }

                    tx.Commit();

                    LogAudit(id, "Cancelled", hdr.Status, PoStatus.Cancelled,
                        (string.IsNullOrWhiteSpace(reason) ? "" : reason)
                      + (prCascaded > 0 ? " — source PR rejected" : ""));

                    string msg = "PO cancelled.";
                    if (prCascaded > 0) msg += " Source PR rejected as well.";
                    return JsonSuccess(msg);
                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    return JsonError("Failed to cancel PO: " + DescribeError(ex));
                }
            }
        }

        // ====================================================================
        // SYNC RECEIPTS
        // -----------------------------------------------------------------
        // Called by the Purchases (GRN) screen after a Purchase line is
        // saved/edited. Recomputes ReceivedQty per PO line and updates
        // the PO header status / ReceivedAmount.
        //
        // Frontend can call this whenever; the heavy lifting is one SQL
        // round-trip per PO.
        // ====================================================================
        [HttpPost, Route("sync-receipts/{id:int}")]
        public ActionResult SyncReceipts(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PO not found.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        // Recompute ReceivedQty per PO line by summing the linked
                        // GRN (PurchaseItems.Qty) rows. UPDATE-from-SELECT keeps
                        // this single-statement and atomic. The summed quantity is
                        // capped at OrderedQty so an over-received line can never
                        // push ReceivedQty past OrderedQty and hide the over-receipt
                        // as a negative "outstanding" figure elsewhere (e.g. prefill-from-po).
                        db.Database.ExecuteSqlCommand(@"
UPDATE poi
SET    poi.ReceivedQty = CASE
           WHEN ISNULL(s.SumQty, 0) > poi.OrderedQty THEN poi.OrderedQty
           ELSE ISNULL(s.SumQty, 0)
       END,
       poi.UpdatedBy   = @uid,
       poi.UpdatedDate = @now
FROM   dbo.PurchaseOrderItems poi
CROSS APPLY (
    SELECT SUM(pi.Qty) AS SumQty
    FROM   dbo.PurchaseItems pi
    INNER JOIN dbo.Purchase p ON p.Id = pi.PurchaseId
    WHERE  pi.PoItemId = poi.Id
      AND  (pi.Deleted = 0 OR pi.Deleted IS NULL)
      AND  (p.Deleted  = 0 OR p.Deleted  IS NULL)
      AND  p.PurchaseOrderId = @poId
) s
WHERE  poi.PoId = @poId",
                            new SqlParameter("@poId", id),
                            new SqlParameter("@uid",  userId),
                            new SqlParameter("@now",  now));

                        // Recompute header ReceivedAmount = sum(min(received, ordered) * rate)
                        // (cap by ordered so over-receipts don't inflate the value)
                        decimal receivedAmount = db.Database.SqlQuery<decimal?>(@"
SELECT SUM(
    CASE WHEN poi.ReceivedQty > poi.OrderedQty THEN poi.OrderedQty
         ELSE poi.ReceivedQty
    END * poi.Rate
)
FROM   dbo.PurchaseOrderItems poi
WHERE  poi.PoId = @poId AND (poi.Deleted = 0 OR poi.Deleted IS NULL)",
                            new SqlParameter("@poId", id)).FirstOrDefault() ?? 0m;

                        // Derive header status: fully if every line met, else partial
                        bool anyReceived = db.Database.SqlQuery<int>(@"
SELECT COUNT(*) FROM dbo.PurchaseOrderItems
WHERE  PoId = @poId AND ReceivedQty > 0", new SqlParameter("@poId", id)).First() > 0;

                        bool allMet = db.Database.SqlQuery<int>(@"
SELECT COUNT(*) FROM dbo.PurchaseOrderItems
WHERE  PoId = @poId AND ReceivedQty < OrderedQty", new SqlParameter("@poId", id)).First() == 0;

                        string newStatus = hdr.Status;
                        if (anyReceived && allMet) newStatus = "FULLY_RECEIVED";
                        else if (anyReceived)     newStatus = "PARTIALLY_RECEIVED";
                        // If no receipts at all, leave whatever current status is (DRAFT/SENT)

                        db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    ReceivedAmount = @amt,
       Status         = @st,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id",
                            new SqlParameter("@amt", receivedAmount),
                            new SqlParameter("@st",  newStatus),
                            new SqlParameter("@uid", userId),
                            new SqlParameter("@now", now),
                            new SqlParameter("@id",  id));

                        tx.Commit();

                        // Sprint-2: only log when status actually changed so
                        // the timeline doesn't drown in repeated SyncReceipts
                        // calls. Receipt-amount-only ticks aren't audit-worthy.
                        if (!string.Equals(hdr.Status ?? "", newStatus ?? "", StringComparison.Ordinal))
                        {
                            LogAudit(id, "ReceiptsSynced", hdr.Status, newStatus,
                                "Received: " + Math.Round(receivedAmount, 2));
                        }

                        return JsonSuccess("Synced.", new
                        {
                            poId           = id,
                            receivedAmount = Math.Round(receivedAmount, 2),
                            status         = newStatus
                        });
                    }
                    catch (Exception ex)
                    {
                        try { tx.Rollback(); } catch { }
                        return JsonError("Sync failed: " + DescribeError(ex));
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to sync receipts.", ex);
            }
        }

        // ====================================================================
        // OPEN ITEMS — for the Purchases (GRN) screen's PO picker
        // -----------------------------------------------------------------
        // Returns the PO's line items with their outstanding (still-to-receive)
        // quantities, so the GRN form can offer "remaining 2,500m" as
        // default qty for each line.
        // ====================================================================
        [HttpGet, Route("open-items/{id:int}")]
        public ActionResult OpenItems(int id)
        {
            try
            {
                string sql = @"
SELECT  poi.Id, poi.PoId, poi.RawItemId,
        r.ItemName AS RawItemName,
        poi.UnitId, u.UnitCode AS UnitCode,
        poi.OrderedQty, poi.Rate, poi.Amount,
        poi.ReceivedQty,
        (poi.OrderedQty - poi.ReceivedQty) AS OutstandingQty,
        poi.Remarks
FROM    dbo.PurchaseOrderItems poi
LEFT JOIN dbo.Gen_RawItems r ON r.Id = poi.RawItemId
LEFT JOIN dbo.Gen_Units    u ON u.Id = poi.UnitId
WHERE   poi.PoId = @id AND (poi.Deleted = 0 OR poi.Deleted IS NULL)
ORDER BY poi.Id";

                var rows = db.Database.SqlQuery<OpenItemRow>(sql, new SqlParameter("@id", id)).ToList();
                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load PO open items.", ex);
            }
        }

        public class OpenItemRow
        {
            public int     Id { get; set; }
            public int     PoId { get; set; }
            public int     RawItemId { get; set; }
            public string  RawItemName { get; set; }
            public int?    UnitId { get; set; }
            public string  UnitCode { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal Rate { get; set; }
            public decimal Amount { get; set; }
            public decimal ReceivedQty { get; set; }
            public decimal OutstandingQty { get; set; }
            public string  Remarks { get; set; }
        }

        // ====================================================================
        // OPEN POs — for the Purchases (GRN) screen's "Receive Against PO" picker
        // ====================================================================
        [HttpGet, Route("open-list")]
        public ActionResult OpenList()
        {
            try
            {
                string sql = @"
SELECT  po.Id, po.PoCode, po.SupplierId, s.SupplierName,
        po.OrderDate, po.Status, po.TotalAmount, po.ReceivedAmount
FROM    dbo.PurchaseOrder po
LEFT JOIN dbo.Gen_Suppliers s ON s.Id = po.SupplierId
WHERE   (po.Deleted = 0 OR po.Deleted IS NULL)
  AND   po.Status IN ('SENT', 'PARTIALLY_RECEIVED')
ORDER BY po.Id DESC";

                var rows = db.Database.SqlQuery<OpenPoRow>(sql).ToList();

                // Re-project to camelCase + ISO date for the Angular picker.
                var items = rows.Select(r => new
                {
                    id             = r.Id,
                    poCode         = r.PoCode,
                    supplierId     = r.SupplierId,
                    supplierName   = r.SupplierName,
                    orderDate      = r.OrderDate.ToString("yyyy-MM-dd"),
                    status         = r.Status,
                    totalAmount    = r.TotalAmount,
                    receivedAmount = r.ReceivedAmount,
                    outstanding    = r.TotalAmount - r.ReceivedAmount
                }).ToList();

                return JsonSuccess("OK", items);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load open POs.", ex);
            }
        }

        public class OpenPoRow
        {
            public int      Id { get; set; }
            public string   PoCode { get; set; }
            public int      SupplierId { get; set; }
            public string   SupplierName { get; set; }
            public DateTime OrderDate { get; set; }
            public string   Status { get; set; }
            public decimal  TotalAmount { get; set; }
            public decimal  ReceivedAmount { get; set; }
        }

        // ====================================================================
        //  DELETE — soft-delete a PO
        // --------------------------------------------------------------------
        //  Only DRAFT and CANCELLED POs can be deleted. Sent / Received /
        //  Closed POs must stay in the DB for the audit trail (a buyer can't
        //  retroactively erase a real procurement event).
        // ====================================================================
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var hdr = LoadHeader(id);
                if (hdr == null) return JsonError("PO not found.");

                string status = (hdr.Status ?? "").Trim().ToUpper();
                if (status != PoStatus.Draft && status != PoStatus.Cancelled)
                    return JsonError("Only DRAFT or CANCELLED POs can be deleted. Cancel the PO first if it has been sent.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                // Sprint-4 TOCTOU guard: only delete if still in a deletable
                // state — a racing Send / Receive call may have flipped the
                // status by now.
                int rowsDeleted = db.Database.ExecuteSqlCommand(@"
UPDATE dbo.PurchaseOrder
SET    Deleted = 1, Active = 0,
       UpdatedBy = @uid, UpdatedDate = @now, RecordTimeStamp = @now
WHERE  Id = @id
   AND  Status IN ('DRAFT', 'CANCELLED')
   AND  (Deleted = 0 OR Deleted IS NULL)",
                    new SqlParameter("@uid", userId),
                    new SqlParameter("@now", now),
                    new SqlParameter("@id",  id));
                if (rowsDeleted == 0)
                    return JsonError("This PO was already processed by another action — refresh and try again.");

                LogAudit(id, "Deleted", hdr.Status, null, "Soft-deleted (Deleted=1, Active=0)");
                return JsonSuccess("Deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete PO: " + DescribeError(ex));
            }
        }

        // ====================================================================
        //  SPRINT-3  BULK ACTIONS + CLONE
        // --------------------------------------------------------------------
        //  Same per-row try/catch + camelCase failure list as PR / WO / Plan
        //  so the Angular afterBulk helper is reusable.
        // ====================================================================
        public class PoBulkIdsDto      { public List<int> Ids { get; set; } }
        public class PoBulkCancelDto   { public List<int> Ids { get; set; } public string Reason { get; set; } }
        private class PoBulkRowResult  { public int Id { get; set; } public string Code { get; set; } public string Reason { get; set; } }

        [HttpPost, Route("bulk-send")]
        public ActionResult BulkSend(PoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No POs selected.");

            int successCount = 0;
            var failures = new List<PoBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Send(id) as System.Web.Mvc.JsonResult;
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
                        failures.Add(new PoBulkRowResult { Id = id, Code = hdr?.PoCode ?? "?", Reason = msg ?? "Send failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " sent, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-close")]
        public ActionResult BulkClose(PoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No POs selected.");

            int successCount = 0;
            var failures = new List<PoBulkRowResult>();

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Close(id) as System.Web.Mvc.JsonResult;
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
                        failures.Add(new PoBulkRowResult { Id = id, Code = hdr?.PoCode ?? "?", Reason = msg ?? "Close failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " closed, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-cancel")]
        public ActionResult BulkCancel(PoBulkCancelDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No POs selected.");
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return JsonError("Cancellation reason is required.");

            int successCount = 0;
            var failures = new List<PoBulkRowResult>();
            // PO Cancel reuses the PR module's RejectDto shape (just a Reason
            // string). Build it inline so the bulk loop can call the single
            // Cancel endpoint without a new DTO type.
            var reasonDto = new PurchaseRequisitionsController.RejectDto { Reason = dto.Reason };

            foreach (var id in dto.Ids.Distinct().OrderBy(x => x))
            {
                try
                {
                    var result = Cancel(id, reasonDto) as System.Web.Mvc.JsonResult;
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
                        failures.Add(new PoBulkRowResult { Id = id, Code = hdr?.PoCode ?? "?", Reason = msg ?? "Cancel failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
                }
            }

            return Json(new
            {
                success = true,
                message = successCount + " cancelled, " + failures.Count + " failed.",
                data = new
                {
                    successCount,
                    failureCount = failures.Count,
                    failures = failures.Select(f => new { id = f.Id, code = f.Code, reason = f.Reason }).ToList()
                }
            });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(PoBulkIdsDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
                return JsonError("No POs selected.");

            int successCount = 0;
            var failures = new List<PoBulkRowResult>();

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
                        failures.Add(new PoBulkRowResult { Id = id, Code = hdr?.PoCode ?? "?", Reason = msg ?? "Delete failed." });
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(new PoBulkRowResult { Id = id, Code = "?", Reason = DescribeError(ex) });
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
        //  CLONE — deep-copy a PO into a fresh DRAFT
        // --------------------------------------------------------------------
        //  Same supplier + items + payment/delivery terms. PR link cleared
        //  (the clone isn't generated from any PR). New PoCode via the same
        //  retry path Create / Approve use.
        //
        //  Optionally accepts an override payload so the frontend can preview
        //  the clone in a modal, let the buyer tweak qty/rate/dates/terms,
        //  THEN persist — instead of creating the record on click and then
        //  having to edit again. If any override field is missing, the
        //  source value is used.
        // ====================================================================
        public class PoCloneOverrideDto
        {
            public string ExpectedDeliveryDate { get; set; }
            public string PaymentTerms { get; set; }
            public string DeliveryTerms { get; set; }
            public string Remarks { get; set; }
            public List<PoCloneOverrideItem> Items { get; set; }
        }
        public class PoCloneOverrideItem
        {
            // Line-level override keyed by RawItemId (the only stable handle —
            // source-item Ids belong to a different PO and would be confusing).
            public int      RawItemId    { get; set; }
            public decimal? OrderedQty   { get; set; }
            public decimal? Rate         { get; set; }
            public string   Remarks      { get; set; }
        }

        [HttpPost, Route("clone/{id:int}")]
        public ActionResult Clone(int id, PoCloneOverrideDto overrides = null)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var src = LoadHeader(id);
                    if (src == null) return JsonError("Source PO not found.");

                    var srcItems = LoadItems(id);
                    if (srcItems == null || srcItems.Count == 0)
                        return JsonError("Source PO has no items to clone.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    int branchId = src.BranchId ?? GetCurrentBranchId();
                    string newCode = GeneratePoCode();

                    // Resolve header overrides — fall back to source values.
                    DateTime? expectedDate = null;
                    if (overrides != null && !string.IsNullOrWhiteSpace(overrides.ExpectedDeliveryDate))
                    {
                        DateTime tmp;
                        if (DateTime.TryParse(overrides.ExpectedDeliveryDate, out tmp))
                            expectedDate = tmp;
                    }
                    string payTerms = overrides?.PaymentTerms  ?? src.PaymentTerms  ?? "";
                    string delTerms = overrides?.DeliveryTerms ?? src.DeliveryTerms ?? "";
                    string remarks  = !string.IsNullOrWhiteSpace(overrides?.Remarks)
                                      ? overrides.Remarks
                                      : "Cloned from " + src.PoCode
                                        + (string.IsNullOrWhiteSpace(src.Remarks) ? "" : " — " + src.Remarks);

                    // Build a per-RawItemId override map (qty / rate / remarks).
                    var overrideByRaw = new Dictionary<int, PoCloneOverrideItem>();
                    if (overrides?.Items != null)
                    {
                        foreach (var o in overrides.Items)
                            if (o.RawItemId > 0 && !overrideByRaw.ContainsKey(o.RawItemId))
                                overrideByRaw[o.RawItemId] = o;
                    }

                    // Compute total from final (possibly-overridden) line values.
                    decimal totalAmount = 0m;
                    foreach (var s in srcItems)
                    {
                        decimal qty  = (overrideByRaw.ContainsKey(s.RawItemId) && overrideByRaw[s.RawItemId].OrderedQty.HasValue)
                                        ? overrideByRaw[s.RawItemId].OrderedQty.Value : s.OrderedQty;
                        decimal rate = (overrideByRaw.ContainsKey(s.RawItemId) && overrideByRaw[s.RawItemId].Rate.HasValue)
                                        ? overrideByRaw[s.RawItemId].Rate.Value : s.Rate;
                        totalAmount += qty * rate;
                    }

                    // Sprint-4: race-safe with skip+retry on unique-violation.
                    int newId = 0;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            newId = db.Database.SqlQuery<int>(@"
INSERT INTO dbo.PurchaseOrder
       (PoCode, PrId, BranchId, SupplierId,
        OrderDate, ExpectedDeliveryDate,
        Status, TotalAmount, ReceivedAmount,
        PaymentTerms, DeliveryTerms, Remarks,
        Active, Deleted,
        CreatedBy, CreatedDate, RecordTimeStamp)
VALUES (@code, NULL, @br, @sup,
        @now,  @eta,
        'DRAFT', @total, 0,
        @pay,  @del,  @rmk,
        1,     0,
        @uid,  @now,  @now);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                                new SqlParameter("@code",  newCode),
                                new SqlParameter("@br",    branchId),
                                new SqlParameter("@sup",   src.SupplierId),
                                new SqlParameter("@now",   now),
                                new SqlParameter("@eta",   (object)expectedDate ?? DBNull.Value),
                                new SqlParameter("@total", totalAmount),
                                new SqlParameter("@pay",   (object)payTerms ?? DBNull.Value),
                                new SqlParameter("@del",   (object)delTerms ?? DBNull.Value),
                                new SqlParameter("@rmk",   (object)remarks ?? DBNull.Value),
                                new SqlParameter("@uid",   userId)
                            ).First();
                            break;
                        }
                        catch (Exception cloneEx) when (IsPoCodeUniqueViolation(cloneEx))
                        {
                            if (attempt + 1 >= 3) throw;
                            newCode = GeneratePoCode(attempt + 1);
                        }
                    }

                    foreach (var s in srcItems)
                    {
                        // Apply override per line (by RawItemId) — qty, rate,
                        // and per-line remarks. Amount recomputed from final
                        // qty × rate.
                        decimal qty  = s.OrderedQty;
                        decimal rate = s.Rate;
                        string  lineRmk = s.Remarks;
                        if (overrideByRaw.ContainsKey(s.RawItemId))
                        {
                            var o = overrideByRaw[s.RawItemId];
                            if (o.OrderedQty.HasValue) qty  = o.OrderedQty.Value;
                            if (o.Rate.HasValue)       rate = o.Rate.Value;
                            if (!string.IsNullOrWhiteSpace(o.Remarks)) lineRmk = o.Remarks;
                        }
                        decimal amount = qty * rate;

                        db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseOrderItems
       (PoId, PrItemId, RawItemId, UnitId, OrderedQty, Rate, Amount, ReceivedQty, Remarks,
        Active, Deleted, CreatedBy, CreatedDate)
VALUES (@po,  NULL,    @raw,      @unit, @qty,       @rate, @amt,  0,           @rmk,
        1,    0,       @uid,     @now)",
                            new SqlParameter("@po",   newId),
                            new SqlParameter("@raw",  s.RawItemId),
                            new SqlParameter("@unit", (object)s.UnitId ?? DBNull.Value),
                            new SqlParameter("@qty",  qty),
                            new SqlParameter("@rate", rate),
                            new SqlParameter("@amt",  amount),
                            new SqlParameter("@rmk",  (object)(lineRmk ?? "") ?? DBNull.Value),
                            new SqlParameter("@uid",  userId),
                            new SqlParameter("@now",  now));
                    }

                    tx.Commit();

                    LogAudit(newId, "Cloned", null, PoStatus.Draft,
                        "Cloned from " + src.PoCode + " (" + srcItems.Count + " line(s))");
                    LogAudit(id, "ClonedTo", null, newCode, "Source for new PO " + newCode);

                    return JsonSuccess("Cloned.", new
                    {
                        id          = newId,
                        poCode      = newCode,
                        sourceId    = src.Id,
                        sourceCode  = src.PoCode,
                        itemsCloned = srcItems.Count
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
        //  SPRINT-4  RACE-SAFE PoCode GENERATION
        // --------------------------------------------------------------------
        //  Mirror of the helper in PurchaseRequisitionsController so the PO
        //  controller can call it directly. Two simultaneous Clones could
        //  pick the same code; the SaveChanges retry below closes the race.
        // ====================================================================
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

        /** SQL Server uses error 2601 (duplicate key in unique index) and
         *  2627 (duplicate key in unique constraint). Walk the inner
         *  exception chain since EF wraps SqlException inside DbUpdateException. */
        private bool IsPoCodeUniqueViolation(Exception ex)
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

        // ====================================================================
        //  SPRINT-2  AUDIT TIMELINE + WORKFLOW STAGE + DescribeError
        // --------------------------------------------------------------------
        //  Append-only audit log keyed by PoId. Writes via raw SQL so no
        //  EDMX regeneration is needed. Every state-changing endpoint calls
        //  LogAudit() inside its happy path. Failures swallowed — audit
        //  never blocks a real save.
        // ====================================================================

        private static readonly string[] _poWorkflowStages = new[] {
            PoStatus.Draft, PoStatus.Sent, PoStatus.PartiallyReceived,
            PoStatus.FullyReceived, PoStatus.Closed, PoStatus.Cancelled
        };

        private int ComputePoWorkflowStageIndex(string status)
        {
            string s = (status ?? "").Trim().ToUpper();
            // Cancelled is the terminal sibling that lives off to the side.
            if (s == PoStatus.Cancelled)         return 5;
            if (s == PoStatus.Closed)            return 4;
            if (s == PoStatus.FullyReceived)     return 3;
            if (s == PoStatus.PartiallyReceived) return 2;
            if (s == PoStatus.Sent)              return 1;
            return 0; // Draft / unknown
        }

        private void LogAudit(int poId, string action, string oldValue, string newValue, string detail)
        {
            try
            {
                int userId = GetSafeCurrentUserId();
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseOrderAuditLog (PoId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@pid, @act, @uid, @now, @ov, @nv, @det)",
                    new SqlParameter("@pid", poId),
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
                    "LogAudit failed for PO " + poId + ": " + ex.Message);
            }
        }

        private class PoAuditRow
        {
            public int      Id            { get; set; }
            public int      PoId          { get; set; }
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
                var rows = db.Database.SqlQuery<PoAuditRow>(@"
SELECT  a.Id, a.PoId, a.Action,
        a.ChangedBy, u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
FROM    dbo.PurchaseOrderAuditLog a
LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
WHERE   a.PoId = @pid
ORDER BY a.ChangedDate DESC, a.Id DESC",
                    new SqlParameter("@pid", id)).ToList();

                var data = rows.Select(r => new
                {
                    id              = r.Id,
                    poId            = r.PoId,
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

        /** Drill into EF/SQL exception chain to extract the real SQL error
         *  text. Same pattern used in PR / WO / Plan modules. */
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
        private PoRow LoadHeader(int id)
        {
            string sql = @"
SELECT  po.Id, po.PoCode,
        po.PrId, pr.PrCode AS PrCode,
        po.BranchId, b.Name AS BranchName,
        po.SupplierId, s.SupplierName,
        po.OrderDate, po.ExpectedDeliveryDate,
        po.Status, po.TotalAmount, po.ReceivedAmount,
        po.PaymentTerms, po.DeliveryTerms, po.Remarks,
        po.SentBy, po.SentDate, po.ClosedDate, po.CancelledDate, po.CancellationReason,
        po.Active, po.CreatedDate, po.UpdatedDate
FROM    dbo.PurchaseOrder po
LEFT JOIN dbo.PurchaseRequisition pr ON pr.Id = po.PrId
LEFT JOIN dbo.Branches             b  ON b.Id  = po.BranchId
LEFT JOIN dbo.Gen_Suppliers        s  ON s.Id  = po.SupplierId
WHERE   po.Id = @id";
            return db.Database.SqlQuery<PoRow>(sql, new SqlParameter("@id", id)).FirstOrDefault();
        }

        private List<PoItemRow> LoadItems(int poId)
        {
            string sql = @"
SELECT  poi.Id, poi.PoId, poi.PrItemId, poi.RawItemId,
        r.ItemName AS RawItemName,
        poi.UnitId, u.UnitCode AS UnitCode,
        poi.OrderedQty, poi.Rate, poi.Amount, poi.ReceivedQty, poi.Remarks
FROM    dbo.PurchaseOrderItems poi
LEFT JOIN dbo.Gen_RawItems r ON r.Id = poi.RawItemId
LEFT JOIN dbo.Gen_Units    u ON u.Id = poi.UnitId
WHERE   poi.PoId = @id AND (poi.Deleted = 0 OR poi.Deleted IS NULL)
ORDER BY poi.Id";
            return db.Database.SqlQuery<PoItemRow>(sql, new SqlParameter("@id", poId)).ToList();
        }
    }
}
