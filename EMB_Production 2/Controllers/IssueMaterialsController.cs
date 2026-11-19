using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/issue-materials")]
    public class IssueMaterialsController : BaseController
    {
        // Phase E: all thread inventory now flows through Gen_Store via the
        // linked Gen_Threads.RawItemId. Threads share the same ledger as raw items.

        
        [HttpGet, Route("suggest/{workOrderId:int}")]
        public ActionResult Suggest(int workOrderId)
        {
            try
            {
                var wo = db.WorkOrders.FirstOrDefault(w => w.Id == workOrderId && w.Deleted != true);
                if (wo == null) return JsonError("Work order not found.");

                if (!wo.DesignId.HasValue || wo.DesignId.Value <= 0)
                    return JsonError("Work order has no design.");

                int designId = wo.DesignId.Value;
                int branchId = wo.BranchId ?? GetCurrentBranchId();
                decimal qtyMultiplier = (decimal)(wo.OrderQty ?? 0);

                var compIds = db.Gen_Components
                    .Where(c => c.D_Code == designId && c.Deleted != true && (c.Active == true || c.Active == null))
                    .Select(c => c.Id)
                    .ToList();

                if (compIds.Count == 0)
                    return JsonSuccess("OK", new
                    {
                        workOrderId,
                        workOrderCode = wo.WorkOrderCode,
                        designId,
                        orderQty = qtyMultiplier,
                        suggestions = new List<object>()
                    });

                // Source of truth: Gen_ComponentMaterials. Covers fabrics,
                // accessories, AND threads (a thread is just a Gen_RawItems
                // row with Type='THREAD' that ComponentMaterials lists for
                // a component). Gen_Threads is LEFT-joined for display info
                // (code/color/unit cost) when the raw item is thread-backed;
                // Gen_RawItems supplies the name otherwise.
                //
                // Gen_ThreadUsage used to be a parallel thread-only path
                // but its Usage values diverged from production reality —
                // see the BOM Audit report for the rationale.
                var usageRows = (
                    from cm in db.Gen_ComponentMaterials
                    join r in db.Gen_RawItems on cm.RawItemId equals r.Id
                    join t in db.Gen_Threads on cm.RawItemId equals t.RawItemId into tj
                    from t in tj.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into uj
                    from u in uj.DefaultIfEmpty()
                    where compIds.Contains(cm.CompId)
                          && cm.Deleted != true
                          && (cm.Active == true)
                          && r.Deleted != true
                          && (t == null || t.Deleted != true)
                    select new
                    {
                        RawItemId = cm.RawItemId,
                        Code      = t != null ? t.Code  : null,
                        Name      = t != null ? t.Name  : r.ItemName,
                        Color     = t != null ? t.Color : null,
                        Unit      = t != null ? t.UnitOfMeasure : (u != null ? u.UnitCode : null),
                        UnitCost  = t != null ? t.UnitCost : (decimal?)null,
                        Usage     = cm.QtyPerUnit
                    }
                ).ToList();

                var grouped = usageRows
                    .GroupBy(r => new { r.RawItemId, r.Code, r.Name, r.Color, r.Unit, r.UnitCost })
                    .Select(g =>
                    {
                        decimal perPiece = g.Sum(x => x.Usage);
                        decimal currentBalance = GetCurrentStoreBalance(g.Key.RawItemId, branchId, null);

                        return new
                        {
                            // `threadId` field name kept for Angular DTO
                            // compatibility; semantically it's now the
                            // RawItemId (single source for all item types).
                            threadId = g.Key.RawItemId,
                            rawItemId = (int?)g.Key.RawItemId,
                            isLinked = true,
                            code = g.Key.Code,
                            name = g.Key.Name,
                            color = g.Key.Color,
                            unit = g.Key.Unit,
                            unitCost = g.Key.UnitCost ?? 0m,
                            perPiece = Math.Round(perPiece, 4),
                            suggestedQty = Math.Round(perPiece * qtyMultiplier, 4),
                            currentBalance
                        };
                    })
                    .OrderBy(x => x.name)
                    .ToList();

                var data = new
                {
                    workOrderId,
                    workOrderCode = wo.WorkOrderCode,
                    designId,
                    orderQty = qtyMultiplier,
                    branchId,
                    suggestions = grouped
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to compute suggestions.", ex);
            }
        }

        // ================================================================
        // SAVE — write one Gen_Store OUT row per thread (via linked RawItemId)
        // all sharing the same IssueCode. Blocks if any item overdraws stock
        // or references an unlinked thread.
        // ================================================================
        [HttpPost, Route("save")]
        public ActionResult Save(IssueMaterialDto dto)
        {
            try
            {
                if (dto == null || dto.Items == null || !dto.Items.Any())
                    return JsonError("Issue must contain at least one thread.");

                if (dto.WorkOrderId <= 0)
                    return JsonError("Work order is required.");

                var wo = db.WorkOrders.FirstOrDefault(w => w.Id == dto.WorkOrderId && w.Deleted != true);
                if (wo == null) return JsonError("Work order not found.");

                int branchId = wo.BranchId ?? GetCurrentBranchId();
                int userId = GetCurrentUserId();
                DateTime now = DateTime.UtcNow;

                // dto.Items[].ThreadId is, per Suggest()'s "single source of
                // truth" refactor, actually a Gen_RawItems.Id (field name kept
                // for Angular DTO compatibility). Gen_Threads is only an
                // optional LEFT-join for display info when the raw item is
                // thread-backed — it must never be the primary lookup key,
                // since fabrics/accessories have no Gen_Threads row at all.
                var rawItemIds = dto.Items.Select(x => x.ThreadId).Distinct().ToList();
                var rawItemMap = db.Gen_RawItems
                    .Where(r => rawItemIds.Contains(r.Id) && r.Deleted != true)
                    .ToDictionary(r => r.Id);

                var threadByRaw = db.Gen_Threads
                    .Where(t => t.RawItemId.HasValue && rawItemIds.Contains(t.RawItemId.Value) && t.Deleted != true)
                    .ToList()
                    .GroupBy(t => t.RawItemId.Value)
                    .ToDictionary(g => g.Key, g => g.First());

                var unitIds = rawItemMap.Values.Select(r => r.UnitId).Distinct().ToList();
                var unitCodeMap = db.Gen_Units
                    .Where(u => unitIds.Contains(u.Id))
                    .ToDictionary(u => u.Id, u => u.UnitCode);

                // Pre-validate every item before any write
                foreach (var i in dto.Items)
                {
                    if (i.ThreadId <= 0) return JsonError("Invalid item.");
                    if (i.Qty <= 0) return JsonError("Quantity must be greater than zero.");
                    if (!rawItemMap.ContainsKey(i.ThreadId))
                        return JsonError($"Item {i.ThreadId} does not exist or is deleted.");

                    var raw = rawItemMap[i.ThreadId];
                    threadByRaw.TryGetValue(i.ThreadId, out var threadInfo);
                    string displayName = threadInfo != null ? $"{threadInfo.Code} {threadInfo.Name}" : raw.ItemName;

                    decimal balance = GetCurrentStoreBalance(i.ThreadId, branchId, null);
                    if (i.Qty > balance)
                        return JsonError($"Insufficient stock for {displayName}. Available: {balance}, Requested: {i.Qty}.");
                }

                string issueCode = NextIssueCode(now.Year);

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        foreach (var i in dto.Items)
                        {
                            int rawItemId = i.ThreadId;
                            var raw = rawItemMap[rawItemId];
                            threadByRaw.TryGetValue(rawItemId, out var thread);
                            string displayName = thread != null ? $"{thread.Code} {thread.Name}" : raw.ItemName;

                            // Sprint-4 TOCTOU guard: the pre-validation loop above
                            // read the balance before this transaction/lock existed,
                            // so a concurrent Save() for the same item could still
                            // slip in between. Serialise on the (item, branch)
                            // ledger slice, then re-check the balance against the
                            // true current state before writing the OUT row.
                            LockBalanceSlice(rawItemId, branchId);

                            decimal opening = GetCurrentStoreBalance(rawItemId, branchId, null);
                            if (i.Qty > opening)
                            {
                                tx.Rollback();
                                return JsonError($"Insufficient stock for {displayName}. Available: {opening}, Requested: {i.Qty}.");
                            }

                            decimal rate = i.Rate ?? thread?.UnitCost ?? GetLastRate(rawItemId, branchId);
                            string unit = thread?.UnitOfMeasure
                                ?? (unitCodeMap.ContainsKey(raw.UnitId) ? unitCodeMap[raw.UnitId] : null)
                                ?? "MTR";

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = rawItemId,
                                BranchId = branchId,
                                WarehouseId = null,
                                TransactionDate = now,

                                TransactionType = "OUT",
                                ReferenceType = "MATERIAL_ISSUE",
                                ReferenceId = wo.Id,
                                ReferenceNo = issueCode,

                                OpeningQty = opening,
                                InQty = 0m,
                                OutQty = i.Qty,
                                CurrentQty = opening - i.Qty,

                                Unit = unit,
                                Rate = rate,
                                Amount = i.Qty * rate,

                                Remarks = string.IsNullOrWhiteSpace(i.Remarks) ? dto.Remarks : i.Remarks,

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

                        return JsonSuccess("Materials issued.", new
                        {
                            issueCode,
                            workOrderId = wo.Id,
                            workOrderCode = wo.WorkOrderCode,
                            itemCount = dto.Items.Count
                        });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return JsonError("Issue save failed.", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to save material issue.", ex);
            }
        }

        // ================================================================
        // LIST — one row per issue (grouped by IssueCode/ReferenceNo)
        // ================================================================
        [HttpGet, Route("list")]
        public ActionResult List(int? workOrderId = null, int top = 200)
        {
            try
            {
                int branchId = GetCurrentBranchId();

                var q = db.Gen_Store
                    .Where(s => s.ReferenceType == "MATERIAL_ISSUE"
                                && s.Deleted != true
                                && s.BranchId == branchId
                                && !string.IsNullOrEmpty(s.ReferenceNo));

                if (workOrderId.HasValue)
                    q = q.Where(s => s.ReferenceId == workOrderId.Value);

                var rows = q
                    .GroupBy(s => new { s.ReferenceNo, s.ReferenceId })
                    .Select(g => new
                    {
                        IssueCode = g.Key.ReferenceNo,
                        WorkOrderId = g.Key.ReferenceId,
                        ItemCount = g.Count(),
                        TotalQty = g.Sum(x => (decimal?)x.OutQty) ?? 0m,
                        TotalAmount = g.Sum(x => (decimal?)x.Amount) ?? 0m,
                        IssueDate = g.Min(x => x.CreatedDate)
                    })
                    .OrderByDescending(g => g.IssueDate)
                    .Take(top)
                    .ToList();

                var woIds = rows.Where(r => r.WorkOrderId.HasValue)
                                .Select(r => r.WorkOrderId.Value)
                                .Distinct()
                                .ToList();

                var woMap = db.WorkOrders
                    .Where(w => woIds.Contains(w.Id))
                    .ToDictionary(w => w.Id, w => w.WorkOrderCode);

                var list = rows.Select(r => new
                {
                    issueCode = r.IssueCode,
                    workOrderId = r.WorkOrderId,
                    workOrderCode = r.WorkOrderId.HasValue && woMap.ContainsKey(r.WorkOrderId.Value)
                                    ? woMap[r.WorkOrderId.Value]
                                    : "-",
                    itemCount = r.ItemCount,
                    totalQty = r.TotalQty,
                    totalAmount = r.TotalAmount,
                    issueDate = r.IssueDate,
                    issueDateText = r.IssueDate.HasValue ? r.IssueDate.Value.ToString("yyyy-MM-dd HH:mm") : ""
                }).ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load issues.", ex);
            }
        }

        // ================================================================
        // DETAIL — all items of a single issue (by IssueCode)
        // ================================================================
        [HttpGet, Route("detail/{issueCode}")]
        public ActionResult Detail(string issueCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(issueCode))
                    return JsonError("Issue code is required.");

                int branchId = GetCurrentBranchId();

                var rows = db.Gen_Store
                    .Where(s => s.ReferenceNo == issueCode
                                && s.ReferenceType == "MATERIAL_ISSUE"
                                && s.Deleted != true
                                && s.BranchId == branchId)
                    .OrderBy(s => s.Id)
                    .ToList();

                if (!rows.Any())
                    return JsonError("Issue not found.");

                var first = rows.First();
                int? workOrderId = first.ReferenceId;
                string workOrderCode = workOrderId.HasValue
                    ? db.WorkOrders.Where(w => w.Id == workOrderId.Value)
                                   .Select(w => w.WorkOrderCode).FirstOrDefault()
                    : null;

                var rawItemIds = rows.Select(r => r.ItemId).Distinct().ToList();
                var rawMap = db.Gen_RawItems
                    .Where(r => rawItemIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);

                // Try to back-link RawItem -> Thread for richer display
                var threadByRaw = db.Gen_Threads
                    .Where(t => t.RawItemId.HasValue && rawItemIds.Contains(t.RawItemId.Value) && t.Deleted != true)
                    .ToList()
                    .GroupBy(t => t.RawItemId.Value)
                    .ToDictionary(g => g.Key, g => g.First());

                var items = rows.Select(r =>
                {
                    Gen_Threads t = null;
                    if (threadByRaw.ContainsKey(r.ItemId)) t = threadByRaw[r.ItemId];

                    return new
                    {
                        id = r.Id,
                        rawItemId = r.ItemId,
                        rawItemName = rawMap.ContainsKey(r.ItemId) ? rawMap[r.ItemId] : "-",
                        threadId = t?.Id,
                        threadCode = t?.Code,
                        threadName = t?.Name,
                        threadColor = t?.Color,
                        unit = r.Unit,
                        qty = r.OutQty ?? 0m,
                        rate = r.Rate ?? 0m,
                        amount = r.Amount ?? 0m,
                        remarks = r.Remarks
                    };
                }).ToList();

                var data = new
                {
                    issueCode,
                    workOrderId,
                    workOrderCode,
                    issueDate = first.CreatedDate,
                    issueDateText = first.CreatedDate.HasValue ? first.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm") : "",
                    createdByEmployeeId = GetEmployeeId(first.CreatedBy),
                    items,
                    totalQty = items.Sum(x => x.qty),
                    totalAmount = items.Sum(x => x.amount)
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load issue.", ex);
            }
        }

        // ================================================================
        // REVERSE — soft-delete the OUT rows and write compensating IN rows
        // (preserves a full audit trail in Gen_Store).
        // ================================================================
        [HttpPost, Route("reverse/{issueCode}")]
        public ActionResult Reverse(string issueCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(issueCode))
                    return JsonError("Issue code is required.");

                var rows = db.Gen_Store
                    .Where(s => s.ReferenceNo == issueCode
                                && s.ReferenceType == "MATERIAL_ISSUE"
                                && s.Deleted != true)
                    .ToList();

                if (!rows.Any())
                    return JsonError("Issue not found or already reversed.");

                int userId = GetCurrentUserId();
                DateTime now = DateTime.UtcNow;
                string reverseCode = issueCode + "-REV";

                using (var tx = db.Database.BeginTransaction())
                {
                    try
                    {
                        foreach (var r in rows)
                        {
                            r.Deleted = true;
                            r.Active = false;
                            r.UpdatedBy = userId;
                            r.UpdatedDate = now;
                            r.Remarks = ((r.Remarks ?? "") + " [REVERSED " + reverseCode + "]").Trim();

                            db.Gen_Store.Add(new Gen_Store
                            {
                                ItemId = r.ItemId,
                                BranchId = r.BranchId,
                                WarehouseId = r.WarehouseId,
                                TransactionDate = now,

                                TransactionType = "IN",
                                ReferenceType = "MATERIAL_ISSUE_REVERSE",
                                ReferenceId = r.ReferenceId,
                                ReferenceNo = reverseCode,

                                OpeningQty = 0m,
                                InQty = r.OutQty ?? 0m,
                                OutQty = 0m,
                                CurrentQty = r.OutQty ?? 0m,

                                Unit = r.Unit,
                                Rate = r.Rate,
                                Amount = r.Amount,

                                Remarks = "Reversal of " + issueCode,

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

                        return JsonSuccess("Issue reversed.", new { issueCode, reverseCode, itemCount = rows.Count });
                    }
                    catch (Exception ex)
                    {
                        tx.Rollback();
                        return JsonError("Reverse failed.", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("Failed to reverse issue.", ex);
            }
        }

        // ================================================================
        // ISSUE CODE generator: ISS-YYYY-NNNN scoped by year, scans Gen_Store
        // ================================================================
        private string NextIssueCode(int year)
        {
            string prefix = "ISS-" + year + "-";
            int next = 1;

            var lastRef = db.Gen_Store
                .Where(s => s.ReferenceType == "MATERIAL_ISSUE"
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

        // ─────────────────────────────────────────────────────────────
        //  TOCTOU LOCK on (ItemId, BranchId) — mirrors
        //  Gen_StoreController.LockBalanceSlice. Holds a row-range
        //  UPDLOCK on the item+branch slice of the Gen_Store ledger
        //  until the surrounding transaction commits, so a concurrent
        //  Save() for the same thread/raw item serialises instead of
        //  reading the same stale balance and double-issuing stock.
        //  Call this immediately BEFORE GetCurrentStoreBalance inside
        //  any transaction that writes a new ledger row.
        // ─────────────────────────────────────────────────────────────
        private void LockBalanceSlice(int itemId, int branchId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT TOP 1 1 FROM dbo.Gen_Store WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE ItemId = @itemId AND BranchId = @branchId",
                new SqlParameter("@itemId", itemId),
                new SqlParameter("@branchId", branchId));
        }

        // Local helper mirroring PurchaseController's GetCurrentStoreBalance
        private decimal GetCurrentStoreBalance(int itemId, int branchId, int? warehouseId)
        {
            var q = db.Gen_Store.Where(x => x.ItemId == itemId && x.Deleted != true && x.BranchId == branchId);
            if (warehouseId.HasValue)
                q = q.Where(x => x.WarehouseId == warehouseId.Value);

            decimal totalIn = q.Sum(x => (decimal?)x.InQty) ?? 0m;
            decimal totalOut = q.Sum(x => (decimal?)x.OutQty) ?? 0m;
            return totalIn - totalOut;
        }

        // Rate fallback for raw items with no explicit Rate/UnitCost (e.g.
        // fabrics/accessories): last rate this item was transacted at.
        // Mirrors Gen_StoreController.GetLastRate.
        private decimal GetLastRate(int itemId, int branchId)
        {
            return db.Gen_Store
                .Where(x => x.ItemId == itemId && x.BranchId == branchId && x.Deleted != true)
                .OrderByDescending(x => x.TransactionDate)
                .ThenByDescending(x => x.Id)
                .Select(x => (decimal?)x.Rate)
                .FirstOrDefault() ?? 0m;
        }
    }
}
