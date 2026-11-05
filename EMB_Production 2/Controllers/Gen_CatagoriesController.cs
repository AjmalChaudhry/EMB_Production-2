using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Web;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;
using PagedList;

namespace EMB_Production.Controllers
{
    [RoutePrefix("Gen_Catagories")]
    public class Gen_CatagoriesController : BaseController
    {
        // ---------------- LIST ----------------
        [HttpGet, Route("list")]
        public ActionResult List( string search = "", int page = 1, int pageSize = 10, string active = "", int? brandId = null, int? branchId = null )
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;

                var q = db.Gen_Catagories
                    .Include(a => a.Gen_Brands)
                    .Include(a => a.Branches)
                    .Where(a => a.Deleted == null || a.Deleted == false);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    q = q.Where(a =>
                        a.Name.Contains(search) ||
                        a.Code.Contains(search) ||
                        a.Description.Contains(search));
                }

                if (!string.IsNullOrWhiteSpace(active))
                {
                    bool isActive = active.Equals("true", StringComparison.OrdinalIgnoreCase);
                    q = q.Where(a => (a.Active ?? false) == isActive);
                }

                if (brandId.HasValue && brandId.Value > 0)
                    q = q.Where(a => a.BrandId == brandId.Value);

                if (branchId.HasValue && branchId.Value > 0)
                    q = q.Where(a => a.BranchId == branchId.Value);

                var total = q.Count();

                // Phase R.12 - summary aggregates
                var summarySource = q.Select(a => new
                {
                    a.BrandId,
                    a.BranchId,
                    Active = a.Active ?? false,
                    a.Price,
                    BrandName = a.Gen_Brands.BrandName,
                    BranchName = a.Branches.Name
                }).ToList();

                int activeCount = summarySource.Count(s => s.Active);
                int inactiveCount = summarySource.Count(s => !s.Active);
                int distinctBrands = summarySource.Where(s => s.BrandId != null).Select(s => s.BrandId).Distinct().Count();
                int distinctBranches = summarySource.Where(s => s.BranchId != null).Select(s => s.BranchId).Distinct().Count();
                decimal totalValue = summarySource.Sum(s => s.Price ?? 0);
                decimal avgPrice = summarySource.Any() ? summarySource.Average(s => s.Price ?? 0) : 0m;
                decimal maxPrice = summarySource.Any() ? summarySource.Max(s => s.Price ?? 0) : 0m;
                decimal minPrice = summarySource.Any(s => s.Price.HasValue) ? summarySource.Where(s => s.Price.HasValue).Min(s => s.Price.Value) : 0m;

                var byBrand = summarySource
                    .Where(s => !string.IsNullOrWhiteSpace(s.BrandName))
                    .GroupBy(s => s.BrandName)
                    .Select(g => new { brand = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(5)
                    .ToList();

                var summary = new
                {
                    count = total,
                    activeCount,
                    inactiveCount,
                    distinctBrands,
                    distinctBranches,
                    totalValue = Math.Round(totalValue, 2),
                    avgPrice = Math.Round(avgPrice, 2),
                    maxPrice = Math.Round(maxPrice, 2),
                    minPrice = Math.Round(minPrice, 2),
                    byBrand
                };

                // 1️⃣ Materialize from database
                var raw = q
                    .OrderBy(a => a.Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();   // <--- debugger friendly

                // 2️⃣ Project in memory (LINQ-to-Objects, NO warnings)
                var items = raw.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    code = a.Code,
                    description = a.Description,

                    // 🔹 VALUES WITH UNIT (already stored as string)
                    quantity = string.IsNullOrWhiteSpace(a.Quantity) ? null : a.Quantity,
                    length = string.IsNullOrWhiteSpace(a.Length) ? null : a.Length,
                    weight = string.IsNullOrWhiteSpace(a.Weight) ? null : a.Weight,

                    price = a.Price,

                    brandId = a.BrandId,
                    brandName = a.Gen_Brands?.BrandName,

                    branchId = a.BranchId,
                    branchName = a.Branches?.Name,

                    active = a.Active ?? false,
                    deleted = a.Deleted ?? false,

                    createdDate = a.CreatedDate,
                    updatedDate = a.UpdatedDate
                }).ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        summary,
                        totalItems = total
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Error loading categories.", ex);
            }
        }

        // ============================================================
        // Phase R.12 - PROFILE
        // ============================================================
        [HttpGet, Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var c = db.Gen_Catagories
                    .Include(x => x.Gen_Brands)
                    .Include(x => x.Branches)
                    .FirstOrDefault(x => x.Id == id);

                if (c == null) return JsonError("Category not found.");

                var header = new
                {
                    id = c.Id,
                    code = c.Code,
                    name = c.Name,
                    description = c.Description,
                    brandId = c.BrandId,
                    brandName = c.Gen_Brands != null ? c.Gen_Brands.BrandName : "",
                    branchId = c.BranchId,
                    branchName = c.Branches != null ? c.Branches.Name : "",
                    quantity = c.Quantity,
                    length = c.Length,
                    weight = c.Weight,
                    price = c.Price,
                    quantityUnit = c.QuantityUnit,
                    lengthUnit = c.LengthUnit,
                    weightUnit = c.WeightUnit,
                    priceUnit = c.PriceUnit,
                    active = c.Active ?? false,
                    createdDateText = c.CreatedDate.HasValue ? c.CreatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null,
                    updatedDateText = c.UpdatedDate.HasValue ? c.UpdatedDate.Value.ToString("dd-MMM-yyyy HH:mm") : null
                };

                // Sibling categories of the same brand
                var brandSiblings = (
                    from x in db.Gen_Catagories
                    where x.Id != id
                       && x.BrandId == c.BrandId
                       && (x.Deleted == false || x.Deleted == null)
                    orderby x.Id descending
                    select new
                    {
                        x.Id,
                        x.Code,
                        x.Name,
                        x.Quantity,
                        x.Price,
                        Active = x.Active ?? false
                    }
                ).Take(20).ToList();

                // Sibling categories in same branch
                var branchSiblings = (
                    from x in db.Gen_Catagories
                    where x.Id != id
                       && x.BranchId == c.BranchId
                       && (x.Deleted == false || x.Deleted == null)
                    orderby x.Id descending
                    select new
                    {
                        x.Id,
                        x.Code,
                        x.Name,
                        BrandName = x.Gen_Brands.BrandName,
                        x.Price,
                        Active = x.Active ?? false
                    }
                ).Take(20).ToList();

                decimal avgBrandPrice = brandSiblings.Any() ? brandSiblings.Average(s => s.Price ?? 0) : 0m;

                var kpis = new
                {
                    price = c.Price,
                    quantity = c.Quantity,
                    length = c.Length,
                    weight = c.Weight,
                    brandSiblings = brandSiblings.Count(),
                    branchSiblings = branchSiblings.Count(),
                    avgBrandPrice = Math.Round(avgBrandPrice, 2)
                };

                return JsonSuccess("OK", new
                {
                    header,
                    kpis,
                    brandSiblings,
                    branchSiblings
                });
            }
            catch (Exception ex)
            {
                return JsonError("Profile failed. " + ex.Message);
            }
        }



        // ---------------- LOOKUPS ----------------
        [HttpGet, Route("lookups")]
        public ActionResult Lookups()
        {
            try
            {
                var branches = db.Branches
                    .Where(b => b.Deleted == null || b.Deleted == false)
                    .Select(b => new { Id = b.Id, Name = b.Name })
                    .ToList();

                var brands = db.Gen_Brands
                    .Where(b => b.Deleted == null || b.Deleted == false)
                    .Select(b => new { Id = b.Id, Name = b.BrandName, Code = b.Code })
                    .ToList();

                var data = new
                {
                    branches,
                    brands
                };

                return JsonSuccess("Lookups loaded.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Error loading lookups.", ex);
            }
        }

        // ---------------- DETAIL ----------------
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var cat = db.Gen_Catagories
                    .Include(c => c.Gen_Brands)
                    .Include(c => c.Branches)
                    .FirstOrDefault(c => c.Id == id && (c.Deleted == null || c.Deleted == false));

                if (cat == null)
                    return JsonError("Category not found.");

                // Resolve EmployeeId for CreatedBy / UpdatedBy (like Users, Brands)
                int? createdEmpId = null;
                int? updatedEmpId = null;

                if (cat.CreatedBy.HasValue)
                {
                    createdEmpId = db.Adm_Users
                        .Where(u => u.Id == cat.CreatedBy.Value)
                        .Select(u => u.EmployeeId)
                        .FirstOrDefault();
                }

                if (cat.UpdatedBy.HasValue)
                {
                    updatedEmpId = db.Adm_Users
                        .Where(u => u.Id == cat.UpdatedBy.Value)
                        .Select(u => u.EmployeeId)
                        .FirstOrDefault();
                }

                string Fmt(DateTime? d) =>
                    d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm") : "-";

                var data = new
                {
                    Id = cat.Id,
                    Deleted = cat.Deleted ?? false,
                    Name = cat.Name,
                    Code = cat.Code,
                    Description = cat.Description,
                    Quantity = cat.Quantity,
                    Length = cat.Length,
                    Weight = cat.Weight,
                    Price = cat.Price,
                    BrandId = cat.BrandId,
                    BrandName = cat.Gen_Brands != null ? cat.Gen_Brands.BrandName : "-",
                    BranchId = cat.BranchId,
                    BranchName = cat.Branches != null ? cat.Branches.Name : "-",
                    Active = cat.Active ?? false,

                    CreatedBy = createdEmpId,
                    CreatedDateFormatted = Fmt(cat.CreatedDate),
                    UpdatedBy = updatedEmpId,
                    UpdatedDateFormatted = Fmt(cat.UpdatedDate),
                    RecordTimeStamp = Fmt(cat.RecordTimeStamp)
                };

                return JsonSuccess("Category detail loaded.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Error loading category detail.", ex);
            }
        }

        // ---------------- CREATE ----------------
        [HttpPost, Route("create")]
        public ActionResult Create()
        {
            try
            {
                var f = Request.Form;
                var userId = GetCurrentUserId();
                if (userId <= 0)
                    return JsonError("Invalid user session.");

                var now = DateTime.Now;

                // -------- required fields --------
                string name = (f["name"] ?? f["Name"])?.Trim();
                string description = (f["description"] ?? f["Description"])?.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    return JsonError("Name is required.");

                // -------- lookups --------
                int? brandId = ToIntSafe(f["brandId"] ?? f["BrandId"]);
                int? branchId = ToIntSafe(f["branchId"] ?? f["BranchId"]);

                if (!brandId.HasValue)
                    return JsonError("Brand is required.");

                if (!branchId.HasValue)
                    return JsonError("Branch is required.");

                // -------- AUTO GENERATE CODE --------
                string code = GenerateCategoryCode();

                // Extra safety check
                while (db.Gen_Catagories.Any(x =>
                    (x.Deleted == null || x.Deleted == false) &&
                    x.Code != null &&
                    x.Code.Trim() == code))
                {
                    code = GenerateCategoryCode();
                }

                // -------- create entity --------
                var category = new Gen_Catagories
                {
                    Name = name,
                    Code = code,
                    Description = string.IsNullOrWhiteSpace(description) ? null : description,

                    BrandId = brandId.Value,
                    BranchId = branchId.Value,

                    Quantity = (f["quantity"] ?? f["Quantity"])?.Trim(),
                    Length = (f["length"] ?? f["Length"])?.Trim(),
                    Weight = (f["weight"] ?? f["Weight"])?.Trim(),

                    Price = ToDecimalSafe(f["price"] ?? f["Price"]),

                    Active = ToBoolSafe(f["active"] ?? f["Active"], true),
                    Deleted = false,

                    CreatedBy = userId,
                    CreatedDate = now,
                    UpdatedBy = userId,
                    UpdatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Catagories.Add(category);
                db.SaveChanges();

                return JsonSuccess("Category created successfully.", new
                {
                    id = category.Id,
                    code = category.Code
                });
            }
            catch (Exception ex)
            {
                return JsonError("SERVER EXCEPTION", ex);
            }
        }

        // ---------------- UPDATE ----------------
        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id)
        {
            try
            {
                var category = db.Gen_Catagories.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (category == null)
                    return JsonError("Category not found.");

                var f = Request.Form;
                var userId = GetCurrentUserId();
                if (userId <= 0)
                    return JsonError("Invalid user session.");

                var now = DateTime.Now;

                string name = (f["name"] ?? f["Name"])?.Trim();
                string description = (f["description"] ?? f["Description"])?.Trim();

                if (string.IsNullOrWhiteSpace(name))
                    return JsonError("Name is required.");

                int? brandId = ToIntSafe(f["brandId"] ?? f["BrandId"]);
                int? branchId = ToIntSafe(f["branchId"] ?? f["BranchId"]);

                if (!brandId.HasValue)
                    return JsonError("Brand is required.");

                if (!branchId.HasValue)
                    return JsonError("Branch is required.");

                // Preserve existing code unless a non-empty code is explicitly sent
                string incomingCode = (f["code"] ?? f["Code"])?.Trim();
                string finalCode = string.IsNullOrWhiteSpace(incomingCode)
                    ? category.Code
                    : incomingCode;

                if (string.IsNullOrWhiteSpace(finalCode))
                    return JsonError("Category code is missing.");

                // -------- DUPLICATE CHECK (exclude same id) --------
                bool codeExists = db.Gen_Catagories.Any(x =>
                    x.Id != id &&
                    (x.Deleted == null || x.Deleted == false) &&
                    x.BranchId == branchId.Value &&
                    x.Code != null &&
                    x.Code.Trim() == finalCode);

                if (codeExists)
                    return JsonError("This Code already exists for this Branch.");

                // -------- update --------
                category.Name = name;
                category.Code = finalCode;
                category.Description = string.IsNullOrWhiteSpace(description) ? null : description;

                category.BrandId = brandId.Value;
                category.BranchId = branchId.Value;

                category.Quantity = (f["quantity"] ?? f["Quantity"])?.Trim();
                category.Length = (f["length"] ?? f["Length"])?.Trim();
                category.Weight = (f["weight"] ?? f["Weight"])?.Trim();

                category.Price = ToDecimalSafe(f["price"] ?? f["Price"]);
                category.Active = ToBoolSafe(f["active"] ?? f["Active"], true);

                category.UpdatedBy = userId;
                category.UpdatedDate = now;
                category.RecordTimeStamp = now;

                db.SaveChanges();

                return JsonSuccess("Category updated successfully.", new
                {
                    id = category.Id,
                    code = category.Code
                });
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
                var cat = db.Gen_Catagories.Find(id);
                if (cat == null)
                    return JsonError("Category not found.");

                cat.Deleted = true;
                db.SaveChanges();

                return JsonSuccess("Category deleted successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Error deleting category.", ex);
            }
        }

        private string GenerateCategoryCode()
        {
            var codes = db.Gen_Catagories
                .Where(x => x.Code != null && x.Code.StartsWith("CAT-"))
                .Select(x => x.Code)
                .ToList();

            int maxNo = 0;

            foreach (var code in codes)
            {
                if (string.IsNullOrWhiteSpace(code) || code.Length <= 4)
                    continue;

                int n;
                if (int.TryParse(code.Substring(4), out n) && n > maxNo)
                    maxNo = n;
            }

            return "CAT-" + (maxNo + 1).ToString("D4");
        }

        [HttpGet, Route("next-code")]
        public ActionResult NextCode()
        {
            try
            {
                return JsonSuccess("Next code fetched successfully.", new
                {
                    code = GenerateCategoryCode()
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to generate next category code.", ex);
            }
        }

        public ActionResult Index(
            string search = null,
            string activeFilter = null,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null,
            int page = 1,
            int pageSize = 20
        )
        {
            // 1) Base query (include Brand + Branch)
            var baseQuery = db.Gen_Catagories
                              .Include(c => c.Gen_Brands)
                              .Include(c => c.Branches)
                              .Where(c => c.Deleted != true);

            // 2) Apply filters
            if (!string.IsNullOrWhiteSpace(search))
                baseQuery = baseQuery.Where(c => c.Name.Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                baseQuery = baseQuery.Where(c => c.Active == want);
            }

            // 3) Turn it into a page
            int totalCount = baseQuery.Count();
            var pageOfCats = baseQuery
                                 .OrderBy(c => c.Name)
                                 .Skip((page - 1) * pageSize)
                                 .Take(pageSize)
                                 .ToList();
            var pagedRows = new StaticPagedList<Gen_Catagories>(
                                    pageOfCats,      // the items for this page
                                    page,            // current page number
                                    pageSize,        // items per page
                                    totalCount       // total items in the query
                                    );

            // 4) Build your dropdowns
            var brandList = new SelectList(
                db.Gen_Brands
                  .Where(b => b.Deleted != true)
                  .OrderBy(b => b.BrandName)
                  .Select(b => new { b.Id, b.BrandName }),
                "Id",
                "BrandName"
            );

            var branchList = new SelectList(
           db.Branches
             .Where(b => b.Deleted != true)
             .OrderBy(b => b.Name)
             .Select(b => new { b.Id, b.Name }),
           "Id",
           "Name"
            );

            // 3) Project into your row VM
            var rowQuery = baseQuery.Select(c => new CategoryRowViewModel
            {
                Id = c.Id,
                BrandName = c.Gen_Brands.BrandName,
                Name = c.Name,
                Code = c.Code,
                Description = c.Description,
                Quantity = c.Quantity,
                Length = c.Length,
                Weight = c.Weight,
                Price = c.Price,
                Active = c.Active ?? false,
                Deleted = c.Deleted ?? false,
                BranchName = c.Branches.Name,
                RecordTimeStamp = c.RecordTimeStamp,
                CreatedBy = c.CreatedBy,
                CreatedDate = c.CreatedDate,
                UpdatedBy = c.UpdatedBy,
                UpdatedDate = c.UpdatedDate
            });
                                            
            var vm = new CategoriesPageViewModel
            {
                Categories = pagedRows,
                NewCategory = showCreate ? new Gen_Catagories() : null,
                EditCategory = (showEdit && editId.HasValue)
                                 ? db.Gen_Catagories.Find(editId.Value)
                                 : null,
                BrandList = new SelectList(db.Gen_Brands.OrderBy(b => b.BrandName), "Id", "BrandName"),
                BranchList = new SelectList(db.Branches.OrderBy(b => b.Name), "Id", "Name"),
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter
            };
            return View(vm);
        }
        public ActionResult Details(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var category = db.Gen_Catagories
                             .Include(c => c.Gen_Brands)
                             .SingleOrDefault(c => c.Id == id.Value);

            if (category == null)
                return HttpNotFound();

            return View(category);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(CategoriesPageViewModel vm)
        {
            // repopulate your selects no matter what
            vm.BrandList = new SelectList(db.Gen_Brands.OrderBy(b => b.BrandName), "Id", "BrandName");
            vm.BranchList = new SelectList(db.Branches.OrderBy(b => b.Name), "Id", "Name");
            vm.ShowCreate = true;  // tell the Index view to open the create pane

            if (!ModelState.IsValid)
            {
                // return Index view with the vm (so the entered values + validation errors show up)
                return View("Index", vm);
            }

            var now = DateTime.UtcNow;
            int userEmpId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            var cat = new Gen_Catagories
            {
                BrandId = vm.NewCategory.BrandId,
                Name = vm.NewCategory.Name,
                Code = vm.NewCategory.Code,
                Description = vm.NewCategory.Description,
                Quantity = vm.NewCategory.Quantity,
                Length = vm.NewCategory.Length,
                Weight = vm.NewCategory.Weight,
                Price = vm.NewCategory.Price,
                BranchId = vm.NewCategory.BranchId,
                CreatedBy = userEmpId,
                CreatedDate = now,
                UpdatedBy = userEmpId,
                UpdatedDate = now,
                Active = true,
                Deleted = false,
                RecordTimeStamp = now
            };

            db.Gen_Catagories.Add(cat);
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(CategoriesPageViewModel vm)
        {
            // repopulate your selects
            vm.BrandList = new SelectList(db.Gen_Brands.OrderBy(b => b.BrandName), "Id", "BrandName");
            vm.BranchList = new SelectList(db.Branches.OrderBy(b => b.Name), "Id", "Name");
            vm.ShowEdit = true;
            vm.EditId = vm.EditCategory?.Id;

            if (!ModelState.IsValid)
            {
                // preserve the VM so the form is re-rendered with errors
                return View("Index", vm);
            }

            var existing = db.Gen_Catagories.Find(vm.EditCategory.Id);
            if (existing == null)
                return HttpNotFound();

            int userEmpId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);

            existing.BrandId = vm.EditCategory.BrandId;
            existing.Name = vm.EditCategory.Name;
            existing.Code = vm.EditCategory.Code;
            existing.Description = vm.EditCategory.Description;
            existing.Quantity = vm.EditCategory.Quantity;
            existing.Length = vm.EditCategory.Length;
            existing.Weight = vm.EditCategory.Weight;
            existing.Price = vm.EditCategory.Price;
            existing.BranchId = vm.EditCategory.BranchId;
            existing.Active = vm.EditCategory.Active;
            existing.Deleted = vm.EditCategory.Deleted;
            existing.UpdatedBy = userEmpId;
            existing.UpdatedDate = DateTime.UtcNow;
            existing.RecordTimeStamp = DateTime.UtcNow;

            db.Entry(existing).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateCategoryField(int categoryId, string fieldName, string newValue)
        {
            // 1) Auth check
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // 2) Load
            var cat = db.Gen_Catagories.Find(categoryId);
            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            // 3) Determine current user
            int userId = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"]);

            try
            {
                // 4) Only allow a small, whitelisted set of updatable fields
                switch (fieldName)
                {
                    case nameof(Gen_Catagories.Name):
                        cat.Name = newValue;
                        break;
                    case nameof(Gen_Catagories.Code):
                        cat.Code = newValue;
                        break;
                    case nameof(Gen_Catagories.Description):
                        cat.Description = newValue;
                        break;
                    case nameof(Gen_Catagories.Quantity):
                        cat.Quantity = newValue;
                        break;
                    case nameof(Gen_Catagories.Length):
                        cat.Length = newValue;
                        break;
                    case nameof(Gen_Catagories.Weight):
                        cat.Weight = newValue;
                        break;
                    case nameof(Gen_Catagories.Price):
                        cat.Price = Decimal.TryParse(newValue, out var p) ? p : (decimal?)null;
                        break;
                    default:
                        return Json(new { success = false, message = "Invalid field." });
                }

                // 5) Stamp update info
                cat.UpdatedBy = userId;
                cat.UpdatedDate = DateTime.UtcNow;
                cat.RecordTimeStamp = DateTime.UtcNow;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                // 6) Bubble up any unexpected error
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Bulk action (Activate/Deactivate/Delete) on multiple selected categories.
        /// </summary>
        [HttpPost]
        [Route("bulk-update")]
        public JsonResult BulkUpdateCategories(List<int> categoryIds, string actionType)
        {
            // 1) Determine current user via ClaimsPrincipal or Session fallback
            int userId;
            if (User is ClaimsPrincipal cp &&
                int.TryParse(cp.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            {
                userId = id;
            }
            else if (Session["EmployeeId"] is int empId)
            {
                userId = empId;
            }
            else if (Session["UserID"] is int uid)
            {
                userId = uid;
            }
            else
            {
                return Json(new { success = false, message = "Not authenticated." });
            }

            // 2) Load all targets
            var cats = db.Gen_Catagories
                         .Where(c => categoryIds.Contains(c.Id))
                         .ToList();

            foreach (var cat in cats)
            {
                switch (actionType)
                {
                    case "Activate":
                        cat.Active = true;
                        break;
                    case "Deactivate":
                        cat.Active = false;
                        break;
                    case "Delete":
                        cat.Deleted = true;
                        break;
                    default:
                        return Json(new { success = false, message = "Unknown action." });
                }

                // 3) Stamp each
                cat.UpdatedBy = userId;
                cat.UpdatedDate = DateTime.UtcNow;
                cat.RecordTimeStamp = DateTime.UtcNow;
            }

            db.SaveChanges();
            return Json(new { success = true });
        }
    
        // GET: Gen_Catagories/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Gen_Catagories gen_Catagories = db.Gen_Catagories.Find(id);
            if (gen_Catagories == null)
            {
                return HttpNotFound();
            }
            return View(gen_Catagories);
        }

        // POST: Gen_Catagories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Gen_Catagories gen_Catagories = db.Gen_Catagories.Find(id);
            db.Gen_Catagories.Remove(gen_Catagories);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        [HttpGet]
        [Obsolete]
        public ActionResult ExportCategoriesToExcel(int? id, string searchString = null, string activeFilter = null)
        {           
            if (id.HasValue)
            {
                // ==== Detail Export ====
                var cat = db.Gen_Catagories
                            .Include(c => c.Gen_Brands)
                            .Include(c => c.Branches)
                            .FirstOrDefault(c => c.Id == id.Value);

                if (cat == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Category Detail");

                    ws.Cells[1, 1].Value = "Field";
                    ws.Cells[1, 2].Value = "Value";
                    ws.Cells[1, 1, 1, 2].Style.Font.Bold = true;

                    int row = 1;
                    void Add(string field, object value)
                    {
                        row++;
                        ws.Cells[row, 1].Value = field;
                        ws.Cells[row, 2].Value = value ?? "(none)";
                    }

                    Add("ID", cat.Id);
                    Add("Name", cat.Name);
                    Add("Code", cat.Code);
                    Add("Description", cat.Description);
                    Add("Brand", cat.Gen_Brands?.BrandName);
                    Add("Quantity", cat.Quantity);
                    Add("Length", cat.Length);
                    Add("Weight", cat.Weight);
                    Add("Price", cat.Price);
                    Add("Branch", cat.Branches?.Name);
                    Add("Active", cat.Active == true ? "Yes" : "No");
                    Add("Deleted", cat.Deleted == true ? "Yes" : "No");
                    Add("CreatedBy", cat.CreatedBy);
                    Add("CreatedDate", cat.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("UpdatedBy", cat.UpdatedBy);
                    Add("UpdatedDate", cat.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Record Timestamp", cat.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"));

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    return File(pkg.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Category_{cat.Id}_Detail.xlsx");
                }
            }
            else
            {
                // ==== Full List Export ====
                var query = db.Gen_Catagories
                              .Include(c => c.Gen_Brands)
                              .Include(c => c.Branches)
                              .Where(c => c.Deleted != true);

                if (!string.IsNullOrEmpty(searchString))
                    query = query.Where(c => c.Name.Contains(searchString));

                if (!string.IsNullOrEmpty(activeFilter))
                {
                    bool want = activeFilter == "Yes";
                    query = query.Where(c => c.Active == want);
                }

                var list = query.OrderBy(c => c.Name).ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Categories");

                    var headers = new[]
                    {
                "ID", "Code", "Name", "Description", "Brand", "Quantity",
                "Length", "Weight", "Price", "Branch", "Active", "Deleted",
                "CreatedBy", "CreatedDate", "UpdatedBy", "UpdatedDate", "RecordTimeStamp"
            };
                    for (int c = 0; c < headers.Length; c++)
                    {
                        ws.Cells[1, c + 1].Value = headers[c];
                        ws.Cells[1, c + 1].Style.Font.Bold = true;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        var cat = list[i];
                        int r = i + 2;
                        ws.Cells[r, 1].Value = cat.Id;
                        ws.Cells[r, 2].Value = cat.Code;
                        ws.Cells[r, 3].Value = cat.Name;
                        ws.Cells[r, 4].Value = cat.Description;
                        ws.Cells[r, 5].Value = cat.Gen_Brands?.BrandName;
                        ws.Cells[r, 6].Value = cat.Quantity;
                        ws.Cells[r, 7].Value = cat.Length;
                        ws.Cells[r, 8].Value = cat.Weight;
                        ws.Cells[r, 9].Value = cat.Price;
                        ws.Cells[r, 10].Value = cat.Branches?.Name;
                        ws.Cells[r, 11].Value = (cat.Active ?? false) ? "Yes" : "No";
                        ws.Cells[r, 12].Value = (cat.Deleted ?? false) ? "Yes" : "No";
                        ws.Cells[r, 13].Value = cat.CreatedBy;
                        ws.Cells[r, 14].Value = cat.CreatedDate?.ToString("g");
                        ws.Cells[r, 15].Value = cat.UpdatedBy;
                        ws.Cells[r, 16].Value = cat.UpdatedDate?.ToString("g");
                        ws.Cells[r, 17].Value = cat.RecordTimeStamp?.ToString("g");
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();

                    return File(pkg.GetAsByteArray(),
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        "Categories.xlsx");
                }
            }
        }
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportCategoriesToPdf(int? id, string searchString = null, string activeFilter = null)
        {
            if (id.HasValue)
            {
                // ==== Detail PDF ====
                var cat = db.Gen_Catagories
                            .Include(c => c.Gen_Brands)
                            .Include(c => c.Branches)
                            .FirstOrDefault(c => c.Id == id.Value);
                if (cat == null) return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 80 };

                    void Cell(string txt, bool isHeader = false)
                    {
                        var cell = new PdfPCell(new Phrase(txt));
                        if (isHeader) cell.BackgroundColor = BaseColor.LIGHT_GRAY;
                        table.AddCell(cell);
                    }

                    void Row(string label, object value)
                    {
                        Cell(label);
                        Cell(value?.ToString() ?? "(none)");
                    }

                    Cell("Field", true);
                    Cell("Value", true);

                    Row("ID", cat.Id);
                    Row("Code", cat.Code);
                    Row("Name", cat.Name);
                    Row("Description", cat.Description);
                    Row("Brand", cat.Gen_Brands?.BrandName);
                    Row("Quantity", cat.Quantity);
                    Row("Length", cat.Length);
                    Row("Weight", cat.Weight);
                    Row("Price", cat.Price);
                    Row("Branch", cat.Branches?.Name);
                    Row("Active", cat.Active == true ? "Yes" : "No");
                    Row("Deleted", cat.Deleted == true ? "Yes" : "No");
                    Row("CreatedBy", cat.CreatedBy);
                    Row("CreatedDate", cat.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("UpdatedBy", cat.UpdatedBy);
                    Row("UpdatedDate", cat.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Record Time", cat.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(table);
                    doc.Close();

                    return File(ms.ToArray(), "application/pdf", $"Category_{cat.Id}_Detail.pdf");
                }
            }
            else
            {
                // ==== Index PDF ====
                var query = db.Gen_Catagories
                              .Include(c => c.Gen_Brands)
                              .Include(c => c.Branches)
                              .Where(c => c.Deleted != true);

                if (!string.IsNullOrEmpty(searchString))
                    query = query.Where(c => c.Name.Contains(searchString));

                if (!string.IsNullOrEmpty(activeFilter))
                {
                    bool want = activeFilter == "Yes";
                    query = query.Where(c => c.Active == want);
                }

                var cats = query.OrderBy(c => c.Name).ToList();
                var vm = new CategoriesPageViewModel
                {
                    Categories = new PagedList<Gen_Catagories>(cats, 1, cats.Count),
                    NewCategory = null,
                    EditCategory = null,
                    BrandList = null,
                    BranchList = null,
                    ShowCreate = false,
                    ShowEdit = false
                };

                return new Rotativa.ViewAsPdf("ExportTable", vm)
                {
                    FileName = "Categories.pdf",
                    PageSize = Rotativa.Options.Size.A4,
                    PageOrientation = Rotativa.Options.Orientation.Portrait,
                    CustomSwitches = "--print-media-type --load-error-handling ignore"
                };
            }
        }

    }
}
