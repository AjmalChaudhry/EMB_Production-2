using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/suppliers")]
    public class Gen_SuppliersController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Suppliers)
        // ─────────────────────────────────────────────────────────────
        private void LogSupplierAudit(int id, string action,
                                      string oldValue = null, string newValue = null,
                                      string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_SuppliersAuditLog
       (SupplierId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@sid, @act, @cb, @when, @ov, @nv, @det)",
                    new SqlParameter("@sid",  id),
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
                var entries = db.Database.SqlQuery<SupplierAuditEntry>(@"
SELECT  a.Id, a.SupplierId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_SuppliersAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.SupplierId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, supplierId = e.SupplierId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeSupplierError(ex) });
            }
        }

        private class SupplierAuditEntry
        {
            public int Id { get; set; }
            public int SupplierId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeSupplierError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Suppliers)
        // ─────────────────────────────────────────────────────────────
        public class SupplierBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkSupplier(List<int> ids, Func<int, Gen_Suppliers, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_Suppliers.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.SupplierName ?? ("SUP#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeSupplierError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(SupplierBulkIdsDto dto)
        {
            var data = DoBulkSupplier(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                db.SaveChanges();
                LogSupplierAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(SupplierBulkIdsDto dto)
        {
            var data = DoBulkSupplier(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                db.SaveChanges();
                LogSupplierAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(SupplierBulkIdsDto dto)
        {
            var data = DoBulkSupplier(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                db.SaveChanges();
                LogSupplierAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // ===================== LIST =====================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
     string search = "",
     string activeFilter = "",
     int page = 1,
     int pageSize = 10)
        {
            var query = db.Gen_Suppliers
                          .Where(s => s.Deleted == false || s.Deleted == null);

            // ---- ACTIVE FILTER ----
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                if (activeFilter == "active")
                    query = query.Where(x => x.Active == true);
                else if (activeFilter == "inactive")
                    query = query.Where(x => x.Active == false || x.Active == null);
            }

            // ---- SEARCH ----
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(x =>
                    (x.SupplierName != null && x.SupplierName.Contains(search)) ||
                    (x.Contact != null && x.Contact.Contains(search)) ||
                    (x.Phone != null && x.Phone.Contains(search)) ||
                    (x.Email != null && x.Email.Contains(search)) ||
                    (x.Address != null && x.Address.Contains(search))   // ✔ Added search on Address
                );
            }

            var total = query.Count();

            var items = query
                .OrderBy(x => x.SupplierName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new SupplierRowViewModel
                {
                    Id = x.Id,
                    SupplierName = x.SupplierName,
                    Contact = x.Contact,
                    Phone = x.Phone,
                    Email = x.Email,
                    Address = x.Address,                           // ✔ ADDED

                    BranchId = x.BranchId,
                    BranchName = x.Branches != null ? x.Branches.Name : null,

                    Active = x.Active ?? false,
                    Deleted = x.Deleted ?? false,

                    CreatedBy = x.CreatedBy,
                    CreatedDate = x.CreatedDate,
                    UpdatedBy = x.UpdatedBy,
                    UpdatedDate = x.UpdatedDate
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = items,
                total = total
            }, JsonRequestBehavior.AllowGet);
        }

        // ===================== DETAIL =====================
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var x = db.Gen_Suppliers
                      .FirstOrDefault(s =>
                           s.Id == id &&
                           (s.Deleted == false || s.Deleted == null));

            if (x == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Supplier not found."
                }, JsonRequestBehavior.AllowGet);
            }

            var vm = new SupplierDetailViewModel
            {
                Id = x.Id,
                SupplierName = x.SupplierName,
                Contact = x.Contact,
                Phone = x.Phone,
                Email = x.Email,
                Address = x.Address,

                BranchId = x.BranchId,
                BranchName = x.Branches != null ? x.Branches.Name : null,

                Active = x.Active ?? false,
                Deleted = x.Deleted ?? false,

                CreatedBy = x.CreatedBy,
                CreatedDate = x.CreatedDate.HasValue? x.CreatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null,                
                UpdatedBy = x.UpdatedBy,
                UpdatedDate = x.UpdatedDate.HasValue? x.UpdatedDate.Value.ToString("yyyy-MM-ddTHH:mm:ss"): null};


            return Json(new { success = true, data = vm },
                        JsonRequestBehavior.AllowGet);
        }

        // ===================== Phase H.0 - SUPPLIER 360 PROFILE =====================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var sup = db.Gen_Suppliers
                    .FirstOrDefault(x => x.Id == id && (x.Deleted == false || x.Deleted == null));
                if (sup == null) return JsonError("Supplier not found.");

                string supBranchName = sup.Branches != null ? sup.Branches.Name : "";
                var supplier = new
                {
                    id = sup.Id,
                    supplierName = sup.SupplierName,
                    contact = sup.Contact,
                    phone = sup.Phone,
                    email = sup.Email,
                    address = sup.Address,
                    branchId = sup.BranchId,
                    branchName = supBranchName,
                    active = sup.Active ?? false,
                    createdDate = sup.CreatedDate.HasValue ? sup.CreatedDate.Value.ToString("yyyy-MM-dd") : null,
                    updatedDate = sup.UpdatedDate.HasValue ? sup.UpdatedDate.Value.ToString("yyyy-MM-dd") : null
                };

                // Step 1: pull purchase headers (join Branches manually since Purchase has no nav).
                var purchaseHeaders = (
                    from p in db.Purchase
                    join br in db.Branches on p.BranchId equals br.Id into bj
                    from br in bj.DefaultIfEmpty()
                    where p.SupplierId == id && p.Deleted != true
                    select new
                    {
                        p.Id,
                        p.PurchaseDate,
                        p.InvoiceNo,
                        p.IsPosted,
                        p.BranchId,
                        BranchName = br != null ? br.Name : ""
                    }
                ).ToList();

                var purchaseIds = purchaseHeaders.Select(p => p.Id).ToList();

                // Step 2: pull line items in a separate query, materialize, then merge in-memory.
                var rawLines = db.PurchaseItems
                    .Where(i => purchaseIds.Contains(i.PurchaseId) && i.Deleted != true)
                    .Select(i => new { i.PurchaseId, i.ItemType, i.ItemId, i.Qty, Rate = i.Rate ?? 0m })
                    .ToList();

                var linesByPurchase = rawLines
                    .GroupBy(i => i.PurchaseId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                int purchaseCount = purchaseHeaders.Count;
                int postedCount = purchaseHeaders.Count(p => p.IsPosted);
                int draftCount  = purchaseCount - postedCount;

                var allLines = rawLines;
                decimal totalQty    = allLines.Sum(i => i.Qty);
                decimal totalAmount = allLines.Sum(i => i.Qty * i.Rate);
                decimal avgOrder    = purchaseCount > 0 ? Math.Round(totalAmount / purchaseCount, 2) : 0m;

                DateTime? lastPurchaseDate  = purchaseHeaders.Any() ? purchaseHeaders.Max(p => (DateTime?)p.PurchaseDate) : null;
                DateTime? firstPurchaseDate = purchaseHeaders.Any() ? purchaseHeaders.Min(p => (DateTime?)p.PurchaseDate) : null;

                var kpis = new
                {
                    purchaseCount,
                    postedCount,
                    draftCount,
                    totalQty = Math.Round(totalQty, 2),
                    totalAmount = Math.Round(totalAmount, 2),
                    avgOrderAmount = avgOrder,
                    lastPurchaseDate  = lastPurchaseDate.HasValue  ? lastPurchaseDate.Value.ToString("dd-MMM-yyyy")  : null,
                    firstPurchaseDate = firstPurchaseDate.HasValue ? firstPurchaseDate.Value.ToString("dd-MMM-yyyy") : null
                };

                // Resolve item names with Phase E canonical mapping
                var rawIds = allLines.Where(i => (i.ItemType ?? "RAW").Trim().ToUpper() == "RAW")
                    .Select(i => i.ItemId).Distinct().ToList();
                var threadIds = allLines.Where(i => (i.ItemType ?? "").Trim().ToUpper() == "THREAD")
                    .Select(i => i.ItemId).Distinct().ToList();

                var rawMap = db.Gen_RawItems
                    .Where(r => rawIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);

                var threadInfo = db.Gen_Threads
                    .Where(th => threadIds.Contains(th.Id))
                    .Select(th => new { th.Id, th.RawItemId, th.Code, th.Name })
                    .ToList();
                var threadIdToRawId = threadInfo
                    .Where(th => th.RawItemId.HasValue)
                    .ToDictionary(th => th.Id, th => th.RawItemId.Value);
                var threadLinkedRawIds = threadIdToRawId.Values.Distinct().ToList();
                var threadLinkedRawNames = db.Gen_RawItems
                    .Where(r => threadLinkedRawIds.Contains(r.Id))
                    .ToDictionary(r => r.Id, r => r.ItemName);
                var threadStandaloneLabel = threadInfo.ToDictionary(
                    th => th.Id,
                    th => ((th.Code ?? "") + " " + (th.Name ?? "")).Trim());

                var canonicalized = allLines.Select(i =>
                {
                    var itType = (i.ItemType ?? "RAW").Trim().ToUpper();
                    int canonical = itType == "THREAD" && threadIdToRawId.ContainsKey(i.ItemId)
                                    ? threadIdToRawId[i.ItemId]
                                    : i.ItemId;
                    return new { ItemType = itType, CanonicalRawId = canonical, SourceId = i.ItemId, i.Qty, i.Rate };
                }).ToList();

                var topItems = canonicalized
                    .GroupBy(i => i.CanonicalRawId)
                    .Select(g =>
                    {
                        string name = "-";
                        if (rawMap.ContainsKey(g.Key)) name = rawMap[g.Key];
                        else
                        {
                            var ft = g.FirstOrDefault(x => x.ItemType == "THREAD");
                            if (ft != null && threadStandaloneLabel.ContainsKey(ft.SourceId))
                                name = threadStandaloneLabel[ft.SourceId];
                        }
                        var types = g.Select(x => x.ItemType).Distinct().OrderBy(t => t).ToList();
                        return new
                        {
                            itemName = name,
                            itemType = string.Join(" + ", types),
                            totalQty = Math.Round(g.Sum(x => x.Qty), 2),
                            totalAmount = Math.Round(g.Sum(x => x.Qty * x.Rate), 2)
                        };
                    })
                    .OrderByDescending(x => x.totalAmount)
                    .Take(10)
                    .ToList();

                // Monthly trend - last 12 months
                var trendEnd = DateTime.Now.Date;
                var trendStart = new DateTime(trendEnd.Year, trendEnd.Month, 1).AddMonths(-11);

                // Build month-amount/count lookup in-memory
                var monthlyTrend = new List<object>();
                for (int m = 0; m < 12; m++)
                {
                    var monthStart = trendStart.AddMonths(m);
                    var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
                    var idsInMonth = purchaseHeaders
                        .Where(p => p.PurchaseDate >= monthStart && p.PurchaseDate <= monthEnd)
                        .Select(p => p.Id)
                        .ToList();
                    int monthCount = idsInMonth.Count;
                    decimal monthAmount = rawLines
                        .Where(l => idsInMonth.Contains(l.PurchaseId))
                        .Sum(l => l.Qty * l.Rate);
                    monthlyTrend.Add(new
                    {
                        month = monthStart.ToString("MMM yyyy"),
                        count = monthCount,
                        amount = Math.Round(monthAmount, 2)
                    });
                }

                // Recent purchases - last 10 (sum via the per-purchase lookup)
                var recentPurchases = purchaseHeaders
                    .OrderByDescending(p => p.Id)
                    .Take(10)
                    .Select(p =>
                    {
                        var lines = linesByPurchase.ContainsKey(p.Id)
                                    ? linesByPurchase[p.Id]
                                    : null;
                        decimal pTotalQty    = lines != null ? lines.Sum(l => l.Qty) : 0m;
                        decimal pTotalAmount = lines != null ? lines.Sum(l => l.Qty * l.Rate) : 0m;
                        return new
                        {
                            id = p.Id,
                            dateText = p.PurchaseDate.ToString("dd-MMM-yyyy"),
                            invoiceNo = p.InvoiceNo,
                            branchName = p.BranchName,
                            isPosted = p.IsPosted,
                            totalQty = Math.Round(pTotalQty, 2),
                            totalAmount = Math.Round(pTotalAmount, 2)
                        };
                    })
                    .ToList();

                return JsonSuccess("OK", new
                {
                    supplier,
                    kpis,
                    topItems,
                    monthlyTrend,
                    recentPurchases
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        // ===================== CREATE =====================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(SupplierDto dto)
        {
            if (dto == null)
                return JsonError("Invalid data.");

            if (string.IsNullOrWhiteSpace(dto.SupplierName))
                return JsonError("Supplier name is required.");

            dto.SupplierName = dto.SupplierName.Trim();
            dto.Contact = dto.Contact?.Trim();
            dto.Phone = dto.Phone?.Trim();
            dto.Email = dto.Email?.Trim();
            dto.Address = dto.Address?.Trim();

            // ---- UNIQUE NAME CHECK ----
            var exists = db.Gen_Suppliers.Any(x =>
                x.SupplierName == dto.SupplierName &&
                (x.Deleted == false || x.Deleted == null)
            );

            if (exists)
                return JsonError("A supplier with this name already exists.");

            var userId = GetSafeCurrentUserId();
            var now = DateTime.Now;

            var entity = new Gen_Suppliers
            {
                SupplierName = dto.SupplierName,
                Contact = dto.Contact,
                Phone = dto.Phone,
                Email = dto.Email,
                Address = dto.Address,

                BranchId = dto.BranchId,

                Active = dto.Active,
                Deleted = false,

                CreatedBy = userId,
                CreatedDate = now
            };

            db.Gen_Suppliers.Add(entity);
            db.SaveChanges();

            LogSupplierAudit(entity.Id, "CREATED",
                oldValue: null, newValue: "Active",
                detail: entity.SupplierName ?? "-");

            return JsonSuccess("Supplier created successfully.");
        }


        // ===================== UPDATE =====================
        // ===================== UPDATE =====================
        [HttpPost]
        [Route("update")]
        public ActionResult Update(SupplierDto dto)
        {
            if (dto == null || dto.Id <= 0)
                return JsonError("Invalid data.");

            if (string.IsNullOrWhiteSpace(dto.SupplierName))
                return JsonError("Supplier name is required.");

            dto.SupplierName = dto.SupplierName.Trim();
            dto.Contact = dto.Contact?.Trim();
            dto.Phone = dto.Phone?.Trim();
            dto.Email = dto.Email?.Trim();
            dto.Address = dto.Address?.Trim();

            var entity = db.Gen_Suppliers
                           .FirstOrDefault(x =>
                               x.Id == dto.Id &&
                               (x.Deleted == false || x.Deleted == null));

            if (entity == null)
                return JsonError("Supplier not found.");

            // ---- UNIQUE NAME CHECK ----
            var exists = db.Gen_Suppliers.Any(x =>
                x.Id != dto.Id &&
                x.SupplierName == dto.SupplierName &&
                (x.Deleted == false || x.Deleted == null)
            );

            if (exists)
                return JsonError("Another supplier with this name already exists.");

            entity.SupplierName = dto.SupplierName;
            entity.Contact = dto.Contact;
            entity.Phone = dto.Phone;
            entity.Email = dto.Email;
            entity.Address = dto.Address;

            entity.BranchId = dto.BranchId;
            entity.Active = dto.Active;

            entity.UpdatedBy = GetSafeCurrentUserId();
            entity.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            LogSupplierAudit(entity.Id, "EDITED",
                oldValue: null, newValue: entity.Active == true ? "Active" : "Inactive",
                detail: entity.SupplierName ?? "-");

            return JsonSuccess("Supplier updated successfully.");
        }


        // ===================== DELETE (soft) =====================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            var entity = db.Gen_Suppliers
                           .FirstOrDefault(x => x.Id == id &&
                                           (x.Deleted == false || x.Deleted == null));

            if (entity == null)
            {
                return Json(new { success = false, message = "Supplier not found." });
            }

            entity.Deleted = true;
            entity.Active = false;
            entity.UpdatedBy = GetSafeCurrentUserId();
            entity.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            LogSupplierAudit(id, "DELETED",
                oldValue: "Active", newValue: "Deleted",
                detail: entity.SupplierName ?? "-");

            return Json(new
            {
                success = true,
                message = "Supplier deleted successfully."
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}

