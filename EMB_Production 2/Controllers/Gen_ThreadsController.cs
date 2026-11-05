using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using PagedList;
using OfficeOpenXml;
using Rotativa;
using System.IO;
using iTextSharp.text;
using iTextSharp.text.pdf;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/threads")]
    public class Gen_ThreadsController : BaseController
    {
        // ===================== LIST =====================
        [HttpGet, Route("list")]
        public ActionResult List(
     string search = "",
     string activeFilter = "",
     int page = 1,
     int pageSize = 10)
        {
            var query = db.Gen_Threads
                .Where(t => t.Deleted == false || t.Deleted == null);

            if (activeFilter == "active")
                query = query.Where(x => x.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(x => x.Active == false || x.Active == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(x =>
                    (x.Code != null && x.Code.Contains(search)) ||
                    (x.Name != null && x.Name.Contains(search)) ||
                    (x.Material != null && x.Material.Contains(search)) ||
                    (x.YarnCount != null && x.YarnCount.Contains(search)) ||
                    (x.Color != null && x.Color.Contains(search)) ||
                    (x.ColorCode != null && x.ColorCode.Contains(search))
                );
            }

            var total = query.Count();

            var items = query
                .OrderBy(x => x.Code)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ThreadRowViewModel
                {
                    Id = x.Id,
                    Code = x.Code,
                    Name = x.Name,
                    Type = x.Type,
                    Material = x.Material,
                    YarnCount = x.YarnCount,
                    Color = x.Color,
                    ColorCode = x.ColorCode,
                    UnitLength = x.UnitLength,
                    UnitOfMeasure = x.UnitOfMeasure,
                    UnitCost = x.UnitCost,
                    BranchName = x.Branches != null ? x.Branches.Name : "-",
                    SupplierName = x.Gen_Suppliers != null ? x.Gen_Suppliers.SupplierName : "-",
                    Active = x.Active ?? false
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = items,
                total
            }, JsonRequestBehavior.AllowGet);
        }


        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var x = db.Gen_Threads
                .FirstOrDefault(t => t.Id == id && (t.Deleted == false || t.Deleted == null));

            if (x == null)
                return JsonError("Thread not found.");

            var vm = new ThreadDetailViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Type = x.Type,
                Material = x.Material,
                YarnCount = x.YarnCount,
                Color = x.Color,
                ColorCode = x.ColorCode,
                UnitLength = x.UnitLength,
                UnitOfMeasure = x.UnitOfMeasure,
                UnitCost = x.UnitCost,

                SupplierId = x.SupplierId,
                SupplierName = x.Gen_Suppliers?.SupplierName,

                BranchId = x.BranchId,
                BranchName = x.Branches?.Name,

                Active = x.Active ?? false,

                CreatedByEmployeeId = GetEmployeeId(x.CreatedBy),
                CreatedDateText = ToFormattedDate(x.CreatedDate),

                UpdatedByEmployeeId = GetEmployeeId(x.UpdatedBy),
                UpdatedDateText = ToFormattedDate(x.UpdatedDate),
                
            };

            return JsonSuccess("Thread detail loaded.", vm);
        }


        // ===================== CREATE =====================
        [HttpPost]
        [Route("create")]
        public ActionResult Create(ThreadFormViewModel dto)
        {
            if (dto == null)
                return JsonError("Invalid data.");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return JsonError("Thread Name is required.");

            // Normalize
            dto.Name = dto.Name.Trim();
            dto.Type = dto.Type?.Trim();
            dto.Material = dto.Material?.Trim();
            dto.YarnCount = dto.YarnCount?.Trim();
            dto.Color = dto.Color?.Trim();
            dto.ColorCode = dto.ColorCode?.Trim();
            dto.UnitOfMeasure = dto.UnitOfMeasure?.Trim();

            var now = DateTime.Now;
            var userId = GetSafeCurrentUserId();

            var entity = new Gen_Threads
            {
                Code = GenerateThreadCode(), // ✅ AUTO
                Name = dto.Name,
                Type = dto.Type,
                Material = dto.Material,
                YarnCount = dto.YarnCount,
                Color = dto.Color,
                ColorCode = dto.ColorCode,
                UnitLength = dto.UnitLength,
                UnitOfMeasure = dto.UnitOfMeasure,
                UnitCost = dto.UnitCost,

                SupplierId = dto.SupplierId,
                BranchId = dto.BranchId,

                Active = dto.Active,
                Deleted = false,

                CreatedBy = userId,
                CreatedDate = now,
                
            };

            db.Gen_Threads.Add(entity);
            db.SaveChanges();

            return JsonSuccess("Thread created successfully.");
        }


        // ===================== UPDATE =====================
        [HttpPost]
        [Route("update")]
        public ActionResult Update(ThreadFormViewModel dto)
        {
            if (dto == null || dto.Id <= 0)
                return JsonError("Invalid data.");

            var entity = db.Gen_Threads.FirstOrDefault(x =>
                x.Id == dto.Id &&
                (x.Deleted == false || x.Deleted == null));

            if (entity == null)
                return JsonError("Thread not found.");

            // Normalize
            dto.Name = dto.Name?.Trim();
            dto.Type = dto.Type?.Trim();
            dto.Material = dto.Material?.Trim();
            dto.YarnCount = dto.YarnCount?.Trim();
            dto.Color = dto.Color?.Trim();
            dto.ColorCode = dto.ColorCode?.Trim();
            dto.UnitOfMeasure = dto.UnitOfMeasure?.Trim();

            // ❌ DO NOT TOUCH entity.Code

            entity.Name = dto.Name;
            entity.Type = dto.Type;
            entity.Material = dto.Material;
            entity.YarnCount = dto.YarnCount;
            entity.Color = dto.Color;
            entity.ColorCode = dto.ColorCode;
            entity.UnitLength = dto.UnitLength;
            entity.UnitOfMeasure = dto.UnitOfMeasure;
            entity.UnitCost = dto.UnitCost;

            entity.SupplierId = dto.SupplierId;
            entity.BranchId = dto.BranchId;
            entity.Active = dto.Active;

            entity.UpdatedBy = GetSafeCurrentUserId();
            entity.UpdatedDate = DateTime.Now;

            db.SaveChanges();

            return JsonSuccess("Thread updated successfully.");
        }



        // ===================== DELETE (soft) =====================
        [HttpPost]
        [Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var entity = db.Gen_Threads.FirstOrDefault(x => x.Id == id);

                if (entity == null)
                    return JsonError("Record not found.");

                int userId = GetCurrentUserId();

                if (userId <= 0 || !db.Adm_Users.Any(u => u.Id == userId))
                    return JsonError("Current user is invalid. Please login again.");

                entity.Deleted = true;
                entity.UpdatedBy = userId;
                entity.UpdatedDate = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed.", ex);
            }
        }

        private string GenerateThreadCode()
        {
            var lastCode = db.Gen_Threads
                .Where(x => x.Code.StartsWith("THR-"))
                .OrderByDescending(x => x.Id)
                .Select(x => x.Code)
                .FirstOrDefault();

            int next = 1;

            if (!string.IsNullOrEmpty(lastCode))
            {
                var num = lastCode.Replace("THR-", "");
                int.TryParse(num, out next);
                next++;
            }

            return "THR-" + next.ToString("D4");
        }


        // GET: Index with inline Create/Edit & paging
        public ActionResult Index(
        string search = "",
        string activeFilter = "",
        bool showCreate = false,
        bool showEdit = false,
        int? editId = null,
        int page = 1,
        int pageSize = 20)
        {
            // --------------------------------------------------
            // BASE QUERY
            // --------------------------------------------------
            var q = db.Gen_Threads
                      .Where(t => t.Deleted == false || t.Deleted == null);

            // --------------------------------------------------
            // SEARCH
            // --------------------------------------------------
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                q = q.Where(t =>
                    (t.Code != null && t.Code.Contains(search)) ||
                    (t.Name != null && t.Name.Contains(search)) ||
                    (t.Material != null && t.Material.Contains(search))
                );
            }

            // --------------------------------------------------
            // ACTIVE FILTER (MATCH FABRIC / ANGULAR)
            // --------------------------------------------------
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                if (activeFilter == "active")
                    q = q.Where(t => t.Active == true);
                else if (activeFilter == "inactive")
                    q = q.Where(t => t.Active == false || t.Active == null);
            }

            // --------------------------------------------------
            // PAGED LIST
            // --------------------------------------------------
            var paged = q.OrderBy(t => t.Code)
                         .Select(t => new ThreadRowViewModel
                         {
                             Id = t.Id,
                             Code = t.Code,
                             Name = t.Name,
                             Type = t.Type,
                             Material = t.Material,
                             YarnCount = t.YarnCount,
                             Color = t.Color,
                             ColorCode = t.ColorCode,
                             UnitLength = t.UnitLength,
                             UnitOfMeasure = t.UnitOfMeasure,
                             UnitCost = t.UnitCost,                             
                             SupplierName = t.Gen_Suppliers != null
                                              ? t.Gen_Suppliers.SupplierName
                                              : "-",

                             BranchName = t.Branches != null
                                              ? t.Branches.Name
                                              : "-",

                             Active = t.Active ?? false
                         })
                         .ToPagedList(page, pageSize);

            // --------------------------------------------------
            // PAGE VIEW MODEL
            // --------------------------------------------------
            var vm = new ThreadsPageViewModel
            {
                Threads = paged,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter
            };

            // --------------------------------------------------
            // CREATE / EDIT MODELS (IMPORTANT FIX)
            // --------------------------------------------------
            if (showCreate)
            {
                vm.NewThread = new ThreadFormViewModel
                {
                    Active = true
                };
            }

            if (showEdit && editId.HasValue)
            {
                var e = db.Gen_Threads
                          .FirstOrDefault(x =>
                              x.Id == editId.Value &&
                              (x.Deleted == false || x.Deleted == null));

                if (e != null)
                {
                    vm.EditThread = new ThreadFormViewModel
                    {
                        Id = e.Id,
                        Code = e.Code,
                        Name = e.Name,
                        Type = e.Type,
                        Material = e.Material,
                        YarnCount = e.YarnCount,
                        Color = e.Color,
                        ColorCode = e.ColorCode,
                        UnitLength = e.UnitLength,
                        UnitOfMeasure = e.UnitOfMeasure,
                        UnitCost = e.UnitCost,
                        SupplierId = e.SupplierId,
                        BranchId = e.BranchId,
                        Active = e.Active ?? true
                    };
                }
            }

            // --------------------------------------------------
            // BRANCH DROPDOWN (FIXED FOR CREATE + EDIT)
            // --------------------------------------------------
            var branchItems = db.Branches
                .Where(b => b.IsActive == true && (b.Deleted == false || b.Deleted == null))
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name })
                .ToList();

            int? selectedBranch =
                vm.EditThread?.BranchId ??
                vm.NewThread?.BranchId;

            vm.BranchList = new SelectList(branchItems, "Id", "Name", selectedBranch);

            return View(vm);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(
                ThreadsPageViewModel vm,
                string search = null,
                string activeFilter = null,
                int page = 1,
                int pageSize = 20)
        {
            // --------------------------------------------------
            // 1️⃣ Rebuild dropdowns (ALWAYS)
            // --------------------------------------------------
            var branchItems = db.Branches
                .Where(b => b.IsActive == true)
                .OrderBy(b => b.Name)
                .Select(b => new { b.Id, b.Name })
                .ToList();

            vm.BranchList = new SelectList(branchItems, "Id", "Name", vm.NewThread?.BranchId);

            vm.ShowCreate = true;

            // --------------------------------------------------
            // 2️⃣ Validation
            // --------------------------------------------------
            if (!ModelState.IsValid || vm.NewThread == null)
                return View("Index", vm);

            // --------------------------------------------------
            // 3️⃣ Generate Dynamic Thread Code (THR-0001)
            // --------------------------------------------------
            string nextCode = GenerateNextThreadCode();

            // --------------------------------------------------
            // 4️⃣ Create ENTITY (NOT ViewModel)
            // --------------------------------------------------
            var uid = GetSafeCurrentUserId();
            var now = DateTime.Now;

            var entity = new Gen_Threads
            {
                Code = nextCode,
                Name = vm.NewThread.Name?.Trim(),
                Type = vm.NewThread.Type,
                Material = vm.NewThread.Material,
                YarnCount = vm.NewThread.YarnCount,
                Color = vm.NewThread.Color,
                ColorCode = vm.NewThread.ColorCode,

                UnitLength = vm.NewThread.UnitLength,
                UnitOfMeasure = vm.NewThread.UnitOfMeasure,
                UnitCost = vm.NewThread.UnitCost,

                SupplierId = vm.NewThread.SupplierId,
                BranchId = vm.NewThread.BranchId,

                Active = vm.NewThread.Active,
                Deleted = false,

                CreatedBy = uid,
                CreatedDate = now
            };

            db.Gen_Threads.Add(entity);
            db.SaveChanges();

            // --------------------------------------------------
            // 5️⃣ Redirect (PRG pattern)
            // --------------------------------------------------
            return RedirectToAction("Index", new
            {
                search,
                activeFilter,
                page,
                pageSize
            });
        }



        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(
            ThreadsPageViewModel vm,
            string search = null,
            string activeFilter = null,
            int page = 1,
            int pageSize = 20)
        {
            vm.ShowEdit = true;
            if (!ModelState.IsValid)
                return RedirectToAction("Index", new { showEdit = true, editId = vm.EditThread.Id, search, activeFilter, page, pageSize });

            var t = db.Gen_Threads.Find(vm.EditThread.Id);
            if (t == null) return HttpNotFound();

            t.Code = vm.EditThread.Code;
            t.Name = vm.EditThread.Name;
            t.Type = vm.EditThread.Type;
            t.Material = vm.EditThread.Material;
            t.YarnCount = vm.EditThread.YarnCount;
            t.Color = vm.EditThread.Color;
            t.ColorCode = vm.EditThread.ColorCode;
            t.UnitLength = vm.EditThread.UnitLength;
            t.UnitOfMeasure = vm.EditThread.UnitOfMeasure;
            t.UnitCost = vm.EditThread.UnitCost;
            t.SupplierId = vm.EditThread.SupplierId;
            t.BranchId = vm.EditThread.BranchId;
            t.Active = vm.EditThread.Active;
            t.Deleted = vm.EditThread.Deleted;
            t.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            t.UpdatedDate = DateTime.UtcNow;

            db.Entry(t).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateThreadField(int threadId, string fieldName, string newValue)
        {
            // ensure user is authenticated
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // load the thread record
            var entity = db.Gen_Threads.Find(threadId);
            if (entity == null)
                return Json(new { success = false, message = "Thread not found." });

            // reflectively get the target property
            var prop = typeof(Gen_Threads).GetProperty(fieldName);
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

                // set the value and audit
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


        // Details
        public ActionResult Details(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // include the Branches navigation property
            var thread = db.Gen_Threads
                           .Include(t => t.Branches)
                           .FirstOrDefault(t => t.Id == id.Value);

            if (thread == null)
                return HttpNotFound();

            return View(thread);
        }

        // GET Delete
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var t = db.Gen_Threads.Find(id.Value);
            if (t == null) return HttpNotFound();
            return View(t);
        }

        // POST Delete
        [HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            var t = db.Gen_Threads.Find(id);
            db.Gen_Threads.Remove(t);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        // Put this INSIDE Gen_ThreadsController class (same file)
        private string GenerateNextThreadCode()
        {
            const string prefix = "THR-";

            // If Code can be NULL, keep filter safe
            var lastCode = db.Gen_Threads
                .Where(x => x.Deleted != true && x.Code != null && x.Code.StartsWith(prefix))
                .OrderByDescending(x => x.Code)
                .Select(x => x.Code)
                .FirstOrDefault();

            int nextNumber = 1;

            if (!string.IsNullOrWhiteSpace(lastCode))
            {
                var numPart = lastCode.Replace(prefix, "");
                int parsed;
                if (int.TryParse(numPart, out parsed))
                    nextNumber = parsed + 1;
            }

            return prefix + nextNumber.ToString("D4"); // THR-0001
        }


        // Export to Excel
        [HttpGet]
        [Route("exporttoexcel")]
        public ActionResult ExportToExcel(int? id = null, string search = null, string activeFilter = null)
        {                        
            if (id.HasValue)
            {
                var t = db.Gen_Threads
                          .Include(x => x.Branches)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (t == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                        var ws = pkg.Workbook.Worksheets.Add("ThreadDetail");

                    // header
                    ws.Cells[1, 1].Value = "Field";
                    ws.Cells[1, 2].Value = "Value";
                    using (var hdr = ws.Cells[1, 1, 1, 2]) hdr.Style.Font.Bold = true;

                    int row = 1;
                    void Add(string field, object val)
                    {
                        row++;
                        ws.Cells[row, 1].Value = field;
                        ws.Cells[row, 2].Value = val ?? "(none)";
                    }

                    Add("ID", t.Id);
                    Add("Code", t.Code);
                    Add("Name", t.Name);
                    Add("Type", t.Type);
                    Add("Material", t.Material);
                    Add("Yarn Count", t.YarnCount);
                    Add("Color", t.Color);
                    Add("Color Code", t.ColorCode);
                    Add("Unit Length", t.UnitLength);
                    Add("Unit Of Measure", t.UnitOfMeasure);
                    Add("Unit Cost", t.UnitCost);
                    Add("Supplier ID", t.SupplierId);
                    Add("Branch", t.Branches?.Name);
                    Add("Active", (t.Active ?? false) ? "Yes" : "No");
                    Add("Deleted", (t.Deleted ?? false) ? "Yes" : "No");
                    Add("Created By", t.CreatedBy);
                    Add("Created Date", t.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Updated By", t.UpdatedBy);
                    Add("Updated Date", t.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                $"Thread_{t.Id}_Detail.xlsx");
                }
            }

            // LIST EXPORT
            var query = db.Gen_Threads.Where(t => t.Deleted != true)
                                      .Include(t => t.Branches)
                                      .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t => t.Code.Contains(search) || t.Name.Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(t => (t.Active ?? false) == want);
            }

            var list = query.OrderBy(t => t.Code).ToList();
            using (var pkg2 = new ExcelPackage())
            { 
                    var ws2 = pkg2.Workbook.Worksheets.Add("Threads");
                var headers = new[]
                {
                "ID","Code","Name","Type","Material","YarnCount","Color","ColorCode",
                "UnitLength","UnitOfMeasure","UnitCost","SupplierId","Branch","Active","Deleted"
            };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws2.Cells[1, c + 1].Value = headers[c];
                    ws2.Cells[1, c + 1].Style.Font.Bold = true;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    int r = i + 2;
                    ws2.Cells[r, 1].Value = t.Id;
                    ws2.Cells[r, 2].Value = t.Code;
                    ws2.Cells[r, 3].Value = t.Name;
                    ws2.Cells[r, 4].Value = t.Type;
                    ws2.Cells[r, 5].Value = t.Material;
                    ws2.Cells[r, 6].Value = t.YarnCount;
                    ws2.Cells[r, 7].Value = t.Color;
                    ws2.Cells[r, 8].Value = t.ColorCode;
                    ws2.Cells[r, 9].Value = t.UnitLength;
                    ws2.Cells[r, 10].Value = t.UnitOfMeasure;
                    ws2.Cells[r, 11].Value = t.UnitCost;
                    ws2.Cells[r, 12].Value = t.SupplierId;
                    ws2.Cells[r, 13].Value = t.Branches?.Name;
                    ws2.Cells[r, 14].Value = (t.Active ?? false) ? "Yes" : "No";
                    ws2.Cells[r, 15].Value = (t.Deleted ?? false) ? "Yes" : "No";
                }
                ws2.Cells[ws2.Dimension.Address].AutoFitColumns();
                var bytes2 = pkg2.GetAsByteArray();
                return File(bytes2,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            "Threads.xlsx");
            }
        }

        // Export to PDF
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(int? id, string search = null, string activeFilter = null)
        {
            // Detail export
            if (id.HasValue)
            {
                var t = db.Gen_Threads
                          .Include(x => x.Branches)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (t == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                { 
                        var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    // two-column table: Field / Value
                    var table = new PdfPTable(2) { WidthPercentage = 80 };
                    void Cell(string text, bool header = false)
                    {
                        var c = new PdfPCell(new Phrase(text));
                        if (header) c.BackgroundColor = BaseColor.LIGHT_GRAY;
                        table.AddCell(c);
                    }
                    void Row(string field, object val)
                    {
                        Cell(field);
                        Cell(val?.ToString() ?? "(none)");
                    }

                    // header row
                    Cell("Field", true);
                    Cell("Value", true);

                    // data rows
                    Row("ID", t.Id);
                    Row("Code", t.Code);
                    Row("Name", t.Name);
                    Row("Type", t.Type);
                    Row("Material", t.Material);
                    Row("Yarn Count", t.YarnCount);
                    Row("Color", t.Color);
                    Row("Color Code", t.ColorCode);
                    Row("Unit Length", t.UnitLength);
                    Row("UOM", t.UnitOfMeasure);
                    Row("Unit Cost", t.UnitCost);
                    Row("Supplier ID", t.SupplierId);
                    Row("Branch", t.Branches?.Name);
                    Row("Active", (t.Active ?? false) ? "Yes" : "No");
                    Row("Deleted", (t.Deleted ?? false) ? "Yes" : "No");
                    Row("Created By", t.CreatedBy);
                    Row("Created Date", t.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Updated By", t.UpdatedBy);
                    Row("Updated Date", t.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(),
                                "application/pdf",
                                $"Thread_{t.Id}_Detail.pdf");
                }
            }

            // List export
            var query = db.Gen_Threads
                          .Include(x => x.Branches)
                          .Where(t => t.Deleted != true)
                          .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(t => t.Code.Contains(search) || t.Name.Contains(search));

            if (activeFilter == "Yes")
                query = query.Where(t => t.Active == true);
            else if (activeFilter == "No")
                query = query.Where(t => t.Active == false);

            var list = query.OrderBy(t => t.Code).ToList();

            using (var ms2 = new MemoryStream())
            {
                    var doc2 = new Document(PageSize.A4.Rotate(), 25, 25, 30, 30);
                PdfWriter.GetInstance(doc2, ms2);
                doc2.Open();

                // wide table for list
                var headers = new[] { "ID", "Code", "Name", "Type", "Branch", "Active" };
                var table2 = new PdfPTable(headers.Length) { WidthPercentage = 100 };
                foreach (var h in headers)
                    table2.AddCell(new PdfPCell(new Phrase(h)) { BackgroundColor = BaseColor.LIGHT_GRAY });

                foreach (var t2 in list)
                {
                    table2.AddCell(t2.Id.ToString());
                    table2.AddCell(t2.Code);
                    table2.AddCell(t2.Name);
                    table2.AddCell(t2.Type);
                    table2.AddCell(t2.Branches?.Name);
                    table2.AddCell((t2.Active ?? false) ? "Yes" : "No");
                }

                doc2.Add(table2);
                doc2.Close();

                return File(ms2.ToArray(),
                            "application/pdf",
                            "Threads.pdf");
            }
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
