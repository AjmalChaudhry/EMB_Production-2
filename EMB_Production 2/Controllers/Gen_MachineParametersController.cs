using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using PagedList;
using OfficeOpenXml;
using Rotativa;
using System.Data.Entity.SqlServer;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/machine-parameters")]
    public class Gen_MachineParametersController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Machine Parameters)
        // ─────────────────────────────────────────────────────────────
        private void LogMpAudit(int id, string action,
                                string oldValue = null, string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_MachineParametersAuditLog
       (MpId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
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
                var entries = db.Database.SqlQuery<MpAuditEntry>(@"
SELECT  a.Id, a.MpId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_MachineParametersAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.MpId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, mpId = e.MpId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeMpError(ex) });
            }
        }

        private class MpAuditEntry
        {
            public int Id { get; set; }
            public int MpId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeMpError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Machine Parameters)
        // ─────────────────────────────────────────────────────────────
        public class MpBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkMp(List<int> ids, Func<int, Gen_MachineParameters, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_MachineParameters.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = "MP#" + id;
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeMpError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(MpBulkIdsDto dto)
        {
            var data = DoBulkMp(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogMpAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(MpBulkIdsDto dto)
        {
            var data = DoBulkMp(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogMpAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(MpBulkIdsDto dto)
        {
            var data = DoBulkMp(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogMpAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        [HttpGet]
        [Route("list")]
        public ActionResult List(string search = "", string activeFilter = "", int? machineId = null, int page = 1, int pageSize = 10)
        {
            var query = db.Gen_MachineParameters
                .Where(x => x.Deleted == false || x.Deleted == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(x =>
                    x.Gen_Machines.Code.Contains(search) ||
                    x.HeadToHead.Contains(search)
                );
            }

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            // Phase R.5 - machineId filter
            if (machineId.HasValue && machineId.Value > 0)
                query = query.Where(x => x.MachineId == machineId.Value);

            var fullList = query
                .OrderByDescending(x => x.Id)
                .ToList()
                .Select(x => new
                {
                    x.Id,
                    x.MachineId,
                    MachineCode = x.Gen_Machines != null ? x.Gen_Machines.Code : "",

                    x.HeadToHead,
                    x.FrameHeight,

                    Active = x.Active ?? false,
                    x.Deleted,

                    x.CreatedBy,
                    CreatedDate = x.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    x.UpdatedBy,
                    UpdatedDate = x.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToList();

            // Phase R.5 - summary aggregates
            int totalItems = fullList.Count();
            int activeCount = fullList.Count(s => s.Active);
            int inactiveCount = fullList.Count(s => !s.Active);
            int distinctMachines = fullList.Select(s => s.MachineId).Distinct().Count();

            var byHeadToHead = fullList
                .Where(s => !string.IsNullOrWhiteSpace(s.HeadToHead))
                .GroupBy(s => s.HeadToHead)
                .Select(g => new { headToHead = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var byMachine = fullList
                .Where(s => !string.IsNullOrWhiteSpace(s.MachineCode))
                .GroupBy(s => s.MachineCode)
                .Select(g => new { machine = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var summary = new
            {
                count = totalItems,
                activeCount,
                inactiveCount,
                distinctMachines,
                byHeadToHead,
                byMachine
            };

            return JsonSuccess("Loaded", new
            {
                items = fullList.Skip((page - 1) * pageSize).Take(pageSize),
                summary,
                totalItems
            });
        }

        // =========================================================
        // Phase R.5 - PROFILE
        // =========================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var p = db.Gen_MachineParameters.FirstOrDefault(x => x.Id == id);
                if (p == null) return JsonError("Parameter not found.");

                var machine = p.MachineId.HasValue
                    ? db.Gen_Machines.FirstOrDefault(m => m.Id == p.MachineId.Value)
                    : null;

                var header = new
                {
                    id = p.Id,
                    machineId = p.MachineId,
                    machineCode = machine != null ? machine.Code : "",
                    brandName = machine != null && machine.Gen_Brands != null ? machine.Gen_Brands.BrandName : "",
                    headToHead = p.HeadToHead,
                    frameHeight = p.FrameHeight,
                    active = p.Active ?? false,
                    createdDateText = p.CreatedDate.HasValue ? p.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = p.UpdatedDate.HasValue ? p.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Siblings - other parameters for the same machine
                var siblings = (
                    from sp in db.Gen_MachineParameters
                    where sp.MachineId == p.MachineId
                       && sp.Id != id
                       && (sp.Deleted == false || sp.Deleted == null)
                    orderby sp.Id descending
                    select new
                    {
                        sp.Id,
                        sp.HeadToHead,
                        sp.FrameHeight,
                        Active = sp.Active ?? false,
                        CreatedDate = sp.CreatedDate
                    }
                ).ToList().Select(s => new
                {
                    s.Id,
                    s.HeadToHead,
                    s.FrameHeight,
                    s.Active,
                    createdDateText = s.CreatedDate.HasValue ? s.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : ""
                }).ToList();

                var kpis = new
                {
                    frameHeight = p.FrameHeight,
                    siblingCount = siblings.Count(),
                    machineParamCount = siblings.Count() + 1
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    siblings
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        // =========================================================
        // DETAIL
        // =========================================================
        [HttpGet]
        [Route("detail/{id}")]
        public ActionResult Detail(int id)
        {
            var result = (
                from m in db.Gen_MachineParameters

                join mac in db.Gen_Machines
                    on m.MachineId equals mac.Id into macJoin
                from mac in macJoin.DefaultIfEmpty()

                join cu in db.Adm_Users
                    on m.CreatedBy equals cu.Id into createdJoin
                from cu in createdJoin.DefaultIfEmpty()

                join uu in db.Adm_Users
                    on m.UpdatedBy equals uu.Id into updatedJoin
                from uu in updatedJoin.DefaultIfEmpty()

                where m.Id == id

                select new
                {
                    m.Id,
                    m.MachineId,
                    MachineCode = mac.Code,

                    m.HeadToHead,
                    m.FrameHeight,

                    m.Active,
                    m.Deleted,

                    CreatedByEmployeeId = cu.EmployeeId,
                    CreatedDate = m.CreatedDate,

                    UpdatedByEmployeeId = uu.EmployeeId,
                    UpdatedDate = m.UpdatedDate
                }
            ).FirstOrDefault();

            if (result == null)
                return JsonError("Record not found");

            return JsonSuccess("Detail loaded", new
            {
                result.Id,
                result.MachineId,
                result.MachineCode,
                result.HeadToHead,
                result.FrameHeight,
                result.Active,
                result.Deleted,
                result.CreatedByEmployeeId,
                CreatedDate = result.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                result.UpdatedByEmployeeId,
                UpdatedDate = result.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        // =========================================================
        // CREATE
        // =========================================================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(MachineParameterDto dto)
        {
            if (dto == null)
                return JsonError("Invalid request.");

            if (dto.MachineId == null)
                return JsonError("Machine is required.");

            var m = new Gen_MachineParameters
            {
                MachineId = dto.MachineId,
                HeadToHead = dto.HeadToHead,
                FrameHeight = dto.FrameHeight,
                Active = dto.Active,
                Deleted = false,

                // ✅ Save UserId (NOT EmployeeId)
                CreatedBy = GetCurrentUserId(),
                CreatedDate = DateTime.Now,
            };

            db.Gen_MachineParameters.Add(m);
            db.SaveChanges();

            LogMpAudit(m.Id, "CREATED",
                oldValue: null, newValue: "Active",
                detail: "Machine #" + dto.MachineId
                      + " · Head " + (dto.HeadToHead?.ToString() ?? "-")
                      + " / Frame " + (dto.FrameHeight?.ToString() ?? "-"));

            return JsonSuccess("Parameter created successfully");
        }
        // =========================================================
        // UPDATE
        // =========================================================
        [HttpPost]
        [Route("update")]
        public ActionResult Update(MachineParameterDto dto)
        {
            var m = db.Gen_MachineParameters.Find(dto.Id);
            if (m == null)
                return JsonError("Record not found");

            m.MachineId = dto.MachineId;
            m.HeadToHead = dto.HeadToHead;
            m.FrameHeight = dto.FrameHeight;
            m.Active = dto.Active;

            // ✅ Save UserId (NOT EmployeeId)
            m.UpdatedBy = GetCurrentUserId();
            m.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            LogMpAudit(m.Id, "EDITED",
                oldValue: null, newValue: m.Active == true ? "Active" : "Inactive",
                detail: "Head " + (m.HeadToHead?.ToString() ?? "-")
                      + " / Frame " + (m.FrameHeight?.ToString() ?? "-"));

            return JsonSuccess("Parameter updated successfully");
        }
        // =========================================================
        // DELETE
        // =========================================================
        [HttpPost]
        [Route("delete/{id}")]
        public ActionResult Delete(int id)
        {
            var m = db.Gen_MachineParameters.Find(id);
            if (m == null) return JsonError("Record not found");

            m.Deleted = true;
            m.Active = false;
            m.UpdatedBy = GetCurrentUserId();
            m.UpdatedDate = DateTime.Now;
            db.SaveChanges();

            LogMpAudit(id, "DELETED", oldValue: "Active", newValue: "Deleted");

            return JsonSuccess("Deleted successfully");
        }
    

    // GET: index + inline create/edit + paging + filter
    public ActionResult Index(
            string search = null,
            string activeFilter = null,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null,
            int page = 1,
            int pageSize = 20
        )
        {
            // dropdown of machines
            var machines = db.Gen_Machines
                .Where(m => m.Active == true && m.Deleted != true)
                .OrderBy(m => m.Code)
                .Select(m => new { m.Id, m.Code })
                .ToList();

            // base query
            var q = db.Gen_MachineParameters.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(p =>
                    p.HeadToHead.Contains(search) ||
                    SqlFunctions.StringConvert((double?)p.FrameHeight).Contains(search)
                );
            }
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                q = q.Where(p => p.Active == want);
            }

            var paged = q
                .OrderBy(p => p.Id)
                .Select(p => new MachineParameterRowViewModel
                {
                    Id = p.Id,
                    MachineCode = p.Gen_Machines.Code,
                    HeadToHead = p.HeadToHead,
                    FrameHeight = p.FrameHeight,
                    Active = p.Active ?? false,
                    Deleted = p.Deleted ?? false,
                    CreatedBy = p.CreatedBy,
                    CreatedDate = p.CreatedDate,
                    UpdatedBy = p.UpdatedBy,
                    UpdatedDate = p.UpdatedDate
                })
                .ToPagedList(page, pageSize);

            var vm = new MachineParametersPageViewModel
            {
                Rows = paged,
                NewParam = showCreate
                                ? new Gen_MachineParameters { Active = true }
                                : null,
                EditParam = (showEdit && editId.HasValue)
                                ? db.Gen_MachineParameters.Find(editId.Value)
                                : null,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter,
                MachineList = new SelectList(machines, "Id", "Code")
            };

            return View(vm);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(
            MachineParametersPageViewModel vm,
            string search = null,
            string activeFilter = null,
            int page = 1,
            int pageSize = 20
        )
        {
            vm.ShowCreate = true;
            // repopulate dropdown
            var machines = db.Gen_Machines
                .Where(m => m.Active == true && m.Deleted != true)
                .OrderBy(m => m.Code)
                .Select(m => new { m.Id, m.Code })
                .ToList();
            vm.MachineList = new SelectList(machines, "Id", "Code", vm.NewParam?.MachineId);

            if (!ModelState.IsValid)
                return View("Index", vm);

            var now = DateTime.UtcNow;
            int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var p = vm.NewParam;
            p.CreatedBy = uid;
            p.CreatedDate = now;
            p.UpdatedBy = uid;
            p.UpdatedDate = now;
            p.Deleted = false;

            db.Gen_MachineParameters.Add(p);
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(
            MachineParametersPageViewModel vm,
            string search = null,
            string activeFilter = null,
            int page = 1,
            int pageSize = 20
        )
        {
            vm.ShowEdit = true;
            if (!ModelState.IsValid)
                return RedirectToAction("Index", new { showEdit = true, editId = vm.EditParam.Id, search, activeFilter, page, pageSize });

            var p = db.Gen_MachineParameters.Find(vm.EditParam.Id);
            if (p == null) return HttpNotFound();

            p.MachineId = vm.EditParam.MachineId;
            p.HeadToHead = vm.EditParam.HeadToHead;
            p.FrameHeight = vm.EditParam.FrameHeight;
            p.Active = vm.EditParam.Active;
            p.Deleted = vm.EditParam.Deleted;
            p.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            p.UpdatedDate = DateTime.UtcNow;

            db.Entry(p).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        // GET: Details
        public ActionResult Details(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // eagerly load the related Gen_Machines so its Name (etc) is populated
            var param = db.Gen_MachineParameters
                          .Include(p => p.Gen_Machines)
                          .FirstOrDefault(p => p.Id == id.Value);

            if (param == null)
                return HttpNotFound();

            return View(param);
        }

        // DELETE
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var p = db.Gen_MachineParameters.Find(id.Value);
            if (p == null) return HttpNotFound();
            return View(p);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var p = db.Gen_MachineParameters.Find(id);
            if (p == null)
                return HttpNotFound();

            // Soft delete — matches the Delete(int id) JSON action and the
            // Create/Update audit convention in this controller; a hard
            // delete here would bypass the audit trail and could violate
            // FK constraints from other tables referencing this row.
            p.Deleted = true;
            p.Active = false;
            p.UpdatedBy = GetCurrentUserId();
            p.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            LogMpAudit(id, "DELETED", oldValue: "Active", newValue: "Deleted");

            return RedirectToAction("Index");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        [HttpGet, Obsolete]
        public ActionResult ExportToExcel(int? id)
        {           
            if (id.HasValue)
            {
                // ---- single‐record export ----
                var p = db.Gen_MachineParameters
                          .Include(x => x.Gen_Machines)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (p == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Parameter");
                    ws.Cells[1, 1].Value = "Field";
                    ws.Cells[1, 2].Value = "Value";
                    using (var hdr = ws.Cells[1, 1, 1, 2]) hdr.Style.Font.Bold = true;

                    int row = 1;
                    void Add(string fld, object val)
                    {
                        row++;
                        ws.Cells[row, 1].Value = fld;
                        ws.Cells[row, 2].Value = val ?? "(none)";
                    }

                    Add("ID", p.Id);
                    Add("Machine Code", p.Gen_Machines?.Code);
                    Add("HeadToHead", p.HeadToHead);
                    Add("FrameHeight", p.FrameHeight);
                    Add("Active", p.Active == true ? "Yes" : "No");
                    Add("Deleted", p.Deleted == true ? "Yes" : "No");
                    Add("CreatedBy", p.CreatedBy);
                    Add("CreatedDate", p.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("UpdatedBy", p.UpdatedBy);
                    Add("UpdatedDate", p.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("MachineId", p.MachineId);

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                 $"Parameter_{p.Id}_Detail.xlsx");
                }
            }
            else
            {
                // ---- full list export ----
                var list = db.Gen_MachineParameters
                             .Include(x => x.Gen_Machines)
                             .OrderBy(x => x.Id)
                             .ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Parameters");
                    var headers = new[] { "ID", "Machine", "HeadToHead", "FrameHeight", "Active", "Deleted" };
                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        int r = i + 2;
                        ws.Cells[r, 1].Value = p.Id;
                        ws.Cells[r, 2].Value = p.Gen_Machines?.Code;
                        ws.Cells[r, 3].Value = p.HeadToHead;
                        ws.Cells[r, 4].Value = p.FrameHeight;
                        ws.Cells[r, 5].Value = p.Active == true ? "Yes" : "No";
                        ws.Cells[r, 6].Value = p.Deleted == true ? "Yes" : "No";
                    }
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                "Parameters.xlsx");
                }
            }
        }

        // GET: /Gen_MachineParameters/ExportToPdf
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(int? id)
        {
            if (id.HasValue)
            {
                var p = db.Gen_MachineParameters
                          .Include(x => x.Gen_Machines)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (p == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {                                   
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 80 };
                    void Cell(string text, bool header = false)
                    {
                        var c = new PdfPCell(new Phrase(text));
                        if (header) c.BackgroundColor = BaseColor.LIGHT_GRAY;
                        table.AddCell(c);
                    }

                    void Row(string f, object v)
                    {
                        Cell(f);
                        Cell(v?.ToString() ?? "(none)");
                    }

                    // header row
                    Cell("Field", true);
                    Cell("Value", true);

                    Row("ID", p.Id);
                    Row("Machine", p.Gen_Machines?.Code);
                    Row("HeadToHead", p.HeadToHead);
                    Row("FrameHeight", p.FrameHeight);
                    Row("Active", p.Active == true ? "Yes" : "No");
                    Row("Deleted", p.Deleted == true ? "Yes" : "No");
                    Row("CreatedBy", p.CreatedBy);
                    Row("CreatedDate", p.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("UpdatedBy", p.UpdatedBy);
                    Row("UpdatedDate", p.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("MachineId", p.MachineId);

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"Parameter_{p.Id}_Detail.pdf");
                }
            }
            else
            {
                var list = db.Gen_MachineParameters
                             .Include(x => x.Gen_Machines)
                             .OrderBy(x => x.Id)
                             .ToList();
                using (var ms = new MemoryStream())
                { 
                        var doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(6) { WidthPercentage = 100 };
                    foreach (var h in new[] { "ID", "Machine", "HeadToHead", "FrameHeight", "Active", "Deleted" })
                        table.AddCell(new PdfPCell(new Phrase(h)) { BackgroundColor = BaseColor.LIGHT_GRAY });
                    foreach (var p in list)
                    {
                        table.AddCell(p.Id.ToString());
                        table.AddCell(p.Gen_Machines?.Code);
                        table.AddCell(p.HeadToHead);
                        table.AddCell(p.FrameHeight?.ToString());
                        table.AddCell(p.Active == true ? "Yes" : "No");
                        table.AddCell(p.Deleted == true ? "Yes" : "No");
                    }

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", "Parameters.pdf");
                }
            }
        }       
    }
}
