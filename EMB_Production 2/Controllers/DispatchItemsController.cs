using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using iTextSharp.text;
using iTextSharp.text.pdf;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/dispatch-items")]
    public class DispatchItemsController : BaseController
    {
        [HttpGet, Route("items/{dispatchId:int}")]
        public ActionResult Items(int dispatchId)
        {
            var dispatch = db.Dispatch.FirstOrDefault(d =>
                d.Id == dispatchId &&
                d.Deleted != true);

            if (dispatch == null)
                return JsonError("Dispatch not found.");

            string saleType = NormalizeSaleType(dispatch.SaleType);

            var rows = db.DispatchItems
                .Include("Gen_Products")
                .Where(x => x.DispatchId == dispatchId && x.Deleted != true)
                .OrderBy(x => x.Id)
                .ToList()
                .Select(x =>
                {
                    int productId = x.ProductId ?? 0;
                    int branchId = x.BranchId ?? dispatch.BranchId;

                    decimal reserved = productId > 0
                        ? GetCustomerReservedStockBalance(productId, branchId)
                        : 0m;

                    decimal free = productId > 0
                        ? GetFreeCompanyStockBalance(productId, branchId)
                        : 0m;

                    decimal available = saleType == "DIRECT_SALE"
                        ? free
                        : reserved;

                    decimal dispatchQty = x.DispatchQty ?? 0m;
                    decimal salePrice = x.SalePrice ?? 0m;

                    return new
                    {
                        id = x.Id,
                        dispatchId = x.DispatchId,

                        productId = productId,
                        productName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : "-",

                        branchId = branchId,

                        customerReservedStock = reserved,
                        freeCompanyStock = free,
                        totalFinishedStock = reserved + free,

                        availableQty = available,
                        availableQtySnapshot = x.AvailableQtySnapshot ?? available,

                        dispatchQty = dispatchQty,

                        costPrice = x.CostPrice ?? 0m,
                        baseSalePrice = x.BaseSalePrice ?? 0m,
                        discountPercent = x.DiscountPercent ?? 0m,
                        discountAmount = x.DiscountAmount ?? 0m,
                        salePrice = salePrice,
                        amount = x.Amount ?? (dispatchQty * salePrice),

                        unit = string.IsNullOrWhiteSpace(x.Unit) ? "PCS" : x.Unit,

                        remarks = x.Remarks ?? "",
                        active = x.Active == true,
                        deleted = x.Deleted == true,
                        isPosted = x.IsPosted
                    };
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = new
                {
                    dispatchIsPosted = dispatch.IsPosted,
                    dispatchIsReversed = dispatch.IsReversed,
                    saleType = saleType,
                    items = rows
                }
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("available-products")]
        public ActionResult AvailableProducts(string saleType = "CUSTOMER_DEMAND",int? branchId = null,
         int? customerId = null,int? jobOrderId = null,string search = "",int? excludeDispatchId = null)
        {
            try
            {
                int bId = branchId.HasValue && branchId.Value > 0
                    ? branchId.Value
                    : GetCurrentBranchId();

                saleType = NormalizeSaleType(saleType);
                search = (search ?? "").Trim();

                if (saleType == "DIRECT_SALE")
                {
                    int excludeId = excludeDispatchId ?? 0;

                    var products = db.Gen_Products
                        .Where(p => p.Deleted != true && p.Active == true)
                        .Select(p => new { p.Id, p.Prod_Name })
                        .ToList();

                    var directRows = new List<object>();

                    foreach (var p in products)
                    {
                        decimal freeStock = GetFreeCompanyStockBalance(p.Id, bId);

                        decimal draftDirect = db.DispatchItems
                            .Where(di =>
                                di.Deleted != true &&
                                di.ProductId == p.Id &&
                                di.BranchId == bId &&
                                di.Dispatch.Deleted != true &&
                                di.Dispatch.IsPosted != true &&
                                di.Dispatch.IsReversed != true &&
                                di.Dispatch.SaleType == "DIRECT_SALE" &&
                                (excludeId == 0 || di.DispatchId != excludeId))
                            .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                        freeStock = Math.Max(0m, freeStock - draftDirect);

                        if (freeStock <= 0m)
                            continue;

                        decimal costPrice = GetProductCostPrice(p.Id, bId);
                        // Walk-in / direct sale has no customer history,
                        // but the resolver falls through to global-last,
                        // master price, then cost × 1.10.
                        decimal baseSalePrice = GetCustomerBaseSalePrice(p.Id, bId, null);

                        directRows.Add(new
                        {
                            productId = p.Id,
                            productName = p.Prod_Name,
                            jobOrderId = (int?)null,
                            jobOrderCode = "",
                            customerReservedStock = 0m,
                            freeCompanyStock = freeStock,
                            totalFinishedStock = freeStock,
                            availableQty = freeStock,
                            dispatchQty = 0m,
                            costPrice,
                            baseSalePrice,
                            discountPercent = 0m,
                            discountAmount = 0m,
                            salePrice = baseSalePrice,
                            amount = 0m,
                            unit = "PCS"
                        });
                    }

                    return JsonSuccess("Available direct-sale products loaded.", directRows);
                }

                if (!customerId.HasValue || customerId.Value <= 0)
                    return JsonSuccess("Select customer first.", new List<object>());

                var demandQuery =
                    from j in db.Gen_JobOrders
                    join d in db.Gen_Design on j.DesignId equals d.Id
                    where
                        j.Deleted != true &&
                        j.Active == true &&
                        j.CustomerId == customerId.Value &&
                        j.BranchId == bId &&
                        d.ProductId.HasValue
                    select new
                    {
                        JobOrderId = j.Id,
                        JobOrderCode = j.JobOrderCode,
                        ProductId = d.ProductId.Value,
                        DemandQty = (decimal)j.OrderQty
                    };

                if (jobOrderId.HasValue && jobOrderId.Value > 0)
                {
                    int joFilter = jobOrderId.Value;
                    demandQuery = demandQuery.Where(x => x.JobOrderId == joFilter);
                }

                var demandProducts = demandQuery
                    .ToList()
                    .GroupBy(x => new { x.JobOrderId, x.JobOrderCode, x.ProductId })
                    .Select(g => new
                    {
                        JobOrderId = (int?)g.Key.JobOrderId,
                        JobOrderCode = g.Key.JobOrderCode,
                        ProductId = g.Key.ProductId,
                        DemandQty = g.Sum(x => x.DemandQty)
                    })
                    .ToList();

                var result = new List<object>();

                foreach (var d in demandProducts)
                {
                    var product = db.Gen_Products.FirstOrDefault(p =>
                        p.Id == d.ProductId &&
                        p.Deleted != true &&
                        p.Active == true);

                    if (product == null)
                        continue;

                    decimal reservedStock = GetCustomerReservedStockBalance(
                        d.ProductId,
                        bId,
                        customerId.Value
                    );

                    int joId = d.JobOrderId ?? 0;
                    int excludeId = excludeDispatchId ?? 0;

                    decimal alreadyDispatched = db.DispatchItems
                        .Where(di =>
                            di.Deleted != true &&
                            di.ProductId == d.ProductId &&
                            di.Dispatch.Deleted != true &&
                            di.Dispatch.IsReversed != true &&
                            di.Dispatch.SaleType == "CUSTOMER_DEMAND" &&
                            di.Dispatch.CustomerId == customerId.Value &&
                            di.Dispatch.BranchId == bId &&
                            (joId == 0 || di.Dispatch.JobOrderId == joId) &&
                            (excludeId == 0 || di.DispatchId != excludeId))
                        .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                    decimal remainingDemand = d.DemandQty - alreadyDispatched;
                    if (remainingDemand < 0m) remainingDemand = 0m;

                    decimal availableQty = Math.Min(remainingDemand, reservedStock);
                    if (availableQty < 0m) availableQty = 0m;

                    decimal freeStock = GetFreeCompanyStockBalance(d.ProductId, bId);
                    decimal costPrice = GetProductCostPrice(d.ProductId, bId);
                    decimal baseSalePrice = GetCustomerBaseSalePrice(d.ProductId, bId, customerId);
                    if (baseSalePrice <= 0m)
                        baseSalePrice = GetBaseSalePrice(costPrice);

                    result.Add(new
                    {
                        productId = d.ProductId,
                        productName = product.Prod_Name,
                        jobOrderId = d.JobOrderId,
                        jobOrderCode = d.JobOrderCode,
                        customerReservedStock = reservedStock,
                        freeCompanyStock = freeStock,
                        totalFinishedStock = reservedStock + freeStock,
                        availableQty = availableQty,
                        demandQty = d.DemandQty,
                        alreadyDispatchedQty = alreadyDispatched,
                        remainingDemandQty = remainingDemand,
                        dispatchQty = 0m,
                        costPrice,
                        baseSalePrice,
                        discountPercent = 0m,
                        discountAmount = 0m,
                        salePrice = baseSalePrice,
                        amount = 0m,
                        unit = "PCS"
                    });
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    result = result.Where(x =>
                    {
                        string name = Convert.ToString(x.GetType().GetProperty("productName").GetValue(x));
                        return !string.IsNullOrWhiteSpace(name) &&
                               name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                    }).ToList();
                }

                return JsonSuccess("Customer demand products loaded.", result);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load available products: " + ex.Message);
            }
        }

        [HttpPost, Route("create")]
        public ActionResult Create(DispatchItemDto dto)
        {
            if (dto == null)
                return JsonError("Invalid payload.");

            if (dto.DispatchId <= 0)
                return JsonError("Invalid dispatch.");

            if (dto.ProductId <= 0)
                return JsonError("Product is required.");

            decimal qty = dto.DispatchQty;

            if (qty <= 0)
                return JsonError("Dispatch quantity must be greater than zero.");

            var dispatch = db.Dispatch.FirstOrDefault(d =>
                d.Id == dto.DispatchId &&
                d.Deleted != true);

            if (dispatch == null)
                return JsonError("Dispatch not found.");

            if (dispatch.IsPosted)
                return JsonError("Posted dispatch cannot be modified.");

            if (dispatch.IsReversed)
                return JsonError("Reversed dispatch cannot be modified.");

            string saleType = NormalizeSaleType(dispatch.SaleType);

            decimal available = GetDispatchAvailableQty(
                dto.ProductId,
                dispatch.BranchId,
                saleType,
                dispatch.Id,
                dispatch.CustomerId);

            if (qty > available)
            {
                return JsonError(
                    saleType == "DIRECT_SALE"
                        ? "Dispatch quantity exceeds free company stock (pending drafts already reserved)."
                        : "Dispatch quantity exceeds customer reserved stock (pending drafts already reserved).");
            }

            bool exists = db.DispatchItems.Any(x =>
                x.DispatchId == dto.DispatchId &&
                x.ProductId == dto.ProductId &&
                x.Deleted != true);

            if (exists)
                return JsonError("This product is already added.");

            decimal costPrice = dto.CostPrice > 0m
                ? dto.CostPrice
                : GetProductCostPrice(dto.ProductId, dispatch.BranchId);

            decimal baseSalePrice = dto.BaseSalePrice > 0m
                ? dto.BaseSalePrice
                : GetBaseSalePrice(costPrice);

            decimal discountPercent = dto.DiscountPercent < 0m ? 0m : dto.DiscountPercent;
            decimal discountAmount = dto.DiscountAmount < 0m ? 0m : dto.DiscountAmount;

            decimal salePrice = dto.SalePrice > 0m
                ? dto.SalePrice
                : CalculateSalePrice(baseSalePrice, discountPercent, discountAmount);

            decimal amount = qty * salePrice;

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            db.DispatchItems.Add(new DispatchItems
            {
                DispatchId = dto.DispatchId,
                ProductId = dto.ProductId,
                BranchId = dispatch.BranchId,

                DispatchQty = qty,
                AvailableQtySnapshot = available,

                CostPrice = costPrice,
                BaseSalePrice = baseSalePrice,
                DiscountPercent = discountPercent,
                DiscountAmount = discountAmount,
                SalePrice = salePrice,
                Amount = amount,

                Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "PCS" : dto.Unit,
                Remarks = dto.Remarks ?? "",

                IsPosted = false,
                Active = true,
                Deleted = false,

                CreatedBy = userId,
                CreatedDate = now,
                UpdatedBy = userId,
                UpdatedDate = now,
                RecordTimeStamp = now
            });

            db.SaveChanges();

            return JsonSuccess("Dispatch item added successfully.");
        }

        [HttpPost, Route("update")]
        public ActionResult Update(DispatchItemDto dto)
        {
            if (dto == null || dto.Id <= 0)
                return JsonError("Invalid item.");

            if (dto.ProductId <= 0)
                return JsonError("Product is required.");

            decimal qty = dto.DispatchQty;

            if (qty <= 0)
                return JsonError("Dispatch quantity must be greater than zero.");

            var row = db.DispatchItems
                .Include("Dispatch")
                .FirstOrDefault(x => x.Id == dto.Id && x.Deleted != true);

            if (row == null)
                return JsonError("Item not found.");

            if (row.Dispatch == null)
                return JsonError("Dispatch header not found.");

            if (row.Dispatch.IsPosted)
                return JsonError("Posted dispatch cannot be modified.");

            if (row.Dispatch.IsReversed)
                return JsonError("Reversed dispatch cannot be modified.");

            string saleType = NormalizeSaleType(row.Dispatch.SaleType);

            decimal available = GetDispatchAvailableQty(
                dto.ProductId,
                row.Dispatch.BranchId,
                saleType,
                row.Dispatch.Id,
                row.Dispatch.CustomerId);

            if (qty > available)
            {
                return JsonError(
                    saleType == "DIRECT_SALE"
                        ? "Dispatch quantity exceeds free company stock (pending drafts already reserved)."
                        : "Dispatch quantity exceeds customer reserved stock (pending drafts already reserved).");
            }

            bool exists = db.DispatchItems.Any(x =>
                x.Id != row.Id &&
                x.DispatchId == row.DispatchId &&
                x.ProductId == dto.ProductId &&
                x.Deleted != true);

            if (exists)
                return JsonError("This product is already added.");

            decimal costPrice = dto.CostPrice > 0m
                ? dto.CostPrice
                : GetProductCostPrice(dto.ProductId, row.Dispatch.BranchId);

            decimal baseSalePrice = dto.BaseSalePrice > 0m
                ? dto.BaseSalePrice
                : GetBaseSalePrice(costPrice);

            decimal discountPercent = dto.DiscountPercent < 0m ? 0m : dto.DiscountPercent;
            decimal discountAmount = dto.DiscountAmount < 0m ? 0m : dto.DiscountAmount;

            decimal salePrice = dto.SalePrice > 0m
                ? dto.SalePrice
                : CalculateSalePrice(baseSalePrice, discountPercent, discountAmount);

            decimal amount = qty * salePrice;

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            row.ProductId = dto.ProductId;
            row.BranchId = row.Dispatch.BranchId;
            row.DispatchQty = qty;
            row.AvailableQtySnapshot = available;

            row.CostPrice = costPrice;
            row.BaseSalePrice = baseSalePrice;
            row.DiscountPercent = discountPercent;
            row.DiscountAmount = discountAmount;
            row.SalePrice = salePrice;
            row.Amount = amount;

            row.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "PCS" : dto.Unit;
            row.Remarks = dto.Remarks ?? "";
            row.Active = true;

            row.UpdatedBy = userId;
            row.UpdatedDate = now;
            row.RecordTimeStamp = now;

            db.SaveChanges();

            return JsonSuccess("Dispatch item updated successfully.");
        }

        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            var row = db.DispatchItems
                .Include("Dispatch")
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (row == null)
                return JsonError("Item not found.");

            if (row.Dispatch == null)
                return JsonError("Dispatch header not found.");

            if (row.Dispatch.IsPosted)
                return JsonError("Posted dispatch item cannot be deleted.");

            if (row.Dispatch.IsReversed)
                return JsonError("Reversed dispatch item cannot be deleted.");

            int userId = GetCurrentUserId();
            DateTime now = DateTime.Now;

            row.Deleted = true;
            row.Active = false;
            row.UpdatedBy = userId;
            row.UpdatedDate = now;
            row.RecordTimeStamp = now;

            db.SaveChanges();

            return JsonSuccess("Dispatch item deleted successfully.");
        }

        [HttpGet]
        [Route("customer-job-orders")]
        public ActionResult CustomerJobOrders(int customerId, int? branchId = null)
        {
            try
            {
                if (customerId <= 0)
                    return JsonSuccess("Select customer first.", new List<object>());

                var q =
                    from j in db.Gen_JobOrders
                    join d in db.Gen_Design on j.DesignId equals d.Id
                    join p in db.Gen_Products on d.ProductId equals p.Id into pp
                    from p in pp.DefaultIfEmpty()
                    where j.Deleted != true
                          && j.Active == true
                          && j.CustomerId == customerId
                    select new
                    {
                        id = j.Id,
                        code = j.JobOrderCode,
                        jobOrderCode = j.JobOrderCode,

                        branchId = j.BranchId,
                        designId = j.DesignId,
                        designCode = d.D_Code,
                        designNo = d.DNo,

                        productId = d.ProductId,
                        productName = p != null ? p.Prod_Name : d.ProductName,

                        displayText = j.JobOrderCode + " - " + (p != null ? p.Prod_Name : d.ProductName)
                    };

                if (branchId.HasValue && branchId.Value > 0)
                {
                    q = q.Where(x => x.branchId == branchId.Value);
                }

                var rows = q
                    .OrderByDescending(x => x.id)
                    .ToList();

                return JsonSuccess("Customer job orders loaded.", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load customer job orders: " + ex.Message);
            }
        }

        private string NormalizeSaleType(string saleType)
        {
            saleType = string.IsNullOrWhiteSpace(saleType)
                ? "CUSTOMER_DEMAND"
                : saleType.Trim().ToUpper();

            return saleType;
        }

        private decimal GetDispatchAvailableQty(int productId, int branchId, string saleType)
        {
            saleType = NormalizeSaleType(saleType);

            if (saleType == "DIRECT_SALE")
                return GetFreeCompanyStockBalance(productId, branchId);

            return GetCustomerReservedStockBalance(productId, branchId);
        }

        private decimal GetDispatchAvailableQty(int productId, int branchId, string saleType, int excludeDispatchId, int? customerId)
        {
            string st = NormalizeSaleType(saleType);

            decimal baseBalance;

            if (st == "DIRECT_SALE")
            {
                baseBalance = GetFreeCompanyStockBalance(productId, branchId);
            }
            else
            {
                baseBalance = customerId.HasValue && customerId.Value > 0
                    ? GetCustomerReservedStockBalance(productId, branchId, customerId.Value)
                    : GetCustomerReservedStockBalance(productId, branchId);
            }

            int excludeId = excludeDispatchId;
            int? scopedCustomerId = (st == "CUSTOMER_DEMAND") ? customerId : null;

            decimal otherDrafts = db.DispatchItems
                .Where(di =>
                    di.Deleted != true &&
                    di.ProductId == productId &&
                    di.Dispatch.Deleted != true &&
                    di.Dispatch.IsPosted != true &&
                    di.Dispatch.IsReversed != true &&
                    di.Dispatch.SaleType == st &&
                    di.Dispatch.BranchId == branchId &&
                    (excludeId == 0 || di.DispatchId != excludeId) &&
                    (!scopedCustomerId.HasValue || di.Dispatch.CustomerId == scopedCustomerId.Value))
                .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

            decimal available = baseBalance - otherDrafts;
            return available < 0m ? 0m : available;
        }

        private decimal GetProductCostPrice(int productId, int branchId)
        {
            // PRIORITY 1: Latest QC-posted finished stock Rate for this branch
            decimal rate = db.Gen_Stocks
                .Where(x =>
                    x.Deleted != true &&
                    x.Active == true &&
                    x.StockStage == "FINISHED" &&
                    x.BranchId == branchId &&
                    (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED") &&
                    (
                        x.ProdId == productId ||
                        (!x.ProdId.HasValue && x.ItemId == productId)
                    ) &&
                    (x.InQty ?? 0m) > 0 &&
                    (
                        x.ReferenceType == "QC_INSPECTION" ||
                        x.ReferenceType == "QC-POST-FINISHED"
                    ))
                .OrderByDescending(x => x.Id)
                .Select(x => (decimal?)x.Rate)
                .FirstOrDefault() ?? 0m;

            if (rate > 0m) return Math.Round(rate, 2);

            // PRIORITY 2: Latest CostPrice from any prior DispatchItem for
            // this product on this branch (last known cost).
            decimal lastDispatchCost = db.DispatchItems
                .Where(di =>
                    di.Deleted != true &&
                    di.ProductId == productId &&
                    di.BranchId == branchId &&
                    di.CostPrice.HasValue &&
                    di.CostPrice.Value > 0)
                .OrderByDescending(di => di.Id)
                .Select(di => (decimal?)di.CostPrice)
                .FirstOrDefault() ?? 0m;

            if (lastDispatchCost > 0m) return Math.Round(lastDispatchCost, 2);

            // PRIORITY 3: DesignCosting.ProductCostPerUnit — most authoritative
            // pre-production cost. The user defines this on the Design Costing
            // screen and it folds in material + production + service costs.
            decimal designCost = db.DesignCosting
                .Where(c => c.ProductId == productId
                         && c.Deleted != true
                         && c.ProductCostPerUnit.HasValue
                         && c.ProductCostPerUnit.Value > 0)
                .OrderByDescending(c => c.Id)
                .Select(c => (decimal?)c.ProductCostPerUnit)
                .FirstOrDefault() ?? 0m;

            if (designCost > 0m) return Math.Round(designCost, 2);

            // PRIORITY 4: Back-derive from Gen_Products.Price (master sale
            // price) assuming the standard 10% markup. Gives a non-zero
            // working cost so discount cascade has something to bite into.
            decimal masterSalePrice = db.Gen_Products
                .Where(p => p.Id == productId && p.Deleted != true)
                .Select(p => (decimal?)p.Price)
                .FirstOrDefault() ?? 0m;

            if (masterSalePrice > 0m)
                return Math.Round(masterSalePrice / 1.10m, 2);

            return 0m;
        }

        private decimal GetBaseSalePrice(decimal costPrice)
        {
            return Math.Round(costPrice * 1.10m, 2);
        }

        private decimal CalculateSalePrice(decimal baseSalePrice, decimal discountPercent, decimal discountAmount)
        {
            decimal percentDiscount = baseSalePrice * discountPercent / 100m;
            decimal salePrice = baseSalePrice - percentDiscount - discountAmount;

            return salePrice < 0m ? 0m : Math.Round(salePrice, 2);
        }

        private decimal GetCustomerBaseSalePrice(int productId, int branchId, int? customerId)
        {
            // PRIORITY 1: This customer's last paid price for this product
            //             on this branch (sticky customer pricing).
            if (customerId.HasValue && customerId.Value > 0)
            {
                decimal lastCustomerPrice = db.DispatchItems
                    .Where(x =>
                        x.Deleted != true &&
                        x.ProductId == productId &&
                        x.BaseSalePrice.HasValue &&
                        x.BaseSalePrice.Value > 0 &&
                        x.Dispatch.Deleted != true &&
                        x.Dispatch.CustomerId == customerId.Value &&
                        x.Dispatch.BranchId == branchId)
                    .OrderByDescending(x => x.Dispatch.DispatchDate)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (decimal?)x.BaseSalePrice)
                    .FirstOrDefault() ?? 0m;

                if (lastCustomerPrice > 0m)
                    return lastCustomerPrice;
            }

            // PRIORITY 2: Last sale price to ANY customer (or walk-in) for
            //             this product on this branch.
            decimal lastAnyPrice = db.DispatchItems
                .Where(x =>
                    x.Deleted != true &&
                    x.ProductId == productId &&
                    x.BranchId == branchId &&
                    x.BaseSalePrice.HasValue &&
                    x.BaseSalePrice.Value > 0 &&
                    x.Dispatch.Deleted != true)
                .OrderByDescending(x => x.Dispatch.DispatchDate)
                .ThenByDescending(x => x.Id)
                .Select(x => (decimal?)x.BaseSalePrice)
                .FirstOrDefault() ?? 0m;

            if (lastAnyPrice > 0m)
                return lastAnyPrice;

            // PRIORITY 3: DesignCosting.SellingPricePerUnit — the user's
            // saved selling price on the Design Costing screen. More
            // authoritative than the master Gen_Products.Price because it
            // reflects the actual cost+margin calculation.
            decimal designSellPrice = db.DesignCosting
                .Where(c => c.ProductId == productId
                         && c.Deleted != true
                         && c.SellingPricePerUnit.HasValue
                         && c.SellingPricePerUnit.Value > 0)
                .OrderByDescending(c => c.Id)
                .Select(c => (decimal?)c.SellingPricePerUnit)
                .FirstOrDefault() ?? 0m;

            if (designSellPrice > 0m)
                return Math.Round(designSellPrice, 2);

            // PRIORITY 4: Master sale price on Gen_Products.
            decimal masterPrice = db.Gen_Products
                .Where(p => p.Id == productId && p.Deleted != true)
                .Select(p => (decimal?)p.Price)
                .FirstOrDefault() ?? 0m;

            if (masterPrice > 0m)
                return Math.Round(masterPrice, 2);

            // PRIORITY 5: Cost × 1.10 markup (the original behaviour).
            decimal costPrice = GetProductCostPrice(productId, branchId);
            if (costPrice > 0m)
                return GetBaseSalePrice(costPrice);

            return 0m;
        }

        private decimal GetCustomerDemandRemaining(int customerId, int productId, int branchId)
        {
            var demandRows =
                (from j in db.Gen_JobOrders
                 join d in db.Gen_Design on j.DesignId equals d.Id
                 where
                     j.Deleted != true &&
                     j.Active == true &&
                     j.CustomerId == customerId &&
                     j.BranchId == branchId &&
                     d.ProductId == productId
                 select new
                 {
                     JobOrderId = j.Id,
                     DemandQty = j.OrderQty 
                 })
                .ToList();

            decimal totalDemand = demandRows.Sum(x => x.DemandQty);

            var jobOrderIds = demandRows.Select(x => x.JobOrderId).ToList();

            decimal dispatched = db.DispatchItems
                .Where(di =>
                    di.Deleted != true &&
                    di.ProductId == productId &&
                    di.Dispatch.Deleted != true &&
                    di.Dispatch.IsPosted == true &&
                    di.Dispatch.IsReversed != true &&
                    di.Dispatch.SaleType == "CUSTOMER_DEMAND" &&
                    di.Dispatch.CustomerId == customerId &&
                    di.Dispatch.BranchId == branchId &&
                    di.Dispatch.JobOrderId.HasValue &&
                    jobOrderIds.Contains(di.Dispatch.JobOrderId.Value))
                .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

            decimal remaining = totalDemand - dispatched;

            return remaining < 0m ? 0m : remaining;
        }

        private decimal GetCustomerReservedStockBalance(int productId, int branchId, int customerId)
        {
            var stockRows = db.Gen_Stocks
                .Where(gs =>
                    gs.Deleted != true &&
                    gs.Active == true &&
                    gs.StockStage == "FINISHED" &&
                    gs.BranchId == branchId &&
                    (gs.ItemType == "PRODUCT" || gs.ItemType == "FINISHED") &&
                    (
                        gs.ProdId == productId ||
                        (!gs.ProdId.HasValue && gs.ItemId == productId)
                    ) &&
                    gs.ReferenceType == "QC_INSPECTION")
                .ToList();

            decimal inQty = 0m;
            decimal outQty = 0m;

            foreach (var s in stockRows)
            {
                var qc = db.QC_Inspection.FirstOrDefault(q =>
                    q.Id == s.ReferenceId &&
                    q.Deleted != true &&
                    q.Status != "Reversed" &&
                    q.Status != "Cancelled" &&
                    q.WorkOrderId.HasValue);

                if (qc == null)
                    continue;

                var wo = db.WorkOrders.FirstOrDefault(w =>
                    w.Id == qc.WorkOrderId.Value &&
                    w.Deleted != true &&
                    w.JobOrderId.HasValue);

                if (wo == null)
                    continue;

                var jo = db.Gen_JobOrders.FirstOrDefault(j =>
                    j.Id == wo.JobOrderId.Value &&
                    j.Deleted != true &&
                    j.Active == true &&
                    j.CustomerId == customerId &&
                    j.BranchId == branchId);

                if (jo == null)
                    continue;

                inQty += s.InQty ?? 0m;
                outQty += s.OutQty ?? 0m;
            }

            decimal balance = inQty - outQty;
            return balance < 0m ? 0m : balance;
        }

        [HttpGet, Route("export-pdf/{dispatchId:int}")]
        public ActionResult ExportPdf(int dispatchId)
        {
            var dispatch = db.Dispatch.FirstOrDefault(x =>
                x.Id == dispatchId &&
                x.Deleted != true);

            if (dispatch == null)
                return JsonError("Dispatch not found.");

            var items = db.DispatchItems
                .Include("Gen_Products")
                .Where(x => x.DispatchId == dispatchId && x.Deleted != true)
                .OrderBy(x => x.Id)
                .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                doc.Add(new Paragraph(
                    $"Dispatch Items (Dispatch #{dispatchId})",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14)
                ));

                doc.Add(new Paragraph($"Date: {dispatch.DispatchDate:dd-MMM-yyyy}"));
                doc.Add(new Paragraph($"Sale Type: {NormalizeSaleType(dispatch.SaleType)}"));
                doc.Add(new Paragraph(" "));

                var table = new PdfPTable(5) { WidthPercentage = 100 };

                table.AddCell("ID");
                table.AddCell("Product");
                table.AddCell("Available");
                table.AddCell("Dispatch Qty");
                table.AddCell("Unit");

                foreach (var x in items)
                {
                    table.AddCell(x.Id.ToString());
                    table.AddCell(x.Gen_Products != null ? x.Gen_Products.Prod_Name : "-");
                    table.AddCell((x.AvailableQtySnapshot ?? 0m).ToString("0.##"));
                    table.AddCell((x.DispatchQty ?? 0m).ToString("0.##"));
                    table.AddCell(string.IsNullOrWhiteSpace(x.Unit) ? "PCS" : x.Unit);
                }

                doc.Add(table);
                doc.Close();

                return File(
                    ms.ToArray(),
                    "application/pdf",
                    $"DispatchItems_{dispatchId}.pdf"
                );
            }
        }
    }
}