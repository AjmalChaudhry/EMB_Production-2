using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using iTextSharp.text;
using iTextSharp.text.pdf;
using OfficeOpenXml;
using PagedList;
using Rotativa;

namespace EMB_Production.Controllers.Api
{
    [RoutePrefix("emb-products/gen_design")]
    public class Gen_DesignController : BaseController
    {
        [HttpGet]
        [Route("list")]
        public ActionResult GetAll(
            string search = "",
            int page = 1,
            int pageSize = 10,
            string activeFilter = "",
            int? productFilter = null,
            int? machineFilter = null,
            int? customerFilter = null,
            int? branchFilter = null,
            string sortBy = "id_desc")
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;

                // Designs are a master entity — visible across branches by default.
                // Pass branchFilter to scope to a single branch.
                var query = db.Gen_Design
                    .AsNoTracking()
                    .Where(x => x.Deleted == false || x.Deleted == null);

                if (branchFilter.HasValue && branchFilter.Value > 0)
                    query = query.Where(x => x.BranchId == branchFilter.Value);

                // SEARCH — code + DNo + Collection + ProductName
                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();

                    query = query.Where(x =>
                        (x.D_Code != null && x.D_Code.Contains(search)) ||
                        (x.DNo != null && x.DNo.Contains(search)) ||
                        (x.CollectionName != null && x.CollectionName.Contains(search)) ||
                        (x.Gen_Products != null && x.Gen_Products.Prod_Name.Contains(search)));
                }

                if (!string.IsNullOrWhiteSpace(activeFilter))
                {
                    bool isActive = activeFilter.Equals("true", StringComparison.OrdinalIgnoreCase);
                    query = query.Where(x => x.Active == isActive);
                }

                if (productFilter.HasValue && productFilter.Value > 0)
                    query = query.Where(x => x.ProductId == productFilter.Value);

                if (machineFilter.HasValue && machineFilter.Value > 0)
                    query = query.Where(x => x.MachineId == machineFilter.Value);

                if (customerFilter.HasValue && customerFilter.Value > 0)
                    query = query.Where(x => x.CustomerId == customerFilter.Value);

                // SORT
                switch ((sortBy ?? "id_desc").ToLowerInvariant())
                {
                    case "code_asc":    query = query.OrderBy(x => x.D_Code); break;
                    case "code_desc":   query = query.OrderByDescending(x => x.D_Code); break;
                    case "dno_asc":     query = query.OrderBy(x => x.DNo); break;
                    case "dno_desc":    query = query.OrderByDescending(x => x.DNo); break;
                    case "name_asc":    query = query.OrderBy(x => x.CollectionName); break;
                    case "name_desc":   query = query.OrderByDescending(x => x.CollectionName); break;
                    case "product_asc": query = query.OrderBy(x => x.Gen_Products.Prod_Name); break;
                    case "id_asc":      query = query.OrderBy(x => x.Id); break;
                    default:            query = query.OrderByDescending(x => x.Id); break;
                }

                var totalItems = query.Count();

                var pageRows = query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(x => new
                    {
                        x.Id,
                        x.D_Code,
                        x.DNo,
                        x.CollectionName,
                        x.ProductId,
                        ProductName = x.Gen_Products != null ? x.Gen_Products.Prod_Name : "",
                        ProductCode = x.Gen_Products != null ? x.Gen_Products.Prod_Code : "",
                        x.MachineId,
                        MachineName = x.Gen_Machines != null ? x.Gen_Machines.Code : "",
                        x.CustomerId,
                        CustomerName = x.Hr_Customers != null ? x.Hr_Customers.Customer_Name : "",
                        x.BranchId,
                        BranchName = x.Branches != null ? x.Branches.Name : "",
                        x.Active
                    })
                    .ToList();

                var designIds = pageRows.Select(r => r.Id).ToList();
                
                var joCounts = db.Gen_JobOrders
                    .Where(j => designIds.Contains(j.DesignId)
                             && j.Deleted != true)
                    .GroupBy(j => j.DesignId)
                    .Select(g => new { DesignId = g.Key, Count = g.Count() })
                    .ToDictionary(g => g.DesignId, g => g.Count);

                var costingMap = db.DesignCosting
                    .Where(c => designIds.Contains(c.DesignId) && c.Deleted != true)
                    .Select(c => new { c.DesignId, c.ProductCostPerUnit, c.SellingPricePerUnit })
                    .ToList()
                    .ToDictionary(c => c.DesignId, c => c);

                var componentCounts = db.Gen_Components
                    .Where(c => c.D_Code.HasValue
                             && designIds.Contains(c.D_Code.Value)
                             && c.Deleted != true)
                    .GroupBy(c => c.D_Code.Value)
                    .Select(g => new { DesignId = g.Key, Count = g.Count() })
                    .ToDictionary(g => g.DesignId, g => g.Count);

                var items = pageRows.Select(x => new
                {
                    id = x.Id,
                    d_Code = x.D_Code,
                    dNo = x.DNo,
                    collectionName = x.CollectionName,

                    productId = x.ProductId,
                    productName = x.ProductName,
                    productCode = x.ProductCode,

                    machineId = x.MachineId,
                    machineName = x.MachineName,

                    customerId = x.CustomerId,
                    customerName = x.CustomerName,

                    branchId = x.BranchId,
                    branchName = x.BranchName,

                    active = x.Active ?? false,

                    // Usage summary
                    jobOrderCount = joCounts.ContainsKey(x.Id) ? joCounts[x.Id] : 0,
                    componentCount = componentCounts.ContainsKey(x.Id) ? componentCounts[x.Id] : 0,
                    hasCosting = costingMap.ContainsKey(x.Id),
                    productCostPerUnit = costingMap.ContainsKey(x.Id)
                        ? costingMap[x.Id].ProductCostPerUnit
                        : (decimal?)null,
                    sellingPricePerUnit = costingMap.ContainsKey(x.Id)
                        ? costingMap[x.Id].SellingPricePerUnit
                        : (decimal?)null,
                    inUse = (joCounts.ContainsKey(x.Id) && joCounts[x.Id] > 0)
                            || costingMap.ContainsKey(x.Id)
                })
                .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        totalItems,
                        page,
                        pageSize,
                        totalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load designs", ex);
            }
        }

        [HttpGet]
        [Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            var d = db.Gen_Design
                .AsNoTracking()
                .Include("Hr_Customers")
                .Include("Branches")
                .Include("Gen_Products")
                .Include("Gen_Machines")
                .FirstOrDefault(x => x.Id == id && x.Deleted != true);

            if (d == null)
                return JsonError("Design not found.");

            // Audit names — friendlier than bare IDs
            string createdByName = null;
            int? createdByEmployeeId = null;
            if (d.CreatedBy.HasValue)
            {
                var u = db.Adm_Users
                    .Where(au => au.Id == d.CreatedBy.Value)
                    .Select(au => new { au.EmployeeId, au.FirstName, au.LastName })
                    .FirstOrDefault();
                if (u != null)
                {
                    createdByEmployeeId = u.EmployeeId;
                    createdByName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
                }
            }

            string updatedByName = null;
            int? updatedByEmployeeId = null;
            if (d.UpdatedBy.HasValue)
            {
                var u = db.Adm_Users
                    .Where(au => au.Id == d.UpdatedBy.Value)
                    .Select(au => new { au.EmployeeId, au.FirstName, au.LastName })
                    .FirstOrDefault();
                if (u != null)
                {
                    updatedByEmployeeId = u.EmployeeId;
                    updatedByName = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim();
                }
            }

            // Linked costing summary (one record per design)
            var costing = db.DesignCosting
                .Where(c => c.DesignId == d.Id && c.Deleted != true)
                .Select(c => new
                {
                    c.Id,
                    c.BatchQty,
                    c.MaterialCostPerUnit,
                    c.ProductionCostPerUnit,
                    c.ServiceCostPerUnit,
                    c.ProductCostPerUnit,
                    c.TotalProductCost,
                    c.SellingPricePerUnit,
                    c.MarginPercent
                })
                .FirstOrDefault();

            int jobOrderCount = db.Gen_JobOrders
                .Count(j => j.DesignId == d.Id && j.Deleted != true);

            int componentCount = db.Gen_Components
                .Count(c => c.D_Code == d.Id && c.Deleted != true);

            return JsonSuccess("Loaded successfully.", new
            {
                id = d.Id,

                d_Code = d.D_Code,
                dNo = d.DNo,
                collectionName = d.CollectionName,

                productId = d.ProductId,
                productName = d.Gen_Products != null ? d.Gen_Products.Prod_Name : "",
                productCode = d.Gen_Products != null ? d.Gen_Products.Prod_Code : "",

                machineId = d.MachineId,
                machineName = d.Gen_Machines != null ? d.Gen_Machines.Code : "",

                customerId = d.CustomerId,
                customerName = d.Hr_Customers != null ? d.Hr_Customers.Customer_Name : "",

                branchId = d.BranchId,
                branchName = d.Branches != null ? d.Branches.Name : "",

                active = d.Active ?? true,
                deleted = d.Deleted ?? false,

                createdDate = d.CreatedDate?.ToString("dd-MMM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                createdBy = createdByEmployeeId,
                createdByName,

                updatedDate = d.UpdatedDate?.ToString("dd-MMM-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                updatedBy = updatedByEmployeeId,
                updatedByName,

                // ===== USAGE / LINKED ENTITIES =====
                jobOrderCount,
                componentCount,
                inUse = jobOrderCount > 0 || costing != null,

                // ===== LINKED COSTING =====
                costing = costing == null ? null : new
                {
                    id = costing.Id,
                    batchQty = costing.BatchQty,
                    materialCostPerUnit = costing.MaterialCostPerUnit,
                    productionCostPerUnit = costing.ProductionCostPerUnit,
                    serviceCostPerUnit = costing.ServiceCostPerUnit,
                    productCostPerUnit = costing.ProductCostPerUnit,
                    totalProductCost = costing.TotalProductCost,
                    sellingPricePerUnit = costing.SellingPricePerUnit,
                    marginPercent = costing.MarginPercent
                }
            });
        }

        [HttpPost]
        [Route("create")]
        public async Task<ActionResult> Create(DesignCreateDto dto)
        {
            try
            {
                int userId = GetCurrentUserId();
                int branchId = GetCurrentBranchId();
                var now = DateTime.Now;

                if (dto == null)
                    return JsonError("Invalid request.");

                if (string.IsNullOrWhiteSpace(dto.DNo))
                    return JsonError("Design No is required.");

                if (string.IsNullOrWhiteSpace(dto.CollectionName))
                    return JsonError("Collection is required.");

                if (!dto.ProductId.HasValue)
                    return JsonError("Product is required.");

                if (!dto.MachineId.HasValue)
                    return JsonError("Machine is required.");

                // Customer is optional — the system has moved to product-based costing.
                // Validate it only if a value was actually provided.
                if (dto.CustomerId.HasValue && dto.CustomerId.Value > 0)
                {
                    bool customerExists = db.Hr_Customers.Any(c => c.Id == dto.CustomerId.Value && c.Deleted != true);
                    if (!customerExists)
                        return JsonError("Selected customer does not exist.");
                }

                // Duplicate-DNo check within the same branch
                string dnoTrim = dto.DNo.Trim();
                bool dnoExists = db.Gen_Design
                    .Any(d => d.BranchId == branchId
                           && d.Deleted != true
                           && d.DNo == dnoTrim);
                if (dnoExists)
                    return JsonError("Another active design already uses this Design No in this branch.");

                var entity = new Gen_Design
                {
                    D_Code = GenerateDesignCode(),

                    DNo = dnoTrim,
                    CollectionName = dto.CollectionName.Trim(),

                    ProductId = dto.ProductId,
                    MachineId = dto.MachineId,
                    CustomerId = (dto.CustomerId.HasValue && dto.CustomerId.Value > 0) ? dto.CustomerId : null,

                    BranchId = branchId,

                    Active = dto.Active,
                    Deleted = false,

                    CreatedBy = userId,
                    CreatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Design.Add(entity);
                await db.SaveChangesAsync();

                return JsonSuccess("Design created successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to create design.", ex);
            }
        }

        [HttpPost]
        [Route("update")]
        public async Task<ActionResult> Update(DesignUpdateDto dto)
        {
            try
            {
                int userId = GetCurrentUserId();
                var now = DateTime.Now;

                if (dto == null || dto.Id <= 0)
                    return JsonError("Invalid data.");

                var entity = db.Gen_Design
                    .FirstOrDefault(d => d.Id == dto.Id && d.Deleted != true);

                if (entity == null)
                    return JsonError("Design not found.");

                if (string.IsNullOrWhiteSpace(dto.DNo))
                    return JsonError("Design No is required.");

                if (string.IsNullOrWhiteSpace(dto.CollectionName))
                    return JsonError("Collection is required.");

                if (!dto.ProductId.HasValue)
                    return JsonError("Product is required.");

                if (!dto.MachineId.HasValue)
                    return JsonError("Machine is required.");

                // Customer optional in product-based mode — validate only if provided
                if (dto.CustomerId.HasValue && dto.CustomerId.Value > 0)
                {
                    bool customerExists = db.Hr_Customers.Any(c => c.Id == dto.CustomerId.Value && c.Deleted != true);
                    if (!customerExists)
                        return JsonError("Selected customer does not exist.");
                }

                // Duplicate-DNo check within the same branch (exclude self)
                string dnoTrim = dto.DNo.Trim();
                bool dnoExists = db.Gen_Design
                    .Any(d => d.BranchId == entity.BranchId
                           && d.Deleted != true
                           && d.Id != entity.Id
                           && d.DNo == dnoTrim);
                if (dnoExists)
                    return JsonError("Another active design already uses this Design No in this branch.");

                entity.DNo = dnoTrim;
                entity.CollectionName = dto.CollectionName.Trim();

                entity.ProductId = dto.ProductId;
                entity.MachineId = dto.MachineId;
                entity.CustomerId = (dto.CustomerId.HasValue && dto.CustomerId.Value > 0) ? dto.CustomerId : null;

                entity.Active = dto.Active;

                entity.UpdatedBy = userId;
                entity.UpdatedDate = now;
                entity.RecordTimeStamp = now;

                await db.SaveChangesAsync();

                return JsonSuccess("Design updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to update design.", ex);
            }
        }

        [HttpPost]
        [Route("delete")]
        public ActionResult Delete(int id)
        {
            try
            {
                var x = db.Gen_Design.Find(id);
                if (x == null)
                    return JsonError("Design not found");

                // Block delete when the design is referenced by active job orders or costing.
                int activeJobOrders = db.Gen_JobOrders.Count(j =>
                    j.DesignId == id && j.Deleted != true);

                bool hasCosting = db.DesignCosting.Any(c =>
                    c.DesignId == id && c.Deleted != true);

                if (activeJobOrders > 0 || hasCosting)
                {
                    var parts = new List<string>();
                    if (activeJobOrders > 0) parts.Add(activeJobOrders + " active job order(s)");
                    if (hasCosting)         parts.Add("a product costing record");
                    return JsonError(
                        "Cannot delete this design — it is still linked to " +
                        string.Join(" and ", parts) +
                        ". Remove those references first or simply mark the design Inactive.");
                }

                x.Deleted = true;
                x.Active = false;
                x.UpdatedBy = GetCurrentUserId();
                x.UpdatedDate = DateTime.Now;
                x.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                return JsonSuccess("Design deleted successfully");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete design", ex);
            }
        }

        // =====================================================
        // CLONE — duplicate a design with a fresh code + DNo suffix
        // =====================================================
        [HttpPost]
        [Route("clone/{id:int}")]
        public ActionResult Clone(int id)
        {
            try
            {
                var src = db.Gen_Design.FirstOrDefault(d => d.Id == id && d.Deleted != true);
                if (src == null)
                    return JsonError("Source design not found.");

                int userId = GetCurrentUserId();
                int branchId = GetCurrentBranchId();
                var now = DateTime.Now;

                string newDNo = src.DNo;
                int suffix = 1;
                while (db.Gen_Design.Any(d =>
                    d.BranchId == branchId && d.Deleted != true && d.DNo == newDNo))
                {
                    suffix++;
                    newDNo = src.DNo + "-COPY" + (suffix > 2 ? suffix.ToString() : "");
                }

                var clone = new Gen_Design
                {
                    D_Code = GenerateDesignCode(),
                    DNo = newDNo,
                    CollectionName = src.CollectionName,
                    ProductId = src.ProductId,
                    MachineId = src.MachineId,
                    CustomerId = src.CustomerId,
                    BranchId = branchId,
                    Active = true,
                    Deleted = false,
                    CreatedBy = userId,
                    CreatedDate = now,
                    RecordTimeStamp = now
                };

                db.Gen_Design.Add(clone);
                db.SaveChanges();

                return JsonSuccess("Design cloned successfully.", new
                {
                    id = clone.Id,
                    d_Code = clone.D_Code,
                    dNo = clone.DNo,
                    collectionName = clone.CollectionName
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to clone design.", ex);
            }
        }

        // =====================================================
        // USAGE — quick "where used" stats
        // =====================================================
        [HttpGet]
        [Route("usage/{id:int}")]
        public ActionResult Usage(int id)
        {
            try
            {
                int jobOrderCount = db.Gen_JobOrders
                    .Count(j => j.DesignId == id && j.Deleted != true);

                int costingCount = db.DesignCosting
                    .Count(c => c.DesignId == id && c.Deleted != true);

                int componentCount = db.Gen_Components
                    .Count(c => c.D_Code == id && c.Deleted != true);

                return JsonSuccess("OK", new
                {
                    jobOrderCount,
                    costingCount,
                    componentCount,
                    inUse = jobOrderCount > 0 || costingCount > 0 || componentCount > 0
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load usage info.", ex);
            }
        }


        // =====================================================
        // PHASE 2A — FILE ATTACHMENTS (PDFs / images per design)
        //   Files are stored under ~/App_Data/DesignFiles/{designId}/
        //   so they are NOT directly web-accessible — clients must
        //   hit /files/download/{fileId} to fetch them.
        // =====================================================

        private static readonly string[] _allowedDesignFileExts =
            new[] { ".pdf", ".png", ".jpg", ".jpeg", ".gif" };

        private const long MaxDesignFileBytes = 20L * 1024 * 1024; // 20 MB

        [HttpPost]
        [Route("files/upload/{designId:int}")]
        public ActionResult UploadDesignFile(int designId)
        {
            try
            {
                if (!db.Gen_Design.Any(d => d.Id == designId && d.Deleted != true))
                    return JsonError("Design not found.");

                if (Request.Files.Count == 0)
                    return JsonError("No file received.");

                var f = Request.Files[0];
                if (f == null || f.ContentLength == 0)
                    return JsonError("Empty file.");

                var ext = (Path.GetExtension(f.FileName) ?? "").ToLowerInvariant();
                if (!_allowedDesignFileExts.Contains(ext))
                    return JsonError("Unsupported file type. Allowed: PDF, PNG, JPG, JPEG, GIF.");

                if (f.ContentLength > MaxDesignFileBytes)
                    return JsonError("File too large (max 20 MB).");

                var storedName = Guid.NewGuid().ToString("N") + ext;
                var relativeFolder = "App_Data/DesignFiles/" + designId;
                var absoluteFolder = Server.MapPath("~/" + relativeFolder);
                Directory.CreateDirectory(absoluteFolder);

                var absolutePath = Path.Combine(absoluteFolder, storedName);
                f.SaveAs(absolutePath);

                var entity = new Gen_DesignFiles
                {
                    DesignId = designId,
                    FileName = storedName,
                    OriginalFileName = f.FileName,
                    ContentType = f.ContentType,
                    SizeBytes = f.ContentLength,
                    StoragePath = relativeFolder + "/" + storedName,
                    IsPrimary = false,
                    UploadedBy = GetSafeCurrentUserId(),
                    UploadedDate = DateTime.Now,
                    Deleted = false
                };

                db.Gen_DesignFiles.Add(entity);
                db.SaveChanges();

                return JsonSuccess("Uploaded.", new
                {
                    id = entity.Id,
                    fileName = entity.OriginalFileName,
                    sizeBytes = entity.SizeBytes,
                    contentType = entity.ContentType,
                    uploadedDate = entity.UploadedDate
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to upload file.", ex);
            }
        }

        [HttpGet]
        [Route("files/list/{designId:int}")]
        public ActionResult ListDesignFiles(int designId)
        {
            try
            {
                var list = db.Gen_DesignFiles
                    .AsNoTracking()
                    .Where(f => f.DesignId == designId && !f.Deleted)
                    .OrderByDescending(f => f.IsPrimary)
                    .ThenByDescending(f => f.UploadedDate)
                    .ToList()
                    .Select(f => new
                    {
                        id = f.Id,
                        fileName = f.OriginalFileName,
                        contentType = f.ContentType,
                        sizeBytes = f.SizeBytes,
                        isPrimary = f.IsPrimary,
                        uploadedBy = f.UploadedBy,
                        uploadedDate = f.UploadedDate.ToString(
                            "dd-MMM-yyyy HH:mm",
                            System.Globalization.CultureInfo.InvariantCulture)
                    })
                    .ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load files.", ex);
            }
        }

        [HttpGet]
        [Route("files/download/{fileId:int}")]
        public ActionResult DownloadDesignFile(int fileId)
        {
            var f = db.Gen_DesignFiles.FirstOrDefault(x => x.Id == fileId && !x.Deleted);
            if (f == null) return HttpNotFound();

            var absolutePath = Server.MapPath("~/" + f.StoragePath);
            if (!System.IO.File.Exists(absolutePath))
                return HttpNotFound();

            return File(
                absolutePath,
                f.ContentType ?? "application/octet-stream",
                f.OriginalFileName);
        }

        [HttpDelete]
        [Route("files/{fileId:int}")]
        public ActionResult DeleteDesignFile(int fileId)
        {
            try
            {
                var f = db.Gen_DesignFiles.FirstOrDefault(x => x.Id == fileId && !x.Deleted);
                if (f == null) return JsonError("File not found.");

                f.Deleted = true;
                db.SaveChanges();

                // best-effort disk cleanup
                try
                {
                    var absolutePath = Server.MapPath("~/" + f.StoragePath);
                    if (System.IO.File.Exists(absolutePath))
                        System.IO.File.Delete(absolutePath);
                }
                catch { /* ignore — soft-deleted row is the source of truth */ }

                return JsonSuccess("File deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to delete file.", ex);
            }
        }

        [HttpPost]
        [Route("files/set-primary/{fileId:int}")]
        public ActionResult SetPrimaryDesignFile(int fileId)
        {
            try
            {
                var f = db.Gen_DesignFiles.FirstOrDefault(x => x.Id == fileId && !x.Deleted);
                if (f == null) return JsonError("File not found.");

                var siblings = db.Gen_DesignFiles
                    .Where(x => x.DesignId == f.DesignId && x.Id != fileId && !x.Deleted && x.IsPrimary)
                    .ToList();
                foreach (var s in siblings) s.IsPrimary = false;

                f.IsPrimary = true;
                db.SaveChanges();
                return JsonSuccess("Set as primary.");
            }
            catch (Exception ex)
            {
                return JsonError("Failed to set primary file.", ex);
            }
        }


        // =====================================================
        // PHASE 2B — THREAD / MATERIAL USAGE (BOM rollup)
        //   Aggregates Gen_ComponentMaterials.QtyPerUnit across all
        //   active components of the design, grouped by RawItemId.
        //   Returned as { rows, totalQtyPerUnit }.
        // =====================================================
        [HttpGet]
        [Route("thread-usage/{designId:int}")]
        public ActionResult ThreadUsage(int designId)
        {
            try
            {
                var compIds = db.Gen_Components
                    .Where(c => c.D_Code == designId && c.Deleted != true)
                    .Select(c => c.Id)
                    .ToList();

                if (compIds.Count == 0)
                {
                    return JsonSuccess("OK", new
                    {
                        designId,
                        totalQtyPerUnit = 0m,
                        rows = new object[0]
                    });
                }

                var raw = db.Gen_ComponentMaterials
                    .AsNoTracking()
                    .Where(m => compIds.Contains(m.CompId) && !m.Deleted)
                    .Select(m => new
                    {
                        m.CompId,
                        m.RawItemId,
                        m.QtyPerUnit,
                        rawItemName = m.Gen_RawItems != null ? m.Gen_RawItems.ItemName : "",
                        rawItemType = m.Gen_RawItems != null ? m.Gen_RawItems.Type : "",
                        unitName = m.Gen_Units != null ? m.Gen_Units.UnitName : "",
                        componentName = m.Gen_Components != null ? m.Gen_Components.Name : ""
                    })
                    .ToList();

                var rows = raw
                    .GroupBy(r => new { r.RawItemId, r.rawItemName, r.rawItemType, r.unitName })
                    .Select(g => new
                    {
                        rawItemId = g.Key.RawItemId,
                        rawItemName = g.Key.rawItemName,
                        rawItemType = g.Key.rawItemType,
                        unitName = g.Key.unitName,
                        qtyPerUnit = Math.Round(g.Sum(x => x.QtyPerUnit), 4),
                        components = g.Select(x => new
                        {
                            compId = x.CompId,
                            componentName = x.componentName,
                            qty = x.QtyPerUnit
                        }).ToList()
                    })
                    .OrderBy(r => r.rawItemType)
                    .ThenBy(r => r.rawItemName)
                    .ToList();

                return JsonSuccess("OK", new
                {
                    designId,
                    totalQtyPerUnit = Math.Round(rows.Sum(r => r.qtyPerUnit), 4),
                    rows
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load thread usage.", ex);
            }
        }


        // =====================================================
        // PHASE 3 — Component versioning helpers.
        //   PRODUCTION = the editable current spec (one IsLatest=1 row per component).
        //   ORIGINAL   = the approved baseline (one IsLatest=1 row per component).
        //   Update mutates the latest PRODUCTION row in place.
        //   Save-as-Version freezes the current row and creates a new IsLatest=1
        //     row with Version=N+1 carrying the same data — explicit checkpoint.
        //   Capture-Original clones the current PRODUCTION row into an
        //     IsLatest=1 ORIGINAL row (and demotes the prior ORIGINAL if any).
        // =====================================================

        private Gen_ComponentSpecs GetLatestSpec(int componentId, string variant)
        {
            return db.Gen_ComponentSpecs
                .Where(s => s.ComponentId == componentId
                         && s.Variant == variant
                         && s.IsLatest
                         && !s.Deleted)
                .FirstOrDefault();
        }

        private static Gen_ComponentSpecs CloneSpec(Gen_ComponentSpecs src, string newVariant, int newVersion, int? userId)
        {
            return new Gen_ComponentSpecs
            {
                ComponentId = src.ComponentId,
                Version = newVersion,
                Variant = newVariant,
                IsLatest = true,
                Width = src.Width,
                Height = src.Height,
                Stitch = src.Stitch,
                Colors = src.Colors,
                MachineHead = src.MachineHead,
                Repeat = src.Repeat,
                HSpace = src.HSpace,
                UPS = src.UPS,
                HeadToHead = src.HeadToHead,
                Tilla = src.Tilla,
                Seq_A = src.Seq_A,
                Seq_B = src.Seq_B,
                Notes = src.Notes,
                CreatedBy = userId,
                CreatedDate = DateTime.Now,
                Deleted = false
            };
        }

        [HttpPost]
        [Route("components/snapshot-version/{componentId:int}")]
        public ActionResult SnapshotNewVersion(int componentId)
        {
            try
            {
                var current = GetLatestSpec(componentId, "PRODUCTION");
                if (current == null)
                    return JsonError("No current PRODUCTION spec found for this component.");

                int nextVersion = (db.Gen_ComponentSpecs
                    .Where(s => s.ComponentId == componentId && s.Variant == "PRODUCTION" && !s.Deleted)
                    .Max(s => (int?)s.Version) ?? 0) + 1;

                current.IsLatest = false;
                var fresh = CloneSpec(current, "PRODUCTION", nextVersion, GetSafeCurrentUserId());
                db.Gen_ComponentSpecs.Add(fresh);
                db.SaveChanges();

                return JsonSuccess($"Saved as V{nextVersion}.", new
                {
                    id = fresh.Id,
                    version = fresh.Version
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to snapshot version.", ex);
            }
        }

        [HttpPost]
        [Route("components/capture-original/{componentId:int}")]
        public ActionResult CaptureOriginal(int componentId)
        {
            try
            {
                var prod = GetLatestSpec(componentId, "PRODUCTION");
                if (prod == null)
                    return JsonError("No PRODUCTION spec found for this component.");

                // demote previous ORIGINAL latest, if any
                var prevOrigLatest = db.Gen_ComponentSpecs
                    .Where(s => s.ComponentId == componentId && s.Variant == "ORIGINAL" && s.IsLatest && !s.Deleted)
                    .ToList();
                foreach (var p in prevOrigLatest) p.IsLatest = false;

                int nextOrigVersion = (db.Gen_ComponentSpecs
                    .Where(s => s.ComponentId == componentId && s.Variant == "ORIGINAL" && !s.Deleted)
                    .Max(s => (int?)s.Version) ?? 0) + 1;

                var orig = CloneSpec(prod, "ORIGINAL", nextOrigVersion, GetSafeCurrentUserId());
                db.Gen_ComponentSpecs.Add(orig);
                db.SaveChanges();

                return JsonSuccess("Captured as ORIGINAL baseline.", new
                {
                    id = orig.Id,
                    version = orig.Version
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to capture original.", ex);
            }
        }

        [HttpGet]
        [Route("components/versions/{componentId:int}")]
        public ActionResult ComponentVersions(int componentId)
        {
            try
            {
                var list = db.Gen_ComponentSpecs
                    .AsNoTracking()
                    .Where(s => s.ComponentId == componentId && !s.Deleted)
                    .OrderBy(s => s.Variant)
                    .ThenByDescending(s => s.Version)
                    .ToList()
                    .Select(s => new
                    {
                        id = s.Id,
                        version = s.Version,
                        variant = s.Variant,
                        isLatest = s.IsLatest,
                        width = s.Width,
                        height = s.Height,
                        stitch = s.Stitch,
                        colors = s.Colors,
                        machineHead = s.MachineHead,
                        repeatVal = s.Repeat,
                        hSpace = s.HSpace,
                        ups = s.UPS,
                        headToHead = s.HeadToHead,
                        tilla = s.Tilla,
                        seqA = s.Seq_A,
                        seqB = s.Seq_B,
                        createdBy = s.CreatedBy,
                        createdDate = s.CreatedDate.ToString(
                            "dd-MMM-yyyy HH:mm",
                            System.Globalization.CultureInfo.InvariantCulture)
                    })
                    .ToList();

                return JsonSuccess("OK", list);
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load versions.", ex);
            }
        }

        [HttpGet]
        [Route("compare/{designId:int}")]
        public ActionResult CompareDesign(int designId)
        {
            try
            {
                // every active component for this design
                var compHeaders = db.Gen_Components
                    .AsNoTracking()
                    .Where(c => c.D_Code == designId && c.Deleted != true)
                    .OrderBy(c => c.Code)
                    .Select(c => new { c.Id, c.Code, c.Name })
                    .ToList();

                var compIds = compHeaders.Select(c => c.Id).ToList();

                var latest = db.Gen_ComponentSpecs
                    .AsNoTracking()
                    .Where(s => compIds.Contains(s.ComponentId) && s.IsLatest && !s.Deleted)
                    .ToList();

                int diffFields = 0, totalFields = 0;

                var rows = compHeaders.Select(c =>
                {
                    var prod = latest.FirstOrDefault(s => s.ComponentId == c.Id && s.Variant == "PRODUCTION");
                    var orig = latest.FirstOrDefault(s => s.ComponentId == c.Id && s.Variant == "ORIGINAL");

                    var fields = SpecComparison(prod, orig);
                    diffFields += fields.Count(f => f.changed);
                    totalFields += fields.Count;

                    int compTotal = fields.Count;
                    int compMatch = fields.Count(f => !f.changed);

                    return new
                    {
                        componentId = c.Id,
                        code = c.Code,
                        name = c.Name,
                        hasProduction = prod != null,
                        hasOriginal = orig != null,
                        productionVersion = prod?.Version,
                        originalVersion = orig?.Version,
                        matchPercent = compTotal == 0 ? 0
                            : Math.Round((decimal)compMatch * 100m / compTotal, 1),
                        fields
                    };
                }).ToList();

                decimal overallMatch = totalFields == 0 ? 100m
                    : Math.Round((decimal)(totalFields - diffFields) * 100m / totalFields, 1);

                return JsonSuccess("OK", new
                {
                    designId,
                    overallMatchPercent = overallMatch,
                    diffFields,
                    totalFields,
                    components = rows
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to compare.", ex);
            }
        }

        private static IList<dynamic> SpecComparison(Gen_ComponentSpecs prod, Gen_ComponentSpecs orig)
        {
            string F(object v) => v == null ? "" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);

            // (label, production, original)
            var pairs = new (string label, object p, object o)[]
            {
                ("Width",       prod?.Width,        orig?.Width),
                ("Height",      prod?.Height,       orig?.Height),
                ("Stitches",    prod?.Stitch,       orig?.Stitch),
                ("Colors",      prod?.Colors,       orig?.Colors),
                ("Mach.Head",   prod?.MachineHead,  orig?.MachineHead),
                ("Repeat",      prod?.Repeat,       orig?.Repeat),
                ("H-Space",     prod?.HSpace,       orig?.HSpace),
                ("UPS",         prod?.UPS,          orig?.UPS),
                ("H2H",         prod?.HeadToHead,   orig?.HeadToHead),
                ("Tilla",       prod?.Tilla,        orig?.Tilla),
                ("Seq A",       prod?.Seq_A,        orig?.Seq_A),
                ("Seq B",       prod?.Seq_B,        orig?.Seq_B),
            };

            var list = new List<dynamic>();
            foreach (var (label, p, o) in pairs)
            {
                bool changed = F(p) != F(o);
                list.Add(new
                {
                    label,
                    production = p,
                    original = o,
                    changed
                });
            }
            return list;
        }


        // =====================================================
        // PROFILE — Design + its Components with full specs.
        //   Returns one document with the design header (id/code/dno/
        //   collection/product/machine/customer/branch) and all child
        //   components including the Phase 1 spec columns.
        // =====================================================
        [HttpGet]
        [Route("profile/{id:int}")]
        public ActionResult GetProfile(int id)
        {
            try
            {
                var d = db.Gen_Design
                    .AsNoTracking()
                    .Include("Hr_Customers")
                    .Include("Branches")
                    .Include("Gen_Products")
                    .Include("Gen_Machines")
                    .FirstOrDefault(x => x.Id == id && x.Deleted != true);

                if (d == null)
                    return JsonError("Design not found.");

                // Phase 3: component header + its CURRENT spec lives in two tables.
                // Read logical rows + latest PRODUCTION spec + latest ORIGINAL spec in one
                // pass each, then merge in memory.
                var compHeaders = db.Gen_Components
                    .AsNoTracking()
                    .Where(c => c.D_Code == id && c.Deleted != true)
                    .OrderBy(c => c.Code)
                    .Select(c => new
                    {
                        id = c.Id,
                        code = c.Code,
                        name = c.Name,
                        active = c.Active ?? true
                    })
                    .ToList();

                var compIds = compHeaders.Select(c => c.id).ToList();

                var latestSpecs = db.Gen_ComponentSpecs
                    .AsNoTracking()
                    .Where(s => compIds.Contains(s.ComponentId) && s.IsLatest && !s.Deleted)
                    .ToList();

                var components = compHeaders.Select(c =>
                {
                    var prod = latestSpecs.FirstOrDefault(s => s.ComponentId == c.id && s.Variant == "PRODUCTION");
                    var orig = latestSpecs.FirstOrDefault(s => s.ComponentId == c.id && s.Variant == "ORIGINAL");

                    return new
                    {
                        id = c.id,
                        code = c.code,
                        name = c.name,
                        active = c.active,

                        // CURRENT spec — flattened so existing frontend keeps working
                        headToHead = prod != null ? prod.HeadToHead : null,
                        stitch = prod != null ? prod.Stitch : null,
                        tilla = prod != null ? prod.Tilla : null,
                        seqA = prod != null ? prod.Seq_A : null,
                        seqB = prod != null ? prod.Seq_B : null,

                        width = prod != null ? prod.Width : null,
                        height = prod != null ? prod.Height : null,
                        colors = prod != null ? prod.Colors : null,
                        machineHead = prod != null ? prod.MachineHead : null,
                        repeatVal = prod != null ? prod.Repeat : null,
                        hSpace = prod != null ? prod.HSpace : null,
                        ups = prod != null ? prod.UPS : null,

                        // Phase 3 — versioning meta
                        version = prod != null ? prod.Version : 0,
                        hasOriginal = orig != null
                    };
                }).ToList();

                return JsonSuccess("OK", new
                {
                    id = d.Id,
                    d_Code = d.D_Code,
                    dNo = d.DNo,
                    collectionName = d.CollectionName,

                    productId = d.ProductId,
                    productName = d.Gen_Products != null ? d.Gen_Products.Prod_Name : "",

                    machineId = d.MachineId,
                    machineName = d.Gen_Machines != null ? d.Gen_Machines.Code : "",

                    customerId = d.CustomerId,
                    customerName = d.Hr_Customers != null ? d.Hr_Customers.Customer_Name : "",

                    branchId = d.BranchId,
                    branchName = d.Branches != null ? d.Branches.Name : "",

                    active = d.Active ?? true,

                    componentCount = components.Count,
                    components
                });
            }
            catch (Exception ex)
            {
                return JsonError("Failed to load design profile.", ex);
            }
        }


        private string GenerateDesignCode()
        {
            // ✅ YEAR ONLY
            var year = DateTime.Now.Year.ToString();

            // Find last code for the same year
            var lastCode = db.Gen_Design
                .Where(x => x.D_Code.StartsWith("DES-" + year))
                .OrderByDescending(x => x.D_Code)
                .Select(x => x.D_Code)
                .FirstOrDefault();

            int nextSeq = 1;

            if (!string.IsNullOrEmpty(lastCode))
            {
                // Expected format: DES-YYYY-XXX
                var parts = lastCode.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out int lastSeq))
                {
                    nextSeq = lastSeq + 1;
                }
            }

            // ✅ FINAL FORMAT
            return $"DES-{year}-{nextSeq:D3}";
        }


        // GET: Gen_Design
        public ActionResult Index(
            string search = null,
            string activeFilter = null,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null,
            int page = 1,
            int pageSize = 20)
        {
            var query = db.Gen_Design
                              .Include(d => d.Branches) // Include related Branch
                              .Where(d => d.Deleted != true);

            var branchList = new SelectList(
                db.Branches.OrderBy(b => b.Name).ToList(),
                "Id", "Name");
            // Filters
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.CollectionName.Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(d => (d.Active ?? false) == want);
            }
            query = query.Where(d => d.Deleted != true)
                         .OrderBy(d => d.DNo);

            // Paging
            var paged = query.ToPagedList(page, pageSize);

            // Build VM
            var vm = new DesignPageViewModel
            {
                Designs = paged,
                NewDesign = showCreate ? new Gen_Design() : null,
                EditDesign = showEdit && editId.HasValue ? db.Gen_Design.Find(editId.Value) : null,
                BranchList = branchList,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter
            };

            return View(vm);
        }

        // GET: Gen_Design/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var design = db.Gen_Design
                           .Include(d => d.Branches)
                           .Include(d => d.Gen_Components)   // ← load related components
                           .FirstOrDefault(d => d.Id == id.Value);

            if (design == null) return HttpNotFound();
            return View(design);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(DesignPageViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                TempData["CreateErrors"] = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .ToList();

                return RedirectToAction("Index", new { showCreate = true });
            }

            int userId = GetCurrentUserId();   // ✔ MUST store UserId (FK OK)
            var now = DateTime.UtcNow;

            var d = new Gen_Design
            {
                D_Code = vm.NewDesign.D_Code,
                DNo = vm.NewDesign.DNo,
                CollectionName = vm.NewDesign.CollectionName,
                CustomerId = vm.NewDesign.CustomerId,
                Active = vm.NewDesign.Active ?? true,
                Deleted = vm.NewDesign.Deleted ?? false,
                BranchId = vm.NewDesign.BranchId,

                // AUDIT (correct values)
                CreatedBy = userId,
                CreatedDate = now,
                UpdatedBy = userId,
                UpdatedDate = now,
                RecordTimeStamp = now
            };

            db.Gen_Design.Add(d);
            db.SaveChanges();

            return RedirectToAction("Index");
        }


        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(DesignPageViewModel vm)
        {
            if (!ModelState.IsValid)
                return RedirectToAction("Index", new { showEdit = true, editId = vm.EditDesign.Id });

            var d = db.Gen_Design.Find(vm.EditDesign.Id);
            if (d == null)
                return HttpNotFound();

            int userId = GetCurrentUserId();   // ✔ Use UserId
            var now = DateTime.UtcNow;

            d.D_Code = vm.EditDesign.D_Code;
            d.DNo = vm.EditDesign.DNo;
            d.CollectionName = vm.EditDesign.CollectionName;
            d.CustomerId = vm.EditDesign.CustomerId;
            d.Active = vm.EditDesign.Active ?? d.Active;
            d.Deleted = vm.EditDesign.Deleted ?? d.Deleted;
            d.BranchId = vm.EditDesign.BranchId;

            // AUDIT
            d.UpdatedBy = userId;
            d.UpdatedDate = now;
            d.RecordTimeStamp = now;

            db.SaveChanges();

            return RedirectToAction("Index");
        }


        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateDesignField(int designId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            var design = db.Gen_Design.Find(designId);
            if (design == null)
                return Json(new { success = false, message = "Design not found." });

            var prop = typeof(Gen_Design).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                object convertedValue;

                if (targetType == typeof(string))
                    convertedValue = newValue;

                else if (targetType == typeof(int))
                    convertedValue = int.TryParse(newValue, out var i) ? (int?)i : null;

                else if (targetType == typeof(decimal))
                    convertedValue = decimal.TryParse(newValue, out var d) ? (decimal?)d : null;

                else if (targetType == typeof(bool))
                    convertedValue = (newValue ?? "").ToLowerInvariant() == "true";

                else
                    return Json(new { success = false, message = $"Unsupported type: {targetType.Name}" });

                // SET VALUE
                prop.SetValue(design, convertedValue);

                // AUDIT
                int userId = GetCurrentUserId();    // ✔ Store UserId
                var now = DateTime.UtcNow;

                design.UpdatedBy = userId;
                design.UpdatedDate = now;
                design.RecordTimeStamp = now;

                db.SaveChanges();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }



        public ActionResult Delete(int? id)
        {
            if (!id.HasValue)
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);

            var design = db.Gen_Design.Find(id.Value);
            if (design == null)
                return HttpNotFound();

            return View(design);
        }

        // POST: Gen_Design/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            var design = db.Gen_Design.Find(id);
            if (design == null)
                return HttpNotFound();

            // “Soft” delete
            design.Deleted = true;
            design.UpdatedBy = GetCurrentEmployeeId();
            design.UpdatedDate = DateTime.UtcNow;

            db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
        // -----------------------
        // 1) INDEX: EXPORT TO EXCEL
        // -----------------------

        [HttpGet]
        [Route("exporttoexcel")]
        public ActionResult ExportToExcel(string search = null, string activeFilter = null)
        {
            // 1) Build & filter your IQueryable
            var query = db.Gen_Design
                          .Include(d => d.Branches)
                          .Where(d => d.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.CollectionName.Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(d => (d.Active ?? false) == want);
            }

            // 2) Materialize into a List<Gen_Design>
            var rawList = query.OrderBy(d => d.DNo)
                               .ToList();
           
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Designs");
                var headers = new[] {
            "ID", "DCode", "DNo", "Collection", "Branch",
            "Active", "Deleted", "CreatedBy", "CreatedDate",
            "UpdatedBy", "UpdatedDate"
        };
                for (int c = 0; c < headers.Length; c++)
                {
                    ws.Cells[1, c + 1].Value = headers[c];
                    ws.Cells[1, c + 1].Style.Font.Bold = true;
                }

                // 4) Write the rows, formatting dates here
                for (int i = 0; i < rawList.Count; i++)
                {
                    var d = rawList[i];
                    int row = i + 2;
                    ws.Cells[row, 1].Value = d.Id;
                    ws.Cells[row, 2].Value = d.D_Code;
                    ws.Cells[row, 3].Value = d.DNo;
                    ws.Cells[row, 4].Value = d.CollectionName;
                    ws.Cells[row, 5].Value = d.Branches?.Name;
                    ws.Cells[row, 6].Value = (d.Active ?? false) ? "Yes" : "No";
                    ws.Cells[row, 7].Value = (d.Deleted ?? false) ? "Yes" : "No";
                    ws.Cells[row, 8].Value = d.CreatedBy;
                    // format published after materialization:
                    ws.Cells[row, 9].Value = d.CreatedDate.HasValue
                        ? d.CreatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : "(none)";
                    ws.Cells[row, 10].Value = d.UpdatedBy;
                    ws.Cells[row, 11].Value = d.UpdatedDate.HasValue
                        ? d.UpdatedDate.Value.ToString("yyyy-MM-dd HH:mm")
                        : "(none)";
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                return File(
                    pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Designs.xlsx"
                );
            }
        }
       
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(string search = null, string activeFilter = null)
        {
            // same query as above
            var query = db.Gen_Design
                          .Include(d => d.Branches)
                          .Include(d => d.Gen_Components)
                          .Where(d => d.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(d => d.CollectionName.Contains(search));
            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(d => (d.Active ?? false) == want);
            }

            var list = query.OrderBy(d => d.DNo).ToList();

            // wrap in a PagedList so Rotativa can handle it
            var vm = new DesignPageViewModel
            {
                Designs = new StaticPagedList<Gen_Design>(list, 1, list.Count, list.Count)
            };

            // Render a dedicated “ExportTable” Razor view that outputs only the <table> markup
            return new Rotativa.ViewAsPdf("ExportTable", vm)
            {
                FileName = "Designs.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait,
                CustomSwitches = "--print-media-type --load-error-handling ignore"
            };
        }

        [HttpGet, Obsolete]
        public ActionResult ExportDetailToExcel(int id)
        {
            var design = db.Gen_Design
                           .Include(d => d.Branches)
                           .Include(d => d.Gen_Components)
                           .SingleOrDefault(d => d.Id == id);
            if (design == null) return HttpNotFound();
           
            using (var pkg = new ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("DesignDetail");
                int row = 1;

                void Add(string field, object value)
                {
                    ws.Cells[row, 1].Value = field;
                    ws.Cells[row, 2].Value = value ?? "(none)";
                    row++;
                }

                // core fields
                Add("ID", design.Id);
                Add("DCode", design.D_Code);
                Add("DNo", design.DNo);
                Add("CollectionName", design.CollectionName);
                Add("Branch", design.Branches?.Name);
                Add("Active", design.Active == true ? "Yes" : "No");
                Add("Deleted", design.Deleted == true ? "Yes" : "No");
                Add("CreatedBy", design.CreatedBy);
                Add("CreatedDate", design.CreatedDate?.ToString("yyyy-MM-dd HH:mm"));
                Add("UpdatedBy", design.UpdatedBy);
                Add("UpdatedDate", design.UpdatedDate?.ToString("yyyy-MM-dd HH:mm"));

                // blank line
                row++;

                // child collection header
                ws.Cells[row, 1].Value = "Components (" + design.Gen_Components.Count + ")";
                ws.Cells[row, 1, row, 3].Merge = true;
                ws.Cells[row, 1].Style.Font.Bold = true;
                row++;

                // table headers
                ws.Cells[row, 1].Value = "ID";
                ws.Cells[row, 2].Value = "Code";
                ws.Cells[row, 3].Value = "Name";
                ws.Cells[row, 1, row, 3].Style.Font.Bold = true;
                row++;

                // components data
                foreach (var c in design.Gen_Components.OrderBy(c => c.Id))
                {
                    ws.Cells[row, 1].Value = c.Id;
                    ws.Cells[row, 2].Value = c.Code;
                    ws.Cells[row, 3].Value = c.Name;
                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                var bytes = pkg.GetAsByteArray();
                return File(bytes,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"Design_{design.Id}_Detail.xlsx");
            }
        }
        [HttpGet]
        [Route("export-detail-pdf/{id:int}")]
        public ActionResult ExportDetailToPdf(int id)
        {
            var design = db.Gen_Design
                           .Include(d => d.Branches)
                           .Include(d => d.Gen_Components)
                           .SingleOrDefault(d => d.Id == id);
            if (design == null) return HttpNotFound();

            using (var ms = new MemoryStream())
            {
                var doc = new Document(PageSize.A4, 25, 25, 30, 30);
                PdfWriter.GetInstance(doc, ms);
                doc.Open();

                // 3-column table for detail + components
                var table = new PdfPTable(3) { WidthPercentage = 90f };
                table.SetWidths(new float[] { 1f, 2f, 4f });
                table.SpacingBefore = 10f;

                // fonts
                var headerFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12f);
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10f);
                var valueFont = FontFactory.GetFont(FontFactory.HELVETICA, 10f);

                // Title row
                table.AddCell(new PdfPCell(new Phrase("Design Details", headerFont))
                {
                    Colspan = 3,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 6f
                });

                // Helper to add label+value
                void AddRow(string label, string value)
                {
                    table.AddCell(new PdfPCell(new Phrase(label, labelFont)) { Padding = 4f });
                    table.AddCell(new PdfPCell(new Phrase(value ?? "(none)", valueFont)) { Colspan = 2, Padding = 4f });
                }

                // Core fields
                AddRow("DCode", design.D_Code?.ToString());
                AddRow("DNo", design.DNo);
                AddRow("CollectionName", design.CollectionName);
                AddRow("Branch", design.Branches?.Name);
                AddRow("Active", (design.Active == true) ? "Yes" : "No");
                AddRow("Deleted", (design.Deleted == true) ? "Yes" : "No");
                AddRow("Created Date", design.CreatedDate?.ToString("yyyy-MM-dd HH:mm"));
                AddRow("Updated Date", design.UpdatedDate?.ToString("yyyy-MM-dd HH:mm"));

                // Spacer
                table.AddCell(new PdfPCell(new Phrase(" "))
                {
                    Colspan = 3,
                    Border = Rectangle.NO_BORDER,
                    FixedHeight = 8f
                });

                // Components header
                table.AddCell(new PdfPCell(new Phrase(
                    "Components (" + design.Gen_Components.Count + ")", labelFont))
                {
                    Colspan = 3,
                    BackgroundColor = BaseColor.LIGHT_GRAY,
                    Padding = 4f
                });

                // Components columns
                table.AddCell(new PdfPCell(new Phrase("ID", labelFont)) { Padding = 4f });
                table.AddCell(new PdfPCell(new Phrase("Code", labelFont)) { Padding = 4f });
                table.AddCell(new PdfPCell(new Phrase("Name", labelFont)) { Padding = 4f });

                if (design.Gen_Components.Any())
                {
                    foreach (var c in design.Gen_Components.OrderBy(c => c.Id))
                    {
                        table.AddCell(new PdfPCell(new Phrase(c.Id.ToString(), valueFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(c.Code?.ToString() ?? "", valueFont)) { Padding = 4f });
                        table.AddCell(new PdfPCell(new Phrase(c.Name ?? "", valueFont)) { Padding = 4f });
                    }
                }
                else
                {
                    table.AddCell(new PdfPCell(new Phrase("(no components)", valueFont))
                    {
                        Colspan = 3,
                        Padding = 4f,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                doc.Add(table);
                doc.Close();

                return File(ms.ToArray(),
                            "application/pdf",
                            $"Design_{design.Id}_Detail.pdf");
            }
        }

    }
}
