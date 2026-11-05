using EMB_Production.Models;
using EMB_Production.ViewModels;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Mvc;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/component-materials")]
    public class GenComponentMaterialsController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Component Materials)
        //  Append-only — best-effort writes, swallow-on-fail.
        // ─────────────────────────────────────────────────────────────
        private void LogCmAudit(int id, string action,
                                string oldValue = null, string newValue = null,
                                string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_ComponentMaterialsAuditLog
       (CmId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@cid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@cid",  id),
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
                var entries = db.Database.SqlQuery<CmAuditEntry>(@"
SELECT  a.Id, a.CmId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_ComponentMaterialsAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.CmId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, cmId = e.CmId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();

                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeCmError(ex) });
            }
        }

        private class CmAuditEntry
        {
            public int Id { get; set; }
            public int CmId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeCmError(Exception ex)
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

        private void LockCmSlice(int compId, int rawItemId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT TOP 1 1 FROM dbo.Gen_ComponentMaterials WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE CompId = @cid AND RawItemId = @rid",
                new SqlParameter("@cid", compId),
                new SqlParameter("@rid", rawItemId));
        }
       
        [HttpGet]
        [Route("")]
        public ActionResult Index()
        {
            return View();
        }

        /* =====================================================
           LIST
        ===================================================== */
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            int page = 1,
            int pageSize = 10,
            string search = "",
            string activeFilter = "",
            bool showDeleted = false,
            int? compId = null)
        {
            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 10 : pageSize;

            // Bug-fix: Unit column was rendering "-" for every row because
            // legacy data left cm.UnitId NULL — designers added the
            // material before the unit field was made mandatory. The raw
            // item itself ALWAYS carries a unit (Gen_RawItems.UnitId is
            // not-null), so we join Gen_Units a second time via the raw
            // item and coalesce: prefer the component-material's explicit
            // unit when set, else fall back to the raw item's unit.
            var query =
                from cm in db.Gen_ComponentMaterials

                join c in db.Gen_Components on cm.CompId equals c.Id into c1
                from c in c1.DefaultIfEmpty()

                join r in db.Gen_RawItems on cm.RawItemId equals r.Id into r1
                from r in r1.DefaultIfEmpty()

                join u in db.Gen_Units on cm.UnitId equals u.Id into u1
                from u in u1.DefaultIfEmpty()

                // Fallback join — raw item's unit (always populated).
                join ru in db.Gen_Units on (r != null ? (int?)r.UnitId : null) equals ru.Id into ru1
                from ru in ru1.DefaultIfEmpty()

                select new
                {
                    cm.Id,
                    cm.CompId,
                    cm.RawItemId,
                    cm.QtyPerUnit,
                    cm.UnitId,
                    cm.Remarks,
                    cm.Active,
                    cm.Deleted,
                    cm.CreatedDate,

                    ComponentCode = c != null ? c.Code.ToString() : "",
                    ComponentName = c != null ? c.Name : "",

                    RawItemName = r != null ? r.ItemName : "",

                    // Coalesce — explicit unit on the row wins, else
                    // inherit from the raw item so the column is never
                    // blank when the raw item knows its unit.
                    UnitCode = u != null ? u.UnitCode
                             : (ru != null ? ru.UnitCode : ""),
                    UnitName = u != null ? u.UnitName
                             : (ru != null ? ru.UnitName : "")
                };

            if (!showDeleted)
                query = query.Where(x => x.Deleted == false);

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                activeFilter = activeFilter.Trim().ToLower();

                if (activeFilter == "active")
                    query = query.Where(x => x.Active == true);
                else if (activeFilter == "inactive")
                    query = query.Where(x => x.Active == false);
            }

            if (compId.HasValue && compId.Value > 0)
                query = query.Where(x => x.CompId == compId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(x =>
                    x.ComponentName.Contains(search) ||
                    x.ComponentCode.Contains(search) ||
                    x.RawItemName.Contains(search) ||
                    x.UnitCode.Contains(search) ||
                    x.Remarks.Contains(search));
            }

            var totalCount = query.Count();

            // Phase R.2 - summary aggregates across the filtered set
            var summaryEntities = query.Select(x => new
            {
                x.CompId,
                x.RawItemId,
                x.UnitCode,
                x.QtyPerUnit,
                x.Active
            }).ToList();

            int activeCount = summaryEntities.Count(s => s.Active);
            int inactiveCount = summaryEntities.Count(s => !s.Active);
            int distinctComponents = summaryEntities.Select(s => s.CompId).Distinct().Count();
            int distinctRawItems = summaryEntities.Select(s => s.RawItemId).Distinct().Count();
            decimal totalQty = summaryEntities.Sum(s => s.QtyPerUnit);
            var byUnit = summaryEntities
                .Where(s => !string.IsNullOrWhiteSpace(s.UnitCode))
                .GroupBy(s => s.UnitCode)
                .Select(g => new { unit = g.Key, count = g.Count(), qty = g.Sum(x => x.QtyPerUnit) })
                .OrderByDescending(x => x.count)
                .ToList();

            var summary = new
            {
                count = totalCount,
                activeCount,
                inactiveCount,
                distinctComponents,
                distinctRawItems,
                totalQty = Math.Round(totalQty, 2),
                byUnit
            };

            var items = query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
                .Select(x => new ComponentMaterialRowViewModel
                {
                    Id = x.Id,
                    CompId = x.CompId,
                    ComponentCode = x.ComponentCode,
                    ComponentName = x.ComponentName,
                    RawItemId = x.RawItemId,
                    RawItemName = x.RawItemName,
                    QtyPerUnit = x.QtyPerUnit,
                    UnitId = x.UnitId,
                    UnitCode = x.UnitCode,
                    UnitName = x.UnitName,
                    Remarks = x.Remarks,
                    Active = x.Active,
                    Deleted = x.Deleted,
                    IsRecent = x.CreatedDate.HasValue
                               && (DateTime.Now - x.CreatedDate.Value).TotalHours < 24
                })
                .ToList();

            return JsonSuccess("Component materials loaded successfully.", new
            {
                items,
                summary,
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }

        // ====================================================================
        // Phase R.2 - PROFILE
        // ====================================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                // Header — same Unit fallback as the list: prefer the
                // row's explicit UnitId, else inherit from the raw item's
                // own UnitId (always populated on Gen_RawItems).
                var raw = (
                    from cm in db.Gen_ComponentMaterials
                    join c in db.Gen_Components on cm.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    join d in db.Gen_Design on (c != null ? c.D_Code : (int?)null) equals d.Id into d1
                    from d in d1.DefaultIfEmpty()
                    join r in db.Gen_RawItems on cm.RawItemId equals r.Id into r1
                    from r in r1.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into u1
                    from u in u1.DefaultIfEmpty()
                    join ru in db.Gen_Units on (r != null ? (int?)r.UnitId : null) equals ru.Id into ru1
                    from ru in ru1.DefaultIfEmpty()
                    where cm.Id == id
                    select new
                    {
                        cm.Id,
                        cm.CompId,
                        ComponentCode = c != null ? (c.Code == null ? "" : c.Code.ToString()) : "",
                        ComponentName = c != null ? c.Name : "",
                        DesignId = c != null ? c.D_Code : (int?)null,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",
                        cm.RawItemId,
                        RawItemName = r != null ? r.ItemName : "",
                        cm.QtyPerUnit,
                        cm.UnitId,
                        UnitCode = u != null ? u.UnitCode
                                 : (ru != null ? ru.UnitCode : ""),
                        UnitName = u != null ? u.UnitName
                                 : (ru != null ? ru.UnitName : ""),
                        cm.Remarks,
                        cm.Active,
                        cm.Deleted,
                        cm.CreatedDate,
                        cm.UpdatedDate
                    }
                ).FirstOrDefault();

                if (raw == null) return JsonError("Component material not found.");

                // Sibling materials for the same component.
                // Two bugs fixed here:
                //   1. anonymous fields renamed to camelCase so the
                //      Angular template (s.id / s.rawItemName / …) can
                //      actually read them (PascalCase was being silently
                //      dropped to undefined).
                //   2. Unit falls back to the raw item's unit, same fix
                //      as the list endpoint.
                var siblings = (
                    from cm in db.Gen_ComponentMaterials
                    join r in db.Gen_RawItems on cm.RawItemId equals r.Id into r1
                    from r in r1.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into u1
                    from u in u1.DefaultIfEmpty()
                    join ru in db.Gen_Units on (r != null ? (int?)r.UnitId : null) equals ru.Id into ru1
                    from ru in ru1.DefaultIfEmpty()
                    where cm.CompId == raw.CompId
                       && cm.Id != id
                       && cm.Deleted == false
                    orderby cm.Id
                    select new
                    {
                        id         = cm.Id,
                        rawItemId  = cm.RawItemId,
                        rawItemName = r != null ? r.ItemName : "-",
                        qtyPerUnit = cm.QtyPerUnit,
                        unitCode   = u != null ? u.UnitCode
                                   : (ru != null ? ru.UnitCode : ""),
                        remarks    = cm.Remarks,
                        active     = cm.Active
                    }
                ).ToList();

                var header = new
                {
                    id = raw.Id,
                    componentId = raw.CompId,
                    componentCode = raw.ComponentCode,
                    componentName = raw.ComponentName,
                    designId = raw.DesignId,
                    designCode = raw.DesignCode,
                    designName = raw.DesignName,
                    rawItemId = raw.RawItemId,
                    rawItemName = raw.RawItemName,
                    qtyPerUnit = raw.QtyPerUnit,
                    unitId = raw.UnitId,
                    unitCode = raw.UnitCode,
                    unitName = raw.UnitName,
                    remarks = raw.Remarks,
                    active = raw.Active,
                    createdDateText = raw.CreatedDate.HasValue ? raw.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = raw.UpdatedDate.HasValue ? raw.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                var kpis = new
                {
                    qtyPerUnit = raw.QtyPerUnit,
                    siblingCount = siblings.Count(),
                    componentMaterialCount = siblings.Count() + 1
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

        /* =====================================================
           DETAIL
        ===================================================== */
        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var raw =
            (
                from cm in db.Gen_ComponentMaterials

                join c in db.Gen_Components on cm.CompId equals c.Id into c1
                from c in c1.DefaultIfEmpty()

                join r in db.Gen_RawItems on cm.RawItemId equals r.Id into r1
                from r in r1.DefaultIfEmpty()

                join u in db.Gen_Units on cm.UnitId equals u.Id into u1
                from u in u1.DefaultIfEmpty()

                join cu in db.Adm_Users on cm.CreatedBy equals cu.Id into cu1
                from cu in cu1.DefaultIfEmpty()

                join uu in db.Adm_Users on cm.UpdatedBy equals uu.Id into uu1
                from uu in uu1.DefaultIfEmpty()

                where cm.Id == id && cm.Deleted == false

                select new
                {
                    cm.Id,
                    cm.CompId,
                    cm.RawItemId,
                    cm.QtyPerUnit,
                    cm.UnitId,
                    cm.Remarks,
                    cm.Active,
                    cm.Deleted,
                    cm.CreatedDate,
                    cm.UpdatedDate,

                    ComponentCode = c != null ? c.Code.ToString() : "",
                    ComponentName = c != null ? c.Name : "",

                    RawItemName = r != null ? r.ItemName : "",

                    UnitCode = u != null ? u.UnitCode : "",
                    UnitName = u != null ? u.UnitName : "",

                    CreatedEmployeeId = cu != null ? (int?)cu.EmployeeId : null,
                    UpdatedEmployeeId = uu != null ? (int?)uu.EmployeeId : null
                }
            ).FirstOrDefault();

            if (raw == null)
                return JsonError("Component material not found.");

            var dto = new ComponentMaterialDetailViewModel
            {
                Id = raw.Id,
                CompId = raw.CompId,
                ComponentCode = raw.ComponentCode,
                ComponentName = raw.ComponentName,
                RawItemId = raw.RawItemId,
                RawItemName = raw.RawItemName,
                QtyPerUnit = raw.QtyPerUnit,
                UnitId = raw.UnitId,
                UnitCode = raw.UnitCode,
                UnitName = raw.UnitName,
                Remarks = raw.Remarks ?? "",
                Active = raw.Active,
                Deleted = raw.Deleted,

                CreatedByEmployeeId = raw.CreatedEmployeeId,
                UpdatedByEmployeeId = raw.UpdatedEmployeeId,

                CreatedDateText = raw.CreatedDate.HasValue
                    ? raw.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                    : "",

                UpdatedDateText = raw.UpdatedDate.HasValue
                    ? raw.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                    : ""
            };

            return JsonSuccess("Component material loaded successfully.", dto);
        }

        /* =====================================================
           SAVE
        ===================================================== */
        [HttpPost]
        [Route("save")]
        public ActionResult Save(ComponentMaterialCreateUpdateDto dto)
        {
            try
            {
                if (dto == null)
                    return JsonError("Invalid request.");

                if (dto.CompId <= 0)
                    return JsonError("Component is required.");

                if (dto.RawItemId <= 0)
                    return JsonError("Raw item is required.");

                if (dto.QtyPerUnit <= 0)
                    return JsonError("Qty per unit must be greater than zero.");

                var compExists = db.Gen_Components.Any(x => x.Id == dto.CompId && x.Deleted == false);
                if (!compExists)
                    return JsonError("Selected component not found.");

                var rawItemExists = db.Gen_RawItems.Any(x => x.Id == dto.RawItemId && x.Deleted == false);
                if (!rawItemExists)
                    return JsonError("Selected raw item not found.");

                if (dto.UnitId.HasValue && dto.UnitId.Value > 0)
                {
                    var unitExists = db.Gen_Units.Any(x => x.Id == dto.UnitId.Value);
                    if (!unitExists)
                        return JsonError("Selected unit not found.");
                }

                int userId = GetCurrentUserId();
                var now = DateTime.Now;

                // duplicate check
                var dup = db.Gen_ComponentMaterials.FirstOrDefault(x =>
                    x.Id != dto.Id &&
                    x.CompId == dto.CompId &&
                    x.RawItemId == dto.RawItemId &&
                    x.Deleted == false);

                if (dup != null)
                    return JsonError("This raw item is already added against the selected component.");

                if (dto.Id > 0)
                {
                    var entity = db.Gen_ComponentMaterials.FirstOrDefault(x => x.Id == dto.Id && x.Deleted == false);
                    if (entity == null)
                        return JsonError("Component material not found.");

                    entity.CompId = dto.CompId;
                    entity.RawItemId = dto.RawItemId;
                    entity.QtyPerUnit = dto.QtyPerUnit;
                    entity.UnitId = dto.UnitId;
                    entity.Remarks = dto.Remarks;
                    entity.Active = dto.Active;

                    entity.UpdatedBy = userId;
                    entity.UpdatedDate = now;

                    db.SaveChanges();

                    LogCmAudit(entity.Id, "EDITED",
                        oldValue: null,
                        newValue: "Qty " + dto.QtyPerUnit + " UnitId " + (dto.UnitId?.ToString() ?? "-"),
                        detail: "Component #" + dto.CompId + " / Raw Item #" + dto.RawItemId);

                    return JsonSuccess("Component material updated successfully.");
                }
                else
                {
                    var entity = new Gen_ComponentMaterials
                    {
                        CompId = dto.CompId,
                        RawItemId = dto.RawItemId,
                        QtyPerUnit = dto.QtyPerUnit,
                        UnitId = dto.UnitId,
                        Remarks = dto.Remarks,
                        Active = dto.Active,
                        Deleted = false,
                        CreatedBy = userId,
                        CreatedDate = now
                    };

                    db.Gen_ComponentMaterials.Add(entity);
                    db.SaveChanges();

                    LogCmAudit(entity.Id, "CREATED",
                        oldValue: null,
                        newValue: "Active",
                        detail: "Component #" + dto.CompId + " / Raw Item #" + dto.RawItemId
                              + " / Qty " + dto.QtyPerUnit);

                    return JsonSuccess("Component material created successfully.");
                }
            }
            catch (Exception ex)
            {
                return JsonError("Error while saving component material. " + DescribeCmError(ex));
            }
        }

        /* =====================================================
           DELETE
        ===================================================== */
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var entity = db.Gen_ComponentMaterials.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (entity == null)
                    return JsonError("Component material not found.");

                entity.Deleted = true;
                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                LogCmAudit(id, "DELETED",
                    oldValue: "Active",
                    newValue: "Deleted",
                    detail: "Component #" + entity.CompId + " / Raw Item #" + entity.RawItemId);

                return JsonSuccess("Component material deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Error while deleting component material. " + DescribeCmError(ex));
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        // ─────────────────────────────────────────────────────────────
        public class CmBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkCm(List<int> ids, Func<int, Gen_ComponentMaterials, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_ComponentMaterials.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code = "CM#" + id, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code = "CM#" + id, reason = DescribeCmError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(CmBulkIdsDto dto)
        {
            var data = DoBulkCm(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCmAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(CmBulkIdsDto dto)
        {
            var data = DoBulkCm(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCmAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(CmBulkIdsDto dto)
        {
            var data = DoBulkCm(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCmAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }

        /* =====================================================
           DROPDOWNS
        ===================================================== */
        [HttpGet]
        [Route("ddl-components")]
        public ActionResult DdlComponents()
        {
            var data = db.Gen_Components
                .Where(x => x.Deleted == false && x.Active == true)
                .OrderBy(x => x.Name)
                .Select(x => new DropdownItemViewModel
                {
                    Id = x.Id,
                    Code = x.Code.ToString(),
                    Name = x.Name
                })
                .ToList();

            return JsonSuccess("Components loaded successfully.", data);
        }

        [HttpGet]
        [Route("ddl-raw-items")]
        public ActionResult DdlRawItems()
        {
            var data = db.Gen_RawItems
                .Where(x => x.Deleted == false && x.Active == true)
                .OrderBy(x => x.ItemName)
                .Select(x => new DropdownItemViewModel
                {
                    Id = x.Id,
                    Code = "", // FIXED
            Name = x.ItemName
                })
                .ToList();

            return JsonSuccess("Raw items loaded successfully.", data);
        }

        [HttpGet]
        [Route("ddl-units")]
        public ActionResult DdlUnits()
        {
            var data = db.Gen_Units
                .OrderBy(x => x.UnitName)
                .Select(x => new DropdownItemViewModel
                {
                    Id = x.Id,
                    Code = x.UnitCode,
                    Name = x.UnitName
                })
                .ToList();

            return JsonSuccess("Units loaded successfully.", data);
        }
    }
}