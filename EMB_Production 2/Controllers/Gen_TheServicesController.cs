using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using OfficeOpenXml;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PagedList;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/services")]
    public class GenTheServicesController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Services)
        // ─────────────────────────────────────────────────────────────
        private void LogSvcAudit(int id, string action,
                                 string oldValue = null, string newValue = null,
                                 string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_TheServicesAuditLog
       (ServiceId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@sid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@sid",  id),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@uid",  GetSafeCurrentUserId()),
                    new SqlParameter("@when", DateTime.Now),
                    new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
            }
            catch { }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var entries = db.Database.SqlQuery<SvcAuditEntry>(@"
SELECT  a.Id, a.ServiceId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_TheServicesAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.ServiceId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, serviceId = e.ServiceId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeSvcError(ex) });
            }
        }

        private class SvcAuditEntry
        {
            public int Id { get; set; }
            public int ServiceId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeSvcError(Exception ex)
        {
            if (ex == null) return "Unknown error.";
            var dbValEx = ex as DbEntityValidationException;
            if (dbValEx != null)
            {
                var firstError = dbValEx.EntityValidationErrors
                    .SelectMany(e => e.ValidationErrors).FirstOrDefault();
                if (firstError != null)
                    return firstError.PropertyName + ": " + firstError.ErrorMessage;
            }
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS (Services)
        // ─────────────────────────────────────────────────────────────
        public class SvcBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkSvc(List<int> ids, Func<int, Gen_TheServices, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_TheServices.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.Service_Name ?? ("SVC#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeSvcError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(SvcBulkIdsDto dto)
        {
            var data = DoBulkSvc(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogSvcAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(SvcBulkIdsDto dto)
        {
            var data = DoBulkSvc(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogSvcAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(SvcBulkIdsDto dto)
        {
            var data = DoBulkSvc(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogSvcAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // ============================================================
        // LIST + SEARCH + FILTER + PAGING
        // ============================================================
        [HttpGet]
        [Route("list")]
        public ActionResult List(string search = "", string activeFilter = "",
                          int page = 1, int pageSize = 10,
                          int? branchFilter = null, int? serviceTypeFilter = null)
        {
            var query =
                from x in db.Gen_TheServices
                where (x.Deleted == false || x.Deleted == null)
                select new ServiceRowViewModel
                {
                    Id = x.Id,
                    ServiceTypeId = x.Service_Type,
                    ServiceTypeName = x.Service_Type.HasValue
                    ? x.Service_Type.Value.ToString()
                    : "-",

                    ServiceName = x.Service_Name,
                    ServiceRateCharge = x.Service_RateCharge,
                    MachineDevice = x.Machine_Device,
                    MaxRange = x.Max_Range,
                    MaxCharges = x.Max_Charges,
                    MinCharges = x.Min_Charges,
                    Field1 = x.Field1,
                    Field2 = x.Field2,

                    BranchId = x.BranchId,
                    BranchName = x.Branches != null ? x.Branches.Name : "",   

                    RecordTimeStamp = x.RecordTimeStamp,
                    Active = x.Active ?? false,
                    Deleted = x.Deleted ?? false,

                    CreatedBy = x.CreatedBy,
                    CreatedDate = x.CreatedDate,
                    UpdatedBy = x.UpdatedBy,
                    UpdatedDate = x.UpdatedDate
                };

            // FILTERS
            if (!string.IsNullOrEmpty(search))
                query = query.Where(x => x.ServiceName.Contains(search));

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);

            if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false);

            if (branchFilter != null)
                query = query.Where(x => x.BranchId == branchFilter);

            if (serviceTypeFilter != null)
                query = query.Where(x => x.ServiceTypeId == serviceTypeFilter);

            // PAGINATION
            var total = query.Count();

            // Phase R.7 - summary aggregates across filtered set
            var summarySource = query.Select(x => new
            {
                x.Active,
                x.ServiceTypeId,
                x.BranchId,
                x.MachineDevice,
                x.ServiceRateCharge,
                x.MaxCharges,
                x.MinCharges,
                x.BranchName
            }).ToList();

            int activeCount = summarySource.Count(s => s.Active);
            int inactiveCount = summarySource.Count(s => !s.Active);
            int distinctTypes = summarySource.Where(s => s.ServiceTypeId != null).Select(s => s.ServiceTypeId).Distinct().Count();
            int distinctMachines = summarySource.Where(s => s.MachineDevice != null).Select(s => s.MachineDevice).Distinct().Count();
            int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();
            decimal avgRate = summarySource.Any() ? (decimal)(summarySource.Average(s => s.ServiceRateCharge ?? 0)) : 0m;
            decimal avgMax = summarySource.Any() ? summarySource.Average(s => s.MaxCharges ?? 0) : 0m;
            decimal avgMin = summarySource.Any() ? summarySource.Average(s => s.MinCharges ?? 0) : 0m;

            var byBranch = summarySource
                .Where(s => !string.IsNullOrWhiteSpace(s.BranchName))
                .GroupBy(s => s.BranchName)
                .Select(g => new { branch = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var summary = new
            {
                count = total,
                activeCount,
                inactiveCount,
                distinctTypes,
                distinctMachines,
                distinctBranches,
                avgRate = Math.Round(avgRate, 2),
                avgMax = Math.Round(avgMax, 2),
                avgMin = Math.Round(avgMin, 2),
                byBranch
            };

            var data = query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Json(new
            {
                success = true,
                data = new
                {
                    items = data,
                    summary,
                    totalItems = total
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // ============================================================
        // Phase R.7 - PROFILE
        // ============================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var s = db.Gen_TheServices.FirstOrDefault(x => x.Id == id);
                if (s == null) return JsonError("Service not found.");

                var serviceType = s.Service_Type.HasValue
                    ? db.Gen_ServiceType.FirstOrDefault(t => t.Id == s.Service_Type.Value)
                    : null;
                var machine = s.Machine_Device.HasValue
                    ? db.Gen_Machines.FirstOrDefault(m => m.Id == s.Machine_Device.Value)
                    : null;

                var header = new
                {
                    id = s.Id,
                    serviceName = s.Service_Name,
                    serviceTypeId = s.Service_Type,
                    serviceTypeName = serviceType != null ? serviceType.Service_Type : "",
                    serviceTypeDescription = serviceType != null ? serviceType.Service_TypeDescription : "",
                    serviceRateCharge = s.Service_RateCharge,
                    machineDevice = s.Machine_Device,
                    machineCode = machine != null ? machine.Code : "",
                    maxRange = s.Max_Range,
                    maxCharges = s.Max_Charges,
                    minCharges = s.Min_Charges,
                    field1 = s.Field1,
                    field2 = s.Field2,
                    branchId = s.BranchId,
                    branchName = s.Branches != null ? s.Branches.Name : "",
                    active = s.Active ?? false,
                    createdDateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = s.UpdatedDate.HasValue ? s.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Linked service rates
                var rates = (
                    from r in db.Gen_ServiceRates
                    join p in db.Gen_Products on r.ProductId equals p.Id into pj
                    from p in pj.DefaultIfEmpty()
                    where r.Service_Id == id && (r.Deleted == false || r.Deleted == null)
                    orderby r.Id descending
                    select new
                    {
                        r.Id,
                        productCode = p != null ? p.Prod_Code : "",
                        productName = p != null ? p.Prod_Name : "",
                        chargeFactor = r.Charge_Fector,
                        r.Remarks,
                        Active = r.Active ?? false
                    }
                ).Take(50).ToList();

                // Sibling services for the same type
                var siblings = (
                    from x in db.Gen_TheServices
                    where x.Service_Type == s.Service_Type
                       && x.Id != id
                       && (x.Deleted == false || x.Deleted == null)
                    orderby x.Id descending
                    select new
                    {
                        x.Id,
                        x.Service_Name,
                        x.Service_RateCharge,
                        Active = x.Active ?? false
                    }
                ).Take(20).ToList();

                var kpis = new
                {
                    rateCharge = s.Service_RateCharge,
                    maxCharges = s.Max_Charges,
                    minCharges = s.Min_Charges,
                    linkedRates = rates.Count(),
                    typeSiblings = siblings.Count()
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    rates,
                    siblings
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }


        // ============================================================
        // DETAIL
        // ============================================================
        [HttpGet]
        [Route("detail/{id}")]
        public ActionResult Detail(int id)
        {
            var model =
                (from x in db.Gen_TheServices

                 join u in db.Adm_Users on x.CreatedBy equals u.Id into cu
                 from cuu in cu.DefaultIfEmpty()

                 join emp in db.Adm_Users on cuu.EmployeeId equals emp.Id into ce
                 from cee in ce.DefaultIfEmpty()

                 where x.Id == id && (x.Deleted == false || x.Deleted == null)

                 select new
                 {
                     Id = x.Id,

                     ServiceTypeId = x.Service_Type,
                     ServiceType = x.Gen_ServiceType.Service_Type,
                     ServiceTypeName = x.Gen_ServiceType.Service_TypeDescription,

                     ServiceName = x.Service_Name,
                     ServiceRateCharge = x.Service_RateCharge,
                     MachineDevice = x.Machine_Device,

                     MaxRange = x.Max_Range,
                     MaxCharges = x.Max_Charges,
                     MinCharges = x.Min_Charges,

                     Field1 = x.Field1,
                     Field2 = x.Field2,

                     BranchId = x.BranchId,
                     BranchName = x.Branches.Name,

                     RecordTimeStamp = x.RecordTimeStamp,

                     Active = x.Active,
                     Deleted = x.Deleted,

            
                    CreatedBy = x.CreatedBy,

                     CreatedDate = x.CreatedDate,

                     UpdatedBy = x.UpdatedBy,
                     UpdatedDate = x.UpdatedDate
                 })
                .FirstOrDefault();

            return Json(new { success = true, data = model }, JsonRequestBehavior.AllowGet);
        }


        // ============================================================
        // CREATE
        // ============================================================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(ServiceDto dto)
        {
            try
            {
                int userId = GetCurrentUserId();   // ✅ save USER ID
                int branchId = GetCurrentBranchId();

                var model = new Gen_TheServices
                {
                    Service_Type = dto.serviceTypeId,
                    Service_Name = dto.serviceName,
                    Service_RateCharge = dto.serviceRateCharge,
                    Machine_Device = dto.machineDevice,
                    Max_Range = dto.maxRange,
                    Max_Charges = dto.maxCharges,
                    Min_Charges = dto.minCharges,
                    Field1 = dto.field1,
                    Field2 = dto.field2,
                    BranchId = dto.branchId ?? branchId,

                    RecordTimeStamp = DateTime.Now,

                    Active = dto.active,
                    Deleted = false,

                    // ⭐ FIX
                    CreatedBy = userId,
                    CreatedDate = DateTime.Now
                };

                db.Gen_TheServices.Add(model);
                db.SaveChanges();

                LogSvcAudit(model.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: (model.Service_Name ?? "-") + " · Type #" + dto.serviceTypeId);

                return Json(new { success = true, message = "Service created successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Create failed. " + DescribeSvcError(ex) });
            }
        }


        // ============================================================
        // UPDATE
        // ============================================================
        [HttpPost]
        [Route("update")]
        public ActionResult Update(ServiceDto dto)
        {
            try
            {
                var model = db.Gen_TheServices.FirstOrDefault(x => x.Id == dto.id && x.Deleted != true);

                if (model == null)
                    return Json(new { success = false, message = "Record not found." });

                int userId = GetCurrentUserId();

                model.Service_Type = dto.serviceTypeId;
                model.Service_Name = dto.serviceName;
                model.Service_RateCharge = dto.serviceRateCharge;
                model.Machine_Device = dto.machineDevice;
                model.Max_Range = dto.maxRange;
                model.Max_Charges = dto.maxCharges;
                model.Min_Charges = dto.minCharges;
                model.Field1 = dto.field1;
                model.Field2 = dto.field2;
                model.BranchId = dto.branchId;
                model.Active = dto.active;

                model.RecordTimeStamp = DateTime.Now;

                // ⭐ FIX
                model.UpdatedBy = userId;
                model.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                LogSvcAudit(model.Id, "EDITED",
                    oldValue: null, newValue: model.Active == true ? "Active" : "Inactive",
                    detail: model.Service_Name ?? "-");

                return Json(new { success = true, message = "Service updated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Update failed. " + DescribeSvcError(ex) });
            }
        }

        // ============================================================
        // DELETE
        // ============================================================
        [HttpPost]
        [Route("delete")]
        public ActionResult Delete(int id)
        {
            var model = db.Gen_TheServices.FirstOrDefault(x => x.Id == id);

            if (model == null)
                return Json(new { success = false, message = "Not found" });

            model.Deleted = true;
            model.Active = false;
            model.UpdatedDate = DateTime.Now;
            model.UpdatedBy = GetCurrentUserId();
            model.RecordTimeStamp = DateTime.Now;

            db.SaveChanges();

            LogSvcAudit(id, "DELETED", "Active", "Deleted");

            return Json(new { success = true, message = "Deleted successfully" });
        }


        private const int PageSize = 10;

        // GET: Index with inline create/edit, filtering, and paging
        public ActionResult Index(
            int? page,
            string search,
            string activeFilter,
            int? serviceTypeFilter,
            int? branchFilter,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null)
        {
            // Base query
            var query = db.Gen_TheServices
                          .Include(s => s.Gen_ServiceType)
                          .Include(s => s.Branches)
                          .AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s => s.Service_Name.Contains(search));
            if (activeFilter == "Yes")
                query = query.Where(s => s.Active == true);
            else if (activeFilter == "No")
                query = query.Where(s => s.Active == false);
            if (serviceTypeFilter.HasValue)
                query = query.Where(s => s.Service_Type == serviceTypeFilter.Value);
            if (branchFilter.HasValue)
                query = query.Where(s => s.BranchId == branchFilter.Value);

            // Project to row view models
            var rows = query
                .OrderBy(s => s.Id)
                .Select(s => new ServiceRowViewModel
                {
                    Id = s.Id,
                    ServiceTypeId = s.Service_Type,
                    ServiceTypeName = s.Gen_ServiceType.Service_Type,
                    ServiceName = s.Service_Name,
                    ServiceRateCharge = s.Service_RateCharge,
                    MachineDevice = s.Machine_Device,
                    MaxRange = s.Max_Range,
                    MaxCharges = s.Max_Charges,
                    MinCharges = s.Min_Charges,
                    Field1 = s.Field1,
                    Field2 = s.Field2,
                    BranchId = s.BranchId,
                    BranchName = s.Branches != null ? s.Branches.Name : "",
                    RecordTimeStamp = s.RecordTimeStamp,
                    Active = s.Active ?? false,
                    Deleted = s.Deleted ?? false,
                    CreatedBy = s.CreatedBy,
                    CreatedDate = s.CreatedDate,
                    UpdatedBy = s.UpdatedBy,
                    UpdatedDate = s.UpdatedDate
                });

            // Prepare view model
            var vm = new ServicesPageViewModel
            {
                Search = search,
                ActiveFilter = activeFilter,
                ServiceTypeFilter = serviceTypeFilter,
                BranchFilter = branchFilter,
                Page = page ?? 1,
                Rows = rows.ToPagedList(page ?? 1, PageSize),
                ServiceTypeList = new SelectList(db.Gen_ServiceType, "Id", "Service_Type"),
                BranchList = new SelectList(db.Branches, "Id", "Name"),
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                NewService = new ServiceRowViewModel(),
                EditService = showEdit && editId.HasValue
                    ? rows.FirstOrDefault(r => r.Id == editId.Value)
                    : new ServiceRowViewModel()
            };

            return View(vm);
        }
        // POST: Create inline
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(ServiceRowViewModel NewService)
        {
            if (ModelState.IsValid)
            {
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                var entity = new Gen_TheServices
                {
                    Service_Type = NewService.ServiceTypeId,
                    Service_Name = NewService.ServiceName,
                    Service_RateCharge = NewService.ServiceRateCharge,
                    Machine_Device = NewService.MachineDevice,
                    Max_Range = NewService.MaxRange,
                    Max_Charges = NewService.MaxCharges,
                    Min_Charges = NewService.MinCharges,
                    Field1 = NewService.Field1,
                    Field2 = NewService.Field2,
                    BranchId = NewService.BranchId,
                    Active = NewService.Active,
                    Deleted = NewService.Deleted,
                    RecordTimeStamp = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    CreatedBy = userId,
                    UpdatedBy = userId
                };
                db.Gen_TheServices.Add(entity);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // POST: Edit inline
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(ServiceRowViewModel EditService)
        {
            if (ModelState.IsValid)
            {
                var entity = db.Gen_TheServices.Find(EditService.Id);
                if (entity != null)
                {
                    entity.Service_Type = EditService.ServiceTypeId;
                    entity.Service_Name = EditService.ServiceName;
                    entity.Service_RateCharge = EditService.ServiceRateCharge;
                    entity.Machine_Device = EditService.MachineDevice;
                    entity.Max_Range = EditService.MaxRange;
                    entity.Max_Charges = EditService.MaxCharges;
                    entity.Min_Charges = EditService.MinCharges;
                    entity.Field1 = EditService.Field1;
                    entity.Field2 = EditService.Field2;
                    entity.BranchId = EditService.BranchId;
                    entity.Active = EditService.Active;
                    entity.Deleted = EditService.Deleted;
                    entity.UpdatedDate = DateTime.Now;
                    entity.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                    db.SaveChanges();
                }
            }
            return RedirectToAction("Index");
        }
        // Only these business fields may be touched through the generic
        // field-update path below. Id, soft-delete, branch and audit
        // columns (Deleted, Active, BranchId, CreatedBy, CreatedDate,
        // UpdatedBy, UpdatedDate, RecordTimeStamp) are deliberately left
        // out — letting a caller set those via raw reflection would bypass
        // soft-delete, branch scoping and audit trail integrity.
        private static readonly HashSet<string> AllowedServiceFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Service_Type",
            "Service_Name",
            "Service_RateCharge",
            "Machine_Device",
            "Max_Range",
            "Max_Charges",
            "Min_Charges",
            "Field1",
            "Field2"
        };

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateGenTheServiceField(int id, string fieldName, string newValue)
        {
            // 1) Auth check
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            if (string.IsNullOrWhiteSpace(fieldName) || !AllowedServiceFields.Contains(fieldName))
                return Json(new { success = false, message = "Invalid field." });

            // 2) Load the Gen_TheServices record
            var entity = db.Gen_TheServices.Find(id);
            if (entity == null)
                return Json(new { success = false, message = "Service not found." });

            // 3) Reflectively locate the property
            var prop = typeof(Gen_TheServices).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                object converted;
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType)
                                 ?? prop.PropertyType;

                // 4) Convert the incoming string to the correct .NET type
                if (targetType == typeof(string))
                {
                    converted = newValue;
                }
                else if (targetType == typeof(int))
                {
                    converted = int.TryParse(newValue, out var i) ? (int?)i : null;
                }
                else if (targetType == typeof(decimal))
                {
                    converted = decimal.TryParse(newValue, out var d) ? (decimal?)d : null;
                }
                else if (targetType == typeof(bool))
                {
                    converted = (newValue ?? "")
                        .Trim()
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (targetType == typeof(DateTime))
                {
                    converted = DateTime.TryParse(newValue, out var dt) ? (DateTime?)dt : null;
                }
                else
                {
                    return Json(new { success = false, message = $"Unsupported type {targetType.Name}" });
                }

                // 5) Apply the new value and stamp audit fields
                prop.SetValue(entity, converted);
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                entity.UpdatedBy = userId;
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Details
        public ActionResult Details(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var s = db.Gen_TheServices.Find(id.Value);
            if (s == null) return HttpNotFound();
            return View(s);
        }

        // GET: Delete confirmation
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var s = db.Gen_TheServices.Find(id.Value);
            if (s == null) return HttpNotFound();
            return View(s);
        }

        // POST: Delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var s = db.Gen_TheServices.Find(id);
            if (s != null)
            {
                db.Gen_TheServices.Remove(s);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpGet,Obsolete ]
        public ActionResult ExportToExcel(int? id, string search, string activeFilter, int? serviceTypeFilter, int? branchFilter)
        {           
            if (id.HasValue)
            {
                // detail export
                var s = db.Gen_TheServices
                          .Include(e => e.Gen_ServiceType)
                          .Include(e => e.Branches)
                          .Include(e => e.Gen_Machines)
                          .Include(e => e.Gen_ServiceRates)
                          .FirstOrDefault(e => e.Id == id.Value);
                if (s == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("ServiceDetails");
                    int row = 1;
                    ws.Cells[row, 1].Value = "Gen_ServiceType";
                    ws.Cells[row++, 2].Value = s.Gen_ServiceType?.Service_Type;
                    ws.Cells[row, 1].Value = "Branch";
                    ws.Cells[row++, 2].Value = s.Branches?.Name;
                    ws.Cells[row, 1].Value = "Gen_Machines";
                    ws.Cells[row++, 2].Value = s.Gen_Machines?.Count;
                    ws.Cells[row, 1].Value = "Gen_ServiceRates";
                    ws.Cells[row++, 2].Value = s.Gen_ServiceRates?.Count;
                    ws.Cells[row, 1].Value = "RecordTimeStamp";
                    ws.Cells[row++, 2].Value = s.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cells[row, 1].Value = "Active";
                    ws.Cells[row++, 2].Value = s.Active == true ? "Yes" : "No";
                    ws.Cells[row, 1].Value = "Deleted";
                    ws.Cells[row++, 2].Value = s.Deleted == true ? "Yes" : "No";
                    ws.Cells[row, 1].Value = "CreatedBy";
                    ws.Cells[row++, 2].Value = s.CreatedBy;
                    ws.Cells[row, 1].Value = "CreatedDate";
                    ws.Cells[row++, 2].Value = s.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cells[row, 1].Value = "UpdatedBy";
                    ws.Cells[row++, 2].Value = s.UpdatedBy;
                    ws.Cells[row, 1].Value = "UpdatedDate";
                    ws.Cells[row, 2].Value = s.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cells[1, 1, row, 2].AutoFitColumns();

                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                $"Service_{id.Value}_Details.xlsx");
                }
            }
            else
            {
                // bulk export
                var query = db.Gen_TheServices.Include(e => e.Gen_ServiceType).Include(e => e.Branches).AsQueryable();
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => s.Service_Name.Contains(search));
                if (activeFilter == "Yes") query = query.Where(s => s.Active == true);
                else if (activeFilter == "No") query = query.Where(s => s.Active == false);
                if (serviceTypeFilter.HasValue) query = query.Where(s => s.Service_Type == serviceTypeFilter.Value);
                if (branchFilter.HasValue) query = query.Where(s => s.BranchId == branchFilter.Value);
                var list = query.ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Services");
                    // headers
                    var headers = new[] { "ID", "Type", "Name", "RateCharge", "Device", "Branch" };
                    for (int i = 0; i < headers.Length; i++)
                        ws.Cells[1, i + 1].Value = headers[i];

                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i];
                        int r = i + 2;
                        ws.Cells[r, 1].Value = s.Id;
                        ws.Cells[r, 2].Value = s.Gen_ServiceType?.Service_Type;
                        ws.Cells[r, 3].Value = s.Service_Name;
                        ws.Cells[r, 4].Value = s.Service_RateCharge;
                        ws.Cells[r, 5].Value = s.Machine_Device;
                        ws.Cells[r, 6].Value = s.Branches?.Name;
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                "Services_All.xlsx");
                }
            }
        }

        public ActionResult ExportToPdf(int? id, string search, string activeFilter, int? serviceTypeFilter, int? branchFilter)
        {
            if (id.HasValue)
            {
                // detail export
                var s = db.Gen_TheServices
                          .Include(e => e.Gen_ServiceType)
                          .Include(e => e.Branches)
                          .Include(e => e.Gen_Machines)
                          .Include(e => e.Gen_ServiceRates)
                          .FirstOrDefault(e => e.Id == id.Value);
                if (s == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 100 };
                    table.AddCell(new PdfPCell(new Phrase("Field")) { BackgroundColor = BaseColor.LIGHT_GRAY });
                    table.AddCell(new PdfPCell(new Phrase("Value")) { BackgroundColor = BaseColor.LIGHT_GRAY });

                    void AddRow(string field, string value)
                    {
                        table.AddCell(new Phrase(field));
                        table.AddCell(new Phrase(value));
                    }

                    AddRow("Gen_ServiceType", s.Gen_ServiceType?.Service_Type);
                    AddRow("Branch", s.Branches?.Name);
                    AddRow("Gen_Machines", s.Gen_Machines?.Count.ToString() ?? "0");
                    AddRow("Gen_ServiceRates", s.Gen_ServiceRates?.Count.ToString() ?? "0");
                    AddRow("RecordTimeStamp", s.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
                    AddRow("Active", s.Active == true ? "Yes" : "No");
                    AddRow("Deleted", s.Deleted == true ? "Yes" : "No");
                    AddRow("CreatedBy", s.CreatedBy?.ToString() ?? string.Empty);
                    AddRow("CreatedDate", s.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
                    AddRow("UpdatedBy", s.UpdatedBy?.ToString() ?? string.Empty);
                    AddRow("UpdatedDate", s.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"Service_{id.Value}_Details.pdf");
                }
            }
            else
            {
                // bulk export
                var query = db.Gen_TheServices.Include(e => e.Gen_ServiceType).Include(e => e.Branches).AsQueryable();
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(s => s.Service_Name.Contains(search));
                if (activeFilter == "Yes") query = query.Where(s => s.Active == true);
                else if (activeFilter == "No") query = query.Where(s => s.Active == false);
                if (serviceTypeFilter.HasValue) query = query.Where(s => s.Service_Type == serviceTypeFilter.Value);
                if (branchFilter.HasValue) query = query.Where(s => s.BranchId == branchFilter.Value);
                var list = query.ToList();

                using (var ms = new MemoryStream())
                {
                    var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(6) { WidthPercentage = 100 };
                    var headers = new[] { "ID", "Type", "Name", "Rate", "Device", "Branch" };
                    foreach (var h in headers)
                        table.AddCell(new PdfPCell(new Phrase(h)) { BackgroundColor = BaseColor.LIGHT_GRAY });

                    foreach (var s in list)
                    {
                        table.AddCell(s.Id.ToString());
                        table.AddCell(s.Gen_ServiceType?.Service_Type);
                        table.AddCell(s.Service_Name);
                        table.AddCell(s.Service_RateCharge?.ToString() ?? string.Empty);
                        table.AddCell(s.Machine_Device?.ToString() ?? string.Empty);
                        table.AddCell(s.Branches?.Name);
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", "Services_All.pdf");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
