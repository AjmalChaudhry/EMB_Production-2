using System;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.Models.Dto;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;
using Rotativa;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/design-costing")]
    public class DesignCostingController : BaseController
    {
        // ---------------- LIST ----------------
        [HttpGet, Route("list")]
        public ActionResult List(
            string activeFilter = "",
            string search = "",
            int? productId = null,
            int? designId = null,
            string marginRange = null,
            string costRange = null)
        {
            try
            {
                var query = db.DesignCosting
                    .Include("Gen_Design")
                    .Where(x => x.Deleted != true);

                if (activeFilter == "active")
                    query = query.Where(x => x.Active == true);
                else if (activeFilter == "inactive")
                    query = query.Where(x => x.Active == false);

                // Phase R.0 - additional filters
                if (productId.HasValue && productId.Value > 0)
                    query = query.Where(x => x.ProductId == productId.Value
                                          || (x.ProductId == null && x.Gen_Design != null && x.Gen_Design.ProductId == productId.Value));
                if (designId.HasValue && designId.Value > 0)
                    query = query.Where(x => x.DesignId == designId.Value);

                var rawData = (
                    from x in query.OrderByDescending(x => x.Id)
                    let effectiveProductId = x.ProductId ?? (x.Gen_Design != null ? x.Gen_Design.ProductId : null)
                    join p in db.Gen_Products on effectiveProductId equals (int?)p.Id into pj
                    from p in pj.DefaultIfEmpty()
                    select new
                    {
                        x.Id,
                        x.DesignId,

                        DesignCode = x.Gen_Design != null ? x.Gen_Design.D_Code : "",
                        DesignNo = x.Gen_Design != null ? x.Gen_Design.DNo : "",
                        DesignBranchId = x.Gen_Design != null ? x.Gen_Design.BranchId : (int?)null,

                        ProductId = p != null ? (int?)p.Id : null,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",

                        x.BatchQty,
                        x.MarginPercent,
                        x.SellingPricePerUnit,

                        x.ThreadCost,
                        x.MachineCost,
                        x.LaborCost,
                        x.ElectricityCost,
                        x.OtherCost,

                        x.TotalCost,
                        x.CostPerUnit,

                        x.MaterialCostPerUnit,
                        x.ProductionCostPerUnit,
                        x.ServiceCostPerUnit,
                        x.ProductCostPerUnit,
                        x.TotalProductCost,

                        x.Active,
                        x.CreatedDate
                    }
                ).ToList();

                int defaultBranchId = GetCurrentBranchId();

                var data = rawData.Select(x =>
                {
                    int bId = x.DesignBranchId ?? defaultBranchId;
                    int pId = x.ProductId ?? 0;
                    int qty = x.BatchQty > 0 ? x.BatchQty : 1;

                    decimal threadCost = x.ThreadCost ?? 0m;
                    decimal machineCost = x.MachineCost ?? 0m;
                    decimal laborCost = x.LaborCost ?? 0m;
                    decimal elecCost = x.ElectricityCost ?? 0m;
                    decimal otherCost = x.OtherCost ?? 0m;

                    decimal threadPerUnit = threadCost / qty;
                    decimal stocksMaterialUnit = GetProductMaterialPerUnitCost(pId, bId);
                    decimal materialPerUnit = threadPerUnit + stocksMaterialUnit;

                    decimal productionPerUnit = (machineCost + laborCost + elecCost + otherCost) / qty;

                    // Service cost: ALWAYS recompute from current Service Rates so
                    // rows saved before rates existed still reflect today's rates.
                    decimal servicePerUnitLive = GetProductServicePerUnitCost(pId, bId);

                    // Prefer the stored snapshot when it's non-zero, else use the live recompute.
                    decimal storedMaterial   = x.MaterialCostPerUnit ?? 0m;
                    decimal storedProduction = x.ProductionCostPerUnit ?? 0m;
                    decimal storedService    = x.ServiceCostPerUnit ?? 0m;

                    decimal displayMaterial   = storedMaterial > 0m   ? storedMaterial   : materialPerUnit;
                    decimal displayProduction = storedProduction > 0m ? storedProduction : productionPerUnit;
                    decimal displayService    = servicePerUnitLive > 0m ? servicePerUnitLive : storedService;

                    decimal productPerUnit  = Math.Round(displayMaterial + displayProduction + displayService, 0);
                    decimal totalProductCost = Math.Round(productPerUnit * x.BatchQty, 0);

                    // Selling price now derives from the FULL product cost (material + production + service),
                    // not just the design cost — otherwise cost can end up greater than selling.
                    decimal margin = x.MarginPercent ?? 20m;
                    if (margin <= 0m) margin = 20m;
                    decimal sellingPerUnit = Math.Round(productPerUnit * (1m + margin / 100m), 0);
                    decimal totalSelling   = Math.Round(sellingPerUnit * x.BatchQty, 0);

                    return new
                    {
                        id = x.Id,
                        designId = x.DesignId,
                        designName = ((x.DesignCode ?? "") + " - " + (x.DesignNo ?? "")).Trim(' ', '-'),

                        productId = x.ProductId,
                        productCode = x.ProductCode,
                        productName = string.IsNullOrWhiteSpace(x.ProductName) ? "-" : x.ProductName,

                        batchQty = x.BatchQty,
                        marginPercent = Math.Round(margin, 0),

                        sellingPricePerUnit = sellingPerUnit,
                        totalSellingPrice = totalSelling,

                        threadCost = Math.Round(threadCost, 0),
                        machineCost = Math.Round(machineCost, 0),
                        laborCost = Math.Round(laborCost, 0),
                        electricityCost = Math.Round(elecCost, 0),
                        otherCost = Math.Round(otherCost, 0),

                        totalCost = Math.Round(x.TotalCost ?? 0, 0),
                        costPerUnit = Math.Round(x.CostPerUnit ?? 0, 0),

                        threadPerUnit = Math.Round(threadPerUnit, 0),
                        stocksMaterialUnit = Math.Round(stocksMaterialUnit, 0),

                        materialCostPerUnit   = Math.Round(displayMaterial, 0),
                        productionCostPerUnit = Math.Round(displayProduction, 0),
                        serviceCostPerUnit    = Math.Round(displayService, 0),
                        productCostPerUnit    = productPerUnit,
                        totalProductCost      = totalProductCost,

                        active = x.Active ?? true,

                        createdDateText = x.CreatedDate.HasValue
                            ? x.CreatedDate.Value.ToString("dd-MMM-yyyy")
                            : ""
                    };
                }).ToList();

                // Phase R.0 - text search + range filters in-memory after enrichment
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim().ToLower();
                    data = data.Where(x =>
                        (x.designName ?? "").ToLower().Contains(s) ||
                        (x.productCode ?? "").ToLower().Contains(s) ||
                        (x.productName ?? "").ToLower().Contains(s)
                    ).ToList();
                }
                if (!string.IsNullOrWhiteSpace(marginRange))
                {
                    var mr = marginRange.Trim().ToUpper();
                    if (mr == "LOW")    data = data.Where(x => x.marginPercent < 10m).ToList();
                    else if (mr == "MED") data = data.Where(x => x.marginPercent >= 10m && x.marginPercent < 25m).ToList();
                    else if (mr == "HIGH") data = data.Where(x => x.marginPercent >= 25m).ToList();
                }

                // Phase R.0 - summary aggregates
                decimal sumMaterial = data.Sum(x => x.materialCostPerUnit * x.batchQty);
                decimal sumProduction = data.Sum(x => x.productionCostPerUnit * x.batchQty);
                decimal sumService = data.Sum(x => x.serviceCostPerUnit * x.batchQty);
                decimal sumProductCost = data.Sum(x => x.totalProductCost);
                decimal sumSelling = data.Sum(x => x.totalSellingPrice);
                decimal sumBatch = data.Sum(x => (decimal)x.batchQty);
                decimal avgMargin = data.Any() ? Math.Round(data.Average(x => x.marginPercent), 1) : 0m;
                decimal avgCostPerUnit = sumBatch > 0 ? Math.Round(sumProductCost / sumBatch, 0) : 0m;
                decimal avgSellingPerUnit = sumBatch > 0 ? Math.Round(sumSelling / sumBatch, 0) : 0m;

                var summary = new
                {
                    count = data.Count,
                    activeCount = data.Count(x => x.active),
                    inactiveCount = data.Count(x => !x.active),
                    totalBatchQty = (int)sumBatch,
                    totalMaterialCost = Math.Round(sumMaterial, 0),
                    totalProductionCost = Math.Round(sumProduction, 0),
                    totalServiceCost = Math.Round(sumService, 0),
                    totalProductCost = Math.Round(sumProductCost, 0),
                    totalSellingPrice = Math.Round(sumSelling, 0),
                    estimatedProfit = Math.Round(sumSelling - sumProductCost, 0),
                    avgMargin,
                    avgCostPerUnit,
                    avgSellingPerUnit
                };

                return JsonSuccess("OK", new { items = data, summary });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load costing records.", ex);
            }
        }

        // ---------------- Phase R.0 PROFILE ----------------
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var item = db.DesignCosting
                    .Include("Gen_Design")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (item == null) return JsonError("Costing not found.");

                int? productId = item.ProductId
                    ?? (item.Gen_Design != null ? item.Gen_Design.ProductId : null);

                var product = productId.HasValue
                    ? db.Gen_Products.FirstOrDefault(p => p.Id == productId.Value)
                    : null;

                int qty = item.BatchQty > 0 ? item.BatchQty : 1;
                decimal threadCost = item.ThreadCost ?? 0m;
                decimal machineCost = item.MachineCost ?? 0m;
                decimal laborCost = item.LaborCost ?? 0m;
                decimal elecCost = item.ElectricityCost ?? 0m;
                decimal otherCost = item.OtherCost ?? 0m;
                decimal material = item.MaterialCostPerUnit ?? 0m;
                decimal production = item.ProductionCostPerUnit ?? 0m;
                decimal service = item.ServiceCostPerUnit ?? 0m;
                decimal productPerUnit = material + production + service;
                decimal totalProductCost = productPerUnit * qty;
                decimal margin = item.MarginPercent ?? 20m;
                decimal sellingPerUnit = productPerUnit * (1m + margin / 100m);
                decimal totalSelling = sellingPerUnit * qty;

                // Components linked to this design (FK is D_Code on Gen_Components)
                var componentEntities = db.Gen_Components
                    .Where(c => c.D_Code == item.DesignId
                             && (c.Deleted == false || c.Deleted == null))
                    .OrderBy(c => c.Id)
                    .ToList();
                var components = componentEntities.Select(c => new
                {
                    id = c.Id,
                    code = c.Code == null ? "" : c.Code.ToString(),
                    name = c.Name,
                    active = c.Active ?? true
                }).ToList();

                // Materials linked to those components
                var componentIds = components.Select(c => c.id).ToList();
                var materialEntities = (
                    from cm in db.Gen_ComponentMaterials
                    join r in db.Gen_RawItems on cm.RawItemId equals r.Id into rj
                    from r in rj.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into uj
                    from u in uj.DefaultIfEmpty()
                    where componentIds.Contains(cm.CompId) && cm.Deleted == false
                    select new
                    {
                        cm.Id,
                        cm.CompId,
                        cm.RawItemId,
                        RawItemName = r != null ? r.ItemName : "-",
                        cm.QtyPerUnit,
                        UnitCode = u != null ? u.UnitCode : ""
                    }
                ).ToList();
                var materials = materialEntities.Select(m => new
                {
                    id = m.Id,
                    componentId = m.CompId,
                    rawItemId = m.RawItemId,
                    rawItemName = m.RawItemName,
                    qty = m.QtyPerUnit,
                    unit = m.UnitCode
                }).ToList();

                var header = new
                {
                    id = item.Id,
                    designId = item.DesignId,
                    designCode = item.Gen_Design != null ? item.Gen_Design.D_Code : "",
                    designNo = item.Gen_Design != null ? item.Gen_Design.DNo : "",
                    designName = item.Gen_Design != null ? item.Gen_Design.CollectionName : "",
                    productId,
                    productCode = product != null ? product.Prod_Code : "",
                    productName = product != null ? product.Prod_Name : "",
                    batchQty = item.BatchQty,
                    marginPercent = Math.Round(margin, 1),
                    active = item.Active ?? true,
                    createdDateText = item.CreatedDate.HasValue
                        ? item.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null,
                    updatedDateText = item.UpdatedDate.HasValue
                        ? item.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null
                };

                var kpis = new
                {
                    threadCost = Math.Round(threadCost, 0),
                    machineCost = Math.Round(machineCost, 0),
                    laborCost = Math.Round(laborCost, 0),
                    electricityCost = Math.Round(elecCost, 0),
                    otherCost = Math.Round(otherCost, 0),
                    materialCostPerUnit = Math.Round(material, 0),
                    productionCostPerUnit = Math.Round(production, 0),
                    serviceCostPerUnit = Math.Round(service, 0),
                    productCostPerUnit = Math.Round(productPerUnit, 0),
                    sellingPricePerUnit = Math.Round(sellingPerUnit, 0),
                    totalProductCost = Math.Round(totalProductCost, 0),
                    totalSellingPrice = Math.Round(totalSelling, 0),
                    estimatedProfit = Math.Round(totalSelling - totalProductCost, 0),
                    componentCount = components.Count(),
                    materialCount = materials.Count()
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    components,
                    materials
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var item = db.DesignCosting
                    .Include("Gen_Design")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (item == null)
                    return JsonError("Record not found.");

                int? productId = item.ProductId
                    ?? (item.Gen_Design != null ? item.Gen_Design.ProductId : null);

                string productCode = "";
                string productName = "";

                int branchId = GetCurrentBranchId();

                if (item.Gen_Design != null && item.Gen_Design.BranchId.HasValue)
                    branchId = item.Gen_Design.BranchId.Value;

                if (productId.HasValue && productId.Value > 0)
                {
                    var prod = db.Gen_Products.FirstOrDefault(p => p.Id == productId.Value);
                    if (prod != null)
                    {
                        productCode = prod.Prod_Code;
                        productName = prod.Prod_Name;
                    }
                }

                int batchQty = item.BatchQty > 0 ? item.BatchQty : 1;

                decimal threadCost = item.ThreadCost ?? 0m;
                decimal machineCost = item.MachineCost ?? 0m;
                decimal laborCost = item.LaborCost ?? 0m;
                decimal elecCost = item.ElectricityCost ?? 0m;
                decimal otherCost = item.OtherCost ?? 0m;

                decimal threadPerUnit = threadCost / batchQty;
                decimal stocksMaterialUnit = GetProductMaterialPerUnitCost(productId ?? 0, branchId);
                decimal materialPerUnit = threadPerUnit + stocksMaterialUnit;

                decimal productionPerUnit = (machineCost + laborCost + elecCost + otherCost) / batchQty;
                decimal servicePerUnitLive = GetProductServicePerUnitCost(productId ?? 0, branchId);

                decimal storedMaterial   = item.MaterialCostPerUnit ?? 0m;
                decimal storedProduction = item.ProductionCostPerUnit ?? 0m;
                decimal storedService    = item.ServiceCostPerUnit ?? 0m;

                decimal displayMaterial   = storedMaterial > 0m   ? storedMaterial   : materialPerUnit;
                decimal displayProduction = storedProduction > 0m ? storedProduction : productionPerUnit;
                decimal displayService    = servicePerUnitLive > 0m ? servicePerUnitLive : storedService;

                decimal productPerUnit = Math.Round(displayMaterial + displayProduction + displayService, 0);
                decimal totalProductCost = Math.Round(productPerUnit * item.BatchQty, 0);

                // Selling price derives from PRODUCT cost (not just design cost)
                decimal marginPercentDet = item.MarginPercent ?? 20m;
                if (marginPercentDet <= 0m) marginPercentDet = 20m;
                decimal sellingPricePerUnit = Math.Round(productPerUnit * (1m + marginPercentDet / 100m), 0);

                var data = new
                {
                    id = item.Id,
                    designId = item.DesignId,

                    designName = item.Gen_Design != null
                        ? ((item.Gen_Design.D_Code ?? "") + " - " + (item.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "",

                    productId = productId,
                    productCode = productCode,
                    productName = productName,

                    batchQty = item.BatchQty,
                    marginPercent = Math.Round(marginPercentDet, 0),

                    sellingPricePerUnit = sellingPricePerUnit,
                    totalSellingPrice = Math.Round(item.BatchQty * sellingPricePerUnit, 0),

                    threadCost = Math.Round(threadCost, 0),
                    machineCost = Math.Round(machineCost, 0),
                    laborCost = Math.Round(laborCost, 0),
                    electricityCost = Math.Round(elecCost, 0),
                    otherCost = Math.Round(otherCost, 0),

                    totalCost = Math.Round(item.TotalCost ?? 0, 0),
                    costPerUnit = Math.Round(item.CostPerUnit ?? 0, 0),

                    threadPerUnit = Math.Round(threadPerUnit, 0),
                    stocksMaterialUnit = Math.Round(stocksMaterialUnit, 0),

                    materialCostPerUnit   = Math.Round(displayMaterial, 0),
                    productionCostPerUnit = Math.Round(displayProduction, 0),
                    serviceCostPerUnit    = Math.Round(displayService, 0),
                    productCostPerUnit    = productPerUnit,
                    totalProductCost      = totalProductCost,

                    active = item.Active ?? true,

                    createdByEmployeeId = GetEmployeeId(item.CreatedBy),
                    updatedByEmployeeId = GetEmployeeId(item.UpdatedBy),

                    createdDateText = item.CreatedDate.HasValue
                        ? item.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : "",

                    updatedDateText = item.UpdatedDate.HasValue
                        ? item.UpdatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : ""
                };

                return JsonSuccess("OK", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load costing detail.", ex);
            }
        }

        [HttpPost, Route("save")]
        public ActionResult Save(DesignCosting model)
        {
            try
            {
                if (model == null)
                    return JsonError("Invalid request.");

                if (model.DesignId <= 0)
                    return JsonError("Design is required.");

                int batchQty = model.BatchQty;
                if (batchQty <= 0)
                    return JsonError("Batch quantity must be greater than zero.");

                var design = db.Gen_Design.FirstOrDefault(d =>
                    d.Id == model.DesignId &&
                    (d.Deleted == false || d.Deleted == null));

                if (design == null)
                    return JsonError("Selected design not found.");

                int linkedProductId = model.ProductId ?? design.ProductId ?? 0;

                if (linkedProductId <= 0)
                    return JsonError("Product is missing against selected design.");

                var product = db.Gen_Products.FirstOrDefault(p =>
                    p.Id == linkedProductId &&
                    (p.Deleted == false || p.Deleted == null));

                if (product == null)
                    return JsonError("Linked product not found.");

                int branchIdForCalc = design.BranchId ?? GetCurrentBranchId();

                int userId = GetCurrentUserId();
                DateTime now = DateTime.Now;

                decimal threadCost = Math.Round(model.ThreadCost ?? 0m, 0);
                decimal machineCost = Math.Round(model.MachineCost ?? 0m, 0);
                decimal laborCost = Math.Round(model.LaborCost ?? 0m, 0);
                decimal electricityCost = Math.Round(model.ElectricityCost ?? 0m, 0);
                decimal otherCost = Math.Round(model.OtherCost ?? 0m, 0);

                decimal totalCost = Math.Round(
                    threadCost + machineCost + laborCost + electricityCost + otherCost, 0);

                decimal costPerUnit = Math.Round(totalCost / batchQty, 0);

                decimal marginPercent = Math.Round(model.MarginPercent ?? 20m, 0);
                if (marginPercent <= 0)
                    marginPercent = 20m;

                // Compute the per-unit aggregates FIRST so selling price can be based on
                // total product cost (material + production + service), not just design cost.
                decimal threadPerUnit = batchQty > 0 ? threadCost / batchQty : 0m;
                decimal stocksMaterialUnit = GetProductMaterialPerUnitCost(linkedProductId, branchIdForCalc);
                if (stocksMaterialUnit <= 0m)
                    stocksMaterialUnit = GetDesignBomMaterialPerUnitCost(model.DesignId, branchIdForCalc);
                decimal materialPerUnit = Math.Round(threadPerUnit + stocksMaterialUnit, 4);

                decimal productionPerUnit = batchQty > 0
                    ? Math.Round((machineCost + laborCost + electricityCost + otherCost) / batchQty, 4)
                    : 0m;

                decimal servicePerUnit = Math.Round(GetProductServicePerUnitCost(linkedProductId, branchIdForCalc), 4);

                decimal productPerUnit = Math.Round(
                    materialPerUnit + productionPerUnit + servicePerUnit, 4);

                decimal totalProductCost = Math.Round(productPerUnit * batchQty, 4);

                // Selling price = product-cost-based, so it can never come out lower than cost.
                decimal sellingPricePerUnit = Math.Round(
                    productPerUnit * (1m + (marginPercent / 100m)), 0);

                decimal totalSellingPrice = Math.Round(sellingPricePerUnit * batchQty, 0);

                if (model.Id == 0)
                {
                    var sameDesign = db.DesignCosting
                        .Where(d => d.DesignId == model.DesignId && d.Deleted != true)
                        .Select(d => new { d.Id })
                        .FirstOrDefault();

                    if (sameDesign != null)
                    {
                        return Json(new
                        {
                            success = false,
                            message = "A costing already exists for this design. Open that record in Edit instead.",
                            conflictId = sameDesign.Id
                        }, JsonRequestBehavior.AllowGet);
                    }

                    model.ProductId = linkedProductId;
                    model.BatchQty = batchQty;

                    model.ThreadCost = threadCost;
                    model.MachineCost = machineCost;
                    model.LaborCost = laborCost;
                    model.ElectricityCost = electricityCost;
                    model.OtherCost = otherCost;

                    model.TotalCost = totalCost;
                    model.CostPerUnit = costPerUnit;
                    model.MarginPercent = marginPercent;
                    model.SellingPricePerUnit = sellingPricePerUnit;

                    model.MaterialCostPerUnit = materialPerUnit;
                    model.ProductionCostPerUnit = productionPerUnit;
                    model.ServiceCostPerUnit = servicePerUnit;
                    model.ProductCostPerUnit = productPerUnit;
                    model.TotalProductCost = totalProductCost;

                    model.CreatedBy = userId;
                    model.CreatedDate = now;
                    model.RecordTimeStamp = now;
                    model.Active = true;
                    model.Deleted = false;

                    db.DesignCosting.Add(model);
                }
                else
                {
                    var existing = db.DesignCosting.FirstOrDefault(x =>
                        x.Id == model.Id &&
                        x.Deleted != true);

                    if (existing == null)
                        return JsonError("Record not found.");

                    if (existing.DesignId != model.DesignId)
                    {
                        var conflict = db.DesignCosting
                            .Where(d =>
                                d.DesignId == model.DesignId &&
                                d.Id != existing.Id &&
                                d.Deleted != true)
                            .Select(d => new { d.Id })
                            .FirstOrDefault();

                        if (conflict != null)
                        {
                            return Json(new
                            {
                                success = false,
                                message = "That design already has an active costing. Edit that record directly."
                            }, JsonRequestBehavior.AllowGet);
                        }
                    }

                    existing.DesignId = model.DesignId;
                    existing.ProductId = linkedProductId;
                    existing.BatchQty = batchQty;

                    existing.ThreadCost = threadCost;
                    existing.MachineCost = machineCost;
                    existing.LaborCost = laborCost;
                    existing.ElectricityCost = electricityCost;
                    existing.OtherCost = otherCost;

                    existing.TotalCost = totalCost;
                    existing.CostPerUnit = costPerUnit;
                    existing.MarginPercent = marginPercent;
                    existing.SellingPricePerUnit = sellingPricePerUnit;

                    existing.MaterialCostPerUnit = materialPerUnit;
                    existing.ProductionCostPerUnit = productionPerUnit;
                    existing.ServiceCostPerUnit = servicePerUnit;
                    existing.ProductCostPerUnit = productPerUnit;
                    existing.TotalProductCost = totalProductCost;

                    existing.Active = model.Active ?? true;
                    existing.Deleted = false;
                    existing.UpdatedBy = userId;
                    existing.UpdatedDate = now;
                    existing.RecordTimeStamp = now;
                }

                db.SaveChanges();

                int refreshedItemCount = RefreshUnpostedWorkOrderItemsForDesign(
                    model.DesignId, userId, now);

                return JsonSuccess("Saved successfully.", new
                {
                    refreshedWorkOrderItems = refreshedItemCount,
                    productId = linkedProductId,
                    productCode = product.Prod_Code,
                    productName = product.Prod_Name,

                    batchQty = batchQty,

                    threadCost = threadCost,
                    machineCost = machineCost,
                    laborCost = laborCost,
                    electricityCost = electricityCost,
                    otherCost = otherCost,

                    totalCost = Math.Round(totalCost, 0),
                    costPerUnit = Math.Round(costPerUnit, 0),

                    marginPercent = Math.Round(marginPercent, 0),
                    sellingPricePerUnit = Math.Round(sellingPricePerUnit, 0),
                    totalSellingPrice = Math.Round(totalSellingPrice, 0),

                    threadPerUnit = Math.Round(threadPerUnit, 0),
                    stocksMaterialUnit = Math.Round(stocksMaterialUnit, 0),
                    materialCostPerUnit = Math.Round(materialPerUnit, 0),
                    productionCostPerUnit = Math.Round(productionPerUnit, 0),
                    serviceCostPerUnit = Math.Round(servicePerUnit, 0),
                    productCostPerUnit = Math.Round(productPerUnit, 0),
                    totalProductCost = Math.Round(totalProductCost, 0)
                });
            }
            catch (System.Data.Entity.Validation.DbEntityValidationException vex)
            {
                var msgs = vex.EntityValidationErrors
                    .SelectMany(e => e.ValidationErrors)
                    .Select(e => e.PropertyName + ": " + e.ErrorMessage)
                    .ToList();

                string joined = string.Join(" | ", msgs);

                return Json(new
                {
                    success = false,
                    message = "Validation failed: " + joined,
                    detail = joined
                }, JsonRequestBehavior.AllowGet);
            }
            catch (System.Data.Entity.Infrastructure.DbUpdateException dex)
            {
                var deepest = dex as Exception;
                while (deepest.InnerException != null)
                    deepest = deepest.InnerException;

                return Json(new
                {
                    success = false,
                    message = "Database update failed: " + deepest.Message,
                    detail = deepest.Message
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                var deepest = ex;
                while (deepest.InnerException != null)
                    deepest = deepest.InnerException;

                return Json(new
                {
                    success = false,
                    message = "Failed to save costing: " + deepest.Message,
                    detail = deepest.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // Propagates the latest DesignCosting numbers into every non-posted, non-deleted
        // WorkOrderItem pointing at this design. Posted WOs are historical and never touched.
        private int RefreshUnpostedWorkOrderItemsForDesign(int designId, int userId, DateTime now)
        {
            if (designId <= 0) return 0;

            var costing = db.DesignCosting
                .FirstOrDefault(c => c.DesignId == designId && c.Deleted != true);
            if (costing == null) return 0;

            decimal cost    = costing.ProductCostPerUnit    ?? 0m;
            decimal selling = costing.SellingPricePerUnit   ?? cost;
            decimal matlPU  = costing.MaterialCostPerUnit   ?? 0m;
            decimal prodPU  = costing.ProductionCostPerUnit ?? 0m;
            decimal svcPU   = costing.ServiceCostPerUnit    ?? 0m;

            var items = (from woi in db.WorkOrderItems
                         join wo in db.WorkOrders on woi.WorkOrderId equals wo.Id
                         where woi.DesignId == designId
                               && woi.Deleted != true
                               && wo.Deleted != true
                               && wo.IsPosted != true
                         select woi).ToList();

            foreach (var woi in items)
            {
                int qty = woi.Quantity;
                woi.CostPerUnit     = cost;
                woi.UnitPrice       = selling;
                woi.TotalCost       = cost   * qty;
                woi.ThreadCost      = matlPU * qty;
                woi.LaborCost       = prodPU * qty;
                woi.OtherCost       = svcPU  * qty;
                woi.MachineCost     = 0m;
                woi.ElectricityCost = 0m;
                woi.UpdatedBy       = userId;
                woi.UpdatedDate     = now;
                woi.RecordTimeStamp = now;
            }

            if (items.Count > 0)
                db.SaveChanges();

            return items.Count;
        }

        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var row = db.DesignCosting.Find(id);
                if (row == null)
                    return JsonError("Record not found.");

                row.Deleted = true;
                row.Active = false;
                row.UpdatedBy = GetCurrentUserId();
                row.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete record.", ex);
            }
        }
        
        [HttpPost]
        [Route("recalculate")]
        public ActionResult Recalculate(RecalculateCostingVm vm)
        {
            try
            {
                if (vm == null)
                    return JsonError("Invalid request.");

                if (vm.DesignId <= 0)
                    return JsonError("Design is required.");

                if (vm.BatchQty <= 0)
                    return JsonError("Batch quantity must be greater than zero.");

                var service = new DesignCostingService(db);

                var result = service.CalculateDesignCost(
                    vm.DesignId,
                    vm.BatchQty,
                    vm.MarginPercent ?? 20
                );

                // Resolve product + branch the same way Save does. Prefer the
                // dropdown's product (vm.ProductId) over the design's stored
                // ProductId — the user may legitimately be costing a design
                // against a different product variant than the one it was
                // originally linked to.
                var design = db.Gen_Design.FirstOrDefault(d =>
                    d.Id == vm.DesignId &&
                    (d.Deleted == false || d.Deleted == null));

                int linkedProductId = (vm.ProductId.HasValue && vm.ProductId.Value > 0)
                    ? vm.ProductId.Value
                    : (design != null ? (design.ProductId ?? 0) : 0);

                int branchIdForCalc = design != null && design.BranchId.HasValue
                    ? design.BranchId.Value
                    : GetCurrentBranchId();

                int batchQty = vm.BatchQty > 0 ? vm.BatchQty : 1;

                decimal threadPerUnit = result.ThreadCost / batchQty;

                // Material per unit (non-thread): try the finished-stock cost
                // first (works after QC), then fall back to a BOM-walk valuation
                // (works as soon as the BOM is set up, before any production).
                decimal stocksMaterialUnit = linkedProductId > 0
                    ? GetProductMaterialPerUnitCost(linkedProductId, branchIdForCalc)
                    : 0m;
                if (stocksMaterialUnit <= 0m)
                    stocksMaterialUnit = GetDesignBomMaterialPerUnitCost(vm.DesignId, branchIdForCalc);

                decimal materialPerUnit = Math.Round(threadPerUnit + stocksMaterialUnit, 2);

                decimal productionPerUnit = Math.Round(
                    (result.MachineCost + result.LaborCost + result.ElectricityCost + result.OtherCost) / batchQty,
                    2);

                decimal servicePerUnit = linkedProductId > 0
                    ? Math.Round(GetProductServicePerUnitCost(linkedProductId, branchIdForCalc), 2)
                    : 0m;

                decimal productPerUnit = Math.Round(
                    materialPerUnit + productionPerUnit + servicePerUnit, 0);

                decimal totalProductCost = Math.Round(productPerUnit * batchQty, 0);

                var data = new
                {
                    // INPUT
                    designId = vm.DesignId,
                    batchQty = vm.BatchQty,

                    // COST BREAKDOWN
                    threadCost = Math.Round(result.ThreadCost, 2),
                    machineCost = Math.Round(result.MachineCost, 2),
                    laborCost = Math.Round(result.LaborCost, 2),
                    electricityCost = Math.Round(result.ElectricityCost, 2),
                    otherCost = Math.Round(result.OtherCost, 2),

                    // ROUND FIGURE MAIN VALUES
                    totalCost = Math.Round(result.TotalCost, 0),
                    costPerUnit = Math.Round(result.CostPerUnit, 0),

                    // PRICE
                    marginPercent = Math.Round(result.MarginPercent, 0),
                    sellingPricePerUnit = Math.Round(result.SellingPricePerUnit, 0),
                    totalSellingPrice = Math.Round(result.TotalSellingPrice, 0),

                    // optional backward compatibility
                    totalPrice = Math.Round(result.TotalSellingPrice, 0),

                    // PER-UNIT PRODUCT COST BREAKDOWN (the 5 fields the form needs)
                    materialCostPerUnit   = materialPerUnit,
                    productionCostPerUnit = productionPerUnit,
                    serviceCostPerUnit    = servicePerUnit,
                    productCostPerUnit    = productPerUnit,
                    totalProductCost      = totalProductCost,

                    // THREAD METRICS
                    threadMetersPerPiece = Math.Round(result.ThreadMetersPerPiece, 2),
                    totalThreadMeters = Math.Round(result.TotalThreadMeters, 2),
                    costPer1000Stitches = Math.Round(result.CostPer1000Stitches, 2),

                    // PRODUCTION METRICS
                    productionMinutes = Math.Round(result.ProductionMinutes, 2),
                    productionHours = Math.Round(result.ProductionHours, 2),
                    piecesPerHour = Math.Round(result.PiecesPerHour, 2),

                    // CAPACITY
                    piecesPerShift = Math.Round(result.PiecesPerShift, 2),
                    dailyCapacity = Math.Round(result.DailyCapacity, 2),
                    productionDays = Math.Round(result.ProductionDays, 2)
                };

                return JsonSuccess("Calculated successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to recalculate costing.", ex);
            }
        }
        
        [HttpGet]
        [Route("calculate")]
        public ActionResult Calculate(int designId, int batchQty, decimal margin = 20)
        {
            try
            {
                if (designId <= 0)
                    return JsonError("Design is required.");

                if (batchQty <= 0)
                    return JsonError("Batch quantity must be greater than zero.");

                if (margin <= 0)
                    margin = 20;

                var svc = new DesignCostingService(db);
                var result = svc.CalculateDesignCost(designId, batchQty, margin);

                var design = db.Gen_Design.FirstOrDefault(d =>
                    d.Id == designId &&
                    (d.Deleted == false || d.Deleted == null));

                int linkedProductId = design != null ? (design.ProductId ?? 0) : 0;
                int branchIdForCalc = design != null && design.BranchId.HasValue
                    ? design.BranchId.Value
                    : GetCurrentBranchId();

                int qty = batchQty > 0 ? batchQty : 1;

                decimal threadPerUnit = result.ThreadCost / qty;
                decimal stocksMaterialUnit = linkedProductId > 0
                    ? GetProductMaterialPerUnitCost(linkedProductId, branchIdForCalc)
                    : 0m;
                if (stocksMaterialUnit <= 0m)
                    stocksMaterialUnit = GetDesignBomMaterialPerUnitCost(designId, branchIdForCalc);
                decimal materialPerUnit = Math.Round(threadPerUnit + stocksMaterialUnit, 2);

                decimal productionPerUnit = Math.Round(
                    (result.MachineCost + result.LaborCost + result.ElectricityCost + result.OtherCost) / qty,
                    2);

                decimal servicePerUnit = linkedProductId > 0
                    ? Math.Round(GetProductServicePerUnitCost(linkedProductId, branchIdForCalc), 2)
                    : 0m;

                decimal productPerUnit = Math.Round(
                    materialPerUnit + productionPerUnit + servicePerUnit, 0);

                decimal totalProductCost = Math.Round(productPerUnit * qty, 0);

                var data = new
                {
                    // input
                    designId = designId,
                    batchQty = batchQty,

                    // costing
                    threadCost = Math.Round(result.ThreadCost, 2),
                    machineCost = Math.Round(result.MachineCost, 2),
                    laborCost = Math.Round(result.LaborCost, 2),
                    electricityCost = Math.Round(result.ElectricityCost, 2),
                    otherCost = Math.Round(result.OtherCost, 2),

                    totalCost = Math.Round(result.TotalCost, 2),
                    costPerUnit = Math.Round(result.CostPerUnit, 2),

                    // selling
                    marginPercent = Math.Round(result.MarginPercent, 2),
                    sellingPricePerUnit = Math.Round(result.SellingPricePerUnit, 2),
                    totalSellingPrice = Math.Round(result.TotalSellingPrice, 2),

                    // backward compatibility
                    totalPrice = Math.Round(result.TotalSellingPrice, 2),

                    // per-unit product cost breakdown
                    materialCostPerUnit   = materialPerUnit,
                    productionCostPerUnit = productionPerUnit,
                    serviceCostPerUnit    = servicePerUnit,
                    productCostPerUnit    = productPerUnit,
                    totalProductCost      = totalProductCost,

                    // thread metrics
                    threadMetersPerPiece = Math.Round(result.ThreadMetersPerPiece, 2),
                    totalThreadMeters = Math.Round(result.TotalThreadMeters, 2),
                    costPer1000Stitches = Math.Round(result.CostPer1000Stitches, 2),

                    // production metrics
                    productionMinutes = Math.Round(result.ProductionMinutes, 2),
                    productionHours = Math.Round(result.ProductionHours, 2),
                    piecesPerHour = Math.Round(result.PiecesPerHour, 2),

                    // capacity
                    piecesPerShift = Math.Round(result.PiecesPerShift, 2),
                    dailyCapacity = Math.Round(result.DailyCapacity, 2),
                    productionDays = Math.Round(result.ProductionDays, 2)
                };

                return JsonSuccess("Preview calculated successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to calculate costing preview.", ex);
            }
        }

        [HttpGet, Route("export-excel")]
        public ActionResult ExportExcel(string activeFilter = "")
        {
            var data = db.DesignCosting
                .Where(x => x.Deleted != true)
                .ToList();

            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("DesignCosting");

                ws.Cells[1, 1].Value = "Design";
                ws.Cells[1, 2].Value = "Total Cost";

                int row = 2;

                foreach (var item in data)
                {
                    ws.Cells[row, 1].Value =
                        item.Gen_Design != null
                        ? item.Gen_Design.D_Code + " - " + item.Gen_Design.DNo
                        : "";

                    ws.Cells[row, 2].Value = item.TotalCost;
                    row++;
                }

                return File(
                    package.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "DesignCosting.xlsx"
                );
            }
        }

        // ---------------- LIST PDF ----------------
        // Generates a PDF of every (non-deleted) DesignCosting row using
        // iTextSharp. The Angular page calls this from the Print/PDF button
        // at /emb-products/design-costing/export-pdf-list.
        [HttpGet]
        [Route("export-pdf-list")]
        public ActionResult ExportPdfList(string activeFilter = "", string search = "")
        {
            var query = db.DesignCosting
                .Include("Gen_Design")
                .Where(x => x.Deleted != true);

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false);

            var rawList = query.OrderByDescending(x => x.Id).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                rawList = rawList.Where(x =>
                    ((x.Gen_Design != null
                        ? ((x.Gen_Design.D_Code ?? "") + " - " + (x.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "") ?? "").ToLower().Contains(s)
                ).ToList();
            }

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 20f, 20f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont   = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.BLACK);
                var headerFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  BaseColor.WHITE);
                var bodyFont    = FontFactory.GetFont(FontFactory.HELVETICA, 8.5f,    BaseColor.BLACK);

                doc.Add(new Paragraph("Design Costing — List", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(" "));

                var table = new PdfPTable(7) { WidthPercentage = 100f };
                table.SetWidths(new float[] { 1.4f, 1.2f, 0.8f, 1.1f, 1.1f, 1.3f, 0.8f });

                void AddHeader(string text)
                {
                    var c = new PdfPCell(new Phrase(text, headerFont))
                    {
                        BackgroundColor = new BaseColor(13, 110, 253),
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5f
                    };
                    table.AddCell(c);
                }

                AddHeader("Design");
                AddHeader("Product");
                AddHeader("Batch Qty");
                AddHeader("Cost / Unit");
                AddHeader("Selling / Unit");
                AddHeader("Total Selling");
                AddHeader("Active");

                foreach (var item in rawList)
                {
                    string design = item.Gen_Design != null
                        ? ((item.Gen_Design.D_Code ?? "") + " - " + (item.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "-";

                    int? productId = item.ProductId
                        ?? (item.Gen_Design != null ? item.Gen_Design.ProductId : null);
                    var product = productId.HasValue
                        ? db.Gen_Products.FirstOrDefault(p => p.Id == productId.Value)
                        : null;
                    string productLabel = product != null
                        ? (product.Prod_Code + " - " + product.Prod_Name)
                        : "-";

                    decimal costPerUnit    = Math.Round(item.ProductCostPerUnit    ?? item.CostPerUnit ?? 0m, 0);
                    decimal sellingPerUnit = Math.Round(item.SellingPricePerUnit   ?? 0m, 0);
                    decimal totalSelling   = Math.Round(sellingPerUnit * item.BatchQty, 0);

                    table.AddCell(new PdfPCell(new Phrase(design, bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(productLabel, bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(item.BatchQty.ToString("N0"), bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase(costPerUnit.ToString("N0"), bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase(sellingPerUnit.ToString("N0"), bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase(totalSelling.ToString("N0"), bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_RIGHT });
                    table.AddCell(new PdfPCell(new Phrase((item.Active ?? true) ? "Yes" : "No", bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                }

                doc.Add(table);

                var footer = new Paragraph(
                    $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | Rows: {rawList.Count}",
                    FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY));
                footer.Alignment = Element.ALIGN_RIGHT;
                footer.SpacingBefore = 10f;
                doc.Add(footer);

                doc.Close();
                return File(ms.ToArray(), "application/pdf", "DesignCosting_List.pdf");
            }
        }

        // ---------------- PDF ----------------
        [HttpGet]
        [Route("pdf/{id:int}")]
        [Route("export-pdf/{id:int}")]
        public ActionResult Pdf(int id)
        {
            var row = db.DesignCosting
                .Include("Gen_Design")
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (row == null)
                return HttpNotFound();

            var vm = new DesignCostingDetailViewModel
            {
                Id = row.Id,
                DesignId = row.DesignId,

                DesignName = row.Gen_Design != null
                    ? (row.Gen_Design.D_Code + " - " + (row.Gen_Design.DNo ?? ""))
                    : "",

                ThreadCost = row.ThreadCost,
                MachineCost = row.MachineCost,
                LaborCost = row.LaborCost,
                ElectricityCost = row.ElectricityCost,
                OtherCost = row.OtherCost,

                TotalCost = row.TotalCost,
                CostPerUnit = row.CostPerUnit,

                CreatedDateText = row.CreatedDate?.ToString("dd-MMM-yyyy hh:mm tt"),
                UpdatedDateText = row.UpdatedDate?.ToString("dd-MMM-yyyy hh:mm tt")
            };

            return new ViewAsPdf("Pdf", vm)
            {
                FileName = $"DesignCosting_{row.Id}.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait
            };
        }

        // ---------------- PRINT ----------------
        [HttpGet, Route("print/{id:int}")]
        public ActionResult Print(int id)
        {
            var row = db.DesignCosting
                .Include("Gen_Design")
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (row == null)
                return HttpNotFound();

            var vm = new DesignCostingDetailViewModel
            {
                Id = row.Id,
                DesignId = row.DesignId,

                DesignName = row.Gen_Design != null
                    ? (row.Gen_Design.D_Code + " - " + (row.Gen_Design.DNo ?? ""))
                    : "",

                ThreadCost = row.ThreadCost,
                MachineCost = row.MachineCost,
                LaborCost = row.LaborCost,
                ElectricityCost = row.ElectricityCost,
                OtherCost = row.OtherCost,

                TotalCost = row.TotalCost,
                CostPerUnit = row.CostPerUnit,

                CreatedDateText = row.CreatedDate?.ToString("dd-MMM-yyyy hh:mm tt"),
                UpdatedDateText = row.UpdatedDate?.ToString("dd-MMM-yyyy hh:mm tt")
            };

            return View("Print", vm);
        }

    }
}