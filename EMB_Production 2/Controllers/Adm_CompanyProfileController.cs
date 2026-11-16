using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Infrastructure;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;
using PagedList;
using Rotativa;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/companies")]
    [SessionAuthorize]
    public class Adm_CompanyProfileController : Controller
    {
        private readonly EMB_ProductionEntities1 db = new EMB_ProductionEntities1();
        private const int PageSize = 10;

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Companies)
        // ─────────────────────────────────────────────────────────────
        private int? CurrentChangedBy()
        {
            // Mirrors BaseController.GetSafeCurrentUserId() — read session
            // user id; fall back to the lowest active admin so the audit
            // row never carries NULL when a session has expired mid-call.
            var sid = Session?["UserId"];
            if (sid is int i && i > 0) return i;
            if (sid != null && int.TryParse(sid.ToString(), out int s) && s > 0) return s;
            try
            {
                return db.Adm_Users
                    .Where(u => u.Deleted != true && u.Active == true)
                    .OrderBy(u => u.Id)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private void LogCompanyAudit(int id, string action,
                                     string oldValue = null, string newValue = null,
                                     string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Adm_CompanyProfileAuditLog
       (CompanyId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@cid, @act, @cb, @when, @ov, @nv, @det)",
                    new SqlParameter("@cid",  id),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@cb",   (object)CurrentChangedBy() ?? DBNull.Value),
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
                var entries = db.Database.SqlQuery<CompanyAuditEntry>(@"
SELECT  a.Id, a.CompanyId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Adm_CompanyProfileAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.CompanyId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, companyId = e.CompanyId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeCompanyError(ex) });
            }
        }

        private class CompanyAuditEntry
        {
            public int Id { get; set; }
            public int CompanyId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeCompanyError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Companies)
        // ─────────────────────────────────────────────────────────────
        public class CompanyBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkCompany(List<int> ids, Func<int, Adm_CompanyProfile, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Adm_CompanyProfile.FirstOrDefault(x => x.CompanyID == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.CompanyName ?? ("CO#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeCompanyError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(CompanyBulkIdsDto dto)
        {
            var data = DoBulkCompany(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                db.SaveChanges();
                LogCompanyAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(CompanyBulkIdsDto dto)
        {
            var data = DoBulkCompany(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                db.SaveChanges();
                LogCompanyAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(CompanyBulkIdsDto dto)
        {
            var data = DoBulkCompany(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                db.SaveChanges();
                LogCompanyAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // ---------------- Helpers ----------------
        private int GetCurrentEmployeeId()
        {
            var username = User.Identity?.Name ?? "system";
            var currentUser = db.Adm_Users.FirstOrDefault(u => u.UserName == username);
            return currentUser?.EmployeeId ?? 1;
        }

        private string GetUserNameById(int? userId)
        {
            if (userId == null) return null;
            var user = db.Adm_Users.FirstOrDefault(u => u.Id == userId);
            return user?.UserName ?? userId.ToString();
        }

        // ---------------- LIST PAGED ----------------
        [HttpGet, Route("listPaged")]
        public ActionResult ApiListPaged(string search = "", int page = 1, int pageSize = 10, bool showDeleted = false)
        {
            var query = db.Adm_CompanyProfile.AsQueryable();

            // Show only non-deleted if toggle is off
            if (!showDeleted)
            {
                query = query.Where(c => c.Deleted == false || c.Deleted == null);
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => c.CompanyName.Contains(search));
            }

            var total = query.Count();

            // Get page data (without CompanyID)
            var data = query
                .OrderBy(c => c.CompanyName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.CompanyID,
                    c.CompanyName,
                    c.NickName,
                    c.Email,
                    c.ContactNumber,
                    c.Address,
                    c.Approval,
                    c.Active,
                    c.Deleted,
                    LogoPath = c.Logo
                })
                .ToList();

            return Json(new
            {
                success = true,
                total,
                page,
                pageSize,
                data
            }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet, Route("detail/{id:int}")]
        public ActionResult ApiDetail(int id)
        {
            var c = db.Adm_CompanyProfile.Find(id);
            if (c == null || c.Deleted == true)
                return Json(new { success = false, message = "Company not found." }, JsonRequestBehavior.AllowGet);

            var dto = new
            {
                c.CompanyID,
                c.CompanyName,
                c.NickName,
                c.Email,
                c.ContactNumber,
                c.Address,
                c.LicenseKey,
                c.NTN,
                c.STRN,
                c.CultureCode,
                c.CalendarCode,
                c.Theme,
                c.AmountFormat,
                c.DecimalsInAmount,
                c.Decimal_Points,
                c.CurrencyDefault,
                c.ShowCurrencyName,
                c.StopLogin,
                c.TaxEnabled,
                c.PeriodId,
                c.Language,
                c.RecordTimeStamp,
                c.Approval,
                c.OperatingUserID,
                c.Active,
                c.Deleted,
                c.AllowExtraP,
                c.AllowRM,
                CreatedBy = GetUserNameById(c.CreatedBy),
                c.CreatedDate,
                UpdatedBy = GetUserNameById(c.UpdatedBy),
                c.UpdatedDate,
                LogoPath = string.IsNullOrEmpty(c.Logo) ? null : "/Content/CompanyLogo/" + c.Logo
            };

            return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
        }

        // ---------------- CREATE ----------------
        [HttpPost, Route("create")]
        public ActionResult ApiCreate()
        {
            try
            {
                var form = Request.Form;
                var empId = GetCurrentEmployeeId();
                var now = DateTime.Now;

                var entity = new Adm_CompanyProfile
                {
                    CompanyName = form["companyName"],
                    NickName = form["nickName"],
                    Email = form["email"],
                    ContactNumber = form["contactNumber"],
                    Address = form["address"],
                    Approval = form["approval"],
                    Active = true,
                    Deleted = false,
                    CreatedBy = empId,
                    CreatedDate = now,
                    RecordTimeStamp = now
                };

                SaveLogoFile(entity);
                db.Adm_CompanyProfile.Add(entity);
                db.SaveChanges();

                LogCompanyAudit(entity.CompanyID, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: entity.CompanyName ?? "-");

                return Json(new { success = true, message = "Company created successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Create failed. " + DescribeCompanyError(ex) });
            }
        }

        // ---------------- UPDATE ----------------
        [HttpPost, Route("update/{id:int}")]
        public ActionResult ApiUpdate(int id)
        {
            try
            {
                var entity = db.Adm_CompanyProfile.Find(id);
                if (entity == null)
                    return Json(new { success = false, message = "Company not found." });

                var form = Request.Form;
                var empId = GetCurrentEmployeeId();
                var now = DateTime.Now;

                // Basic company info
                entity.CompanyName = form["companyName"];
                entity.NickName = form["nickName"];
                entity.Email = form["email"];
                entity.ContactNumber = form["contactNumber"];
                entity.Address = form["address"];
                entity.Approval = form["approval"];
                entity.Active = form["active"] == "true";
                if (form.AllKeys.Contains("deleted"))
                {
                    entity.Deleted = form["deleted"] == "true" || form["deleted"] == "True";
                }

                // Optional fields
                entity.LicenseKey = form["licenseKey"];
                entity.NTN = form["ntn"];
                entity.STRN = form["strn"];
                entity.MasterPassword = form["masterPassword"];
                entity.CultureCode = form["cultureCode"];
                entity.CalendarCode = form["calendarCode"];
                entity.Theme = form["theme"];
                entity.CurrencyDefault = form["currencyDefault"];
                entity.DecimalsInAmount = int.TryParse(form["decimalsInAmount"], out var dec) ? dec : 2;
                entity.Decimal_Points = int.TryParse(form["decimal_Points"], out var dp) ? dp : 2;
                entity.PeriodId = int.TryParse(form["periodId"], out var pid) ? (int?)pid : null;
                entity.ShowCurrencyName = form["showCurrencyName"] == "true";
                entity.TaxEnabled = form["taxEnabled"] == "true";
                entity.StopLogin = form["stopLogin"] == "true";
                entity.AllowExtraP = form["allowExtraP"] == "true";

                // OperatingUserID mapping (replace OwnerName)
                entity.OperatingUserID = int.TryParse(form["operatingUserID"], out var ouid) ? ouid : (int?)null;

                // Audit fields
                entity.UpdatedBy = empId;
                entity.UpdatedDate = now;
                entity.RecordTimeStamp = now;

                // Handle OperatingUserID if present in form
                if (int.TryParse(form["operatingUserID"], out int opUser))
                    entity.OperatingUserID = opUser;

                // Logo handling
                SaveLogoFile(entity, replaceOld: true);

                db.SaveChanges();
                var resultVm = new
                {
                    companyID = entity.CompanyID,
                    companyName = entity.CompanyName,
                    nickName = entity.NickName,
                    email = entity.Email,
                    contactNumber = entity.ContactNumber,
                    address = entity.Address,
                    approval = entity.Approval,
                    active = entity.Active,
                    deleted = entity.Deleted ?? false,
                    licenseKey = entity.LicenseKey,
                    ntn = entity.NTN,
                    strn = entity.STRN,
                    cultureCode = entity.CultureCode,
                    calendarCode = entity.CalendarCode,
                    theme = entity.Theme,
                    currencyDefault = entity.CurrencyDefault,
                    decimalsInAmount = entity.DecimalsInAmount,
                    decimal_Points = entity.Decimal_Points,
                    periodId = entity.PeriodId,
                    showCurrencyName = entity.ShowCurrencyName,
                    taxEnabled = entity.TaxEnabled,
                    stopLogin = entity.StopLogin,
                    allowExtraP = entity.AllowExtraP,
                    operatingUserID = entity.OperatingUserID,
                    logo = entity.Logo,
                    updatedBy = entity.UpdatedBy,
                    updatedDate = entity.UpdatedDate,
                    recordTimeStamp = entity.RecordTimeStamp
                };

                return Json(new
                {
                    success = true,
                    message = "Company updated successfully.",
                    data = resultVm
                });
            }
            catch (Exception ex)
            {
                // Log exception for debugging
                return Json(new
                {
                    success = false,
                    message = "Error updating company: " + ex.Message
                });
            }
        }

        // ---------------- DELETE ----------------
        [HttpDelete, Route("api/delete/{id:int}")]
        public ActionResult ApiDelete(int id)
        {
            var company = db.Adm_CompanyProfile.Find(id);
            if (company == null)
                return Json(new { success = false, message = "Company not found." });

            if (company.Deleted == true) // make sure company.Deleted is bool? checked
                return Json(new { success = false, message = "Already deleted." });

            company.Deleted = true;
            company.Active = false;
            company.UpdatedDate = DateTime.UtcNow;
            company.UpdatedBy = GetCurrentEmployeeId();

            if (!string.IsNullOrEmpty(company.Logo))
                DeleteLogoFile(company.Logo);

            db.SaveChanges();

            LogCompanyAudit(id, "DELETED",
                oldValue: "Active", newValue: "Deleted",
                detail: company.CompanyName ?? "-");

            return Json(new { success = true, message = "Company deleted successfully." });
        }
    
    // ---------------- Approval ----------------
    [HttpPut, Route("approval/{id:int}")]
        public ActionResult ApiApproval(int id)
        {
            var company = db.Adm_CompanyProfile.Find(id);
            if (company == null)
                return Json(new { success = false, message = "Company not found." });

            try
            {
                string approval = Request.Form["approval"];
                if (string.IsNullOrEmpty(approval))
                {
                    Request.InputStream.Position = 0;
                    using (var reader = new StreamReader(Request.InputStream))
                    {
                        var body = reader.ReadToEnd();
                        dynamic json = Newtonsoft.Json.JsonConvert.DeserializeObject(body);
                        approval = json?.approval;
                    }
                }

                if (string.IsNullOrEmpty(approval))
                    return Json(new { success = false, message = "Approval value missing." });

                company.Approval = approval;
                company.RecordTimeStamp = DateTime.UtcNow;
                db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ---------------- Mapping helpers (no duplicates) ----------------

        private CompanyProfileViewModel ToProfileVm(Adm_CompanyProfile c)
        {
            if (c == null) return null;

            return new CompanyProfileViewModel
            {
                CompanyID = c.CompanyID,
                CompanyName = c.CompanyName,
                NickName = c.NickName,
                Email = c.Email,
                ContactNumber = c.ContactNumber,
                Address = c.Address,
                Approval = c.Approval,
                Active = c.Active ?? false,
                Logo = string.IsNullOrEmpty(c.Logo) ? null : c.Logo, // Path already stored in DB
                CreatedDate = c.CreatedDate,
                CreatedBy = GetUserNameById(c.CreatedBy),
                UpdatedDate = c.UpdatedDate,
                UpdatedBy = GetUserNameById(c.UpdatedBy)
            };
        }

        // Full detail mapping for detail endpoint (includes all fields)
        private CompanyDetailDto ToDetailDto(Adm_CompanyProfile c)
        {
            if (c == null) return null;

            return new CompanyDetailDto
            {
                CompanyID = c.CompanyID,
                CompanyName = c.CompanyName,
                NickName = c.NickName,
                Email = c.Email,
                ContactNumber = c.ContactNumber,
                Address = c.Address,
                Approval = c.Approval,
                Active = c.Active ?? false,
                Deleted = c.Deleted ?? false,
                Logo = string.IsNullOrEmpty(c.Logo) ? null : c.Logo,

                LicenseKey = c.LicenseKey,
                NTN = c.NTN,
                STRN = c.STRN,
                CultureCode = c.CultureCode,
                CalendarCode = c.CalendarCode,
                Theme = c.Theme,
                AmountFormat = c.AmountFormat,
                DecimalsInAmount = c.DecimalsInAmount,
                Decimal_Points = c.Decimal_Points,
                CurrencyDefault = c.CurrencyDefault,
                ShowCurrencyName = c.ShowCurrencyName,
                StopLogin = c.StopLogin,
                TaxEnabled = c.TaxEnabled,
                PeriodId = c.PeriodId,
                Language = c.Language,
                OperatingUserID = c.OperatingUserID,
                AllowExtraP = c.AllowExtraP,
                AllowRM = c.AllowRM,

                // Audit info
                CreatedBy = c.CreatedBy,
                CreatedDate = c.CreatedDate,
                UpdatedBy = c.UpdatedBy,
                UpdatedDate = c.UpdatedDate,
                RecordTimeStamp = c.RecordTimeStamp
            };
        }

        // ---------------- Map DTO → Entity (if you later accept DTO body) ----------------
        private void MapToEntity(CompanyDetailDto vm, Adm_CompanyProfile entity)
        {
            if (vm == null || entity == null) return;

            entity.CompanyName = vm.CompanyName;
            entity.NickName = vm.NickName;
            entity.Email = vm.Email;
            entity.ContactNumber = vm.ContactNumber;
            entity.Address = vm.Address;
            entity.Logo = vm.Logo;
            entity.Approval = vm.Approval;
            entity.Active = vm.Active;
            entity.Deleted = vm.Deleted;
            entity.LicenseKey = vm.LicenseKey;
            entity.NTN = vm.NTN;
            entity.STRN = vm.STRN;
            entity.MasterPassword = vm.MasterPassword;
            entity.CultureCode = vm.CultureCode;
            entity.CalendarCode = vm.CalendarCode;
            entity.Theme = vm.Theme;
            entity.AmountFormat = vm.AmountFormat;
            entity.DecimalsInAmount = vm.DecimalsInAmount;
            entity.Decimal_Points = vm.Decimal_Points;
            entity.CurrencyDefault = vm.CurrencyDefault;
            entity.ShowCurrencyName = vm.ShowCurrencyName;
            entity.StopLogin = vm.StopLogin;
            entity.TaxEnabled = vm.TaxEnabled;
            entity.PeriodId = vm.PeriodId;
            entity.Language = vm.Language;
            entity.OperatingUserID = vm.OperatingUserID;
            entity.AllowExtraP = vm.AllowExtraP;
            entity.AllowRM = vm.AllowRM;
        }

        // Existing method, slightly improved
        private void SaveLogoFile(Adm_CompanyProfile company, bool replaceOld = false)
        {
            try
            {
                // Check file presence
                if (Request.Files.Count == 0 || Request.Files["logoFile"] == null)
                    return;

                var file = Request.Files["logoFile"];
                if (file == null || file.ContentLength <= 0)
                    return;

                // Ensure folder exists
                var folderPath = Server.MapPath("~/Content/CompanyLogo/");
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                // Delete old logo if requested and exists
                if (replaceOld && !string.IsNullOrEmpty(company.Logo))
                    DeleteLogoFile(company.Logo);

                // Generate unique filename with proper extension
                var ext = Path.GetExtension(file.FileName);
                var fileName = $"{Guid.NewGuid()}{ext}";
                var filePath = Path.Combine(folderPath, fileName);

                // Save file
                file.SaveAs(filePath);

                // Update DB field only after success
                company.Logo = fileName;
            }
            catch (Exception ex)
            {
                // Don't break the update — just log internally if you have a logger
                System.Diagnostics.Debug.WriteLine($"[SaveLogoFile] Error saving logo: {ex.Message}");
                // Optionally: keep the old logo if saving failed
            }
        }


        private void DeleteLogoFile(string logoFileName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(logoFileName))
                    return;

                var folderPath = Server.MapPath("~/Content/CompanyLogo/");
                var filePath = Path.Combine(folderPath, logoFileName);

                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeleteLogoFile] Error deleting logo: {ex.Message}");
            }
        }

        private int? TryParseNullableInt(string input)
        {
            if (int.TryParse(input, out int value))
                return value;
            return null;
        }
    


        // GET: /Adm_CompanyProfile/Index
        public ActionResult Index(string search,string activeFilter,int? parentFilter,int? page,bool showCreate = false,bool showEdit = false,int? editId = null )
        {
                int pageNumber = page ?? 1;
                // Build base VM & wire up create/edit
                var vm = BuildViewModel(search, activeFilter, parentFilter, pageNumber, PageSize);
                vm.ShowCreate = showCreate;
                vm.NewCompany = showCreate ? new Adm_CompanyProfile() : null;
                vm.ShowEdit = showEdit;
                vm.EditId = editId;
                vm.EditCompany = (showEdit && editId.HasValue) ? db.Adm_CompanyProfile.Find(editId.Value): null;

                if (Request.IsAjaxRequest())
                    return PartialView("Index", vm);

                return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(CompanyPageViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                int page = vm.Companies?.PageNumber ?? 1;
                var errorVm = BuildViewModel(vm.Search, vm.ActiveFilter, vm.ParentFilter, page, PageSize);

                errorVm.NewCompany = vm.NewCompany;
                errorVm.ShowCreate = true;

                if (Request.IsAjaxRequest())
                    return PartialView("Index", errorVm);

                return View("Index", errorVm);
            }

            // Handle file upload
            
                if (vm.LogoFile != null && vm.LogoFile.ContentLength > 0)
                {
                    var folder = Server.MapPath("~/Content/CompanyLogo");
                    Directory.CreateDirectory(folder);

                    var fn = Path.GetFileName(vm.LogoFile.FileName);
                    var fullPath = Path.Combine(folder, fn);
                    vm.LogoFile.SaveAs(fullPath);

                    vm.NewCompany.Logo = "/Content/CompanyLogo/" + fn;
                }


                vm.NewCompany.Deleted = false;

            int currentUserId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            vm.NewCompany.CreatedBy = currentUserId;
            vm.NewCompany.CreatedDate = DateTime.UtcNow;
            vm.NewCompany.RecordTimeStamp = DateTime.UtcNow;

            db.Adm_CompanyProfile.Add(vm.NewCompany);
            db.SaveChanges();

            var newVm = BuildViewModel(vm.Search, vm.ActiveFilter, vm.ParentFilter, 1, PageSize);
            if (Request.IsAjaxRequest())
                return PartialView("Index", newVm);

            return RedirectToAction("Index", new
            {
                search = vm.Search,
                activeFilter = vm.ActiveFilter,
                parentFilter = vm.ParentFilter,
                page = 1
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(CompanyPageViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                int page = vm.Companies?.PageNumber ?? 1;
                var errorVm = BuildViewModel(vm.Search, vm.ActiveFilter, vm.ParentFilter, page, PageSize);

                errorVm.EditCompany = vm.EditCompany;
                errorVm.ShowEdit = true;

                if (Request.IsAjaxRequest())
                    return PartialView("Index", errorVm);
                return View("Index", errorVm);
            }

            var company = db.Adm_CompanyProfile.Find(vm.EditCompany.CompanyID);
            if (company == null) return HttpNotFound();

            // Handle file upload
            if (vm.LogoFile != null && vm.LogoFile.ContentLength > 0)
            {
                var folder = Server.MapPath("~/Content/CompanyLogo");
                Directory.CreateDirectory(folder);

                var fn = Path.GetFileName(vm.LogoFile.FileName);
                var fullPath = Path.Combine(folder, fn);
                vm.LogoFile.SaveAs(fullPath);

                vm.NewCompany.Logo = "/Content/CompanyLogo/" + fn;
            }

            // ✅ Copy all fields explicitly
            company.CompanyName = vm.EditCompany.CompanyName;
            company.NickName = vm.EditCompany.NickName;
            company.LicenseKey = vm.EditCompany.LicenseKey;
            company.NTN = vm.EditCompany.NTN;
            company.STRN = vm.EditCompany.STRN;
            company.ParentId = vm.EditCompany.ParentId;
            company.MasterPassword = vm.EditCompany.MasterPassword;

            company.Address = vm.EditCompany.Address;
            company.Email = vm.EditCompany.Email;
            company.ContactNumber = vm.EditCompany.ContactNumber;

            company.ShowCurrencyName = vm.EditCompany.ShowCurrencyName;
            company.StopLogin = vm.EditCompany.StopLogin;
            company.TaxEnabled = vm.EditCompany.TaxEnabled;
            company.Active = vm.EditCompany.Active;

            // Optional: update period/culture/format/etc if editable

            // Metadata
            int currentUserId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            company.UpdatedBy = currentUserId;
            company.UpdatedDate = DateTime.UtcNow;
            company.RecordTimeStamp = DateTime.UtcNow;

            db.Entry(company).State = EntityState.Modified;
            db.SaveChanges();

            int currentPage = vm.Companies?.PageNumber ?? 1;
            var newVm = BuildViewModel(vm.Search, vm.ActiveFilter, vm.ParentFilter, currentPage, PageSize);

            if (Request.IsAjaxRequest())
                return PartialView("Index", newVm);

            return RedirectToAction("Index", new
            {
                search = vm.Search,
                activeFilter = vm.ActiveFilter,
                parentFilter = vm.ParentFilter,
                page = currentPage
            });
        }

        // GET: /Adm_CompanyProfile/DetailsFromIndex/5
        [HttpGet]
        [Route("details-from-index/{id:int}")]
        public PartialViewResult DetailsFromIndex(int id)
        {
            var company = db.Adm_CompanyProfile.Find(id);
            if (company == null)
                return PartialView("_CompanyNotFound");

            return PartialView("_CompanyDetail", company);
        }

        public ActionResult DeleteFromIndex(int id,string search,string activeFilter,int? parentFilter,int? page )
        {
            var company = db.Adm_CompanyProfile.Find(id);
            if (company == null) return HttpNotFound();

            ViewBag.Search = search;
            ViewBag.ActiveFilter = activeFilter;
            ViewBag.ParentFilter = parentFilter;
            ViewBag.Page = page;
          
            return PartialView("_CompanyDeleteConfirmation", company);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult DeleteFromIndexConfirmed(int id,string search,string activeFilter,int? parentFilter,int? page)
        {
            var company = db.Adm_CompanyProfile.Find(id);
            if (company == null)
                return HttpNotFound();

            // **NEW**: check for any users still assigned to this company
            bool hasUsers = db.Adm_Users.Any(u => u.CompanyId == id);
            if (hasUsers)
            {
                // return a 409 Conflict with a friendly message
                Response.StatusCode = 409;
                return Content("Cannot delete this company because one or more users are still assigned to it.");
            }

            // safe to delete
            db.Adm_CompanyProfile.Remove(company);
            db.SaveChanges();

            // rebuild VM
            int pageNumber = page ?? 1;
            var vm = BuildViewModel(search, activeFilter, parentFilter, pageNumber, PageSize);

            if (Request.IsAjaxRequest())
                return PartialView("_CompanyTable", vm);

            return RedirectToAction("Index", new { search, activeFilter, parentFilter, page = pageNumber });
        }

        private CompanyPageViewModel BuildViewModel(string search, string activeFilter, int? parentFilter, int page, int pageSize)
        {
            // 1) Base EF query
            var query = db.Adm_CompanyProfile.AsQueryable();

            // 2) Filters
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.CompanyName.Contains(search));

            if (activeFilter == "active")
                query = query.Where(c => c.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(c => c.Active == false);

            if (parentFilter.HasValue)
                query = query.Where(c => c.ParentId == parentFilter.Value);

            // 3) Projection to DTO
            var dtoQuery = query
                .OrderBy(c => c.CompanyName)
                .Select(c => new CompanyDto
                {
                    CompanyID = c.CompanyID,
                    CompanyName = c.CompanyName,
                    Logo = c.Logo,
                    Email = c.Email,
                    ContactNumber = c.ContactNumber,
                    Address = c.Address,
                    UserCount = c.Adm_Users.Count(),
                    ShowCurrencyName = c.ShowCurrencyName ?? false,
                    StopLogin = c.StopLogin ?? false,
                    TaxEnabled = c.TaxEnabled ?? false,
                    Active = c.Active ?? false,
                    Deleted = c.Deleted ?? false
                });

            // 4) Paging
            var pagedList = dtoQuery.ToPagedList(page, pageSize);

            // 5) Build and return the ViewModel
            return new CompanyPageViewModel
            {
                Companies = pagedList,
                Search = search,
                ActiveFilter = activeFilter,
                ParentFilter = parentFilter,

                // Parent dropdown: all companies
                ParentList = new SelectList(db.Adm_CompanyProfile.OrderBy(c => c.CompanyName)
                                        .Select(c => new { c.CompanyID, c.CompanyName })
                                        .ToList(),"CompanyID","CompanyName",parentFilter),

                // Theme options: distinct, non-null, ordered
                ThemeOptions = db.Adm_CompanyProfile.Select(c => c.Theme)
                                      .Where(t => !string.IsNullOrEmpty(t))
                                      .Distinct().OrderBy(t => t).ToArray(),

                // Language options: distinct, non-null, ordered
                LanguageOptions = db.Adm_CompanyProfile.Select(c => c.Language)
                                      .Where(l => !string.IsNullOrEmpty(l))
                                      .Distinct().OrderBy(l => l).ToArray(),

                // initial state for inline forms
                ShowCreate = false,
                ShowEdit = false
            };
        }


        private string GetLogoFolder()
        {
            var folder = Server.MapPath("~/Content/CompanyLogo");
            Directory.CreateDirectory(folder);
            return folder;
        }
   
            [HttpGet, Obsolete]
            public ActionResult ExportToExcel(int? id, string search = null, string activeFilter = null, int? parentFilter = null)
                {                   
                    if (id.HasValue)
                    {
                        var c = db.Adm_CompanyProfile.Find(id.Value);
                        if (c == null) return HttpNotFound();

                        using (var pkg = new ExcelPackage())
                        {
                            var ws = pkg.Workbook.Worksheets.Add("CompanyDetail");
                            ws.Cells[1, 1].Value = "Field"; ws.Cells[1, 2].Value = "Value";
                            using (var hdr = ws.Cells[1, 1, 1, 2]) hdr.Style.Font.Bold = true;
                            int row = 1;
                            void Add(string fld, object val)
                            {
                                row++;
                                ws.Cells[row, 1].Value = fld;
                                ws.Cells[row, 2].Value = val ?? "(none)";
                            }
                            Add("CompanyID", c.CompanyID);
                            Add("CompanyName", c.CompanyName);
                            Add("Email", c.Email);
                            Add("ContactNumber", c.ContactNumber);
                            Add("Address", c.Address);
                            Add("Active", c.Active == true ? "Yes" : "No");
                            Add("Deleted", c.Deleted == true ? "Yes" : "No");
                            Add("CreatedDate", c.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                            Add("UpdatedDate", c.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                            ws.Cells[ws.Dimension.Address].AutoFitColumns();
                            return File(pkg.GetAsByteArray(),
                                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                        $"Company_{c.CompanyID}_Detail.xlsx");
                        }
                    }

                    var q = db.Adm_CompanyProfile.Where(c => c.Deleted != true);
                    if (!string.IsNullOrWhiteSpace(search))
                        q = q.Where(c => c.CompanyName.Contains(search));
                    if (!string.IsNullOrWhiteSpace(activeFilter))
                    {
                        bool want = activeFilter.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                        q = q.Where(c => (c.Active ?? false) == want);
                    }
                    if (parentFilter.HasValue)
                        q = q.Where(c => c.ParentId == parentFilter.Value);

                    var list = q.OrderBy(c => c.CompanyName).ToList();

                    using (var pkg = new ExcelPackage())
                    {
                        var ws = pkg.Workbook.Worksheets.Add("Companies");
                        var headers = new[] { "CompanyID", "CompanyName", "Email", "ContactNumber", "Active", "Deleted" };
                        for (int c = 0; c < headers.Length; c++)
                        {
                            ws.Cells[1, c + 1].Value = headers[c];
                            ws.Cells[1, c + 1].Style.Font.Bold = true;
                        }
                        for (int i = 0; i < list.Count; i++)
                        {
                            var x = list[i];
                            int r = i + 2;
                            ws.Cells[r, 1].Value = x.CompanyID;
                            ws.Cells[r, 2].Value = x.CompanyName;
                            ws.Cells[r, 3].Value = x.Email;
                            ws.Cells[r, 4].Value = x.ContactNumber;
                            ws.Cells[r, 5].Value = x.Active == true ? "Yes" : "No";
                            ws.Cells[r, 6].Value = x.Deleted == true ? "Yes" : "No";
                        }
                        ws.Cells[ws.Dimension.Address].AutoFitColumns();
                        return File(pkg.GetAsByteArray(),
                                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                    "Companies.xlsx");
                    }
                }

        // GET: AdmCompanyProfile/ExportToPdf
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(
                                        int? id,
                                        string search = null,
                                        string activeFilter = null,
                                        int? parentFilter = null)
        {
            // 1) Single‐Company detail PDF
            if (id.HasValue)
            {
                var c = db.Adm_CompanyProfile.Find(id.Value);
                if (c == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new iTextSharp.text.Document(
                                  iTextSharp.text.PageSize.A4,
                                  25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    // build a 2-column table
                    var tbl = new PdfPTable(2) { WidthPercentage = 80 };

                    void Cell(string text, bool hdr = false)
                    {
                        var cell = new PdfPCell(new Phrase(text));
                        if (hdr) cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        tbl.AddCell(cell);
                    }
                    Cell("Field", true);
                    Cell("Value", true);

                    void Row(string field, object value)
                    {
                        Cell(field);
                        Cell(value?.ToString() ?? "(none)");
                    }

                    Row("CompanyID", c.CompanyID);
                    Row("CompanyName", c.CompanyName);
                    Row("Email", c.Email);
                    Row("ContactNumber", c.ContactNumber);
                    Row("ShowCurrencyName", c.ShowCurrencyName == true ? "Yes" : "No");
                    Row("StopLogin", c.StopLogin == true ? "Yes" : "No");
                    Row("TaxEnabled", c.TaxEnabled == true ? "Yes" : "No");
                    Row("Active", c.Active == true ? "Yes" : "No");
                    Row("Deleted", c.Deleted == true ? "Yes" : "No");
                    // … add more rows if desired …

                    doc.Add(tbl);
                    doc.Close();

                    var fileName = $"Company_{c.CompanyID}_Detail.pdf";
                    return File(ms.ToArray(),
                                "application/pdf",
                                fileName);
                }
            }

            // 2) Full companies list PDF
            //    — build the EF query with your filters
            var q = db.Adm_CompanyProfile.Where(c => c.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(c => c.CompanyName.Contains(search));

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                q = q.Where(c => (c.Active ?? false) == want);
            }

            if (parentFilter.HasValue)
                q = q.Where(c => c.ParentId == parentFilter.Value);

            // 3) Project into your DTO, **not** an anonymous type
            var list2 = q
                .OrderBy(c => c.CompanyName)
                .Select(c => new CompanyDto
                {
                    CompanyID = c.CompanyID,
                    CompanyName = c.CompanyName,
                    Email = c.Email,
                    ContactNumber = c.ContactNumber,
                    ShowCurrencyName = c.ShowCurrencyName ?? false,
                    StopLogin = c.StopLogin ?? false,
                    TaxEnabled = c.TaxEnabled ?? false,
                    Active = c.Active ?? false,
                    Deleted = c.Deleted ?? false
                })
                .ToList();

            // 4) Render the same ExportCompaniesTable view, now strongly typed
            return new ViewAsPdf("ExportCompaniesTable", list2)
            {
                FileName = "Companies_List.pdf",
                PageSize = Rotativa.Options.Size.A4
            };
        }

        [AllowAnonymous]
        public ActionResult Logo(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest, "Filename is required");

            // Remove leading slashes for safety and convert to a safe relative path
            var safeVirtualPath = fileName.TrimStart('~', '/');

            // Map the virtual path to a physical one
            var physicalPath = Server.MapPath("~/" + safeVirtualPath);

            if (string.IsNullOrEmpty(physicalPath) || !System.IO.File.Exists(physicalPath))
                return HttpNotFound("Logo not found");

            var mimeType = MimeMapping.GetMimeMapping(physicalPath);
            return File(physicalPath, mimeType);
        }


        protected override void Dispose(bool disposing)
        {
                    if (disposing) db.Dispose();
                    base.Dispose(disposing);
        }
     }
}