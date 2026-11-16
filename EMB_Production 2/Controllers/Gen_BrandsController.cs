using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using OfficeOpenXml;
using PagedList;
using System.Security.Claims;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using System.Globalization;
using EMB_Production.Models;

namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/brands")]
    public class Gen_BrandsController : BaseController
    {
        // ---------------- LIST ----------------
        [HttpGet, Route("list")]
        public ActionResult List(string search = "", int page = 1, int pageSize = 10, string activeFilter = "", int? branchId = null)
        {
            var query =
                from b in db.Gen_Brands
                where b.Deleted == false || b.Deleted == null
                join br in db.Branches on b.BranchId equals br.Id into gbr
                from br in gbr.DefaultIfEmpty()
                select new BrandListViewModel
                {
                    Id = b.Id,
                    Code = b.Code,
                    BrandName = b.BrandName,
                    Description = b.Description,
                    BranchId = b.BranchId,     // ✅ include this
                BranchName = br.Name,        // display in table
                Active = b.Active ?? false
                };

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(b =>
                    b.Code.Contains(search) ||
                    b.BrandName.Contains(search) ||
                    b.BranchName.Contains(search) ||
                    (b.Description != null && b.Description.Contains(search))
                );
            }

            if (activeFilter == "active")
                query = query.Where(b => b.Active == true);
            else if (activeFilter == "inactive")
                query = query.Where(b => b.Active == false);

            if (branchId.HasValue && branchId.Value > 0)
                query = query.Where(b => b.BranchId == branchId.Value);

            var totalItems = query.Count();

            // Phase R.11 - summary aggregates
            var summarySource = query.Select(b => new
            {
                b.Active,
                b.BranchId,
                b.BranchName
            }).ToList();

            int activeCount = summarySource.Count(s => s.Active);
            int inactiveCount = summarySource.Count(s => !s.Active);
            int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();

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
                distinctBranches,
                byBranch
            };

            var items = query
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
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
        // Phase R.11 - PROFILE
        // ============================================================
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var b = db.Gen_Brands.FirstOrDefault(x => x.Id == id);
                if (b == null) return JsonError("Brand not found.");

                var branch = b.BranchId.HasValue
                    ? db.Branches.FirstOrDefault(br => br.Id == b.BranchId.Value)
                    : null;

                var header = new
                {
                    id = b.Id,
                    code = b.Code,
                    brandName = b.BrandName,
                    description = b.Description,
                    branchId = b.BranchId,
                    branchName = branch != null ? branch.Name : "",
                    active = b.Active ?? false,
                    createdDateText = b.CreatedDate.HasValue ? b.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = b.UpdatedDate.HasValue ? b.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Linked categories
                var categories = (
                    from c in db.Gen_Catagories
                    where c.BrandId == id && (c.Deleted == false || c.Deleted == null)
                    orderby c.Id descending
                    select new
                    {
                        c.Id,
                        c.Code,
                        c.Name,
                        c.Quantity,
                        c.Length,
                        c.Weight,
                        c.Price,
                        Active = c.Active ?? false
                    }
                ).Take(50).ToList();

                // Linked machines that use this brand
                var machines = (
                    from m in db.Gen_Machines
                    where m.BrandId == id && (m.Deleted == false || m.Deleted == null)
                    orderby m.Id descending
                    select new
                    {
                        m.Id,
                        m.Code,
                        SerialNumber = m.Serial_Number,
                        m.HeadsNum,
                        Active = m.Active == true
                    }
                ).Take(50).ToList();

                // Sibling brands in same branch
                var siblings = (
                    from x in db.Gen_Brands
                    where x.Id != id
                       && (x.Deleted == false || x.Deleted == null)
                       && x.BranchId == b.BranchId
                    orderby x.Id descending
                    select new
                    {
                        x.Id,
                        x.Code,
                        x.BrandName,
                        Active = x.Active ?? false
                    }
                ).Take(20).ToList();

                decimal avgPrice = categories.Any() ? categories.Average(c => c.Price ?? 0) : 0m;

                var kpis = new
                {
                    categoryCount = categories.Count(),
                    machineCount = machines.Count(),
                    branchSiblings = siblings.Count(),
                    avgCategoryPrice = Math.Round(avgPrice, 2),
                    activeCategories = categories.Count(c => c.Active)
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    categories,
                    machines,
                    siblings
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }

        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var b = db.Gen_Brands.FirstOrDefault(x => x.Id == id);
            if (b == null)
                return Json(new { success = false, message = "Brand not found." }, JsonRequestBehavior.AllowGet);

            var createdUser = db.Adm_Users.FirstOrDefault(u => u.Id == b.CreatedBy);
            var updatedUser = db.Adm_Users.FirstOrDefault(u => u.Id == b.UpdatedBy);
            var branch = db.Branches.FirstOrDefault(br => br.Id == b.BranchId);

            var culture = new CultureInfo("en-US");

            var dto = new
            {
                b.Id,
                b.Code,
                b.BrandName,
                b.Description,
                b.BranchId,
                BranchName = branch?.Name ?? "-",
                Active = b.Active ?? false,
                Deleted = b.Deleted ?? false,

                CreatedBy = createdUser?.EmployeeId ?? null,
                CreatedByName = createdUser != null ?
                    (createdUser.FirstName + " " + createdUser.LastName).Trim() : "-",
                CreatedDateFormatted = b.CreatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",

                UpdatedBy = updatedUser?.EmployeeId ?? null,
                UpdatedByName = updatedUser != null ?
                    (updatedUser.FirstName + " " + updatedUser.LastName).Trim() : "-",
                UpdatedDateFormatted = b.UpdatedDate?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-",

                RecordTimeStampFormatted =
                    b.RecordTimeStamp?.ToString("dd MMM yyyy hh:mm tt", culture) ?? "-"
            };

            return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
        }


        [HttpPost, Route("create")]
        public ActionResult Create()
        {
            try
            {
                var f = Request.Form;
                int userId = GetCurrentUserId();

                if (userId <= 0)
                    return JsonError("Invalid user session.");

                // ---------------------------
                // 1️⃣ Normalize inputs
                // ---------------------------
                string code = (f["code"] ?? f["Code"])?.Trim();
                string brandName = (f["brandName"] ?? f["BrandName"])?.Trim();
                string description = (f["description"] ?? f["Description"])?.Trim();

                // ---------------------------
                // 2️⃣ Extract BranchId
                // ---------------------------
                int? branchId =
                    ToInt(f["branchId"]) ??
                    ToInt(f["BranchId"]);

                if (!branchId.HasValue)
                    return JsonError("Branch selection is required.");

                bool branchExists = db.Branches.Any(x =>
                    x.Id == branchId.Value &&
                    (x.Deleted == false || x.Deleted == null));

                if (!branchExists)
                    return JsonError("Selected branch does not exist.");

                // ---------------------------
                // 3️⃣ Validate required fields
                // ---------------------------
                if (string.IsNullOrWhiteSpace(code))
                    return JsonError("Code is required.");

                if (string.IsNullOrWhiteSpace(brandName))
                    return JsonError("Brand name is required.");

                // ---------------------------
                // 4️⃣ Prevent duplicates
                // ---------------------------
                bool exists = db.Gen_Brands.Any(x =>
                    x.BranchId == branchId.Value &&
                    x.BrandName == brandName &&
                    (x.Deleted == false || x.Deleted == null));

                if (exists)
                    return JsonError("Brand already exists for this branch.");

                // ---------------------------
                // 5️⃣ Active flag (SAFE)
                // ---------------------------
                bool active = true;
                if (f.AllKeys.Contains("active"))
                    active = ToBool(f["active"]);

                // ---------------------------
                // 6️⃣ Create entity
                // ---------------------------
                var now = DateTime.Now;

                var brand = new Gen_Brands
                {
                    Code = code,
                    BrandName = brandName,
                    Description = description,

                    BranchId = branchId.Value,
                    Active = active,

                    CreatedBy = userId,
                    CreatedDate = now,
                    UpdatedBy = userId,
                    UpdatedDate = now,
                    RecordTimeStamp = now,
                    Deleted = false
                };

                db.Gen_Brands.Add(brand);
                db.SaveChanges();

                return JsonSuccess("Brand created successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("SERVER EXCEPTION", ex);
            }
        }


        // ============ UPDATE ============
        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id)
        {
            try
            {
                var brand = db.Gen_Brands.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (brand == null)
                    return JsonError("Brand not found.");

                int userId = GetCurrentUserId();
                if (userId <= 0)
                    return JsonError("Invalid user session.");

                var f = Request.Form;

                // ---------------------------
                // Normalize inputs
                // ---------------------------
                string code = (f["code"] ?? f["Code"])?.Trim();
                string brandName = (f["brandName"] ?? f["BrandName"])?.Trim();
                string description = (f["description"] ?? f["Description"])?.Trim();

                if (string.IsNullOrWhiteSpace(code))
                    return JsonError("Code is required.");

                if (string.IsNullOrWhiteSpace(brandName))
                    return JsonError("Brand Name is required.");

                // ---------------------------
                // BranchId (safe)
                // ---------------------------
                int? branchId =
                    ToInt(f["branchId"]) ??
                    ToInt(f["BranchId"]);

                if (!branchId.HasValue)
                    return JsonError("Branch selection is required.");

                bool branchExists = db.Branches.Any(x =>
                    x.Id == branchId.Value &&
                    (x.Deleted == false || x.Deleted == null));

                if (!branchExists)
                    return JsonError("Selected branch does not exist.");

                // ---------------------------
                // Active (checkbox safe)
                // ---------------------------
                bool active = true;
                if (f.AllKeys.Contains("active"))
                    active = ToBool(f["active"]);

                // ---------------------------
                // Update entity
                // ---------------------------
                brand.Code = code;
                brand.BrandName = brandName;
                brand.Description = description;
                brand.BranchId = branchId.Value;
                brand.Active = active;

                brand.UpdatedBy = userId;
                brand.UpdatedDate = DateTime.Now;
                brand.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Brand updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("SERVER EXCEPTION", ex);               
            }
        }


        // ---------------- DELETE ----------------
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var brand = db.Gen_Brands.Find(id);
                if (brand == null)
                    return JsonError("Brand not found.");

                brand.Deleted = true;
                brand.UpdatedBy = GetCurrentUserId();
                brand.UpdatedDate = DateTime.Now;

                db.SaveChanges();
                return JsonSuccess("Brand deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Error deleting brand.", ex);
            }
        }



        public ActionResult Index(
            string searchString,
            string activeFilter,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null,
            int page = 1,
            int pageSize = 10)
        {
            // 1) Base query
            var query = db.Gen_Brands
                          .Where(b => b.Deleted != true);

            // 2) Filters
            if (!string.IsNullOrEmpty(searchString))
                query = query.Where(b => b.BrandName.Contains(searchString));
            if (!string.IsNullOrEmpty(activeFilter))
            {
                bool wantActive = activeFilter == "Yes";
                query = query.Where(b => b.Active == wantActive);
            }

            // 3) Paging
            var paged = query
                .OrderBy(b => b.BrandName)
                .ToPagedList(page, pageSize);

            // 4) Build VM
            var vm = new BrandsPageViewModel
            {
                Brands = paged,                   // <- use 'paged' here
                NewBrand = showCreate ? new Gen_Brands() : null,
                EditBrand = showEdit && editId.HasValue
                                 ? db.Gen_Brands.Find(editId.Value)
                                 : null,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                SearchString = searchString,
                ActiveFilter = activeFilter,
                BranchList = new SelectList(
                                  db.Branches
                                    .Where(b => b.Deleted != true)
                                    .OrderBy(b => b.Name)
                                    .ToList(),
                                  "Id", "Name")
            };

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(Gen_Brands formBrand)
        {
           
            if (!ModelState.IsValid)
            {
                TempData["CreateErrors"] = ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return RedirectToAction("Index", new { showCreate = true });
            }
            int userEmpId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            formBrand.CreatedBy = userEmpId;
            formBrand.CreatedDate = DateTime.UtcNow;
            formBrand.UpdatedBy = userEmpId;
            formBrand.UpdatedDate = formBrand.CreatedDate;
            formBrand.Active = true;
            formBrand.Deleted = false;
            formBrand.RecordTimeStamp = DateTime.UtcNow;
            db.Gen_Brands.Add(formBrand);
            db.SaveChanges();
            return RedirectToAction("Index");
        }
               
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(Gen_Brands formBrand)
        {
            if (!ModelState.IsValid)
            {
                TempData["EditErrors"] = ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return RedirectToAction("Index", new { showEdit = true, editId = formBrand.Id });
            }
            var existing = db.Gen_Brands.Find(formBrand.Id);
            int userEmpId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            // copy allowed fields
            existing.Code = formBrand.Code;
            existing.BrandName = formBrand.BrandName;
            existing.Description = formBrand.Description;
            existing.BranchId = formBrand.BranchId;
            existing.Active = formBrand.Active;
            existing.Deleted = formBrand.Deleted;
            existing.UpdatedBy = userEmpId;
            existing.UpdatedDate = DateTime.UtcNow;
            existing.RecordTimeStamp = DateTime.UtcNow;
            db.Entry(existing).State = EntityState.Modified;
            db.SaveChanges();
            return RedirectToAction("Index");
        }
                            
        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateBrandField(int brandId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            var brand = db.Gen_Brands.Find(brandId);
            if (brand == null)
                return Json(new { success = false, message = "Brand not found." });

            int userId = Convert.ToInt32(Session["UserID"]);

            try
            {
                // Use reflection or switch-case for security
                switch (fieldName)
                {
                    case "BrandName":
                        brand.BrandName = newValue;
                        break;
                    case "Email":
                        brand.Code = newValue;
                        break;
                    // Add more fields as needed
                    default:
                        return Json(new { success = false, message = "Invalid field." });
                }

                brand.UpdatedBy = userId;
                brand.UpdatedDate = DateTime.UtcNow;
                brand.RecordTimeStamp = DateTime.UtcNow;

                db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        [HttpPost]
        [Route("bulk-update")]
        public JsonResult BulkUpdateBrand(List<int> brandIds, string actionType)
        {

            int userId;
            var cp = User as ClaimsPrincipal;
            var idClaim = cp?.FindFirst(ClaimTypes.NameIdentifier);
            if (idClaim != null && int.TryParse(idClaim.Value, out userId))
            {
                // got it from Identity
            }
            // 2) Fallback to Session["UserID"]
            else if (Session["UserID"] != null && int.TryParse(Session["UserID"].ToString(), out userId))
            {
                // got it from Session
            }
            else
            {
                return Json(new { success = false, message = "Not authenticated." });
            }


            var brands = db.Gen_Brands.Where(b => brandIds.Contains(b.Id)).ToList();

            foreach (var brand in brands)
            {
                switch (actionType)
                {
                    case "Activate":
                        brand.Active = true;
                        break;
                    case "Deactivate":
                        brand.Active = false;
                        break;
                    case "Delete":
                        brand.Deleted = true;
                        break;
                    default:
                        return Json(new { success = false, message = "Unknown action." });
                }

                brand.UpdatedBy = userId;
                brand.UpdatedDate = DateTime.UtcNow;
                brand.RecordTimeStamp = DateTime.UtcNow;
            }

            db.SaveChanges();

            return Json(new { success = true });
        }
        // GET: Gen_Brands/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_Brands gen_Brands = db.Gen_Brands.Find(id);
            if (gen_Brands == null)
            {
                return HttpNotFound();
            }
            return View(gen_Brands);
        }


        // GET: Gen_Brands/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_Brands gen_Brands = db.Gen_Brands.Find(id);
            if (gen_Brands == null)
            {
                return HttpNotFound();
            }
            return View(gen_Brands);
        }

        // POST: Gen_Brands/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Gen_Brands gen_Brands = db.Gen_Brands.Find(id);
            if (gen_Brands == null)
                return HttpNotFound();

            gen_Brands.Deleted = true;
            gen_Brands.UpdatedBy = GetCurrentUserId();
            gen_Brands.UpdatedDate = DateTime.Now;

            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

       [Obsolete]
        public ActionResult ExportToExcel(string searchString, string activeFilter)
        {
            // 1) Build your filtered query exactly as in Index/AllBrands
            var query = db.Gen_Brands.AsQueryable();
            if (!string.IsNullOrEmpty(searchString))
                query = query.Where(b => b.BrandName.Contains(searchString));
            if (!string.IsNullOrEmpty(activeFilter))
            {
                bool wantActive = activeFilter == "Yes";
                query = query.Where(b => b.Active == wantActive);
            }
            query = query.Where(b =>(bool) !b.Deleted)
                         .OrderBy(b => b.BrandName);

            var list = query.ToList();
                      
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Brands");

                // Headers
                var headers = new[] { "ID", "Code", "Brand Name", "Description", "Branch ID", "Active" };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = headers[c];
                    ws.Cells[1, c + 1].Style.Font.Bold = true;
                }

                // Data
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    int row = i + 2;
                    ws.Cells[row, 1].Value = b.Id;
                    ws.Cells[row, 2].Value = b.Code;
                    ws.Cells[row, 3].Value = b.BrandName;
                    ws.Cells[row, 4].Value = b.Description;
                    ws.Cells[row, 5].Value = b.BranchId;
                    ws.Cells[row, 6].Value =(bool) b.Active ? "Yes" : "No";
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                // 4) Return file
                var bytes = package.GetAsByteArray();
                return File(
                    bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Brands.xlsx"
                );
            }
        }


        public ActionResult ExportToPdf(string searchString, string activeFilter)
        {
            var query = db.Gen_Brands.AsQueryable();
            if (!string.IsNullOrEmpty(searchString))
                query = query.Where(b => b.BrandName.Contains(searchString));
            if (!string.IsNullOrEmpty(activeFilter))
                query = query.Where(b => ((bool)b.Active ? "Yes" : "No") == activeFilter);
            query = query.Where(b => b.Deleted != true)
                         .OrderBy(b => b.BrandName);

            var pagedList = query.ToPagedList(1, Int32.MaxValue);
            var vm = new BrandsPageViewModel
            {
                Brands = pagedList,
                NewBrand = null,
                EditBrand = null
            };

            return new Rotativa.ViewAsPdf("Index", vm)
            {
                FileName = "Brands.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait,
                CustomSwitches = "--print-media-type --load-error-handling ignore"
            };
        }
        [HttpGet]
        [Route("export-detail-excel/{id:int}")]
        public ActionResult ExportDetailToExcel(int id)
        {
            var b = db.Gen_Brands
                      .Include(x => x.Branches)
                      .Include(x => x.Gen_Catagories)   
                      .Include(x => x.Gen_Machines)     
                      .FirstOrDefault(x => x.Id == id);
            if (b == null) return HttpNotFound();
           
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("BrandDetail");

                // Header row
                ws.Cells[1, 1].Value = "Field";
                ws.Cells[1, 2].Value = "Value";
                ws.Cells[1, 1, 1, 2].Style.Font.Bold = true;

                int r = 2;
                void Add(string field, object val)
                {
                    ws.Cells[r, 1].Value = field;
                    ws.Cells[r, 2].Value = val ?? "(none)";
                    r++;
                }

                Add("ID", b.Id);
                Add("Code", b.Code);
                Add("Brand Name", b.BrandName);
                Add("Description", b.Description);
                Add("Branch", b.Branches?.Name);
                Add("Active", (b.Active ?? false) ? "Yes" : "No");
                Add("Deleted", (b.Deleted ?? false) ? "Yes" : "No");
                Add("RecordTimeStamp", b.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"));
                Add("CreatedBy", b.CreatedBy);
                Add("CreatedDate", b.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                Add("UpdatedBy", b.UpdatedBy);
                Add("UpdatedDate", b.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                // optionally, list child collections:
                ws.Cells[r, 1].Value = "Categories (" + b.Gen_Catagories.Count + ")";
                ws.Cells[r, 1, r, 2].Merge = true; ws.Cells[r, 1].Style.Font.Bold = true; r++;
                ws.Cells[r, 1].Value = "ID"; ws.Cells[r, 2].Value = "Name";
                ws.Cells[r, 1, r, 2].Style.Font.Bold = true; r++;
                foreach (var c in b.Gen_Catagories.OrderBy(x => x.Id))
                {
                    Add(c.Id.ToString(), c.Name);
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                var bytes = pkg.GetAsByteArray();
                return File(bytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"Brand_{b.Id}_Detail.xlsx");
            }
        }
        [HttpGet]
        [Route("export-detail-pdf/{id:int}")]
        public ActionResult ExportDetailToPdf(int id)
        {
            var b = db.Gen_Brands
                      .Include(x => x.Branches)
                      .Include(x => x.Gen_Catagories)
                      .Include(x => x.Gen_Machines)
                      .FirstOrDefault(x => x.Id == id);
            if (b == null) return HttpNotFound();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                var table = new PdfPTable(2) { WidthPercentage = 90 };
                table.DefaultCell.Padding = 5;

                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 10, BaseColor.DARK_GRAY);
                var italFont = FontFactory.GetFont(FontFactory.HELVETICA_OBLIQUE, 10, BaseColor.GRAY);

                // Title
                table.AddCell(new PdfPCell(new Phrase("Brand Detail", headerFont))
                {
                    Colspan = 2,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 6
                });

                void Row(string label, object val)
                {
                    table.AddCell(new PdfPCell(new Phrase(label, labelFont)));
                    table.AddCell(new PdfPCell(new Phrase(val?.ToString() ?? "(none)", valueFont)));
                }

                Row("ID", b.Id);
                Row("Code", b.Code);
                Row("Name", b.BrandName);
                Row("Description", b.Description);
                Row("Branch", b.Branches?.Name);
                Row("Active", (b.Active ?? false) ? "Yes" : "No");
                Row("Deleted", (b.Deleted ?? false) ? "Yes" : "No");
                Row("Created By", b.CreatedBy);
                Row("Created Date", b.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                Row("Updated By", b.UpdatedBy);
                Row("Updated Date", b.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                // — Categories —
                table.AddCell(new PdfPCell(new Phrase($"Categories ({b.Gen_Catagories.Count})", labelFont))
                {
                    Colspan = 2,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 4
                });

                if (b.Gen_Catagories.Any())
                {
                    foreach (var c in b.Gen_Catagories.OrderBy(x => x.Id))
                    {
                        Row("ID", c.Id);
                        Row("Name", c.Name);
                    }
                }
                else
                {
                    table.AddCell(new PdfPCell(new Phrase("(no categories)", italFont))
                    {
                        Colspan = 2,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 4
                    });
                }

                // — Machines (if you want) —
                table.AddCell(new PdfPCell(new Phrase($"Machines ({b.Gen_Machines.Count})", labelFont))
                {
                    Colspan = 2,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 4
                });

                if (b.Gen_Machines.Any())
                {
                    foreach (var m in b.Gen_Machines.OrderBy(x => x.Id))
                    {
                        Row("ID", m.Id);
                        Row("Name", m.Code);
                    }
                }
                else
                {
                    table.AddCell(new PdfPCell(new Phrase("(no machines)", italFont))
                    {
                        Colspan = 2,
                        HorizontalAlignment = Element.ALIGN_CENTER,
                        Padding = 4
                    });
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(), "application/pdf", $"Brand_{b.Id}_Detail.pdf");
            }
        }

    }
}
