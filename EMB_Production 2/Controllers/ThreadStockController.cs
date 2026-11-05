using System;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/thread-stock")]
    public class ThreadStockController : BaseController
    {
        // Phase E: thread stock lives in Gen_Store. This controller is a thread-specific
        // VIEW over Gen_Store, filtering rows whose ItemId is a linked Gen_Threads.RawItemId.

        // ================================================================
        // BALANCE — one row per Thread x Branch with totals + recent rate
        // ================================================================
        [HttpGet, Route("balance")]
        public ActionResult Balance(int? branchId = null, string search = "")
        {
            try
            {
                int bid = branchId ?? GetCurrentBranchId();

                var threads = db.Gen_Threads
                    .Where(t => t.Deleted != true)
                    .Select(t => new
                    {
                        t.Id,
                        t.RawItemId,
                        t.Code,
                        t.Name,
                        t.Color,
                        t.Material,
                        t.UnitOfMeasure,
                        t.UnitCost,
                        t.Active
                    })
                    .ToList();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    threads = threads.Where(t =>
                        (t.Code ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (t.Name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (t.Color ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                    ).ToList();
                }

                // All linked RawItemIds in one set
                var rawIds = threads.Where(t => t.RawItemId.HasValue)
                                    .Select(t => t.RawItemId.Value)
                                    .Distinct()
                                    .ToList();

                // Aggregate Gen_Store once per RawItemId
                var ledger = db.Gen_Store
                    .Where(x => x.Deleted == false
                                && x.BranchId == bid
                                && rawIds.Contains(x.ItemId))
                    .GroupBy(x => x.ItemId)
                    .Select(g => new
                    {
                        RawItemId = g.Key,
                        TotalIn = g.Sum(x => (decimal?)x.InQty) ?? 0m,
                        TotalOut = g.Sum(x => (decimal?)x.OutQty) ?? 0m,
                        LastRate = g.OrderByDescending(x => x.Id)
                                    .Select(x => x.Rate)
                                    .FirstOrDefault()
                    })
                    .ToList()
                    .ToDictionary(x => x.RawItemId);

                // Linked raw-item names
                var rawNames = db.Gen_RawItems
                    .Where(r => rawIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);

                var data = threads.Select(t =>
                {
                    bool linked = t.RawItemId.HasValue && t.RawItemId.Value > 0;
                    decimal totalIn = 0m, totalOut = 0m;
                    decimal lastRate = t.UnitCost ?? 0m;
                    string rawItemName = "[unlinked]";

                    if (linked)
                    {
                        int rid = t.RawItemId.Value;
                        if (ledger.ContainsKey(rid))
                        {
                            totalIn = ledger[rid].TotalIn;
                            totalOut = ledger[rid].TotalOut;
                            lastRate = ledger[rid].LastRate ?? (t.UnitCost ?? 0m);
                        }
                        if (rawNames.ContainsKey(rid))
                            rawItemName = rawNames[rid];
                    }

                    return new
                    {
                        threadId = t.Id,
                        rawItemId = t.RawItemId,
                        isLinked = linked,
                        rawItemName,
                        code = t.Code,
                        name = t.Name,
                        color = t.Color,
                        material = t.Material,
                        unit = t.UnitOfMeasure,
                        unitCost = t.UnitCost ?? 0m,
                        active = t.Active ?? true,

                        totalIn,
                        totalOut,
                        balance = totalIn - totalOut,

                        lastRate,
                        balanceValue = (totalIn - totalOut) * lastRate
                    };
                })
                .OrderBy(x => x.code)
                .ToList();

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load thread stock.", ex);
            }
        }

        // ================================================================
        // HISTORY — full Gen_Store ledger for one thread's linked RawItem
        // ================================================================
        [HttpGet, Route("history/{threadId:int}")]
        public ActionResult History(int threadId, int? branchId = null)
        {
            try
            {
                int bid = branchId ?? GetCurrentBranchId();

                var thread = db.Gen_Threads.FirstOrDefault(t => t.Id == threadId && t.Deleted != true);
                if (thread == null) return JsonError("Thread not found.");

                if (!thread.RawItemId.HasValue || thread.RawItemId.Value <= 0)
                {
                    return JsonSuccess("OK", new
                    {
                        threadId = thread.Id,
                        code = thread.Code,
                        name = thread.Name,
                        color = thread.Color,
                        unit = thread.UnitOfMeasure,
                        branchId = bid,
                        isLinked = false,
                        rawItemName = "[unlinked]",
                        totalIn = 0m,
                        totalOut = 0m,
                        balance = 0m,
                        ledger = new object[0]
                    });
                }

                int rawId = thread.RawItemId.Value;
                var rawName = db.Gen_RawItems.Where(r => r.Id == rawId).Select(r => r.ItemName).FirstOrDefault() ?? "-";

                var rows = db.Gen_Store
                    .Where(x => x.Deleted == false
                                && x.ItemId == rawId
                                && x.BranchId == bid)
                    .OrderBy(x => x.Id)
                    .ToList();

                decimal running = 0m;
                var ledger = rows.Select(r =>
                {
                    decimal inq = r.InQty ?? 0m;
                    decimal outq = r.OutQty ?? 0m;
                    running += inq - outq;

                    return new
                    {
                        id = r.Id,
                        date = r.CreatedDate,
                        dateText = r.CreatedDate.HasValue ? r.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm") : "",
                        transactionType = r.TransactionType,
                        referenceType = r.ReferenceType,
                        referenceNo = r.ReferenceNo,
                        referenceId = r.ReferenceId,
                        inQty = inq,
                        outQty = outq,
                        balance = running,
                        unit = r.Unit,
                        rate = r.Rate ?? 0m,
                        amount = r.Amount ?? 0m,
                        remarks = r.Remarks
                    };
                }).ToList();

                var data = new
                {
                    threadId = thread.Id,
                    code = thread.Code,
                    name = thread.Name,
                    color = thread.Color,
                    unit = thread.UnitOfMeasure,
                    branchId = bid,
                    isLinked = true,
                    rawItemId = rawId,
                    rawItemName = rawName,
                    totalIn = ledger.Sum(x => x.inQty),
                    totalOut = ledger.Sum(x => x.outQty),
                    balance = running,
                    ledger
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load thread history.", ex);
            }
        }
    }
}
