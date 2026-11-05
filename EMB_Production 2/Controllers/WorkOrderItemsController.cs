using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using ClosedXML.Excel;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/work-order-items")]
    public class WorkOrderItemsController : BaseController
    {
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            string showDeleted = "",
            int page = 1,
            int pageSize = 10,
            int? workOrderId = null,
            int? branchId = null,
            string fromDate = null,
            string toDate = null,
            string statusFilter = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 10;
                if (pageSize > 200) pageSize = 200;

                // Phase L.0b - parse date range filters defensively
                DateTime? fromDt = null, toDt = null;
                if (!string.IsNullOrWhiteSpace(fromDate) && DateTime.TryParse(fromDate, out var fParsed)) fromDt = fParsed.Date;
                if (!string.IsNullOrWhiteSpace(toDate) && DateTime.TryParse(toDate, out var tParsed)) toDt = tParsed.Date.AddDays(1).AddTicks(-1);
                string statusUp = (statusFilter ?? "").Trim().ToUpper();

                var query = db.WorkOrderItems
                    .Include(x => x.WorkOrders)
                    .Include(x => x.Gen_JobOrders)
                    .Include(x => x.Gen_Design)
                    .Include(x => x.Gen_Products)
                    .Include(x => x.Gen_Components)
                    .Include(x => x.Branches)
                    .Include(x => x.Gen_Machines)
                    .AsQueryable();

                showDeleted = (showDeleted ?? "").Trim().ToLower();

                if (string.IsNullOrWhiteSpace(showDeleted) || showDeleted == "false" || showDeleted == "no")
                    query = query.Where(x => x.Deleted == false || x.Deleted == null);
                else if (showDeleted == "true" || showDeleted == "only")
                    query = query.Where(x => x.Deleted == true);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    int numericSearch;
                    bool isNumericSearch = int.TryParse(search, out numericSearch);

                    query = query.Where(x =>
                        (isNumericSearch && (
                            x.Id == numericSearch ||
                            x.WorkOrderId == numericSearch ||
                            x.JobOrderId == numericSearch ||
                            x.DesignId == numericSearch ||
                            x.CompId == numericSearch ||
                            x.ProductId == numericSearch ||
                            x.BranchId == numericSearch ||
                            x.MachineId == numericSearch
                        )) ||
                        (x.WorkOrders != null && (x.WorkOrders.WorkOrderCode ?? "").Contains(search)) ||
                        (x.Gen_JobOrders != null && (x.Gen_JobOrders.JobOrderCode ?? "").Contains(search)) ||
                        (x.Gen_Components != null && (x.Gen_Components.Name ?? "").Contains(search)) ||
                        (x.Gen_Design != null &&
                            (
                                (x.Gen_Design.DNo ?? "").Contains(search) ||
                                (x.Gen_Design.D_Code ?? "").Contains(search) ||
                                (x.Gen_Design.CollectionName ?? "").Contains(search)
                            )) ||
                        (x.Gen_Products != null && (x.Gen_Products.Prod_Name ?? "").Contains(search)) ||
                        (x.Branches != null && (x.Branches.Name ?? "").Contains(search)) ||
                        (x.Gen_Machines != null &&
                            (
                                (x.Gen_Machines.Serial_Number ?? "").Contains(search) ||
                                (x.Gen_Machines.Calling_Number ?? "").Contains(search) ||
                                (x.Gen_Machines.Code ?? "").Contains(search) ||
                                (x.Gen_Machines.HeadtoHead ?? "").Contains(search) ||
                                (x.Gen_Machines.FrameHeight ?? "").Contains(search)
                            )) ||
                        (x.Remarks ?? "").Contains(search) ||
                        (x.Status ?? "").Contains(search)
                    );
                }

                // Phase L.0b - additional filters
                if (workOrderId.HasValue && workOrderId.Value > 0)
                    query = query.Where(x => x.WorkOrderId == workOrderId.Value);
                if (branchId.HasValue && branchId.Value > 0)
                    query = query.Where(x => x.BranchId == branchId.Value);
                if (fromDt.HasValue)
                    query = query.Where(x => x.CreatedDate >= fromDt.Value);
                if (toDt.HasValue)
                    query = query.Where(x => x.CreatedDate <= toDt.Value);
                if (!string.IsNullOrEmpty(statusUp))
                    query = query.Where(x => x.Status != null && x.Status.ToUpper() == statusUp);

                var totalItems = query.Count();

                var rows = query
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // ✅ IMPORTANT: fallback JobOrderId from parent WorkOrder
                var jobOrderIds = rows
                    .Select(x => x.JobOrderId ?? (x.WorkOrders != null ? x.WorkOrders.JobOrderId : null))
                    .Where(x => x.HasValue)
                    .Select(x => x.Value)
                    .Distinct()
                    .ToList();

                var jobOrderMap = db.Gen_JobOrders
                    .Where(x => jobOrderIds.Contains(x.Id))
                    .ToDictionary(x => x.Id, x => x.JobOrderCode);

                var items = rows.Select(x =>
                {
                    int? resolvedJobOrderId =
                        x.JobOrderId ?? (x.WorkOrders != null ? x.WorkOrders.JobOrderId : null);

                    string resolvedJobOrderCode =
                        resolvedJobOrderId.HasValue && jobOrderMap.ContainsKey(resolvedJobOrderId.Value)
                            ? jobOrderMap[resolvedJobOrderId.Value]
                            : null;

                    return new WorkOrderItemRowViewModel
                    {
                        Id = x.Id,

                        WorkOrderId = x.WorkOrderId,
                        WorkOrderCode = x.WorkOrders != null ? x.WorkOrders.WorkOrderCode : null,

                        JobOrderId = resolvedJobOrderId,
                        JobOrderCode = resolvedJobOrderCode,

                        DesignId = x.DesignId,
                        DesignCode = x.Gen_Design != null ? x.Gen_Design.D_Code : null,
                        DesignName = x.Gen_Design != null ? x.Gen_Design.CollectionName : null,

                        CompId = x.CompId,
                        ComponentCode = x.Gen_Components != null
                            ? (x.Gen_Components.Code == null ? null : x.Gen_Components.Code.ToString())
                            : null,
                        ComponentName = x.Gen_Components != null ? x.Gen_Components.Name : null,

                        ProductId = x.ProductId,
                        ProductName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : null,

                        Quantity = x.Quantity,

                        UnitPrice = x.UnitPrice,
                        TotalPrice = CalculateTotalPrice(x.Quantity, x.UnitPrice),

                        BranchId = x.BranchId,
                        BranchName = x.Branches != null ? x.Branches.Name : null,

                        MachineId = x.MachineId,
                        MachineName = x.Gen_Machines != null
                            ? (!string.IsNullOrWhiteSpace(x.Gen_Machines.Serial_Number)
                                ? x.Gen_Machines.Serial_Number
                                : (!string.IsNullOrWhiteSpace(x.Gen_Machines.Calling_Number)
                                    ? x.Gen_Machines.Calling_Number
                                    : x.Gen_Machines.Code))
                            : null,

                        Remarks = x.Remarks,
                        Status = x.Status,

                        Active = x.Active,
                        Deleted = x.Deleted,

                        CompletedQty = x.CompletedQty,
                        RejectedQty = x.RejectedQty,

                        ThreadCost = x.ThreadCost,
                        MachineCost = x.MachineCost,
                        LaborCost = x.LaborCost,
                        ElectricityCost = x.ElectricityCost,
                        OtherCost = x.OtherCost,
                        TotalCost = x.TotalCost,
                        CostPerUnit = x.CostPerUnit,

                        CreatedDate = x.CreatedDate,
                        UpdatedDate = x.UpdatedDate,
                        CreatedDateText = x.CreatedDate.HasValue
                            ? x.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                            : null,
                        UpdatedDateText = x.UpdatedDate.HasValue
                            ? x.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                            : null
                    };
                }).ToList();

                var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
                if (totalPages <= 0) totalPages = 1;

                // Phase L.0b - summary aggregates across the entire FILTERED set (not paginated)
                var summaryRows = query.Select(x => new
                {
                    Quantity = x.Quantity,
                    CompletedQty = x.CompletedQty,
                    RejectedQty = x.RejectedQty,
                    UnitPrice = x.UnitPrice,
                    TotalCost = x.TotalCost,
                    Status = x.Status
                }).ToList();

                var summary = new
                {
                    count        = summaryRows.Count,
                    totalQty     = summaryRows.Sum(s => s.Quantity),
                    totalCompletedQty = summaryRows.Sum(s => s.CompletedQty),
                    totalRejectedQty  = summaryRows.Sum(s => s.RejectedQty),
                    totalAmount  = summaryRows.Sum(s => s.Quantity * (s.UnitPrice ?? 0m)),
                    totalCost    = summaryRows.Sum(s => s.TotalCost ?? 0m),
                    byStatus     = summaryRows
                        .GroupBy(s => string.IsNullOrWhiteSpace(s.Status) ? "Unknown" : s.Status.Trim())
                        .Select(g => new { status = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .ToList()
                };

                return Json(new
                {
                    success = true,
                    message = "Loaded successfully.",
                    data = new
                    {
                        items,
                        summary,
                        totalItems,
                        page,
                        pageSize,
                        totalPages
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load records.", ex);
            }
        }

        // ========================= DETAIL =========================
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var raw = (
                    from x in db.WorkOrderItems

                    join wo in db.WorkOrders on x.WorkOrderId equals wo.Id into wo1
                    from wo in wo1.DefaultIfEmpty()

                    join jo in db.Gen_JobOrders on x.JobOrderId equals jo.Id into jo1
                    from jo in jo1.DefaultIfEmpty()

                    join d in db.Gen_Design on x.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on x.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    join p in db.Gen_Products on x.ProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()

                    join b in db.Branches on x.BranchId equals b.Id into b1
                    from b in b1.DefaultIfEmpty()

                    join m in db.Gen_Machines on x.MachineId equals m.Id into m1
                    from m in m1.DefaultIfEmpty()

                    join cu in db.Adm_Users on x.CreatedBy equals cu.Id into cu1
                    from cu in cu1.DefaultIfEmpty()

                    join uu in db.Adm_Users on x.UpdatedBy equals uu.Id into uu1
                    from uu in uu1.DefaultIfEmpty()

                    where x.Id == id && (x.Deleted == false || x.Deleted == null)

                    select new
                    {
                        Id = x.Id,

                        WorkOrderId = x.WorkOrderId,
                        WorkOrderCode = wo != null ? wo.WorkOrderCode : null,
                        WorkOrderLabel = wo != null && wo.WorkOrderCode != null && wo.WorkOrderCode != ""
                            ? wo.WorkOrderCode
                            : ("WO-" + x.WorkOrderId),

                        JobOrderId = x.JobOrderId,
                        JobOrderCode = jo != null ? jo.JobOrderCode : null,

                        DesignId = x.DesignId,
                        DesignCode = d != null ? d.D_Code : null,
                        DesignName = d != null ? d.CollectionName : null,

                        CompId = x.CompId,
                        ComponentCodeNumeric = c != null ? c.Code : null,
                        ComponentName = c != null ? c.Name : null,

                        ProductId = x.ProductId,
                        ProductName = p != null ? p.Prod_Name : null,

                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        TotalPrice = x.UnitPrice.HasValue ? (decimal?)(x.Quantity * x.UnitPrice.Value) : null,

                        BranchId = x.BranchId,
                        BranchName = b != null ? b.Name : null,

                        MachineId = x.MachineId,
                        MachineName = m != null
                            ? (m.Serial_Number != null && m.Serial_Number != ""
                                ? m.Serial_Number
                                : (m.Calling_Number != null && m.Calling_Number != ""
                                    ? m.Calling_Number
                                    : m.Code))
                            : null,

                        Status = x.Status,
                        Active = x.Active,
                        CompletedQty = x.CompletedQty,
                        RejectedQty = x.RejectedQty,

                        Remarks = x.Remarks,
                        Deleted = x.Deleted,

                        ThreadCost = x.ThreadCost,
                        MachineCost = x.MachineCost,
                        LaborCost = x.LaborCost,
                        ElectricityCost = x.ElectricityCost,
                        OtherCost = x.OtherCost,
                        TotalCost = x.TotalCost,
                        CostPerUnit = x.CostPerUnit,

                        CreatedBy = x.CreatedBy,
                        CreatedDate = x.CreatedDate,
                        UpdatedBy = x.UpdatedBy,
                        UpdatedDate = x.UpdatedDate,
                        RecordTimeStamp = x.RecordTimeStamp,

                        CreatedByEmployeeId = cu != null ? (int?)cu.EmployeeId : null,
                        UpdatedByEmployeeId = uu != null ? (int?)uu.EmployeeId : null
                    }
                ).FirstOrDefault();

                if (raw == null)
                    return JsonError("Not found.");

                var entity = new WorkOrderItemDetailViewModel
                {
                    Id = raw.Id,

                    WorkOrderId = raw.WorkOrderId,
                    WorkOrderCode = raw.WorkOrderCode,
                    WorkOrderLabel = raw.WorkOrderLabel,

                    JobOrderId = raw.JobOrderId,
                    JobOrderCode = raw.JobOrderCode,

                    DesignId = raw.DesignId,
                    DesignCode = raw.DesignCode,
                    DesignName = raw.DesignName,

                    CompId = raw.CompId,
                    ComponentCode = raw.ComponentCodeNumeric == null ? null : raw.ComponentCodeNumeric.ToString(),
                    ComponentName = raw.ComponentName,

                    ProductId = raw.ProductId,
                    ProductName = raw.ProductName,

                    Quantity = raw.Quantity,
                    UnitPrice = raw.UnitPrice,
                    TotalPrice = raw.TotalPrice,

                    BranchId = raw.BranchId,
                    BranchName = raw.BranchName,

                    MachineId = raw.MachineId,
                    MachineName = raw.MachineName,

                    Status = raw.Status,
                    Active = raw.Active,
                    CompletedQty = raw.CompletedQty,
                    RejectedQty = raw.RejectedQty,

                    Remarks = raw.Remarks,
                    Deleted = raw.Deleted,

                    ThreadCost = raw.ThreadCost,
                    MachineCost = raw.MachineCost,
                    LaborCost = raw.LaborCost,
                    ElectricityCost = raw.ElectricityCost,
                    OtherCost = raw.OtherCost,
                    TotalCost = raw.TotalCost,
                    CostPerUnit = raw.CostPerUnit,

                    CreatedBy = raw.CreatedBy,
                    CreatedDate = raw.CreatedDate,
                    UpdatedBy = raw.UpdatedBy,
                    UpdatedDate = raw.UpdatedDate,
                    RecordTimeStamp = raw.RecordTimeStamp,

                    CreatedByEmployeeId = raw.CreatedByEmployeeId,
                    UpdatedByEmployeeId = raw.UpdatedByEmployeeId,

                    CreatedDateText = raw.CreatedDate.HasValue
                        ? raw.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null,
                    UpdatedDateText = raw.UpdatedDate.HasValue
                        ? raw.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                        : null
                };

                return JsonSuccess("Loaded successfully.", entity);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work order item.", ex);
            }
        }

        // ========================= GET BY ID FOR EDIT =========================
        [HttpGet]
        [Route("get/{id:int}")]
        public ActionResult Get(int id)
        {
            try
            {
                var entity = db.WorkOrderItems
                    .Where(x => x.Id == id && (x.Deleted == false || x.Deleted == null))
                    .Select(x => new
                    {
                        x.Id,
                        x.WorkOrderId,
                        x.JobOrderId,
                        x.DesignId,
                        x.CompId,
                        x.ProductId,
                        x.BranchId,
                        x.MachineId,
                        x.Quantity,
                        x.UnitPrice,
                        x.ThreadCost,
                        x.MachineCost,
                        x.LaborCost,
                        x.ElectricityCost,
                        x.OtherCost,
                        x.TotalCost,
                        x.CostPerUnit,
                        x.CompletedQty,
                        x.RejectedQty,
                        x.Status,
                        x.Remarks,
                        x.Active,
                        x.Deleted,
                        x.CreatedBy,
                        x.CreatedDate,
                        x.UpdatedBy,
                        x.UpdatedDate,
                        x.RecordTimeStamp
                    })
                    .FirstOrDefault();

                if (entity == null)
                    return JsonError("Record not found.");

                return JsonSuccess("Loaded successfully.", entity);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work order item.", ex);
            }
        }

        // ========================= CREATE =========================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(WorkOrderItemDto dto)
        {
            return JsonError("Manual Work Order Item creation is disabled. Items are auto-generated when Work Order is posted.");
        }

        // ========================= UPDATE =========================
        [HttpPost]
        [Route("update/{id:int}")]
        public ActionResult Update(int id, WorkOrderItemDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid request.");

                var entity = db.WorkOrderItems                    
                    .FirstOrDefault(x =>
                        x.Id == id &&
                        (x.Deleted == false || x.Deleted == null));

                if (entity == null)
                    return JsonError("Record not found.");
                
                bool hasProduction = db.DailyProduction.Any(x =>
                    x.WorkOrderItemId == id &&
                    x.Deleted != true);

                if (hasProduction)
                    return JsonError("This Work Order Item is already used in production and cannot be modified.");

                entity.MachineId = dto.MachineId;
                entity.Remarks = string.IsNullOrWhiteSpace(dto.Remarks)
                    ? null
                    : dto.Remarks.Trim();

                entity.Status = string.IsNullOrWhiteSpace(dto.Status)
                    ? (string.IsNullOrWhiteSpace(entity.Status) ? "Pending" : entity.Status)
                    : dto.Status.Trim();

                entity.Active = dto.Active ?? true;

                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;
                entity.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Updated successfully.", new
                {
                    entity.Id,
                    entity.WorkOrderId,
                    entity.CompId,
                    entity.MachineId,
                    entity.Status,
                    entity.Active,
                    entity.Remarks
                });
            }
            catch (Exception ex)
            {
                return JsonError("Update failed.", ex);
            }
        }

        // ========================= DELETE (SOFT) =========================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var entity = db.WorkOrderItems                    
                    .FirstOrDefault(x => x.Id == id);

                if (entity == null || entity.Deleted == true)
                    return JsonError("Record not found.");
               
                entity.Deleted = true;
                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;
                entity.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed.", ex);
            }
        }

        // ========================= COMPONENTS BY DESIGN =========================
        [HttpGet]
        [Route("components-by-design/{designId:int}")]
        public ActionResult GetComponentsByDesign(int designId)
        {
            try
            {
                if (designId <= 0)
                    return JsonError("Invalid design.");

                var rows = (
                    from bom in db.Gen_BOM
                    join comp in db.Gen_Components on bom.CompId equals comp.Id
                    where bom.DesignId == designId
                          && (bom.Deleted == false || bom.Deleted == null)
                          && (comp.Deleted == false || comp.Deleted == null)
                    orderby comp.Id
                    select new
                    {
                        comp.Id,
                        comp.Code,
                        comp.Name
                    }
                )
                .Distinct()
                .ToList();

                var data = rows.Select(x => new
                {
                    id = x.Id,
                    name = x.Name ?? "",
                    code = x.Code == null ? "" : x.Code.ToString(),
                    display = (x.Code == null ? "" : x.Code.ToString()) + " - " + (x.Name ?? "")
                }).ToList();

                return Json(new
                {
                    success = true,
                    message = "Components loaded successfully.",
                    data = data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load components.", ex);
            }
        }

        // ========================= GENERATE FROM WORK ORDER =========================
        [HttpPost]
        [Route("generate-from-workorder/{workOrderId:int}")]
        public ActionResult GenerateFromWorkOrder(int workOrderId)
        {
            return JsonError("This action is disabled. Work Order Items are generated automatically when Work Order is posted.");
        }
       
        // ========================= RECALCULATE COST =========================
        [HttpGet]
        [Route("recalculate-cost")]
        public ActionResult RecalculateCost(int designId, int quantity)
        {
            try
            {
                if (designId <= 0)
                    return JsonError("Invalid design.");

                if (quantity <= 0)
                    return JsonError("Quantity must be greater than zero.");

                var costingService = new DesignCostingService(db);
                var result = costingService.CalculateDesignCost(designId, quantity, 20);

                return JsonSuccess("Cost calculated.", new
                {
                    costPerUnit = result.CostPerUnit,
                    totalCost = result.TotalCost,

                    sellingPricePerUnit = result.SellingPricePerUnit,
                    totalPrice = result.TotalSellingPrice,

                    marginPercent = result.MarginPercent,

                    threadCost = result.ThreadCost,
                    machineCost = result.MachineCost,
                    laborCost = result.LaborCost,
                    electricityCost = result.ElectricityCost,
                    otherCost = result.OtherCost
                });
            }
            catch (Exception ex)
            {
                return JsonError("Calculation failed.", ex);
            }
        }

        // ========================= HELPERS =========================
        private decimal? CalculateTotalPrice(int quantity, decimal? unitPrice)
        {
            if (!unitPrice.HasValue)
                return null;

            return quantity * unitPrice.Value;
        }

        private string GetDeepInnerMessage(Exception ex)
        {
            if (ex == null) return "";

            var messages = new StringBuilder();
            Exception current = ex;

            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Message))
                {
                    if (messages.Length > 0)
                        messages.Append(" | ");

                    messages.Append(current.Message);
                }

                current = current.InnerException;
            }

            return messages.ToString();
        }

        private void ApplyDynamicCosting(WorkOrderItems item, int designId, int quantity)
        {
            if (item == null)
                throw new Exception("Work Order Item is null.");

            if (designId <= 0)
                throw new Exception("Invalid design.");

            if (quantity <= 0)
                throw new Exception("Quantity must be greater than zero.");

            var costingService = new DesignCostingService(db);

            // 20% margin
            var costing = costingService.CalculateDesignCost(designId, quantity, 20);

            item.ThreadCost = costing.ThreadCost;
            item.MachineCost = costing.MachineCost;
            item.LaborCost = costing.LaborCost;
            item.ElectricityCost = costing.ElectricityCost;
            item.OtherCost = costing.OtherCost;

            item.CostPerUnit = costing.CostPerUnit;
            item.TotalCost = costing.TotalCost;

            // PRICE now differs from COST
            item.UnitPrice = costing.SellingPricePerUnit;
        }

        [HttpGet]
        [Route("dropdown/workorders")]
        public ActionResult WorkOrdersDropdown()
        {
            try
            {
                var data = db.WorkOrders
                    .Where(x => x.Deleted == false || x.Deleted == null)
                    .OrderByDescending(x => x.Id)
                    .ToList()
                    .Select(x => new
                    {
                        id = x.Id,
                        code = x.WorkOrderCode,
                        name = x.WorkOrderCode,

                        jobOrderId = x.JobOrderId,
                        designId = x.DesignId,
                        productId = x.ProductId,
                        productName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : null,
                        branchId = x.BranchId,

                        orderQty = x.OrderQty
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    message = "Loaded successfully.",
                    data = data
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load work orders.", ex);
            }
        }

        // ========================= EXPORT EXCEL =========================
        [HttpGet]
        [Route("export-excel")]
        public FileResult ExportExcel()
        {
            var rawData = db.WorkOrderItems
                .Where(x => x.Deleted == false || x.Deleted == null)
                .ToList()
                .Select(x => new
                {
                    x.Id,
                    x.WorkOrderId,
                    x.DesignId,
                    x.Quantity,
                    x.UnitPrice,
                    x.BranchId,
                    x.MachineId,
                    x.Remarks,
                    Created = x.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    Updated = x.UpdatedDate?.ToString("yyyy-MM-dd HH:mm") ?? ""
                })
                .ToList();

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("WorkOrderItems");
                ws.Cell(1, 1).InsertTable(rawData);

                using (var stream = new MemoryStream())
                {
                    workbook.SaveAs(stream);
                    var bytes = stream.ToArray();

                    return File(
                        bytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "WorkOrderItems.xlsx"
                    );
                }
            }
        }

        // ========================= EXPORT PDF =========================
        [HttpGet]
        [Route("export-pdf")]
        public FileResult ExportPdf()
        {
            var rawData = db.WorkOrderItems
                .Where(x => x.Deleted == false || x.Deleted == null)
                .ToList()
                .Select(x => new
                {
                    x.Id,
                    x.WorkOrderId,
                    x.DesignId,
                    x.Quantity,
                    x.UnitPrice,
                    x.BranchId,
                    x.MachineId,
                    x.Remarks,
                    Created = x.CreatedDate?.ToString("yyyy-MM-dd HH:mm") ?? "",
                    Updated = x.UpdatedDate?.ToString("yyyy-MM-dd HH:mm") ?? ""
                })
                .ToList();

            using (var stream = new MemoryStream())
            {
                var doc = new iTextSharp.text.Document(
                    iTextSharp.text.PageSize.A4.Rotate(), 10, 10, 10, 10);
                iTextSharp.text.pdf.PdfWriter.GetInstance(doc, stream);

                doc.Open();

                var table = new iTextSharp.text.pdf.PdfPTable(9)
                {
                    WidthPercentage = 100
                };

                string[] headers = { "Id", "WO Id", "Design Id", "Qty", "UnitPrice", "BranchId", "MachineId", "Remarks", "Created" };
                foreach (var h in headers)
                {
                    var cell = new iTextSharp.text.pdf.PdfPCell(new iTextSharp.text.Phrase(h))
                    {
                        BackgroundColor = iTextSharp.text.BaseColor.LIGHT_GRAY
                    };
                    table.AddCell(cell);
                }

                foreach (var item in rawData)
                {
                    table.AddCell(item.Id.ToString());
                    table.AddCell(item.WorkOrderId.ToString());
                    table.AddCell(item.DesignId?.ToString() ?? "");
                    table.AddCell(item.Quantity.ToString());
                    table.AddCell(item.UnitPrice?.ToString() ?? "");
                    table.AddCell(item.BranchId?.ToString() ?? "");
                    table.AddCell(item.MachineId?.ToString() ?? "");
                    table.AddCell(item.Remarks ?? "");
                    table.AddCell(item.Created);
                }

                doc.Add(table);
                doc.Close();

                return File(
                    stream.ToArray(),
                    "application/pdf",
                    "WorkOrderItems.pdf"
                );
            }
        }

        // ========================= PRINT VIEW =========================
        [HttpGet]
        [Route("print")]
        public ActionResult Print()
        {
            var data = db.WorkOrderItems
                .Where(x => x.Deleted == false || x.Deleted == null)
                .ToList()
                .Select(x => new
                {
                    x.Id,
                    x.WorkOrderId,
                    x.DesignId,
                    x.Quantity,
                    x.UnitPrice,
                    x.BranchId,
                    x.MachineId,
                    x.Remarks,
                    x.CreatedDate,
                    x.UpdatedDate
                })
                .ToList();

            return View("WorkOrderItemsPrint", data);
        }
    }
}