using System.Data.Entity;  // for Include(...)
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;
using PagedList;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using System;
using System.Collections.Generic;
using System.Net;
using OfficeOpenXml;
using System.IO;
using iTextSharp.text.pdf;
using iTextSharp.text;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/machines")]
    public class Gen_MachinesController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Machines)
        // ─────────────────────────────────────────────────────────────
        private void LogMachineAudit(int id, string action,
                                     string oldValue = null, string newValue = null,
                                     string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_MachinesAuditLog
       (MachineId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@mid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@mid",  id),
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
                var entries = db.Database.SqlQuery<MachineAuditEntry>(@"
SELECT  a.Id, a.MachineId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_MachinesAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.MachineId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, machineId = e.MachineId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();

                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeMachineError(ex) });
            }
        }

        private class MachineAuditEntry
        {
            public int Id { get; set; }
            public int MachineId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeMachineError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Machines)
        // ─────────────────────────────────────────────────────────────
        public class MachineBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkMachine(List<int> ids, Func<int, Gen_Machines, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_Machines.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.Code ?? ("MC#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeMachineError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(MachineBulkIdsDto dto)
        {
            var data = DoBulkMachine(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentEmployeeId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogMachineAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(MachineBulkIdsDto dto)
        {
            var data = DoBulkMachine(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentEmployeeId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogMachineAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(MachineBulkIdsDto dto)
        {
            var data = DoBulkMachine(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetCurrentEmployeeId();
                e.UpdatedDate = DateTime.Now;
                e.RecordTimeStamp = DateTime.Now;
                db.SaveChanges();
                LogMachineAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // ============================================================
        // LIST
        // ============================================================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
    string search = "",
    string activeFilter = "",
    int? brandFilter = null,
    int? serviceFilter = null,
    int? branchFilter = null,
    int page = 1,
    int pageSize = 10)
        {
            var query = db.Gen_Machines.Where(x => x.Deleted == false || x.Deleted == null);

            // SEARCH
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                       (x.Code ?? "").Contains(search)
                    || (x.Serial_Number ?? "").Contains(search)
                    || (x.Gen_Brands.BrandName).Contains(search)
                    || (x.Gen_TheServices.Service_Name).Contains(search)
                );
            }

            // ACTIVE
            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            // BRAND
            if (brandFilter != null && brandFilter > 0)
                query = query.Where(x => x.BrandId == brandFilter);

            // SERVICE
            if (serviceFilter != null && serviceFilter > 0)
                query = query.Where(x => x.ServiceId == serviceFilter);

            // BRANCH
            if (branchFilter != null && branchFilter > 0)
                query = query.Where(x => x.BranchId == branchFilter);

            var totalItems = query.Count();

            // Phase R.4 - summary aggregates across filtered set
            var summarySource = query
                .Select(x => new
                {
                    x.BrandId,
                    x.ServiceId,
                    x.BranchId,
                    x.HeadsNum,
                    x.NeedlesNum,
                    Active = x.Active ?? false,
                    BrandName = x.Gen_Brands.BrandName,
                    BranchName = x.Branches.Name,
                    ServiceNext = x.ServiceNext
                })
                .ToList();

            int activeCount = summarySource.Count(s => s.Active);
            int inactiveCount = summarySource.Count(s => !s.Active);
            int distinctBrands = summarySource.Where(s => s.BrandId != null).Select(s => s.BrandId).Distinct().Count();
            int distinctServices = summarySource.Where(s => s.ServiceId != null).Select(s => s.ServiceId).Distinct().Count();
            int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();
            int totalHeads = summarySource.Sum(s => s.HeadsNum ?? 0);
            int totalNeedles = summarySource.Sum(s => s.NeedlesNum ?? 0);

            var now = DateTime.Now;
            int serviceDueSoon = summarySource.Count(s => s.ServiceNext.HasValue
                && s.ServiceNext.Value >= now
                && s.ServiceNext.Value <= now.AddDays(30));
            int serviceOverdue = summarySource.Count(s => s.ServiceNext.HasValue && s.ServiceNext.Value < now);

            var byBrand = summarySource
                .Where(s => !string.IsNullOrWhiteSpace(s.BrandName))
                .GroupBy(s => s.BrandName)
                .Select(g => new { brand = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var byBranch = summarySource
                .Where(s => !string.IsNullOrWhiteSpace(s.BranchName))
                .GroupBy(s => s.BranchName)
                .Select(g => new { branch = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var summary = new
            {
                count = totalItems,
                activeCount,
                inactiveCount,
                distinctBrands,
                distinctServices,
                distinctBranches,
                totalHeads,
                totalNeedles,
                serviceDueSoon,
                serviceOverdue,
                byBrand,
                byBranch
            };

            var list = query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var items = list.Select(r => new {
                r.Id,
                r.Code,
                BrandId = r.BrandId,
                BrandName = r.Gen_Brands?.BrandName ?? "",
                ServiceId = r.ServiceId,
                ServiceName = r.Gen_TheServices?.Service_Name ?? "",
                r.HeadtoHead,
                r.FrameHeight,
                r.HeadsNum,
                r.NeedlesNum,
                SerialNumber = r.Serial_Number,
                CallingNumber = r.Calling_Number,
                PurchaseDate = r.PurchaseDate?.ToString("yyyy-MM-dd"),
                Warranty = r.Warranty,
                ServiceEnd = r.ServiceEnd?.ToString("yyyy-MM-dd"),
                ServiceNext = r.ServiceNext?.ToString("yyyy-MM-dd"),
                BranchId = r.BranchId,
                BranchName = r.Branches?.Name ?? "",
                r.Active
            });

            return Json(new
            {
                success = true,
                data = new
                {
                    items,
                    summary,
                    totalItems
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // ============================================================
        // Phase R.4 - PROFILE
        // ============================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var x = db.Gen_Machines.FirstOrDefault(m => m.Id == id);
                if (x == null) return JsonError("Machine not found.");

                var header = new
                {
                    id = x.Id,
                    code = x.Code,
                    brandId = x.BrandId,
                    brandName = x.Gen_Brands != null ? x.Gen_Brands.BrandName : "",
                    serviceId = x.ServiceId,
                    serviceName = x.Gen_TheServices != null ? x.Gen_TheServices.Service_Name : "",
                    headtoHead = x.HeadtoHead,
                    frameHeight = x.FrameHeight,
                    headsNum = x.HeadsNum,
                    needlesNum = x.NeedlesNum,
                    serialNumber = x.Serial_Number,
                    callingNumber = x.Calling_Number,
                    purchaseDateText = x.PurchaseDate.HasValue ? x.PurchaseDate.Value.ToString("dd-MMM-yyyy") : null,
                    warranty = x.Warranty,
                    serviceEndText = x.ServiceEnd.HasValue ? x.ServiceEnd.Value.ToString("dd-MMM-yyyy") : null,
                    serviceNextText = x.ServiceNext.HasValue ? x.ServiceNext.Value.ToString("dd-MMM-yyyy") : null,
                    branchId = x.BranchId,
                    branchName = x.Branches != null ? x.Branches.Name : "",
                    active = x.Active ?? false,
                    createdDateText = x.CreatedDate.HasValue ? x.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = x.UpdatedDate.HasValue ? x.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Service status (Due soon / Overdue / OK)
                var nowDate = DateTime.Now;
                string serviceStatus = "OK";
                int? daysToService = null;
                if (x.ServiceNext.HasValue)
                {
                    var span = x.ServiceNext.Value - nowDate;
                    daysToService = (int)span.TotalDays;
                    if (daysToService < 0) serviceStatus = "OVERDUE";
                    else if (daysToService <= 30) serviceStatus = "DUE_SOON";
                }

                // Parameters trail
                var parameters = (
                    from p in db.Gen_MachineParameters
                    where p.MachineId == id && (p.Deleted == false || p.Deleted == null)
                    orderby p.Id descending
                    select new
                    {
                        p.Id,
                        p.HeadToHead,
                        p.FrameHeight,
                        Active = p.Active ?? false,
                        CreatedDate = p.CreatedDate
                    }
                ).ToList().Select(p => new
                {
                    p.Id,
                    p.HeadToHead,
                    p.FrameHeight,
                    p.Active,
                    createdDateText = p.CreatedDate.HasValue ? p.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : ""
                }).ToList();

                // Cost profile
                var cost = (
                    from c in db.MachineCostProfile
                    where c.MachineId == id && (c.Deleted == false || c.Deleted == null)
                    orderby c.Id descending
                    select new
                    {
                        c.Id,
                        c.HourlyRate,
                        c.PowerKW,
                        c.StitchPerMinute,
                        c.OperatorHourlyRate,
                        c.ElectricityUnitRate,
                        c.EffectiveFrom,
                        Active = c.Active ?? false
                    }
                ).FirstOrDefault();

                object costInfo = null;
                if (cost != null)
                {
                    var electricHourly = (cost.PowerKW ?? 0) * (cost.ElectricityUnitRate ?? 0);
                    var totalHourly = (cost.HourlyRate ?? 0) + (cost.OperatorHourlyRate ?? 0) + electricHourly;
                    costInfo = new
                    {
                        id = cost.Id,
                        hourlyRate = cost.HourlyRate,
                        powerKW = cost.PowerKW,
                        stitchPerMinute = cost.StitchPerMinute,
                        operatorHourlyRate = cost.OperatorHourlyRate,
                        electricityUnitRate = cost.ElectricityUnitRate,
                        effectiveFromText = cost.EffectiveFrom.HasValue ? cost.EffectiveFrom.Value.ToString("dd-MMM-yyyy") : "",
                        active = cost.Active,
                        electricHourly,
                        totalHourly
                    };
                }

                var kpis = new
                {
                    heads = x.HeadsNum ?? 0,
                    needles = x.NeedlesNum ?? 0,
                    parameterCount = parameters.Count(),
                    hasCostProfile = cost != null,
                    serviceStatus,
                    daysToService
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    parameters,
                    cost = costInfo
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }


        // ============================================================
        // Phase R.4 - NEXT CODE
        // ============================================================
        [HttpGet]
        [Route("next-code")]
        public ActionResult NextCode()
        {
            const string prefix = "MC-";
            int max = 0;
            var codes = db.Gen_Machines
                .Where(m => m.Code != null && m.Code.StartsWith(prefix))
                .Select(m => m.Code)
                .ToList();
            foreach (var c in codes)
            {
                if (string.IsNullOrEmpty(c)) continue;
                var tail = c.Substring(prefix.Length).Trim();
                int n;
                if (int.TryParse(tail, out n) && n > max)
                    max = n;
            }
            var next = (max + 1).ToString("D2");
            return JsonSuccess("OK", new { code = prefix + next });
        }

        // ============================================================
        // DETAIL
        // ============================================================
        [HttpGet]
        [Route("detail/{id}")]
        public ActionResult Detail(int id)
        {
            // STEP 1 — Load raw entity first (NO formatting inside LINQ!)
            var x = db.Gen_Machines
                .Where(m => m.Id == id)
                .FirstOrDefault();

            if (x == null)
                return JsonError("Record not found");

            // STEP 2 — Now format dates safely in memory
            var r = new
            {
                x.Id,
                x.Code,

                BrandId = x.BrandId,
                BrandName = x.Gen_Brands?.BrandName,

                ServiceId = x.ServiceId,
                ServiceName = x.Gen_TheServices?.Service_Name,

                x.HeadtoHead,
                x.FrameHeight,
                x.HeadsNum,
                x.NeedlesNum,
                SerialNumber = x.Serial_Number,
                CallingNumber = x.Calling_Number,

                PurchaseDate = x.PurchaseDate?.ToString("yyyy-MM-dd"),
                Warranty = x.Warranty,
                ServiceEnd = x.ServiceEnd?.ToString("yyyy-MM-dd"),
                ServiceNext = x.ServiceNext?.ToString("yyyy-MM-dd"),

                x.BranchId,
                BranchName = x.Branches?.Name,

                x.Active,
                x.Deleted,

                RecordTimeStamp = x.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"),

                x.CreatedBy,
                CreatedDate = x.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"),

                x.UpdatedBy,
                UpdatedDate = x.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss")
            };

            return JsonSuccess("Detail loaded", r);
        }



        // ============================================================
        // CREATE
        // ============================================================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(MachineDto dto)
        {
            try
            {
                var empId = GetCurrentEmployeeId();

                var model = new Gen_Machines
                {
                    Code = dto.Code ?? "",
                    BrandId = dto.BrandId,
                    ServiceId = dto.ServiceId,

                    HeadtoHead = dto.HeadtoHead ?? "",
                    FrameHeight = dto.FrameHeight ?? "",
                    HeadsNum = dto.HeadsNum,
                    NeedlesNum = dto.NeedlesNum,

                    Serial_Number = dto.SerialNumber ?? "",
                    Calling_Number = dto.CallingNumber ?? "",

                    PurchaseDate = string.IsNullOrEmpty(dto.PurchaseDate) ? (DateTime?)null : DateTime.Parse(dto.PurchaseDate),
                    Warranty = dto.Warranty,
                    ServiceEnd = string.IsNullOrEmpty(dto.ServiceEnd) ? (DateTime?)null : DateTime.Parse(dto.ServiceEnd),
                    ServiceNext = string.IsNullOrEmpty(dto.ServiceNext) ? (DateTime?)null : DateTime.Parse(dto.ServiceNext),

                    BranchId = dto.BranchId,

                    Active = dto.Active,
                    Deleted = false,
                    RecordTimeStamp = DateTime.Now,

                    CreatedBy = empId,
                    CreatedDate = DateTime.Now,
                    UpdatedBy = empId,
                    UpdatedDate = DateTime.Now
                };

                db.Gen_Machines.Add(model);
                db.SaveChanges();

                LogMachineAudit(model.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: model.Code + " · Brand #" + model.BrandId + " · Branch #" + model.BranchId);

                return JsonSuccess("Machine added successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Create failed. " + DescribeMachineError(ex));
            }
        }


        // ============================================================
        // UPDATE
        // ============================================================
        [HttpPost]
        [Route("update")]
        public ActionResult Update(MachineDto dto)
        {
            try
            {
                var empId = GetCurrentEmployeeId();

                var model = db.Gen_Machines.FirstOrDefault(x => x.Id == dto.Id);
                if (model == null)
                    return JsonError("Record not found");

                model.Code = dto.Code;
                model.BrandId = dto.BrandId;
                model.ServiceId = dto.ServiceId;

                model.HeadtoHead = dto.HeadtoHead;
                model.FrameHeight = dto.FrameHeight;
                model.HeadsNum = dto.HeadsNum;
                model.NeedlesNum = dto.NeedlesNum;

                model.Serial_Number = dto.SerialNumber;
                model.Calling_Number = dto.CallingNumber;

                model.PurchaseDate = string.IsNullOrEmpty(dto.PurchaseDate) ? (DateTime?)null : DateTime.Parse(dto.PurchaseDate);
                model.Warranty = dto.Warranty;
                model.ServiceEnd = string.IsNullOrEmpty(dto.ServiceEnd) ? (DateTime?)null : DateTime.Parse(dto.ServiceEnd);
                model.ServiceNext = string.IsNullOrEmpty(dto.ServiceNext) ? (DateTime?)null : DateTime.Parse(dto.ServiceNext);

                model.BranchId = dto.BranchId;
                model.Active = dto.Active;

                model.UpdatedBy = empId;
                model.UpdatedDate = DateTime.Now;
                model.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogMachineAudit(model.Id, "EDITED",
                    oldValue: null, newValue: model.Active == true ? "Active" : "Inactive",
                    detail: model.Code + " · Branch #" + model.BranchId);

                return JsonSuccess("Machine updated successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Update failed. " + DescribeMachineError(ex));
            }
        }


        // ============================================================
        // DELETE (SOFT DELETE)
        // ============================================================
        [HttpPost]
        [Route("delete")]
        public ActionResult Delete(int id)
        {
            try
            {
                var model = db.Gen_Machines.FirstOrDefault(x => x.Id == id);
                if (model == null)
                    return JsonError("Record not found");

                model.Deleted = true;
                model.Active = false;
                model.UpdatedBy = GetCurrentEmployeeId();
                model.UpdatedDate = DateTime.Now;
                model.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogMachineAudit(id, "DELETED",
                    oldValue: "Active", newValue: "Deleted",
                    detail: model.Code);

                return JsonSuccess("Machine deleted");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed. " + DescribeMachineError(ex));
            }
        }

        public ActionResult Index(
        string search = null,
        string activeFilter = null,
        bool showCreate = false,
        bool showEdit = false,
        int? editId = null,
        int page = 1,
        int pageSize = 20)
        {
            // -- 1) build your base query
            var query = db.Gen_Machines
                          .Include(m => m.Gen_Brands)
                          .Include(m => m.Gen_TheServices)
                          .Where(m => m.Deleted != true);

            // -- 2) apply filters …
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(m => m.Code.Contains(search)
                                      || (m.Serial_Number ?? "").Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(m => m.Active == want);
            }

            // -- 3) page it
            var paged = query
                        .OrderBy(m => m.Code)
                        .ToPagedList(page, pageSize);

            // -- 4) build the VM
            var vm = new MachinesPageViewModel
            {
                Machines = paged,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter,
                NewMachine = showCreate ? new Gen_Machines() : null,
                EditMachine = (showEdit && editId.HasValue)
                                 ? db.Gen_Machines.Find(editId.Value)
                                 : null
            };

            // — IMPORTANT: populate your dropdowns every time —
            PopulateBrandAndServiceLists(vm.NewMachine?.BrandId, vm.NewMachine?.ServiceId);

            var branchItems = db.Branches
                        .Where(b => b.IsActive == true)
                        .OrderBy(b => b.Name)
                        .Select(b => new { b.Id, b.Name })
                        .ToList();
            ViewBag.BranchList = new SelectList(branchItems, "Id", "Name", vm.NewMachine?.BranchId);

            return View(vm);
        }


        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(
            MachinesPageViewModel vm,
            string search = null,
            string activeFilter = null,
            int page = 1,
            int pageSize = 20)
        {
            // re-populate your grid (same as above)
            vm.ShowCreate = true;
            var query = db.Gen_Machines
                  .Where(machine => machine.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(machine =>
                    machine.Code.Contains(search)
                    || (machine.Serial_Number ?? "").Contains(search)
                );
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(machine => machine.Active == want);
            }

            // 2) Apply ordering & paging
            vm.Machines = query
                .OrderBy(machine => machine.Code)
                .ToPagedList(page, pageSize);

            // re-populate your dropdowns before you check ModelState
            PopulateBrandAndServiceLists(vm.NewMachine?.BrandId, vm.NewMachine?.ServiceId);

            if (!ModelState.IsValid)
                return View("Index", vm);

            // save your new machine …
            int userEmpId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var now = DateTime.UtcNow;
            var m = vm.NewMachine;
            m.CreatedBy = userEmpId;
            m.CreatedDate = now;
            m.UpdatedBy = userEmpId;
            m.UpdatedDate = now;
            m.Active = true;
            m.Deleted = false;

            db.Gen_Machines.Add(m);
            db.SaveChanges();

            return RedirectToAction("Index",
                new { search, activeFilter, page, pageSize });
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(MachinesPageViewModel vm)
        {
            if (!ModelState.IsValid)
                return RedirectToAction("Index", new { showEdit = true, editId = vm.EditMachine.Id });

            var m = db.Gen_Machines.Find(vm.EditMachine.Id);
            if (m == null) return HttpNotFound();

            int userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            // map your updatable fields
            m.Code = vm.EditMachine.Code;
            m.BrandId = vm.EditMachine.BrandId;
            m.ServiceId = vm.EditMachine.ServiceId;
            m.Serial_Number = vm.EditMachine.Serial_Number;
            m.Active = vm.EditMachine.Active;
            m.Deleted = vm.EditMachine.Deleted;
            m.BranchId = vm.EditMachine.BranchId;

            m.UpdatedBy = userId;
            m.UpdatedDate = DateTime.UtcNow;
            m.RecordTimeStamp = DateTime.UtcNow;

            db.Entry(m).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index");
        }
        private void PopulateBrandAndServiceLists(int? selectedBrandId, int? selectedServiceId)
        {
            var brands = db.Gen_Brands
           .Where(b => b.Deleted != true)
           .OrderBy(b => b.BrandName)
           .Select(b => new { b.Id, b.BrandName })
           .ToList();
            ViewBag.BrandList = new SelectList(brands, "Id", "BrandName", selectedBrandId);

            var services = db.Gen_TheServices
           .Where(s => s.Deleted != true)
           .OrderBy(s => s.Service_Name)
           .Select(s => new { s.Id, s.Service_Name })
           .ToList();
            ViewBag.ServiceList = new SelectList(services, "Id", "Service_Name", selectedServiceId);
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateMachineField(int machineId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // 1) Load the machine
            var machine = db.Gen_Machines.Find(machineId);
            if (machine == null)
                return Json(new { success = false, message = "Machine not found." });

            // 2) Find the property on the Gen_Machines type
            var prop = typeof(Gen_Machines).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                // 3) Convert the incoming string to the correct type
                object converted;
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                if (targetType == typeof(string))
                {
                    converted = newValue;
                }
                else if (targetType == typeof(int))
                {
                    converted = int.TryParse(newValue, out var i) ? i : (int?)null;
                }
                else if (targetType == typeof(decimal))
                {
                    converted = decimal.TryParse(newValue, out var d) ? d : (decimal?)null;
                }
                else if (targetType == typeof(bool))
                {
                    converted = (newValue ?? "")
                                .Trim()
                                .Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (targetType == typeof(DateTime))
                {
                    converted = DateTime.TryParse(newValue, out var dt) ? dt : (DateTime?)null;
                }
                else
                {
                    return Json(new { success = false, message = $"Unsupported type {targetType.Name}" });
                }

                // 4) Apply it
                prop.SetValue(machine, converted);

                // 5) Stamp the update
                machine.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                machine.UpdatedDate = DateTime.UtcNow;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // GET: Gen_Machines/Details/5
        public ActionResult Details(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var m = db.Gen_Machines
                      .Include(x => x.Gen_Brands)
                      .Include(x => x.Gen_TheServices)
                      .SingleOrDefault(x => x.Id == id.Value);
            if (m == null) return HttpNotFound();
            return View(m);
        }

        // GET: Gen_Machines/Delete/5
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var m = db.Gen_Machines.Find(id.Value);
            if (m == null) return HttpNotFound();
            return View(m);
        }

        // POST: Gen_Machines/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var m = db.Gen_Machines.Find(id);
            db.Gen_Machines.Remove(m);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        // GET: /Gen_Machines/ExportMachinesToExcel
        [HttpGet, Obsolete]
        public ActionResult ExportMachinesToExcel(int? id, string searchString, string activeFilter)
        {            
            if (id.HasValue)
            {
                // --- DETAIL EXPORT ---
                var m = db.Gen_Machines
                          .Include(x => x.Gen_Brands)
                          .Include(x => x.Gen_TheServices)
                          .Include(x => x.Branches)
                            .FirstOrDefault(x => x.Id == id.Value);
                if (m == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("MachineDetail");

                    // Header row
                    ws.Cells[1, 1].Value = "Field";
                    ws.Cells[1, 2].Value = "Value";
                    ws.Cells[1, 1, 1, 2].Style.Font.Bold = true;

                    // Helper to add rows
                    var row = 1;
                    void Add(string field, object val)
                    {
                        row++;
                        ws.Cells[row, 1].Value = field;
                        ws.Cells[row, 2].Value = val ?? "(none)";
                    }

                    Add("ID", m.Id);
                    Add("Code", m.Code);
                    Add("Brand", m.Gen_Brands?.BrandName);
                    Add("Service", m.Gen_TheServices?.Service_Name);
                    Add("HeadToHead", m.HeadtoHead);
                    Add("FrameHeight", m.FrameHeight);
                    Add("HeadsNum", m.HeadsNum);
                    Add("NeedlesNum", m.NeedlesNum);
                    Add("Serial Number", m.Serial_Number);
                    Add("Calling Number", m.Calling_Number);
                    Add("Purchase Date", m.PurchaseDate?.ToString("yyyy-MM-dd"));
                    Add("Warranty", m.Warranty);
                    Add("Service End", m.ServiceEnd?.ToString("yyyy-MM-dd"));
                    Add("Service Next", m.ServiceNext?.ToString("yyyy-MM-dd"));
                    Add("Branch", m.Branches?.Name);
                    Add("Active", m.Active == true ? "Yes" : "No");
                    Add("Deleted", m.Deleted == true ? "Yes" : "No");
                    Add("Created By", m.CreatedBy);
                    Add("Created Date", m.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Updated By", m.UpdatedBy);
                    Add("Updated Date", m.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    var detailBytes = pkg.GetAsByteArray();
                    return File(
                        detailBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Machine_{m.Id}_Detail.xlsx"
                    );
                }
            }
            else
            {
                // --- LIST EXPORT ---
                var query = db.Gen_Machines.AsQueryable();

                if (!string.IsNullOrEmpty(searchString))
                    query = query.Where(m =>
                        m.Code.Contains(searchString) ||
                        m.Serial_Number.Contains(searchString));

                if (!string.IsNullOrEmpty(activeFilter))
                {
                    var want = activeFilter == "Yes";
                    query = query.Where(m => m.Active == want);
                }

                var list = query
                    .Where(m => m.Deleted != true)
                    .OrderBy(m => m.Code)
                    .ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Machines");

                    var headers = new[]
                    {
                    "ID","Code","Brand","Service","PurchaseDate",
                    "Warranty","ServiceEnd","ServiceNext"
                    };
                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        var m = list[i];
                        var r = i + 2;
                        ws.Cells[r, 1].Value = m.Id;
                        ws.Cells[r, 2].Value = m.Code;
                        ws.Cells[r, 3].Value = m.Gen_Brands?.BrandName;
                        ws.Cells[r, 4].Value = m.Gen_TheServices?.Service_Name;
                        ws.Cells[r, 5].Value = m.PurchaseDate?.ToString("yyyy-MM-dd");
                        ws.Cells[r, 6].Value = m.Warranty;
                        ws.Cells[r, 7].Value = m.ServiceEnd?.ToString("yyyy-MM-dd");
                        ws.Cells[r, 8].Value = m.ServiceNext?.ToString("yyyy-MM-dd");
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    var listBytes = pkg.GetAsByteArray();
                    return File(
                        listBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "Machines.xlsx"
                    );
                }
            }
        }



        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportMachinesToPdf(int? id, string searchString, string activeFilter)
        {
            if (id.HasValue)
            {
                // SINGLE‐RECORD DETAIL PDF
                var m = db.Gen_Machines
                    .Include(x => x.Gen_Brands)
                    .Include(x => x.Gen_TheServices)
                    .Include(x => x.Branches)
                    .FirstOrDefault(x => x.Id == id.Value);
                if (m == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 100 };
                    // header
                    table.AddCell(new PdfPCell(new Phrase("Field")) { BackgroundColor = BaseColor.LIGHT_GRAY });
                    table.AddCell(new PdfPCell(new Phrase("Value")) { BackgroundColor = BaseColor.LIGHT_GRAY });

                    void AddRow(string field, string val)
                    {
                        table.AddCell(field);
                        table.AddCell(val ?? "(none)");
                    }

                    AddRow("ID", m.Id.ToString());
                    AddRow("Code", m.Code);
                    AddRow("Brand", m.Gen_Brands?.BrandName);
                    AddRow("Service", m.Gen_TheServices?.Service_Name);
                    AddRow("HeadToHead", m.HeadtoHead);
                    AddRow("FrameHeight", m.FrameHeight?.ToString());
                    AddRow("HeadsNum", m.HeadsNum?.ToString());
                    AddRow("NeedlesNum", m.NeedlesNum?.ToString());
                    AddRow("Serial Number", m.Serial_Number);
                    AddRow("Calling Number", m.Calling_Number);
                    AddRow("Purchase Date", m.PurchaseDate?.ToString("yyyy-MM-dd"));
                    AddRow("Warranty", m.Warranty?.ToString());
                    AddRow("Service End", m.ServiceEnd?.ToString("yyyy-MM-dd"));
                    AddRow("Service Next", m.ServiceNext?.ToString("yyyy-MM-dd"));
                    AddRow("Branch", m.Branches?.Name);
                    AddRow("Active", m.Active == true ? "Yes" : "No");
                    AddRow("Deleted", m.Deleted == true ? "Yes" : "No");
                    AddRow("Created By", m.CreatedBy?.ToString());
                    AddRow("Created Date", m.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    AddRow("Updated By", m.UpdatedBy?.ToString());
                    AddRow("Updated Date", m.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"Machine_{m.Id}_Detail.pdf");
                }
            }
            else
            {
                // FALLBACK: list‐style PDF (as you already have it)
                var query = db.Gen_Machines.AsQueryable();
                if (!string.IsNullOrEmpty(searchString))
                    query = query.Where(m => m.Code.Contains(searchString)
                                          || m.Serial_Number.Contains(searchString));
                if (!string.IsNullOrEmpty(activeFilter))
                {
                    var want = activeFilter == "Yes";
                    query = query.Where(m => m.Active == want);
                }
                var list = query.Where(m => m.Deleted != true)
                                .OrderBy(m => m.Code)
                                .ToList();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(8) { WidthPercentage = 100 };
                    // headers
                    foreach (var h in new[] { "ID", "Code", "Brand", "Service", "PurchaseDate", "Warranty", "ServiceEnd", "ServiceNext" })
                        table.AddCell(new PdfPCell(new Phrase(h)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                    // rows
                    foreach (var m in list)
                    {
                        table.AddCell(m.Id.ToString());
                        table.AddCell(m.Code);
                        table.AddCell(m.Gen_Brands?.BrandName);
                        table.AddCell(m.Gen_TheServices?.Service_Name);
                        table.AddCell(m.PurchaseDate?.ToString("yyyy-MM-dd"));
                        table.AddCell(m.Warranty?.ToString());
                        table.AddCell(m.ServiceEnd?.ToString("yyyy-MM-dd"));
                        table.AddCell(m.ServiceNext?.ToString("yyyy-MM-dd"));
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", "Machines.pdf");
                }
            }
        }
    }
}