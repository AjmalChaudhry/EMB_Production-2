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
using PagedList.Mvc;

namespace EMB_Production.Controllers
{

    [RoutePrefix("emb-products/service-types")]
    public class Gen_ServiceTypeController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Service Types)
        // ─────────────────────────────────────────────────────────────
        private void LogStAudit(int id, string action,
                                string oldValue = null, string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_ServiceTypeAuditLog
       (ServiceTypeId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
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
                var entries = db.Database.SqlQuery<StAuditEntry>(@"
SELECT  a.Id, a.ServiceTypeId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_ServiceTypeAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.ServiceTypeId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, serviceTypeId = e.ServiceTypeId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeStError(ex) });
            }
        }

        private class StAuditEntry
        {
            public int Id { get; set; }
            public int ServiceTypeId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeStError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Service Types)
        // ─────────────────────────────────────────────────────────────
        public class StBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkSt(List<int> ids, Func<int, Gen_ServiceType, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_ServiceType.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.Service_Type ?? ("ST#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeStError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(StBulkIdsDto dto)
        {
            var data = DoBulkSt(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                db.SaveChanges();
                LogStAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(StBulkIdsDto dto)
        {
            var data = DoBulkSt(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                db.SaveChanges();
                LogStAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(StBulkIdsDto dto)
        {
            var data = DoBulkSt(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                db.SaveChanges();
                LogStAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
               
        [HttpGet]
        [Route("list")]
        public JsonResult List(string search = "", int page = 1, int pageSize = 10, int? branchId = null, string activeFilter = "")
        {
            try
            {
                var query = db.Gen_ServiceType
                    .Where(x => x.Deleted == false || x.Deleted == null);

                // SEARCH
                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    query = query.Where(x =>
                        (x.Service_Type != null && x.Service_Type.Contains(search)) ||
                        (x.Service_TypeDescription != null && x.Service_TypeDescription.Contains(search))
                    );
                }

                // BRANCH FILTER
                if (branchId.HasValue && branchId > 0)
                    query = query.Where(x => x.BranchId == branchId);

                // ACTIVE FILTER
                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    if (activeFilter == "active")
                        query = query.Where(x => x.Active == true);
                    else if (activeFilter == "inactive")
                        query = query.Where(x => x.Active == false || x.Active == null);
                }

                var total = query.Count();

                // Phase R.8 - summary aggregates
                var summarySource = query.Select(x => new
                {
                    x.Active,
                    x.BranchId,
                    BranchName = x.Branches.Name,
                    ServiceCount = x.Gen_TheServices.Count(s => s.Deleted != true)
                }).ToList();

                int activeCount = summarySource.Count(s => s.Active == true);
                int inactiveCount = summarySource.Count(s => s.Active != true);
                int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();
                int totalServicesUnderTypes = summarySource.Sum(s => s.ServiceCount);

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
                    distinctBranches,
                    totalServicesUnderTypes,
                    byBranch
                };

                var rows = query
                    .OrderByDescending(x => x.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new ServiceTypeRowViewModel
                    {
                        Id = x.Id,
                        Service_Type = x.Service_Type,
                        Service_TypeDescription = x.Service_TypeDescription,
                        BranchId = x.BranchId,
                        BranchName = x.Branches.Name,
                        Active = x.Active ?? false
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = rows,
                    summary,
                    total = total
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ======================================================
        // DETAIL
        // ======================================================
        [HttpGet]
        [Route("detail/{id}")]
        public JsonResult Detail(int id)
        {
            var entity = db.Gen_ServiceType.FirstOrDefault(x => x.Id == id);

            if (entity == null)
            {
                return Json(
                    new { success = false, message = "Record not found." },
                    JsonRequestBehavior.AllowGet
                );
            }

            // ⭐ EMPLOYEE ID lookup for CreatedBy
            string createdByEmployeeId = null;
            if (entity.CreatedBy.HasValue)
            {
                createdByEmployeeId = db.Adm_Users
                    .Where(u => u.Id == entity.CreatedBy.Value)
                    .Select(u => u.EmployeeId)            // <-- CORRECT COLUMN
                    .FirstOrDefault()
                    ?.ToString();
            }

            // ⭐ EMPLOYEE ID lookup for UpdatedBy
            string updatedByEmployeeId = null;
            if (entity.UpdatedBy.HasValue)
            {
                updatedByEmployeeId = db.Adm_Users
                    .Where(u => u.Id == entity.UpdatedBy.Value)
                    .Select(u => u.EmployeeId)            // <-- CORRECT COLUMN
                    .FirstOrDefault()
                    ?.ToString();
            }

            // ⭐ BUILD VIEWMODEL
            var vm = new ServiceTypeRowViewModel
            {
                Id = entity.Id,

                Service_Type = entity.Service_Type,
                Service_TypeDescription = entity.Service_TypeDescription,

                BranchId = entity.BranchId,
                BranchName = entity.Branches?.Name,

                Active = entity.Active ?? false,
                Deleted = entity.Deleted ?? false,

                RecordTimeStamp = entity.RecordTimeStamp,

                // ⭐ AUDIT (UserId + EmployeeId)
                CreatedBy = entity.CreatedBy,
                CreatedByEmployeeId = createdByEmployeeId,

                CreatedDate = entity.CreatedDate,

                UpdatedBy = entity.UpdatedBy,
                UpdatedByEmployeeId = updatedByEmployeeId,

                UpdatedDate = entity.UpdatedDate,

                // Extra
                RelatedServicesCount = entity.Gen_TheServices.Count()
            };

            return Json(new { success = true, data = vm }, JsonRequestBehavior.AllowGet);
        }

        // ======================================================
        // Phase R.8 - PROFILE
        // ======================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var t = db.Gen_ServiceType.FirstOrDefault(x => x.Id == id);
                if (t == null) return JsonError("Service Type not found.");

                var header = new
                {
                    id = t.Id,
                    serviceType = t.Service_Type,
                    description = t.Service_TypeDescription,
                    branchId = t.BranchId,
                    branchName = t.Branches != null ? t.Branches.Name : "",
                    active = t.Active ?? false,
                    createdDateText = t.CreatedDate.HasValue ? t.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = t.UpdatedDate.HasValue ? t.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Linked services under this type
                var services = (
                    from s in db.Gen_TheServices
                    where s.Service_Type == id && (s.Deleted == false || s.Deleted == null)
                    orderby s.Id descending
                    select new
                    {
                        s.Id,
                        s.Service_Name,
                        s.Service_RateCharge,
                        s.Max_Charges,
                        s.Min_Charges,
                        BranchName = s.Branches.Name,
                        Active = s.Active ?? false
                    }
                ).Take(50).ToList();

                // Service Rates referencing this type
                var rateCount = db.Gen_ServiceRates.Count(r => r.Service_Id == id && (r.Deleted == false || r.Deleted == null));

                decimal avgRate = services.Any() ? services.Average(s => (decimal)(s.Service_RateCharge ?? 0)) : 0m;

                var kpis = new
                {
                    serviceCount = services.Count(),
                    rateCount,
                    activeServices = services.Count(s => s.Active),
                    avgRate = Math.Round(avgRate, 2)
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    services
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }




        [HttpPost]
        [Route("create")]
        public ActionResult Create(ServiceTypeRowViewModel dto)
        {
            try
            {
                var userId = GetCurrentUserId(); // ✔ Correct UserId

                var entity = new Gen_ServiceType
                {
                    Service_Type = dto.Service_Type,
                    Service_TypeDescription = dto.Service_TypeDescription,
                    BranchId = dto.BranchId,
                    Active = dto.Active,
                    Deleted = false,

                    // ⭐ Correct Audit Values
                    CreatedBy = userId,
                    CreatedDate = DateTime.Now,

                    // ⭐ Timestamp for first creation
                    RecordTimeStamp = DateTime.Now
                };

                db.Gen_ServiceType.Add(entity);
                db.SaveChanges();

                LogStAudit(entity.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: (entity.Service_Type ?? "-"));

                return Json(new { success = true, message = "Created successfully" },
                            JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Create failed. " + DescribeStError(ex) },
                            JsonRequestBehavior.AllowGet);
            }
        }



        [HttpPost]
        [Route("update")]
        public ActionResult Update(ServiceTypeRowViewModel dto)
        {
            try
            {
                var userId = GetCurrentUserId(); // ✔ Correct UserId

                var entity = db.Gen_ServiceType.FirstOrDefault(x => x.Id == dto.Id);

                if (entity == null)
                {
                    return Json(new { success = false, message = "Record not found" },
                                JsonRequestBehavior.AllowGet);
                }

                // ⭐ Update fields
                entity.Service_Type = dto.Service_Type;
                entity.Service_TypeDescription = dto.Service_TypeDescription;
                entity.BranchId = dto.BranchId;
                entity.Active = dto.Active;

                // ⭐ Audit for UPDATE
                entity.UpdatedBy = userId;
                entity.UpdatedDate = DateTime.Now;

                // ⭐ DO NOT modify RecordTimeStamp
                // entity.RecordTimeStamp remains unchanged

                db.SaveChanges();

                LogStAudit(entity.Id, "EDITED",
                    oldValue: null, newValue: entity.Active == true ? "Active" : "Inactive",
                    detail: (entity.Service_Type ?? "-"));

                return Json(new { success = true, message = "Updated successfully" },
                            JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Update failed. " + DescribeStError(ex) },
                            JsonRequestBehavior.AllowGet);
            }
        }



        // ======================================================
        // DELETE
        // ======================================================
        [HttpPost]
        [Route("delete/{id}")]
        public JsonResult Delete(int id)
        {
            try
            {
                var entity = db.Gen_ServiceType.FirstOrDefault(x => x.Id == id);
                if (entity == null)
                    return Json(new { success = false, message = "Record not found." });

                entity.Deleted = true;
                entity.Active = false;
                entity.UpdatedDate = DateTime.Now;
                entity.UpdatedBy = GetCurrentUserId();

                db.SaveChanges();

                LogStAudit(id, "DELETED", "Active", "Deleted");

                return Json(new { success = true, message = "Record deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Delete failed. " + DescribeStError(ex) });
            }
        }
        
        private const int PageSize = 10;

        // GET: Index with filters, paging, inline create/edit
        public ActionResult Index(int? page, string search, string activeFilter, int? branchFilter,
                                  bool showCreate = false, bool showEdit = false, int? editId = null)
        {
            var query = db.Gen_ServiceType
                          .Include(t => t.Branches)
                          .Include(t => t.Gen_TheServices)
                          .AsQueryable();

            // filtering
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t => t.Service_Type.Contains(search) || t.Service_TypeDescription.Contains(search));
            if (activeFilter == "Yes") query = query.Where(t => t.Active == true);
            else if (activeFilter == "No") query = query.Where(t => t.Active == false);
            if (branchFilter.HasValue) query = query.Where(t => t.BranchId == branchFilter.Value);

            // projection
            var rows = query.OrderBy(t => t.Id).Select(t => new ServiceTypeRowViewModel
            {
                Id = t.Id,
                Service_Type = t.Service_Type,
                Service_TypeDescription = t.Service_TypeDescription,
                BranchId = t.BranchId,
                BranchName = t.Branches.Name,
                RecordTimeStamp = t.RecordTimeStamp,
                Active = t.Active ?? false,
                Deleted = t.Deleted ?? false,
                CreatedBy = t.CreatedBy,
                CreatedDate = t.CreatedDate,
                UpdatedBy = t.UpdatedBy,
                UpdatedDate = t.UpdatedDate,
                RelatedServicesCount = t.Gen_TheServices.Count
            });

            var vm = new ServiceTypesPageViewModel
            {
                Search = search,
                ActiveFilter = activeFilter,
                BranchFilter = branchFilter,
                Page = page ?? 1,
                Rows = rows.ToPagedList(page ?? 1, PageSize),
                BranchList = new SelectList(db.Branches, "Id", "Name"),
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                NewType = new ServiceTypeRowViewModel(),
                EditType = showEdit && editId.HasValue
                           ? rows.FirstOrDefault(r => r.Id == editId.Value)
                           : new ServiceTypeRowViewModel()
            };

            return View(vm);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(ServiceTypeRowViewModel NewType)
        {
            if (ModelState.IsValid)
            {
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                var entity = new Gen_ServiceType
                {
                    Service_Type = NewType.Service_Type,
                    Service_TypeDescription = NewType.Service_TypeDescription,
                    BranchId = NewType.BranchId,
                    Active = NewType.Active,
                    Deleted = NewType.Deleted,
                    RecordTimeStamp = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    CreatedBy = userId,
                    UpdatedBy = userId
                };
                db.Gen_ServiceType.Add(entity);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(ServiceTypeRowViewModel EditType)
        {
            if (ModelState.IsValid)
            {
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                var entity = db.Gen_ServiceType.Find(EditType.Id);
                if (entity != null)
                {
                    entity.Service_Type = EditType.Service_Type;
                    entity.Service_TypeDescription = EditType.Service_TypeDescription;
                    entity.BranchId = EditType.BranchId;
                    entity.Active = EditType.Active;
                    entity.Deleted = EditType.Deleted;
                    entity.UpdatedDate = DateTime.Now;
                    entity.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                    db.SaveChanges();
                }
            }
            return RedirectToAction("Index");
        }
        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateServiceTypeField(int id, string fieldName, string newValue)
        {
            // ensure user is logged in
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // load the service‐type record
            var entity = db.Gen_ServiceType.Find(id);
            if (entity == null)
                return Json(new { success = false, message = "Service type not found." });

            // reflectively get the target property
            var prop = typeof(Gen_ServiceType).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                object converted;
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

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

                // set and audit
                prop.SetValue(entity, converted);
                entity.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
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
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var serviceType = db.Gen_ServiceType
                                .Include(s => s.Gen_TheServices)
                                .Include(s => s.Branches)
                                .FirstOrDefault(s => s.Id == id.Value);

            if (serviceType == null)
                return HttpNotFound();

            return View(serviceType);
        }

        // GET: Delete confirmation
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var t = db.Gen_ServiceType.Find(id.Value);
            if (t == null) return HttpNotFound();
            return View(t);
        }

        // POST: Delete
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var t = db.Gen_ServiceType.Find(id);
            if (t != null)
            {
                db.Gen_ServiceType.Remove(t);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // GET/POST: Export to Excel (detail or bulk)
        [HttpGet,Obsolete]
        public ActionResult ExportToExcel(int? id, string search, string activeFilter, int? branchFilter)
        {           
            if (id.HasValue)
            {
                // detail export
                var t = db.Gen_ServiceType
                          .Include(e => e.Branches)
                          .Include(e => e.Gen_TheServices)
                          .FirstOrDefault(e => e.Id == id.Value);
                if (t == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("ServiceTypeDetails");
                    int row = 1;
                    ws.Cells[row, 1].Value = "Service Type";
                    ws.Cells[row++, 2].Value = t.Service_Type;
                    ws.Cells[row, 1].Value = "Description";
                    ws.Cells[row++, 2].Value = t.Service_TypeDescription;
                    ws.Cells[row, 1].Value = "Branch";
                    ws.Cells[row++, 2].Value = t.Branches?.Name;
                    ws.Cells[row, 1].Value = "Gen_TheServices Count";
                    ws.Cells[row++, 2].Value = t.Gen_TheServices.Count;
                    ws.Cells[row, 1].Value = "RecordTimeStamp";
                    ws.Cells[row++, 2].Value = t.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cells[row, 1].Value = "Active";
                    ws.Cells[row++, 2].Value = t.Active == true ? "Yes" : "No";
                    ws.Cells[row, 1].Value = "Deleted";
                    ws.Cells[row++, 2].Value = t.Deleted == true ? "Yes" : "No";
                    ws.Cells[row, 1].Value = "CreatedBy";
                    ws.Cells[row++, 2].Value = t.CreatedBy;
                    ws.Cells[row, 1].Value = "CreatedDate";
                    ws.Cells[row++, 2].Value = t.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss");
                    ws.Cells[row, 1].Value = "UpdatedBy";
                    ws.Cells[row++, 2].Value = t.UpdatedBy;
                    ws.Cells[row, 1].Value = "UpdatedDate";
                    ws.Cells[row, 2].Value = t.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss");

                    ws.Cells[1, 1, row, 2].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ServiceType_{id.Value}_Details.xlsx");
                }
            }
            else
            {
                // bulk export
                var query = db.Gen_ServiceType.Include(e => e.Branches).AsQueryable();
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Service_Type.Contains(search) || t.Service_TypeDescription.Contains(search));
                if (activeFilter == "Yes") query = query.Where(t => t.Active == true);
                else if (activeFilter == "No") query = query.Where(t => t.Active == false);
                if (branchFilter.HasValue) query = query.Where(t => t.BranchId == branchFilter.Value);
                var list = query.ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("ServiceTypes");
                    var headers = new[] { "ID", "Service Type", "Description", "Branch", "Active", "Deleted" };
                    for (int i = 0; i < headers.Length; i++) ws.Cells[1, i + 1].Value = headers[i];

                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i]; int r = i + 2;
                        ws.Cells[r, 1].Value = t.Id;
                        ws.Cells[r, 2].Value = t.Service_Type;
                        ws.Cells[r, 3].Value = t.Service_TypeDescription;
                        ws.Cells[r, 4].Value = t.Branches?.Name;
                        ws.Cells[r, 5].Value = t.Active == true ? "Yes" : "No";
                        ws.Cells[r, 6].Value = t.Deleted == true ? "Yes" : "No";
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ServiceTypes_All.xlsx");
                }
            }
        }

        // PDF export: detail or bulk
        public ActionResult ExportToPdf(int? id, string search, string activeFilter, int? branchFilter)
        {
            if (id.HasValue)
            {
                // detail export
                var t = db.Gen_ServiceType
                          .Include(e => e.Branches)
                          .Include(e => e.Gen_TheServices)
                          .FirstOrDefault(e => e.Id == id.Value);
                if (t == null) return HttpNotFound();

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

                    AddRow("Service Type", t.Service_Type);
                    AddRow("Description", t.Service_TypeDescription);
                    AddRow("Branch", t.Branches?.Name);
                    AddRow("Gen_TheServices Count", t.Gen_TheServices.Count.ToString());
                    AddRow("RecordTimeStamp", t.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
                    AddRow("Active", t.Active == true ? "Yes" : "No");
                    AddRow("Deleted", t.Deleted == true ? "Yes" : "No");
                    AddRow("CreatedBy", t.CreatedBy?.ToString() ?? string.Empty);
                    AddRow("CreatedDate", t.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
                    AddRow("UpdatedBy", t.UpdatedBy?.ToString() ?? string.Empty);
                    AddRow("UpdatedDate", t.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"ServiceType_{id.Value}_Details.pdf");
                }
            }
            else
            {
                // bulk export
                var query = db.Gen_ServiceType.Include(e => e.Branches).AsQueryable();
                if (!string.IsNullOrWhiteSpace(search)) query = query.Where(t => t.Service_Type.Contains(search) || t.Service_TypeDescription.Contains(search));
                if (activeFilter == "Yes") query = query.Where(t => t.Active == true);
                else if (activeFilter == "No") query = query.Where(t => t.Active == false);
                if (branchFilter.HasValue) query = query.Where(t => t.BranchId == branchFilter.Value);
                var list = query.ToList();

                using (var ms = new MemoryStream())
                {
                    var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(6) { WidthPercentage = 100 };
                    var headers = new[] { "ID", "Service Type", "Description", "Branch", "Active", "Deleted" };
                    foreach (var h in headers)
                        table.AddCell(new PdfPCell(new Phrase(h)) { BackgroundColor = BaseColor.LIGHT_GRAY });

                    foreach (var t2 in list)
                    {
                        table.AddCell(t2.Id.ToString());
                        table.AddCell(t2.Service_Type);
                        table.AddCell(t2.Service_TypeDescription);
                        table.AddCell(t2.Branches?.Name);
                        table.AddCell(t2.Active == true ? "Yes" : "No");
                        table.AddCell(t2.Deleted == true ? "Yes" : "No");
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", "ServiceTypes_All.pdf");
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
