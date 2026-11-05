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
using OfficeOpenXml;
using iTextSharp.text;
using iTextSharp.text.pdf;
using PagedList;
using EMB_Production.ViewModels;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/service-rates")]
    public class GenServiceRatesController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Service Rates)
        // ─────────────────────────────────────────────────────────────
        private void LogSrAudit(int id, string action,
                                string oldValue = null, string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_ServiceRatesAuditLog
       (ServiceRateId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
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
                var entries = db.Database.SqlQuery<SrAuditEntry>(@"
SELECT  a.Id, a.ServiceRateId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_ServiceRatesAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.ServiceRateId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, serviceRateId = e.ServiceRateId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeSrError(ex) });
            }
        }

        private class SrAuditEntry
        {
            public int Id { get; set; }
            public int ServiceRateId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeSrError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Service Rates)
        // ─────────────────────────────────────────────────────────────
        public class SrBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkSr(List<int> ids, Func<int, Gen_ServiceRates, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_ServiceRates.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = "SR#" + id;
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeSrError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(SrBulkIdsDto dto)
        {
            var data = DoBulkSr(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                db.SaveChanges();
                LogSrAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(SrBulkIdsDto dto)
        {
            var data = DoBulkSr(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                db.SaveChanges();
                LogSrAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(SrBulkIdsDto dto)
        {
            var data = DoBulkSr(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                db.SaveChanges();
                LogSrAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        [HttpGet]
        [Route("list")]
        public ActionResult List(string search = "", string activeFilter = "", int? branchFilter = null,
                string productFilter = "", int? serviceFilter = null, int page = 1, int pageSize = 10)
        {
            var query =
                from r in db.Gen_ServiceRates
                where (r.Deleted == false || r.Deleted == null)
                join p in db.Gen_Products on r.ProductId equals p.Id into pj
                from p in pj.DefaultIfEmpty()
                select new
                {
                    Rate = r,
                    Product = p
                };

            // ---------- SEARCH ----------
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(x =>
                    (x.Rate.Charge_Fector ?? "").Contains(search) ||
                    (x.Rate.Remarks ?? "").Contains(search) ||
                    (x.Rate.Gen_ServiceType.Service_Type ?? "").Contains(search) ||
                    (x.Product.Prod_Name ?? "").Contains(search) ||
                    (x.Product.Prod_Code ?? "").Contains(search)
                );
            }

            // ---------- STATUS ----------
            if (activeFilter == "active")
                query = query.Where(x => x.Rate.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Rate.Active == false || x.Rate.Active == null);

            // ---------- BRANCH ----------
            if (branchFilter.HasValue && branchFilter > 0)
                query = query.Where(x => x.Rate.BranchId == branchFilter);

            // ---------- PRODUCT ----------
            if (!string.IsNullOrEmpty(productFilter)
                && int.TryParse(productFilter, out int prodId))
            {
                query = query.Where(x => x.Rate.ProductId == prodId);
            }

            // ---------- SERVICE FILTER ----------
            if (serviceFilter.HasValue && serviceFilter > 0)
                query = query.Where(x => x.Rate.Service_Id == serviceFilter);

            var totalItems = query.Count();

            // Phase R.9 - summary aggregates
            var summarySource = query.Select(x => new
            {
                x.Rate.ProductId,
                x.Rate.Service_Id,
                x.Rate.BranchId,
                x.Rate.Active,
                ServiceTypeName = x.Rate.Gen_ServiceType.Service_Type,
                ProductName = x.Product.Prod_Name,
                BranchName = x.Rate.Branches.Name
            }).ToList();

            int activeCount = summarySource.Count(s => s.Active == true);
            int inactiveCount = summarySource.Count(s => s.Active != true);
            int distinctProducts = summarySource.Where(s => s.ProductId != null).Select(s => s.ProductId).Distinct().Count();
            int distinctServices = summarySource.Where(s => s.Service_Id != null).Select(s => s.Service_Id).Distinct().Count();
            int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();

            var byService = summarySource
                .Where(s => !string.IsNullOrWhiteSpace(s.ServiceTypeName))
                .GroupBy(s => s.ServiceTypeName)
                .Select(g => new { service = g.Key, count = g.Count() })
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
                distinctProducts,
                distinctServices,
                distinctBranches,
                byService,
                byBranch
            };

            var items = query
                .OrderByDescending(x => x.Rate.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    Id = x.Rate.Id,

                    ProductId = x.Rate.ProductId,
                    ProductCode = x.Product != null ? x.Product.Prod_Code : null,
                    ProductName = x.Product != null ? x.Product.Prod_Name : null,
                    ProductDisplay = x.Product != null
                        ? (x.Product.Prod_Code + " - " + x.Product.Prod_Name)
                        : null,

                    ServiceId = x.Rate.Service_Id,
                    ServiceName = x.Rate.Gen_ServiceType.Service_Type,

                    ChargeFactor = x.Rate.Charge_Fector,
                    Remarks = x.Rate.Remarks,

                    BranchId = x.Rate.BranchId,
                    BranchName = x.Rate.Branches.Name,

                    Active = x.Rate.Active,
                    Deleted = x.Rate.Deleted,
                    RecordTimeStamp = x.Rate.RecordTimeStamp,
                    CreatedBy = x.Rate.CreatedBy,
                    CreatedDate = x.Rate.CreatedDate,
                    UpdatedBy = x.Rate.UpdatedBy,
                    UpdatedDate = x.Rate.UpdatedDate
                })
                .ToList();

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
        // Phase R.9 - PROFILE
        // ============================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var raw = (
                    from r in db.Gen_ServiceRates
                    join p in db.Gen_Products on r.ProductId equals p.Id into pj
                    from p in pj.DefaultIfEmpty()
                    where r.Id == id
                    select new
                    {
                        r.Id,
                        r.ProductId,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",
                        r.Service_Id,
                        ServiceTypeName = r.Gen_ServiceType.Service_Type,
                        ServiceTypeDescription = r.Gen_ServiceType.Service_TypeDescription,
                        r.Charge_Fector,
                        r.Remarks,
                        r.BranchId,
                        BranchName = r.Branches.Name,
                        r.Active,
                        r.CreatedDate,
                        r.UpdatedDate
                    }
                ).FirstOrDefault();

                if (raw == null) return JsonError("Service Rate not found.");

                var header = new
                {
                    id = raw.Id,
                    productId = raw.ProductId,
                    productCode = raw.ProductCode,
                    productName = raw.ProductName,
                    serviceId = raw.Service_Id,
                    serviceTypeName = raw.ServiceTypeName,
                    serviceTypeDescription = raw.ServiceTypeDescription,
                    chargeFactor = raw.Charge_Fector,
                    remarks = raw.Remarks,
                    branchId = raw.BranchId,
                    branchName = raw.BranchName,
                    active = raw.Active ?? false,
                    createdDateText = raw.CreatedDate.HasValue ? raw.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = raw.UpdatedDate.HasValue ? raw.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Other rates for the same product
                var siblingsByProduct = (
                    from r in db.Gen_ServiceRates
                    where r.ProductId == raw.ProductId
                       && r.Id != id
                       && (r.Deleted == false || r.Deleted == null)
                    orderby r.Id descending
                    select new
                    {
                        r.Id,
                        r.Service_Id,
                        ServiceName = r.Gen_ServiceType.Service_Type,
                        ChargeFactor = r.Charge_Fector,
                        Active = r.Active ?? false
                    }
                ).Take(50).ToList();

                // Other rates for the same service type
                var siblingsByService = (
                    from r in db.Gen_ServiceRates
                    join p in db.Gen_Products on r.ProductId equals p.Id into pj
                    from p in pj.DefaultIfEmpty()
                    where r.Service_Id == raw.Service_Id
                       && r.Id != id
                       && (r.Deleted == false || r.Deleted == null)
                    orderby r.Id descending
                    select new
                    {
                        r.Id,
                        ProductCode = p != null ? p.Prod_Code : "",
                        ProductName = p != null ? p.Prod_Name : "",
                        ChargeFactor = r.Charge_Fector,
                        Active = r.Active ?? false
                    }
                ).Take(50).ToList();

                var kpis = new
                {
                    chargeFactor = raw.Charge_Fector,
                    siblingsByProduct = siblingsByProduct.Count(),
                    siblingsByService = siblingsByService.Count(),
                    activeOnProduct = siblingsByProduct.Count(s => s.Active) + ((raw.Active ?? false) ? 1 : 0),
                    activeOnService = siblingsByService.Count(s => s.Active) + ((raw.Active ?? false) ? 1 : 0)
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    siblingsByProduct,
                    siblingsByService
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        [HttpGet]
        [Route("detail/{id}")]
        public ActionResult Detail(int id)
        {
            var r = (
                from x in db.Gen_ServiceRates

                join p in db.Gen_Products on x.ProductId equals p.Id into pj
                from p in pj.DefaultIfEmpty()

                join cu in db.Adm_Users on x.CreatedBy equals cu.Id into cuj
                from cu in cuj.DefaultIfEmpty()

                join uu in db.Adm_Users on x.UpdatedBy equals uu.Id into uuj
                from uu in uuj.DefaultIfEmpty()

                where x.Id == id
                select new
                {
                    x.Id,

                    ProductId = x.ProductId,
                    ProductCode = p != null ? p.Prod_Code : null,
                    ProductName = p != null ? p.Prod_Name : null,

                    ServiceId = x.Service_Id,
                    ServiceName = x.Gen_ServiceType.Service_Type,

                    ChargeFactor = x.Charge_Fector,
                    x.Remarks,

                    x.BranchId,
                    BranchName = x.Branches.Name,

                    x.Active,
                    x.Deleted,

                    x.RecordTimeStamp,

                    CreatedByEmployeeId = cu != null ? cu.EmployeeId : null,
                    x.CreatedDate,

                    UpdatedByEmployeeId = uu != null ? uu.EmployeeId : null,
                    x.UpdatedDate
                }
            ).FirstOrDefault();

            if (r == null)
                return JsonError("Record not found");

            return JsonSuccess("Detail loaded", new
            {
                r.Id,
                r.ProductId,
                r.ProductCode,
                r.ProductName,
                r.ServiceId,
                r.ServiceName,
                r.ChargeFactor,
                r.Remarks,
                r.BranchId,
                r.BranchName,
                r.Active,
                r.Deleted,

                RecordTimeStamp = r.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"),

                r.CreatedByEmployeeId,
                CreatedDate = r.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"),

                r.UpdatedByEmployeeId,
                UpdatedDate = r.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        [HttpPost]
        [Route("create")]
        public ActionResult Create(ServiceRatesDto dto)
        {            
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value.Errors.Count > 0)
                    .Select(x => new
                    {
                        Field = x.Key,
                        Errors = x.Value.Errors.Select(e => e.ErrorMessage)
                    });

                return Json(new
                {
                    success = false,
                    message = "Invalid data submitted.",
                    modelErrors = errors
                });
            }

            try
            {               
                var userId = GetCurrentUserId();

                if (userId <= 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = "User session expired. Please login again."
                    });
                }

                var now = DateTime.Now;

                if (!dto.ProductId.HasValue || !db.Gen_Products.Any(x => x.Id == dto.ProductId.Value))
                    return Json(new { success = false, message = "Invalid Product selected." });

                if (!db.Gen_ServiceType.Any(x => x.Id == dto.ServiceId))
                    return Json(new { success = false, message = "Invalid Service Type selected." });

                if (!db.Branches.Any(x => x.Id == dto.BranchId))
                    return Json(new { success = false, message = "Invalid Branch selected." });

                bool exists = db.Gen_ServiceRates.Any(x =>
                    x.ProductId == dto.ProductId &&
                    x.Service_Id == dto.ServiceId &&
                    x.BranchId == dto.BranchId &&
                    (x.Deleted == false || x.Deleted == null)
                );

                if (exists)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Service rate already exists for this Product, Service and Branch."
                    });
                }

                var model = new Gen_ServiceRates
                {
                    ProductId = dto.ProductId,
                    Service_Id = dto.ServiceId,
                    Charge_Fector = dto.ChargeFactor?.Trim() ?? "",
                    Remarks = dto.Remarks?.Trim() ?? "",
                    BranchId = dto.BranchId,

                    Active = dto.Active,
                    Deleted = false,

                    CreatedBy = userId,
                    CreatedDate = now,

                    UpdatedBy = null,
                    UpdatedDate = null,

                    RecordTimeStamp = now
                };

                db.Gen_ServiceRates.Add(model);
                db.SaveChanges();

                LogSrAudit(model.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: "Product #" + dto.ProductId + " · Service #" + dto.ServiceId + " · Charge " + (dto.ChargeFactor ?? "-"));

                return Json(new
                {
                    success = true,
                    message = "Service rate added successfully."
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Create failed. " + DescribeSrError(ex)
                });
            }
        }

        [HttpPost]
        [Route("update")]
        public ActionResult Update(ServiceRatesDto dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId <= 0)
                    return JsonError("Session expired.");

                var model = db.Gen_ServiceRates.FirstOrDefault(x => x.Id == dto.Id);
                if (model == null)
                    return JsonError("Record not found");

                model.ProductId = dto.ProductId;

                model.Service_Id = dto.ServiceId;
                model.Charge_Fector = dto.ChargeFactor ?? "";
                model.Remarks = dto.Remarks ?? "";
                model.BranchId = dto.BranchId;

                model.Active = dto.Active;

                model.UpdatedBy = userId;
                model.UpdatedDate = DateTime.Now;
                model.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogSrAudit(model.Id, "EDITED",
                    oldValue: null, newValue: model.Active == true ? "Active" : "Inactive",
                    detail: "Product #" + dto.ProductId + " · Charge " + (dto.ChargeFactor ?? "-"));

                return JsonSuccess("Service rate updated successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Update failed. " + DescribeSrError(ex));
            }
        }



        // ============================================================
        // DELETE
        // ============================================================
        [HttpPost]
        [Route("delete")]
        public ActionResult Delete(int id)
        {
            try
            {
                var empId = GetCurrentEmployeeId();

                var model = db.Gen_ServiceRates.FirstOrDefault(x => x.Id == id);
                if (model == null)
                    return JsonError("Record not found");

                model.Deleted = true;
                model.Active = false;
                model.UpdatedBy = empId;
                model.UpdatedDate = DateTime.Now;
                model.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogSrAudit(id, "DELETED", "Active", "Deleted");

                return JsonSuccess("Deleted successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed", ex);
            }
        }

        // GET: Gen_ServiceRates
        public ActionResult Index(string search, string activeFilter, int? branchFilter, bool showCreate = false, bool showEdit = false, int editId = 0, int page = 1)
        {
            var query = db.Gen_ServiceRates
                .Include(r => r.Gen_TheServices)
                .Include(r => r.Branches)
                .AsQueryable();

            // filters
            if (!string.IsNullOrEmpty(search))
                query = query.Where(r => r.Remarks.Contains(search));
            if (activeFilter == "Yes")
                query = query.Where(r => r.Active == true);
            else if (activeFilter == "No")
                query = query.Where(r => r.Active == false);
            if (branchFilter.HasValue)
                query = query.Where(r => r.BranchId == branchFilter);

            // paging
            const int pageSize = 10;
            var vmList = (
                from r in query.OrderBy(r => r.Id)
                join p in db.Gen_Products on r.ProductId equals p.Id into pj
                from p in pj.DefaultIfEmpty()
                select new ServiceRatesRowViewModel
                {
                    Id = r.Id,
                    ProductId = r.ProductId,
                    ProductCode = p != null ? p.Prod_Code : null,
                    ProductName = p != null ? p.Prod_Name : null,
                    ServiceId = r.Service_Id,
                    ChargeFactor = r.Charge_Fector,
                    Remarks = r.Remarks,
                    BranchId = r.BranchId,
                    BranchName = r.Branches.Name,
                    Active = r.Active ?? false,
                    Deleted = r.Deleted ?? false,
                    RecordTimeStamp = r.RecordTimeStamp,
                    CreatedBy = r.CreatedBy,
                    CreatedDate = r.CreatedDate,
                    UpdatedBy = r.UpdatedBy,
                    UpdatedDate = r.UpdatedDate
                }).ToPagedList(page, pageSize);

            var vm = new ServiceRatesPageViewModel
            {
                Search = search,
                ActiveFilter = activeFilter,
                BranchFilter = branchFilter,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Rows = vmList,
                ServiceList = new SelectList(db.Gen_ServiceType.Where(x => (x.Deleted == false || x.Deleted == null)
                && x.Active == true).OrderBy(x => x.Service_Type),"Id", "Service_Type"),
                ProductList = new SelectList(
                    db.Gen_Products.Where(x => (x.Deleted == false || x.Deleted == null) && x.Active == true)
                                   .OrderBy(x => x.Prod_Name),
                    "Id", "Prod_Name"),
                BranchList = new SelectList(db.Branches, "Id", "Name"),
                NewRate = new ServiceRatesRowViewModel(),
                EditRate = showEdit ? vmList.FirstOrDefault(r => r.Id == editId) : null
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(ServiceRatesRowViewModel NewRate)
        {
            if (ModelState.IsValid)
            {
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                var entity = new Gen_ServiceRates
                {
                    ProductId = NewRate.ProductId,
                    Service_Id = NewRate.ServiceId,
                    Charge_Fector = NewRate.ChargeFactor,
                    Remarks = NewRate.Remarks,
                    BranchId = NewRate.BranchId,
                    Active = NewRate.Active,
                    Deleted = NewRate.Deleted,
                    RecordTimeStamp = DateTime.Now,
                    CreatedDate = DateTime.Now,
                    CreatedBy = userId,
                    UpdatedBy = userId
                };
                db.Gen_ServiceRates.Add(entity);
                db.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(ServiceRatesRowViewModel EditRate)
        {
            if (ModelState.IsValid)
            {
                var userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                var entity = db.Gen_ServiceRates.Find(EditRate.Id);
                if (entity != null)
                {
                    entity.ProductId = EditRate.ProductId;
                    entity.Service_Id = EditRate.ServiceId;
                    entity.Charge_Fector = EditRate.ChargeFactor;
                    entity.Remarks = EditRate.Remarks;
                    entity.BranchId = EditRate.BranchId;
                    entity.Active = EditRate.Active;
                    entity.Deleted = EditRate.Deleted;
                    entity.UpdatedDate = DateTime.Now;
                    entity.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                    db.SaveChanges();
                }
            }
            return RedirectToAction("Index");
        }
        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateServiceRateField(int id, string fieldName, string newValue)
        {
            // make sure the user is authenticated
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // load the rate
            var entity = db.Gen_ServiceRates.Find(id);
            if (entity == null)
                return Json(new { success = false, message = "Service rate not found." });

            // reflectively find the property
            var prop = typeof(Gen_ServiceRates).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                object converted;
                // handle nullable underlying types
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType)
                                 ?? prop.PropertyType;

                if (targetType == typeof(string))
                {
                    converted = newValue;
                }
                else if (targetType == typeof(int))
                {
                    converted = int.TryParse(newValue, out var i)
                                ? (int?)i
                                : null;
                }
                else if (targetType == typeof(bool))
                {
                    converted = (newValue ?? "")
                        .Trim()
                        .Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (targetType == typeof(DateTime))
                {
                    converted = DateTime.TryParse(newValue, out var dt)
                                ? (DateTime?)dt
                                : null;
                }
                else
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Unsupported type {targetType.Name}"
                    });
                }

                // set the new value
                prop.SetValue(entity, converted);

                // audit stamp
                var userId = Convert.ToInt32(Session["EmployeeId"]
                                ?? Session["UserID"]
                                ?? 0);
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

        // GET: Gen_ServiceRates/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var rate = db.Gen_ServiceRates
                        .Include(r => r.Gen_TheServices)
                        .Include(r => r.Branches)
                        .FirstOrDefault(r => r.Id == id.Value);
            if (rate == null) return HttpNotFound();
            return View(rate);
        }

        // GET: Gen_ServiceRates/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var rate = db.Gen_ServiceRates.Find(id);
            if (rate == null) return HttpNotFound();
            return View(rate);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var rate = db.Gen_ServiceRates.Find(id);
            db.Gen_ServiceRates.Remove(rate);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        // Export to Excel
        [HttpGet, Obsolete]
        public ActionResult ExportToExcel()
        {
            var list = (
                from r in db.Gen_ServiceRates
                    .Include(x => x.Gen_ServiceType)
                    .Include(x => x.Branches)
                join p in db.Gen_Products on r.ProductId equals p.Id into pj
                from p in pj.DefaultIfEmpty()
                select new
                {
                    r.Id,
                    ProductName = p != null ? p.Prod_Name : "",
                    ServiceName = r.Gen_ServiceType.Service_Type,
                    r.Charge_Fector,
                    BranchName = r.Branches.Name
                }).ToList();

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Rates");

                ws.Cells[1, 1].Value = "ID";
                ws.Cells[1, 2].Value = "Product";
                ws.Cells[1, 3].Value = "Service Type";
                ws.Cells[1, 4].Value = "Charge";
                ws.Cells[1, 5].Value = "Branch";

                for (int i = 0; i < list.Count; i++)
                {
                    var r = list[i];
                    int row = i + 2;

                    ws.Cells[row, 1].Value = r.Id;
                    ws.Cells[row, 2].Value = r.ProductName ?? "";
                    ws.Cells[row, 3].Value = r.ServiceName ?? "";
                    ws.Cells[row, 4].Value = r.Charge_Fector;
                    ws.Cells[row, 5].Value = r.BranchName ?? "";
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Gen_ServiceRates.xlsx");
            }
        }

        // Export to PDF
        public ActionResult ExportToPdf()
        {
            var list = (
                from r in db.Gen_ServiceRates
                    .Include(x => x.Gen_ServiceType)
                    .Include(x => x.Branches)
                join p in db.Gen_Products on r.ProductId equals p.Id into pj
                from p in pj.DefaultIfEmpty()
                select new
                {
                    r.Id,
                    ProductName = p != null ? p.Prod_Name : "",
                    ServiceName = r.Gen_ServiceType.Service_Type,
                    r.Charge_Fector,
                    BranchName = r.Branches.Name
                }).ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var table = new PdfPTable(5) { WidthPercentage = 100 };

                foreach (var h in new[] { "ID", "Product", "Service Type", "Charge", "Branch" })
                    table.AddCell(new PdfPCell(new Phrase(h))
                    { BackgroundColor = BaseColor.LIGHT_GRAY });

                foreach (var r in list)
                {
                    table.AddCell(r.Id.ToString());
                    table.AddCell(r.ProductName ?? "");
                    table.AddCell(r.ServiceName ?? "");
                    table.AddCell(r.Charge_Fector ?? "");
                    table.AddCell(r.BranchName ?? "");
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", "Gen_ServiceRates.pdf");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}