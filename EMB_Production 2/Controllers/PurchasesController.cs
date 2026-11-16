using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using iTextSharp.text;
using iTextSharp.text.pdf;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using System.Data.Entity.Validation;


namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical GRN/Purchase status values. Stop comparing
    //  raw strings; one typo silently breaks a filter. Keep the names
    //  identical to the strings already in the DB so this is a drop-in
    //  refactor.
    //  REVERSED is a derived label only — the schema doesn't store it
    //  (a reversed purchase has IsPosted=false and a [PUR-REV-N] tag on
    //  its Gen_Store rows). The constant is kept here so the workflow
    //  stepper and the new IsReversed flag agree.
    // ─────────────────────────────────────────────────────────────────
    public static class PurchaseStatus
    {
        public const string DRAFT    = "DRAFT";
        public const string POSTED   = "POSTED";
        public const string REVERSED = "REVERSED";
    }

    [RoutePrefix("emb-products/purchase")]
    public class PurchaseController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  WORKFLOW STEPPER
        //  Canonical pipeline rendered as horizontal dots in the detail
        //  drawer header. REVERSED is rendered as a red sibling — the
        //  stepper draws it OFF the main line, in the same slot as the
        //  POSTED stage but with a different colour, so a reversed
        //  purchase shows DRAFT (active blue) + REVERSED (red) + a
        //  greyed-out POSTED. The client uses the index to colour-up to.
        // ─────────────────────────────────────────────────────────────
        private static readonly string[] _purchaseWorkflowStages = new[]
        {
            PurchaseStatus.DRAFT,
            PurchaseStatus.POSTED,
            PurchaseStatus.REVERSED
        };

        private static int ComputePurchaseWorkflowStageIndex(string status)
        {
            switch ((status ?? "").Trim().ToUpper())
            {
                case PurchaseStatus.POSTED:   return 1;
                case PurchaseStatus.REVERSED: return 2;
                case PurchaseStatus.DRAFT:
                default:                       return 0;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table that records every state transition on a
        //  GRN. Writes are best-effort (we wrap in try/catch so a missing
        //  table or a transient SQL error never blocks the user's main
        //  action). Read via /audit-log/{id} for the detail-drawer
        //  timeline.
        // ─────────────────────────────────────────────────────────────
        private void LogAudit(int purchaseId, string action,
                              string oldValue = null, string newValue = null,
                              string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.PurchaseAuditLog
       (PurchaseId, Action, ChangedBy, ChangedDate,
        OldValue, NewValue, Detail)
VALUES (@pid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@pid",  purchaseId),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@uid",  GetSafeCurrentUserId()),
                    new SqlParameter("@when", DateTime.Now),
                    new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
            }
            catch
            {
                // Audit is observability, not a hard requirement — never
                // surface a logger failure to the operator mid-action.
            }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var entries = db.Database.SqlQuery<PurchaseAuditEntry>(@"
SELECT  a.Id,
        a.PurchaseId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.PurchaseAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.PurchaseId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                // Format friendly dates server-side so the client renders
                // a consistent string regardless of locale settings.
                var dto = entries.Select(e => new
                {
                    id            = e.Id,
                    purchaseId    = e.PurchaseId,
                    action        = e.Action,
                    changedBy     = e.ChangedBy,
                    changedByName = e.ChangedByName,
                    changedDate   = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue      = e.OldValue,
                    newValue      = e.NewValue,
                    detail        = e.Detail
                }).ToList();

                return JsonSuccess("OK", dto);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load audit log. " + DescribePurchaseError(ex));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  ERROR UNWRAP
        //  EF6 + SqlClient wrap the real diagnostic message inside two
        //  layers of generic exceptions. Surface the deepest message so
        //  the operator sees the SQL constraint or check that fired,
        //  not "An error occurred while saving the entity".
        // ─────────────────────────────────────────────────────────────
        private static string DescribePurchaseError(Exception ex)
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

        // Shape for the audit-log raw-SQL result set.
        private class PurchaseAuditEntry
        {
            public int Id { get; set; }
            public int PurchaseId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        [HttpGet, Route("list")]
        public ActionResult List(
            string fromDate = null,
            string toDate = null,
            int? supplierId = null,
            string status = null,
            string itemSearch = null,
            string invoiceSearch = null)
        {
            // Parse dates defensively
            DateTime? fromDt = null, toDt = null;
            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
            if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);

            string statusUp = (status ?? "").Trim().ToUpper();

            // Apply server-side filters where possible (date, supplier, status, invoice)
            var baseQ = db.Purchase.Where(p => p.Deleted != true);
            if (fromDt.HasValue) baseQ = baseQ.Where(p => p.PurchaseDate >= fromDt.Value);
            if (toDt.HasValue) baseQ = baseQ.Where(p => p.PurchaseDate <= toDt.Value);
            if (supplierId.HasValue && supplierId.Value > 0) baseQ = baseQ.Where(p => p.SupplierId == supplierId.Value);
            // Sprint-1: status filter routes through PurchaseStatus constants.
            // DRAFT covers "never posted" + "reversed" both at the schema
            // level (IsPosted = false). REVERSED is filtered as a sub-set
            // after we know which rows have a reversal marker.
            if (statusUp == PurchaseStatus.DRAFT)
                baseQ = baseQ.Where(p => p.IsPosted != true);
            else if (statusUp == PurchaseStatus.POSTED)
                baseQ = baseQ.Where(p => p.IsPosted == true);
            // REVERSED is post-filtered below — the schema doesn't store it.
            if (!string.IsNullOrWhiteSpace(invoiceSearch))
            {
                var s = invoiceSearch.Trim();
                baseQ = baseQ.Where(p => p.InvoiceNo != null && p.InvoiceNo.Contains(s));
            }

            var raw = (
                from p in baseQ
                join b in db.Branches on p.BranchId equals b.Id into br
                from b in br.DefaultIfEmpty()

                join s in db.Gen_Suppliers on p.SupplierId equals s.Id into sup
                from s in sup.DefaultIfEmpty()

                orderby p.Id descending

                select new
                {
                    p.Id,
                    p.PurchaseDate,
                    p.InvoiceNo,
                    p.BranchId,
                    p.SupplierId,

                    BranchName = b != null ? b.Name : "-",
                    SupplierName = s != null ? s.SupplierName : "-",

                    p.IsPosted,
                    Active = p.Active ?? true,

                    Items = p.PurchaseItems
                        .Where(i => i.Deleted != true)
                        .Select(i => new
                        {
                            i.Id,
                            i.ItemType,
                            i.ItemId,
                            i.Qty,
                            Rate = i.Rate ?? 0m,
                            i.IsPosted,
                            Active = i.Active ?? true,
                            i.Remarks
                        })
                        .ToList()
                }
            ).ToList();

            var rawItemIds = raw
                .SelectMany(x => x.Items)
                .Where(x => (x.ItemType ?? "").Trim().ToUpper() == "RAW")
                .Select(x => x.ItemId)
                .Distinct()
                .ToList();

            var threadItemIds = raw
                .SelectMany(x => x.Items)
                .Where(x => (x.ItemType ?? "").Trim().ToUpper() == "THREAD")
                .Select(x => x.ItemId)
                .Distinct()
                .ToList();

            var fabricItemIds = raw
                .SelectMany(x => x.Items)
                .Where(x => (x.ItemType ?? "").Trim().ToUpper() == "FABRIC")
                .Select(x => x.ItemId)
                .Distinct()
                .ToList();

            var rawMap = db.Gen_RawItems
                .Where(x => rawItemIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            var listFabricMap = db.Gen_RawItems
                .Where(x => fabricItemIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            // Phase E: prefer linked RawItem name for threads, fall back to thread label if unlinked
            var threadInfo = db.Gen_Threads
                .Where(x => threadItemIds.Contains(x.Id))
                .Select(x => new { x.Id, x.RawItemId, x.Code, x.Name, x.Color })
                .ToList();

            var linkedRawIdsList = threadInfo.Where(t => t.RawItemId.HasValue).Select(t => t.RawItemId.Value).Distinct().ToList();
            var linkedRawNames = db.Gen_RawItems
                .Where(r => linkedRawIdsList.Contains(r.Id))
                .ToDictionary(r => r.Id, r => r.ItemName);

            var threadMap = threadInfo.ToDictionary(
                t => t.Id,
                t => t.RawItemId.HasValue && linkedRawNames.ContainsKey(t.RawItemId.Value)
                     ? linkedRawNames[t.RawItemId.Value]
                     : "[unlinked] " + (t.Code ?? "") + " " + (t.Name ?? "")
            );

            string ItemLabel(int itemType, string typeStr, int itemId)
            {
                var t = (typeStr ?? "").Trim().ToUpper();
                if (t == "RAW" && rawMap.ContainsKey(itemId)) return rawMap[itemId];
                if (t == "THREAD" && threadMap.ContainsKey(itemId)) return threadMap[itemId];
                if (t == "FABRIC" && listFabricMap.ContainsKey(itemId)) return listFabricMap[itemId];
                return "-";
            }

            // ─── Sprint-1: batch-load PO source codes ────────────────────
            //   The EDMX Purchase entity hasn't been regenerated to include
            //   PurchaseOrderId, so we round-trip with raw SQL keyed by the
            //   ids on this page and assemble a dict for the projection.
            //   One query — N+1 free.
            var idsOnPage = raw.Select(x => x.Id).ToList();
            var poSourceMap = new Dictionary<int, Tuple<int, string>>();
            if (idsOnPage.Any())
            {
                string idCsv = string.Join(",", idsOnPage);
                var rows = db.Database.SqlQuery<PoSourceRow>(@"
SELECT  p.Id AS PurchaseId,
        p.PurchaseOrderId AS PoId,
        po.PoCode
  FROM  dbo.Purchase p
  LEFT JOIN dbo.PurchaseOrder po
         ON po.Id = p.PurchaseOrderId
        AND ISNULL(po.Deleted, 0) = 0
 WHERE  p.Id IN (" + idCsv + @")
   AND  p.PurchaseOrderId IS NOT NULL").ToList();
                foreach (var r in rows)
                    poSourceMap[r.PurchaseId] = Tuple.Create(r.PoId, r.PoCode ?? "");
            }

            // ─── Sprint-1: batch-detect REVERSED rows ────────────────────
            //   A Purchase is "reversed" when its Gen_Store IN rows carry
            //   a [PUR-REV-N] tag — i.e. it was posted at some point, then
            //   un-posted. The schema has no flag, so we query the marker.
            var reversedIds = new HashSet<int>();
            if (idsOnPage.Any())
            {
                // Gen_Store.ReferenceId is nullable; project to int via
                // `?? 0` so the Contains/Distinct work against the same
                // type as idsOnPage (List<int>). Rows with null ReferenceId
                // are filtered out by the != null guard upstream.
                var matches = db.Gen_Store
                    .Where(s => s.ReferenceType == "PURCHASE"
                             && s.ReferenceId.HasValue
                             && idsOnPage.Contains(s.ReferenceId.Value)
                             && s.Remarks != null
                             && s.Remarks.Contains("[REVERSED PUR-REV-"))
                    .Select(s => s.ReferenceId.Value)
                    .Distinct()
                    .ToList();
                foreach (var pid in matches) reversedIds.Add(pid);
            }

            string ToStatus(bool isPosted, bool isReversed)
            {
                if (isPosted)    return PurchaseStatus.POSTED;
                if (isReversed)  return PurchaseStatus.REVERSED;
                return PurchaseStatus.DRAFT;
            }

            var list = raw.Select(p =>
            {
                bool isReversed = reversedIds.Contains(p.Id);
                Tuple<int, string> poRef;
                poSourceMap.TryGetValue(p.Id, out poRef);

                return new PurchaseRowViewModel
                {
                    Id = p.Id,
                    PurchaseDate = p.PurchaseDate.ToString("yyyy-MM-dd"),
                    PurchaseDateText = p.PurchaseDate.ToString("dd-MMMM-yyyy"),
                    InvoiceNo = p.InvoiceNo,
                    HasInvoice = !string.IsNullOrWhiteSpace(p.InvoiceNo)
                                 && p.InvoiceNo.Trim() != "-",

                    BranchId = p.BranchId,
                    BranchName = p.BranchName,

                    SupplierId = p.SupplierId,
                    SupplierName = p.SupplierName,

                    PurchaseOrderId = poRef != null ? (int?)poRef.Item1 : null,
                    PoCode = poRef != null ? poRef.Item2 : null,

                    ItemDisplay = string.Join(", ",
                        p.Items
                            .Select(i => ItemLabel(0, i.ItemType, i.ItemId))
                            .Where(s => s != "-")
                    ),

                    ItemCount = p.Items.Count(i => ItemLabel(0, i.ItemType, i.ItemId) != "-"),

                    ItemTooltip = string.Join("\n",
                        p.Items
                            .Select(i => $"{ItemLabel(0, i.ItemType, i.ItemId)} | Qty: {i.Qty:0.##} | Rate: {i.Rate:0.##}")
                            .Where(s => !s.StartsWith("-"))
                    ),

                    TotalQty = p.Items.Sum(i => i.Qty),
                    TotalAmount = p.Items.Sum(i => i.Qty * i.Rate),

                    IsPosted = p.IsPosted,
                    Active = p.Active,
                    IsReversed = isReversed,
                    Status = ToStatus(p.IsPosted, isReversed)
                };
            }).ToList();

            // Post-filter by item name (server-side join is too expensive across polymorphic types)
            if (!string.IsNullOrWhiteSpace(itemSearch))
            {
                var q = itemSearch.Trim();
                list = list.Where(x =>
                    (x.ItemDisplay ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (x.ItemTooltip ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                ).ToList();
            }

            // Sprint-1: REVERSED is a post-filter, mirrored after IsReversed
            // is computed. Cannot be a SQL where-clause (it's a remark scan).
            if (statusUp == PurchaseStatus.REVERSED)
                list = list.Where(x => x.IsReversed).ToList();

            var summary = new
            {
                count = list.Count,
                totalQty = list.Sum(x => x.TotalQty),
                totalAmount = list.Sum(x => x.TotalAmount),
                postedCount = list.Count(x => x.IsPosted),
                draftCount = list.Count(x => !x.IsPosted && !x.IsReversed),
                reversedCount = list.Count(x => x.IsReversed)
            };

            return JsonSuccess("OK", new { rows = list, summary });
        }

        // Private DTO for the raw-SQL PO source batch lookup. Kept inside
        // the controller — it's an implementation detail of List().
        private class PoSourceRow
        {
            public int PurchaseId { get; set; }
            public int PoId { get; set; }
            public string PoCode { get; set; }
        }

        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var raw = (
                from p in db.Purchase

                join b in db.Branches
                    on p.BranchId equals b.Id into br
                from b in br.DefaultIfEmpty()

                join s in db.Gen_Suppliers
                    on p.SupplierId equals s.Id into sup
                from s in sup.DefaultIfEmpty()

                where p.Id == id && p.Deleted != true

                select new
                {
                    p.Id,
                    p.PurchaseDate,
                    p.InvoiceNo,
                    p.Remarks,

                    SupplierId = p.SupplierId,
                    SupplierName = s != null ? s.SupplierName : "",

                    BranchId = p.BranchId,
                    BranchName = b != null ? b.Name : "",

                    p.IsPosted,
                    p.Active,

                    p.CreatedBy,
                    p.CreatedDate,
                    p.UpdatedBy,
                    p.UpdatedDate,
                    // Sprint-2: posted-by audit fields for the workflow
                    // stepper. PostedBy/Date live on the Purchase entity.
                    p.PostedBy,
                    p.PostedDate,

                    Items = p.PurchaseItems
                        .Where(i => i.Deleted != true)
                        .Select(i => new
                        {
                            i.Id,
                            i.ItemType,
                            i.ItemId,
                            i.Qty,
                            Rate = i.Rate ?? 0m,
                            i.IsPosted,
                            Active = i.Active ?? true,
                            i.Remarks
                        })
                        .ToList()
                }
            ).FirstOrDefault();

            if (raw == null)
                return JsonError("Purchase not found.");

            var rawItemIds = raw.Items
                .Where(i => (i.ItemType ?? "").Trim().ToUpper() == "RAW")
                .Select(i => i.ItemId)
                .Distinct()
                .ToList();

            var threadItemIds = raw.Items
                .Where(i => (i.ItemType ?? "").Trim().ToUpper() == "THREAD")
                .Select(i => i.ItemId)
                .Distinct()
                .ToList();

            var detailFabricIds = raw.Items
                .Where(i => (i.ItemType ?? "").Trim().ToUpper() == "FABRIC")
                .Select(i => i.ItemId)
                .Distinct()
                .ToList();

            var rawMap = db.Gen_RawItems
                .Where(x => rawItemIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            var detailFabricMap = db.Gen_RawItems
                .Where(x => detailFabricIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            // Phase E: prefer linked RawItem name for threads
            var detailThreadInfo = db.Gen_Threads
                .Where(x => threadItemIds.Contains(x.Id))
                .Select(x => new { x.Id, x.RawItemId, x.Code, x.Name })
                .ToList();

            var detailLinkedRawIds = detailThreadInfo.Where(t => t.RawItemId.HasValue).Select(t => t.RawItemId.Value).Distinct().ToList();
            var detailLinkedRawNames = db.Gen_RawItems
                .Where(r => detailLinkedRawIds.Contains(r.Id))
                .ToDictionary(r => r.Id, r => r.ItemName);

            var threadMap = detailThreadInfo.ToDictionary(
                t => t.Id,
                t => t.RawItemId.HasValue && detailLinkedRawNames.ContainsKey(t.RawItemId.Value)
                     ? detailLinkedRawNames[t.RawItemId.Value]
                     : "[unlinked] " + (t.Code ?? "") + " " + (t.Name ?? "")
            );

            // Sprint-1: PO source single-row lookup (EDMX has no PurchaseOrderId).
            int? poSourceId = null; string poSourceCode = null;
            var poSource = db.Database.SqlQuery<PoSourceRow>(@"
SELECT  p.Id AS PurchaseId,
        ISNULL(p.PurchaseOrderId, 0) AS PoId,
        po.PoCode
  FROM  dbo.Purchase p
  LEFT JOIN dbo.PurchaseOrder po ON po.Id = p.PurchaseOrderId AND ISNULL(po.Deleted, 0) = 0
 WHERE  p.Id = @id AND p.PurchaseOrderId IS NOT NULL",
                new SqlParameter("@id", id)).FirstOrDefault();
            if (poSource != null && poSource.PoId > 0)
            {
                poSourceId = poSource.PoId;
                poSourceCode = poSource.PoCode;
            }

            // Sprint-1: is this purchase reversed?  Same marker scan as List.
            //   ReferenceId is nullable; the .HasValue guard matters so
            //   EF doesn't fall through to `NULL == @id` (which is never
            //   true in SQL and silently returns no rows).
            bool isReversedDetail = db.Gen_Store.Any(s =>
                s.ReferenceType == "PURCHASE"
                && s.ReferenceId.HasValue
                && s.ReferenceId.Value == id
                && s.Remarks != null
                && s.Remarks.Contains("[REVERSED PUR-REV-"));

            string statusDetail = raw.IsPosted
                ? PurchaseStatus.POSTED
                : (isReversedDetail ? PurchaseStatus.REVERSED : PurchaseStatus.DRAFT);

            var data = new PurchaseDetailViewModel
            {
                Id = raw.Id,

                PurchaseDate = raw.PurchaseDate.ToString("yyyy-MM-dd"),
                PurchaseDateText = raw.PurchaseDate.ToString("dd-MMMM-yyyy"),

                InvoiceNo = raw.InvoiceNo,
                Remarks = raw.Remarks,
                HasInvoice = !string.IsNullOrWhiteSpace(raw.InvoiceNo)
                             && raw.InvoiceNo.Trim() != "-",

                SupplierId = raw.SupplierId,
                SupplierName = raw.SupplierName,

                PurchaseOrderId = poSourceId,
                PoCode = poSourceCode,

                BranchId = raw.BranchId,
                BranchName = raw.BranchName,

                TotalQty = raw.Items.Sum(i => i.Qty),
                TotalAmount = raw.Items.Sum(i => i.Qty * i.Rate),

                IsPosted = raw.IsPosted,
                Active = raw.Active ?? true,
                IsReversed = isReversedDetail,
                Status = statusDetail,

                CreatedByUserId = raw.CreatedBy,
                CreatedByEmployeeCode = GetEmployeeCodeByUserId(raw.CreatedBy),
                CreatedDate = raw.CreatedDate,

                UpdatedByUserId = raw.UpdatedBy,
                UpdatedByEmployeeCode = GetEmployeeCodeByUserId(raw.UpdatedBy),
                UpdatedDate = raw.UpdatedDate,

                PostedByUserId = raw.PostedBy,
                PostedByEmployeeCode = GetEmployeeCodeByUserId(raw.PostedBy),
                PostedDate = raw.PostedDate,

                WorkflowStages = _purchaseWorkflowStages,
                WorkflowStageIndex = ComputePurchaseWorkflowStageIndex(statusDetail),

                Items = raw.Items.Select(i =>
                {
                    var t = (i.ItemType ?? "RAW").Trim().ToUpper();
                    string itemName = "-";
                    if (t == "RAW" && rawMap.ContainsKey(i.ItemId)) itemName = rawMap[i.ItemId];
                    else if (t == "THREAD" && threadMap.ContainsKey(i.ItemId)) itemName = threadMap[i.ItemId];
                    else if (t == "FABRIC" && detailFabricMap.ContainsKey(i.ItemId)) itemName = detailFabricMap[i.ItemId];

                    return new PurchaseDetailItemViewModel
                    {
                        Id = i.Id,
                        ItemType = t,
                        ItemId = i.ItemId,
                        ItemName = itemName,
                        Qty = i.Qty,
                        Rate = i.Rate,
                        Amount = i.Qty * i.Rate,
                        IsPosted = i.IsPosted,
                        Active = i.Active,
                        Remarks = i.Remarks
                    };
                }).ToList()
            };

            // Phase F.1: Stock ledger trail — Gen_Store rows written by this purchase when posted.
            var ledgerRows = (
                from s in db.Gen_Store
                join r in db.Gen_RawItems on s.ItemId equals r.Id into rr
                from r in rr.DefaultIfEmpty()
                where s.ReferenceType == "PURCHASE"
                   && s.ReferenceId == id
                   && s.Deleted != true
                orderby s.CreatedDate, s.Id
                select new
                {
                    s.Id,
                    s.TransactionDate,
                    s.ItemId,
                    ItemName = r != null ? r.ItemName : "-",
                    s.Unit,
                    s.TransactionType,
                    s.ReferenceType,
                    s.ReferenceNo,
                    s.OpeningQty,
                    s.InQty,
                    s.OutQty,
                    s.CurrentQty,
                    s.Rate,
                    s.Amount,
                    s.Remarks,
                    s.Active
                }
            ).ToList();

            data.Ledger = ledgerRows.Select(s => new PurchaseLedgerEntryViewModel
            {
                Id = s.Id,
                TransactionDateText = s.TransactionDate.ToString("dd-MMM-yyyy"),
                ItemId = s.ItemId,
                ItemName = s.ItemName,
                Unit = s.Unit,
                TransactionType = s.TransactionType,
                ReferenceType = s.ReferenceType,
                ReferenceNo = s.ReferenceNo,
                OpeningQty = s.OpeningQty ?? 0m,
                InQty = s.InQty ?? 0m,
                OutQty = s.OutQty ?? 0m,
                CurrentQty = s.CurrentQty ?? 0m,
                Rate = s.Rate ?? 0m,
                Amount = s.Amount ?? 0m,
                Remarks = s.Remarks,
                Active = s.Active
            }).ToList();

            // FABRIC purchases flow through Gen_Store keyed directly by the
            // Gen_RawItems.Id (Type='FABRIC'), so the trail above already covers them.

            return JsonSuccess("OK", data);
        }

        [HttpPost]
        [Route("create")]
        public ActionResult Create(PurchaseDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any())
                return JsonError("Purchase must contain at least one item.");

            if (string.IsNullOrWhiteSpace(dto.PurchaseDate))
                return JsonError("Purchase date is required.");

            if (!DateTime.TryParse(dto.PurchaseDate, out DateTime purchaseDate))
                return JsonError("Purchase date is invalid.");

            if (dto.BranchId <= 0)
                return JsonError("Branch is required.");

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    var now = DateTime.UtcNow;

                    var purchase = new Purchase
                    {
                        PurchaseDate = purchaseDate,
                        BranchId = dto.BranchId,
                        SupplierId = dto.SupplierId,
                        InvoiceNo = dto.InvoiceNo?.Trim(),
                        Remarks = dto.Remarks?.Trim(),

                        IsPosted = false,
                        Active = true,
                        Deleted = false,

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now
                    };

                    db.Purchase.Add(purchase);
                    db.SaveChanges();

                    // PO linkage (header). Raw SQL because the EDMX Purchase
                    // entity hasn't been regenerated to include PurchaseOrderId.
                    if (dto.PurchaseOrderId.HasValue && dto.PurchaseOrderId.Value > 0)
                    {
                        db.Database.ExecuteSqlCommand(
                            "UPDATE dbo.Purchase SET PurchaseOrderId = @poId WHERE Id = @id",
                            new System.Data.SqlClient.SqlParameter("@poId", dto.PurchaseOrderId.Value),
                            new System.Data.SqlClient.SqlParameter("@id",   purchase.Id));
                    }

                    foreach (var i in dto.Items)
                    {
                        if (i.ItemId <= 0)
                            return JsonError("Invalid item selected.");

                        if (i.Qty <= 0)
                            return JsonError("Invalid quantity.");

                        if (i.Rate < 0)
                            return JsonError("Invalid rate.");

                        string itemType = (i.ItemType ?? "RAW").Trim().ToUpper();
                        if (itemType != "RAW" && itemType != "THREAD" && itemType != "FABRIC")
                            return JsonError("Item type must be RAW, THREAD or FABRIC.");

                        if (itemType == "RAW")
                        {
                            bool rawExists = db.Gen_RawItems.Any(x => x.Id == i.ItemId && x.Deleted != true);
                            if (!rawExists)
                                return JsonError("Selected raw item does not exist.");
                        }
                        else if (itemType == "THREAD")
                        {
                            // Sprint-4: validate thread → raw-item link at
                            // Create/Update-time instead of letting the post
                            // step blow up days later. Operator gets the
                            // error on the row they entered, not after a
                            // long workflow.
                            var thr = db.Gen_Threads.FirstOrDefault(x => x.Id == i.ItemId && x.Deleted != true);
                            if (thr == null)
                                return JsonError("Selected thread does not exist.");
                            if (!thr.RawItemId.HasValue || thr.RawItemId.Value <= 0)
                                return JsonError($"Thread {thr.Code} {thr.Name} is not linked to a raw item. Open the Threads master and link it before purchasing.");
                        }
                        else // FABRIC
                        {
                            bool fabricExists = db.Gen_RawItems.Any(x => x.Id == i.ItemId
                                                                     && x.Deleted != true
                                                                     && x.Type != null
                                                                     && x.Type.ToUpper() == "FABRIC");
                            if (!fabricExists)
                                return JsonError("Selected fabric does not exist or is not a FABRIC raw item.");
                        }

                        // Cap the GRN line against the linked PO line's outstanding
                        // quantity (OrderedQty - ReceivedQty). Without this, a GRN
                        // can over-receive a PO line, which drives SyncReceipts'
                        // outstanding figure negative and hides the over-receipt
                        // instead of flagging it. No existing "allow over-receipt"
                        // override flow exists in this codebase, so this is a hard block.
                        if (i.PoItemId.HasValue && i.PoItemId.Value > 0)
                        {
                            var poLine = db.PurchaseOrderItems
                                .FirstOrDefault(x => x.Id == i.PoItemId.Value && x.Deleted != true);
                            if (poLine == null)
                                return JsonError("Linked purchase order line not found.");

                            decimal outstanding = poLine.OrderedQty - poLine.ReceivedQty;

                            // Multiple GRN lines in this same submission can reference
                            // the same PO line; account for quantity already claimed
                            // earlier in this request before checking this one.
                            decimal alreadyQueuedInThisRequest = dto.Items
                                .TakeWhile(x => !ReferenceEquals(x, i))
                                .Where(x => x.PoItemId.HasValue && x.PoItemId.Value == i.PoItemId.Value)
                                .Sum(x => x.Qty);

                            decimal remainingOutstanding = outstanding - alreadyQueuedInThisRequest;

                            if (i.Qty > remainingOutstanding)
                                return JsonError(string.Format(
                                    "Quantity {0} exceeds the outstanding PO quantity ({1}) for this line.",
                                    i.Qty, Math.Max(remainingOutstanding, 0m)));
                        }

                        var purchaseItem = new PurchaseItems
                        {
                            PurchaseId = purchase.Id,
                            ItemType = itemType,
                            ItemId = i.ItemId,

                            Qty = i.Qty,
                            Rate = i.Rate,
                            Remarks = i.Remarks,

                            IsPosted = false,
                            Active = true,
                            Deleted = false,

                            CreatedBy = userId,
                            CreatedDate = now
                        };
                        db.PurchaseItems.Add(purchaseItem);
                    }

                    db.SaveChanges();

                    // After save, line Ids exist. Stamp PoItemId on each linked
                    // GRN line. Walk in insertion order; entity Ids match dto order.
                    if (dto.PurchaseOrderId.HasValue && dto.PurchaseOrderId.Value > 0)
                    {
                        var savedLines = db.PurchaseItems
                            .Where(x => x.PurchaseId == purchase.Id)
                            .OrderBy(x => x.Id)
                            .ToList();

                        for (int idx = 0; idx < dto.Items.Count && idx < savedLines.Count; idx++)
                        {
                            var dtoLine = dto.Items[idx];
                            if (dtoLine.PoItemId.HasValue && dtoLine.PoItemId.Value > 0)
                            {
                                db.Database.ExecuteSqlCommand(
                                    "UPDATE dbo.PurchaseItems SET PoItemId = @poItemId WHERE Id = @id",
                                    new System.Data.SqlClient.SqlParameter("@poItemId", dtoLine.PoItemId.Value),
                                    new System.Data.SqlClient.SqlParameter("@id", savedLines[idx].Id));
                            }
                        }
                    }

                    tx.Commit();

                    // Sprint-2: stamp the create on the audit log so the
                    // detail-drawer timeline has a starting point.
                    LogAudit(purchase.Id, "CREATED",
                        oldValue: null,
                        newValue: PurchaseStatus.DRAFT,
                        detail: dto.PurchaseOrderId.HasValue
                                ? "Source PO: #" + dto.PurchaseOrderId.Value
                                : "Ad-hoc draft");

                    return JsonSuccess("Purchase saved as draft.",
                        new { id = purchase.Id, purchaseOrderId = dto.PurchaseOrderId });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError(DescribePurchaseError(ex));
                }
            }
        }

        [HttpPost]
        [Route("update/{id:int}")]
        public ActionResult Update(int id, PurchaseDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any())
                return JsonError("Invalid data.");

            if (dto.BranchId <= 0)
                return JsonError("Branch is required.");

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var p = db.Purchase
                        .Include(x => x.PurchaseItems)
                        .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                    if (p == null)
                        return JsonError("Purchase not found.");

                    if (p.IsPosted)
                        return JsonError("Posted purchase cannot be edited.");

                    if (!DateTime.TryParse(dto.PurchaseDate, out DateTime parsedDate))
                        return JsonError("Purchase date is invalid.");

                    int userId = GetCurrentUserId();
                    var now = DateTime.UtcNow;

                    p.PurchaseDate = parsedDate;
                    p.BranchId = dto.BranchId;
                    p.SupplierId = dto.SupplierId;
                    p.InvoiceNo = dto.InvoiceNo?.Trim();
                    p.Remarks = dto.Remarks?.Trim();
                    p.UpdatedBy = userId;
                    p.UpdatedDate = now;

                    foreach (var old in p.PurchaseItems.Where(x => x.Deleted != true))
                    {
                        old.Deleted = true;
                        old.Active = false;
                    }

                    foreach (var i in dto.Items)
                    {
                        if (i.ItemId <= 0)
                            return JsonError("Invalid item selected.");

                        if (i.Qty <= 0)
                            return JsonError("Quantity must be greater than zero.");

                        if (i.Rate < 0)
                            return JsonError("Rate cannot be negative.");

                        string itemType = (i.ItemType ?? "RAW").Trim().ToUpper();
                        if (itemType != "RAW" && itemType != "THREAD" && itemType != "FABRIC")
                            return JsonError("Item type must be RAW, THREAD or FABRIC.");

                        if (itemType == "RAW")
                        {
                            bool rawExists = db.Gen_RawItems.Any(x => x.Id == i.ItemId && x.Deleted != true);
                            if (!rawExists)
                                return JsonError("Selected raw item does not exist.");
                        }
                        else if (itemType == "THREAD")
                        {
                            // Sprint-4: validate thread → raw-item link at
                            // Create/Update-time instead of letting the post
                            // step blow up days later. Operator gets the
                            // error on the row they entered, not after a
                            // long workflow.
                            var thr = db.Gen_Threads.FirstOrDefault(x => x.Id == i.ItemId && x.Deleted != true);
                            if (thr == null)
                                return JsonError("Selected thread does not exist.");
                            if (!thr.RawItemId.HasValue || thr.RawItemId.Value <= 0)
                                return JsonError($"Thread {thr.Code} {thr.Name} is not linked to a raw item. Open the Threads master and link it before purchasing.");
                        }
                        else // FABRIC
                        {
                            bool fabricExists = db.Gen_RawItems.Any(x => x.Id == i.ItemId
                                                                     && x.Deleted != true
                                                                     && x.Type != null
                                                                     && x.Type.ToUpper() == "FABRIC");
                            if (!fabricExists)
                                return JsonError("Selected fabric does not exist or is not a FABRIC raw item.");
                        }

                        db.PurchaseItems.Add(new PurchaseItems
                        {
                            PurchaseId = p.Id,
                            ItemType = itemType,
                            ItemId = i.ItemId,

                            Qty = i.Qty,
                            Rate = i.Rate,
                            Remarks = i.Remarks,

                            IsPosted = false,
                            Active = true,
                            Deleted = false,

                            CreatedBy = userId,
                            CreatedDate = now
                        });
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogAudit(id, "UPDATED",
                        oldValue: PurchaseStatus.DRAFT,
                        newValue: PurchaseStatus.DRAFT,
                        detail: dto.Items.Count + " line(s); invoice " + (dto.InvoiceNo ?? "-"));

                    return JsonSuccess("Purchase updated successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError(DescribePurchaseError(ex));
                }
            }
        }

        [HttpPost]
        [Route("post/{id:int}")]
        public ActionResult PostPurchase(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: TOCTOU guard. Two operators clicking Post
                    // on the same draft simultaneously could each pass the
                    // IsPosted check below and end up writing Gen_Store IN
                    // rows twice. A row-level UPDLOCK held until commit
                    // serialises them — the second caller's read blocks
                    // until the first commits, then sees IsPosted = true
                    // and exits cleanly.
                    db.Database.ExecuteSqlCommand(
                        "SELECT 1 FROM dbo.Purchase WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                        new SqlParameter("@id", id));

                    var purchase = db.Purchase
                        .Include(p => p.PurchaseItems)
                        .FirstOrDefault(p => p.Id == id && p.Deleted != true);

                    if (purchase == null)
                        return JsonError("Purchase not found.");

                    if (purchase.IsPosted)
                        return JsonError("Purchase already posted.");

                    if (purchase.BranchId <= 0)
                        return JsonError("Purchase branch not defined.");

                    var items = purchase.PurchaseItems
                        .Where(x => x.Deleted != true && (x.Active == true || x.Active == null))
                        .ToList();

                    if (!items.Any())
                        return JsonError("Purchase items not found.");

                    bool alreadyPostedToStore = db.Gen_Store.Any(x =>
                        x.ReferenceType == "PURCHASE" &&
                        x.ReferenceId == purchase.Id &&
                        x.Deleted == false);

                    if (alreadyPostedToStore)
                        return JsonError("Store transaction already exists for this purchase.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.UtcNow;
                    int postedCount = 0;

                    foreach (var item in items)
                    {
                        string itemType = (item.ItemType ?? "").Trim().ToUpper();

                        if (itemType != "RAW" && itemType != "THREAD" && itemType != "FABRIC")
                            continue;

                        if (item.ItemId <= 0)
                            return JsonError("Invalid item in purchase items.");

                        if (item.Qty <= 0)
                            return JsonError("Invalid quantity in purchase items.");

                        decimal qty = item.Qty;
                        decimal rate = item.Rate ?? 0m;

                        if (itemType == "RAW")
                        {
                            bool rawExists = db.Gen_RawItems.Any(x => x.Id == item.ItemId && x.Deleted != true);
                            if (!rawExists)
                                return JsonError("Selected raw item does not exist.");

                            decimal previousBalance = GetCurrentStoreBalance(item.ItemId, purchase.BranchId, null);

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = item.ItemId,
                                BranchId = purchase.BranchId,
                                WarehouseId = null,
                                TransactionDate = purchase.PurchaseDate,

                                TransactionType = "IN",
                                ReferenceType = "PURCHASE",
                                ReferenceId = purchase.Id,
                                ReferenceNo = purchase.InvoiceNo,

                                OpeningQty = previousBalance,
                                InQty = qty,
                                OutQty = 0m,
                                CurrentQty = previousBalance + qty,

                                Unit = GetRawItemUnit(item.ItemId),
                                Rate = rate,
                                Amount = qty * rate,

                                Remarks = string.IsNullOrWhiteSpace(item.Remarks)
                                         ? purchase.Remarks
                                         : item.Remarks,

                                Active = true,
                                Deleted = false,
                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            });
                        }
                        else if (itemType == "THREAD")
                        {
                            var thread = db.Gen_Threads.FirstOrDefault(x => x.Id == item.ItemId && x.Deleted != true);
                            if (thread == null)
                                return JsonError("Selected thread does not exist.");

                            // Phase E: threads write to Gen_Store via the linked RawItemId.
                            // Threads without a RawItemId can't be purchased through the new flow.
                            if (!thread.RawItemId.HasValue || thread.RawItemId.Value <= 0)
                                return JsonError($"Thread {thread.Code} {thread.Name} is not linked to a raw item. Open Threads and set its linked Raw Item before purchasing.");

                            int rawItemId = thread.RawItemId.Value;
                            decimal previousBalance = GetCurrentStoreBalance(rawItemId, purchase.BranchId, null);

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = rawItemId,
                                BranchId = purchase.BranchId,
                                WarehouseId = null,
                                TransactionDate = purchase.PurchaseDate,

                                TransactionType = "IN",
                                ReferenceType = "PURCHASE",
                                ReferenceId = purchase.Id,
                                ReferenceNo = purchase.InvoiceNo,

                                OpeningQty = previousBalance,
                                InQty = qty,
                                OutQty = 0m,
                                CurrentQty = previousBalance + qty,

                                Unit = thread.UnitOfMeasure ?? "MTR",
                                Rate = rate,
                                Amount = qty * rate,

                                Remarks = string.IsNullOrWhiteSpace(item.Remarks)
                                         ? purchase.Remarks
                                         : item.Remarks,

                                Active = true,
                                Deleted = false,
                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            });
                        }
                        else // FABRIC - item.ItemId is already a Gen_RawItems.Id (Type='FABRIC')
                        {
                            var rawItem = db.Gen_RawItems.FirstOrDefault(x => x.Id == item.ItemId && x.Deleted != true);
                            if (rawItem == null)
                                return JsonError("Selected fabric does not exist.");

                            decimal previousBalance = GetCurrentStoreBalance(item.ItemId, purchase.BranchId, null);

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = item.ItemId,
                                BranchId = purchase.BranchId,
                                WarehouseId = null,
                                TransactionDate = purchase.PurchaseDate,

                                TransactionType = "IN",
                                ReferenceType = "PURCHASE",
                                ReferenceId = purchase.Id,
                                ReferenceNo = purchase.InvoiceNo,

                                OpeningQty = previousBalance,
                                InQty = qty,
                                OutQty = 0m,
                                CurrentQty = previousBalance + qty,

                                Unit = GetRawItemUnit(item.ItemId),
                                Rate = rate,
                                Amount = qty * rate,

                                Remarks = string.IsNullOrWhiteSpace(item.Remarks)
                                         ? purchase.Remarks
                                         : item.Remarks,

                                Active = true,
                                Deleted = false,
                                CreatedBy = userId,
                                CreatedDate = now,
                                UpdatedBy = userId,
                                UpdatedDate = now,
                                RecordTimeStamp = now
                            });
                        }

                        item.IsPosted = true;
                        postedCount++;
                    }

                    if (postedCount == 0)
                        return JsonError("No RAW, THREAD or FABRIC purchase items found to post.");

                    purchase.IsPosted = true;
                    purchase.PostedBy = userId;
                    purchase.PostedDate = now;
                    purchase.UpdatedBy = userId;
                    purchase.UpdatedDate = now;

                    db.SaveChanges();
                    tx.Commit();

                    LogAudit(purchase.Id, "POSTED",
                        oldValue: PurchaseStatus.DRAFT,
                        newValue: PurchaseStatus.POSTED,
                        detail: postedCount + " line(s) written to Gen_Store");

                    return JsonSuccess("Purchase posted to raw store successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Post failed. " + DescribePurchaseError(ex));
                }
            }
        }

        [HttpGet]
        [Route("preview-post/{id:int}")]
        public ActionResult PreviewPost(int id)
        {
            var purchase = db.Purchase.FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (purchase == null)
                return JsonError("Purchase not found.");

            var items = db.PurchaseItems
                .Where(i => i.PurchaseId == id && i.Deleted != true)
                .Select(i => new
                {
                    i.ItemId,
                    i.ItemType,
                    i.Qty,
                    Rate = i.Rate ?? 0m
                })
                .ToList()
                .Where(i =>
                {
                    var t = (i.ItemType ?? "").Trim().ToUpper();
                    return t == "RAW" || t == "THREAD" || t == "FABRIC";
                })
                .ToList();

            if (!items.Any())
                return JsonSuccess("OK", new List<object>());

            var rawIds = items.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "RAW").Select(i => i.ItemId).Distinct().ToList();
            var threadIds = items.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "THREAD").Select(i => i.ItemId).Distinct().ToList();
            var fabricIds = items.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "FABRIC").Select(i => i.ItemId).Distinct().ToList();
            var fabricMap = db.Gen_RawItems
                .Where(x => fabricIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            var rawMap = db.Gen_RawItems
                .Where(x => rawIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x.ItemName);

            // Phase E: thread display uses the linked RawItem name, not the THR-#### code
            var threadRows = db.Gen_Threads
                .Where(x => threadIds.Contains(x.Id))
                .Select(x => new { x.Id, x.RawItemId, x.Code, x.Name, x.Color })
                .ToList();

            var linkedRawIds = threadRows.Where(t => t.RawItemId.HasValue).Select(t => t.RawItemId.Value).Distinct().ToList();
            var linkedRawNameMap = db.Gen_RawItems
                .Where(r => linkedRawIds.Contains(r.Id))
                .ToDictionary(r => r.Id, r => r.ItemName);

            var threadDisplayMap = threadRows.ToDictionary(
                t => t.Id,
                t => t.RawItemId.HasValue && linkedRawNameMap.ContainsKey(t.RawItemId.Value)
                     ? linkedRawNameMap[t.RawItemId.Value]
                     : "[unlinked] " + (t.Code ?? "") + " " + (t.Name ?? "")
            );

            var threadRawIdMap = threadRows
                .Where(t => t.RawItemId.HasValue)
                .ToDictionary(t => t.Id, t => t.RawItemId.Value);

            // FABRIC ItemId is already a Gen_RawItems.Id — no remap needed.

            var preview = items.Select(i =>
            {
                var t = (i.ItemType ?? "RAW").Trim().ToUpper();
                int lookupId = i.ItemId;
                if (t == "THREAD" && threadRawIdMap.ContainsKey(i.ItemId))
                    lookupId = threadRawIdMap[i.ItemId];

                decimal current = GetCurrentStoreBalance(lookupId, purchase.BranchId, null);

                string name;
                if (t == "RAW")
                    name = rawMap.ContainsKey(i.ItemId) ? rawMap[i.ItemId] : "-";
                else if (t == "THREAD")
                    name = threadDisplayMap.ContainsKey(i.ItemId) ? threadDisplayMap[i.ItemId] : "-";
                else // FABRIC
                    name = fabricMap.ContainsKey(i.ItemId) ? fabricMap[i.ItemId] : "-";

                return new
                {
                    itemId = i.ItemId,
                    itemType = t,
                    itemName = name,
                    qty = i.Qty,
                    rate = i.Rate,
                    amount = i.Qty * i.Rate,
                    currentStock = current,
                    afterPost = current + i.Qty
                };
            }).ToList();

            return JsonSuccess("OK", preview);
        }

        // ================================================================
        // REVERSE — un-post a posted purchase. BLOCKS if any THREAD it brought
        // in has been partially consumed downstream (would create negative stock).
        // Soft-deletes the IN rows and writes compensating OUT rows for audit.
        // ================================================================
        [HttpPost]
        [Route("reverse/{id:int}")]
        public ActionResult Reverse(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: same TOCTOU guard as Post. Two Reverse
                    // calls on the same posted GRN would race the balance
                    // check; serialise them on a row-level UPDLOCK.
                    db.Database.ExecuteSqlCommand(
                        "SELECT 1 FROM dbo.Purchase WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                        new SqlParameter("@id", id));

                    var purchase = db.Purchase
                        .Include(p => p.PurchaseItems)
                        .FirstOrDefault(p => p.Id == id && p.Deleted != true);

                    if (purchase == null)
                        return JsonError("Purchase not found.");

                    if (!purchase.IsPosted)
                        return JsonError("Purchase is not posted, nothing to reverse.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.UtcNow;
                    string reverseTag = "PUR-REV-" + purchase.Id;

                    // ── Phase E guard rail: for every Gen_Store IN row this purchase wrote,
                    //    the linked item's current balance must be >= the qty brought in.
                    //    Otherwise downstream issues consumed it and reversal would underflow.
                    var storeInRows = db.Gen_Store
                        .Where(s => s.ReferenceType == "PURCHASE"
                                    && s.ReferenceId == purchase.Id
                                    && s.TransactionType == "IN"
                                    && s.Deleted == false)
                        .ToList();

                    foreach (var r in storeInRows)
                    {
                        decimal balance = GetCurrentStoreBalance(r.ItemId, purchase.BranchId, null);
                        decimal broughtIn = r.InQty ?? 0m;
                        if (balance < broughtIn)
                        {
                            var ri = db.Gen_RawItems.FirstOrDefault(x => x.Id == r.ItemId);
                            string label = ri != null ? ri.ItemName : ("Item #" + r.ItemId);
                            return JsonError($"Cannot reverse: '{label}' has been consumed downstream. Available {balance}, purchase brought {broughtIn}. Reverse the relevant material issues first.");
                        }
                    }

                    // ── Reversal: soft-delete the Gen_Store IN rows (one ledger for RAW + THREAD + FABRIC now)
                    foreach (var r in storeInRows)
                    {
                        r.Deleted = true;
                        r.Active = false;
                        r.UpdatedBy = userId;
                        r.UpdatedDate = now;
                        r.Remarks = ((r.Remarks ?? "") + " [REVERSED " + reverseTag + "]").Trim();
                    }

                    purchase.IsPosted = false;
                    purchase.PostedBy = null;
                    purchase.PostedDate = null;
                    purchase.UpdatedBy = userId;
                    purchase.UpdatedDate = now;

                    foreach (var item in purchase.PurchaseItems.Where(i => i.Deleted != true))
                        item.IsPosted = false;

                    db.SaveChanges();
                    tx.Commit();

                    LogAudit(purchase.Id, "REVERSED",
                        oldValue: PurchaseStatus.POSTED,
                        newValue: PurchaseStatus.REVERSED,
                        detail: "Tag: " + reverseTag);

                    return JsonSuccess("Purchase reversed.", new { id = purchase.Id, reverseTag });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Reverse failed. " + DescribePurchaseError(ex));
                }
            }
        }

        // ================================================================
        // RETURN MATERIAL TO SUPPLIER
        // ----------------------------------------------------------------
        // Lightweight purchase-return flow: stay on the Purchase module,
        // skip the supplier-finance side (no debit note). Each return
        // writes Gen_Store OUT rows tagged ReferenceType=PURCHASE_RETURN
        // and ReferenceId=<original Purchase.Id> so audit can link back.
        // ================================================================

        // ----------- Return info: rows + per-item returnable qty -------
        [HttpGet, Route("return-info/{id:int}")]
        public ActionResult ReturnInfo(int id)
        {
            try
            {
                var purchase = db.Purchase
                    .Include(p => p.PurchaseItems)
                    .FirstOrDefault(p => p.Id == id && p.Deleted != true);

                if (purchase == null) return JsonError("Purchase not found.");
                if (!purchase.IsPosted) return JsonError("Only posted purchases can be returned.");

                int branchId = purchase.BranchId;

                // Total qty already returned per item across all prior return rounds.
                var returnedByItem = db.Gen_Store
                    .Where(s => s.ReferenceType == "PURCHASE_RETURN"
                                && s.ReferenceId == purchase.Id
                                && s.Deleted == false)
                    .GroupBy(s => s.ItemId)
                    .Select(g => new { ItemId = g.Key, Returned = g.Sum(x => (decimal?)x.OutQty) ?? 0m })
                    .ToList()
                    .ToDictionary(x => x.ItemId, x => x.Returned);

                var lines = purchase.PurchaseItems
                    .Where(i => i.Deleted != true)
                    .Select(i =>
                    {
                        var raw = db.Gen_RawItems.FirstOrDefault(r => r.Id == i.ItemId);
                        decimal alreadyReturned = returnedByItem.ContainsKey(i.ItemId) ? returnedByItem[i.ItemId] : 0m;
                        decimal currentBalance = GetCurrentStoreBalance(i.ItemId, branchId, null);

                        // Cap the returnable amount by both the original purchase qty
                        // (less what's already been returned) and the current stock
                        // balance — can't return what's no longer there.
                        decimal returnableByPurchase = Math.Max(0m, i.Qty - alreadyReturned);
                        decimal returnableQty = Math.Min(returnableByPurchase, currentBalance);

                        string unitCode = "";
                        if (raw != null)
                        {
                            unitCode = db.Gen_Units
                                .Where(u => u.Id == raw.UnitId)
                                .Select(u => u.UnitCode)
                                .FirstOrDefault() ?? "";
                        }

                        return new
                        {
                            purchaseItemId = i.Id,
                            itemId = i.ItemId,
                            itemType = i.ItemType,
                            itemName = raw != null ? raw.ItemName : ("Item #" + i.ItemId),
                            unit = unitCode,
                            originalQty = i.Qty,
                            rate = i.Rate ?? 0m,
                            alreadyReturned,
                            currentBalance,
                            returnableQty
                        };
                    })
                    .ToList();

                var supplierName = db.Gen_Suppliers
                    .Where(s => s.Id == purchase.SupplierId)
                    .Select(s => s.SupplierName)
                    .FirstOrDefault();

                return JsonSuccess("OK", new
                {
                    purchaseId = purchase.Id,
                    invoiceNo = purchase.InvoiceNo,
                    supplierId = purchase.SupplierId,
                    supplierName,
                    branchId,
                    purchaseDate = purchase.PurchaseDate,
                    lines
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load return info.", ex);
            }
        }

        // ----------- Submit the return ---------------------------------
        [HttpPost, Route("return/{id:int}")]
        public ActionResult ReturnToSupplier(int id, PurchaseReturnDto dto)
        {
            if (dto == null || dto.Lines == null || !dto.Lines.Any(l => l.ReturnQty > 0))
                return JsonError("At least one line must have a return quantity greater than zero.");

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var purchase = db.Purchase
                        .Include(p => p.PurchaseItems)
                        .FirstOrDefault(p => p.Id == id && p.Deleted != true);

                    if (purchase == null) return JsonError("Purchase not found.");
                    if (!purchase.IsPosted) return JsonError("Only posted purchases can be returned.");

                    int branchId = purchase.BranchId;
                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.UtcNow;
                    string reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

                    string returnCode = NextPurchaseReturnCode(now.Year);

                    // Pre-validate every line before any write.
                    foreach (var line in dto.Lines.Where(l => l.ReturnQty > 0))
                    {
                        var item = purchase.PurchaseItems.FirstOrDefault(i => i.Id == line.PurchaseItemId && i.Deleted != true);
                        if (item == null)
                            return JsonError("Invalid purchase item: " + line.PurchaseItemId);

                        decimal alreadyReturned = db.Gen_Store
                            .Where(s => s.ReferenceType == "PURCHASE_RETURN"
                                        && s.ReferenceId == purchase.Id
                                        && s.ItemId == item.ItemId
                                        && s.Deleted == false)
                            .Sum(s => (decimal?)s.OutQty) ?? 0m;

                        decimal currentBalance = GetCurrentStoreBalance(item.ItemId, branchId, null);
                        decimal returnableByPurchase = Math.Max(0m, item.Qty - alreadyReturned);
                        decimal returnableQty = Math.Min(returnableByPurchase, currentBalance);

                        if (line.ReturnQty > returnableQty)
                        {
                            var raw = db.Gen_RawItems.FirstOrDefault(r => r.Id == item.ItemId);
                            string label = raw != null ? raw.ItemName : ("Item #" + item.ItemId);
                            return JsonError($"Return qty for '{label}' exceeds returnable ({returnableQty}). Either stock has been consumed downstream or you've already returned this much.");
                        }
                    }

                    int linesWritten = 0;
                    var affectedPoIds = new HashSet<int>();

                    foreach (var line in dto.Lines.Where(l => l.ReturnQty > 0))
                    {
                        var item = purchase.PurchaseItems.First(i => i.Id == line.PurchaseItemId);

                        decimal opening = GetCurrentStoreBalance(item.ItemId, branchId, null);
                        decimal rate = item.Rate ?? 0m;

                        string lineRemarks = string.IsNullOrWhiteSpace(line.Remarks) ? null : line.Remarks.Trim();
                        string fullRemarks = "Returned to supplier"
                            + (reason != null ? " | " + reason : "")
                            + (lineRemarks != null ? " | " + lineRemarks : "");

                        db.Gen_Store.Add(new Gen_Store
                        {
                            ItemId = item.ItemId,
                            BranchId = branchId,
                            WarehouseId = null,
                            TransactionDate = now,

                            TransactionType = "OUT",
                            ReferenceType = "PURCHASE_RETURN",
                            ReferenceId = purchase.Id,
                            ReferenceNo = returnCode,

                            OpeningQty = opening,
                            InQty = 0m,
                            OutQty = line.ReturnQty,
                            CurrentQty = opening - line.ReturnQty,

                            Unit = (
                                from r in db.Gen_RawItems
                                join u in db.Gen_Units on r.UnitId equals u.Id
                                where r.Id == item.ItemId
                                select u.UnitCode
                            ).FirstOrDefault() ?? "",
                            Rate = rate,
                            Amount = line.ReturnQty * rate,

                            Remarks = fullRemarks,

                            Active = true,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now,
                            UpdatedBy = userId,
                            UpdatedDate = now,
                            RecordTimeStamp = now
                        });

                        linesWritten++;

                        // Keep the originating PO's receipt KPIs/status consistent:
                        // a return un-receives stock, so roll the returned qty back
                        // off PurchaseOrderItems.ReceivedQty (never below zero). We
                        // adjust directly here rather than calling PurchaseOrders'
                        // /sync-receipts, because that endpoint recomputes ReceivedQty
                        // by re-summing the full (unreturned) PurchaseItems.Qty per PO
                        // line — it has no notion of returns, so it would simply undo
                        // this adjustment back up to the original received quantity.
                        if (item.PoItemId.HasValue && item.PoItemId.Value > 0)
                        {
                            var poItem = db.PurchaseOrderItems.FirstOrDefault(x => x.Id == item.PoItemId.Value);
                            if (poItem != null)
                            {
                                poItem.ReceivedQty = Math.Max(0m, poItem.ReceivedQty - line.ReturnQty);
                                poItem.UpdatedBy = userId;
                                poItem.UpdatedDate = now;
                                affectedPoIds.Add(poItem.PoId);
                            }
                        }
                    }

                    // Re-derive each affected PO's header ReceivedAmount/Status from
                    // the now-adjusted PurchaseOrderItems rows, mirroring the same
                    // cap-at-ordered / any-received / all-met rules PurchaseOrders'
                    // SyncReceipts uses, so the two stay consistent with each other.
                    foreach (var poId in affectedPoIds)
                    {
                        var poItems = db.PurchaseOrderItems
                            .Where(x => x.PoId == poId && x.Deleted != true)
                            .ToList();

                        decimal receivedAmount = poItems.Sum(x =>
                            (x.ReceivedQty > x.OrderedQty ? x.OrderedQty : x.ReceivedQty) * x.Rate);
                        bool anyReceived = poItems.Any(x => x.ReceivedQty > 0);
                        bool allMet = poItems.All(x => x.ReceivedQty >= x.OrderedQty);

                        var po = db.PurchaseOrder.FirstOrDefault(x => x.Id == poId);
                        if (po != null)
                        {
                            po.ReceivedAmount = receivedAmount;
                            if (anyReceived && allMet) po.Status = "FULLY_RECEIVED";
                            else if (anyReceived) po.Status = "PARTIALLY_RECEIVED";
                            // If the return zeroed out every line, leave Status as-is —
                            // same behavior as SyncReceipts when nothing is received.
                            po.UpdatedBy = userId;
                            po.UpdatedDate = now;
                            po.RecordTimeStamp = now;
                        }
                    }

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess($"Returned {linesWritten} item(s) to supplier ({returnCode}).", new
                    {
                        returnCode,
                        linesWritten,
                        purchaseId = purchase.Id
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Return failed.", ex);
                }
            }
        }

        // ----------- Return history for a purchase ---------------------
        [HttpGet, Route("returns/{id:int}")]
        public ActionResult ReturnHistory(int id)
        {
            try
            {
                var rows = db.Gen_Store
                    .Where(s => s.ReferenceType == "PURCHASE_RETURN"
                                && s.ReferenceId == id
                                && s.Deleted == false)
                    .OrderByDescending(s => s.Id)
                    .ToList()
                    .Select(s => new
                    {
                        id = s.Id,
                        returnCode = s.ReferenceNo,
                        itemId = s.ItemId,
                        itemName = db.Gen_RawItems.Where(r => r.Id == s.ItemId).Select(r => r.ItemName).FirstOrDefault() ?? ("Item #" + s.ItemId),
                        qty = s.OutQty ?? 0m,
                        rate = s.Rate ?? 0m,
                        amount = s.Amount ?? 0m,
                        unit = s.Unit,
                        date = s.TransactionDate,
                        remarks = s.Remarks
                    })
                    .ToList();

                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load return history.", ex);
            }
        }

        // PR code generator: PR-YYYY-NNNN, scoped by year via Gen_Store
        private string NextPurchaseReturnCode(int year)
        {
            string prefix = "PR-" + year + "-";
            int next = 1;

            var lastRef = db.Gen_Store
                .Where(s => s.ReferenceType == "PURCHASE_RETURN"
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

        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            var p = db.Purchase.FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (p == null)
                return JsonError("Purchase not found.");

            if (p.IsPosted)
                return JsonError("Posted purchase cannot be deleted.");

            int userId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            p.Deleted = true;
            p.Active = false;
            p.UpdatedBy = userId;
            p.UpdatedDate = now;

            var items = db.PurchaseItems.Where(x => x.PurchaseId == id && x.Deleted != true).ToList();

            foreach (var item in items)
            {
                item.Deleted = true;
                item.Active = false;
                // Draft purchases (the only ones Delete allows, per the IsPosted
                // guard above) never posted a Gen_Store entry, so their lines
                // must stay IsPosted = false — leave it untouched rather than
                // marking them posted.
            }

            db.SaveChanges();

            LogAudit(id, "DELETED",
                oldValue: PurchaseStatus.DRAFT,
                newValue: "DELETED",
                detail: items.Count + " line(s) soft-deleted");

            return JsonSuccess("Purchase deleted.");
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        //  Each endpoint accepts { ids: int[] } and returns the standard
        //  bulk envelope: { successCount, failureCount, failures }.
        //  Per-row failures don't break the batch — they're reported in
        //  the response and rendered in the toolbar's "n succeeded, m
        //  failed (reason: ...)" banner. Each row runs in its own action
        //  call so the existing Post/Reverse/Delete transactions stay
        //  atomic per-row.
        // ─────────────────────────────────────────────────────────────
        public class BulkIdsDto { public List<int> Ids { get; set; } }

        private class BulkFailure
        {
            public int id;
            public string invoiceNo;
            public string reason;
        }

        private object DoBulkPurchase(List<int> ids, Func<int, Purchase, string> runOne)
        {
            var failures = new List<BulkFailure>();
            int ok = 0;

            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var p = db.Purchase.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (p == null)
                {
                    failures.Add(new BulkFailure { id = id, invoiceNo = "(missing)", reason = "Not found." });
                    continue;
                }
                try
                {
                    string err = runOne(id, p);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new BulkFailure { id = id, invoiceNo = p.InvoiceNo ?? "-", reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new BulkFailure { id = id, invoiceNo = p.InvoiceNo ?? "-", reason = DescribePurchaseError(ex) });
                }
            }

            return new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures = failures.Select(f => new { id = f.id, code = f.invoiceNo, reason = f.reason })
            };
        }

        [HttpPost, Route("bulk-post")]
        public ActionResult BulkPost(BulkIdsDto dto)
        {
            var data = DoBulkPurchase(dto?.Ids, (id, p) =>
            {
                var res = PostPurchase(id) as JsonResult;
                if (res?.Data == null) return "Post failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk post completed.", data);
        }

        [HttpPost, Route("bulk-reverse")]
        public ActionResult BulkReverse(BulkIdsDto dto)
        {
            var data = DoBulkPurchase(dto?.Ids, (id, p) =>
            {
                var res = Reverse(id) as JsonResult;
                if (res?.Data == null) return "Reverse failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk reverse completed.", data);
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(BulkIdsDto dto)
        {
            var data = DoBulkPurchase(dto?.Ids, (id, p) =>
            {
                var res = Delete(id) as JsonResult;
                if (res?.Data == null) return "Delete failed.";
                var ok = (bool)res.Data.GetType().GetProperty("success").GetValue(res.Data);
                if (ok) return null;
                return (string)res.Data.GetType().GetProperty("message").GetValue(res.Data);
            });
            return JsonSuccess("Bulk delete completed.", data);
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  CLONE WITH OVERRIDES
        //  The legacy /duplicate endpoint commits immediately. /clone is
        //  the modern preview-then-save variant — the client opens the
        //  form pre-filled, the buyer tweaks qty/rate/invoice/remarks,
        //  and only THEN calls /clone with the edited values. Per-line
        //  overrides are keyed by source PurchaseItem.Id; missing keys
        //  fall back to the source values.
        // ─────────────────────────────────────────────────────────────
        public class PurchaseCloneOverrideItem
        {
            public int      SourceItemId { get; set; }   // PurchaseItems.Id
            public decimal? Qty          { get; set; }
            public decimal? Rate         { get; set; }
            public string   Remarks      { get; set; }
        }

        public class PurchaseCloneOverrideDto
        {
            public string PurchaseDate { get; set; }
            public string InvoiceNo    { get; set; }
            public string Remarks      { get; set; }
            public List<PurchaseCloneOverrideItem> Items { get; set; }
        }

        [HttpPost, Route("clone/{id:int}")]
        public ActionResult Clone(int id, PurchaseCloneOverrideDto overrides = null)
        {
            var src = db.Purchase.FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (src == null) return JsonError("Source purchase not found.");

            var srcItems = db.PurchaseItems
                .Where(x => x.PurchaseId == id && x.Deleted != true)
                .ToList();
            if (!srcItems.Any()) return JsonError("Source purchase has no items to copy.");

            // Build per-line override map (by source PurchaseItem.Id).
            var overrideByItem = new Dictionary<int, PurchaseCloneOverrideItem>();
            if (overrides?.Items != null)
            {
                foreach (var o in overrides.Items)
                    if (o.SourceItemId > 0 && !overrideByItem.ContainsKey(o.SourceItemId))
                        overrideByItem[o.SourceItemId] = o;
            }

            // Resolve header overrides (fall back to source).
            DateTime newDate = DateTime.Now.Date;
            if (overrides != null && !string.IsNullOrWhiteSpace(overrides.PurchaseDate))
            {
                DateTime tmp;
                if (DateTime.TryParse(overrides.PurchaseDate, out tmp)) newDate = tmp.Date;
            }
            string newInvoice = (overrides?.InvoiceNo ?? src.InvoiceNo);
            string newRemarks = !string.IsNullOrWhiteSpace(overrides?.Remarks)
                                ? overrides.Remarks
                                : ("Cloned from purchase #" + src.Id
                                   + (string.IsNullOrWhiteSpace(src.Remarks) ? "" : " — " + src.Remarks));

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    var now = DateTime.UtcNow;

                    var clone = new Purchase
                    {
                        PurchaseDate = newDate,
                        BranchId = src.BranchId,
                        SupplierId = src.SupplierId,
                        InvoiceNo = newInvoice,
                        Remarks = newRemarks,
                        IsPosted = false,
                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now
                    };
                    db.Purchase.Add(clone);
                    db.SaveChanges();

                    int itemsCloned = 0;
                    foreach (var si in srcItems)
                    {
                        // Apply line override if present (qty / rate / remarks).
                        decimal qty  = si.Qty;
                        decimal rate = si.Rate ?? 0m;
                        string  rmk  = si.Remarks;
                        if (overrideByItem.ContainsKey(si.Id))
                        {
                            var ov = overrideByItem[si.Id];
                            if (ov.Qty.HasValue)  qty  = ov.Qty.Value;
                            if (ov.Rate.HasValue) rate = ov.Rate.Value;
                            if (!string.IsNullOrWhiteSpace(ov.Remarks)) rmk = ov.Remarks;
                        }

                        // Skip lines that the buyer zero-ed out in the preview.
                        if (qty <= 0) continue;

                        db.PurchaseItems.Add(new PurchaseItems
                        {
                            PurchaseId = clone.Id,
                            ItemType = si.ItemType,
                            ItemId = si.ItemId,
                            Qty = qty,
                            Rate = rate,
                            Remarks = rmk,
                            IsPosted = false,
                            Active = true,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now
                        });
                        itemsCloned++;
                    }

                    if (itemsCloned == 0)
                    {
                        tx.Rollback();
                        return JsonError("Nothing to clone — all source lines were zero-ed out.");
                    }

                    db.SaveChanges();
                    tx.Commit();

                    LogAudit(clone.Id, "CREATED",
                        oldValue: null,
                        newValue: PurchaseStatus.DRAFT,
                        detail: "Cloned from purchase #" + src.Id + " (" + itemsCloned + " line(s))");

                    return JsonSuccess("Purchase cloned as new draft.", new
                    {
                        id = clone.Id,
                        sourceId = src.Id,
                        invoiceNo = clone.InvoiceNo,
                        itemsCloned
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Clone failed. " + DescribePurchaseError(ex));
                }
            }
        }

        // ============================================================
        // Phase F.2 - Duplicate a purchase
        // Clones the header + active items into a new DRAFT purchase
        // (IsPosted=false). Invoice no is suffixed with "(COPY)".
        // ============================================================
        [HttpPost]
        [Route("duplicate/{id:int}")]
        public ActionResult Duplicate(int id)
        {
            var src = db.Purchase
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (src == null) return JsonError("Source purchase not found.");

            var srcItems = db.PurchaseItems
                .Where(x => x.PurchaseId == id && x.Deleted != true)
                .ToList();
            if (!srcItems.Any()) return JsonError("Source purchase has no items to copy.");

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    int userId = GetCurrentUserId();
                    var now = DateTime.UtcNow;

                    var clone = new Purchase
                    {
                        PurchaseDate = DateTime.Now.Date,
                        BranchId = src.BranchId,
                        SupplierId = src.SupplierId,
                        InvoiceNo = string.IsNullOrWhiteSpace(src.InvoiceNo)
                                    ? null
                                    : (src.InvoiceNo + " (COPY)"),
                        Remarks = src.Remarks,
                        IsPosted = false,
                        Active = true,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now
                    };

                    db.Purchase.Add(clone);
                    db.SaveChanges();

                    foreach (var si in srcItems)
                    {
                        var ci = new PurchaseItems
                        {
                            PurchaseId = clone.Id,
                            ItemType = si.ItemType,
                            ItemId = si.ItemId,
                            Qty = si.Qty,
                            Rate = si.Rate,
                            Remarks = si.Remarks,
                            IsPosted = false,
                            Active = true,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now
                        };
                        db.PurchaseItems.Add(ci);
                    }

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("Purchase duplicated as new draft.", new
                    {
                        id = clone.Id,
                        invoiceNo = clone.InvoiceNo,
                        itemCount = srcItems.Count
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Duplicate failed. " + ex.Message);
                }
            }
        }

        // ============================================================
        // Phase F.3 - Purchase analytics (last-6-months trend +
        // top suppliers + top items) inside the current filter window
        // ============================================================
        [HttpGet]
        [Route("analytics")]
        public ActionResult Analytics(
            string fromDate = null,
            string toDate = null,
            int? supplierId = null)
        {
            try
            {
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate)   && DateTime.TryParse(toDate, out var tParsed))   toDt = tParsed.Date.AddDays(1).AddTicks(-1);

                var baseQ = db.Purchase.Where(p => p.Deleted != true);
                if (fromDt.HasValue) baseQ = baseQ.Where(p => p.PurchaseDate >= fromDt.Value);
                if (toDt.HasValue)   baseQ = baseQ.Where(p => p.PurchaseDate <= toDt.Value);
                if (supplierId.HasValue && supplierId.Value > 0)
                    baseQ = baseQ.Where(p => p.SupplierId == supplierId.Value);

                var purchases = (
                    from p in baseQ
                    join s in db.Gen_Suppliers on p.SupplierId equals s.Id into sj
                    from s in sj.DefaultIfEmpty()
                    select new
                    {
                        p.Id,
                        p.PurchaseDate,
                        p.SupplierId,
                        SupplierName = s != null ? s.SupplierName : "",
                        Items = p.PurchaseItems.Where(i => i.Deleted != true).Select(i => new
                        {
                            i.ItemType,
                            i.ItemId,
                            i.Qty,
                            Rate = i.Rate ?? 0m
                        })
                    }
                ).ToList();

                // Top suppliers by amount
                var topSuppliers = purchases
                    .Where(p => p.SupplierId.HasValue)
                    .GroupBy(p => new { p.SupplierId, p.SupplierName })
                    .Select(g => new
                    {
                        supplierId = g.Key.SupplierId,
                        supplierName = string.IsNullOrWhiteSpace(g.Key.SupplierName) ? "-" : g.Key.SupplierName,
                        purchaseCount = g.Count(),
                        totalAmount = Math.Round(g.SelectMany(x => x.Items).Sum(i => i.Qty * i.Rate), 2)
                    })
                    .OrderByDescending(x => x.totalAmount)
                    .Take(5)
                    .ToList();

                // Top items by qty
                var allItems = purchases.SelectMany(p => p.Items).ToList();
                var rawIds = allItems.Where(i => (i.ItemType ?? "RAW").Trim().ToUpper() == "RAW")
                    .Select(i => i.ItemId).Distinct().ToList();
                var threadIds = allItems.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "THREAD")
                    .Select(i => i.ItemId).Distinct().ToList();

                var rawMap = db.Gen_RawItems
                    .Where(r => rawIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);

                var threadInfo = db.Gen_Threads
                    .Where(x => threadIds.Contains(x.Id))
                    .Select(x => new { x.Id, x.RawItemId, x.Code, x.Name })
                    .ToList();
                var threadLinkedRawIds = threadInfo
                    .Where(t => t.RawItemId.HasValue).Select(t => t.RawItemId.Value).Distinct().ToList();
                var threadLinkedRawNames = db.Gen_RawItems
                    .Where(r => threadLinkedRawIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);
                var threadMap = threadInfo.ToDictionary(
                    t => t.Id,
                    t => t.RawItemId.HasValue && threadLinkedRawNames.ContainsKey(t.RawItemId.Value)
                         ? threadLinkedRawNames[t.RawItemId.Value]
                         : ((t.Code ?? "") + " " + (t.Name ?? "")).Trim());

                // Phase F.3 polish - consolidate same-physical-item rows that
                // were purchased as both RAW and THREAD. We resolve each line
                // to its canonical "key" (RawItemId) when possible.
                var threadIdToRawId = threadInfo
                    .Where(t => t.RawItemId.HasValue)
                    .ToDictionary(t => t.Id, t => t.RawItemId.Value);

                var canonicalized = allItems.Select(i =>
                {
                    var itType = (i.ItemType ?? "RAW").Trim().ToUpper();
                    int canonicalId = itType == "THREAD" && threadIdToRawId.ContainsKey(i.ItemId)
                                      ? threadIdToRawId[i.ItemId]
                                      : i.ItemId;
                    return new
                    {
                        ItemType = itType,
                        CanonicalRawId = canonicalId,
                        SourceItemId = i.ItemId,
                        i.Qty,
                        i.Rate
                    };
                }).ToList();

                var topItems = canonicalized
                    .GroupBy(i => i.CanonicalRawId)
                    .Select(g =>
                    {
                        // Prefer the raw-items name (canonical). Fall back to thread label.
                        string name = "-";
                        if (rawMap.ContainsKey(g.Key)) name = rawMap[g.Key];
                        else
                        {
                            var firstThread = g.FirstOrDefault(x => x.ItemType == "THREAD");
                            if (firstThread != null && threadMap.ContainsKey(firstThread.SourceItemId))
                                name = threadMap[firstThread.SourceItemId];
                        }

                        var types = g.Select(x => x.ItemType).Distinct().OrderBy(s => s).ToList();
                        string typeLabel = string.Join(" + ", types);

                        return new
                        {
                            itemName = name,
                            itemType = typeLabel,
                            totalQty = Math.Round(g.Sum(x => x.Qty), 2),
                            totalAmount = Math.Round(g.Sum(x => x.Qty * x.Rate), 2)
                        };
                    })
                    .OrderByDescending(x => x.totalAmount)
                    .Take(5)
                    .ToList();

                // Last 6 months trend (relative to today, regardless of filter — gives stable spark)
                var trendEnd = DateTime.Now.Date;
                var trendStart = new DateTime(trendEnd.Year, trendEnd.Month, 1).AddMonths(-5);

                var trendQ = db.Purchase
                    .Where(p => p.Deleted != true
                             && p.PurchaseDate >= trendStart
                             && p.PurchaseDate <= trendEnd);
                if (supplierId.HasValue && supplierId.Value > 0)
                    trendQ = trendQ.Where(p => p.SupplierId == supplierId.Value);

                var trendRaw = trendQ
                    .Select(p => new
                    {
                        p.PurchaseDate,
                        Total = p.PurchaseItems
                            .Where(i => i.Deleted != true)
                            .Sum(i => (decimal?)(i.Qty * (i.Rate ?? 0m))) ?? 0m
                    })
                    .ToList();

                var monthlyTrend = Enumerable.Range(0, 6)
                    .Select(i => trendStart.AddMonths(i))
                    .Select(monthStart =>
                    {
                        var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
                        var rows = trendRaw.Where(x => x.PurchaseDate >= monthStart && x.PurchaseDate <= monthEnd).ToList();
                        return new
                        {
                            month = monthStart.ToString("MMM yyyy"),
                            count = rows.Count,
                            amount = Math.Round(rows.Sum(x => x.Total), 2)
                        };
                    })
                    .ToList();

                return JsonSuccess("OK", new
                {
                    topSuppliers,
                    topItems,
                    monthlyTrend
                });
            }
            catch (Exception ex)
            {
                return JsonError("Analytics failed. " + ex.Message);
            }
        }

        private decimal GetCurrentStoreBalance(int itemId, int? branchId, int? warehouseId)
        {
            var q = db.Gen_Store.Where(x =>
                x.ItemId == itemId &&
                x.Deleted == false);

            if (branchId.HasValue)
                q = q.Where(x => x.BranchId == branchId.Value);

            if (warehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == warehouseId.Value);

            decimal totalIn = q.Sum(x => (decimal?)x.InQty) ?? 0m;
            decimal totalOut = q.Sum(x => (decimal?)x.OutQty) ?? 0m;
            decimal opening = q
                .OrderBy(x => x.Id)
                .Select(x => (decimal?)x.OpeningQty)
                .FirstOrDefault() ?? 0m;

            return opening + totalIn - totalOut;
        }

        private string GetRawItemUnit(int itemId)
        {
            var unitCode = db.Gen_RawItems
                .Where(r => r.Id == itemId)
                .Select(r => r.Gen_Units.UnitCode)
                .FirstOrDefault();

            return string.IsNullOrWhiteSpace(unitCode) ? "PCS" : unitCode;
        }

        // FABRIC: balance computed from Gen_Stocks rows tagged ItemType="FABRIC"
        private decimal GetCurrentFabricBalance(int fabricId, int? branchId)
        {
            var q = db.Gen_Stocks.Where(x =>
                x.ItemType == "FABRIC" &&
                x.ItemId == fabricId &&
                x.Deleted != true);

            if (branchId.HasValue)
                q = q.Where(x => x.BranchId == branchId.Value);

            decimal totalIn = q.Sum(x => (decimal?)x.InQty) ?? 0m;
            decimal totalOut = q.Sum(x => (decimal?)x.OutQty) ?? 0m;
            decimal opening = q
                .OrderBy(x => x.Id)
                .Select(x => (decimal?)x.OpeningQty)
                .FirstOrDefault() ?? 0m;

            return opening + totalIn - totalOut;
        }

        private int GetNextStoreId()
        {
            return (db.Gen_Store.Select(x => (int?)x.Id).Max() ?? 0) + 1;
        }

        // ============================================================
        // EXPORT EXCEL — Purchase list with the same filters as the List endpoint.
        // Two sheets: Summary (one row per purchase header) + Items (line-level detail).
        // ============================================================
        [HttpGet]
        [Route("export/excel")]
        public ActionResult ExportExcel(
            string fromDate = null,
            string toDate = null,
            int? supplierId = null,
            string status = null,
            string itemSearch = null,
            string invoiceSearch = null)
        {
            try
            {
            // Parse dates
            DateTime? fromDt = null, toDt = null;
            if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
            if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);

            string statusUp = (status ?? "").Trim().ToUpper();

            var baseQ = db.Purchase.Where(p => p.Deleted != true);
            if (fromDt.HasValue) baseQ = baseQ.Where(p => p.PurchaseDate >= fromDt.Value);
            if (toDt.HasValue) baseQ = baseQ.Where(p => p.PurchaseDate <= toDt.Value);
            if (supplierId.HasValue && supplierId.Value > 0) baseQ = baseQ.Where(p => p.SupplierId == supplierId.Value);
            if (statusUp == "DRAFT") baseQ = baseQ.Where(p => p.IsPosted != true);
            else if (statusUp == "POSTED") baseQ = baseQ.Where(p => p.IsPosted == true);
            if (!string.IsNullOrWhiteSpace(invoiceSearch))
            {
                var s = invoiceSearch.Trim();
                baseQ = baseQ.Where(p => p.InvoiceNo != null && p.InvoiceNo.Contains(s));
            }

            var purchases = baseQ
                .OrderByDescending(p => p.Id)
                .ToList();

            var purchaseIds = purchases.Select(p => p.Id).ToList();

            // Pull all line items for these purchases
            var allItems = db.PurchaseItems
                .Where(i => purchaseIds.Contains(i.PurchaseId) && i.Deleted != true)
                .ToList();

            // Build lookup maps
            var supplierMap = db.Gen_Suppliers
                .ToDictionary(x => x.Id, x => x.SupplierName);
            var branchMap = db.Branches
                .ToDictionary(x => x.Id, x => x.Name);
            var rawIds = allItems.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "RAW").Select(i => i.ItemId).Distinct().ToList();
            var rawMap = db.Gen_RawItems.Where(r => rawIds.Contains(r.Id)).ToDictionary(r => r.Id, r => r.ItemName);
            var threadIds = allItems.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "THREAD").Select(i => i.ItemId).Distinct().ToList();
            var threadInfo = db.Gen_Threads
                .Where(t => threadIds.Contains(t.Id))
                .Select(t => new { t.Id, t.RawItemId, t.Code, t.Name, t.Color })
                .ToList();
            // Materialize linkedRawIds before the EF Contains() to avoid translation issues
            var linkedRawIds = threadInfo
                .Where(t => t.RawItemId.HasValue)
                .Select(t => t.RawItemId.Value)
                .Distinct()
                .ToList();
            var threadLinkedNames = db.Gen_RawItems
                .Where(r => linkedRawIds.Contains(r.Id))
                .ToDictionary(r => r.Id, r => r.ItemName);
            var threadDisplayMap = threadInfo.ToDictionary(
                t => t.Id,
                t => t.RawItemId.HasValue && threadLinkedNames.ContainsKey(t.RawItemId.Value)
                     ? threadLinkedNames[t.RawItemId.Value]
                     : "[unlinked] " + (t.Code ?? "") + " " + (t.Name ?? "")
            );

            var excelFabricIds = allItems.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "FABRIC").Select(i => i.ItemId).Distinct().ToList();
            var excelFabricMap = db.Gen_RawItems
                .Where(f => excelFabricIds.Contains(f.Id))
                .ToDictionary(f => f.Id, f => f.ItemName);

            string ItemLabel(string typeStr, int itemId)
            {
                var t = (typeStr ?? "").Trim().ToUpper();
                if (t == "RAW" && rawMap.ContainsKey(itemId)) return rawMap[itemId];
                if (t == "THREAD" && threadDisplayMap.ContainsKey(itemId)) return threadDisplayMap[itemId];
                if (t == "FABRIC" && excelFabricMap.ContainsKey(itemId)) return excelFabricMap[itemId];
                return "-";
            }

            // Post-filter by item name search (since item names come from joined tables)
            if (!string.IsNullOrWhiteSpace(itemSearch))
            {
                var q = itemSearch.Trim();
                var matchingPurchaseIds = new HashSet<int>(allItems
                    .Where(i => ItemLabel(i.ItemType, i.ItemId).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(i => i.PurchaseId)
                    .Distinct());
                purchases = purchases.Where(p => matchingPurchaseIds.Contains(p.Id)).ToList();
                purchaseIds = purchases.Select(p => p.Id).ToList();
                allItems = allItems.Where(i => purchaseIds.Contains(i.PurchaseId)).ToList();
            }

            using (var package = new ExcelPackage())
            {
                // ===== Sheet 1: Summary =====
                var ws = package.Workbook.Worksheets.Add("Purchases");
                var headers = new[] { "Id", "Date", "Invoice", "Supplier", "Branch", "Status", "Items", "Total Qty", "Total Amount" };
                for (int i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];

                int row = 2;
                foreach (var p in purchases)
                {
                    var items = allItems.Where(i => i.PurchaseId == p.Id).ToList();
                    decimal totalQty = items.Sum(i => i.Qty);
                    decimal totalAmount = items.Sum(i => i.Qty * (i.Rate ?? 0m));

                    string supplierName = "";
                    if (p.SupplierId.HasValue && supplierMap.ContainsKey(p.SupplierId.Value))
                        supplierName = supplierMap[p.SupplierId.Value] ?? "";
                    string branchName = "";
                    if (branchMap.ContainsKey(p.BranchId))
                        branchName = branchMap[p.BranchId] ?? "";

                    ws.Cells[row, 1].Value = p.Id;
                    ws.Cells[row, 2].Value = p.PurchaseDate.ToString("yyyy-MM-dd");
                    ws.Cells[row, 3].Value = p.InvoiceNo ?? "";
                    ws.Cells[row, 4].Value = supplierName;
                    ws.Cells[row, 5].Value = branchName;
                    ws.Cells[row, 6].Value = p.IsPosted ? "Posted" : "Draft";
                    ws.Cells[row, 7].Value = items.Count;
                    ws.Cells[row, 8].Value = totalQty;
                    ws.Cells[row, 9].Value = totalAmount;
                    row++;
                }
                using (var headerRange = ws.Cells[1, 1, 1, headers.Length])
                {
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                ws.Cells[ws.Dimension?.Address ?? "A1"].AutoFitColumns();

                // ===== Sheet 2: Items =====
                var ws2 = package.Workbook.Worksheets.Add("Items");
                var itemHeaders = new[] { "PurchaseId", "Invoice", "Date", "Type", "Item", "Qty", "Rate", "Amount", "Remarks" };
                for (int i = 0; i < itemHeaders.Length; i++) ws2.Cells[1, i + 1].Value = itemHeaders[i];

                int row2 = 2;
                foreach (var p in purchases)
                {
                    var items = allItems.Where(i => i.PurchaseId == p.Id).ToList();
                    foreach (var i in items)
                    {
                        decimal rate = i.Rate ?? 0m;
                        ws2.Cells[row2, 1].Value = p.Id;
                        ws2.Cells[row2, 2].Value = p.InvoiceNo ?? "";
                        ws2.Cells[row2, 3].Value = p.PurchaseDate.ToString("yyyy-MM-dd");
                        ws2.Cells[row2, 4].Value = (i.ItemType ?? "RAW").Trim().ToUpper();
                        ws2.Cells[row2, 5].Value = ItemLabel(i.ItemType, i.ItemId);
                        ws2.Cells[row2, 6].Value = i.Qty;
                        ws2.Cells[row2, 7].Value = rate;
                        ws2.Cells[row2, 8].Value = i.Qty * rate;
                        ws2.Cells[row2, 9].Value = i.Remarks ?? "";
                        row2++;
                    }
                }
                using (var headerRange2 = ws2.Cells[1, 1, 1, itemHeaders.Length])
                {
                    headerRange2.Style.Font.Bold = true;
                    headerRange2.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    headerRange2.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                ws2.Cells[ws2.Dimension?.Address ?? "A1"].AutoFitColumns();

                string fileName = "Purchases_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx";
                return File(
                    package.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
            }
            catch (Exception ex)
            {
                var deepest = ex;
                while (deepest.InnerException != null) deepest = deepest.InnerException;
                return JsonError("Excel export failed: " + deepest.Message);
            }
        }


        [HttpGet]
        [Route("export/pdf")]
        public ActionResult ExportPdf()
        {
            var data = db.Purchase
                .Where(x => x.Deleted != true)
                .OrderByDescending(x => x.Id)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                doc.Add(new Paragraph("Purchase Report"));
                doc.Add(new Paragraph(" "));

                foreach (var p in data)
                {
                    doc.Add(new Paragraph(
                        $"{p.PurchaseDate:yyyy-MM-dd} | Invoice: {p.InvoiceNo} | Posted: {p.IsPosted}"
                    ));
                }

                doc.Close();

                return File(ms.ToArray(), "application/pdf", "Purchases.pdf");
            }
        }

        [HttpGet]
        [Route("print")]
        public ActionResult Print()
        {
            var list = db.Purchase
                .Where(x => x.Deleted != true)
                .OrderByDescending(x => x.Id)
                .Select(x => new PurchaseRowViewModel
                {
                    Id = x.Id,
                    PurchaseDateText = x.PurchaseDate.ToString("dd-MMMM-yyyy"),
                    InvoiceNo = x.InvoiceNo,
                    BranchName = db.Branches
                        .Where(b => b.Id == x.BranchId)
                        .Select(b => b.Name)
                        .FirstOrDefault(),
                    IsPosted = x.IsPosted,
                    Active = x.Active ?? true
                })
                .ToList();

            return View("Print", list);
        }

        [HttpGet]
        [Route("dropdowns/raw-items")]
        public ActionResult RawItemsDropdown()
        {
            // Exclude FABRIC and THREAD types — they have their own dropdowns.
            // FABRIC: filtered Gen_RawItems Type='FABRIC' (dropdowns/fabrics)
            // THREAD: Gen_Threads → linked Gen_RawItems via Phase E bridge
            // Filter by Type column AND by name prefix as a safety net for legacy rows
            // where the Type column wasn't populated.
            var rows = db.Gen_RawItems
                .Where(x => x.Active == true && x.Deleted != true
                         && (x.Type == null || (x.Type.ToUpper() != "FABRIC" && x.Type.ToUpper() != "THREAD"))
                         && !x.ItemName.StartsWith("FABRIC -")
                         && !x.ItemName.StartsWith("FABRIC-")
                         && !x.ItemName.StartsWith("THREAD -")
                         && !x.ItemName.StartsWith("THREAD-"))
                .OrderBy(x => x.ItemName)
                .Select(x => new
                {
                    id = x.Id,
                    text = x.ItemName
                })
                .ToList();

            return JsonSuccess("OK", rows);
        }

        [HttpGet]
        [Route("dropdowns/threads")]
        public ActionResult ThreadsDropdown()
        {
            var rows = db.Gen_Threads
                .Where(x => (x.Active == true || x.Active == null) && x.Deleted != true)
                .OrderBy(x => x.Code).ThenBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name,
                    color = x.Color,
                    unit = x.UnitOfMeasure,
                    unitCost = x.UnitCost,
                    text = (x.Code ?? "") + " " + (x.Name ?? "") + " (" + (x.Color ?? "") + ")"
                })
                .ToList();

            return JsonSuccess("OK", rows);
        }

        // Fabric dropdown sources from Gen_RawItems Type='FABRIC' so that what
        // you can buy matches what the BOM (Gen_ComponentMaterials) references.
        // Stock then flows through the unified Gen_Store ledger keyed by
        // Gen_RawItems.Id and Issue Material picks it up automatically.
        [HttpGet]
        [Route("dropdowns/fabrics")]
        public ActionResult FabricsDropdown()
        {
            var rows = db.Gen_RawItems
                .Where(x => x.Deleted != true
                         && x.Active == true
                         && x.Type != null
                         && x.Type.ToUpper() == "FABRIC")
                .OrderBy(x => x.ItemName)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Id.ToString(),
                    name = x.ItemName,
                    text = x.ItemName
                })
                .ToList();

            return JsonSuccess("OK", rows);
        }

        // ============================================================
        // PREFILL FROM PURCHASE ORDER
        // ------------------------------------------------------------
        // Receiving goods against a PO: the buyer opens the GRN form
        // already populated with supplier, branch, items (outstanding
        // qty + last-known rate). They only key the invoice number and
        // tweak received qty if partial. Saving stamps PurchaseOrderId
        // on the header and PoItemId on each line so the PO controller's
        // /sync-receipts can roll ReceivedQty up.
        //
        // Outstanding = PurchaseOrderItems.OrderedQty − ReceivedQty.
        // Rows with outstanding <= 0 are excluded — already fully received.
        // ============================================================
        [HttpGet, Route("prefill-from-po/{poId:int}")]
        public ActionResult PrefillFromPo(int poId)
        {
            try
            {
                var header = (from po in db.Database.SqlQuery<PoPrefillHeader>(@"
SELECT po.Id, po.PoCode, po.BranchId, b.Name AS BranchName,
       po.SupplierId, s.SupplierName, po.Status
FROM   dbo.PurchaseOrder po
LEFT JOIN dbo.Branches      b ON b.Id = po.BranchId
LEFT JOIN dbo.Gen_Suppliers s ON s.Id = po.SupplierId
WHERE  po.Id = @id AND (po.Deleted = 0 OR po.Deleted IS NULL)",
                                  new System.Data.SqlClient.SqlParameter("@id", poId))
                              select po).FirstOrDefault();

                if (header == null) return JsonError("PO not found.");
                if (string.Equals(header.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
                    return JsonError("PO is still DRAFT. Send it to the supplier first.");
                if (string.Equals(header.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("PO has been cancelled.");
                if (string.Equals(header.Status, "FULLY_RECEIVED", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                    return JsonError("PO is already fully received.");

                var lines = db.Database.SqlQuery<PoPrefillLine>(@"
SELECT  poi.Id          AS PoItemId,
        poi.RawItemId,
        r.ItemName      AS RawItemName,
        r.Type          AS RawType,
        poi.UnitId,
        u.UnitCode,
        poi.OrderedQty,
        ISNULL(poi.ReceivedQty, 0) AS ReceivedQty,
        poi.Rate,
        poi.Remarks
FROM    dbo.PurchaseOrderItems poi
LEFT JOIN dbo.Gen_RawItems r ON r.Id = poi.RawItemId
LEFT JOIN dbo.Gen_Units    u ON u.Id = poi.UnitId
WHERE   poi.PoId = @id
   AND  (poi.Deleted = 0 OR poi.Deleted IS NULL)
ORDER BY poi.Id",
                    new System.Data.SqlClient.SqlParameter("@id", poId)).ToList();

                // Resolve thread-backed raw items to their Gen_Threads.Id, since
                // the Purchase form's THREAD dropdown is keyed on Gen_Threads.Id
                // (not the underlying RawItemId). Without this mapping the Item
                // dropdown would render empty for thread receipts.
                var threadRawIds = lines
                    .Where(l => (l.RawType ?? "").ToUpper() == "THREAD")
                    .Select(l => l.RawItemId)
                    .Distinct()
                    .ToList();

                // Materialize the candidate threads first, then build the
                // map in memory. EF6 chokes on `g.OrderBy().First().Id`
                // inside a GroupBy projection — translating that to T-SQL
                // throws at query time.
                var rawToThread = new Dictionary<int, int>();
                if (threadRawIds.Count > 0)
                {
                    // Match the threads-dropdown endpoint's filter (Active
                    // OR Active IS NULL, Deleted != true) so the id we
                    // return is guaranteed to exist in what the frontend
                    // sees in `this.threads`. Otherwise the dropdown
                    // <select> would render the placeholder forever.
                    var threadRows = db.Gen_Threads
                        .Where(t => t.Deleted != true
                                 && (t.Active == true || t.Active == null)
                                 && t.RawItemId.HasValue
                                 && threadRawIds.Contains(t.RawItemId.Value))
                        .Select(t => new { t.Id, RawId = t.RawItemId.Value })
                        .OrderBy(x => x.Id)
                        .ToList();

                    foreach (var t in threadRows)
                    {
                        if (!rawToThread.ContainsKey(t.RawId))
                            rawToThread[t.RawId] = t.Id;
                    }
                }

                var items = lines
                    .Select(l =>
                    {
                        decimal outstanding = l.OrderedQty - l.ReceivedQty;

                        // Map the underlying raw item to the form's three-way type.
                        // For threads we also swap the id from RawItemId to
                        // Gen_Threads.Id so the dropdown can find a match.
                        string rawType = (l.RawType ?? "").ToUpper();
                        string itemType;
                        int    itemId;
                        if (rawType == "THREAD" && rawToThread.ContainsKey(l.RawItemId))
                        {
                            itemType = "THREAD";
                            itemId   = rawToThread[l.RawItemId];
                        }
                        else if (rawType == "FABRIC")
                        {
                            itemType = "FABRIC";
                            itemId   = l.RawItemId;
                        }
                        else
                        {
                            itemType = "RAW";
                            itemId   = l.RawItemId;
                        }

                        return new
                        {
                            poItemId           = l.PoItemId,
                            // itemId is what the form's dropdown matches against —
                            // Gen_Threads.Id for THREAD, Gen_RawItems.Id otherwise.
                            itemId,
                            // rawItemId is what the GRN save payload still needs to
                            // stamp on PurchaseItems.ItemId when itemType != THREAD,
                            // and what we use to look up store balance per raw item.
                            rawItemId          = l.RawItemId,
                            rawItemName        = l.RawItemName,
                            itemType,
                            unitId             = l.UnitId,
                            unitCode           = l.UnitCode,
                            orderedQty         = l.OrderedQty,
                            alreadyReceivedQty = l.ReceivedQty,
                            outstandingQty     = outstanding,
                            // Default the receive-now field to the outstanding qty.
                            // Buyer can lower it for partial receipt.
                            suggestedQty       = outstanding,
                            rate               = l.Rate,
                            remarks            = l.Remarks ?? ""
                        };
                    })
                    .Where(x => x.outstandingQty > 0)
                    .ToList();

                if (items.Count == 0)
                    return JsonError("Every line on this PO has already been received.");

                return JsonSuccess("OK", new
                {
                    poId               = header.Id,
                    poCode             = header.PoCode,
                    supplierId         = header.SupplierId,
                    supplierName       = header.SupplierName,
                    branchId           = header.BranchId,
                    branchName         = header.BranchName,
                    purchaseDate       = DateTime.Now.ToString("yyyy-MM-dd"),
                    invoiceNo          = "",
                    remarks            = "Receipt against " + header.PoCode,
                    items
                });
            }
            catch (Exception ex) { return JsonError("Failed to load PO prefill.", ex); }
        }

        private class PoPrefillHeader
        {
            public int    Id { get; set; }
            public string PoCode { get; set; }
            public int?   BranchId { get; set; }
            public string BranchName { get; set; }
            public int?   SupplierId { get; set; }
            public string SupplierName { get; set; }
            public string Status { get; set; }
        }
        private class PoPrefillLine
        {
            public int     PoItemId { get; set; }
            public int     RawItemId { get; set; }
            public string  RawItemName { get; set; }
            public string  RawType { get; set; }
            public int?    UnitId { get; set; }
            public string  UnitCode { get; set; }
            public decimal OrderedQty { get; set; }
            public decimal ReceivedQty { get; set; }
            public decimal Rate { get; set; }
            public string  Remarks { get; set; }
        }

        private string GetEmployeeCodeByUserId(int? userId)
        {
            if (!userId.HasValue || userId.Value <= 0)
                return "-";

            var user = db.Adm_Users.FirstOrDefault(x => x.Id == userId.Value);

            if (user == null)
                return "-";

            // Replace EmployeeId with your actual column if different
            var empId = user.EmployeeId;

            if (!empId.HasValue || empId.Value <= 0)
                return "-";

            return "EMP-" + empId.Value;
        }

    }    
}
