using System;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using OfficeOpenXml;
using PagedList;
using Rotativa;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Data.Entity.Infrastructure;
using System.Diagnostics;
using EMB_Production_2.Models;

namespace EMB_Production.Controllers
    {
        [RoutePrefix("emb-products/branches")]
        public class Adm_BranchController : BaseController   
        {
            private const int PageSize = 10;

        [HttpGet, Route("list")]
        public ActionResult ApiList(string search = "", int page = 1, int pageSize = 10)
        {
            try
            {
                page = page < 1 ? 1 : page;
                pageSize = pageSize < 1 ? 10 : pageSize;

                var query =
                    from b in db.Branches.AsNoTracking()
                    join c in db.Adm_CompanyProfile
                        on b.CompanyId equals c.CompanyID into cj
                    from c in cj.DefaultIfEmpty()
                    where b.Deleted != true
                    select new { b, c };

                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim().ToLower();

                    query = query.Where(x =>
                        (x.b.Name ?? "").ToLower().Contains(s) ||
                        (x.b.Location ?? "").ToLower().Contains(s) ||
                        (x.b.Contect ?? "").ToLower().Contains(s) ||
                        (x.c != null && (x.c.CompanyName ?? "").ToLower().Contains(s))
                    );
                }

                int totalItems = query.Count();

                var items = query
                    .OrderBy(x => x.b.BranchId ?? x.b.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new
                    {
                        id = x.b.Id,
                        branchId = x.b.BranchId,
                        name = x.b.Name,
                        location = x.b.Location,
                        contect = x.b.Contect,

                        companyId = x.b.CompanyId,
                        companyName = x.c != null ? x.c.CompanyName : "",

                        isActive = x.b.IsActive ?? false,
                        deleted = x.b.Deleted ?? false
                    })
                    .ToList();

                return JsonSuccess("Branch list loaded.", new
                {
                    items,
                    totalItems
                });
            }
            catch
            {
                return JsonError("Error loading branches.");
            }
        }

        [HttpGet, Route("detail/{id:int}")]
        public ActionResult ApiDetail(int id)
        {
            var row =
                (from b in db.Branches
                 join c in db.Adm_CompanyProfile
                     on b.CompanyId equals c.CompanyID into cj
                 from c in cj.DefaultIfEmpty()
                 where (b.Id == id || b.BranchId == id)
                 select new
                 {
                     b.Id,
                     b.BranchId,
                     b.Name,
                     b.Location,
                     b.Contect,

                     b.CompanyId,
                     CompanyName = c != null ? c.CompanyName : "",

                     IsActive = b.IsActive ?? false,
                     Deleted = b.Deleted ?? false,

                     b.CreatedBy,
                     b.CreatedDate,
                     b.UpdatedBy,
                     b.UpdatedDate
                 })
                .FirstOrDefault();

            if (row == null)
                return JsonError("Branch detail not found.");

            return JsonSuccess("Branch detail loaded.", new
            {
                id = row.Id,
                branchId = row.BranchId,
                name = row.Name,
                location = row.Location,
                contect = row.Contect,

                companyId = row.CompanyId,
                companyName = row.CompanyName,

                isActive = row.IsActive,
                deleted = row.Deleted,

                createdByEmployeeId = row.CreatedBy,
                updatedByEmployeeId = row.UpdatedBy,

                createdDateText = row.CreatedDate.HasValue
                    ? row.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                    : null,

                updatedDateText = row.UpdatedDate.HasValue
                    ? row.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm")
                    : null
            });
        }

        // =====================================================
        // CREATE
        // =====================================================
        [HttpPost, Route("create")]
            public ActionResult ApiCreate(BranchForm form)
            {
                try
                {
                    if (form == null)
                        return JsonError("Invalid branch data.");

                    if (string.IsNullOrWhiteSpace(form.Name) || form.CompanyId <= 0)
                        return JsonError("Branch name and company are required.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.UtcNow;

                    var entity = new Branches
                    {
                        BranchId = form.BranchId,
                        Name = form.Name.Trim(),
                        Location = form.Location?.Trim(),
                        Contect = form.Contect?.Trim(),
                        CompanyId = form.CompanyId,
                        IsActive = form.IsActive,
                        Deleted = false,

                        CreatedBy = userId,
                        CreatedDate = now,
                        UpdatedBy = userId,
                        UpdatedDate = now
                    };

                    db.Branches.Add(entity);
                    db.SaveChanges();

                    return JsonSuccess("Branch created.", new
                    {
                        branchId = entity.BranchId
                    });
                }
                catch (Exception ex)
                {
                    return JsonError("Error creating branch.", ex);
                }
            }

            // =====================================================
            // UPDATE
            // =====================================================
            [HttpPost, Route("update/{branchId:int}")]
            public ActionResult ApiUpdate(int branchId, BranchForm form)
            {
                try
                {
                    var entity = db.Branches
                        .FirstOrDefault(b => b.BranchId == branchId && b.Deleted != true);

                    if (entity == null)
                        return JsonError("Branch not found.");

                    if (string.IsNullOrWhiteSpace(form.Name) || form.CompanyId <= 0)
                        return JsonError("Branch name and company are required.");

                    int userId = GetCurrentUserId();
                    DateTime now = DateTime.UtcNow;

                    entity.BranchId = form.BranchId;
                    entity.Name = form.Name.Trim();
                    entity.Location = form.Location?.Trim();
                    entity.Contect = form.Contect?.Trim();
                    entity.CompanyId = form.CompanyId;
                    entity.IsActive = form.IsActive;

                    entity.UpdatedBy = userId;
                    entity.UpdatedDate = now;

                    db.SaveChanges();

                    return JsonSuccess("Branch updated.");
                }
                catch (Exception ex)
                {
                    return JsonError("Error updating branch.", ex);
                }
            }

            // =====================================================
            // DELETE (Soft Delete)
            // =====================================================
            [HttpPost, Route("delete")]
            public ActionResult ApiDelete(int id)
            {
                try
                {
                    var entity = db.Branches.FirstOrDefault(b => b.BranchId == id);
                    if (entity == null)
                        return JsonError("Branch not found.");

                    entity.Deleted = true;
                    entity.UpdatedBy = GetCurrentUserId();
                    entity.UpdatedDate = DateTime.UtcNow;

                    db.SaveChanges();

                    return JsonSuccess("Branch deleted.");
                }
                catch (Exception ex)
                {
                    return JsonError("Error deleting branch.", ex);
                }
            }

            // =====================================================
            // COMPANY DROPDOWN
            // =====================================================
            [HttpGet, Route("companies")]
            public ActionResult ApiCompanies()
        {
            var list = db.Adm_CompanyProfile
                .AsNoTracking()
                .OrderBy(c => c.CompanyName)
                .Select(c => new
                {
                    companyId = c.CompanyID,
                    companyName = c.CompanyName
                })
                .ToList();

            return JsonSuccess("Companies loaded.", list);
        }
    

            public ActionResult Index(string search = null,string activeFilter = null,int? companyFilter = null,
                 bool showCreate = false,bool showEdit = false,int? editId = null,int page = 1,int pageSize = 20)
            {

                var vm = BuildViewModel(search: search,activeFilter: activeFilter,companyFilter: companyFilter,
                    page: page,pageSize: pageSize,showCreate: showCreate,showEdit: showEdit,editId: editId);

                if (Request.IsAjaxRequest())
                {                
                    if (showCreate || showEdit)
                        return PartialView("Index", vm); 
               
                    return PartialView("_BranchesGrid", vm);
                }
                return View(vm);
            }

            // POST: inline Create
            [HttpPost, ValidateAntiForgeryToken]
            public ActionResult CreateFromIndex([Bind(Prefix = "NewBranch")] Branches newBranch,string search = null,string activeFilter = null,
                int? companyFilter = null,int page = 1,int pageSize = 20)
            {
                // Safety
                if (newBranch == null)
                    ModelState.AddModelError("", "No branch data was posted.");

                if (!ModelState.IsValid)
                {
                   var vm = BuildViewModel(search: search,activeFilter: activeFilter,companyFilter: companyFilter,
                                    page: page, pageSize: pageSize, showCreate: true);
                    // keep user-entered values so the form re-renders with them
                    vm.NewBranch = newBranch ?? new Branches();  // keeps the selected BranchId
                    return Request.IsAjaxRequest() ? (ActionResult)PartialView("Index", vm): View("Index", vm);
                }
                var now = DateTime.UtcNow;
                int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

                newBranch.CreatedBy = uid;
                newBranch.CreatedDate = now;
                newBranch.UpdatedBy = uid;
                newBranch.UpdatedDate = now;
                newBranch.Deleted = false;

                db.Branches.Add(newBranch);
                db.SaveChanges();

                return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
            }

            // POST: inline Edit
            [HttpPost, ValidateAntiForgeryToken]
            public ActionResult EditFromIndex([Bind(Prefix = "EditBranch")] Branches editBranch,string search = null,string activeFilter = null,
                int? companyFilter = null,int page = 1,int pageSize = 20)
            {
                if (editBranch == null)
                    ModelState.AddModelError("", "No branch data was posted.");

                if (!ModelState.IsValid)
                {
                   var vm = BuildViewModel(search: search,activeFilter: activeFilter,companyFilter: companyFilter,
                                          page: page,pageSize: pageSize,showEdit: true,editId: editBranch?.Id );
                    vm.EditBranch = editBranch ?? new Branches(); // keeps the selected BranchId
                    return Request.IsAjaxRequest() ? (ActionResult)PartialView("Index", vm) : View("Index", vm);
                }
                var u = db.Branches.Find(editBranch.Id);
                if (u == null) return HttpNotFound();
                // map editable fields
                u.Name = editBranch.Name;
                u.Location = editBranch.Location;
                u.Contect = editBranch.Contect;
                u.BranchId = editBranch.BranchId;
                u.CompanyId = editBranch.CompanyId;
                u.IsActive = editBranch.IsActive;
                u.Deleted = editBranch.Deleted;

                u.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                u.UpdatedDate = DateTime.Now;

                db.Entry(u).State = EntityState.Modified;
                db.SaveChanges();

                return RedirectToAction("Index", new { search, activeFilter, companyFilter, page, pageSize });
            }

            // POST: inline JSON‐edit single field
            [HttpPost]
            [Route("update-field")]
            public JsonResult UpdateBranchField(int id, string fieldName, string newValue)
            {
                if (Session["UserID"] == null)
                    return Json(new { success = false, message = "Not authenticated." });

                var b = db.Branches.Find(id);
                if (b == null)
                    return Json(new { success = false, message = "Branch not found." });

                var prop = typeof(Branches).GetProperty(fieldName);
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
                        converted = (newValue ?? "").Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (tgt == typeof(DateTime))
                        converted = DateTime.TryParse(newValue, out var dt) ? (DateTime?)dt : null;
                    else
                        return Json(new { success = false, message = $"Unsupported type {tgt.Name}" });

                    prop.SetValue(b, converted);
                    b.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                    b.UpdatedDate = DateTime.UtcNow;
                    db.SaveChanges();
                    return Json(new { success = true });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

        // GET: Details
        [HttpGet]
        [Route("details-from-index/{id:int}")]
        public ActionResult DetailsFromIndex( int id, string search = null,string activeFilter = null,
                                            int? companyFilter = null, int page = 1, int pageSize = 20)
        {
            var branch = db.Branches
                .FirstOrDefault(b =>
                    b.Id == id &&
                    (b.Deleted == false || b.Deleted == null));

            if (branch == null)
                return HttpNotFound();

            // 🔹 Load company name safely (NO navigation property)
            string companyName = db.Adm_CompanyProfile
                .Where(c => c.CompanyID == branch.CompanyId)
                .Select(c => c.CompanyName)
                .FirstOrDefault();

            ViewBag.CompanyName = companyName ?? "";

            return PartialView("_BranchesDetail", branch);
        }


        // GET: AJAX delete modal
        [HttpGet]
        [Route("delete-modal")]
            public ActionResult Delete(int? id, string search = null, string activeFilter = null, int? companyFilter = null, int page = 1)
            {
                if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

                var branch = db.Branches
                                .FirstOrDefault(b => b.Id == id.Value);

                if (branch == null) return HttpNotFound();
           
                ViewBag.Search = search;
                ViewBag.ActiveFilter = activeFilter;
                ViewBag.CompanyFilter = companyFilter;
                ViewBag.Page = page;

                return PartialView("_BranchesDeleteModal", branch);
            }

            // POST: AJAX delete      
            [HttpPost, ActionName("Delete")]
            [ValidateAntiForgeryToken]
            public ActionResult DeleteConfirmed(int id,string search = null,string activeFilter = null,int? companyFilter = null,int page = 1)
            {
            var branch = db.Branches.Find(id);
            if (branch == null)
            {
                Response.TrySkipIisCustomErrors = true;
                Response.StatusCode = 404;
                return Content("Branch not found.", "text/plain");
            }

            try
            {
                // Try HARD delete
                db.Branches.Remove(branch);
                db.SaveChanges();
            }
            catch (DbUpdateException)
            {
                // FK conflict -> SOFT DELETE fallback
                var entry = db.Entry(branch);

                // If EF marked it Deleted from Remove(), reset it so we can modify
                if (entry.State == EntityState.Deleted)
                    entry.State = EntityState.Unchanged;

                // Optional: refresh from DB in case of concurrency
                try { entry.Reload(); } catch { /* ignore */ }

                if (branch.Deleted != true) // works for bool and bool?
                {
                    branch.Deleted = true;
                    entry.State = EntityState.Modified;
                    db.SaveChanges();
                }

                var vmSoft = BuildViewModel(search, activeFilter, companyFilter, page);
                return Request.IsAjaxRequest()
                    ? (ActionResult)PartialView("_BranchesGrid", vmSoft)
                    : RedirectToAction("Index", new { search, activeFilter, companyFilter, page });
            }
            catch
            {
                Response.TrySkipIisCustomErrors = true;
                Response.StatusCode = 500;
                return Content("Unexpected error while deleting.", "text/plain");
            }

            // HARD delete succeeded
            var updatedVm = BuildViewModel(search, activeFilter, companyFilter, page);
            return Request.IsAjaxRequest()
                ? (ActionResult)PartialView("_BranchesGrid", updatedVm)
                : RedirectToAction("Index", new { search, activeFilter, companyFilter, page });
            }

            private BranchesPageViewModel BuildViewModel(
                string search, string activeFilter, int? companyFilter, int? page,
                int pageSize = 20, bool showCreate = false, bool showEdit = false, int? editId = null)
            {
                int pageNumber = page ?? 1;

                // Load entity for edit (no tracking = lighter)
                Branches editEntity = null;
                if (showEdit && editId.HasValue)
                {
                    editEntity = db.Branches
                        .AsNoTracking()
                        .FirstOrDefault(b => b.Id == editId.Value && b.Deleted != true);
                }

                // Selected BranchId when editing (use the entity we just loaded)
                int? selectedBranchId = showEdit ? editEntity?.BranchId : null;

                // Branch dropdown items (Value must be string)
                var branchListItems = db.Branches
                    .AsNoTracking()
                    .Where(b => b.Deleted != true)
                    .OrderBy(b => b.Name)
                    .Select(b => new SelectListItem
                    {
                        Value = b.BranchId.HasValue ? b.BranchId.Value.ToString() : "",
                        Text = b.Name
                    })
                    .ToList();

                // Company dropdown items
                var companyItems = db.Adm_CompanyProfile
                    .AsNoTracking()
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new { Id = c.CompanyID, Name = c.CompanyName })
                    .ToList();

                // Grid query (mirror filters used elsewhere)
                var q = db.Branches
                    .AsNoTracking()
                   
                    .Where(b => b.Deleted != true);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    q = q.Where(b =>
                        b.Name.Contains(s) ||
                        b.Location.Contains(s) ||
                        b.Contect.Contains(s));

                    // Optional: numeric search matches Id/BranchId too
                    int n;
                    if (int.TryParse(s, out n))
                    {
                        q = q.Where(b => b.Id == n || b.BranchId == n);
                    }
                }

                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    bool want = activeFilter.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                    q = q.Where(b => (b.IsActive ?? false) == want);
                }

                if (companyFilter.HasValue)
                    q = q.Where(b => b.CompanyId == companyFilter.Value);

                var rows = q
                    .OrderBy(b => b.Name)
                    .ToList()
                    .Select(b => new BranchRowViewModel
                    {
                        Id = b.Id,
                        Name = b.Name,
                        BranchCode = b.BranchId, // BranchRowViewModel.BranchCode is int?
                        Location = b.Location,
                        Contect = b.Contect,
                        
                        Active = b.IsActive ?? false,
                        Deleted = b.Deleted ?? false,
                        CreatedDate = b.CreatedDate,
                        UpdatedDate = b.UpdatedDate
                    })
                    .ToPagedList(pageNumber, pageSize);

                return new BranchesPageViewModel
                {
                    Branches = rows,
                    ShowCreate = showCreate,
                    ShowEdit = showEdit,
                    EditId = editId,
                    Search = search,
                    ActiveFilter = activeFilter,
                    CompanyFilter = companyFilter,

                    // Dropdowns
                    BranchId = selectedBranchId,
                    BranchList = branchListItems,                                   // IEnumerable<SelectListItem>
                    CompanyList = new SelectList(companyItems, "Id", "Name", editEntity?.CompanyId),

                    // Forms (Create/Edit)
                    NewBranch = showCreate ? new Branches { IsActive = true, Deleted = false } : null,
                    EditBranch = editEntity
                };
            }


            // GET: Export to Excel
            [HttpGet, Obsolete]
            public ActionResult ExportToExcel(int? id, string search = null, string activeFilter = null, int? companyFilter = null)
            {                
                if (id.HasValue)
                {
                    // single‐record
                    var b = db.Branches
                              
                              .FirstOrDefault(x => x.Id == id.Value);
                    if (b == null) return HttpNotFound();

                    using (var pkg = new ExcelPackage())
                    {
                        var ws = pkg.Workbook.Worksheets.Add("BranchDetail");
                        ws.Cells[1, 1].Value = "Field"; ws.Cells[1, 2].Value = "Value";
                        using (var hdr = ws.Cells[1, 1, 1, 2]) hdr.Style.Font.Bold = true;
                        int row = 1;
                        void Add(string fld, object val)
                        {
                            row++;
                            ws.Cells[row, 1].Value = fld;
                            ws.Cells[row, 2].Value = val ?? "(none)";
                        }
                        Add("ID", b.Id);
                        Add("Name", b.Name);
                        Add("Code", b.BranchId);
                        Add("Location", b.Location);
                       
                        Add("Active", b.IsActive == true ? "Yes" : "No");
                        Add("Deleted", b.Deleted == true ? "Yes" : "No");
                        Add("Created", b.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                        Add("Updated", b.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                        ws.Cells[ws.Dimension.Address].AutoFitColumns();
                        return File(pkg.GetAsByteArray(),
                                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                    $"Branch_{b.Id}_Detail.xlsx");
                    }
                }
                else
                {
                    // list export
                    var q = db.Branches.AsQueryable();
                    if (!string.IsNullOrWhiteSpace(search))
                        q = q.Where(b => b.Name.Contains(search));
                    if (!string.IsNullOrWhiteSpace(activeFilter))
                    {
                        bool want = activeFilter == "Yes";
                        q = q.Where(b => b.IsActive == want);
                    }
                    if (companyFilter.HasValue)
                        q = q.Where(b => b.CompanyId == companyFilter.Value);

                    var list = q.OrderBy(b => b.Name).ToList();

                    using (var pkg = new ExcelPackage())
                    {
                        var ws = pkg.Workbook.Worksheets.Add("Branches");
                        var headers = new[] { "ID", "Name", "Code", "Location", "Company", "Active", "Deleted" };
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
                            ws.Cells[r, 2].Value = x.Name;
                            ws.Cells[r, 3].Value = x.BranchId;
                            ws.Cells[r, 4].Value = x.Location;
                            
                            ws.Cells[r, 6].Value = x.IsActive == true ? "Yes" : "No";
                            ws.Cells[r, 7].Value = x.Deleted == true ? "Yes" : "No";
                        }
                        ws.Cells[ws.Dimension.Address].AutoFitColumns();
                        return File(pkg.GetAsByteArray(),
                                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                    "Branches.xlsx");
                    }
                }
            }

            // GET: Export to PDF
            [HttpGet]
            [Route("exporttopdf")]
            public ActionResult ExportToPdf(int? id, string search = null, string activeFilter = null, int? companyFilter = null)
            {
                if (id.HasValue)
                {
                    // Single-record PDF
                    var b = db.Branches
                                       .FirstOrDefault(x => x.BranchId == id.Value);
                    if (b == null) return HttpNotFound();

                    using (var ms = new MemoryStream())
                    {
                        var doc = new iTextSharp.text.Document(iTextSharp.text.PageSize.A4, 25, 25, 30, 30);
                        PdfWriter.GetInstance(doc, ms);
                        doc.Open();

                        var tbl = new PdfPTable(2) { WidthPercentage = 80f };

                        Action<string, bool> Cell = (text, isHeader) =>
                        {
                            var cell = new PdfPCell(new Phrase(text ?? string.Empty))
                            {
                                BackgroundColor = isHeader ? BaseColor.LIGHT_GRAY : BaseColor.WHITE
                            };
                            tbl.AddCell(cell);
                        };

                        Action<string, object> Row = (label, val) =>
                        {
                            Cell(label, false);
                            Cell(val == null ? "(none)" : val.ToString(), false);
                        };

                        // Header
                        Cell("Field", true);
                        Cell("Value", true);

                        // Data rows
                        Row("ID", b.Id);
                        Row("BranchId", b.BranchId);
                        Row("Name", b.Name);
                        Row("Location", b.Location);
                       
                        Row("Active", (b.IsActive ?? false) ? "Yes" : "No");
                        Row("Deleted", (b.Deleted ?? false) ? "Yes" : "No");
                        Row("Created", b.CreatedDate.HasValue ? b.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : null);
                        Row("Updated", b.UpdatedDate.HasValue ? b.UpdatedDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : null);

                        doc.Add(tbl);
                        doc.Close();

                        return File(ms.ToArray(), "application/pdf", $"Branch_{b.BranchId}_Detail.pdf");
                    }
                }

                // Full-list PDF
                var q = db.Branches
                                   .Where(x => x.Deleted != true);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.Trim();
                    q = q.Where(b =>
                        b.Name.Contains(s) ||
                        b.Location.Contains(s) ||
                        b.Contect.Contains(s));

                    if (int.TryParse(s, out int n))
                        q = q.Where(b => b.Id == n || b.BranchId == n);
                }

                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    var want = activeFilter.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                    q = q.Where(b => (b.IsActive ?? false) == want);
                }

                if (companyFilter.HasValue)
                    q = q.Where(b => b.CompanyId == companyFilter.Value);

                var list = q.OrderBy(b => b.Name)
                            .Select(b => new BranchExportRow
                            {
                                Id = b.Id,
                                BranchId = b.BranchId,
                                Name = b.Name,
                                Location = b.Location,
                               
                                Active = (b.IsActive ?? false) ? "Yes" : "No",
                                Deleted = (b.Deleted ?? false) ? "Yes" : "No"
                            })
                            .ToList();

                return new ViewAsPdf("ExportBranchesTable", list)
                {
                    FileName = "Branches_List.pdf",
                    PageSize = Rotativa.Options.Size.A4
                };
            }


            protected override void Dispose(bool disposing)
            {
                if (disposing) db.Dispose();
                base.Dispose(disposing);
            }
        }
    }
