using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;

namespace EMB_Production.Controllers
{
    // ─────────────────────────────────────────────────────────────────
    //  Sprint-1 — canonical BOM status values. BOM is master data with
    //  no transactional workflow; the lifecycle is just Active /
    //  Inactive / Deleted (soft).
    // ─────────────────────────────────────────────────────────────────
    public static class BomStatus
    {
        public const string ACTIVE   = "Active";
        public const string INACTIVE = "Inactive";
        public const string DELETED  = "Deleted";
    }

    [RoutePrefix("emb-products/gen-bom")]
    public class GenBomController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG
        //  Append-only table that records every state-changing event on
        //  a BOM row: CREATED, EDITED, ACTIVATED, DEACTIVATED, DELETED,
        //  CLONED. Best-effort writes — wrapped in try/catch so a
        //  missing table never blocks the user's main action.
        // ─────────────────────────────────────────────────────────────
        private void LogBomAudit(int bomId, string action,
                                 string oldValue = null,
                                 string newValue = null,
                                 string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_BOMAuditLog
       (BomId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@bid, @act, @uid, @when, @ov, @nv, @det)",
                    new SqlParameter("@bid",  bomId),
                    new SqlParameter("@act",  action ?? ""),
                    new SqlParameter("@uid",  GetSafeCurrentUserId()),
                    new SqlParameter("@when", DateTime.Now),
                    new SqlParameter("@ov",   (object)(oldValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@nv",   (object)(newValue ?? "") ?? DBNull.Value),
                    new SqlParameter("@det",  (object)(detail   ?? "") ?? DBNull.Value));
            }
            catch
            {
                // Observability, not a hard requirement.
            }
        }

        [HttpGet, Route("audit-log/{id:int}")]
        public ActionResult AuditLog(int id)
        {
            try
            {
                var entries = db.Database.SqlQuery<BomAuditEntry>(@"
SELECT  a.Id,
        a.BomId,
        a.Action,
        a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate,
        a.OldValue,
        a.NewValue,
        a.Detail
  FROM  dbo.Gen_BOMAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.BomId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id              = e.Id,
                    bomId           = e.BomId,
                    action          = e.Action,
                    changedBy       = e.ChangedBy,
                    changedByName   = e.ChangedByName,
                    changedDate     = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue        = e.OldValue,
                    newValue        = e.NewValue,
                    detail          = e.Detail
                }).ToList();

                return Json(new { success = true, message = "OK", data = dto },
                    JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeBomError(ex) });
            }
        }

        private class BomAuditEntry
        {
            public int Id { get; set; }
            public int BomId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  ERROR UNWRAP
        // ─────────────────────────────────────────────────────────────
        private static string DescribeBomError(Exception ex)
        {
            if (ex == null) return "Unknown error.";
            var dbValEx = ex as DbEntityValidationException;
            if (dbValEx != null)
            {
                var firstError = dbValEx.EntityValidationErrors
                    .SelectMany(e => e.ValidationErrors)
                    .FirstOrDefault();
                if (firstError != null)
                    return firstError.PropertyName + ": " + firstError.ErrorMessage;
            }
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-4  TOCTOU LOCK on the Design+Component slice. Two
        //  concurrent Save calls for the same (DesignId, CompId) pair
        //  would otherwise both pass the duplicate check, then SQL
        //  rejects the second with a constraint violation. The range
        //  UPDLOCK serialises them — the second caller blocks until
        //  the first commits, then sees the row and gets a friendly
        //  "duplicate" error.
        // ─────────────────────────────────────────────────────────────
        private void LockBomSlice(int designId, int compId)
        {
            db.Database.ExecuteSqlCommand(
                "SELECT TOP 1 1 FROM dbo.Gen_BOM WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE DesignId = @did AND CompId = @cid",
                new SqlParameter("@did", designId),
                new SqlParameter("@cid", compId));
        }
        // ============================================================
        // LIST
        // ============================================================
        [HttpGet]
        [Route("list")]
        public ActionResult List(
            int page = 1,
            int pageSize = 10,
            string search = "",
            string activeFilter = "",
            string showDeleted = "",
            int? designId = null,
            int? compId = null)
        {
            var q = db.Gen_BOM.AsQueryable();

            if (string.IsNullOrEmpty(showDeleted))
                q = q.Where(x => x.Deleted != true);

            if (activeFilter == "active")
                q = q.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                q = q.Where(x => x.Active != true);

            // Phase R.3 - filter by designId / compId
            if (designId.HasValue && designId.Value > 0)
                q = q.Where(x => x.DesignId == designId.Value);

            if (compId.HasValue && compId.Value > 0)
                q = q.Where(x => x.CompId == compId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                q =
                    from bom in q
                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    where
                        (bom.Remarks ?? "").Contains(search) ||
                        (d.CollectionName ?? "").Contains(search) ||
                        (d.D_Code ?? "").Contains(search) ||
                        (c.Name ?? "").Contains(search) ||
                        (c.Code.HasValue && c.Code.Value.ToString().Contains(search))
                    select bom;
            }

            var totalCount = q.Count();

            // Phase R.3 - summary aggregates across filtered set
            var summarySource = (
                from bom in q
                join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                from d in d1.DefaultIfEmpty()
                select new
                {
                    bom.DesignId,
                    bom.CompId,
                    bom.ReqRepeats,
                    bom.ReqHead,
                    bom.Panni,
                    bom.Solving,
                    Active = bom.Active ?? true,
                    DesignCode = d != null ? d.D_Code : ""
                }
            ).ToList();

            int activeCount = summarySource.Count(s => s.Active);
            int inactiveCount = summarySource.Count(s => !s.Active);
            int distinctDesigns = summarySource.Select(s => s.DesignId).Distinct().Count();
            int distinctComponents = summarySource.Select(s => s.CompId).Distinct().Count();
            int totalRepeats = summarySource.Sum(s => s.ReqRepeats ?? 0);
            int totalHead = summarySource.Sum(s => s.ReqHead ?? 0);
            int totalPanni = summarySource.Sum(s => s.Panni ?? 0);
            int totalSolving = summarySource.Sum(s => s.Solving ?? 0);

            var byDesign = summarySource
                .Where(s => !string.IsNullOrWhiteSpace(s.DesignCode))
                .GroupBy(s => s.DesignCode)
                .Select(g => new { design = g.Key, count = g.Count() })
                .OrderByDescending(x => x.count)
                .Take(5)
                .ToList();

            var summary = new
            {
                count = totalCount,
                activeCount,
                inactiveCount,
                distinctDesigns,
                distinctComponents,
                totalRepeats,
                totalHead,
                totalPanni,
                totalSolving,
                byDesign
            };

            var rawList =
                (
                    from bom in q

                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    orderby bom.Id descending

                    select new
                    {
                        bom.Id,
                        bom.DesignId,
                        bom.CompId,
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        bom.Remarks,
                        bom.CreatedDate,

                        Active = bom.Active ?? true,
                        Deleted = bom.Deleted ?? false,

                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",

                        ComponentCode = c != null && c.Code.HasValue
                            ? c.Code.Value.ToString()
                            : "",
                        ComponentName = c != null ? c.Name : ""
                    }
                )
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var list = rawList.Select(x => new
            {
                id = x.Id,
                designId = x.DesignId,
                compId = x.CompId,

                designCode = x.DesignCode,
                designName = x.DesignName,

                componentCode = x.ComponentCode,
                componentName = x.ComponentName,

                reqRepeats = x.ReqRepeats,
                reqHead = x.ReqHead,
                panni = x.Panni,
                solving = x.Solving,

                remarks = x.Remarks,
                active = x.Active,
                deleted = x.Deleted,

                // Sprint-1: row created within last 24h → "NEW" badge
                // on the list row so designers can spot fresh entries.
                isRecent = x.CreatedDate.HasValue
                           && (DateTime.Now - x.CreatedDate.Value).TotalHours < 24
            });

            return Json(new
            {
                success = true,
                data = new
                {
                    items = list,
                    summary,
                    page,
                    pageSize,
                    totalCount,
                    totalPages = pageSize == 0
                        ? 1
                        : (int)Math.Ceiling((double)totalCount / pageSize)
                }
            }, JsonRequestBehavior.AllowGet);
        }

        // ============================================================
        // Phase R.3 - PROFILE
        // ============================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var raw = (
                    from bom in db.Gen_BOM
                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()
                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    where bom.Id == id
                    select new
                    {
                        bom.Id,
                        bom.DesignId,
                        DesignCode = d != null ? d.D_Code : "",
                        DesignName = d != null ? d.CollectionName : "",
                        bom.CompId,
                        ComponentCode = c != null && c.Code.HasValue ? c.Code.Value.ToString() : "",
                        ComponentName = c != null ? c.Name : "",
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        bom.Remarks,
                        Active = bom.Active ?? true,
                        Deleted = bom.Deleted ?? false,
                        bom.CreatedDate,
                        bom.UpdatedDate
                    }
                ).FirstOrDefault();

                if (raw == null) return JsonError("BOM not found.");

                int repeats = raw.ReqRepeats ?? 0;
                if (repeats <= 0) repeats = 1;

                // Material BOM for this component
                var materials = (
                    from cm in db.Gen_ComponentMaterials
                    join ri in db.Gen_RawItems on cm.RawItemId equals ri.Id into ri1
                    from ri in ri1.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into u1
                    from u in u1.DefaultIfEmpty()
                    where cm.CompId == raw.CompId
                       && cm.Deleted == false
                       && cm.Active == true
                    orderby cm.Id
                    select new
                    {
                        cm.Id,
                        cm.RawItemId,
                        rawItemName = ri != null ? ri.ItemName : "-",
                        cm.QtyPerUnit,
                        unitCode = u != null ? u.UnitCode : "",
                        totalQty = cm.QtyPerUnit * repeats
                    }
                ).ToList();

                // Siblings - other BOMs for the same design
                var siblings = (
                    from bom in db.Gen_BOM
                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()
                    where bom.DesignId == raw.DesignId
                       && bom.Id != id
                       && bom.Deleted != true
                    orderby bom.Id
                    select new
                    {
                        bom.Id,
                        componentCode = c != null && c.Code.HasValue ? c.Code.Value.ToString() : "",
                        componentName = c != null ? c.Name : "",
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        Active = bom.Active ?? true
                    }
                ).ToList();

                var header = new
                {
                    id = raw.Id,
                    designId = raw.DesignId,
                    designCode = raw.DesignCode,
                    designName = raw.DesignName,
                    compId = raw.CompId,
                    componentCode = raw.ComponentCode,
                    componentName = raw.ComponentName,
                    reqRepeats = raw.ReqRepeats ?? 0,
                    reqHead = raw.ReqHead ?? 0,
                    panni = raw.Panni ?? 0,
                    solving = raw.Solving ?? 0,
                    remarks = raw.Remarks ?? "",
                    active = raw.Active,
                    createdDateText = raw.CreatedDate.HasValue ? raw.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = raw.UpdatedDate.HasValue ? raw.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                var kpis = new
                {
                    repeats = raw.ReqRepeats ?? 0,
                    head = raw.ReqHead ?? 0,
                    panni = raw.Panni ?? 0,
                    solving = raw.Solving ?? 0,
                    materialCount = materials.Count(),
                    designBomCount = siblings.Count() + 1
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    materials,
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
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var raw = (
                from bom in db.Gen_BOM

                join des in db.Gen_Design
                    on bom.DesignId equals des.Id into d1
                from des in d1.DefaultIfEmpty()

                join comp in db.Gen_Components
                    on bom.CompId equals comp.Id into c1
                from comp in c1.DefaultIfEmpty()

                join cu in db.Adm_Users
                    on bom.CreatedBy equals cu.Id into cu1
                from cu in cu1.DefaultIfEmpty()

                join uu in db.Adm_Users
                    on bom.UpdatedBy equals uu.Id into uu1
                from uu in uu1.DefaultIfEmpty()

                where bom.Id == id && (bom.Deleted == false || bom.Deleted == null)

                select new
                {
                    bom.Id,
                    bom.DesignId,
                    bom.CompId,
                    bom.ReqRepeats,
                    bom.ReqHead,
                    bom.Panni,
                    bom.Solving,
                    bom.Remarks,

                    Active = bom.Active ?? true,
                    Deleted = bom.Deleted ?? false,

                    bom.CreatedDate,
                    bom.UpdatedDate,

                    DesignName = des != null ? des.CollectionName : "",
                    DesignCode = des != null ? des.D_Code : "",

                    ComponentName = comp != null ? comp.Name : "",
                    ComponentCode = comp != null ? comp.Code.ToString() : "",

                    CreatedEmployeeId = cu != null ? (int?)cu.EmployeeId : null,
                    UpdatedEmployeeId = uu != null ? (int?)uu.EmployeeId : null
                }
            ).FirstOrDefault();

            if (raw == null)
                return JsonError("BOM not found.");

            int repeats = raw.ReqRepeats ?? 0;
            if (repeats <= 0)
                repeats = 1;

            var materials = (
                from cm in db.Gen_ComponentMaterials
                join ri in db.Gen_RawItems
                    on cm.RawItemId equals ri.Id into ri1
                from ri in ri1.DefaultIfEmpty()

                join u in db.Gen_Units
                    on cm.UnitId equals u.Id into u1
                from u in u1.DefaultIfEmpty()

                where cm.CompId == raw.CompId
                      && cm.Deleted == false
                      && cm.Active == true

                select new BomMaterialViewModel
                {
                    RawItemId = cm.RawItemId,
                    RawItemName = ri != null ? ri.ItemName : "",
                    QtyPerUnit = cm.QtyPerUnit,
                    UnitCode = u != null ? u.UnitCode : "",
                    TotalQty = cm.QtyPerUnit * repeats
                }
            ).ToList();

            decimal materialCost = 0m;
            decimal laborCost = 0m;
            decimal machineCost = 0m;

            // optional future costing logic can go here
            decimal totalCost = materialCost + laborCost + machineCost;

            var dto = new BomDetailViewModel
            {
                Id = raw.Id,

                DesignId = raw.DesignId,
                DesignName = raw.DesignName ?? "",
                DesignCode = raw.DesignCode ?? "",

                CompId = raw.CompId,
                ComponentName = raw.ComponentName ?? "",
                ComponentCode = raw.ComponentCode ?? "",

                ReqRepeats = raw.ReqRepeats ?? 0,
                ReqHead = raw.ReqHead ?? 0,
                Panni = raw.Panni ?? 0,
                Solving = raw.Solving ?? 0,

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
                    : "",

                Materials = materials,

                MaterialCost = materialCost,
                LaborCost = laborCost,
                MachineCost = machineCost,
                TotalCost = totalCost
            };

            return JsonSuccess("BOM detail loaded successfully.", dto);
        }

        // ============================================================
        // SAVE
        // ============================================================
        [HttpPost]
        [Route("save")]
        public ActionResult Save(BomFormViewModel vm)
        {
            if (vm == null)
                return Json(new { success = false, message = "Invalid payload" });

            if (vm.DesignId <= 0 || vm.CompId <= 0)
                return Json(new { success = false, message = "Design and Component are required" });

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    // Sprint-4: serialise on the (DesignId, CompId) slice so
                    // two concurrent Save calls can't both pass the
                    // duplicate check and then crash on the unique
                    // constraint.
                    LockBomSlice(vm.DesignId, vm.CompId);

                    var userId = GetCurrentUserId();
                    var now = DateTime.Now;

                    var component = db.Gen_Components
                        .FirstOrDefault(x => x.Id == vm.CompId && x.Deleted != true);

                    if (component == null)
                        return Json(new { success = false, message = "Invalid component" });

                    // Sprint-4: validate component is active before
                    // letting a BOM reference it — prevents orphaned BOM
                    // rows pointing at deactivated components.
                    if (component.Active != true)
                        return Json(new { success = false, message = "Component is inactive and cannot be added to a BOM. Activate the component first." });

                    Gen_BOM entity;
                    bool isCreate;
                    string oldSnapshot = null;

                    if (vm.Id <= 0)
                    {
                        isCreate = true;
                        var exists = db.Gen_BOM.Any(x =>
                            x.Deleted != true &&
                            x.DesignId == vm.DesignId &&
                            x.CompId == vm.CompId);

                        if (exists)
                            return Json(new { success = false, message = "Duplicate BOM entry" });

                        entity = new Gen_BOM
                        {
                            DesignId = vm.DesignId,
                            CompId = vm.CompId,
                            ReqRepeats = vm.ReqRepeats,
                            ReqHead = vm.ReqHead,
                            Panni = vm.Panni,
                            Solving = vm.Solving,
                            Remarks = vm.Remarks,
                            Active = vm.Active,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now
                        };

                        db.Gen_BOM.Add(entity);
                    }
                    else
                    {
                        isCreate = false;
                        entity = db.Gen_BOM
                            .FirstOrDefault(x => x.Id == vm.Id && x.Deleted != true);

                        if (entity == null)
                            return Json(new { success = false, message = "Record not found" });

                        oldSnapshot = "Rpt " + entity.ReqRepeats + " / Head " + entity.ReqHead
                                    + " / Panni " + entity.Panni + " / Solv " + entity.Solving
                                    + " / Active " + (entity.Active == true ? "Y" : "N");

                        var duplicateExists = db.Gen_BOM.Any(x =>
                            x.Id != vm.Id &&
                            x.Deleted != true &&
                            x.DesignId == vm.DesignId &&
                            x.CompId == vm.CompId);

                        if (duplicateExists)
                            return Json(new { success = false, message = "Duplicate BOM entry" });

                        entity.DesignId = vm.DesignId;
                        entity.CompId = vm.CompId;
                        entity.ReqRepeats = vm.ReqRepeats;
                        entity.ReqHead = vm.ReqHead;
                        entity.Panni = vm.Panni;
                        entity.Solving = vm.Solving;
                        entity.Remarks = vm.Remarks;
                        entity.Active = vm.Active;
                        entity.UpdatedBy = userId;
                        entity.UpdatedDate = now;
                    }

                    db.SaveChanges();
                    tx.Commit();

                    string newSnapshot = "Rpt " + entity.ReqRepeats + " / Head " + entity.ReqHead
                                       + " / Panni " + entity.Panni + " / Solv " + entity.Solving
                                       + " / Active " + (entity.Active == true ? "Y" : "N");
                    LogBomAudit(entity.Id, isCreate ? "CREATED" : "EDITED",
                        oldValue: oldSnapshot,
                        newValue: newSnapshot,
                        detail: "Design #" + vm.DesignId + ", Component #" + vm.CompId);

                    return Json(new
                    {
                        success = true,
                        data = new { id = entity.Id }
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { success = false, message = "Save failed. " + DescribeBomError(ex) });
                }
            }
        }

        // ============================================================
        // DELETE (SOFT)
        // ============================================================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var entity = db.Gen_BOM.FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (entity == null)
                    return Json(new { success = false, message = "Record not found" });

                entity.Deleted = true;
                entity.Active = false;
                entity.UpdatedBy = GetCurrentUserId();
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                LogBomAudit(id, "DELETED",
                    oldValue: BomStatus.ACTIVE,
                    newValue: BomStatus.DELETED,
                    detail: "Design #" + entity.DesignId + ", Component #" + entity.CompId);

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Delete failed. " + DescribeBomError(ex) });
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  BULK ACTIONS
        //  Each endpoint accepts { ids: int[] } and returns the standard
        //  envelope: { successCount, failureCount, failures }.
        // ─────────────────────────────────────────────────────────────
        public class BomBulkIdsDto { public List<int> Ids { get; set; } }

        private class BomBulkFailure
        {
            public int id;
            public string code;
            public string reason;
        }

        private object DoBulkBom(List<int> ids, Func<int, Gen_BOM, string> runOne)
        {
            var failures = new List<BomBulkFailure>();
            int ok = 0;

            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var entity = db.Gen_BOM.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (entity == null)
                {
                    failures.Add(new BomBulkFailure { id = id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = "BOM#" + id;
                try
                {
                    string err = runOne(id, entity);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new BomBulkFailure { id = id, code = code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new BomBulkFailure { id = id, code = code, reason = DescribeBomError(ex) });
                }
            }

            return new
            {
                successCount = ok,
                failureCount = failures.Count,
                failures = failures.Select(f => new { id = f.id, code = f.code, reason = f.reason })
            };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(BomBulkIdsDto dto)
        {
            var data = DoBulkBom(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogBomAudit(id, "ACTIVATED",
                    oldValue: BomStatus.INACTIVE, newValue: BomStatus.ACTIVE);
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(BomBulkIdsDto dto)
        {
            var data = DoBulkBom(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogBomAudit(id, "DEACTIVATED",
                    oldValue: BomStatus.ACTIVE, newValue: BomStatus.INACTIVE);
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(BomBulkIdsDto dto)
        {
            var data = DoBulkBom(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogBomAudit(id, "DELETED",
                    oldValue: BomStatus.ACTIVE, newValue: BomStatus.DELETED);
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }

        // ─────────────────────────────────────────────────────────────
        //  Sprint-3  CLONE BOM FROM EXISTING DESIGN
        //  Copies all live BOM rows from sourceDesignId to
        //  targetDesignId. Skips (per-line failure report) any
        //  component pair that already exists on the target so the
        //  user can re-run safely after a partial first pass.
        //  Use cases: a new season's design that shares 90 % of its
        //  components with last season — copy then tweak the diff.
        // ─────────────────────────────────────────────────────────────
        public class BomCloneDto
        {
            public int SourceDesignId { get; set; }
            public int TargetDesignId { get; set; }
        }

        [HttpPost, Route("clone-from-design")]
        public ActionResult CloneFromDesign(BomCloneDto dto)
        {
            if (dto == null || dto.SourceDesignId <= 0 || dto.TargetDesignId <= 0)
                return Json(new { success = false, message = "Source and target design ids are required." });
            if (dto.SourceDesignId == dto.TargetDesignId)
                return Json(new { success = false, message = "Source and target must differ." });

            using (var tx = db.Database.BeginTransaction())
            {
                try
                {
                    var sourceRows = db.Gen_BOM
                        .Where(b => b.DesignId == dto.SourceDesignId && b.Deleted != true)
                        .ToList();
                    if (!sourceRows.Any())
                        return Json(new { success = false, message = "Source design has no BOM rows." });

                    var existingCompIds = db.Gen_BOM
                        .Where(b => b.DesignId == dto.TargetDesignId && b.Deleted != true)
                        .Select(b => b.CompId)
                        .ToList();

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.Now;
                    var failures = new List<object>();
                    int ok = 0;

                    foreach (var src in sourceRows)
                    {
                        if (existingCompIds.Contains(src.CompId))
                        {
                            failures.Add(new
                            {
                                id = src.Id,
                                code = "Component #" + src.CompId,
                                reason = "Target design already has this component."
                            });
                            continue;
                        }

                        var clone = new Gen_BOM
                        {
                            DesignId = dto.TargetDesignId,
                            CompId = src.CompId,
                            ReqRepeats = src.ReqRepeats,
                            ReqHead = src.ReqHead,
                            Panni = src.Panni,
                            Solving = src.Solving,
                            Remarks = string.IsNullOrWhiteSpace(src.Remarks)
                                      ? ("Cloned from design #" + dto.SourceDesignId)
                                      : (src.Remarks + " | Cloned from design #" + dto.SourceDesignId),
                            Active = src.Active,
                            Deleted = false,
                            CreatedBy = userId,
                            CreatedDate = now
                        };
                        db.Gen_BOM.Add(clone);
                        db.SaveChanges();

                        LogBomAudit(clone.Id, "CLONED",
                            oldValue: null,
                            newValue: BomStatus.ACTIVE,
                            detail: "Cloned from BOM #" + src.Id + " (design " + dto.SourceDesignId + " → " + dto.TargetDesignId + ")");
                        ok++;
                    }

                    tx.Commit();

                    return Json(new
                    {
                        success = true,
                        message = ok + " BOM row(s) cloned, " + failures.Count + " skipped.",
                        data = new
                        {
                            sourceDesignId = dto.SourceDesignId,
                            targetDesignId = dto.TargetDesignId,
                            cloned = ok,
                            skipped = failures.Count,
                            failures
                        }
                    });
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    return Json(new { success = false, message = "Clone failed. " + DescribeBomError(ex) });
                }
            }
        }

        // ============================================================
        // COMPONENT DROPDOWN
        // ============================================================
        [HttpGet]
        [Route("components")]
        public ActionResult Components()
        {
            var items = db.Gen_Components
                .Where(x => x.Deleted != true && x.Active == true)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name
                })
                .ToList();

            return Json(new { success = true, data = items },
                JsonRequestBehavior.AllowGet);
        }

        // ============================================================
        // EXPORT EXCEL
        // ============================================================
        [HttpGet]
        [Route("export-excel")]
        public ActionResult ExportExcel(string search = "", string activeFilter = "", string showDeleted = "")
        {
            var q = db.Gen_BOM.AsQueryable();

            if (string.IsNullOrEmpty(showDeleted))
                q = q.Where(x => x.Deleted != true);

            if (activeFilter == "active")
                q = q.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                q = q.Where(x => x.Active != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                q =
                    from bom in q
                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    where
                        (bom.Remarks ?? "").Contains(search) ||
                        (d.CollectionName ?? "").Contains(search) ||
                        (d.D_Code ?? "").Contains(search) ||
                        (c.Name ?? "").Contains(search)
                    select bom;
            }

            var list =
                (
                    from bom in q

                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    orderby bom.Id descending

                    select new
                    {
                        bom.Id,
                        Design = d == null ? "" : (d.D_Code ?? "") + " - " + (d.CollectionName ?? ""),
                        Component = c == null ? "" : ((c.Code.HasValue ? c.Code.Value.ToString() : "") + " - " + (c.Name ?? "")),
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        Remarks = bom.Remarks ?? "",
                        Status = (bom.Active ?? true) ? "Active" : "Inactive"
                    }
                ).ToList();

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("BOM");

                string[] headers = { "ID", "Design", "Component", "Repeats", "Head", "Panni", "Solving", "Remarks", "Status" };

                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = headers[c];
                    ws.Cells[1, c + 1].Style.Font.Bold = true;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    int rIndex = i + 2;

                    ws.Cells[rIndex, 1].Value = b.Id;
                    ws.Cells[rIndex, 2].Value = b.Design;
                    ws.Cells[rIndex, 3].Value = b.Component;
                    ws.Cells[rIndex, 4].Value = b.ReqRepeats;
                    ws.Cells[rIndex, 5].Value = b.ReqHead;
                    ws.Cells[rIndex, 6].Value = b.Panni;
                    ws.Cells[rIndex, 7].Value = b.Solving;
                    ws.Cells[rIndex, 8].Value = b.Remarks;
                    ws.Cells[rIndex, 9].Value = b.Status;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(
                    pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "BOM.xlsx"
                );
            }
        }

        // ============================================================
        // EXPORT PDF
        // ============================================================
        [HttpGet]
        [Route("export-pdf")]
        public ActionResult ExportPdf(string search = "", string activeFilter = "", string showDeleted = "")
        {
            var q = db.Gen_BOM.AsQueryable();

            if (string.IsNullOrEmpty(showDeleted))
                q = q.Where(x => x.Deleted != true);

            if (activeFilter == "active")
                q = q.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                q = q.Where(x => x.Active != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                q =
                    from bom in q
                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    where
                        (bom.Remarks ?? "").Contains(search) ||
                        (d.CollectionName ?? "").Contains(search) ||
                        (d.D_Code ?? "").Contains(search) ||
                        (c.Name ?? "").Contains(search)
                    select bom;
            }

            var list =
                (
                    from bom in q

                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    orderby bom.Id descending

                    select new
                    {
                        bom.Id,
                        Design = d == null ? "" : (d.D_Code ?? "") + " - " + (d.CollectionName ?? ""),
                        Component = c == null ? "" : ((c.Code.HasValue ? c.Code.Value.ToString() : "") + " - " + (c.Name ?? "")),
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        Remarks = bom.Remarks ?? "",
                        Status = (bom.Active ?? true) ? "Active" : "Inactive"
                    }
                ).ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var table = new PdfPTable(9) { WidthPercentage = 100 };

                string[] headers = { "ID", "Design", "Component", "Repeats", "Head", "Panni", "Solving", "Remarks", "Status" };

                foreach (var h in headers)
                {
                    table.AddCell(new PdfPCell(new Phrase(h))
                    {
                        BackgroundColor = BaseColor.LIGHT_GRAY
                    });
                }

                foreach (var b in list)
                {
                    table.AddCell(b.Id.ToString());
                    table.AddCell(b.Design);
                    table.AddCell(b.Component);
                    table.AddCell((b.ReqRepeats ?? 0).ToString());
                    table.AddCell((b.ReqHead ?? 0).ToString());
                    table.AddCell((b.Panni ?? 0).ToString());
                    table.AddCell((b.Solving ?? 0).ToString());
                    table.AddCell(b.Remarks);
                    table.AddCell(b.Status);
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", "BOM.pdf");
            }
        }

        // ============================================================
        // PRINT
        // ============================================================
        [HttpGet]
        [Route("print")]
        public ActionResult Print(string search = "", string activeFilter = "", string showDeleted = "")
        {
            var q = db.Gen_BOM.AsQueryable();

            if (string.IsNullOrEmpty(showDeleted))
                q = q.Where(x => x.Deleted != true);

            if (activeFilter == "active")
                q = q.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                q = q.Where(x => x.Active != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                q =
                    from bom in q
                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    where
                        (bom.Remarks ?? "").Contains(search) ||
                        (d.CollectionName ?? "").Contains(search) ||
                        (d.D_Code ?? "").Contains(search) ||
                        (c.Name ?? "").Contains(search)
                    select bom;
            }

            var list =
                (
                    from bom in q

                    join d in db.Gen_Design on bom.DesignId equals d.Id into d1
                    from d in d1.DefaultIfEmpty()

                    join c in db.Gen_Components on bom.CompId equals c.Id into c1
                    from c in c1.DefaultIfEmpty()

                    orderby bom.Id descending

                    select new
                    {
                        bom.Id,
                        Design = d == null ? "" : (d.D_Code ?? "") + " - " + (d.CollectionName ?? ""),
                        Component = c == null ? "" : ((c.Code.HasValue ? c.Code.Value.ToString() : "") + " - " + (c.Name ?? "")),
                        bom.ReqRepeats,
                        bom.ReqHead,
                        bom.Panni,
                        bom.Solving,
                        Remarks = bom.Remarks ?? "",
                        Status = (bom.Active ?? true) ? "Active" : "Inactive"
                    }
                ).ToList();

            var html = @"<!doctype html>
<html>
<head>
<meta charset='utf-8'/>
<title>BOM Print</title>
<style>
@page { size: A4 landscape; margin: 10mm; }
body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; color: #212529; margin: 14mm; }
h3 { text-align: center; margin: 0 0 10px; }
table { width: 100%; border-collapse: collapse; }
thead { background: #0d6efd; color: #fff; }
th, td { border: 1px solid #ccc; padding: 5px 7px; vertical-align: top; font-size: 10pt; }
th { text-align: center; }
tbody tr:nth-child(even) { background: #f6f8fb; }
.footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
</style>
</head>
<body>
<h3>Bill of Materials (BOM)</h3>
<table>
<thead>
<tr>
<th>ID</th>
<th>Design</th>
<th>Component</th>
<th>Repeats</th>
<th>Head</th>
<th>Panni</th>
<th>Solving</th>
<th>Remarks</th>
<th>Status</th>
</tr>
</thead>
<tbody>";

            foreach (var b in list)
            {
                html += "<tr>"
                    + "<td>" + b.Id + "</td>"
                    + "<td>" + System.Net.WebUtility.HtmlEncode(b.Design) + "</td>"
                    + "<td>" + System.Net.WebUtility.HtmlEncode(b.Component) + "</td>"
                    + "<td style='text-align:right;'>" + (b.ReqRepeats ?? 0) + "</td>"
                    + "<td style='text-align:right;'>" + (b.ReqHead ?? 0) + "</td>"
                    + "<td style='text-align:right;'>" + (b.Panni ?? 0) + "</td>"
                    + "<td style='text-align:right;'>" + (b.Solving ?? 0) + "</td>"
                    + "<td>" + System.Net.WebUtility.HtmlEncode(b.Remarks) + "</td>"
                    + "<td>" + System.Net.WebUtility.HtmlEncode(b.Status) + "</td>"
                    + "</tr>";
            }

            html += "</tbody></table>";
            html += "<div class='footer'>Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                 + " | Rows: " + list.Count + "</div>";
            // Auto-trigger the browser print dialog as soon as the document
            // finishes loading. Without this, the user has to press Ctrl+P
            // manually after the table renders.
            html += "<script>window.addEventListener('load', function(){ setTimeout(function(){ window.print(); }, 100); });</script>";
            html += "</body></html>";

            return Content(html, "text/html");
        }
    }
}