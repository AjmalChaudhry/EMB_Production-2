using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web.Mvc;
using PagedList;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using OfficeOpenXml;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace EMB_Production.Controllers
{

    [RoutePrefix("emb-products/components")]
    public class Gen_ComponentsController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Components)
        // ─────────────────────────────────────────────────────────────
        private void LogCompAudit(int id, string action,
                                  string oldValue = null, string newValue = null,
                                  string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Gen_ComponentsAuditLog
       (ComponentId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
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
                var entries = db.Database.SqlQuery<CompAuditEntry>(@"
SELECT  a.Id, a.ComponentId AS CompId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Gen_ComponentsAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.ComponentId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, componentId = e.CompId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();

                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeCompError(ex) });
            }
        }

        private class CompAuditEntry
        {
            public int Id { get; set; }
            public int CompId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeCompError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Components)
        // ─────────────────────────────────────────────────────────────
        public class CompBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkComp(List<int> ids, Func<int, Gen_Components, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Gen_Components.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = "CMP-" + (e.Code?.ToString("D4") ?? id.ToString());
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeCompError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(CompBulkIdsDto dto)
        {
            var data = DoBulkComp(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCompAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(CompBulkIdsDto dto)
        {
            var data = DoBulkComp(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCompAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(CompBulkIdsDto dto)
        {
            var data = DoBulkComp(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                e.UpdatedBy = GetCurrentUserId();
                e.UpdatedDate = DateTime.Now;
                db.SaveChanges();
                LogCompAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // =====================================================
        // LIST
        // =====================================================
        [HttpGet, Route("")]
        public ActionResult GetAll(
            string search = "",
            int page = 1,
            int pageSize = 10,
            int? designId = null,
            int? branchId = null,
            string activeFilter = "")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;

            var query = db.Gen_Components
                .Where(x => x.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(x => x.Name.Contains(search));
            }

            // Phase R.1 - additional filters
            if (designId.HasValue && designId.Value > 0)
                query = query.Where(x => x.D_Code == designId.Value);
            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(x => x.BranchId == branchId.Value);
            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            var total = query.Count();

            // Phase R.1 - summary aggregates over the filtered set
            var summaryEntities = query.Select(x => new { x.Id, x.BranchId, DesignId = x.D_Code, Active = x.Active ?? false }).ToList();
            int activeCount = summaryEntities.Count(s => s.Active);
            int inactiveCount = summaryEntities.Count(s => !s.Active);
            var byBranch = summaryEntities
                .Where(s => s.BranchId.HasValue)
                .GroupBy(s => s.BranchId.Value)
                .ToList();
            var byBranchList = (from g in byBranch
                                join b in db.Branches on g.Key equals b.Id into bj
                                from b in bj.DefaultIfEmpty()
                                select new { branch = b != null ? b.Name : "-", count = g.Count() })
                                .OrderByDescending(x => x.count)
                                .ToList();
            var byDesign = summaryEntities
                .Where(s => s.DesignId.HasValue)
                .GroupBy(s => s.DesignId.Value)
                .ToList();
            var byDesignList = (from g in byDesign
                                join d in db.Gen_Design on g.Key equals d.Id into dj
                                from d in dj.DefaultIfEmpty()
                                select new { design = d != null ? d.D_Code : "-", count = g.Count() })
                                .OrderByDescending(x => x.count)
                                .Take(8)
                                .ToList();

            var summary = new
            {
                count = total,
                activeCount,
                inactiveCount,
                byBranch = byBranchList,
                byDesign = byDesignList
            };

            // Logical component headers
            var headers = query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    id = x.Id,
                    code = x.Code,
                    name = x.Name,
                    dCode = x.D_Code,
                    designDisplay = x.Gen_Design != null
                        ? x.Gen_Design.D_Code + " - " + x.Gen_Design.CollectionName
                        : "",
                    branchId = x.BranchId,
                    branchName = x.Branches != null ? x.Branches.Name : "",
                    active = x.Active ?? false
                })
                .ToList();

            // Pull the latest PRODUCTION spec for the visible page
            var pageIds = headers.Select(h => h.id).ToList();
            var specs = db.Gen_ComponentSpecs
                .AsNoTracking()
                .Where(s => pageIds.Contains(s.ComponentId)
                         && s.Variant == "PRODUCTION"
                         && s.IsLatest
                         && !s.Deleted)
                .ToList();

            var data = headers.Select(h =>
            {
                var s = specs.FirstOrDefault(z => z.ComponentId == h.id);
                return new
                {
                    h.id, h.code, h.name, h.dCode, h.designDisplay,
                    headToHead = s?.HeadToHead ?? 0,
                    stitch = s?.Stitch ?? 0,
                    tilla = s?.Tilla ?? 0,
                    seqA = s?.Seq_A ?? 0,
                    seqB = s?.Seq_B ?? 0,
                    width = s?.Width,
                    height = s?.Height,
                    colors = s?.Colors,
                    machineHead = s?.MachineHead,
                    repeatVal = s?.Repeat,
                    hSpace = s?.HSpace,
                    ups = s?.UPS,
                    h.branchId, h.branchName, h.active
                };
            }).ToList();

            return Json(new
            {
                success = true,
                data,
                total,
                summary
            }, JsonRequestBehavior.AllowGet);
        }

        // =====================================================
        // Phase R.1 - PROFILE
        // =====================================================
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var c = db.Gen_Components
                    .Include("Gen_Design")
                    .Include("Branches")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (c == null) return JsonError("Component not found.");

                var spec = db.Gen_ComponentSpecs
                    .AsNoTracking()
                    .Where(s => s.ComponentId == id
                             && s.Variant == "PRODUCTION"
                             && s.IsLatest
                             && !s.Deleted)
                    .FirstOrDefault();

                // Materials linked to this component
                var materialEntities = (
                    from cm in db.Gen_ComponentMaterials
                    join r in db.Gen_RawItems on cm.RawItemId equals r.Id into rj
                    from r in rj.DefaultIfEmpty()
                    join u in db.Gen_Units on cm.UnitId equals u.Id into uj
                    from u in uj.DefaultIfEmpty()
                    where cm.CompId == id && cm.Deleted == false
                    orderby cm.Id
                    select new
                    {
                        cm.Id,
                        cm.RawItemId,
                        RawItemName = r != null ? r.ItemName : "-",
                        cm.QtyPerUnit,
                        UnitCode = u != null ? u.UnitCode : "",
                        cm.Remarks,
                        cm.Active
                    }
                ).ToList();
                var materials = materialEntities.Select(m => new
                {
                    id = m.Id,
                    rawItemId = m.RawItemId,
                    rawItemName = m.RawItemName,
                    qty = m.QtyPerUnit,
                    unit = m.UnitCode,
                    remarks = m.Remarks,
                    active = m.Active
                }).ToList();

                // WorkOrderItems referencing this component (recent 15)
                var workOrderItems = db.WorkOrderItems
                    .Include("WorkOrders")
                    .Where(wi => wi.CompId == id && wi.Deleted != true)
                    .OrderByDescending(wi => wi.Id)
                    .Take(15)
                    .ToList()
                    .Select(wi => new
                    {
                        id = wi.Id,
                        workOrderCode = wi.WorkOrders != null ? wi.WorkOrders.WorkOrderCode : ("WO-" + wi.WorkOrderId),
                        quantity = wi.Quantity,
                        status = wi.Status,
                        completedQty = wi.CompletedQty,
                        rejectedQty = wi.RejectedQty
                    })
                    .ToList();

                var header = new
                {
                    id = c.Id,
                    code = c.Code == null ? "" : c.Code.ToString(),
                    name = c.Name,
                    designId = c.D_Code,
                    designCode = c.Gen_Design != null ? c.Gen_Design.D_Code : "",
                    designName = c.Gen_Design != null ? c.Gen_Design.CollectionName : "",
                    branchId = c.BranchId,
                    branchName = c.Branches != null ? c.Branches.Name : "",
                    active = c.Active ?? true
                };

                var spec_kpis = new
                {
                    headToHead = spec?.HeadToHead ?? 0,
                    stitch = spec?.Stitch ?? 0,
                    tilla = spec?.Tilla ?? 0,
                    seqA = spec?.Seq_A ?? 0,
                    seqB = spec?.Seq_B ?? 0,
                    width = spec?.Width,
                    height = spec?.Height,
                    colors = spec?.Colors,
                    machineHead = spec?.MachineHead,
                    repeatVal = spec?.Repeat,
                    hSpace = spec?.HSpace,
                    ups = spec?.UPS,
                    materialCount = materials.Count(),
                    workOrderItemCount = workOrderItems.Count()
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis = spec_kpis,
                    materials,
                    workOrderItems
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        // =====================================================
        // DETAIL
        // =====================================================
        [HttpPost, Route("detail")]
        public ActionResult Detail(ComponentIdDto dto)
        {
            var header = db.Gen_Components
                .Where(c => c.Id == dto.Id && c.Deleted != true)
                .Select(c => new
                {
                    id = c.Id,
                    code = c.Code,
                    name = c.Name,
                    designId = c.D_Code,
                    designDisplay = c.Gen_Design != null
                        ? c.Gen_Design.D_Code + " - " + c.Gen_Design.CollectionName
                        : "",
                    branchId = c.BranchId,
                    branchName = c.Branches != null ? c.Branches.Name : "",
                    active = c.Active ?? false,
                    deleted = c.Deleted ?? false,
                    createdDate = c.CreatedDate,
                    updatedDate = c.UpdatedDate,
                    createdByEmployeeId = c.Adm_Users != null ? c.Adm_Users.EmployeeId : null,
                    updatedByEmployeeId = c.Adm_Users1 != null ? c.Adm_Users1.EmployeeId : null
                })
                .FirstOrDefault();

            if (header == null)
                return JsonError("Component not found.");

            var s = db.Gen_ComponentSpecs
                .AsNoTracking()
                .Where(z => z.ComponentId == header.id
                         && z.Variant == "PRODUCTION"
                         && z.IsLatest
                         && !z.Deleted)
                .FirstOrDefault();

            var data = new
            {
                header.id, header.code, header.name,
                header.designId, header.designDisplay,

                headToHead = s?.HeadToHead ?? 0,
                stitch = s?.Stitch ?? 0,
                tilla = s?.Tilla ?? 0,
                seqA = s?.Seq_A ?? 0,
                seqB = s?.Seq_B ?? 0,

                width = s?.Width,
                height = s?.Height,
                colors = s?.Colors,
                machineHead = s?.MachineHead,
                repeatVal = s?.Repeat,
                hSpace = s?.HSpace,
                ups = s?.UPS,

                header.branchId, header.branchName,
                header.active, header.deleted,
                header.createdDate, header.updatedDate,
                header.createdByEmployeeId, header.updatedByEmployeeId
            };

            return Json(new
            {
                success = true,
                data
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, Route("create")]
        public ActionResult Create(ComponentCreateUpdateDto dto)
        {
            if (dto == null)
                return JsonError("Invalid request.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return JsonError("Component name is required.");

            if (dto.DesignId <= 0)
                return JsonError("Design selection is required.");

            if (dto.BranchId <= 0)
                return JsonError("Branch selection is required.");

            int userId = GetCurrentUserId();
            var now = DateTime.Now;

            // Phase 3.2: Gen_Components now holds only logical fields.
            // Specs (HeadToHead, Stitch, Width, Height, ...) live in Gen_ComponentSpecs.
            var entity = new Gen_Components
            {
                Code = GenerateComponentCode(),      // AUTO
                Name = dto.Name.Trim(),
                D_Code = dto.DesignId,

                BranchId = dto.BranchId,
                Active = dto.Active,
                Deleted = false,

                CreatedBy = userId,
                CreatedDate = now
            };

            db.Gen_Components.Add(entity);
            db.SaveChanges();

            // Phase 3: also create the V1 PRODUCTION spec row (canonical storage)
            var spec = new Gen_ComponentSpecs
            {
                ComponentId = entity.Id,
                Version = 1,
                Variant = "PRODUCTION",
                IsLatest = true,
                Width = dto.Width,
                Height = dto.Height,
                Stitch = dto.Stitch,
                Colors = dto.Colors,
                MachineHead = dto.MachineHead,
                Repeat = dto.Repeat,
                HSpace = dto.HSpace,
                UPS = dto.UPS,
                HeadToHead = dto.HeadToHead,
                Tilla = dto.Tilla,
                Seq_A = dto.SeqA,
                Seq_B = dto.SeqB,
                CreatedBy = userId,
                CreatedDate = now,
                Deleted = false
            };
            db.Gen_ComponentSpecs.Add(spec);
            db.SaveChanges();

            return JsonSuccess("Component created successfully.", new { id = entity.Id });
        }

        [HttpPost, Route("update")]
        public ActionResult Update(ComponentCreateUpdateDto dto)
        {
            if (dto == null || dto.Id <= 0)
                return JsonError("Invalid request.");

            var x = db.Gen_Components
                .FirstOrDefault(c => c.Id == dto.Id && c.Deleted != true);

            if (x == null)
                return JsonError("Component not found.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return JsonError("Component name is required.");

            if (dto.DesignId <= 0)
                return JsonError("Design selection is required.");

            if (dto.BranchId <= 0)
                return JsonError("Branch selection is required.");

            int userId = GetCurrentUserId();
            var now = DateTime.Now;

            // Phase 3.2: only logical fields are written to Gen_Components.
            // Spec changes (HeadToHead, Stitch, etc.) go to Gen_ComponentSpecs below.
            x.Name = dto.Name.Trim();
            x.D_Code = dto.DesignId;
            x.BranchId = dto.BranchId;
            x.Active = dto.Active;
            x.UpdatedBy = userId;
            x.UpdatedDate = now;

            db.SaveChanges();

            // Phase 3: mutate the CURRENT PRODUCTION spec row (no new version unless user
            // explicitly clicks "Save as New Version" on the profile page).
            var latestProd = db.Gen_ComponentSpecs
                .Where(s => s.ComponentId == x.Id
                         && s.Variant == "PRODUCTION"
                         && s.IsLatest
                         && !s.Deleted)
                .FirstOrDefault();

            if (latestProd == null)
            {
                // No spec yet — first save creates V1
                latestProd = new Gen_ComponentSpecs
                {
                    ComponentId = x.Id,
                    Version = 1,
                    Variant = "PRODUCTION",
                    IsLatest = true,
                    CreatedBy = userId,
                    CreatedDate = now,
                    Deleted = false
                };
                db.Gen_ComponentSpecs.Add(latestProd);
            }

            latestProd.Width = dto.Width;
            latestProd.Height = dto.Height;
            latestProd.Stitch = dto.Stitch;
            latestProd.Colors = dto.Colors;
            latestProd.MachineHead = dto.MachineHead;
            latestProd.Repeat = dto.Repeat;
            latestProd.HSpace = dto.HSpace;
            latestProd.UPS = dto.UPS;
            latestProd.HeadToHead = dto.HeadToHead;
            latestProd.Tilla = dto.Tilla;
            latestProd.Seq_A = dto.SeqA;
            latestProd.Seq_B = dto.SeqB;

            db.SaveChanges();

            return JsonSuccess("Component updated successfully.");
        }


        // =====================================================
        // CODE GENERATOR
        // =====================================================
        private int GenerateComponentCode()
        {
            return (db.Gen_Components.Max(x => (int?)x.Code) ?? 0) + 1;
        }
    
        // ===================== DELETE (SOFT) =====================
        [HttpDelete]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var x = db.Gen_Components.Find(id);
                if (x == null)
                    return JsonError("Component not found");

                x.Deleted = true;
                x.UpdatedBy = GetCurrentUserId();
                x.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Component deleted successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete component", ex);
            }
        }

        private static readonly string[] _allowedLogoExts = { ".jpg", ".jpeg", ".png", ".gif" };
        private string GetLogoFolder() => Server.MapPath("~/CompanyLogo"); // adjust if different



        public ActionResult Index(
             string search = null,
             string activeFilter = null,
             int? selectedDesignId = null,
             bool showCreate = false,
             bool showEdit = false,
             int? editId = null,
             int page = 1,
             int pageSize = 10)
        {
            // ================= BRANCH DROPDOWN =================
            var branchItems = db.Branches
                .OrderBy(b => b.Name)
                .Select(b => new
                {
                    BranchId = b.Id,        // ✅ FIX
            BranchName = b.Name     // ✅ FIX
        })
                .ToList();

            // ================= QUERY =================
            var q = db.Gen_Components
                .Where(c => c.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(c => c.Name.Contains(search));

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool wantActive = activeFilter.Equals("true", StringComparison.OrdinalIgnoreCase);
                q = q.Where(c => (c.Active ?? false) == wantActive);
            }

            if (selectedDesignId.HasValue)
                q = q.Where(c => c.D_Code == selectedDesignId.Value);

            // ================= PAGED DATA =================
            var paged = q
                .OrderBy(c => c.Name)
                .Select(c => new ComponentsRowViewModel
                {
                    Id = c.Id,

                    Code = c.Code,
                    D_Code = c.D_Code,

                    Name = c.Name,

                    // Phase 3.2: spec columns moved to Gen_ComponentSpecs.
                    // The legacy MVC Index view no longer surfaces these values;
                    // use the Angular Profile page for spec data.

                    Active = c.Active ?? false,
                    Deleted = c.Deleted ?? false,

                    BranchId = c.BranchId,

                    CreatedBy = c.CreatedBy,
                    CreatedDate = c.CreatedDate,
                    UpdatedBy = c.UpdatedBy,
                    UpdatedDate = c.UpdatedDate
                })
                .ToPagedList(page, pageSize);

            // ================= VIEWMODEL =================
            var vm = new ComponentsPageViewModel
            {
                Components = paged,

                NewComponent = showCreate
                    ? new ComponentsRowViewModel()
                    : null,

                EditComponent = (showEdit && editId.HasValue)
                    ? paged.FirstOrDefault(r => r.Id == editId.Value)
                    : null,

                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,

                Search = search,
                ActiveFilter = activeFilter,
                SelectedDesignId = selectedDesignId,

                BranchOptions = new SelectList(
                    branchItems,
                    "BranchId",
                    "BranchName"
                )
            };

            return View(vm);
        }


        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(ComponentsPageViewModel vm)
        {
            vm.ShowCreate = true; // keep form open
            if (!ModelState.IsValid)
            {
                TempData["CreateErrors"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return RedirectToAction("Index", new { showCreate = true });
            }

            int userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var now = DateTime.UtcNow;

            var comp = new Gen_Components
            {
                Code = vm.NewComponent.Code,
                D_Code = vm.NewComponent.D_Code,
                Name = vm.NewComponent.Name,
                BranchId = vm.NewComponent.BranchId,
                Active = true,
                Deleted = false,
                CreatedBy = userId,
                CreatedDate = now,
                UpdatedBy = userId,
                UpdatedDate = now
            };
            db.Gen_Components.Add(comp);
            db.SaveChanges();

            // Phase 3.2: also create the V1 PRODUCTION spec row.
            db.Gen_ComponentSpecs.Add(new Gen_ComponentSpecs
            {
                ComponentId = comp.Id,
                Version = 1,
                Variant = "PRODUCTION",
                IsLatest = true,
                HeadToHead = vm.NewComponent.HeadToHead,
                Stitch = vm.NewComponent.Stitch,
                Tilla = vm.NewComponent.Tilla,
                Seq_A = vm.NewComponent.Seq_A,
                Seq_B = vm.NewComponent.Seq_B,
                CreatedBy = userId,
                CreatedDate = now,
                Deleted = false
            });
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(ComponentsPageViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["EditErrors"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return RedirectToAction("Index", new { showEdit = true, editId = vm.EditComponent.Id });
            }

            var comp = db.Gen_Components.Find(vm.EditComponent.Id);
            if (comp == null) return HttpNotFound();

            int userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var now = DateTime.UtcNow;

            // map updated fields (Phase 3.2: spec columns moved to Gen_ComponentSpecs)
            comp.Code = vm.EditComponent.Code;
            comp.D_Code = vm.EditComponent.D_Code;
            comp.Name = vm.EditComponent.Name;
            comp.BranchId = vm.EditComponent.BranchId;
            comp.Active = vm.EditComponent.Active;
            comp.Deleted = vm.EditComponent.Deleted;
            comp.UpdatedBy = userId;
            comp.UpdatedDate = now;

            db.Entry(comp).State = EntityState.Modified;
            db.SaveChanges();

            // Phase 3.2: mutate the latest PRODUCTION spec for this component.
            var latest = db.Gen_ComponentSpecs.FirstOrDefault(s =>
                s.ComponentId == comp.Id && s.Variant == "PRODUCTION" && s.IsLatest && !s.Deleted);
            if (latest == null)
            {
                latest = new Gen_ComponentSpecs
                {
                    ComponentId = comp.Id,
                    Version = 1,
                    Variant = "PRODUCTION",
                    IsLatest = true,
                    CreatedBy = userId,
                    CreatedDate = now,
                    Deleted = false
                };
                db.Gen_ComponentSpecs.Add(latest);
            }
            latest.HeadToHead = vm.EditComponent.HeadToHead;
            latest.Stitch = vm.EditComponent.Stitch;
            latest.Tilla = vm.EditComponent.Tilla;
            latest.Seq_A = vm.EditComponent.Seq_A;
            latest.Seq_B = vm.EditComponent.Seq_B;
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        // Explicit whitelist of client-editable business fields for the inline
        // "update-field" grid endpoint. Anything not listed here — including
        // Id, Deleted, BranchId and the audit columns — can never be set
        // through this reflection-based endpoint.
        private static readonly HashSet<string> ComponentEditableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code",
            "D_Code",
            "Name",
            "Active"
        };

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateComponentField(int componentId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            var comp = db.Gen_Components.Find(componentId);
            if (comp == null)
                return Json(new { success = false, message = "Component not found." });

            if (string.IsNullOrWhiteSpace(fieldName) || !ComponentEditableFields.Contains(fieldName))
                return Json(new { success = false, message = "Field is not allowed to be updated." });

            var prop = typeof(Gen_Components).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                object converted;
                var t = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                if (t == typeof(string))
                {
                    converted = newValue;
                }
                else if (t == typeof(int))
                {
                    converted = int.TryParse(newValue, out var i) ? i : (int?)null;
                }
                else if (t == typeof(decimal))
                {
                    converted = decimal.TryParse(newValue, out var d) ? d : (decimal?)null;
                }
                else if (t == typeof(bool))
                {
                    converted = (newValue ?? "").ToLowerInvariant() == "true";
                }
                else
                {
                    // you can add more types here
                    return Json(new { success = false, message = $"Unsupported type {t.Name}" });
                }

                prop.SetValue(comp, converted);
                comp.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                comp.UpdatedDate = DateTime.UtcNow;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Gen_Components/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var component = db.Gen_Components
                              .Include(c => c.Branches)
                              .Include(c => c.Gen_Design)                             
                              .FirstOrDefault(c => c.Id == id.Value);

            if (component == null)
                return HttpNotFound();

            return View(component);
        }
        // GET: /Gen_Components/Delete/5
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var comp = db.Gen_Components.Find(id.Value);
            if (comp == null)
                return HttpNotFound();

            return View(comp);
        }

        // POST: /Gen_Components/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var comp = db.Gen_Components.Find(id);
            if (comp == null)
                return HttpNotFound();

            comp.Deleted = true;
            comp.Active = false;
            comp.UpdatedBy = GetCurrentUserId();
            comp.UpdatedDate = DateTime.Now;

            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        [HttpGet, Obsolete]
        public ActionResult ExportDetailToExcel(int id)
        {
            var comp = db.Gen_Components
                         .Include(c => c.Branches)
                         .Include(c => c.Gen_Design)
                         .FirstOrDefault(c => c.Id == id);

            if (comp == null) return HttpNotFound();

            // Get JobOrders through JobOrderItems
            var jobOrders = db.Gen_JobOrderItems
                              .Where(x => x.CompId == comp.Id)
                              .Select(x => x.Gen_JobOrders)
                              .Distinct()
                              .ToList();

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("ComponentDetail");

                int row = 1;

                void Add(string field, object value)
                {
                    ws.Cells[row, 1].Value = field;
                    ws.Cells[row, 2].Value = value ?? "(none)";
                    row++;
                }

                ws.Cells[row, 1].Value = "Field";
                ws.Cells[row, 2].Value = "Value";
                ws.Cells[row, 1, row, 2].Style.Font.Bold = true;
                row++;

                // Phase 3.2: specs come from Gen_ComponentSpecs (latest PRODUCTION)
                var specForXls = db.Gen_ComponentSpecs.FirstOrDefault(s =>
                    s.ComponentId == comp.Id && s.Variant == "PRODUCTION" && s.IsLatest && !s.Deleted);

                Add("Code", comp.Code);
                Add("D_Code", comp.D_Code);
                Add("Name", comp.Name);
                Add("HeadToHead", specForXls?.HeadToHead);
                Add("Stitch", specForXls?.Stitch);
                Add("Tilla", specForXls?.Tilla);
                Add("Seq A", specForXls?.Seq_A);
                Add("Seq B", specForXls?.Seq_B);
                Add("Branch", comp.Branches?.Name);
                Add("Design", comp.Gen_Design?.CollectionName);
                Add("Active", comp.Active.GetValueOrDefault() ? "Yes" : "No");
                Add("Deleted", comp.Deleted.GetValueOrDefault() ? "Yes" : "No");
                Add("Created By", comp.CreatedBy);
                Add("Created Date", comp.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                Add("Updated By", comp.UpdatedBy);
                Add("Updated Date", comp.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                // Job Orders Section
                ws.Cells[++row, 1].Value = "Job Orders";
                ws.Cells[row, 1, row, 2].Merge = true;
                ws.Cells[row, 1].Style.Font.Bold = true;
                row++;

                if (jobOrders.Any())
                {
                    ws.Cells[row, 1].Value = "ID";
                    ws.Cells[row, 2].Value = "Code";
                    ws.Cells[row, 1, row, 2].Style.Font.Bold = true;
                    row++;

                    foreach (var job in jobOrders.OrderBy(j => j.Id))
                    {
                        ws.Cells[row, 1].Value = job.Id;
                        ws.Cells[row, 2].Value = job.JobOrderCode;
                        row++;
                    }
                }
                else
                {
                    ws.Cells[row, 1].Value = "(no job orders)";
                    ws.Cells[row, 1, row, 2].Merge = true;
                    ws.Cells[row, 1].Style.Font.Italic = true;
                    row++;
                }

                // Thread Usage Section
                ws.Cells[++row, 1].Value = "Thread Usage";
                ws.Cells[row, 1, row, 2].Merge = true;
                ws.Cells[row, 1].Style.Font.Bold = true;
                row++;

                // Thread usage block removed — Gen_ThreadUsage was retired
                // in favor of Gen_ComponentMaterials. If this Excel export
                // ever needs a material breakdown again, source it from
                // comp.Gen_ComponentMaterials (with Gen_RawItems for name).
                ws.Cells[row, 1].Value = "(thread usage section removed — see Gen_ComponentMaterials)";
                ws.Cells[row, 1, row, 2].Merge = true;
                ws.Cells[row, 1].Style.Font.Italic = true;
                row++;

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Component_{comp.Id}_Detail.xlsx");
            }
        }
        // ---------------- LIST EXPORTS ----------------
        // Excel: real .xlsx via EPPlus
        [HttpGet]
        [Route("export-excel")]
        public ActionResult ExportListToExcel(string search = null, string activeFilter = null)
        {
            var query = db.Gen_Components
                          .Include(c => c.Branches)
                          .Include(c => c.Gen_Design)
                          .Where(c => c.Deleted == false || c.Deleted == null);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search)
                                      || (c.Code != null && c.Code.ToString().Contains(search)));

            if (activeFilter == "active")
                query = query.Where(c => c.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(c => c.Active == false);

            var rows = query.OrderByDescending(c => c.Id).ToList();

            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Components");

                var headers = new[] { "Code", "Name", "Design", "Branch", "Active" };
                for (int i = 0; i < headers.Length; i++)
                {
                    ws.Cells[1, i + 1].Value = headers[i];
                    ws.Cells[1, i + 1].Style.Font.Bold = true;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    var c = rows[i];
                    int row = i + 2;
                    ws.Cells[row, 1].Value = c.Code.HasValue ? "CMP-" + c.Code.Value.ToString("D4") : "";
                    ws.Cells[row, 2].Value = c.Name ?? "";
                    ws.Cells[row, 3].Value = c.Gen_Design != null
                        ? ((c.Gen_Design.D_Code ?? "") + " - " + (c.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "";
                    ws.Cells[row, 4].Value = c.Branches != null ? c.Branches.Name : "";
                    ws.Cells[row, 5].Value = (c.Active ?? true) ? "Yes" : "No";
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Components.xlsx");
            }
        }

        // PDF: real application/pdf via iTextSharp
        [HttpGet]
        [Route("export-pdf")]
        public ActionResult ExportListToPdf(string search = null, string activeFilter = null)
        {
            var query = db.Gen_Components
                          .Include(c => c.Branches)
                          .Include(c => c.Gen_Design)
                          .Where(c => c.Deleted == false || c.Deleted == null);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Name.Contains(search)
                                      || (c.Code != null && c.Code.ToString().Contains(search)));

            if (activeFilter == "active")
                query = query.Where(c => c.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(c => c.Active == false);

            var rows = query.OrderByDescending(c => c.Id).ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25f, 25f, 30f, 30f);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var titleFont  = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 14, BaseColor.BLACK);
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 9,  BaseColor.WHITE);
                var bodyFont   = FontFactory.GetFont(FontFactory.HELVETICA, 8.5f,    BaseColor.BLACK);

                doc.Add(new Paragraph("Components — List", titleFont) { Alignment = Element.ALIGN_CENTER });
                doc.Add(new Paragraph(" "));

                var table = new PdfPTable(5) { WidthPercentage = 100f };
                table.SetWidths(new float[] { 1.0f, 2.0f, 2.4f, 1.6f, 0.8f });

                void AddHeader(string text)
                {
                    table.AddCell(new PdfPCell(new Phrase(text, headerFont))
                    {
                        BackgroundColor = new BaseColor(13, 110, 253),
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 5f
                    });
                }

                AddHeader("Code");
                AddHeader("Name");
                AddHeader("Design");
                AddHeader("Branch");
                AddHeader("Active");

                foreach (var c in rows)
                {
                    string code   = c.Code.HasValue ? "CMP-" + c.Code.Value.ToString("D4") : "-";
                    string design = c.Gen_Design != null
                        ? ((c.Gen_Design.D_Code ?? "") + " - " + (c.Gen_Design.DNo ?? "")).Trim(' ', '-')
                        : "-";
                    string branch = c.Branches != null ? c.Branches.Name : "-";

                    table.AddCell(new PdfPCell(new Phrase(code,        bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(c.Name ?? "", bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(design,      bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(branch,      bodyFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase((c.Active ?? true) ? "Yes" : "No", bodyFont))
                        { Padding = 4f, HorizontalAlignment = Element.ALIGN_CENTER });
                }

                doc.Add(table);

                var footer = new Paragraph(
                    $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm} | Rows: {rows.Count}",
                    FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 8, BaseColor.GRAY));
                footer.Alignment = Element.ALIGN_RIGHT;
                footer.SpacingBefore = 10f;
                doc.Add(footer);

                doc.Close();
                return File(ms.ToArray(), "application/pdf", "Components.pdf");
            }
        }

        [HttpGet]
        [Route("export-detail-pdf/{id:int}")]
        public ActionResult ExportDetailToPdf(int id)
        {
            var italicFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10, BaseColor.GRAY);

            var comp = db.Gen_Components
                         .Include(c => c.Branches)
                         .Include(c => c.Gen_Design)
                         .FirstOrDefault(c => c.Id == id);

            if (comp == null) return HttpNotFound();

            var jobOrders = db.Gen_JobOrderItems
                              .Where(x => x.CompId == comp.Id)
                              .Select(x => x.Gen_JobOrders)
                              .Distinct()
                              .ToList();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var table = new PdfPTable(2) { WidthPercentage = 90 };
                table.DefaultCell.Padding = 5;

                Font headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11);
                Font labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                Font valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 10);

                void Row(string label, object value)
                {
                    table.AddCell(new PdfPCell(new Phrase(label, labelFont)));
                    table.AddCell(new PdfPCell(new Phrase(value?.ToString() ?? "(none)", valueFont)));
                }

                table.AddCell(new PdfPCell(new Phrase("Component Detail", headerFont))
                {
                    Colspan = 2,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 6
                });

                // Phase 3.2: specs come from Gen_ComponentSpecs (latest PRODUCTION)
                var specForPdf = db.Gen_ComponentSpecs.FirstOrDefault(s =>
                    s.ComponentId == comp.Id && s.Variant == "PRODUCTION" && s.IsLatest && !s.Deleted);

                Row("Code", comp.Code);
                Row("D_Code", comp.D_Code);
                Row("Name", comp.Name);
                Row("HeadToHead", specForPdf?.HeadToHead);
                Row("Stitch", specForPdf?.Stitch);
                Row("Tilla", specForPdf?.Tilla);
                Row("Seq A", specForPdf?.Seq_A);
                Row("Seq B", specForPdf?.Seq_B);
                Row("Branch", comp.Branches?.Name);
                Row("Design", comp.Gen_Design?.CollectionName);
                Row("Active", comp.Active.GetValueOrDefault() ? "Yes" : "No");
                Row("Deleted", comp.Deleted.GetValueOrDefault() ? "Yes" : "No");
                Row("Created By", comp.CreatedBy);
                Row("Created Date", comp.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                Row("Updated By", comp.UpdatedBy);
                Row("Updated Date", comp.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                // Job Orders
                table.AddCell(new PdfPCell(new Phrase("Job Orders", headerFont))
                {
                    Colspan = 2,
                    BackgroundColor = BaseColor.LIGHT_GRAY
                });

                if (jobOrders.Any())
                {
                    foreach (var job in jobOrders.OrderBy(j => j.Id))
                    {
                        Row("ID", job.Id);
                        Row("Code", job.JobOrderCode);
                    }
                }
                else
                {
                    table.AddCell(new PdfPCell(new Phrase("(no job orders)", italicFont)) { Colspan = 2 });
                }

                // Thread Usage
                table.AddCell(new PdfPCell(new Phrase("Thread Usage", headerFont))
                {
                    Colspan = 2,
                    BackgroundColor = BaseColor.LIGHT_GRAY
                });

                // Thread usage section removed — Gen_ThreadUsage was
                // retired in favor of Gen_ComponentMaterials. Future
                // material breakdown should source from
                // comp.Gen_ComponentMaterials.
                table.AddCell(new PdfPCell(new Phrase("(thread usage section removed — see Gen_ComponentMaterials)", italicFont)) { Colspan = 2 });

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"Component_{comp.Id}_Detail.pdf");
            }
        }
    }
}
