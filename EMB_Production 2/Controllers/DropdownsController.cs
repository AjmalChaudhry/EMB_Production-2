using System;
using System.Linq;
using System.Web.Mvc;
using System.Collections.Generic;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/dropdowns")]
    public class DropdownsController : BaseController
    {        
        [HttpGet, Route("branches")]
        public ActionResult Branches()
        {
            var list = db.Branches
                .Where(x => (x.Deleted == false || x.Deleted == null) && x.IsActive == true)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("~/emb-products/branches/list-dropdown")]
        public ActionResult BranchesAlias() => Branches();
        
        [HttpGet, Route("brands")]
        public ActionResult Brands()
        {
            var list = db.Gen_Brands
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.BrandName)
                .Select(x => new { id = x.Id, name = x.BrandName })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
        
        [HttpGet, Route("services")]
        public ActionResult Services()
        {
            var list = db.Gen_TheServices
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.Service_Name)
                .Select(x => new { id = x.Id, name = x.Service_Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("services-basic")]
        public ActionResult GetServicesBasic()
        {
            var list = db.Gen_TheServices
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.Service_Name)
                .Select(x => new { id = x.Id, name = x.Service_Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
        
        [HttpGet, Route("service-types")]
        public ActionResult ServiceTypes()
        {
            var list = db.Gen_ServiceType
                .Where(x => (x.Deleted == false || x.Deleted == null) && x.Active == true)
                .OrderBy(x => x.Service_Type)
                .Select(x => new { id = x.Id, name = x.Service_Type })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("~/emb-products/service-types/list-dropdown")]
        public ActionResult ServiceTypesAlias() => ServiceTypes();
        
        [HttpGet, Route("machines")]
        public ActionResult Machines()
        {
            var list = db.Gen_Machines
                .Where(x => (x.Deleted == false || x.Deleted == null) && x.Active == true)
                .OrderBy(x => x.Code)
                .Select(x => new
                {
                    id = x.Id,
                    name = (x.Code ?? "") + " - " + (x.Serial_Number ?? "")
                })
                .ToList();

            // ✅ Use central response style
            return JsonSuccess("OK", list);
        }

        [HttpGet, Route("~/emb-products/machines/list-dropdown")]
        public ActionResult MachinesAlias() => Machines();
       
        [HttpGet, Route("components")]
        public ActionResult Components()
        {
            var list = db.Gen_Components
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.Code)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
       
        [HttpGet, Route("customers")]
        public ActionResult Customers()
        {
            var list = db.Hr_Customers
                .Where(x => (x.Deleted == false || x.Deleted == null))
                .OrderBy(x => x.Customer_Name)
                
                .Select(x => new {
                    id = x.Id,                    
                    name = x.Customer_Name,        
                    customerCode = x.Customer_Id   
                })
                .ToList();

            return Json(
                new
                {
                    success = true,
                    data = list
                },
                JsonRequestBehavior.AllowGet
            );
        }
        
        [HttpGet, Route("suppliers")] 
        public ActionResult Suppliers()
        {
            var list = db.Gen_Suppliers
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.SupplierName)
                .Select(x => new { id = x.Id, name = x.SupplierName })
                .ToList();

            return Json(new { success = true, data = list }, JsonRequestBehavior.AllowGet);
        }
        
        [HttpGet, Route("designs-for-components")]
        public ActionResult DesignsForComponents()
        {
            // Label format: "{Code} - {Name}" so it matches the Designs list
            // table's Code column (D_Code, e.g. "DES-2026-009"). Falls back
            // to DNo if D_Code is empty for legacy designs. Customer name is
            // intentionally omitted — components are reusable across orders,
            // so tying their selector to a customer is misleading.
            var list = db.Gen_Design
                .Where(d => d.Deleted == false || d.Deleted == null)
                .OrderBy(d => d.D_Code ?? d.DNo)
                .ToList()
                .Select(d =>
                {
                    string code = !string.IsNullOrWhiteSpace(d.D_Code) ? d.D_Code : d.DNo;
                    string name = d.CollectionName;
                    string label =
                        (string.IsNullOrWhiteSpace(code) ? "" : code) +
                        (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name) ? " - " : "") +
                        (string.IsNullOrWhiteSpace(name) ? "" : name);

                    return new { id = d.Id, label };
                })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
        
        [HttpGet, Route("companies")]
        public ActionResult Companies()
        {
            var list = db.Adm_CompanyProfile
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.CompanyName)
                .Select(x => new { id = x.CompanyID, name = x.CompanyName })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
       
        [HttpGet, Route("roles")]
        public ActionResult Roles()
        {
            var list = db.Adm_Roles
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }
        
        [HttpGet, Route("threads")]
        public ActionResult Threads()
        {
            var list = db.Gen_Threads
                .Where(x => x.Active == true && (x.Deleted == false || x.Deleted == null))
                .OrderBy(x => x.Name)
                .Select(x => new { id = x.Id, name = x.Name })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("products")]
        public ActionResult Products()
        {
            var list = db.Gen_Products
                .Where(x =>
                    x.Deleted != true &&
                    x.Active == true &&
                    x.Prod_Code.StartsWith("PRD-"))
                .OrderBy(x => x.Prod_Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Prod_Name,
                    code = x.Prod_Code
                })
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("workorders")]
        public ActionResult WorkOrders()
        {
            try
            {
                var list = (
                    from wo in db.WorkOrders
                    join p in db.Gen_Products on wo.FinishedProductId equals p.Id into p1
                    from p in p1.DefaultIfEmpty()
                    where wo.Deleted == false || wo.Deleted == null
                    orderby wo.Id descending
                    select new
                    {
                        id = wo.Id,
                        code = wo.WorkOrderCode,
                        name = wo.WorkOrderCode,

                        jobOrderId = wo.JobOrderId,
                        designId = wo.DesignId,
                        productId = wo.ProductId,
                        branchId = wo.BranchId,
                        finishedProductId = wo.FinishedProductId,
                        finishedProductName = p != null ? p.Prod_Name : null,

                        orderQty = wo.OrderQty
                    }
                ).ToList();

                return Json(new
                {
                    success = true,
                    data = list
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Failed to load work orders.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet, Route("~/emb-products/workorders/list-dropdown")]
        public ActionResult WorkOrdersAlias() => WorkOrders();

        [HttpGet]
        [Route("work-order-items")]
        public ActionResult WorkOrderItems()
        {
            try
            {
                // Gate: only WOs that have had materials issued can have production posted.
                var issuedWorkOrderIds = db.Gen_Store
                    .Where(s =>
                        s.Deleted != true &&
                        s.ReferenceType == "MATERIAL_ISSUE" &&
                        s.ReferenceId.HasValue &&
                        s.OutQty > 0)
                    .Select(s => s.ReferenceId.Value)
                    .Distinct()
                    .ToList();

                var list = db.WorkOrderItems
                    .Include("WorkOrders")
                    .Include("WorkOrders.Gen_JobOrders")
                    .Include("Gen_Products")
                    .Include("Gen_Design")
                    .Include("Gen_Components")
                    .Include("Gen_Machines")
                    .Where(x =>
                        x.Deleted != true &&
                        x.Active == true &&

                        x.WorkOrders != null &&
                        x.WorkOrders.Deleted != true &&
                        x.WorkOrders.Active == true &&
                        x.WorkOrders.IsPosted == true &&

                        x.WorkOrders.Gen_JobOrders != null &&
                        x.WorkOrders.Gen_JobOrders.Deleted != true &&
                        x.WorkOrders.Gen_JobOrders.Active == true &&

                        issuedWorkOrderIds.Contains(x.WorkOrderId))
                    .ToList()
                    .Select(x =>
                    {
                        decimal plannedQty = x.Quantity;
                        decimal completedQty = x.CompletedQty;
                        decimal rejectedQty = x.RejectedQty;

                        decimal remainingQty = plannedQty - completedQty;
                        if (remainingQty < 0)
                            remainingQty = 0;

                        // Stitches per piece — read from the latest Gen_ComponentSpecs
                        // row for this WO Item's component. If the WO Item has no
                        // component link, sum stitches across all latest specs of the
                        // design's components (so the operator still gets a hint).
                        int stitchesPerPiece = 0;
                        if (x.CompId.HasValue && x.CompId.Value > 0)
                        {
                            stitchesPerPiece = db.Gen_ComponentSpecs
                                .Where(s => s.ComponentId == x.CompId.Value
                                            && s.IsLatest
                                            && s.Deleted == false
                                            && s.Stitch.HasValue)
                                .Select(s => s.Stitch.Value)
                                .FirstOrDefault();
                        }
                        else if (x.DesignId.HasValue && x.DesignId.Value > 0)
                        {
                            stitchesPerPiece = (
                                from c in db.Gen_Components
                                join s in db.Gen_ComponentSpecs on c.Id equals s.ComponentId
                                where c.D_Code == x.DesignId.Value
                                      && c.Deleted != true
                                      && s.IsLatest
                                      && s.Deleted == false
                                      && s.Stitch.HasValue
                                select s.Stitch.Value
                            ).DefaultIfEmpty(0).Sum();
                        }

                        string workOrderLabel =
                            x.WorkOrders != null &&
                            !string.IsNullOrWhiteSpace(x.WorkOrders.WorkOrderCode)
                                ? x.WorkOrders.WorkOrderCode
                                : "WO-" + x.WorkOrderId;

                        string productName =
                            x.Gen_Products != null
                                ? x.Gen_Products.Prod_Name
                                : null;

                        string designName =
                            x.Gen_Design != null
                                ? x.Gen_Design.DNo
                                : null;

                        string componentName =
                            x.Gen_Components != null
                                ? x.Gen_Components.Name
                                : null;

                        string machineName =
                            x.Gen_Machines != null
                                ? x.Gen_Machines.Serial_Number
                                : null;

                        return new
                        {
                            Id = x.Id,
                            WorkOrderId = x.WorkOrderId,
                            WorkOrderLabel = workOrderLabel,

                            ProductId = x.ProductId,
                            ProductName = productName,

                            DesignId = x.DesignId,
                            DesignName = designName,

                            ComponentId = x.CompId,
                            ComponentName = componentName,

                            MachineId = x.MachineId,
                            MachineName = machineName,

                            PlannedQty = plannedQty,
                            CompletedQty = completedQty,
                            RejectedQty = rejectedQty,
                            RemainingQty = remainingQty,

                            StitchesPerPiece = stitchesPerPiece,

                            Status = x.Status,
                            IsCompleted = remainingQty <= 0,

                            Name =
                                workOrderLabel
                                + " | "
                                + (!string.IsNullOrWhiteSpace(componentName)
                                    ? componentName
                                    : (!string.IsNullOrWhiteSpace(productName) ? productName : "-"))
                                + " | Planned: " + plannedQty
                                + " | Done: " + completedQty
                                + " | Rem: " + remainingQty
                        };
                    })
                    .Where(x => x.RemainingQty > 0)
                    .OrderBy(x => x.WorkOrderLabel)
                    .ThenBy(x => x.ComponentName)
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = list
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;

                return Json(new
                {
                    success = false,
                    message = "Failed to load work order items.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ================================================================
        // REWORK TASKS — QC rows that are posted with rework pending.
        // The operator picks one in Daily Production to record the
        // rework run. Posting that DP entry flips the QC back to
        // AwaitingReinspection so the inspector can re-check.
        // ================================================================
        [HttpGet, Route("rework-tasks")]
        public ActionResult ReworkTasks()
        {
            try
            {
                var rows = db.QC_Inspection
                    .Include("WorkOrders")
                    .Include("Gen_Products")
                    .Include("Gen_Design")
                    .Where(x =>
                        (x.Deleted == false || x.Deleted == null) &&
                        x.Status == "AwaitingRework" &&
                        x.IsPosted == true &&
                        (x.ReworkQty ?? 0m) > 0m)
                    .ToList()
                    .Select(x =>
                    {
                        string qcNo = x.QCNo ?? ("QC-" + x.Id);
                        string woLabel = x.WorkOrders != null && !string.IsNullOrWhiteSpace(x.WorkOrders.WorkOrderCode)
                            ? x.WorkOrders.WorkOrderCode
                            : ("WO-" + (x.WorkOrderId ?? 0));
                        string productName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : "-";
                        string designName = x.Gen_Design != null ? x.Gen_Design.DNo : "-";
                        decimal reworkQty = x.ReworkQty ?? 0m;

                        // Resolve a WO Item for the rework entry. Prefer the one
                        // stored on the QC row, else fall back to the first WO Item
                        // of the parent WO.
                        int? workOrderItemId = x.WorkOrderItemId;
                        if (!workOrderItemId.HasValue || workOrderItemId.Value <= 0)
                        {
                            workOrderItemId = db.WorkOrderItems
                                .Where(w =>
                                    w.WorkOrderId == (x.WorkOrderId ?? 0) &&
                                    w.Deleted != true &&
                                    w.Active == true)
                                .Select(w => (int?)w.Id)
                                .FirstOrDefault();
                        }

                        int? machineId = null;
                        int? compId = null;
                        if (workOrderItemId.HasValue && workOrderItemId.Value > 0)
                        {
                            var woi = db.WorkOrderItems
                                .Where(w => w.Id == workOrderItemId.Value)
                                .Select(w => new { w.MachineId, w.CompId })
                                .FirstOrDefault();
                            if (woi != null)
                            {
                                machineId = woi.MachineId;
                                compId = woi.CompId;
                            }
                        }

                        // Stitches per piece — same resolution as the
                        // WorkOrderItems dropdown: latest Gen_ComponentSpecs.Stitch
                        // for the component, fall back to summing the design's
                        // component specs when no CompId is set.
                        int stitchesPerPiece = 0;
                        if (compId.HasValue && compId.Value > 0)
                        {
                            stitchesPerPiece = db.Gen_ComponentSpecs
                                .Where(s => s.ComponentId == compId.Value
                                            && s.IsLatest
                                            && s.Deleted == false
                                            && s.Stitch.HasValue)
                                .Select(s => s.Stitch.Value)
                                .FirstOrDefault();
                        }
                        else if (x.DesignId.HasValue && x.DesignId.Value > 0)
                        {
                            stitchesPerPiece = (
                                from c in db.Gen_Components
                                join s in db.Gen_ComponentSpecs on c.Id equals s.ComponentId
                                where c.D_Code == x.DesignId.Value
                                      && c.Deleted != true
                                      && s.IsLatest
                                      && s.Deleted == false
                                      && s.Stitch.HasValue
                                select s.Stitch.Value
                            ).DefaultIfEmpty(0).Sum();
                        }

                        return new
                        {
                            qcInspectionId = x.Id,
                            qcNo,
                            workOrderId = x.WorkOrderId,
                            workOrderLabel = woLabel,
                            workOrderItemId,
                            productId = x.ProductId,
                            productName,
                            designId = x.DesignId,
                            designName,
                            reworkQty,
                            machineId,
                            stitchesPerPiece,
                            name = qcNo + " | " + woLabel + " | " + productName + " | Rework: " + reworkQty
                        };
                    })
                    .OrderBy(x => x.qcNo)
                    .ToList();

                return Json(new { success = true, data = rows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Json(new
                {
                    success = false,
                    message = "Failed to load rework tasks.",
                    detail = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet, Route("operators")]
        public ActionResult Operators()
        {
            var list = db.Adm_Users
                .Where(x => (x.Deleted == false || x.Deleted == null) && (x.Active == true))
                .Select(x => new
                {
                    id = x.Id,

                    name = x.EmployeeId != null
                        ? ("EMP-" + x.EmployeeId + " - " + x.UserName)
                        : x.UserName
                })
                .OrderBy(x => x.name)
                .ToList();

            return Json(new { data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("daily-productions")]
        public ActionResult DailyProductions(bool includeAll = false)
        {
            // Phase Q.3 - Pending productions only by default.
            // The truth source for "pending" is QC_Inspection: if a non-reversed,
            // non-cancelled QC inspection exists for this production (whether posted
            // or not), the production is considered closed for stop logging.
            // Pass ?includeAll=true to bypass this filter for places that need history.
            var q = db.DailyProduction
                .Include("WorkOrderItems")
                .Include("WorkOrderItems.WorkOrders")
                .Where(x => x.Deleted == false || x.Deleted == null);

            if (!includeAll)
            {
                // Productions that already have an active QC inspection
                var qcLockedIds = db.QC_Inspection
                    .Where(qc => qc.ProductionId.HasValue
                              && qc.Deleted != true
                              && qc.Status != "Reversed"
                              && qc.Status != "Cancelled")
                    .Select(qc => qc.ProductionId.Value)
                    .Distinct()
                    .ToList();

                q = q.Where(x =>
                    (x.IsReversed == null || x.IsReversed == false) &&
                    (x.IsSentToQC == null || x.IsSentToQC == false) &&
                    !qcLockedIds.Contains(x.Id));
            }

            var list = q
                .OrderByDescending(x => x.Id)
                .ToList()
                .Select(x => new
                {
                    id = x.Id,
                    name =
                        "DP-" + x.Id +
                        (
                            x.WorkOrderItems != null && x.WorkOrderItems.WorkOrders != null
                                ? " / " + x.WorkOrderItems.WorkOrders.WorkOrderCode
                                : x.WorkOrderItems != null
                                    ? " / WO-" + x.WorkOrderItems.WorkOrderId
                                    : ""
                        )
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = list
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("work-order-items-basic")]
        public ActionResult WorkOrderItemsBasic()
        {
            var list = db.WorkOrderItems
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    name = "WOI-" + x.Id + " / WO-" + x.WorkOrderId
                })
                .ToList();

            return Json(new { success = true, data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("workorders-all")]
        public ActionResult GetAllWorkOrders()
        {
            var data = db.WorkOrders
                .Where(x => (x.Deleted == false || x.Deleted == null))
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    id = x.Id,
                    text = x.WorkOrderCode 
                })
                .ToList();

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("stock-items")]
        public ActionResult StockItems(int? branchId = null, string search = "")
        {
            var q = db.Gen_Stocks
                .Include("Gen_Products")
                .Include("Branches")
                .Where(s => (s.Deleted == false || s.Deleted == null))
                .Where(s => (s.CurrentQty ?? 0) > 0);

            if (branchId.HasValue && branchId.Value > 0)
                q = q.Where(s => s.BranchId == branchId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                q = q.Where(s =>
                    (s.Gen_Products != null && s.Gen_Products.Prod_Name.Contains(search)) ||
                    (s.Branches != null && s.Branches.Name.Contains(search)));
            }

            var items = q.Select(s => new
            {
                stockId = s.Id,
                productId = s.ProdId,
                productName = s.Gen_Products != null ? s.Gen_Products.Prod_Name : "-",
                branchId = s.BranchId,
                branchName = s.Branches != null ? s.Branches.Name : "-",
                availableQty = s.CurrentQty ?? 0,
                unit = s.Unit ?? "PCS"
            }).ToList();

            return Json(new { success = true, data = items }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("departments")]
        public ActionResult Departments()
        {
            var list = db.Adm_Departments
                .Where(x => x.Active == true && x.Deleted == false)
                .OrderBy(x => x.DepartmentName)
                .Select(x => new
                {
                    x.Id,
                    x.DepartmentName
                })
                .ToList();

            return Json(new { success = true, data = list }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("raw-items")]
        public ActionResult RawItems()
        {
            var rows = db.Gen_RawItems
                .Include("Gen_Units")
                .Where(x => x.Deleted != true && x.Active == true)
                .OrderBy(x => x.Type)
                .ThenBy(x => x.ItemName)
                .Select(x => new
                {
                    x.Id,
                    x.ItemName,
                    x.Type,
                    x.UnitId,
                    UnitName = x.Gen_Units != null ? x.Gen_Units.UnitName : null
                })
                .ToList();

            var list = rows.Select(x =>
            {
                var type = (x.Type ?? "").Trim().ToUpper();
                var itemName = (x.ItemName ?? "").Trim();
                var unitName = string.IsNullOrWhiteSpace(x.UnitName)
                    ? "PCS"
                    : x.UnitName.Trim();

                var displayName = type + " - " + itemName + " (" + unitName + ")";

                return new
                {
                    id = x.Id,
                    name = displayName,
                    text = displayName,
                    itemName = itemName,
                    type = type,
                    unitId = x.UnitId,
                    unitName = unitName
                };
            }).ToList();

            return Json(new
            {
                success = true,
                data = list
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("~/emb-products/dropdowns/designs")]
        public ActionResult Designs()
        {
            var items = db.Gen_Design
                .Where(x => x.Deleted != true && x.Active == true)
                .OrderBy(x => x.DNo)
                .Select(x => new
                {
                    id = x.Id,

                    designCode = x.DNo,
                    designName = x.CollectionName,

                    text = x.DNo + " - " + x.CollectionName,
                    name = x.DNo + " - " + x.CollectionName
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = items
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("designs-by-product")]
        public ActionResult DesignsByProduct(int productId)
        {
            try
            {
                var list = db.Gen_Design
                    .Where(x =>
                        x.ProductId == productId &&
                        x.Deleted != true &&
                        (x.Active == true || x.Active == null))
                    .OrderBy(x => x.CollectionName)
                    .Select(x => new
                    {
                        id = x.Id,
                        code = x.D_Code,
                        dNo = x.DNo,
                        name = x.CollectionName,
                        text = x.CollectionName
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = list
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    data = new object[] { }
                }, JsonRequestBehavior.AllowGet);
            }
        }


        [HttpGet]
        [Route("units")]
        public ActionResult Units()
        {
            var data = db.Gen_Units
                .Where(x => x.Deleted != true && x.Active == true)
                .OrderBy(x => x.UnitName)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.UnitName
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = data
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("qc-inspections")]
        public ActionResult QCInspections()
        {
            var list = db.QC_Inspection
                .Where(x => x.Deleted == false || x.Deleted == null)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    Id = x.Id,
                    Text = x.QCNo
                })
                .ToList();

            return JsonSuccess("QC inspections loaded.", list);
        }

    }
}
       
