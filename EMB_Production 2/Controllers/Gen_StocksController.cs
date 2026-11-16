using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/gen-stocks")]
    public class Gen_StocksController : BaseController
    {        
        // ------------------------------------------------------------------
        // IMPORTANT - this query is a DISPLAY/UI query, not the balance source
        // of truth. It applies filters that BaseController's balance helpers
        // (ComputeStockBuckets / GetFreeCompanyStockBalance /
        // GetCustomerReservedStockBalance) do NOT apply:
        //   - restricts ItemType to "PRODUCT" only (Base helpers also accept
        //     ItemType == "FINISHED")
        //   - requires the joined Gen_Products row to be Active == true
        //   - requires Gen_Products.Prod_Code to start with "PRD-"
        // These extra filters exist so the UI only lists "real" sellable
        // products, but they mean a product/row that is excluded here can
        // still be counted by the Base balance helpers (and vice versa for
        // ItemType == "FINISHED" rows). Any screen or export in this
        // controller that reports CustomerReservedStock / FreeCompanyStock /
        // TotalFinishedStock numbers must get those numbers from
        // GetCustomerReservedStockBalance / GetFreeCompanyStockBalance (see
        // BuildFinishedGroupedRows, List, Detail below) rather than
        // recomputing them from rows pulled out of this query, otherwise the
        // UI and dispatch-validation (which calls the Base helpers directly)
        // can disagree on available quantity.
        // ------------------------------------------------------------------
        private IQueryable<Gen_Stocks> FinishedStockQuery()
        {
            return db.Gen_Stocks
                .Include(s => s.Gen_Products)
                .Include(s => s.Branches)
                .Where(s =>
                    s.Deleted != true &&
                    (s.StockStage ?? "").ToUpper() == "FINISHED" &&

                    // only PRODUCT stock
                    (s.ItemType ?? "").ToUpper() == "PRODUCT" &&

                    // product must be active and not deleted
                    s.Gen_Products != null &&
                    s.Gen_Products.Deleted != true &&
                    s.Gen_Products.Active == true &&

                    // finished sale products only
                    s.Gen_Products.Prod_Code != null &&
                    s.Gen_Products.Prod_Code.StartsWith("PRD-"));
        }
       
        private string ResolveItemName(Gen_Stocks s)
        {
            if (s == null) return "-";

            if (s.Gen_Products != null && !string.IsNullOrWhiteSpace(s.Gen_Products.Prod_Name))
                return s.Gen_Products.Prod_Name;

            if (s.ProdId.HasValue)
                return $"Product #{s.ProdId.Value}";

            return !string.IsNullOrWhiteSpace(s.StockDescription)
                ? s.StockDescription
                : "Finished Product";
        }

        private decimal GetCustomerReservedStock(int? productId, int? branchId)
        {
            // Later connect this with Dispatch / Customer Reservation table.
            // For now no reservation module is finalized, so reserved stock = 0.
            return 0m;
        }

        private StockRowViewModel MapRow(Gen_Stocks s)
        {
            return new StockRowViewModel
            {
                Id = s.Id,

                ProdId = s.ProdId,
                ItemType = s.ItemType,
                ItemId = s.ItemId,
                ItemName = ResolveItemName(s),

                BranchId = s.BranchId,
                BranchName = s.Branches != null ? s.Branches.Name : "",

                WarehouseId = s.WarehouseId,
                WarehouseName = "",

                StockStage = s.StockStage,
                TransactionType = s.TransactionType,

                OpeningQty = s.OpeningQty ?? 0,
                InQty = s.InQty ?? 0,
                OutQty = s.OutQty ?? 0,
                Qty = s.Qty ?? 0,
                CurrentQty = s.CurrentQty ?? 0,

                Rate = s.Rate,
                Amount = s.Amount,
                LastKnownRate = s.LastKnownRate,
                Unit = s.Unit ?? "PCS",

                ReferenceType = s.ReferenceType,
                ReferenceId = s.ReferenceId,
                ReferenceNo = s.ReferenceNo,

                StockDescription = s.StockDescription,
                Remarks = s.Remarks,

                Active = s.Active ?? true,
                Deleted = s.Deleted ?? false,

                CreatedBy = s.CreatedBy,
                UpdatedBy = s.UpdatedBy,
                RecordTimeStamp = s.RecordTimeStamp,
                CreatedDate = s.CreatedDate,
                UpdatedDate = s.UpdatedDate
            };
        }

        private List<StockRowViewModel> BuildFinishedGroupedRows(IQueryable<Gen_Stocks> query)
        {
            var rows = query.ToList();

            // Map every Dispatch.Id -> SaleType so OUT rows route correctly
            var dispatchIds = rows
                .Where(s => s.ReferenceType == "DISPATCH" && s.ReferenceId.HasValue)
                .Select(s => s.ReferenceId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, string> dispatchSaleTypeMap;
            if (dispatchIds.Count > 0)
            {
                dispatchSaleTypeMap = db.Dispatch
                    .Where(d => dispatchIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.SaleType })
                    .ToList()
                    .ToDictionary(d => d.Id, d => (d.SaleType ?? "").Trim().ToUpper());
            }
            else
            {
                dispatchSaleTypeMap = new Dictionary<int, string>();
            }

            var grouped = rows
                .GroupBy(s => new
                {
                    ProductId = s.ProdId ?? s.ItemId,
                    s.BranchId
                })
                .Select(g =>
                {
                    var latest = g
                        .OrderByDescending(x => x.RecordTimeStamp ?? x.CreatedDate ?? DateTime.MinValue)
                        .FirstOrDefault();

                    decimal customerIn = 0m;
                    decimal freeIn = 0m;
                    decimal customerOut = 0m;
                    decimal freeOut = 0m;

                    foreach (var s in g)
                    {
                        decimal inQty = SafeQty(s.InQty);
                        decimal outQty = SafeQty(s.OutQty);

                        if (inQty > 0)
                        {
                            if (IsCustomerReservedStock(s))
                                customerIn += inQty;
                            else
                                freeIn += inQty;
                        }

                        if (outQty > 0)
                        {
                            string refType = (s.ReferenceType ?? "").Trim().ToUpper();
                            bool isCustomerDrain = false;

                            if (refType == "DISPATCH" && s.ReferenceId.HasValue)
                            {
                                string st;
                                if (dispatchSaleTypeMap.TryGetValue(s.ReferenceId.Value, out st))
                                    isCustomerDrain = (st == "CUSTOMER_DEMAND");
                            }
                            else if (refType == "QC_INSPECTION" && IsCustomerReservedStock(s))
                            {
                                isCustomerDrain = true;
                            }

                            if (isCustomerDrain)
                                customerOut += outQty;
                            else
                                freeOut += outQty;
                        }
                    }

                    // Balance numbers must come from the same Base helpers that
                    // dispatch-validation uses (ComputeStockBuckets via
                    // GetCustomerReservedStockBalance/GetFreeCompanyStockBalance),
                    // not from a local re-derivation of this (filtered) row set,
                    // so the UI and dispatch validation can never drift apart.
                    // Fall back to the local split only when the group has no
                    // resolvable product/branch (should not normally happen).
                    decimal customerReserved;
                    decimal freeCompany;

                    int? groupProductId = g.Key.ProductId;
                    int? groupBranchId = g.Key.BranchId;

                    if (groupProductId.HasValue && groupBranchId.HasValue)
                    {
                        customerReserved = GetCustomerReservedStockBalance(groupProductId.Value, groupBranchId.Value);
                        freeCompany = GetFreeCompanyStockBalance(groupProductId.Value, groupBranchId.Value);
                    }
                    else
                    {
                        customerReserved = Math.Max(0m, customerIn - customerOut);
                        freeCompany = Math.Max(0m, freeIn - freeOut);
                    }

                    decimal totalFinished = customerReserved + freeCompany;
                    decimal totalOut = customerOut + freeOut;

                    string productName = latest != null
                        ? ResolveItemName(latest)
                        : "Finished Product";

                    return new StockRowViewModel
                    {
                        Id = latest != null ? latest.Id : 0,

                        ProdId = g.Key.ProductId,
                        ProductName = productName,

                        ItemType = "PRODUCT",
                        ItemId = g.Key.ProductId,
                        ItemName = productName,

                        BranchId = g.Key.BranchId,
                        BranchName = latest != null && latest.Branches != null ? latest.Branches.Name : "",

                        WarehouseId = latest != null ? latest.WarehouseId : null,
                        WarehouseName = "",

                        StockStage = "FINISHED",
                        TransactionType = latest != null ? latest.TransactionType : "FINISHED-BALANCE",

                        OpeningQty = 0m,
                        InQty = customerIn + freeIn,
                        OutQty = totalOut,
                        Qty = totalFinished,
                        CurrentQty = totalFinished,

                        CustomerReservedStock = customerReserved,
                        FreeCompanyStock = freeCompany,
                        TotalFinishedStock = totalFinished,

                        Rate = latest != null ? latest.Rate : null,
                        Amount = latest != null ? latest.Amount : null,
                        LastKnownRate = latest != null ? latest.LastKnownRate : null,
                        Unit = latest != null ? (latest.Unit ?? "PCS") : "PCS",

                        ReferenceType = latest != null ? latest.ReferenceType : "",
                        ReferenceId = latest != null ? latest.ReferenceId : null,
                        ReferenceNo = latest != null ? latest.ReferenceNo : "",

                        StockDescription = latest != null ? latest.StockDescription : productName,
                        Remarks = latest != null ? latest.Remarks : "",

                        RecordTimeStamp = latest != null ? latest.RecordTimeStamp : null,
                        CreatedDate = latest != null ? latest.CreatedDate : null,
                        UpdatedDate = latest != null ? latest.UpdatedDate : null,
                        CreatedBy = latest != null ? latest.CreatedBy : null,
                        UpdatedBy = latest != null ? latest.UpdatedBy : null,

                        Active = latest != null ? (latest.Active ?? true) : true,
                        Deleted = false
                    };
                })
                .Where(x => x.TotalFinishedStock > 0 || x.InQty > 0 || x.OutQty > 0)
                .OrderBy(x => x.ItemName)
                .ThenBy(x => x.BranchName)
                .ToList();

            return grouped;
        }

        private IEnumerable<StockRowViewModel> ApplySearch(IEnumerable<StockRowViewModel> rows, string search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return rows;

            search = search.Trim();

            return rows.Where(x =>
                (!string.IsNullOrWhiteSpace(x.ItemName) &&
                 x.ItemName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                (!string.IsNullOrWhiteSpace(x.ItemType) &&
                 x.ItemType.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                (!string.IsNullOrWhiteSpace(x.BranchName) &&
                 x.BranchName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                (!string.IsNullOrWhiteSpace(x.ReferenceNo) &&
                 x.ReferenceNo.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                (!string.IsNullOrWhiteSpace(x.ReferenceType) &&
                 x.ReferenceType.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            );
        }
      
        private decimal SafeQty(decimal? value)
        {
            return value ?? 0m;
        }

        private decimal GetProductSellingPrice(int productId, decimal unitCost)
        {
            if (productId <= 0 || unitCost <= 0m)
                return 0m;

            var cost =
                (from qc in db.QC_Inspection
                 join dc in db.DesignCosting on qc.DesignId equals dc.DesignId
                 where qc.ProductId == productId
                       && qc.Deleted != true
                       && dc.Deleted != true
                 orderby dc.Id descending
                 select new
                 {
                     dc.SellingPricePerUnit,
                     dc.MarginPercent
                 })
                .FirstOrDefault();

            decimal marginPercent = cost?.MarginPercent ?? 10m;

            decimal sellingPriceFromCost = unitCost * (1m + (marginPercent / 100m));

            if (cost != null && cost.SellingPricePerUnit.HasValue && cost.SellingPricePerUnit.Value >= unitCost)
                return cost.SellingPricePerUnit.Value;

            return sellingPriceFromCost;
        }

        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            int? branchId = null,
            int page = 1,
            int pageSize = 20,
            string stockStatusFilter = null,
            int? productIdFilter = null)
        {
            try
            {
                int userId = GetCurrentUserId();

                if (userId <= 0)
                {
                    Response.StatusCode = 401;
                    return Json(new
                    {
                        success = false,
                        message = "Unauthorized."
                    }, JsonRequestBehavior.AllowGet);
                }

                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 20;

                var stockRows = FinishedStockQuery();

                // Default to the caller's own branch when none is supplied, instead
                // of silently returning every branch's stock.
                int effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                    ? branchId.Value
                    : GetCurrentBranchId();

                stockRows = stockRows.Where(s => s.BranchId == effectiveBranchId);

                // Phase P.0 - product filter (used by per-item history)
                if (productIdFilter.HasValue && productIdFilter.Value > 0)
                    stockRows = stockRows.Where(s => s.ProdId == productIdFilter.Value);

                var draftDispatchAgg = db.DispatchItems
                    .Where(di =>
                        di.Deleted != true &&
                        di.Dispatch.Deleted != true &&
                        di.Dispatch.IsPosted != true &&
                        di.Dispatch.IsReversed != true)
                    .Select(di => new
                    {
                        ProductId = di.ProductId ?? 0,
                        BranchId = di.BranchId ?? di.Dispatch.BranchId,
                        SaleType = di.Dispatch.SaleType,
                        Qty = di.DispatchQty ?? 0m
                    })
                    .ToList()
                    .GroupBy(x => x.ProductId + "|" + x.BranchId)
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Customer = g.Where(x => (x.SaleType ?? "").Trim().ToUpper() == "CUSTOMER_DEMAND").Sum(x => x.Qty),
                            Direct = g.Where(x => (x.SaleType ?? "").Trim().ToUpper() == "DIRECT_SALE").Sum(x => x.Qty)
                        });

                // Map every Dispatch.Id → SaleType so we can route OUT rows correctly
                // (direct sale drains free stock, customer demand drains reserved stock).
                var dispatchSaleTypeMap = db.Dispatch
                    .Where(d => d.Deleted != true)
                    .Select(d => new { d.Id, d.SaleType })
                    .ToList()
                    .ToDictionary(d => d.Id, d => (d.SaleType ?? "").Trim().ToUpper());

                var rows = stockRows
                    .ToList()
                    .GroupBy(s => new
                    {
                        ProductId = s.ProdId ?? s.ItemId ?? 0,
                        BranchId = s.BranchId ?? 0,
                        Unit = string.IsNullOrWhiteSpace(s.Unit) ? "PCS" : s.Unit
                    })
                    .Select(g =>
                    {
                        int productId = g.Key.ProductId;
                        int bId = g.Key.BranchId;

                        var product = db.Gen_Products.FirstOrDefault(p =>
                            p.Id == productId &&
                            p.Deleted != true);

                        var branch = db.Branches.FirstOrDefault(b =>
                            b.Id == bId &&
                            (b.Deleted == false || b.Deleted == null));

                        decimal customerIn = 0m;
                        decimal freeIn = 0m;
                        decimal customerOut = 0m;
                        decimal freeOut = 0m;

                        decimal totalCostAmount = 0m;
                        decimal totalCostQty = 0m;

                        int qcPostCount = 0;
                        int dispatchCount = 0;
                        int reverseCount = 0;

                        foreach (var s in g)
                        {
                            decimal inQty = s.InQty ?? 0m;
                            decimal outQty = s.OutQty ?? 0m;

                            if (s.ReferenceType == "QC_INSPECTION" && inQty > 0)
                                qcPostCount++;

                            if (s.ReferenceType == "DISPATCH" && outQty > 0)
                                dispatchCount++;

                            if (
                                (s.TransactionType != null && s.TransactionType.ToUpper().Contains("REV")) ||
                                (s.ReferenceNo != null && s.ReferenceNo.ToUpper().Contains("REV"))
                            )
                                reverseCount++;

                            if (inQty > 0)
                            {
                                if (IsCustomerReservedStock(s))
                                    customerIn += inQty;
                                else
                                    freeIn += inQty;

                                decimal rate = s.LastKnownRate ?? s.Rate ?? 0m;

                                if (rate <= 0m && s.Amount.HasValue && inQty > 0m)
                                    rate = s.Amount.Value / inQty;

                                if (rate > 0m)
                                {
                                    totalCostAmount += inQty * rate;
                                    totalCostQty += inQty;
                                }
                            }

                            if (outQty > 0)
                            {
                                string refType = (s.ReferenceType ?? "").Trim().ToUpper();
                                bool isCustomerDrain = false;

                                if (refType == "DISPATCH" && s.ReferenceId.HasValue)
                                {
                                    string st;
                                    if (dispatchSaleTypeMap.TryGetValue(s.ReferenceId.Value, out st))
                                        isCustomerDrain = (st == "CUSTOMER_DEMAND");
                                }
                                else if (refType == "QC_INSPECTION" && IsCustomerReservedStock(s))
                                {
                                    isCustomerDrain = true;
                                }

                                if (isCustomerDrain)
                                    customerOut += outQty;
                                else
                                    freeOut += outQty;
                            }
                        }

                        // Use the same Base balance helpers dispatch-validation relies on
                        // (GetCustomerReservedStockBalance / GetFreeCompanyStockBalance)
                        // instead of re-deriving the split from this (filtered) row set,
                        // so the dashboard figures can never drift from dispatch validation.
                        decimal reservedStock = GetCustomerReservedStockBalance(productId, bId);
                        decimal freeStock = GetFreeCompanyStockBalance(productId, bId);

                        string aggKey = productId + "|" + bId;
                        if (draftDispatchAgg.ContainsKey(aggKey))
                        {
                            var pending = draftDispatchAgg[aggKey];
                            reservedStock = Math.Max(0m, reservedStock - pending.Customer);
                            freeStock = Math.Max(0m, freeStock - pending.Direct);
                        }

                        decimal totalFinished = reservedStock + freeStock;

                        decimal weightedAvgCost = totalCostQty > 0m
                            ? totalCostAmount / totalCostQty
                            : 0m;

                        decimal servicePerUnit = GetProductServicePerUnitCost(productId, bId);
                        decimal unitCost = weightedAvgCost + servicePerUnit;

                        decimal totalFinishedCost = totalFinished * unitCost;
                        decimal reservedValue = reservedStock * unitCost;
                        decimal freeStockValue = freeStock * unitCost;

                        decimal sellingPrice = GetProductSellingPrice(productId, unitCost);
                        decimal expectedRevenue = totalFinished * sellingPrice;
                        decimal estimatedProfit = expectedRevenue - totalFinishedCost;

                        decimal profitMarginPercent = expectedRevenue > 0m
                            ? (estimatedProfit / expectedRevenue) * 100m
                            : 0m;

                        string stockStatus;
                        string stockValueStatus;
                        string stockValueClass;

                        if (totalFinished < 0m)
                        {
                            stockStatus = "Negative";
                            stockValueStatus = "Negative Stock";
                            stockValueClass = "danger";
                        }
                        else if (totalFinished > 0m && unitCost <= 0m)
                        {
                            stockStatus = "Cost Missing";
                            stockValueStatus = "Zero Cost";
                            stockValueClass = "warning";
                        }
                        else if (totalFinished > 0m && totalFinishedCost > 0m)
                        {
                            stockStatus = "Available";
                            stockValueStatus = "Valued";
                            stockValueClass = "success";
                        }
                        else
                        {
                            stockStatus = "Empty";
                            stockValueStatus = "Empty";
                            stockValueClass = "secondary";
                        }

                        var latestRef = g
                            .OrderByDescending(x => x.Id)
                            .FirstOrDefault();

                        return new
                        {
                            id = latestRef != null ? latestRef.Id : 0,

                            itemType = "PRODUCT",
                            productId = productId,
                            itemId = productId,

                            itemName = product != null ? product.Prod_Name : "-",
                            productName = product != null ? product.Prod_Name : "-",

                            branchId = bId,
                            branchName = branch != null ? branch.Name : "-",

                            customerReservedStock = Math.Round(reservedStock, 0),
                            freeCompanyStock = Math.Round(freeStock, 0),
                            totalFinishedStock = Math.Round(totalFinished, 0),

                            unit = g.Key.Unit,

                            unitCost = Math.Round(unitCost, 0),
                            weightedAvgCost = Math.Round(weightedAvgCost, 2),

                            totalFinishedCost = Math.Round(totalFinishedCost, 0),
                            totalCost = Math.Round(totalFinishedCost, 0),

                            reservedValue = Math.Round(reservedValue, 0),
                            freeStockValue = Math.Round(freeStockValue, 0),

                            sellingPrice = Math.Round(sellingPrice, 0),
                            expectedRevenue = Math.Round(expectedRevenue, 0),
                            estimatedProfit = Math.Round(estimatedProfit, 0),
                            profitMarginPercent = Math.Round(profitMarginPercent, 2),

                            stockStatus = stockStatus,
                            stockValueStatus = stockValueStatus,
                            stockValueClass = stockValueClass,

                            qcPostCount = qcPostCount,
                            dispatchCount = dispatchCount,
                            reverseCount = reverseCount,
                            movementCount = g.Count(),

                            referenceType = latestRef != null ? latestRef.ReferenceType : "",
                            referenceNo = latestRef != null ? latestRef.ReferenceNo : "",
                            transactionType = latestRef != null ? latestRef.TransactionType : ""
                        };
                    })
                    .Where(x => x.totalFinishedStock > 0)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    rows = rows.Where(x =>
                        (!string.IsNullOrWhiteSpace(x.itemName) &&
                         x.itemName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                        (!string.IsNullOrWhiteSpace(x.branchName) &&
                         x.branchName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) ||

                        (!string.IsNullOrWhiteSpace(x.referenceNo) &&
                         x.referenceNo.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    ).ToList();
                }

                rows = rows
                    .OrderBy(x => x.itemName)
                    .ThenBy(x => x.branchName)
                    .ToList();

                // Phase P.0 - stockStatus filter applied after enrichment
                if (!string.IsNullOrWhiteSpace(stockStatusFilter))
                {
                    string ss = stockStatusFilter.Trim();
                    rows = rows.Where(x => string.Equals(x.stockStatus, ss, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                decimal totalInventoryCost = rows.Sum(x => x.totalCost);
                decimal totalReservedValue = rows.Sum(x => x.reservedValue);
                decimal freeStockValueTotal = rows.Sum(x => x.freeStockValue);

                decimal totalExpectedRevenue = rows.Sum(x => x.expectedRevenue);
                decimal totalEstimatedProfit = rows.Sum(x => x.estimatedProfit);

                decimal overallProfitMarginPercent = totalExpectedRevenue > 0m
                    ? (totalEstimatedProfit / totalExpectedRevenue) * 100m
                    : 0m;

                int totalItems = rows.Count;
                int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

                var items = rows
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        page,
                        pageSize,
                        totalItems,
                        totalPages,

                        dashboard = new
                        {
                            customerReservedStock = rows.Sum(x => x.customerReservedStock),
                            freeCompanyStock = rows.Sum(x => x.freeCompanyStock),
                            totalFinishedStock = rows.Sum(x => x.totalFinishedStock),

                            totalInventoryCost = Math.Round(totalInventoryCost, 0),
                            totalReservedValue = Math.Round(totalReservedValue, 0),
                            freeStockValue = Math.Round(freeStockValueTotal, 0),

                            totalExpectedRevenue = Math.Round(totalExpectedRevenue, 0),
                            totalEstimatedProfit = Math.Round(totalEstimatedProfit, 0),
                            overallProfitMarginPercent = Math.Round(overallProfitMarginPercent, 2),

                            totalQcPostCount = rows.Sum(x => x.qcPostCount),
                            totalDispatchCount = rows.Sum(x => x.dispatchCount),
                            totalReverseCount = rows.Sum(x => x.reverseCount),
                            totalMovementCount = rows.Sum(x => x.movementCount),

                            totalProducts = rows.Select(x => x.productId).Distinct().Count(),
                            totalBranches = rows.Select(x => x.branchId).Distinct().Count(),

                            // Phase P.0 - breakdown chips
                            byStockStatus = rows
                                .GroupBy(x => string.IsNullOrWhiteSpace(x.stockStatus) ? "Unknown" : x.stockStatus)
                                .Select(g => new { status = g.Key, count = g.Count() })
                                .OrderByDescending(x => x.count)
                                .ToList(),
                            byBranch = rows
                                .GroupBy(x => string.IsNullOrWhiteSpace(x.branchName) ? "-" : x.branchName)
                                .Select(g => new { branch = g.Key, count = g.Count(), qty = g.Sum(r => r.totalFinishedStock) })
                                .OrderByDescending(x => x.count)
                                .ToList()
                        }
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;

                return Json(new
                {
                    success = false,
                    message = "Failed to load finished stock summary.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ========================= Phase P.1 - ITEM HISTORY =========================
        [HttpGet]
        [Route("item-history/{productId:int}")]
        public ActionResult ItemHistory(int productId, int? branchId = null)
        {
            try
            {
                if (productId <= 0) return JsonError("Product id is required.");

                var product = db.Gen_Products.FirstOrDefault(p => p.Id == productId && p.Deleted != true);
                if (product == null) return JsonError("Product not found.");

                var q = db.Gen_Stocks
                    .Include(s => s.Branches)
                    .Where(s => s.ProdId == productId
                             && s.Deleted != true
                             && (s.StockStage ?? "").ToUpper() == "FINISHED"
                             && (s.ItemType ?? "").ToUpper() == "PRODUCT");

                // Default to the caller's own branch when none is supplied.
                int effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                    ? branchId.Value
                    : GetCurrentBranchId();

                q = q.Where(s => s.BranchId == effectiveBranchId);

                var rows = q
                    .OrderBy(s => s.CreatedDate)
                    .ThenBy(s => s.Id)
                    .ToList();

                string branchName = rows
                    .Select(r => r.Branches != null ? r.Branches.Name : null)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "-";

                decimal running = 0m;
                var ledger = rows.Select(r =>
                {
                    decimal inq = r.InQty ?? 0m;
                    decimal outq = r.OutQty ?? 0m;
                    running += inq - outq;
                    return new
                    {
                        id = r.Id,
                        dateText = r.CreatedDate.HasValue ? r.CreatedDate.Value.ToString("dd-MMM-yyyy") : "-",
                        transactionType = r.TransactionType ?? "",
                        referenceType = r.ReferenceType ?? "",
                        referenceNo = r.ReferenceNo ?? "",
                        referenceId = r.ReferenceId,
                        stockStage = r.StockStage ?? "",
                        inQty = inq,
                        outQty = outq,
                        balance = running,
                        unit = r.Unit ?? "PCS",
                        rate = r.Rate ?? r.LastKnownRate ?? 0m,
                        amount = r.Amount ?? 0m,
                        branchName = r.Branches != null ? r.Branches.Name : branchName,
                        remarks = r.Remarks ?? ""
                    };
                }).ToList();

                var data = new
                {
                    productId,
                    productName = product.Prod_Name,
                    productCode = product.Prod_Code,
                    branchId = effectiveBranchId,
                    branchName,
                    totalIn = ledger.Sum(x => x.inQty),
                    totalOut = ledger.Sum(x => x.outQty),
                    balance = running,
                    rowCount = ledger.Count,
                    ledger
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load item history. " + ex.Message);
            }
        }

        [HttpGet]
        [Route("branches")]
        public ActionResult Branches()
        {
            try
            {
                var rows = db.Branches
                    .Where(x => x.Deleted == false || x.Deleted == null)
                    .OrderBy(x => x.Name)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = x.Name
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
                Response.StatusCode = 500;
                return Json(new
                {
                    success = false,
                    message = "Failed to load branches.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }
       
        [HttpGet]
        [Route("dashboard-summary")]
        public ActionResult DashboardSummary(string search = "", int? branchId = null)
        {
            try
            {
                var query = FinishedStockQuery();

                // Default to the caller's own branch when none is supplied.
                int effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                    ? branchId.Value
                    : GetCurrentBranchId();

                query = query.Where(x => x.BranchId == effectiveBranchId);

                var groupedRows = BuildFinishedGroupedRows(query);
                var rows = ApplySearch(groupedRows, search).ToList();

                var summary = new StockDashboardViewModel
                {
                    CustomerReservedStock = rows.Sum(x => x.CustomerReservedStock),
                    FreeCompanyStock = rows.Sum(x => x.FreeCompanyStock),
                    TotalFinishedStock = rows.Sum(x => x.TotalFinishedStock),

                    TotalProducts = rows
                        .Where(x => x.ProdId.HasValue || x.ItemId.HasValue)
                        .Select(x => x.ProdId ?? x.ItemId)
                        .Distinct()
                        .Count(),

                    TotalBranches = rows
                        .Where(x => x.BranchId.HasValue)
                        .Select(x => x.BranchId.Value)
                        .Distinct()
                        .Count()
                };

                return Json(new
                {
                    success = true,
                    data = summary
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new
                {
                    success = false,
                    message = "Failed to load finished stock dashboard.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }
        
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var seed = FinishedStockQuery()
                    .FirstOrDefault(x => x.Id == id);

                if (seed == null)
                    return JsonError("Finished stock record not found.");

                // Hard branch check - a record belonging to another branch must
                // behave like it doesn't exist for this caller.
                if (seed.BranchId != GetCurrentBranchId())
                    return JsonError("Finished stock record not found.");

                var relatedRows = FinishedStockQuery()
                    .Where(x =>
                        x.ItemType == seed.ItemType &&
                        x.ItemId == seed.ItemId &&
                        x.BranchId == seed.BranchId &&
                        (x.StockStage ?? "") == (seed.StockStage ?? ""))
                    .ToList();

                if (!relatedRows.Any())
                    return JsonError("Finished stock detail records not found.");

                int productId = seed.ProdId ?? seed.ItemId ?? 0;
                int branchId = seed.BranchId ?? 0;

                decimal totalOpening = relatedRows.Sum(x => (decimal?)x.OpeningQty ?? 0m);
                decimal totalIn = relatedRows.Sum(x => (decimal?)x.InQty ?? 0m);
                decimal totalOut = relatedRows.Sum(x => (decimal?)x.OutQty ?? 0m);
                decimal current = totalOpening + totalIn - totalOut;

                // ----- Cost basis (weighted average across IN rows with rate) -----
                decimal totalCostAmount = 0m;
                decimal totalCostQty = 0m;
                foreach (var s in relatedRows)
                {
                    decimal inQty = s.InQty ?? 0m;
                    if (inQty <= 0m) continue;

                    decimal rate = s.LastKnownRate ?? s.Rate ?? 0m;
                    if (rate <= 0m && s.Amount.HasValue && inQty > 0m)
                        rate = s.Amount.Value / inQty;

                    if (rate > 0m)
                    {
                        totalCostAmount += inQty * rate;
                        totalCostQty += inQty;
                    }
                }
                decimal materialAvgCost = totalCostQty > 0m ? totalCostAmount / totalCostQty : 0m;
                decimal servicePerUnit = GetProductServicePerUnitCost(productId, branchId);
                decimal weightedAvgCost = materialAvgCost + servicePerUnit;
                decimal totalFinishedCost = current * weightedAvgCost;

                // ----- Customer-wise reservation breakdown -----
                var reservationRows = (
                    from gs in db.Gen_Stocks
                    join qc in db.QC_Inspection on gs.ReferenceId equals qc.Id
                    join wo in db.WorkOrders    on qc.WorkOrderId  equals wo.Id
                    join jo in db.Gen_JobOrders on wo.JobOrderId   equals jo.Id
                    join c  in db.Hr_Customers  on jo.CustomerId   equals c.Id
                    where gs.Deleted != true
                       && gs.StockStage == "FINISHED"
                       && gs.BranchId == branchId
                       && (gs.ItemType == "PRODUCT" || gs.ItemType == "FINISHED")
                       && (gs.ProdId == productId || (!gs.ProdId.HasValue && gs.ItemId == productId))
                       && gs.ReferenceType == "QC_INSPECTION"
                       && (gs.InQty ?? 0m) > 0
                       && qc.Deleted != true
                       && qc.Status != "Reversed"
                       && qc.Status != "Cancelled"
                       && wo.Deleted != true
                       && jo.Deleted != true
                       && jo.Active == true
                       && c.Deleted != true
                    select new
                    {
                        CustomerId   = c.Id,
                        CustomerCode = c.Customer_Id,
                        CustomerName = c.Customer_Name,
                        JobOrderCode = jo.JobOrderCode,
                        InQty        = gs.InQty ?? 0m,
                        OutQty       = gs.OutQty ?? 0m
                    }).ToList();

                var dispatchedPerCustomer = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productId
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsReversed != true
                              && di.Dispatch.SaleType == "CUSTOMER_DEMAND"
                              && di.Dispatch.BranchId == branchId
                              && di.Dispatch.CustomerId.HasValue)
                    .GroupBy(di => di.Dispatch.CustomerId.Value)
                    .Select(g => new
                    {
                        CustomerId = g.Key,
                        Dispatched = g.Sum(x => (decimal?)x.DispatchQty) ?? 0m
                    })
                    .ToList()
                    .ToDictionary(x => x.CustomerId, x => x.Dispatched);

                var customerReservations = reservationRows
                    .GroupBy(r => new { r.CustomerId, r.CustomerCode, r.CustomerName })
                    .Select(g =>
                    {
                        decimal reserved = g.Sum(x => x.InQty) - g.Sum(x => x.OutQty);
                        decimal dispatched = dispatchedPerCustomer.ContainsKey(g.Key.CustomerId)
                            ? dispatchedPerCustomer[g.Key.CustomerId]
                            : 0m;
                        decimal remaining = reserved - dispatched;
                        if (remaining < 0m) remaining = 0m;

                        return new StockCustomerReservationViewModel
                        {
                            CustomerId   = g.Key.CustomerId,
                            CustomerCode = g.Key.CustomerCode,
                            CustomerName = g.Key.CustomerName,
                            JobOrders    = string.Join(", ", g.Select(x => x.JobOrderCode).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                            ReservedQty  = reserved,
                            DispatchedQty = dispatched,
                            RemainingQty = remaining
                        };
                    })
                    .Where(x => x.ReservedQty > 0m)
                    .OrderByDescending(x => x.RemainingQty)
                    .ThenBy(x => x.CustomerName)
                    .ToList();

                // ----- Pending (draft) dispatches against this product -----
                decimal pendingDispatchQty = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productId
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsPosted != true
                              && di.Dispatch.IsReversed != true
                              && di.Dispatch.BranchId == branchId)
                    .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                int pendingDispatchCount = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productId
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsPosted != true
                              && di.Dispatch.IsReversed != true
                              && di.Dispatch.BranchId == branchId)
                    .Select(di => di.DispatchId)
                    .Distinct()
                    .Count();

                DateTime? firstMovement = relatedRows
                    .Select(x => x.CreatedDate ?? x.RecordTimeStamp)
                    .Where(d => d.HasValue)
                    .OrderBy(d => d)
                    .FirstOrDefault();

                DateTime? lastMovement = relatedRows
                    .Select(x => x.RecordTimeStamp ?? x.UpdatedDate ?? x.CreatedDate)
                    .Where(d => d.HasValue)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                // Net out customer-demand drafts from the reserved bucket
                decimal pendingCustomerDrafts = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productId
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsPosted != true
                              && di.Dispatch.IsReversed != true
                              && di.Dispatch.SaleType == "CUSTOMER_DEMAND"
                              && di.Dispatch.BranchId == branchId)
                    .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                decimal pendingDirectDrafts = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.ProductId == productId
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsPosted != true
                              && di.Dispatch.IsReversed != true
                              && di.Dispatch.SaleType == "DIRECT_SALE"
                              && di.Dispatch.BranchId == branchId)
                    .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                // Headline balance numbers must come from the same Base helpers that
                // dispatch-validation uses, not from a local re-derivation against
                // this (FinishedStockQuery-filtered) row set, so Detail can never
                // disagree with the List dashboard or with dispatch validation.
                // (customerReservations above remains a supplementary per-customer
                // breakdown and is not used for the headline totals.)
                decimal customerReservedDisplay = GetCustomerReservedStockBalance(productId, branchId);
                decimal freeStockDisplay = GetFreeCompanyStockBalance(productId, branchId);

                if (pendingDirectDrafts > 0m)
                    freeStockDisplay = Math.Max(0m, freeStockDisplay - pendingDirectDrafts);

                decimal totalFinishedDisplay = customerReservedDisplay + freeStockDisplay;
                decimal reservedValue = customerReservedDisplay * weightedAvgCost;
                decimal freeStockValue = freeStockDisplay * weightedAvgCost;

                var model = new StockDetailViewModel
                {
                    Id = seed.Id,

                    ProdId = seed.ProdId,
                    ItemType = seed.ItemType,
                    ItemId = seed.ItemId,
                    ItemName = ResolveItemName(seed),
                    ProductName = ResolveItemName(seed),

                    BranchId = seed.BranchId,
                    BranchName = seed.Branches != null ? seed.Branches.Name : "",

                    WarehouseId = seed.WarehouseId,
                    WarehouseName = "",

                    StockStage = seed.StockStage,
                    TransactionType = null,

                    OpeningQty = totalOpening,
                    InQty = totalIn,
                    OutQty = totalOut,
                    Qty = current,
                    CurrentQty = current,

                    CustomerReservedStock = customerReservedDisplay,
                    FreeCompanyStock = freeStockDisplay,
                    TotalFinishedStock = totalFinishedDisplay,

                    Rate = weightedAvgCost > 0m ? (decimal?)weightedAvgCost : null,
                    Amount = totalFinishedCost > 0m ? (decimal?)totalFinishedCost : null,
                    LastKnownRate = weightedAvgCost > 0m ? (decimal?)weightedAvgCost : null,
                    Unit = string.IsNullOrWhiteSpace(seed.Unit) ? "PCS" : seed.Unit,

                    ReservedValue = reservedValue,
                    FreeStockValue = freeStockValue,
                    TotalCost = totalFinishedCost,
                    TotalFinishedCost = totalFinishedCost,
                    WeightedAvgCost = weightedAvgCost,
                    UnitCost = weightedAvgCost,

                    ReferenceType = null,
                    ReferenceId = null,
                    ReferenceNo = null,

                    StockDescription = null,
                    Remarks = null,

                    Active = true,
                    Deleted = false,

                    CreatedBy = null,
                    UpdatedBy = null,

                    RecordTimeStamp = lastMovement,
                    CreatedDate = firstMovement,
                    UpdatedDate = lastMovement,

                    LastMovementDate = lastMovement,
                    FirstMovementDate = firstMovement,

                    QcPostCount = relatedRows.Count(x => x.ReferenceType == "QC_INSPECTION" && (x.InQty ?? 0m) > 0),
                    DispatchCount = relatedRows.Count(x => x.ReferenceType == "DISPATCH" && (x.OutQty ?? 0m) > 0),
                    ReverseCount = relatedRows.Count(x =>
                        (x.TransactionType != null && x.TransactionType.ToUpper().Contains("REV"))
                        || (x.ReferenceNo != null && x.ReferenceNo.ToUpper().Contains("REV"))),
                    MovementCount = relatedRows.Count,

                    PendingDispatchCount = pendingDispatchCount,
                    PendingDispatchQty = pendingDispatchQty,

                    CustomerReservations = customerReservations
                };

                return Json(new
                {
                    success = true,
                    message = "OK",
                    data = model
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load finished stock detail. " + ex.Message);
            }
        }
               
        [HttpGet]
        [Route("export-excel")]
        public ActionResult ExportExcel(string search = "", int? branchId = null)
        {
            var query = FinishedStockQuery();

            // Default to the caller's own branch when none is supplied.
            int effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                ? branchId.Value
                : GetCurrentBranchId();

            query = query.Where(s => s.BranchId == effectiveBranchId);

            var list = ApplySearch(BuildFinishedGroupedRows(query), search)
                .OrderBy(x => x.BranchName)
                .ThenBy(x => x.ItemName)
                .ToList();

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("FinishedStock");

                string[] headers =
                {
                    "Item Type", "Item", "Branch", "Stage", "Opening", "In", "Out", "Current",
                    "Unit", "Transaction", "Reference Type", "Reference No", "Rate", "Amount", "Status"
                };

                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = headers[c];
                    ws.Cells[1, c + 1].Style.Font.Bold = true;
                }

                int r = 2;
                foreach (var s in list)
                {
                    ws.Cells[r, 1].Value = s.ItemType;
                    ws.Cells[r, 2].Value = s.ItemName;
                    ws.Cells[r, 3].Value = s.BranchName;
                    ws.Cells[r, 4].Value = s.StockStage;
                    ws.Cells[r, 5].Value = s.OpeningQty;
                    ws.Cells[r, 6].Value = s.InQty;
                    ws.Cells[r, 7].Value = s.OutQty;
                    ws.Cells[r, 8].Value = s.CurrentQty;
                    ws.Cells[r, 9].Value = s.Unit;
                    ws.Cells[r, 10].Value = s.TransactionType;
                    ws.Cells[r, 11].Value = s.ReferenceType;
                    ws.Cells[r, 12].Value = s.ReferenceNo;
                    ws.Cells[r, 13].Value = s.Rate ?? 0;
                    ws.Cells[r, 14].Value = s.Amount ?? 0;
                    ws.Cells[r, 15].Value = s.Active ? "Active" : "Inactive";
                    r++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(
                    pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"FinishedStock_{DateTime.Now:yyyyMMddHHmm}.xlsx"
                );
            }
        }
       
        [HttpGet]
        [Route("export-pdf")]
        public ActionResult ExportPdf(string search = "", int? branchId = null)
        {
            var query = FinishedStockQuery();

            // Default to the caller's own branch when none is supplied.
            int effectiveBranchId = (branchId.HasValue && branchId.Value > 0)
                ? branchId.Value
                : GetCurrentBranchId();

            query = query.Where(s => s.BranchId == effectiveBranchId);

            var list = ApplySearch(BuildFinishedGroupedRows(query), search)
                .OrderBy(x => x.BranchName)
                .ThenBy(x => x.ItemName)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 20, 20, 20, 20);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var table = new PdfPTable(8) { WidthPercentage = 100 };

                string[] headers =
                {
                    "Item", "Branch", "Opening", "In", "Out", "Current", "Unit", "Ref No"
                };

                foreach (var h in headers)
                {
                    table.AddCell(new PdfPCell(new Phrase(h))
                    {
                        BackgroundColor = BaseColor.LIGHT_GRAY
                    });
                }

                foreach (var s in list)
                {
                    table.AddCell(s.ItemName ?? "");
                    table.AddCell(s.BranchName ?? "");
                    table.AddCell(s.OpeningQty.ToString("0.###"));
                    table.AddCell(s.InQty.ToString("0.###"));
                    table.AddCell(s.OutQty.ToString("0.###"));
                    table.AddCell(s.CurrentQty.ToString("0.###"));
                    table.AddCell(s.Unit ?? "PCS");
                    table.AddCell(s.ReferenceNo ?? "");
                }

                doc.Add(table);
                doc.Close();

                return File(
                    ms.ToArray(),
                    "application/pdf",
                    $"FinishedStock_{DateTime.Now:yyyyMMddHHmm}.pdf"
                );
            }
        }

        [HttpGet]
        [Route("print")]
        public ActionResult Print(string search = "", int? branchId = null)
        {
            var query = FinishedStockQuery();

            if (branchId.HasValue)
                query = query.Where(s => s.BranchId == branchId.Value);

            var list = ApplySearch(BuildFinishedGroupedRows(query), search)
                .OrderBy(x => x.BranchName)
                .ThenBy(x => x.ItemName)
                .ToList();

            var dashboard = new StockDashboardViewModel
            {
                CustomerReservedStock = list.Sum(x => x.CustomerReservedStock),
                FreeCompanyStock = list.Sum(x => x.FreeCompanyStock),
                TotalFinishedStock = list.Sum(x => x.TotalFinishedStock),

                TotalProducts = list
                    .Where(x => x.ProdId.HasValue || x.ItemId.HasValue)
                    .Select(x => x.ProdId ?? x.ItemId)
                    .Distinct()
                    .Count(),

                TotalBranches = list
                    .Where(x => x.BranchId.HasValue)
                    .Select(x => x.BranchId.Value)
                    .Distinct()
                    .Count()
            };

            return View("Print", new StocksPageViewModel
            {
                Dashboard = dashboard,
                Rows = list,
                Search = search,
                BranchId = branchId,
                Page = 1,
                PageSize = list.Count,
                TotalItems = list.Count,
                TotalPages = 1
            });
        }

        [HttpGet]
        [Route("index-view")]
        public ActionResult Index(string search = null, int? branchId = null, int page = 1, int pageSize = 20)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var query = FinishedStockQuery();

            if (branchId.HasValue)
                query = query.Where(s => s.BranchId == branchId.Value);

            var ordered = ApplySearch(BuildFinishedGroupedRows(query), search)
                .OrderByDescending(x => x.RecordTimeStamp ?? x.CreatedDate ?? DateTime.MinValue)
                .ToList();

            int totalItems = ordered.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var dashboard = new StockDashboardViewModel
            {
                CustomerReservedStock = ordered.Sum(x => x.CustomerReservedStock),
                FreeCompanyStock = ordered.Sum(x => x.FreeCompanyStock),
                TotalFinishedStock = ordered.Sum(x => x.TotalFinishedStock),

                TotalProducts = ordered
                    .Where(x => x.ProdId.HasValue || x.ItemId.HasValue)
                    .Select(x => x.ProdId ?? x.ItemId)
                    .Distinct()
                    .Count(),

                TotalBranches = ordered
                    .Where(x => x.BranchId.HasValue)
                    .Select(x => x.BranchId.Value)
                    .Distinct()
                    .Count()
            };

            var pagedRows = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var vm = new StocksPageViewModel
            {
                Dashboard = dashboard,
                Rows = pagedRows,
                Search = search,
                BranchId = branchId,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages
            };

            return View(vm);
        }

        [HttpGet]
        [Route("details-view/{id:int?}")]
        public ActionResult Details(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var s = FinishedStockQuery().FirstOrDefault(x => x.Id == id.Value);

            if (s == null)
                return HttpNotFound();

            return View(s);
        }

        [HttpGet]
        [Route("delete-view/{id:int?}")]
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var s = FinishedStockQuery().FirstOrDefault(x => x.Id == id.Value);

            if (s == null)
                return HttpNotFound();

            return View(s);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var s = db.Gen_Stocks.FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (s == null)
                return HttpNotFound();

            s.Deleted = true;
            s.UpdatedBy = GetCurrentUserId();
            s.UpdatedDate = DateTime.Now;

            db.SaveChanges();
            return RedirectToAction("Index");
        }

        [HttpGet]
        [Route("movement-detail")]
        public ActionResult MovementDetail(int productId, int? branchId = null)
        {
            try
            {
                if (productId <= 0)
                    return Json(new
                    {
                        success = false,
                        message = "Invalid product."
                    }, JsonRequestBehavior.AllowGet);

                var query = FinishedStockQuery()
                    .Where(x =>
                        (x.ProdId == productId || x.ItemId == productId));

                if (branchId.HasValue && branchId.Value > 0)
                    query = query.Where(x => x.BranchId == branchId.Value);

                var rows = query
                    .OrderBy(x => x.CreatedDate)
                    .ThenBy(x => x.Id)
                    .ToList();

                decimal balance = 0m;

                var movements = rows.Select(x =>
                {
                    decimal inQty = x.InQty ?? 0m;
                    decimal outQty = x.OutQty ?? 0m;

                    balance += inQty;
                    balance -= outQty;

                    decimal rate = x.LastKnownRate ?? x.Rate ?? 0m;

                    if (rate <= 0m && x.Amount.HasValue && (inQty + outQty) > 0m)
                        rate = x.Amount.Value / (inQty + outQty);

                    string movementType;

                    if (x.ReferenceType == "QC_INSPECTION" && inQty > 0)
                        movementType = "QC Posting";
                    else if (x.ReferenceType == "DISPATCH" && outQty > 0)
                        movementType = "Dispatch";
                    else if (
                        (x.TransactionType != null && x.TransactionType.ToUpper().Contains("REV")) ||
                        (x.ReferenceNo != null && x.ReferenceNo.ToUpper().Contains("REV"))
                    )
                        movementType = "Reverse";
                    else
                        movementType = x.TransactionType ?? "-";

                    return new
                    {
                        id = x.Id,
                        date = x.CreatedDate.HasValue
                            ? x.CreatedDate.Value.ToString("dd-MMM-yyyy hh:mm tt")
                            : "",

                        movementType = movementType,

                        inQty = inQty,
                        outQty = outQty,
                        balance = balance,

                        rate = Math.Round(rate, 2),
                        amount = Math.Round((inQty + outQty) * rate, 0),

                        referenceType = x.ReferenceType,
                        referenceId = x.ReferenceId,
                        referenceNo = x.ReferenceNo,

                        remarks = x.Remarks ?? x.StockDescription ?? ""
                    };
                }).ToList();

                return Json(new
                {
                    success = true,
                    message = "Stock movement detail loaded.",
                    data = new
                    {
                        productId,
                        branchId,
                        movements,
                        qcPostingHistory = movements.Where(x => x.movementType == "QC Posting").ToList(),
                        dispatchHistory = movements.Where(x => x.movementType == "Dispatch").ToList(),
                        reverseEntries = movements.Where(x => x.movementType == "Reverse").ToList(),
                        currentBalance = movements.LastOrDefault()?.balance ?? 0m
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;

                return Json(new
                {
                    success = false,
                    message = "Failed to load stock movement detail.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}