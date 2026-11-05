using System;
using System.Linq;
using System.Data.Entity;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/qc-inspection-items")]
    public class QCInspectionItemsController : BaseController
    {
        // =========================================================
        // LIST
        // =========================================================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            string search = "",
            string activeFilter = "",
            bool showDeleted = false,
            int page = 1,
            int pageSize = 10,
            int? qcInspectionId = null)
        {
            try
            {
                if (page <= 0) page = 1;
                if (pageSize <= 0) pageSize = 10;

                var query =
                    from x in db.QC_InspectionItems

                    join q in db.QC_Inspection
                        on x.QCInspectionId equals q.Id

                    join w in db.WorkOrderItems
                        on x.WorkOrderItemId equals w.Id into wj
                    from w in wj.DefaultIfEmpty()

                    join wo in db.WorkOrders
                        on w.WorkOrderId equals wo.Id into woj
                    from wo in woj.DefaultIfEmpty()

                    join p in db.DailyProduction
                        on x.ProductionId equals p.Id into pj
                    from p in pj.DefaultIfEmpty()

                    join c in db.Gen_Components
                        on x.CompId equals c.Id into cj
                    from c in cj.DefaultIfEmpty()

                    where showDeleted || (x.Deleted == false || x.Deleted == null)

                    select new
                    {
                        x,
                        Header = q,
                        QCInspectionNo = q.QCNo,
                        WorkOrderCode = wo != null ? wo.WorkOrderCode : "",
                        ProductionLabel = p != null ? ("DP-" + p.Id) : "",
                        ComponentName = c != null ? c.Name : ""
                    };

                if (qcInspectionId.HasValue && qcInspectionId.Value > 0)
                {
                    query = query.Where(r => r.x.QCInspectionId == qcInspectionId.Value);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    query = query.Where(r =>
                        (r.QCInspectionNo != null && r.QCInspectionNo.Contains(search)) ||
                        (r.WorkOrderCode != null && r.WorkOrderCode.Contains(search)) ||
                        (r.ProductionLabel != null && r.ProductionLabel.Contains(search)) ||
                        (r.ComponentName != null && r.ComponentName.Contains(search)) ||
                        (r.x.Result != null && r.x.Result.Contains(search)) ||
                        (r.x.Remarks != null && r.x.Remarks.Contains(search))
                    );
                }

                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    activeFilter = activeFilter.Trim().ToLower();

                    if (activeFilter == "active")
                    {
                        query = query.Where(r => r.x.Active == true);
                    }
                    else if (activeFilter == "inactive")
                    {
                        query = query.Where(r => r.x.Active == false || r.x.Active == null);
                    }
                }

                var totalItems = query.Count();

                var rows = query
                    .OrderByDescending(r => r.x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList()
                    .Select(r => new QCInspectionItemRowVm
                    {
                        Id = r.x.Id,

                        QCInspectionId = r.x.QCInspectionId,
                        QCInspectionNo = r.QCInspectionNo,

                        WorkOrderItemId = r.x.WorkOrderItemId,
                        WorkOrderCode = r.WorkOrderCode,

                        ProductionId = r.x.ProductionId,
                        ProductionNo = r.ProductionLabel,

                        CompId = r.x.CompId,
                        ComponentName = r.ComponentName,

                        Qty = r.x.Qty,
                        PassedQty = r.x.PassedQty,
                        FailedQty = r.x.FailedQty,
                        ReworkQty = r.x.ReworkQty,
                        Result = r.x.Result,
                        Remarks = r.x.Remarks,

                        Active = r.x.Active ?? false,
                        Deleted = r.x.Deleted ?? false,

                        CreatedBy = r.x.CreatedBy,
                        CreatedByEmployeeId = GetEmployeeId(r.x.CreatedBy)?.ToString(),
                        CreatedDate = r.x.CreatedDate,
                        CreatedDateText = r.x.CreatedDate.ToString("dd-MMM-yyyy HH:mm")
                    })
                    .ToList();

                return JsonSuccess("QC inspection items loaded successfully.", new
                {
                    items = rows,
                    page,
                    pageSize,
                    totalItems,
                    totalPages = (int)Math.Ceiling((double)totalItems / pageSize)
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load QC inspection items.", ex);
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
                var row =
                    (from x in db.QC_InspectionItems

                     join q in db.QC_Inspection
                        on x.QCInspectionId equals q.Id

                     join w in db.WorkOrderItems
                        on x.WorkOrderItemId equals w.Id into wj
                     from w in wj.DefaultIfEmpty()

                     join wo in db.WorkOrders
                        on w.WorkOrderId equals wo.Id into woj
                     from wo in woj.DefaultIfEmpty()

                     join p in db.DailyProduction
                        on x.ProductionId equals p.Id into pj
                     from p in pj.DefaultIfEmpty()

                     join c in db.Gen_Components
                        on x.CompId equals c.Id into cj
                     from c in cj.DefaultIfEmpty()

                     where x.Id == id && (x.Deleted == false || x.Deleted == null)

                     select new QCInspectionItemDetailVm
                     {
                         Id = x.Id,

                         QCInspectionId = x.QCInspectionId,
                         QCInspectionNo = q.QCNo,

                         WorkOrderItemId = x.WorkOrderItemId,
                         WorkOrderCode = wo != null ? wo.WorkOrderCode : "",

                         ProductionId = x.ProductionId,
                         ProductionNo = p != null ? ("DP-" + p.Id) : "",

                         CompId = x.CompId,
                         ComponentName = c != null ? c.Name : "",

                         Qty = x.Qty,
                         PassedQty = x.PassedQty,
                         FailedQty = x.FailedQty,
                         ReworkQty = x.ReworkQty,
                         Result = x.Result,
                         Remarks = x.Remarks,

                         Active = x.Active ?? false,
                         Deleted = x.Deleted ?? false,

                         CreatedBy = x.CreatedBy,
                         CreatedByEmployeeId = "",
                         CreatedDate = x.CreatedDate,
                         CreatedDateText = x.CreatedDate.ToString("dd-MMM-yyyy HH:mm")
                     }).FirstOrDefault();

                if (row == null)
                    return JsonError("QC inspection item not found.");

                row.CreatedByEmployeeId = GetEmployeeId(row.CreatedBy)?.ToString();

                return JsonSuccess("QC inspection item detail loaded successfully.", row);
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
        public ActionResult Create(QCInspectionItemDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid request.");

                    var validation = ValidateDto(dto);
                    if (validation != null)
                        return validation;

                    var qc = db.QC_Inspection.FirstOrDefault(x =>
                        x.Id == dto.QCInspectionId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (qc == null)
                        return JsonError("QC Inspection not found.");

                    if (qc.IsPosted == true)
                        return JsonError("Posted QC Inspection cannot be modified.");

                    var woItem = db.WorkOrderItems.FirstOrDefault(x =>
                        x.Id == dto.WorkOrderItemId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (woItem == null)
                        return JsonError("Work Order Item not found.");

                    if (qc.WorkOrderId.HasValue && woItem.WorkOrderId != qc.WorkOrderId.Value)
                        return JsonError("Selected Work Order Item does not belong to the same QC batch.");

                    if (qc.DesignId.HasValue && woItem.DesignId != qc.DesignId)
                        return JsonError("Selected Work Order Item does not match QC design.");

                    if (qc.ProductId.HasValue && woItem.ProductId != qc.ProductId)
                        return JsonError("Selected Work Order Item does not match QC product.");

                    if (dto.ProductionId.HasValue)
                    {
                        var production = db.DailyProduction
                            .Include(x => x.WorkOrderItems)
                            .FirstOrDefault(x =>
                                x.Id == dto.ProductionId.Value &&
                                (x.Deleted == false || x.Deleted == null));

                        if (production == null)
                            return JsonError("Selected production record not found.");

                        if (production.WorkOrderItems == null)
                            return JsonError("Selected production row is invalid.");

                        if (qc.WorkOrderId.HasValue && production.WorkOrderItems.WorkOrderId != qc.WorkOrderId.Value)
                            return JsonError("Selected production record does not belong to the same QC batch.");

                        if (qc.DesignId.HasValue && production.WorkOrderItems.DesignId != qc.DesignId)
                            return JsonError("Selected production record does not match QC design.");

                        if (qc.ProductId.HasValue && production.WorkOrderItems.ProductId != qc.ProductId)
                            return JsonError("Selected production record does not match QC product.");
                    }

                    if (dto.CompId.HasValue)
                    {
                        var compExists = db.Gen_Components.Any(x =>
                            x.Id == dto.CompId.Value &&
                            (x.Deleted == false || x.Deleted == null));

                        if (!compExists)
                            return JsonError("Selected component not found.");

                        if (woItem.CompId.HasValue && woItem.CompId != dto.CompId)
                            return JsonError("Selected component does not match Work Order Item component.");
                    }

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    var entity = new QC_InspectionItems
                    {
                        QCInspectionId = dto.QCInspectionId,
                        WorkOrderItemId = dto.WorkOrderItemId.Value,
                        ProductionId = dto.ProductionId,
                        CompId = dto.CompId,

                        Qty = dto.Qty,
                        PassedQty = dto.PassedQty,
                        FailedQty = dto.FailedQty,
                        ReworkQty = dto.ReworkQty,
                        Result = string.IsNullOrWhiteSpace(dto.Result) ? null : dto.Result.Trim(),
                        Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim(),

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now,

                        Active = dto.Active,
                        Deleted = false
                    };

                    db.QC_InspectionItems.Add(entity);
                    db.SaveChanges();

                    SyncHeaderTotals(dto.QCInspectionId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("QC inspection item created successfully.", new { id = entity.Id });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to create QC inspection item.", ex);
                }
            }
        }

        // =========================================================
        // UPDATE
        // =========================================================
        [HttpPost]
        [Route("update/{id:int}")]
        public ActionResult Update(int id, QCInspectionItemDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid request.");

                    var validation = ValidateDto(dto);
                    if (validation != null)
                        return validation;

                    var entity = db.QC_InspectionItems.FirstOrDefault(x =>
                        x.Id == id &&
                        (x.Deleted == false || x.Deleted == null));

                    if (entity == null)
                        return JsonError("QC inspection item not found.");

                    var qc = db.QC_Inspection.FirstOrDefault(x =>
                        x.Id == dto.QCInspectionId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (qc == null)
                        return JsonError("QC Inspection not found.");

                    if (qc.IsPosted == true)
                        return JsonError("Posted QC Inspection cannot be modified.");

                    var woItem = db.WorkOrderItems.FirstOrDefault(x =>
                        x.Id == dto.WorkOrderItemId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (woItem == null)
                        return JsonError("Work Order Item not found.");

                    if (qc.WorkOrderId.HasValue && woItem.WorkOrderId != qc.WorkOrderId.Value)
                        return JsonError("Selected Work Order Item does not belong to the same QC batch.");

                    if (qc.DesignId.HasValue && woItem.DesignId != qc.DesignId)
                        return JsonError("Selected Work Order Item does not match QC design.");

                    if (qc.ProductId.HasValue && woItem.ProductId != qc.ProductId)
                        return JsonError("Selected Work Order Item does not match QC product.");

                    if (dto.ProductionId.HasValue)
                    {
                        var production = db.DailyProduction
                            .Include(x => x.WorkOrderItems)
                            .FirstOrDefault(x =>
                                x.Id == dto.ProductionId.Value &&
                                (x.Deleted == false || x.Deleted == null));

                        if (production == null)
                            return JsonError("Selected production record not found.");

                        if (production.WorkOrderItems == null)
                            return JsonError("Selected production row is invalid.");

                        if (qc.WorkOrderId.HasValue && production.WorkOrderItems.WorkOrderId != qc.WorkOrderId.Value)
                            return JsonError("Selected production record does not belong to the same QC batch.");

                        if (qc.DesignId.HasValue && production.WorkOrderItems.DesignId != qc.DesignId)
                            return JsonError("Selected production record does not match QC design.");

                        if (qc.ProductId.HasValue && production.WorkOrderItems.ProductId != qc.ProductId)
                            return JsonError("Selected production record does not match QC product.");
                    }

                    if (dto.CompId.HasValue)
                    {
                        var compExists = db.Gen_Components.Any(x =>
                            x.Id == dto.CompId.Value &&
                            (x.Deleted == false || x.Deleted == null));

                        if (!compExists)
                            return JsonError("Selected component not found.");

                        if (woItem.CompId.HasValue && woItem.CompId != dto.CompId)
                            return JsonError("Selected component does not match Work Order Item component.");
                    }

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    int oldHeaderId = entity.QCInspectionId;

                    entity.QCInspectionId = dto.QCInspectionId;
                    entity.WorkOrderItemId = dto.WorkOrderItemId.Value;
                    entity.ProductionId = dto.ProductionId;
                    entity.CompId = dto.CompId;

                    entity.Qty = dto.Qty;
                    entity.PassedQty = dto.PassedQty;
                    entity.FailedQty = dto.FailedQty;
                    entity.ReworkQty = dto.ReworkQty;
                    entity.Result = string.IsNullOrWhiteSpace(dto.Result) ? null : dto.Result.Trim();
                    entity.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

                    entity.Active = dto.Active;
                    entity.UpdatedBy = userId;
                    entity.UpdatedDate = now;

                    db.SaveChanges();

                    SyncHeaderTotals(dto.QCInspectionId, userId, now);

                    if (oldHeaderId != dto.QCInspectionId)
                        SyncHeaderTotals(oldHeaderId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("QC inspection item updated successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to update QC inspection item.", ex);
                }
            }
        }

        // =========================================================
        // SOFT DELETE
        // =========================================================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var entity = db.QC_InspectionItems.FirstOrDefault(x =>
                        x.Id == id &&
                        (x.Deleted == false || x.Deleted == null));

                    if (entity == null)
                        return JsonError("QC inspection item not found.");

                    var qc = db.QC_Inspection.FirstOrDefault(x =>
                        x.Id == entity.QCInspectionId &&
                        (x.Deleted == false || x.Deleted == null));

                    if (qc != null && qc.IsPosted == true)
                        return JsonError("Posted QC Inspection cannot be modified.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    int headerId = entity.QCInspectionId;

                    entity.Deleted = true;
                    entity.Active = false;
                    entity.UpdatedBy = userId;
                    entity.UpdatedDate = now;

                    db.SaveChanges();

                    SyncHeaderTotals(headerId, userId, now);

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("QC inspection item deleted successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to delete QC inspection item.", ex);
                }
            }
        }

        // =========================================================
        // TOGGLE ACTIVE
        // =========================================================
        [HttpPost]
        [Route("toggle-active/{id:int}")]
        public ActionResult ToggleActive(int id)
        {
            try
            {
                var entity = db.QC_InspectionItems.FirstOrDefault(x =>
                    x.Id == id &&
                    (x.Deleted == false || x.Deleted == null));

                if (entity == null)
                    return JsonError("QC inspection item not found.");

                var qc = db.QC_Inspection.FirstOrDefault(x =>
                    x.Id == entity.QCInspectionId &&
                    (x.Deleted == false || x.Deleted == null));

                if (qc != null && qc.IsPosted == true)
                    return JsonError("Posted QC Inspection cannot be modified.");

                entity.Active = !(entity.Active ?? false);
                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Status updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update status.", ex);
            }
        }

        // =========================================================
        // DROPDOWN: QC INSPECTIONS
        // =========================================================
        [HttpGet]
        [Route("dropdown/qc-inspections")]
        public ActionResult QcInspectionDropdown()
        {
            try
            {
                var rows = db.QC_Inspection
                    .Include(x => x.WorkOrders)
                    .Include(x => x.Gen_Products)
                    .Include(x => x.Gen_Design)
                    .Where(x => (x.Deleted == false || x.Deleted == null))
                    .OrderByDescending(x => x.Id)
                    .Select(x => new
                    {
                        id = x.Id,
                        name =
                            (x.QCNo ?? ("QC-" + x.Id)) +
                            " | " +
                            (x.Gen_Products != null ? x.Gen_Products.Prod_Name : "Product") +
                            " | " +
                            (x.WorkOrders != null ? x.WorkOrders.WorkOrderCode : ("WO-" + (x.WorkOrderId.HasValue ? x.WorkOrderId.Value.ToString() : "-"))) +
                            " | " +
                            (x.Gen_Design != null
                                ? (!string.IsNullOrWhiteSpace(x.Gen_Design.DNo) ? x.Gen_Design.DNo : x.Gen_Design.D_Code)
                                : "Design")
                    })
                    .ToList();

                return JsonSuccess("QC inspection dropdown loaded successfully.", rows);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load QC inspection dropdown.", ex);
            }
        }

        // =========================================================
        // SMART AUTO-FILL BY QC
        // =========================================================
        [HttpGet]
        [Route("autofill/by-qc/{qcInspectionId:int}")]
        public ActionResult GetAutoFillByQc(int qcInspectionId)
        {
            try
            {
                var qc = db.QC_Inspection.FirstOrDefault(x =>
                    x.Id == qcInspectionId &&
                    (x.Deleted == false || x.Deleted == null));

                if (qc == null)
                    return JsonError("QC inspection not found.");

                var workOrderItems = (
                    from x in db.WorkOrderItems
                    join c in db.Gen_Components on x.CompId equals c.Id into cj
                    from c in cj.DefaultIfEmpty()
                    where x.WorkOrderId == qc.WorkOrderId &&
                          x.DesignId == qc.DesignId &&
                          x.ProductId == qc.ProductId &&
                          (x.Deleted == false || x.Deleted == null)
                    select new
                    {
                        id = x.Id,
                        name =
                            "WOI-" + x.Id +
                            " | " +
                            (c != null ? c.Name : (x.CompId.HasValue ? ("Comp #" + x.CompId.Value) : "No Comp")) +
                            " | Qty: " + x.Quantity
                    })
                    .ToList();

                var productions = db.DailyProduction
                    .Include(x => x.WorkOrderItems)
                    .Where(x =>
                        (x.Deleted == false || x.Deleted == null) &&
                        (x.IsReversed == false || x.IsReversed == null) &&
                        x.WorkOrderItems != null &&
                        x.WorkOrderItems.WorkOrderId == qc.WorkOrderId &&
                        x.WorkOrderItems.DesignId == qc.DesignId &&
                        x.WorkOrderItems.ProductId == qc.ProductId)
                    .Select(x => new
                    {
                        id = x.Id,
                        name = "DP-" + x.Id + " | Accepted: " + (x.AcceptedQty ?? 0)
                    })
                    .ToList();

                var components = (
                    from x in db.WorkOrderItems
                    join c in db.Gen_Components on x.CompId equals c.Id
                    where x.WorkOrderId == qc.WorkOrderId &&
                          x.DesignId == qc.DesignId &&
                          x.ProductId == qc.ProductId &&
                          x.CompId.HasValue &&
                          (x.Deleted == false || x.Deleted == null)
                    select new
                    {
                        id = x.CompId.Value,
                        name = c.Name
                    })
                    .Distinct()
                    .ToList();

                return JsonSuccess("Loaded successfully.", new
                {
                    qcInspectionId = qc.Id,
                    workOrderId = qc.WorkOrderId,
                    designId = qc.DesignId,
                    productId = qc.ProductId,
                    workOrderItems,
                    productions,
                    components
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load QC auto-fill data.", ex);
            }
        }

        [HttpPost]
        [Route("item-save")]
        public ActionResult SaveItem(QCInspectionItemDto dto)
        {
            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    if (dto == null)
                        return JsonError("Invalid request.");

                    var entity = db.QC_InspectionItems
                        .FirstOrDefault(x => x.Id == dto.Id && (x.Deleted == false || x.Deleted == null));

                    if (entity == null)
                        return JsonError("QC item not found.");

                    var header = db.QC_Inspection
                        .FirstOrDefault(x =>
                            x.Id == entity.QCInspectionId &&
                            (x.Deleted == false || x.Deleted == null));

                    if (header == null)
                        return JsonError("QC header not found.");

                    bool isReversed = header.Status == "Reversed";
                    if (isReversed)
                        return JsonError("Reversed QC cannot be modified.");

                    if (dto.Qty < 0 || dto.PassedQty < 0 || dto.FailedQty < 0 || dto.ReworkQty < 0)
                        return JsonError("Quantities cannot be negative.");

                    if (dto.Qty != (dto.PassedQty + dto.FailedQty + dto.ReworkQty))
                        return JsonError("Qty must equal Passed + Failed + Rework.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;

                    entity.Qty = dto.Qty;
                    entity.PassedQty = dto.PassedQty;
                    entity.FailedQty = dto.FailedQty;
                    entity.ReworkQty = dto.ReworkQty;
                    entity.Remarks = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

                    if (dto.ReworkQty > 0)
                        entity.Result = "Rework";
                    else if (dto.FailedQty > 0)
                        entity.Result = "Failed";
                    else if (dto.PassedQty > 0)
                        entity.Result = "Passed";
                    else
                        entity.Result = "Pending";

                    entity.UpdatedBy = userId;
                    entity.UpdatedDate = now;

                    db.SaveChanges();

                    // Recalculate header totals from item breakup
                    var items = db.QC_InspectionItems
                        .Where(x =>
                            x.QCInspectionId == entity.QCInspectionId &&
                            (x.Deleted == false || x.Deleted == null))
                        .ToList();

                    header.InspectedQty = items.Sum(x => (decimal?)x.Qty) ?? 0m;
                    header.PassedQty = items.Sum(x => (decimal?)x.PassedQty) ?? 0m;
                    header.FailedQty = items.Sum(x => (decimal?)x.FailedQty) ?? 0m;
                    header.ReworkQty = items.Sum(x => (decimal?)x.ReworkQty) ?? 0m;

                    // Keep posted rows posted; only draft rows move Pending/InProgress here
                    if (header.IsPosted == true)
                    {
                        header.Status = "Posted";
                    }
                    else
                    {
                        header.Status = (header.InspectedQty ?? 0m) > 0m ? "InProgress" : "Pending";
                    }

                    header.UpdatedBy = userId;
                    header.UpdatedDate = now;
                    header.RecordTimeStamp = now;

                    db.SaveChanges();
                    tx.Commit();

                    return JsonSuccess("QC item saved successfully.");
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return JsonError("Failed to save QC item.", ex);
                }
            }
        }

        // =========================================================
        // PRIVATE: VALIDATION
        // =========================================================
        private ActionResult ValidateDto(QCInspectionItemDto dto)
        {
            if (dto.QCInspectionId <= 0)
                return JsonError("QC Inspection is required.");

            if (dto.WorkOrderItemId <= 0)
                return JsonError("Work Order Item is required.");

            if (dto.Qty <= 0)
                return JsonError("Qty must be greater than zero.");

            if (dto.PassedQty < 0 || dto.FailedQty < 0 || dto.ReworkQty < 0)
                return JsonError("Passed, Failed and Rework quantities cannot be negative.");

            if (dto.Qty != (dto.PassedQty + dto.FailedQty + dto.ReworkQty))
                return JsonError("Qty must equal Passed + Failed + Rework.");

            return null;
        }
        
        // =========================================================
        // PRIVATE: SYNC HEADER TOTALS
        // =========================================================
        private void SyncHeaderTotals(int qcInspectionId, int userId, DateTime now)
        {
            var header = db.QC_Inspection.FirstOrDefault(x =>
                x.Id == qcInspectionId &&
                (x.Deleted == false || x.Deleted == null));

            if (header == null)
                return;

            var items = db.QC_InspectionItems
                .Where(x =>
                    x.QCInspectionId == qcInspectionId &&
                    (x.Deleted == false || x.Deleted == null))
                .ToList();

            header.InspectedQty = items.Sum(x => (decimal?)x.Qty) ?? 0m;
            header.PassedQty = items.Sum(x => (decimal?)x.PassedQty) ?? 0m;
            header.FailedQty = items.Sum(x => (decimal?)x.FailedQty) ?? 0m;
            header.ReworkQty = items.Sum(x => (decimal?)x.ReworkQty) ?? 0m;

            bool isReversed = header.Status == "Reversed";

            if (!isReversed)
            {
                if (header.IsPosted == true)
                    header.Status = "Posted";
                else
                    header.Status = (header.InspectedQty ?? 0m) > 0m ? "InProgress" : "Pending";
            }

            header.UpdatedBy = userId;
            header.UpdatedDate = now;
            header.RecordTimeStamp = now;
        }
       
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}