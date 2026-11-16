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
using System.Collections.Generic;
using System.Text;
using EMB_Production.Infrastructure;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/products")]
    public class Gen_ProductsController : BaseController

    {
        private JsonNetResult JsonNet(object data)
        {
            return new JsonNetResult
            {
                Data = data,
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                ContentEncoding = Encoding.UTF8
            };
        }
        // ================================================================
        // LIST (PAGED) – PRODUCT MASTER (NO STOCK)
        // ================================================================
        [HttpGet, Route("listPaged")]
        public ActionResult ListPaged(string search = "", int page = 1, int pageSize = 10)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 10 : pageSize;

            var q = db.Gen_Products
                      .Include(x => x.Gen_Catagories)
                      .Include(x => x.Branches)
                      .Where(x => x.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                q = q.Where(x =>
                    x.Prod_Name.Contains(search) ||
                    x.Prod_Code.Contains(search));
            }

            var total = q.Count();

            var items = q
                .OrderByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    id = x.Id,

                    prodName = x.Prod_Name,
                    prodCode = x.Prod_Code,
                    prodDescription = x.Prod_Description,

                    catId = x.CatId,
                    branchId = x.BranchId,

                    categoryName = x.Gen_Catagories != null ? x.Gen_Catagories.Name : "",
                    branchName = x.Branches != null ? x.Branches.Name : "",

                    active = x.Active ?? true
                })
                .ToList();

            return JsonNet(new
            {
                success = true,
                total,
                items
            });
        }

        // ================================================================
        // DETAIL – PRODUCT MASTER
        // ================================================================
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var raw =
                (from p in db.Gen_Products

                 join c in db.Gen_Catagories on p.CatId equals c.Id into cj
                 from c in cj.DefaultIfEmpty()

                 join b in db.Branches on p.BranchId equals b.Id into bj
                 from b in bj.DefaultIfEmpty()

                 join cu in db.Adm_Users on p.CreatedBy equals cu.Id into cuj
                 from cu in cuj.DefaultIfEmpty()

                 join uu in db.Adm_Users on p.UpdatedBy equals uu.Id into uuj
                 from uu in uuj.DefaultIfEmpty()

                 where p.Id == id && p.Deleted != true

                 select new
                 {
                     p.Id,
                     p.CatId,
                     p.BranchId,

                     CategoryName = c.Name,
                     BranchName = b.Name,

                     p.Prod_Name,
                     p.Prod_Code,
                     p.Prod_Description,

                     Active = p.Active ?? true,
                     Deleted = p.Deleted ?? false,

                     CreatedByEmployeeId = cu.EmployeeId,
                     UpdatedByEmployeeId = uu.EmployeeId,

                     p.CreatedDate,
                     p.UpdatedDate,
                     p.RecordTimeStamp
                 })
                .FirstOrDefault();

            if (raw == null)
                return JsonNet(new { success = false, message = "Product not found." });

            var data = new ProductDetailViewModel
            {
                Id = raw.Id,
                CatId = raw.CatId,
                BranchId = raw.BranchId,

                CategoryName = raw.CategoryName,
                BranchName = raw.BranchName,

                ProdName = raw.Prod_Name,
                ProdCode = raw.Prod_Code,
                ProdDescription = raw.Prod_Description,

                Active = raw.Active,
                Deleted = raw.Deleted,

                CreatedByEmployeeId = raw.CreatedByEmployeeId,
                UpdatedByEmployeeId = raw.UpdatedByEmployeeId,

                CreatedDateText = raw.CreatedDate?.ToString("dd-MMM-yyyy HH:mm"),
                UpdatedDateText = raw.UpdatedDate?.ToString("dd-MMM-yyyy HH:mm"),
                RecordTimeStamp = raw.RecordTimeStamp
            };

            return JsonNet(new { success = true, data });
        }

        // ================================================================
        // CREATE
        // ================================================================
        [HttpPost, Route("create")]
        public ActionResult Create(ProductDetailViewModel vm)
        {
            if (vm == null)
                return JsonNet(new { success = false, message = "Invalid data." });

            if (string.IsNullOrWhiteSpace(vm.ProdName))
                return JsonNet(new { success = false, message = "Product name is required." });

            if (!vm.CatId.HasValue || vm.CatId.Value <= 0)
                return JsonNet(new { success = false, message = "Category is required." });

            if (!vm.BranchId.HasValue || vm.BranchId.Value <= 0)
                return JsonNet(new { success = false, message = "Branch is required." });

            int userId = GetCurrentUserId();
            var now = DateTime.Now;

            string newCode = GenerateProductCode();

            // Extra safety check
            while (db.Gen_Products.Any(x => x.Prod_Code == newCode && x.Deleted != true))
            {
                newCode = GenerateProductCode();
            }

            var p = new Gen_Products
            {
                CatId = vm.CatId,
                BranchId = vm.BranchId,
                Prod_Name = vm.ProdName.Trim(),
                Prod_Code = newCode, // ✅ auto generated
                Prod_Description = string.IsNullOrWhiteSpace(vm.ProdDescription)
                    ? null
                    : vm.ProdDescription.Trim(),
                Active = vm.Active,
                Deleted = false,

                CreatedBy = userId,
                CreatedDate = now,
                UpdatedBy = userId,
                UpdatedDate = now,
                RecordTimeStamp = now
            };

            db.Gen_Products.Add(p);
            db.SaveChanges();

            return JsonNet(new
            {
                success = true,
                message = "Product created successfully.",
                data = new
                {
                    id = p.Id,
                    prodCode = p.Prod_Code
                }
            });
        }

        // ================================================================
        // UPDATE
        // ================================================================
        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id, ProductDetailViewModel vm)
        {
            if (vm == null)
                return JsonNet(new { success = false, message = "Invalid data." });

            var p = db.Gen_Products.FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (p == null)
                return JsonNet(new { success = false, message = "Product not found." });

            if (string.IsNullOrWhiteSpace(vm.ProdName))
                return JsonNet(new { success = false, message = "Product name is required." });

            if (!vm.CatId.HasValue || vm.CatId.Value <= 0)
                return JsonNet(new { success = false, message = "Category is required." });

            if (!vm.BranchId.HasValue || vm.BranchId.Value <= 0)
                return JsonNet(new { success = false, message = "Branch is required." });

            int userId = GetCurrentUserId();
            var now = DateTime.Now;

            string incomingCode = string.IsNullOrWhiteSpace(vm.ProdCode)
                ? p.Prod_Code
                : vm.ProdCode.Trim();

            // Unique code check excluding current record
            if (!string.IsNullOrWhiteSpace(incomingCode))
            {
                bool codeExists = db.Gen_Products.Any(x =>
                    x.Prod_Code == incomingCode &&
                    x.Id != id &&
                    x.Deleted != true);

                if (codeExists)
                {
                    return JsonNet(new
                    {
                        success = false,
                        message = $"Product Code '{incomingCode}' already exists."
                    });
                }
            }

            p.CatId = vm.CatId;
            p.BranchId = vm.BranchId;
            p.Prod_Name = vm.ProdName.Trim();
            p.Prod_Code = incomingCode; // ✅ preserve existing if blank from UI
            p.Prod_Description = string.IsNullOrWhiteSpace(vm.ProdDescription)
                ? null
                : vm.ProdDescription.Trim();
            p.Active = vm.Active;

            p.UpdatedBy = userId;
            p.UpdatedDate = now;
            p.RecordTimeStamp = now;

            db.SaveChanges();

            return JsonNet(new
            {
                success = true,
                message = "Product updated successfully.",
                data = new
                {
                    id = p.Id,
                    prodCode = p.Prod_Code
                }
            });
        }

        // ================================================================
        // SOFT DELETE
        // ================================================================
        [HttpPost, Route("delete")]
        public ActionResult Delete(int id)
        {
            var p = db.Gen_Products.FirstOrDefault(x => x.Id == id && x.Deleted != true);
            if (p == null)
                return JsonNet(new { success = false, message = "Not found." });

            var now = DateTime.UtcNow;
            int userId = GetCurrentUserId();

            p.Deleted = true;
            p.UpdatedBy = userId;
            p.UpdatedDate = now;
            p.RecordTimeStamp = now;

            db.SaveChanges();

            return JsonNet(new { success = true });
        }

        [HttpGet, Route("categories")]
        public ActionResult Categories()
        
        {
            var list = db.Gen_Catagories
                .Where(x => x.Active == true)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name
                })
                .ToList();

            return JsonNet(list);
        }

        [HttpGet, Route("branches")]
        public ActionResult Branches()
        {
            var list = db.Branches
                .Where(x => x.IsActive == true)
                .OrderBy(x => x.Name)
                .Select(x => new
                {
                    id = x.Id,
                    name = x.Name
                })
                .ToList();

            return JsonNet(list);
        }

        [HttpGet, Route("products-for-purchase")]
        public ActionResult ProductsForPurchase()
        {
            var list = db.Gen_Products
            .Where(p => p.Active == true && p.Deleted != true)
            .Select(p => new
            {
                p.Id,
                ProductName = p.Prod_Name,

                CategoryId = p.CatId,  
                
                Rate =
                    p.Price ??
                    (p.Gen_Catagories != null ? p.Gen_Catagories.Price : 0m)
            })
            .ToList();

            return Json(new
            {
                success = true,
                data = list
            }, JsonRequestBehavior.AllowGet);
        }

        private string GenerateProductCode()
        {
            var codes = db.Gen_Products
                .Where(x => x.Prod_Code != null && x.Prod_Code.StartsWith("PRD-"))
                .Select(x => x.Prod_Code)
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

            return "PRD-" + (maxNo + 1).ToString("D4");
        }

        [HttpGet]
        [Route("next-code")]
        public ActionResult GetNextCode()
        {
            var code = GenerateProductCode();
            return Json(new { success = true, data = new { code } }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("finished-products")]
        public ActionResult FinishedProducts()
        {
            try
            {
                var finishedCategoryIds = new[] { 50, 51, 52 };

                var data = db.Gen_Products
                    .Where(x =>
                        x.Deleted != true &&
                        x.Active == true &&
                        x.CatId.HasValue &&
                        finishedCategoryIds.Contains(x.CatId.Value))
                    .OrderBy(x => x.Prod_Name)
                    .Select(x => new
                    {
                        id = x.Id,
                        code = x.Prod_Code,
                        name = x.Prod_Name
                    })
                    .ToList();

                return JsonSuccess("Finished products fetched successfully.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load finished products.", ex);
            }
        }

        // ================================================================
        // MVC INDEX (LEGACY – FIXED AUDIT)
        // ================================================================
        public ActionResult Index(string search = null, string activeFilter = null, int page = 1)
        {
            var q = db.Gen_Products
                      .Include(x => x.Gen_Catagories)
                      .Include(x => x.Branches)
                      .Where(x => x.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(x => x.Prod_Name.Contains(search) || x.Prod_Code.Contains(search));

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                q = q.Where(x => (x.Active ?? false) == want);
            }

            var paged = q.OrderBy(x => x.Prod_Name)
                         .Select(x => new ProductRowViewModel
                         {
                             Id = x.Id,
                             Prod_Name = x.Prod_Name,
                             Prod_Code = x.Prod_Code,
                             CategoryName = x.Gen_Catagories.Name,
                             BranchName = x.Branches.Name,
                             Price = x.Price,
                             Active = x.Active ?? false
                         })
                         .ToPagedList(page, 20);

            return View(new ProductsPageViewModel { Products = paged });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    


        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(
            ProductsPageViewModel vm,
            string search = null,
            string activeFilter = null,
            int page = 1,
            int pageSize = 20)
        {
            vm.ShowCreate = true;
            // repopulate dropdowns
            var catItems = db.Gen_Catagories.OrderBy(c => c.Name)
                                 .Select(c => new { c.Id, c.Name }).ToList();
            var branchItems = db.Branches.Where(b => b.IsActive == true)
                                 .Select(b => new { b.Id, b.Name }).ToList();
            vm.CategoryList = new SelectList(catItems, "Id", "Name", vm.NewProduct?.CatId);
            vm.BranchList = new SelectList(branchItems, "Id", "Name", vm.NewProduct?.BranchId);

            if (!ModelState.IsValid)
                return View("Index", vm);

            // audit & save
            // CreatedBy/UpdatedBy must hold an Adm_Users.Id (per Detail(), which
            // joins p.CreatedBy/p.UpdatedBy against Adm_Users.Id), not an
            // EmployeeId, so resolve via the same helper the rest of this
            // controller uses.
            var now = DateTime.UtcNow;
            int userId = GetCurrentUserId();

            var p = vm.NewProduct;
            p.CreatedBy = userId;
            p.CreatedDate = now;
            p.UpdatedBy = userId;
            p.UpdatedDate = now;
            p.RecordTimeStamp = now;
            p.Deleted = false;

            db.Gen_Products.Add(p);
            db.SaveChanges();


            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(ProductsPageViewModel vm,string search = null,
                string activeFilter = null,int page = 1,int pageSize = 20)
        {
            vm.ShowEdit = true;
            if (!ModelState.IsValid)
                return RedirectToAction("Index", new
                {
                    showEdit = true,
                    editId = vm.EditProduct.Id,
                    search,
                    activeFilter,
                    page,
                    pageSize
                });

            var p = db.Gen_Products.Find(vm.EditProduct.Id);
            if (p == null) return HttpNotFound();

            // map only editable fields
            p.CatId = vm.EditProduct.CatId;
            p.Prod_Name = vm.EditProduct.Prod_Name;
            p.Prod_Code = vm.EditProduct.Prod_Code;
            p.Prod_Description = vm.EditProduct.Prod_Description;
            p.Prod_Qty = vm.EditProduct.Prod_Qty;
            p.Prod_Length = vm.EditProduct.Prod_Length;
            p.Prod_Weight = vm.EditProduct.Prod_Weight;
            p.Prod_DateIn = vm.EditProduct.Prod_DateIn;
            p.Prod_DateOut = vm.EditProduct.Prod_DateOut;
            p.Price = vm.EditProduct.Price;
            p.BranchId = vm.EditProduct.BranchId;
            p.Active = vm.EditProduct.Active;
            p.Deleted = vm.EditProduct.Deleted;
            p.UpdatedBy = GetCurrentUserId();
            p.UpdatedDate = DateTime.UtcNow;
            p.RecordTimeStamp = DateTime.UtcNow;

            db.Entry(p).State = EntityState.Modified;
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateProductField(int productId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            var p = db.Gen_Products.Find(productId);
            if (p == null)
                return Json(new { success = false, message = "Product not found." });

            int userId = Convert.ToInt32(Session["UserID"]);

            try
            {
                switch (fieldName)
                {
                    case "Prod_Qty":
                        if (!int.TryParse(newValue, out var qty))
                            return Json(new { success = false, message = "Quantity must be an integer." });
                        p.Prod_Qty = qty;
                        break;

                    case "Prod_Length":
                        if (!decimal.TryParse(newValue, out var len))
                            return Json(new { success = false, message = "Length must be a decimal." });
                        p.Prod_Length = len;
                        break;

                    case "Prod_Weight":
                        if (!decimal.TryParse(newValue, out var wt))
                            return Json(new { success = false, message = "Weight must be a decimal." });
                        p.Prod_Weight = wt;
                        break;

                    // … other cases …

                    default:
                        return Json(new { success = false, message = "Invalid field." });
                }

                p.UpdatedBy = userId;
                p.UpdatedDate = DateTime.UtcNow;
                p.RecordTimeStamp = DateTime.UtcNow;

                db.SaveChanges();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // POST: Gen_Products/BulkUpdateProduct
        [HttpPost]
        [Route("bulk-update")]
        public JsonResult BulkUpdateProduct(List<int> productIds, string actionType)
        {
            // 1) Determine operator
            int userId;
            var cp = User as System.Security.Claims.ClaimsPrincipal;
            var idClaim = cp?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (idClaim != null && int.TryParse(idClaim.Value, out userId))
            {
                // ok
            }
            else if (Session["UserID"] != null && int.TryParse(Session["UserID"].ToString(), out userId))
            {
                // ok
            }
            else
            {
                return Json(new { success = false, message = "Not authenticated." });
            }

            // 2) Load products
            var list = db.Gen_Products
                         .Where(p => productIds.Contains(p.Id))
                         .ToList();
            if (!list.Any())
                return Json(new { success = false, message = "No matching products." });

            // 3) Apply action
            foreach (var p in list)
            {
                switch (actionType)
                {
                    case "Activate":
                        p.Active = true;
                        break;
                    case "Deactivate":
                        p.Active = false;
                        break;
                    case "Delete":
                        p.Deleted = true;
                        break;
                    default:
                        return Json(new { success = false, message = "Unknown action." });
                }

                p.UpdatedBy = userId;
                p.UpdatedDate = DateTime.UtcNow;
                p.RecordTimeStamp = DateTime.UtcNow;
            }

            db.SaveChanges();
            return Json(new { success = true });
        }
        // GET: Details
        public ActionResult Details(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var product = db.Gen_Products
                            .Include(p => p.Gen_Catagories)
                            .Include(p => p.Gen_Stocks)
                            .Include(p => p.Branches)
                            .FirstOrDefault(p => p.Id == id.Value);
            if (product == null) return HttpNotFound();
            return View(product);
        }

        [HttpGet]
        [Route("delete-modal/{id:int?}")]
        public ActionResult Delete(int? id)
        {
            if (id == null)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            // load the product with its nav‐props so your view can display Category, Branch, Stocks, etc.
            var product = db.Gen_Products
                            .Include(p => p.Gen_Catagories)
                            .Include(p => p.Branches)
                            .Include(p => p.Gen_Stocks)
                            .FirstOrDefault(p => p.Id == id.Value);
            if (product == null)
                return HttpNotFound();

            return View(product);
        }
        // POST: DeleteConfirmed
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var p = db.Gen_Products.Find(id);
            if (p == null)
                return HttpNotFound();

            var now = DateTime.UtcNow;
            int userId = GetCurrentUserId();

            p.Deleted = true;
            p.UpdatedBy = userId;
            p.UpdatedDate = now;
            p.RecordTimeStamp = now;

            db.SaveChanges();
            return RedirectToAction("Index");
        }
        
        [HttpGet, Obsolete]       
        public ActionResult ExportToExcel(int? id, string search = null, string activeFilter = null)
        {
           
            if (id.HasValue)
            {
                
                var p = db.Gen_Products
                          .Include(x => x.Gen_Catagories)
                          .Include(x => x.Branches)
                          .Include(x => x.Gen_Stocks)
                          .FirstOrDefault(x => x.Id == id.Value);
                if (p == null) return HttpNotFound();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("ProductDetail");
                    // Headers
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

                    Add("ID", p.Id);
                    Add("Name", p.Prod_Name);
                    Add("Code", p.Prod_Code);
                    Add("Description", p.Prod_Description);
                    Add("Qty", p.Prod_Qty);
                    Add("Length", p.Prod_Length);
                    Add("Weight", p.Prod_Weight);
                    Add("Date In", p.Prod_DateIn?.ToString("yyyy-MM-dd"));
                    Add("Date Out", p.Prod_DateOut?.ToString("yyyy-MM-dd"));
                    Add("Price", p.Price);
                    Add("Category", p.Gen_Catagories?.Name);
                    Add("Branch", p.Branches?.Name);
                    Add("Deleted", p.Deleted == true ? "Yes" : "No");
                    Add("Active", p.Active == true ? "Yes" : "No");
                    Add("Created Date", p.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Updated Date", p.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Add("Record Timestamp", p.RecordTimeStamp?.ToString("yyyy-MM-dd HH:mm:ss"));

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();

                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                $"Product_{p.Id}_Detail.xlsx");
                }
            }
            else
            {
                // -- list export --
                var query = db.Gen_Products.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(x => x.Prod_Name.Contains(search)
                                          || x.Prod_Code.Contains(search));
                if (!string.IsNullOrEmpty(activeFilter))
                {
                    bool want = activeFilter == "Yes";
                    query = query.Where(x => (x.Active ?? false) == want);
                }

                var list = query
                           .Include(x => x.Gen_Catagories)
                           .Include(x => x.Branches)
                           .Where(x => x.Deleted != true)
                           .OrderBy(x => x.Prod_Name)
                           .ToList();

                using (var pkg = new ExcelPackage())
                {
                    var ws = pkg.Workbook.Worksheets.Add("Products");

                    var headers = new[] {
                    "ID","Name","Code","Category","Branch",
                    "Price","Active","Deleted"
                };
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
                        ws.Cells[r, 2].Value = x.Prod_Name;
                        ws.Cells[r, 3].Value = x.Prod_Code;
                        ws.Cells[r, 4].Value = x.Gen_Catagories?.Name;
                        ws.Cells[r, 5].Value = x.Branches?.Name;
                        ws.Cells[r, 6].Value = x.Price;
                        ws.Cells[r, 7].Value = x.Active == true ? "Yes" : "No";
                        ws.Cells[r, 8].Value = x.Deleted == true ? "Yes" : "No";
                    }

                    ws.Cells[ws.Dimension.Address].AutoFitColumns();
                    var bytes = pkg.GetAsByteArray();
                    return File(bytes,
                                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                                "Products.xlsx");
                }
            }
        }

        // GET: /Gen_Products/ExportToPdf
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(int? id, string search = null, string activeFilter = null)
        {
            
            if (id.HasValue)
            {
                var p = db.Gen_Products
                          .Include(x => x.Gen_Catagories)
                          .Include(x => x.Branches)
                          .FirstOrDefault(x => x.Id == id.Value && x.Deleted != true);

                if (p == null)
                    return HttpNotFound();

                using (var ms = new MemoryStream())
                {
                    var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                    PdfWriter.GetInstance(doc, ms);
                    doc.Open();

                    var table = new PdfPTable(2) { WidthPercentage = 85 };

                    void Header(string txt)
                    {
                        var c = new PdfPCell(new Phrase(txt))
                        {
                            BackgroundColor = BaseColor.LIGHT_GRAY
                        };
                        table.AddCell(c);
                    }

                    void Row(string field, object value)
                    {
                        table.AddCell(new Phrase(field));
                        table.AddCell(new Phrase(value?.ToString() ?? "(none)"));
                    }

                    Header("Field");
                    Header("Value");

                    Row("ID", p.Id);
                    Row("Name", p.Prod_Name);
                    Row("Code", p.Prod_Code);
                    Row("Description", p.Prod_Description);
                    Row("Quantity", p.Prod_Qty);
                    Row("Length", p.Prod_Length);
                    Row("Weight", p.Prod_Weight);
                    Row("Date In", p.Prod_DateIn?.ToString("yyyy-MM-dd"));
                    Row("Date Out", p.Prod_DateOut?.ToString("yyyy-MM-dd"));
                    Row("Price", p.Price);
                    Row("Category", p.Gen_Catagories?.Name);
                    Row("Branch", p.Branches?.Name);
                    Row("Active", p.Active == true ? "Yes" : "No");
                    Row("Created Date", p.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                    Row("Updated Date", p.UpdatedDate?.ToString("yyyy-MM-dd HH:mm:ss"));

                    doc.Add(table);
                    doc.Close();

                    return File(
                        ms.ToArray(),
                        "application/pdf",
                        $"Product_{p.Id}_Detail.pdf"
                    );
                }
            }

            // =========================================================
            // 🔹 PRODUCT LIST PDF (Rotativa)
            // =========================================================
            var query = db.Gen_Products
                          .Include(x => x.Gen_Catagories)
                          .Include(x => x.Branches)
                          .Where(x => x.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.Prod_Name.Contains(search) ||
                    x.Prod_Code.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(x => (x.Active ?? false) == want);
            }

            var rows = query
                .OrderBy(x => x.Prod_Name)
                .Select(x => new ProductRowViewModel
                {
                    Id = x.Id,
                    Prod_Name = x.Prod_Name,
                    Prod_Code = x.Prod_Code,
                    CategoryName = x.Gen_Catagories.Name,
                    BranchName = x.Branches.Name,
                    Price = x.Price,
                    Active = x.Active ?? false
                })
                .ToList();

            int total = rows.Count;   // ✅ FIX

            var vm = new ProductsPageViewModel
            {
                Products = new StaticPagedList<ProductRowViewModel>(
                    rows,
                    1,
                    total,
                    total
                ),
                ShowCreate = false,
                ShowEdit = false,
                Search = search,
                ActiveFilter = activeFilter
            };

            return new ViewAsPdf("ExportProductsTable", vm)
            {
                FileName = "Products_List.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait,
                CustomSwitches = "--print-media-type --load-error-handling ignore"
            };
        }

    }
}
