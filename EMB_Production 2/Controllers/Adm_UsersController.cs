using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using PagedList;
using OfficeOpenXml;
using Rotativa;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using EMB_Production.Infrastructure;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/users")]
    [SessionAuthorize]
    public class Adm_UsersController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Users)
        //  Audit table is sensitive — a user-management change trail is
        //  high-value for compliance. Append-only, best-effort writes
        //  (swallow on missing table).
        // ─────────────────────────────────────────────────────────────
        private void LogUserAudit(int id, string action,
                                  string oldValue = null, string newValue = null,
                                  string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Adm_UsersAuditLog
       (UserId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@uid, @act, @cb, @when, @ov, @nv, @det)",
                    new SqlParameter("@uid",  id),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@cb",   GetSafeCurrentUserId()),
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
                var entries = db.Database.SqlQuery<UserAuditEntry>(@"
SELECT  a.Id, a.UserId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Adm_UsersAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.UserId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, userId = e.UserId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeUserError(ex) });
            }
        }

        private class UserAuditEntry
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeUserError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Users)
        //  NB: deleting a user is irreversible from the UI side (soft-
        //  delete); admins should bulk-deactivate first, then delete.
        // ─────────────────────────────────────────────────────────────
        public class UserBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkUser(List<int> ids, Func<int, Adm_Users, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Adm_Users.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.UserName ?? ("USR#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeUserError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(UserBulkIdsDto dto)
        {
            var data = DoBulkUser(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetSafeCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogUserAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(UserBulkIdsDto dto)
        {
            var data = DoBulkUser(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetSafeCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogUserAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(UserBulkIdsDto dto)
        {
            var data = DoBulkUser(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetSafeCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogUserAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        
        // ---------------- LIST ----------------
        [HttpGet, Route("list")]
        public ActionResult List(
            string search = "",
            int page = 1,
            int pageSize = 10,
            int? roleId = null,
            int? branchId = null,
            int? companyId = null,
            int? designationId = null,
            string activeFilter = "",
            string dashboardFilter = "")
        {
            search = (search ?? "").Trim();
            activeFilter = (activeFilter ?? "").Trim().ToLower();
            dashboardFilter = (dashboardFilter ?? "").Trim().ToLower();

            var query = db.Adm_Users.Where(u => u.Deleted == null || u.Deleted == false);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(u =>
                    u.FirstName.Contains(search) ||
                    u.LastName.Contains(search) ||
                    u.UserName.Contains(search) ||
                    u.EmailAddress.Contains(search) ||
                    u.MobilePrimary.Contains(search));

            if (roleId.HasValue && roleId.Value > 0)
                query = query.Where(u => u.RoleId == roleId.Value);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(u => u.BranchId == branchId.Value);

            if (companyId.HasValue && companyId.Value > 0)
                query = query.Where(u => u.CompanyId == companyId.Value);

            if (designationId.HasValue && designationId.Value > 0)
                query = query.Where(u => u.DesignationId == designationId.Value);

            if (activeFilter == "active")
                query = query.Where(u => u.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(u => u.Active == false || u.Active == null);

            if (dashboardFilter == "yes")
                query = query.Where(u => u.DashboardAccess == true);
            else if (dashboardFilter == "no")
                query = query.Where(u => u.DashboardAccess == false || u.DashboardAccess == null);

            var total = query.Count();

            // Summary aggregates computed over the FILTERED set (not paginated).
            var summarySource = query.Select(u => new
            {
                u.Id,
                u.Active,
                u.DashboardAccess,
                u.RoleId,
                RoleName = u.Adm_Roles.Name,
                u.BranchId,
                BranchName = u.Branches.Name,
                u.LastLoginDate
            }).ToList();

            int activeCount = summarySource.Count(u => u.Active == true);
            int inactiveCount = summarySource.Count - activeCount;
            int dashboardCount = summarySource.Count(u => u.DashboardAccess == true);
            int loggedInCount = summarySource.Count(u => u.LastLoginDate.HasValue);
            int neverLoggedInCount = summarySource.Count - loggedInCount;
            int distinctRoles = summarySource.Where(u => u.RoleId.HasValue).Select(u => u.RoleId).Distinct().Count();
            int distinctBranches = summarySource.Where(u => u.BranchId.HasValue).Select(u => u.BranchId).Distinct().Count();

            var byRole = summarySource
                .Where(u => u.RoleId.HasValue)
                .GroupBy(u => new { u.RoleId, u.RoleName })
                .Select(g => new { roleId = g.Key.RoleId, roleName = g.Key.RoleName ?? "-", count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var byBranch = summarySource
                .Where(u => u.BranchId.HasValue)
                .GroupBy(u => new { u.BranchId, u.BranchName })
                .Select(g => new { branchId = g.Key.BranchId, branchName = g.Key.BranchName ?? "-", count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var summary = new
            {
                count = summarySource.Count,
                activeCount,
                inactiveCount,
                dashboardCount,
                loggedInCount,
                neverLoggedInCount,
                distinctRoles,
                distinctBranches,
                byRole,
                byBranch
            };

            var pageRows = query
                .OrderBy(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.EmployeeId,
                    u.UserName,
                    FullName = (u.FirstName + " " + u.LastName).Trim(),
                    u.EmailAddress,
                    u.MobilePrimary,
                    u.Landline,
                    u.BranchId,
                    BranchName = u.Branches.Name,
                    u.RoleId,
                    RoleName = u.Adm_Roles.Name,
                    u.CompanyId,
                    CompanyName = u.Adm_CompanyProfile.CompanyName,
                    u.DesignationId,
                    u.Active,
                    u.DashboardAccess,
                    u.Approvel,
                    u.FacialRecognition,
                    u.CreatedBy,
                    u.CreatedDate,
                    u.UpdatedBy,
                    u.UpdatedDate,
                    u.LastLoginDate
                }).ToList();

            // Hydrate DesignationName via the raw-SQL helper so this works
            // before the EDMX has been refreshed for Adm_Designations.
            var designationLookup = GetDesignationsLookup();

            var data = pageRows.Select(u => new
            {
                u.Id,
                u.EmployeeId,
                u.UserName,
                u.FullName,
                u.EmailAddress,
                u.MobilePrimary,
                u.Landline,
                u.BranchId,
                u.BranchName,
                u.RoleId,
                u.RoleName,
                u.CompanyId,
                u.CompanyName,
                u.DesignationId,
                DesignationName = (u.DesignationId.HasValue && designationLookup.ContainsKey(u.DesignationId.Value))
                    ? designationLookup[u.DesignationId.Value]
                    : null,
                u.Active,
                u.DashboardAccess,
                u.Approvel,
                u.FacialRecognition,
                u.CreatedBy,
                u.CreatedDate,
                u.UpdatedBy,
                u.UpdatedDate,
                u.LastLoginDate
            }).ToList();

            return JsonSuccess("Users loaded.", new { items = data, totalItems = total, summary });
        }

        // ---------------- DESIGNATIONS DROPDOWN ----------------
        // Reads directly from dbo.Adm_Designations via raw SQL so the
        // endpoint works regardless of whether the EDMX has been
        // refreshed to include the new entity.
        [HttpGet, Route("designations")]
        public ActionResult GetDesignations()
        {
            try
            {
                var rows = db.Database.SqlQuery<DesignationDto>(
                    @"SELECT Id, Name, Code, Active
                      FROM   dbo.Adm_Designations
                      WHERE  (Deleted IS NULL OR Deleted = 0)
                        AND  (Active = 1 OR Active IS NULL)
                      ORDER BY Name").ToList();

                return JsonSuccess("OK", rows);
            }
            catch (Exception ex)
            {
                // Most common cause: SQL script hasn't been run yet.
                return JsonError("Failed to load designations. Run App_Data/Scripts/CreateAdmDesignations.sql first.", ex);
            }
        }

        // Map of DesignationId → Name. Cached per request via raw SQL.
        private System.Collections.Generic.Dictionary<int, string> GetDesignationsLookup()
        {
            try
            {
                var rows = db.Database.SqlQuery<DesignationDto>(
                    @"SELECT Id, Name, Code, Active
                      FROM   dbo.Adm_Designations
                      WHERE  (Deleted IS NULL OR Deleted = 0)").ToList();

                return rows.ToDictionary(r => r.Id, r => r.Name);
            }
            catch
            {
                // Table missing: return empty so existing flows still work.
                return new System.Collections.Generic.Dictionary<int, string>();
            }
        }

        // ---------------- 360 PROFILE ----------------
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var u = (
                    from x in db.Adm_Users
                    join br in db.Branches on x.BranchId equals br.Id into gbr
                    from br in gbr.DefaultIfEmpty()
                    join rl in db.Adm_Roles on x.RoleId equals rl.Id into grl
                    from rl in grl.DefaultIfEmpty()
                    join cp in db.Adm_CompanyProfile on x.CompanyId equals cp.CompanyID into gcp
                    from cp in gcp.DefaultIfEmpty()
                    join cu in db.Adm_Users on x.CreatedBy equals cu.Id into gcu
                    from cu in gcu.DefaultIfEmpty()
                    join uu in db.Adm_Users on x.UpdatedBy equals uu.Id into guu
                    from uu in guu.DefaultIfEmpty()
                    where x.Id == id
                    select new
                    {
                        x.Id,
                        x.EmployeeId,
                        x.UserName,
                        x.FirstName,
                        x.LastName,
                        x.EmailAddress,
                        x.MobilePrimary,
                        x.MobileSecondary,
                        x.Landline,
                        x.LandlineExt,
                        x.FacialRecognition,
                        x.DesignationId,
                        x.IsSalesmanLinked,
                        x.SalesmanCode,

                        x.BranchId,
                        BranchName = br.Name,
                        x.RoleId,
                        RoleName = rl.Name,
                        x.CompanyId,
                        CompanyName = cp.CompanyName,

                        x.Active,
                        x.DashboardAccess,
                        x.Approvel,

                        x.LastLoginDate,
                        x.LastPasswordChangeDate,
                        x.CreatedDate,
                        x.UpdatedDate,

                        CreatedByEmpId = cu.EmployeeId,
                        CreatedByFirst = cu.FirstName,
                        CreatedByLast = cu.LastName,
                        UpdatedByEmpId = uu.EmployeeId,
                        UpdatedByFirst = uu.FirstName,
                        UpdatedByLast = uu.LastName
                    }
                ).FirstOrDefault();

                if (u == null) return JsonError("User not found.");

                var culture = new CultureInfo("en-US");

                // Designation name lookup via raw SQL (EDMX-free).
                string designationName = null;
                if (u.DesignationId.HasValue && u.DesignationId.Value > 0)
                {
                    var lookup = GetDesignationsLookup();
                    if (lookup.ContainsKey(u.DesignationId.Value))
                        designationName = lookup[u.DesignationId.Value];
                }

                // Siblings: peers in the same role within the same branch.
                var siblings = db.Adm_Users
                    .Where(s => s.Id != u.Id
                             && (s.Deleted == null || s.Deleted == false)
                             && s.RoleId == u.RoleId
                             && s.BranchId == u.BranchId)
                    .OrderBy(s => s.EmployeeId)
                    .Take(20)
                    .Select(s => new
                    {
                        s.Id,
                        s.EmployeeId,
                        s.UserName,
                        FullName = (s.FirstName + " " + s.LastName).Trim(),
                        s.EmailAddress,
                        s.Active
                    })
                    .ToList();

                // Activity counters (Created/Updated by this user across the app).
                int createdCount = db.Adm_Users.Count(x => x.CreatedBy == u.Id && (x.Deleted == null || x.Deleted == false));
                int branchPeersCount = db.Adm_Users.Count(x => x.BranchId == u.BranchId && (x.Deleted == null || x.Deleted == false));
                int rolePeersCount = db.Adm_Users.Count(x => x.RoleId == u.RoleId && (x.Deleted == null || x.Deleted == false));

                var fullName = ($"{u.FirstName} {u.LastName}").Trim();

                var header = new
                {
                    id = u.Id,
                    employeeId = u.EmployeeId,
                    userName = u.UserName,
                    fullName,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    emailAddress = u.EmailAddress,
                    mobilePrimary = u.MobilePrimary,
                    mobileSecondary = u.MobileSecondary,
                    landline = u.Landline,
                    landlineExt = u.LandlineExt,
                    facialRecognition = u.FacialRecognition,
                    designationId = u.DesignationId,
                    designationName = designationName ?? "-",

                    branchId = u.BranchId,
                    branchName = u.BranchName ?? "-",
                    roleId = u.RoleId,
                    roleName = u.RoleName ?? "-",
                    companyId = u.CompanyId,
                    companyName = u.CompanyName ?? "-",

                    active = u.Active == true,
                    dashboardAccess = u.DashboardAccess == true,
                    approvel = u.Approvel,
                    isSalesmanLinked = u.IsSalesmanLinked == true,
                    salesmanCode = u.SalesmanCode,

                    lastLoginText = u.LastLoginDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "Never",
                    lastPasswordChangeText = u.LastPasswordChangeDate?.ToString("dd MMM yyyy", culture) ?? "Never",
                    createdDateText = u.CreatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",
                    updatedDateText = u.UpdatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",

                    createdBy = u.CreatedByEmpId,
                    createdByName = ($"{u.CreatedByFirst} {u.CreatedByLast}").Trim(),
                    updatedBy = u.UpdatedByEmpId,
                    updatedByName = ($"{u.UpdatedByFirst} {u.UpdatedByLast}").Trim()
                };

                var kpis = new
                {
                    createdCount,
                    branchPeersCount,
                    rolePeersCount,
                    siblingsCount = siblings.Count
                };

                return JsonSuccess("OK", new { header, kpis, siblings });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load profile.", ex);
            }
        }

        // ---------------- DETAIL ----------------
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var raw = (
                from u in db.Adm_Users
                join br in db.Branches on u.BranchId equals br.Id into gbr
                from br in gbr.DefaultIfEmpty()
                join rl in db.Adm_Roles on u.RoleId equals rl.Id into grl
                from rl in grl.DefaultIfEmpty()
                join cp in db.Adm_CompanyProfile on u.CompanyId equals cp.CompanyID into gcp
                from cp in gcp.DefaultIfEmpty()
                join cu in db.Adm_Users on u.CreatedBy equals cu.Id into gcu
                from cu in gcu.DefaultIfEmpty()
                join uu in db.Adm_Users on u.UpdatedBy equals uu.Id into guu
                from uu in guu.DefaultIfEmpty()
                where u.Id == id
                select new
                {
                    u.Id,
                    u.EmployeeId,
                    u.UserName,
                    u.FirstName,
                    u.LastName,
                    u.EmailAddress,
                    u.MobilePrimary,
                    u.Landline,

                    u.BranchId,
                    BranchName = br.Name,

                    u.RoleId,
                    RoleName = rl.Name,

                    u.CompanyId,
                    CompanyName = cp.CompanyName,

                    u.DesignationId,

                    u.Active,
                    u.DashboardAccess,
                    u.Approvel,
                    u.FacialRecognition,

            // AUDIT
            CreatedByEmployeeId = cu.EmployeeId,
                    UpdatedByEmployeeId = uu.EmployeeId,

                    CreatedByFirst = cu.FirstName,
                    CreatedByLast = cu.LastName,
                    UpdatedByFirst = uu.FirstName,
                    UpdatedByLast = uu.LastName,

                    u.CreatedDate,
                    u.UpdatedDate,
                    u.LastLoginDate
                }
            ).FirstOrDefault();

            if (raw == null)
                return Json(new { success = false, message = "User not found." }, JsonRequestBehavior.AllowGet);

            var culture = new CultureInfo("en-US");

            var dto = new
            {
                raw.Id,
                raw.EmployeeId,
                raw.UserName,
                raw.FirstName,
                raw.LastName,
                raw.EmailAddress,
                raw.MobilePrimary,
                raw.Landline,

                raw.BranchId,
                raw.BranchName,

                raw.RoleId,
                raw.RoleName,

                raw.CompanyId,
                raw.CompanyName,

                raw.DesignationId,
                DesignationName = (raw.DesignationId.HasValue
                                   && GetDesignationsLookup().ContainsKey(raw.DesignationId.Value))
                    ? GetDesignationsLookup()[raw.DesignationId.Value]
                    : null,

                raw.Active,
                raw.DashboardAccess,
                raw.Approvel,
                raw.FacialRecognition,

                // Audit Formatting
                CreatedBy = raw.CreatedByEmployeeId,
                CreatedByName = $"{raw.CreatedByFirst} {raw.CreatedByLast}".Trim(),
                CreatedDateFormatted = raw.CreatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",

                UpdatedBy = raw.UpdatedByEmployeeId,
                UpdatedByName = $"{raw.UpdatedByFirst} {raw.UpdatedByLast}".Trim(),
                UpdatedDateFormatted = raw.UpdatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",

                LastLoginDateFormatted = raw.LastLoginDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-"
            };

            return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
        }




        // ---------------- CREATE ----------------
        [HttpPost, Route("create")]
        public ActionResult Create()
        {
            try
            {
                var userId = GetCurrentUserId(); // ✅ use Id, not EmployeeId
                var f = Request.Form;

                // --- Safe conversions ---
                int? branchId = ToInt(f["BranchId"]) ?? ToInt(f["branchId"]);
                int? roleId = ToInt(f["RoleId"]) ?? ToInt(f["roleId"]);
                int? companyId = ToInt(f["CompanyId"]) ?? ToInt(f["companyId"]);
                int? designationId = ToInt(f["DesignationId"]) ?? ToInt(f["designationId"]);

                // --- Validate foreign keys ---
                if (!branchId.HasValue || !db.Branches.Any(b => b.Id == branchId.Value))
                    return JsonError("Invalid BranchId: branch does not exist.");

                if (!roleId.HasValue || !db.Adm_Roles.Any(r => r.Id == roleId.Value))
                    return JsonError("Invalid RoleId: role does not exist.");

                if (!companyId.HasValue || !db.Adm_CompanyProfile.Any(c => c.CompanyID == companyId.Value))
                    return JsonError("Invalid CompanyId: company does not exist.");

                // --- Build entity ---
                var user = new Adm_Users
                {
                    EmployeeId = ToInt(f["EmployeeId"]) ?? 0,
                    UserName = f["UserName"]?.Trim(),
                    FirstName = f["FirstName"]?.Trim(),
                    LastName = f["LastName"]?.Trim(),
                    EmailAddress = f["EmailAddress"]?.Trim(),
                    MobilePrimary = f["MobilePrimary"]?.Trim(),
                    Landline = f["Landline"]?.Trim(),
                    Password = PasswordHasher.Hash(f["Password"]),
                    BranchId = branchId,
                    RoleId = roleId,
                    CompanyId = companyId,
                    DesignationId = designationId,
                    Active = ToBool(f["Active"]),
                    DashboardAccess = ToBool(f["DashboardAccess"]),
                    Approvel = ToBool(f["Approvel"]) ? "true" : "false",
                    CreatedBy = userId, // ✅ correct FK reference
                    CreatedDate = DateTime.Now,
                    UpdatedBy = userId,
                    UpdatedDate = DateTime.Now
                };

                SaveFacialRecognition(user);
                db.Adm_Users.Add(user);
                db.SaveChanges();

                LogUserAudit(user.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: (user.UserName ?? "-") + " · Role #" + user.RoleId);

                return JsonSuccess("✅ User created successfully.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                while (inner?.InnerException != null) inner = inner.InnerException;

                return Json(new
                {
                    success = false,
                    message = "❌ SERVER EXCEPTION",
                    error = inner?.Message ?? ex.Message,
                    stack = inner?.StackTrace ?? ex.StackTrace
                }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id)
        {
            try
            {
                var user = db.Adm_Users.Find(id);
                if (user == null)
                    return JsonError("User not found.");

                var userId = GetCurrentUserId(); // ✅ correct FK
                var f = Request.Form;

                int? branchId = ToInt(f["BranchId"]) ?? ToInt(f["branchId"]);
                int? roleId = ToInt(f["RoleId"]) ?? ToInt(f["roleId"]);
                int? companyId = ToInt(f["CompanyId"]) ?? ToInt(f["companyId"]);
                int? designationId = ToInt(f["DesignationId"]) ?? ToInt(f["designationId"]);

                if (branchId.HasValue && db.Branches.Any(b => b.Id == branchId.Value))
                    user.BranchId = branchId;

                if (roleId.HasValue && db.Adm_Roles.Any(r => r.Id == roleId.Value))
                    user.RoleId = roleId;

                if (companyId.HasValue && db.Adm_CompanyProfile.Any(c => c.CompanyID == companyId.Value))
                    user.CompanyId = companyId;

                // Treat absence of the DesignationId field as "leave unchanged".
                // Treat an explicit empty string / 0 as "clear" (DesignationId = null).
                string desigRaw = f["DesignationId"] ?? f["designationId"];
                if (desigRaw != null)
                {
                    user.DesignationId = (designationId.HasValue && designationId.Value > 0)
                        ? designationId
                        : (int?)null;
                }

                user.EmployeeId = ToInt(f["EmployeeId"]) ?? user.EmployeeId;
                user.UserName = f["UserName"]?.Trim();
                user.FirstName = f["FirstName"]?.Trim();
                user.LastName = f["LastName"]?.Trim();
                user.EmailAddress = f["EmailAddress"]?.Trim();
                user.MobilePrimary = f["MobilePrimary"]?.Trim();
                user.Landline = f["Landline"]?.Trim();

                if (!string.IsNullOrWhiteSpace(f["Password"]))
                    user.Password = PasswordHasher.Hash(f["Password"]);

                user.Active = ToBool(f["Active"]);
                user.DashboardAccess = ToBool(f["DashboardAccess"]);
                user.Approvel = ToBool(f["Approvel"]) ? "true" : "false";
                user.UpdatedBy = userId; // ✅ correct FK reference
                user.UpdatedDate = DateTime.Now;

                SaveFacialRecognition(user);
                db.SaveChanges();

                LogUserAudit(user.Id, "EDITED",
                    oldValue: null, newValue: user.Active == true ? "Active" : "Inactive",
                    detail: user.UserName ?? "-");

                return JsonSuccess("✅ User updated successfully.");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException;
                while (inner?.InnerException != null) inner = inner.InnerException;

                return Json(new
                {
                    success = false,
                    message = "❌ SERVER EXCEPTION",
                    error = inner?.Message ?? ex.Message,
                    stack = inner?.StackTrace ?? ex.StackTrace
                }, JsonRequestBehavior.AllowGet);
            }
        }




        // ---------------- FILE UPLOAD ----------------
        private void SaveFacialRecognition(Adm_Users user)
        {
            try
            {
                var file = Request.Files["file"];
                if (file != null && file.ContentLength > 0)
                {
                    var fileName = System.IO.Path.GetFileName(file.FileName);
                    var uploadPath = System.IO.Path.Combine(Server.MapPath("~/Uploads/FacialRecognition"), fileName);
                    file.SaveAs(uploadPath);
                    user.FacialRecognition = "/Uploads/FacialRecognition/" + fileName;
                }
            }
            catch
            {
                // no crash on file save failure
            }
        }

        // ---------------- DELETE ----------------
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var user = db.Adm_Users.Find(id);
                if (user == null || user.Deleted == true)
                    return JsonError("User not found.");

                user.Deleted = true;
                user.Active = false;
                user.UpdatedBy = GetCurrentEmployeeId();
                user.UpdatedDate = DateTime.Now;
                db.SaveChanges();

                LogUserAudit(id, "DELETED",
                    oldValue: "Active", newValue: "Deleted",
                    detail: user.UserName ?? "-");

                return JsonSuccess("✅ User deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed. " + DescribeUserError(ex));
            }
        }

        // ---------------- DROPDOWNS ----------------
        [HttpGet, Route("roles")]
        public ActionResult GetRoles()
        {
            var data = db.Adm_Roles
                .Where(r => r.Deleted != true)
                .Select(r => new { Id = r.Id, Name = r.Name })
                .ToList();

            return JsonSuccess("✅ Roles loaded successfully.", data);
        }

        [HttpGet, Route("branches")]
        public ActionResult GetBranches()
        {
            var data = db.Branches
                .Where(b => b.Deleted == null || b.Deleted == false)
                .Select(b => new { Id = b.Id, Code = b.BranchId, Name = b.Name })
                .ToList();

            return JsonSuccess("✅ Branches loaded successfully.", data);
        }


        [HttpGet, Route("companies")]
        public ActionResult GetCompanies()
        {
            var data = db.Adm_CompanyProfile
                .Where(c => c.Deleted != true)
                .Select(c => new { Id = c.CompanyID, Name = c.CompanyName })
                .ToList();

            return JsonSuccess("✅ Companies loaded successfully.", data);
        }

        // ---------------- IMAGE FETCH ----------------
        [HttpGet, Route("uploads/facialrecognition/{fileName}")]
        public ActionResult GetUserPhoto(string fileName)
        {
            // Strip any directory component so a value like "..\..\Web.config" or
            // an absolute path can't escape the intended upload folder.
            var safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return HttpNotFound();

            var baseDir = Path.GetFullPath(Server.MapPath("~/Uploads/FacialRecognition/"));
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseDir += Path.DirectorySeparatorChar;

            var path = Path.GetFullPath(Path.Combine(baseDir, safeFileName));

            // Belt-and-braces: confirm the resolved path still lives under the
            // upload directory before ever touching the filesystem with it.
            if (!path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return HttpNotFound();

            if (!System.IO.File.Exists(path))
                return HttpNotFound();

            var contentType = "image/jpeg";
            return File(path, contentType);
        }
    
        // GET: inline Index + Create/Edit + Paging
        public ActionResult Index(string search, string activeFilter, int? page, int pageSize = 15,
                          bool showCreate = false, bool showEdit = false, int? editId = null)
        {
            var vm = BuildViewModel(search, activeFilter, page, pageSize, showCreate, showEdit, editId);

            if (Request.IsAjaxRequest())
            {
                // When showing a form, return the FULL wrapper
                if (showCreate || showEdit)
                    return PartialView("Index", vm); // your Index.cshtml handles Layout=null for AJAX

                // For plain paging/search refresh, just table is fine
                return PartialView("_UsersGrid", vm);
            }
            return View(vm);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex([Bind(Prefix = "NewUser")] Adm_Users newUser,
                string search = null, string activeFilter = null, int page = 1, int pageSize = 15)
        {
            // Safety
            if (newUser == null)
                ModelState.AddModelError("", "No user data was posted.");

            if (!ModelState.IsValid)
            {
                var vm = BuildViewModel(search, activeFilter, page, pageSize, showCreate: true);
                // keep user-entered values so the form re-renders with them
                vm.NewUser = newUser ?? new Adm_Users();
                if (Request.IsAjaxRequest()) return PartialView("Index", vm);
                return View("Index", vm);
            }

            var now = DateTime.UtcNow;
            int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            newUser.CreatedBy = uid;
            newUser.CreatedDate = now;
            newUser.UpdatedBy = uid;
            newUser.UpdatedDate = now;
            newUser.Deleted = false;

            db.Adm_Users.Add(newUser);
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex([Bind(Prefix = "EditUser")] Adm_Users editUser,
                    string search = null, string activeFilter = null, int page = 1, int pageSize = 15)
        {
            if (editUser == null)
                ModelState.AddModelError("", "No user data was posted.");

            if (!ModelState.IsValid)
            {
                var vm = BuildViewModel(search, activeFilter, page, pageSize, showEdit: true, editId: editUser?.Id);
                vm.EditUser = editUser ?? new Adm_Users();
                if (Request.IsAjaxRequest()) return PartialView("Index", vm);
                return View("Index", vm);
            }

            var u = db.Adm_Users.Find(editUser.Id);
            if (u == null) return HttpNotFound();

            // Map fields
            u.EmployeeId = editUser.EmployeeId;
            u.RoleId = editUser.RoleId;
            u.UserName = editUser.UserName;
            u.FirstName = editUser.FirstName;
            u.LastName = editUser.LastName;
            u.EmailAddress = editUser.EmailAddress;
            u.Landline = editUser.Landline;
            u.LandlineExt = editUser.LandlineExt;
            u.MobilePrimary = editUser.MobilePrimary;
            u.MobileSecondary = editUser.MobileSecondary;
            u.Password = editUser.Password;
            u.PasswordQuestion = editUser.PasswordQuestion;
            u.PasswordAnswer = editUser.PasswordAnswer;
            u.DashboardAccess = editUser.DashboardAccess;
            u.IsSalesmanLinked = editUser.IsSalesmanLinked;
            u.SalesmanCode = editUser.SalesmanCode;
            u.DesignationId = editUser.DesignationId;
            u.Approvel = editUser.Approvel;
            u.BranchId = editUser.BranchId;
            u.CompanyId = editUser.CompanyId;
            u.Active = editUser.Active;
            u.Deleted = editUser.Deleted;

            u.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            u.UpdatedDate = DateTime.UtcNow;

            db.Entry(u).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        // POST: inline JSON‐edit single field
        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateUserField(int userId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            var u = db.Adm_Users.Find(userId);
            if (u == null)
                return Json(new { success = false, message = "User not found." });

            var prop = typeof(Adm_Users).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                object converted;
                var tgt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (tgt == typeof(string))
                    converted = newValue;
                else if (tgt == typeof(int))
                    converted = int.TryParse(newValue, out var i) ? (int?)i : null;
                else if (tgt == typeof(bool))
                    converted = (newValue ?? "").Trim()
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                else if (tgt == typeof(DateTime))
                    converted = DateTime.TryParse(newValue, out var dt) ? (DateTime?)dt : null;
                else
                    return Json(new { success = false, message = $"Unsupported type {tgt.Name}" });

                prop.SetValue(u, converted);
                u.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                u.UpdatedDate = DateTime.UtcNow;
                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public ActionResult DetailsFromIndex(int id, string search = null, string activeFilter = null, int? page = 1)
        {
            var user = db.Adm_Users
                         .Include(u => u.Branches)
                         .Include(u => u.Adm_CompanyProfile)
                         .FirstOrDefault(u => u.Id == id);

            if (user == null)
                return HttpNotFound();

            return PartialView("_UserDetailPartial", user); 
        }


        public ActionResult Delete(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var user = db.Adm_Users.Include(x => x.Branches).Include(x => x.Adm_CompanyProfile).FirstOrDefault(x => x.Id == id.Value);
            if (user == null)
                return HttpNotFound();

            if (Request.IsAjaxRequest())
                return PartialView("_UserDeleteConfirm", user);

            return View(user);
        }


        // POST: actually delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Debug.WriteLine($"POST Delete received for user ID: {id}");

            var user = db.Adm_Users.Find(id);
            if (user == null)
            {
                Debug.WriteLine("User not found!");
                return HttpNotFound();
            }

            // Soft delete — consistent with the Delete(int id) action's pattern
            // in this same controller. A hard Remove() here would also throw if
            // the user is referenced by CreatedBy/UpdatedBy FKs elsewhere.
            user.Deleted = true;
            user.Active = false;
            user.UpdatedBy = GetCurrentEmployeeId();
            user.UpdatedDate = DateTime.Now;
            db.SaveChanges();

            LogUserAudit(id, "DELETED",
                oldValue: "Active", newValue: "Deleted",
                detail: user.UserName ?? "-");

            if (Request.IsAjaxRequest())
            {
                var updatedVm = BuildViewModel(null, null, 1);
                return PartialView("_UsersGrid", updatedVm);
            }

            return RedirectToAction("Index");
        }
        private UsersPageViewModel BuildViewModel(string search, string activeFilter, int? page,
                int pageSize = 15, bool showCreate = false, bool showEdit = false, int? editId = null)
        {
            int pageNumber = page ?? 1;
           
            Adm_Users editEntity = (showEdit && editId.HasValue)
                ? db.Adm_Users.Find(editId.Value)
                : null;
           
            var roleItems = new[]
            {
            new { Id = 1, Name = "User" },
            new { Id = 2, Name = "AuthorityUser" }};

            var branchItems = db.Branches
                .Where(b => (b.IsActive ?? false))
                .OrderBy(b => b.Name)
                .Select(b => new { Id = b.Id, Name = b.Name })
                .ToList();

            var companyItems = db.Adm_CompanyProfile
                //.Where(c => c.Deleted != true)                
                .OrderBy(c => c.CompanyName)
                .Select(c => new { Id = c.CompanyID, Name = c.CompanyName })
                .ToList();

            // Users grid
            var q = db.Adm_Users
                .Include(u => u.Branches)
                .Include(u => u.Adm_CompanyProfile)
                .Where(u => u.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(u =>
                    u.UserName.Contains(search) ||
                    u.FirstName.Contains(search) ||
                    u.LastName.Contains(search) ||
                    u.EmailAddress.Contains(search) ||
                    u.Branches.Name.Contains(search) ||
                    u.Adm_CompanyProfile.CompanyName.Contains(search)
                );
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                q = q.Where(u => (u.Active ?? false) == want);
            }

            var rows = q.OrderBy(u => u.EmployeeId)
                        .ToList()
                        .Select(u => new UserRowViewModel
                        {
                            Id = u.Id,
                            EmployeeId = u.EmployeeId,
                            RoleId = u.RoleId,
                            RoleName = u.RoleId == 1 ? "User" : "AuthorityUser",
                            UserName = u.UserName,
                            FirstName = u.FirstName,
                            LastName = u.LastName,
                            FullName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(),
                            EmailAddress = u.EmailAddress,
                            Landline = u.Landline,
                            LandlineExt = u.LandlineExt,
                            MobilePrimary = u.MobilePrimary,
                            MobileSecondary = u.MobileSecondary,
                            LastLoginDate = u.LastLoginDate,
                            LastPasswordChangeDate = u.LastPasswordChangeDate,
                            DashboardAccess = u.DashboardAccess,
                            IsSalesmanLinked = u.IsSalesmanLinked,
                            SalesmanCode = u.SalesmanCode,
                            DesignationId = u.DesignationId,
                            Approvel = u.Approvel,
                            BranchName = u.Branches != null ? u.Branches.Name : "",
                            CompanyName = u.Adm_CompanyProfile != null ? u.Adm_CompanyProfile.CompanyName : "",
                            Active = u.Active ?? false,
                            Deleted = u.Deleted ?? false,
                            CreatedBy = u.CreatedBy,
                            CreatedDate = u.CreatedDate,
                            UpdatedBy = u.UpdatedBy,
                            UpdatedDate = u.UpdatedDate
                        })
                        .ToPagedList(pageNumber, pageSize);

            return new UsersPageViewModel
            {
                Users = rows,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter,

                // Selected values must come from Edit entity
                RoleList = new SelectList(roleItems, "Id", "Name", editEntity?.RoleId),
                BranchList = new SelectList(branchItems, "Id", "Name", editEntity?.BranchId),
                CompanyList = new SelectList(companyItems, "Id", "Name", editEntity?.CompanyId),

                NewUser = showCreate ? new Adm_Users { Active = true } : null,
                EditUser = editEntity
            };
        }


        [HttpGet, Obsolete]
        public ActionResult ExportToExcel(int? id, string search = null, string activeFilter = null)
        {            
            if (id.HasValue)
            {
                // single-record export
                var u = db.Adm_Users
                          .Include(x => x.Branches)
                          .Include(x => x.Adm_CompanyProfile)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (u == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("UserDetail");
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

                    Add("ID", u.Id);
                    Add("Employee ID", u.EmployeeId);
                    Add("Role", u.RoleId == 1 ? "User" : "AuthorityUser");
                    Add("Username", u.UserName);
                    Add("Name", $"{u.FirstName} {u.LastName}");
                    Add("Email", u.EmailAddress);
                    Add("Mobile", u.MobilePrimary);
                    Add("Landline", u.Landline);
                    Add("Landline Ext", u.LandlineExt);
                    Add("Mobile Secondary", u.MobileSecondary);
                    Add("Branch", u.Branches?.Name);
                    Add("Company", u.Adm_CompanyProfile?.CompanyName);
                    Add("Active", u.Active == true ? "Yes" : "No");
                    Add("Deleted", u.Deleted == true ? "Yes" : "No");
                    Add("Dashboard Access", u.DashboardAccess == true ? "Yes" : "No");
                    Add("Salesman Linked", u.IsSalesmanLinked == true ? "Yes" : "No");
                    Add("Salesman Code", u.SalesmanCode);
                    Add("Designation ID", u.DesignationId);
                    Add("Approval", u.Approvel);
                    Add("Login Ticket", u.LoginTicket);
                    Add("Device Ticket", u.DeviceTicket);
                    Add("Facial Recognition", u.FacialRecognition);
                    Add("Biometric", u.Biomatric);
                    Add("Last Login Date", u.LastLoginDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Last Password Change", u.LastPasswordChangeDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Created By", u.CreatedBy);
                    Add("Created Date", u.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Updated By", u.UpdatedBy);
                    Add("Updated Date", u.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Record Timestamp", u.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                $"User_{u.Id}_Detail.xlsx");
                }
            }
            else
            {
                // full-list export
                var q = db.Adm_Users
                          .Include(x => x.Branches)
                          .Include(x => x.Adm_CompanyProfile)
                          .Where(x => x.Deleted != true);

                if (!string.IsNullOrWhiteSpace(search))
                    q = q.Where(x =>
                        x.UserName.Contains(search) ||
                        x.FirstName.Contains(search) ||
                        x.LastName.Contains(search) ||
                        x.EmailAddress.Contains(search));

                if (activeFilter == "Yes") q = q.Where(x => x.Active == true);
                if (activeFilter == "No") q = q.Where(x => x.Active == false);

                var list = q.OrderBy(x => x.Id).ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Users");
                    var headers = new[]
                    {
                "ID","EmpID","Role","Username","Name",
                "Email","Mobile","Landline","LandlineExt","MobileSecondary",
                "Branch","Company","Active","Deleted",
                "DashboardAccess","SalesmanLinked","SalesmanCode","DesignationID","Approval",
                "LoginTicket","DeviceTicket","FacialRecognition","Biometric",
                "LastLoginDate","LastPasswordChangeDate",
                "CreatedBy","CreatedDate","UpdatedBy","UpdatedDate"};
                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        var x = list[i];
                        int r = i + 2;
                        ws.Cells[r, 1].Value = x.Id;
                        ws.Cells[r, 2].Value = x.EmployeeId;
                        ws.Cells[r, 3].Value = x.RoleId == 1 ? "User" : "AuthorityUser";
                        ws.Cells[r, 4].Value = x.UserName;
                        ws.Cells[r, 5].Value = $"{x.FirstName} {x.LastName}";
                        ws.Cells[r, 6].Value = x.EmailAddress;
                        ws.Cells[r, 7].Value = x.MobilePrimary;
                        ws.Cells[r, 8].Value = x.Landline;
                        ws.Cells[r, 9].Value = x.LandlineExt;
                        ws.Cells[r, 10].Value = x.MobileSecondary;
                        ws.Cells[r, 11].Value = x.Branches?.Name;
                        ws.Cells[r, 12].Value = x.Adm_CompanyProfile?.CompanyName;
                        ws.Cells[r, 13].Value = x.Active == true ? "Yes" : "No";
                        ws.Cells[r, 14].Value = x.Deleted == true ? "Yes" : "No";
                        ws.Cells[r, 15].Value = x.DashboardAccess == true ? "Yes" : "No";
                        ws.Cells[r, 16].Value = x.IsSalesmanLinked == true ? "Yes" : "No";
                        ws.Cells[r, 17].Value = x.SalesmanCode;
                        ws.Cells[r, 18].Value = x.DesignationId;
                        ws.Cells[r, 19].Value = x.Approvel;
                        ws.Cells[r, 20].Value = x.LoginTicket;
                        ws.Cells[r, 21].Value = x.DeviceTicket;
                        ws.Cells[r, 22].Value = x.FacialRecognition;
                        ws.Cells[r, 23].Value = x.Biomatric;
                        ws.Cells[r, 24].Value = x.LastLoginDate?.ToString("yyyy-MM-dd HH:mm:ss");
                        ws.Cells[r, 25].Value = x.LastPasswordChangeDate?.ToString("yyyy-MM-dd HH:mm:ss");
                        ws.Cells[r, 26].Value = x.CreatedBy;
                        ws.Cells[r, 27].Value = x.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss");
                        ws.Cells[r, 28].Value = x.UpdatedBy;
                        ws.Cells[r, 29].Value = x.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                 
                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                "Users.xlsx");
                }
            }
        }

        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(int? id, string search = null, string activeFilter = null)
        {
            if (id.HasValue)
            {
                // single-record PDF
                var u = db.Adm_Users
                          .Include(x => x.Branches)
                          .Include(x => x.Adm_CompanyProfile)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (u == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    var writer = PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var tbl = new PdfPTable(2) { WidthPercentage = 80 };
                    void Cell(string txt, bool hdr = false)
                    {
                        var c = new PdfPCell(new Phrase(txt));
                        if (hdr) c.BackgroundColor = BaseColor.LIGHT_GRAY;
                        tbl.AddCell(c);
                    }
                    void Row(string f, object v)
                    {
                        Cell(f);
                        Cell(v?.ToString() ?? "(none)");
                    }

                    Cell("Field", true); Cell("Value", true);
                    Row("ID", u.Id);
                    Row("Employee ID", u.EmployeeId);
                    Row("Role", u.RoleId == 1 ? "User" : "AuthorityUser");
                    Row("Username", u.UserName);
                    Row("Name", $"{u.FirstName} {u.LastName}");
                    Row("Email", u.EmailAddress);
                    Row("Mobile", u.MobilePrimary);
                    Row("Landline", u.Landline);
                    Row("Landline Ext", u.LandlineExt);
                    Row("Mobile Secondary", u.MobileSecondary);
                    Row("Branch", u.Branches?.Name);
                    Row("Company", u.Adm_CompanyProfile?.CompanyName);
                    Row("Active", u.Active == true ? "Yes" : "No");
                    Row("Deleted", u.Deleted == true ? "Yes" : "No");
                    Row("Dashboard Access", u.DashboardAccess == true ? "Yes" : "No");
                    Row("Salesman Linked", u.IsSalesmanLinked == true ? "Yes" : "No");
                    Row("Salesman Code", u.SalesmanCode);
                    Row("Designation ID", u.DesignationId);
                    Row("Approval", u.Approvel);
                    Row("Login Ticket", u.LoginTicket);
                    Row("Device Ticket", u.DeviceTicket);
                    Row("Facial Recognition", u.FacialRecognition);
                    Row("Biometric", u.Biomatric);
                    Row("Last Login Date", u.LastLoginDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Last Password Change", u.LastPasswordChangeDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Created By", u.CreatedBy);
                    Row("Created Date", u.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Updated By", u.UpdatedBy);
                    Row("Updated Date", u.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(tbl);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"User_{u.Id}_Detail.pdf");
                }
            }
            else
            {
                // full-list via Rotativa
                var q = db.Adm_Users
                          .Include(x => x.Branches)
                          .Include(x => x.Adm_CompanyProfile)
                          .Where(x => x.Deleted != true);

                if (!string.IsNullOrWhiteSpace(search))
                    q = q.Where(x =>
                        x.UserName.Contains(search) ||
                        x.FirstName.Contains(search) ||
                        x.LastName.Contains(search));

                if (activeFilter == "Yes") q = q.Where(x => x.Active == true);
                if (activeFilter == "No") q = q.Where(x => x.Active == false);

                var rows = q.OrderBy(x => x.Id)
                            .Select(x => new UserRowViewModel
                            {
                                Id = x.Id,
                                EmployeeId = x.EmployeeId,
                                RoleName = x.RoleId == 1 ? "User" : "AuthorityUser",
                                UserName = x.UserName,
                                FullName = x.FirstName + " " + x.LastName,
                                EmailAddress = x.EmailAddress,
                                MobilePrimary = x.MobilePrimary,
                                BranchName = x.Branches.Name,
                                CompanyName = x.Adm_CompanyProfile.CompanyName,
                                Active = x.Active ?? false,
                                Deleted = x.Deleted ?? false
                            })
                            .ToList();

                var vm = new UsersPageViewModel
                {
                    Users = new StaticPagedList<UserRowViewModel>(rows, 1, rows.Count, rows.Count),
                    ShowCreate = false,
                    ShowEdit = false
                };

                return new ViewAsPdf("ExportUsersTable", vm)
                {
                    FileName = "Users_List.pdf",
                    PageSize = Rotativa.Options.Size.A4,
                    PageOrientation = Rotativa.Options.Orientation.Portrait,
                    CustomSwitches = "--print-media-type --load-error-handling ignore"
                };
            }
        }

        private void PopulateLookups(UsersPageViewModel vm)
        {
            var roles = new[]
            {
            new { Id = 1, Name = "User" },
            new { Id = 2, Name = "AuthorityUser" }
            };

            vm.RoleList = new SelectList(roles, "Id", "Name", vm.EditUser?.RoleId ?? vm.NewUser?.RoleId);

            vm.BranchList = new SelectList(
                db.Branches.Where(b => (b.IsActive ?? false))
                           .OrderBy(b => b.Name)
                           .Select(b => new { b.Id, b.Name })
                           .ToList(),
                "Id", "Name", vm.EditUser?.BranchId ?? vm.NewUser?.BranchId);

            vm.CompanyList = new SelectList(
                db.Adm_CompanyProfile.Where(c => (c.Active ?? false) && c.Deleted != true)
                                     .OrderBy(c => c.CompanyName)
                                     .Select(c => new { c.CompanyID, c.CompanyName })
                                     .ToList(),
                "CompanyID", "CompanyName", vm.EditUser?.CompanyId ?? vm.NewUser?.CompanyId);
        }



        [Authorize]
        public ActionResult ChangePassword()
        {
            return View();
        }

        // POST: Adm_Users/ChangePassword
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult ChangePassword(ChangePasswordViewModel m)
        {
            if (!ModelState.IsValid)
                return View(m);

            // 1. Pull your current user’s ID out of session:
            var currentUserId = Convert.ToInt32(
                Session["UserID"] ?? Session["EmployeeId"] ?? 0
            );

            // 2. Load the user by that key:
            var u = db.Adm_Users.Find(currentUserId);
            if (u == null)
                return HttpNotFound();

            // 3. Verify old password:
            if (!PasswordHasher.Verify(m.OldPassword, u.Password))
            {
                ModelState.AddModelError(
                    nameof(m.OldPassword),
                    "Incorrect current password."
                );
                return View(m);
            }

            // 4. Stamp and swap in the new one (always stored hashed):
            u.Password = PasswordHasher.Hash(m.NewPassword);
            u.LastPasswordChangeDate = DateTime.UtcNow;
            u.UpdatedBy = currentUserId;
            u.UpdatedDate = DateTime.UtcNow;

            db.Entry(u).State = EntityState.Modified;
            db.SaveChanges();

            TempData["Success"] = "Your password has been updated.";
            return RedirectToAction("Index", "Home");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
