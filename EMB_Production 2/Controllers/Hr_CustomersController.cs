using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using EMB_Production.Models;
using EMB_Production.ViewModels;
using OfficeOpenXml;
using PagedList;
using Rotativa;

namespace EMB_Production.Controllers
{

    [RoutePrefix("emb-products/customers")]
    public class Hr_CustomersController : BaseController
    {
        // ─────────────────────────────────────────────────────────────
        //  Sprint-2  AUDIT LOG (Customers)
        // ─────────────────────────────────────────────────────────────
        private void LogCustomerAudit(int id, string action,
                                      string oldValue = null, string newValue = null,
                                      string detail = null)
        {
            try
            {
                db.Database.ExecuteSqlCommand(@"
INSERT INTO dbo.Hr_CustomersAuditLog
       (CustomerId, Action, ChangedBy, ChangedDate, OldValue, NewValue, Detail)
VALUES (@cid, @act, @cb, @when, @ov, @nv, @det)",
                    new SqlParameter("@cid",  id),
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
                var entries = db.Database.SqlQuery<CustomerAuditEntry>(@"
SELECT  a.Id, a.CustomerId, a.Action, a.ChangedBy,
        u.UserName AS ChangedByName,
        a.ChangedDate, a.OldValue, a.NewValue, a.Detail
  FROM  dbo.Hr_CustomersAuditLog a
  LEFT JOIN dbo.Adm_Users u ON u.Id = a.ChangedBy
 WHERE  a.CustomerId = @id
 ORDER BY a.Id DESC",
                    new SqlParameter("@id", id)).ToList();

                var dto = entries.Select(e => new
                {
                    id = e.Id, customerId = e.CustomerId, action = e.Action,
                    changedBy = e.ChangedBy, changedByName = e.ChangedByName,
                    changedDate = e.ChangedDate,
                    changedDateText = e.ChangedDate?.ToString("dd-MMM-yyyy HH:mm"),
                    oldValue = e.OldValue, newValue = e.NewValue, detail = e.Detail
                }).ToList();
                return Json(new { success = true, data = dto }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Failed to load audit log. " + DescribeCustomerError(ex) });
            }
        }

        private class CustomerAuditEntry
        {
            public int Id { get; set; }
            public int CustomerId { get; set; }
            public string Action { get; set; }
            public int? ChangedBy { get; set; }
            public string ChangedByName { get; set; }
            public DateTime? ChangedDate { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string Detail { get; set; }
        }

        private static string DescribeCustomerError(Exception ex)
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
        //  Sprint-3  BULK ACTIONS (Customers)
        // ─────────────────────────────────────────────────────────────
        public class CustomerBulkIdsDto { public List<int> Ids { get; set; } }

        private object DoBulkCustomer(List<int> ids, Func<int, Hr_Customers, string> runOne)
        {
            var failures = new List<object>();
            int ok = 0;
            foreach (var id in (ids ?? new List<int>()).Distinct())
            {
                var e = db.Hr_Customers.FirstOrDefault(x => x.Id == id && x.Deleted != true);
                if (e == null)
                {
                    failures.Add(new { id, code = "(missing)", reason = "Not found." });
                    continue;
                }
                string code = e.Customer_Name ?? ("CUST#" + id);
                try
                {
                    string err = runOne(id, e);
                    if (string.IsNullOrEmpty(err)) ok++;
                    else failures.Add(new { id, code, reason = err });
                }
                catch (Exception ex)
                {
                    failures.Add(new { id, code, reason = DescribeCustomerError(ex) });
                }
            }
            return new { successCount = ok, failureCount = failures.Count, failures };
        }

        [HttpPost, Route("bulk-activate")]
        public ActionResult BulkActivate(CustomerBulkIdsDto dto)
        {
            var data = DoBulkCustomer(dto?.Ids, (id, e) =>
            {
                if (e.Active == true) return "Already active.";
                e.Active = true;
                db.SaveChanges();
                LogCustomerAudit(id, "ACTIVATED", "Inactive", "Active");
                return null;
            });
            return Json(new { success = true, message = "Bulk activate completed.", data });
        }

        [HttpPost, Route("bulk-deactivate")]
        public ActionResult BulkDeactivate(CustomerBulkIdsDto dto)
        {
            var data = DoBulkCustomer(dto?.Ids, (id, e) =>
            {
                if (e.Active != true) return "Already inactive.";
                e.Active = false;
                db.SaveChanges();
                LogCustomerAudit(id, "DEACTIVATED", "Active", "Inactive");
                return null;
            });
            return Json(new { success = true, message = "Bulk deactivate completed.", data });
        }

        [HttpPost, Route("bulk-delete")]
        public ActionResult BulkDelete(CustomerBulkIdsDto dto)
        {
            var data = DoBulkCustomer(dto?.Ids, (id, e) =>
            {
                e.Deleted = true;
                e.Active = false;
                db.SaveChanges();
                LogCustomerAudit(id, "DELETED", "Active", "Deleted");
                return null;
            });
            return Json(new { success = true, message = "Bulk delete completed.", data });
        }
        // ---------------- LIST ----------------
        [HttpGet, Route("list")]
        public ActionResult List(
            string search = "",
            int page = 1,
            int pageSize = 10,
            string active = ""
        )
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;

                var q = db.Hr_Customers
                    .Include("Branches")
                    .Where(c => c.Deleted == null || c.Deleted == false);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    search = search.Trim();
                    q = q.Where(c =>
                        c.Customer_Name.Contains(search) ||
                        c.Customer_Id.Contains(search) ||
                        c.Customer_City.Contains(search) ||
                        c.Customer_Phone.Contains(search) ||
                        c.Customer_Mobile.Contains(search));
                }

                if (!string.IsNullOrWhiteSpace(active))
                {
                    bool isActive = active.Equals("true", StringComparison.OrdinalIgnoreCase);
                    q = q.Where(c => (c.Active ?? false) == isActive);
                }

                var total = q.Count();

                var items = q
                    .OrderBy(c => c.Customer_Name)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList()
                    .Select(c => new
                    {
                        id = c.Id,
                        customerId = c.Customer_Id,
                        name = c.Customer_Name,

                        customerCity = c.Customer_City,
                        customerCountry = c.Customer_Country,
                        customerEmail = c.Customer_Email,
                        customerPhone = c.Customer_Phone,
                        customerMobile = c.Customer_Mobile,

                        branchId = c.BranchId,
                        branchName = c.Branches?.Name,

                        active = c.Active ?? false,
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        items,
                        totalItems = total
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return JsonError("Error loading customers.", ex);
            }
        }



        // ---------------- DETAIL ----------------
        // The frontend sends the integer PK (Hr_Customers.Id), not the
        // customer code (Hr_Customers.Customer_Id, e.g. "C005"). The
        // route constraint `{id:int}` matches the PK form.
        [HttpGet, Route("detail/{id:int}")]
        public ActionResult Detail(int id)
        {
            try
            {
                var c = db.Hr_Customers
                    .Include("Branches")
                    .FirstOrDefault(x => x.Id == id && (x.Deleted == null || x.Deleted == false));

                if (c == null)
                    return JsonError("Customer not found.");

                // format dates
                string F(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd HH:mm") : "-";

                // resolve employeeId from Adm_Users
                int? createdByEmp = null;
                int? updatedByEmp = null;

                if (c.CreatedBy.HasValue)
                {
                    createdByEmp = db.Adm_Users
                        .Where(u => u.Id == c.CreatedBy)
                        .Select(u => u.EmployeeId)
                        .FirstOrDefault();
                }

                if (c.UpdatedBy.HasValue)
                {
                    updatedByEmp = db.Adm_Users
                        .Where(u => u.Id == c.UpdatedBy)
                        .Select(u => u.EmployeeId)
                        .FirstOrDefault();
                }

                var data = new
                {
                    id = c.Id,
                    customerId = c.Customer_Id,
                    name = c.Customer_Name,

                    ntnNumber = c.NTN_Number,
                    strnNumber = c.STRN_Number,
                    customerAddress = c.Customer_Address,

                    customerCity = c.Customer_City,
                    customerCountry = c.Customer_Country,
                    customerEmail = c.Customer_Email,
                    customerEmail2 = c.Customer_Email2,

                    customerPhone = c.Customer_Phone,
                    customerPhone2 = c.Customer_Phone2,
                    customerMobile = c.Customer_Mobile,
                    customerFax = c.Customer_Fax,

                    customerCreditLimitCap = c.Customer_Credit_Limit_CAP,
                    customerInvoicePeriod = c.Customer_Invoice_Period,

                    branchId = c.BranchId,
                    branchName = c.Branches?.Name,

                    active = c.Active ?? false,
                    deleted = c.Deleted ?? false,

                    createdBy = createdByEmp,
                    createdDateFormatted = F(c.CreatedDate),

                    updatedBy = updatedByEmp,
                    updatedDateFormatted = F(c.UpdatedDate),

                    recordTimeStamp = F(c.RecordTimeStamp)
                };

                return JsonSuccess("Customer detail loaded.", data);
            }
            catch (Exception ex)
            {
                return JsonError("Error loading detail.", ex);
            }
        }

        // ---------------- CREATE ----------------
        [HttpPost, Route("create")]
        public ActionResult Create(Hr_Customers model)
        {
            try
            {
                model.Active = model.Active ?? true;

                model.CreatedBy = GetCurrentUserId();
                model.CreatedDate = DateTime.Now;
                model.RecordTimeStamp = DateTime.Now;

                db.Hr_Customers.Add(model);
                db.SaveChanges();

                LogCustomerAudit(model.Id, "CREATED",
                    oldValue: null, newValue: "Active",
                    detail: (model.Customer_Id ?? "-") + " · " + (model.Customer_Name ?? "-"));

                return JsonSuccess("Customer created successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Create failed. " + DescribeCustomerError(ex));
            }
        }

        // ---------------- UPDATE ----------------
        [HttpPost, Route("update/{id:int}")]
        public ActionResult Update(int id, Hr_Customers model)
        {
            try
            {
                var c = db.Hr_Customers.Find(id);
                if (c == null)
                    return JsonError("Customer not found.");

                c.Customer_Id = model.Customer_Id;
                c.Customer_Name = model.Customer_Name;

                c.NTN_Number = model.NTN_Number;
                c.STRN_Number = model.STRN_Number;

                c.Customer_Address = model.Customer_Address;
                c.Customer_City = model.Customer_City;
                c.Customer_Country = model.Customer_Country;

                c.Customer_Email = model.Customer_Email;
                c.Customer_Email2 = model.Customer_Email2;

                c.Customer_Phone = model.Customer_Phone;
                c.Customer_Phone2 = model.Customer_Phone2;
                c.Customer_Mobile = model.Customer_Mobile;
                c.Customer_Fax = model.Customer_Fax;

                c.Customer_Credit_Limit_CAP = model.Customer_Credit_Limit_CAP;
                c.Customer_Invoice_Period = model.Customer_Invoice_Period;

                c.BranchId = model.BranchId;

                c.Active = model.Active;
                c.UpdatedBy = GetCurrentUserId();
                c.UpdatedDate = DateTime.Now;
                c.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogCustomerAudit(c.Id, "EDITED",
                    oldValue: null, newValue: c.Active == true ? "Active" : "Inactive",
                    detail: (c.Customer_Id ?? "-") + " · " + (c.Customer_Name ?? "-"));

                return JsonSuccess("Customer updated successfully.");
            }
            catch (Exception ex)
            {
                return JsonError("Update failed. " + DescribeCustomerError(ex));
            }
        }

        // ---------------- DELETE ----------------
        [HttpPost, Route("delete/{id:int}")]
        public ActionResult Delete(int id)
        {
            try
            {
                var c = db.Hr_Customers.Find(id);
                if (c == null) return JsonError("Customer not found.");

                c.Deleted = true;
                c.Active = false;
                c.UpdatedBy = GetCurrentUserId();
                c.UpdatedDate = DateTime.Now;
                c.RecordTimeStamp = DateTime.Now;

                db.SaveChanges();

                LogCustomerAudit(id, "DELETED",
                    oldValue: "Active", newValue: "Deleted",
                    detail: (c.Customer_Id ?? "-") + " · " + (c.Customer_Name ?? "-"));

                return JsonSuccess("Customer deleted.");
            }
            catch (Exception ex)
            {
                return JsonError("Delete failed. " + DescribeCustomerError(ex));
            }
        }
    


    private int? TryInt(string v)
        {
            if (int.TryParse(v, out var i)) return i;
            return null;
        }

        private bool TryBool(string v, bool def)
        {
            if (bool.TryParse(v, out var b)) return b;
            return def;
        }
    

        // GET: Hr_Customers/Index
        public ActionResult Index(
            string search = null,
            string activeFilter = null,
            bool showCreate = false,
            bool showEdit = false,
            int? editId = null,
            int page = 1,
            int pageSize = 20)
        {
            // 1) Build branch dropdown
            var branchItems = db.Branches
                                .Where(b => b.IsActive==true)
                                .OrderBy(b => b.Name)
                                .Select(b => new { b.Id, b.Name })
                                .ToList();

            // 2) Base query
            var q = db.Hr_Customers
                      .Include(c => c.Branches)
                      .Where(c => c.Deleted != true);

            // 3) Filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                q = q.Where(c =>
              c.Customer_Name.Contains(search) ||
              c.Customer_City.Contains(search) ||
              c.Customer_Country.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                q = q.Where(c => (c.Active ?? false) == want);
            }

            // 4) Project & page
            var paged = q
                        .OrderBy(c => c.Customer_Name)
                        .Select(c => new CustomerRowViewModel
                        {
                            Id = c.Id,
                            CustomerId = c.Customer_Id,
                            Name = c.Customer_Name,
                            NTN_Number = c.NTN_Number,
                            STRN_Number = c.STRN_Number,
                            Customer_Address = c.Customer_Address,
                            Customer_City = c.Customer_City,      
                            Customer_Country = c.Customer_Country,
                            Customer_Email = c.Customer_Email,
                            Customer_Email2 = c.Customer_Email2,
                            Customer_Phone = c.Customer_Phone,
                            Customer_Phone2 = c.Customer_Phone2,
                            Customer_Mobile = c.Customer_Mobile,
                            Customer_Fax = c.Customer_Fax,
                            Customer_Credit_Limit_CAP = c.Customer_Credit_Limit_CAP,
                            Customer_Invoice_Period = c.Customer_Invoice_Period != null
                                                ? c.Customer_Invoice_Period.ToString(): "",
                            BranchName = c.Branches != null ? c.Branches.Name : "",
                            Active = c.Active ?? false,
                            Deleted = c.Deleted ?? false,
                            CreatedBy = c.CreatedBy,
                            CreatedDate = c.CreatedDate,
                            UpdatedBy = c.UpdatedBy,
                            UpdatedDate = c.UpdatedDate
                        })
                         .ToPagedList(page, pageSize);

            // 5) Build page VM
            var vm = new CustomersPageViewModel
            {
                Customers = paged,
                NewCustomer = showCreate
                                ? new Hr_Customers { Active = true }
                                : null,
                EditCustomer = (showEdit && editId.HasValue)
                                ? db.Hr_Customers.Find(editId.Value)
                                : null,
                ShowCreate = showCreate,
                ShowEdit = showEdit,
                EditId = editId,
                Search = search,
                ActiveFilter = activeFilter,
                BranchList = new SelectList(branchItems, "Id", "Name")
            };

            return View(vm);
        }

        // POST: inline Create
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult CreateFromIndex(CustomersPageViewModel vm, string search = null, string activeFilter = null, int page = 1, int pageSize = 20)
        {
            vm.ShowCreate = true;
            // re‐populate BranchList
            var branchItems = db.Branches
                                .Where(b => b.IsActive==true)
                                .OrderBy(b => b.Name)
                                .Select(b => new { b.Id, b.Name })
                                .ToList();
            vm.BranchList = new SelectList(branchItems, "Id", "Name", vm.NewCustomer?.BranchId);

            if (!ModelState.IsValid)
                return View("Index", vm);


            int uid = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            var now = DateTime.UtcNow;

            var c = vm.NewCustomer;
            c.RecordTimeStamp = now;
            c.CreatedBy = uid;
            c.CreatedDate = now;
            c.UpdatedBy = uid;
            c.UpdatedDate = now;
            c.Deleted = false;

            db.Hr_Customers.Add(c);
            db.SaveChanges();

            return RedirectToAction("Index", new { search, activeFilter, page, pageSize });
        }

        // POST: inline Edit
        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult EditFromIndex(CustomersPageViewModel vm, string search = null, string activeFilter = null, int page = 1, int pageSize = 20)
        {
            vm.ShowEdit = true;

            if (!ModelState.IsValid)
                return RedirectToAction("Index", new
                {
                    showEdit = true,
                    editId = vm.EditCustomer.Id,
                    search,
                    activeFilter,
                    page,
                    pageSize
                });

            // 1) Load the existing entity
            var c = db.Hr_Customers.Find(vm.EditCustomer.Id);
            if (c == null) return HttpNotFound();

            // 2) Map every editable field back onto `c`
            c.Customer_Id = vm.EditCustomer.Customer_Id;
            c.Customer_Name = vm.EditCustomer.Customer_Name;
            c.NTN_Number = vm.EditCustomer.NTN_Number;
            c.STRN_Number = vm.EditCustomer.STRN_Number;
            c.Customer_Address = vm.EditCustomer.Customer_Address;
            c.Customer_City = vm.EditCustomer.Customer_City;
            c.Customer_Country = vm.EditCustomer.Customer_Country;
            c.Customer_Email = vm.EditCustomer.Customer_Email;
            c.Customer_Email2 = vm.EditCustomer.Customer_Email2;
            c.Customer_Phone = vm.EditCustomer.Customer_Phone;
            c.Customer_Phone2 = vm.EditCustomer.Customer_Phone2;
            c.Customer_Mobile = vm.EditCustomer.Customer_Mobile;
            c.Customer_Fax = vm.EditCustomer.Customer_Fax;
            c.Customer_Credit_Limit_CAP = vm.EditCustomer.Customer_Credit_Limit_CAP;
            c.Customer_Invoice_Period = vm.EditCustomer.Customer_Invoice_Period;
            c.BranchId = vm.EditCustomer.BranchId;
            c.RecordTimeStamp = vm.EditCustomer.RecordTimeStamp;
            c.Active = vm.EditCustomer.Active;
            c.Deleted = vm.EditCustomer.Deleted;

            // 3) Audit
            c.RecordTimeStamp = DateTime.UtcNow;
            c.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            c.UpdatedDate = DateTime.UtcNow;

            db.Entry(c).State = EntityState.Modified;
            db.SaveChanges();

            // 4) Go back to your paged Index
            return RedirectToAction("Index", new
            {
                search,
                activeFilter,
                page,
                pageSize
            });
        }

        [HttpPost]
        [Route("update-field")]
        public JsonResult UpdateCustomerField(int customerId, string fieldName, string newValue)
        {
            if (Session["UserID"] == null)
                return Json(new { success = false, message = "Not authenticated." });

            // 1) Load the customer
            var customer = db.Hr_Customers.Find(customerId);
            if (customer == null)
                return Json(new { success = false, message = "Customer not found." });

            // 2) Find the property on the Hr_Customers type
            var prop = typeof(Hr_Customers).GetProperty(fieldName);
            if (prop == null)
                return Json(new { success = false, message = "Invalid field." });

            try
            {
                // 3) Convert the incoming string to the correct type
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

                // 4) Apply it
                prop.SetValue(customer, converted);

                // 5) Stamp the update audit fields
                customer.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
                customer.UpdatedDate = DateTime.UtcNow;

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
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var c = db.Hr_Customers.Find(id.Value);
            if (c == null) return HttpNotFound();
            return View(c);
        }

        // GET: Delete confirmation
        public ActionResult Delete(int? id)
        {
            if (!id.HasValue) return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            var c = db.Hr_Customers.Find(id.Value);
            if (c == null) return HttpNotFound();
            return View(c);
        }

        // POST: actually delete
        [HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            var c = db.Hr_Customers.Find(id);
            if (c == null) return HttpNotFound();

            // soft-delete
            c.Deleted = true;
            c.UpdatedBy = Convert.ToInt32(Session["EmployeeId"] ?? Session["UserID"] ?? 0);
            c.UpdatedDate = DateTime.UtcNow;
            db.SaveChanges();

            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }

        [HttpGet]
        [Route("exporttoexcel")]
        public ActionResult ExportToExcel(string search = "", string activeFilter = "")
        {
            // ---------------------- QUERY ----------------------
            var query = db.Hr_Customers
                          .Include(c => c.Branches)
                          .Where(c => c.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(c =>
                    (c.Customer_Name ?? "").Contains(search) ||
                    (c.Customer_City ?? "").Contains(search) ||
                    (c.Customer_Country ?? "").Contains(search)
                );
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool wantActive = activeFilter == "Yes";
                query = query.Where(c => (c.Active ?? false) == wantActive);
            }

            // ---------------------- MATERIALIZE ----------------------
            var rows = query
                .OrderBy(c => c.Customer_Name)
                .Select(c => new
                {
                    c.Id,
                    CustomerID = c.Customer_Id,
                    Customer_Name = c.Customer_Name ?? "",
                    Customer_City = c.Customer_City ?? "",
                    Customer_Country = c.Customer_Country ?? "",
                    Branch = c.Branches != null ? c.Branches.Name : "",
                    Active = (c.Active ?? false) ? "Yes" : "No",
                    Deleted = (c.Deleted ?? false) ? "Yes" : "No",
                    c.CreatedBy,
                    c.CreatedDate,
                    c.UpdatedBy,
                    c.UpdatedDate
                })
                .ToList();

            // ---------------------- EXCEL ----------------------
            using (var pkg = new OfficeOpenXml.ExcelPackage())
            {
                var ws = pkg.Workbook.Worksheets.Add("Customers");

                string[] headers =
                {
            "ID","Cust-ID","Name","City","Country",
            "Branch","Active","Deleted",
            "Created By","Created Date","Updated By","Updated Date"
        };

                for (int col = 0; col < headers.Length; col++)
                {
                    ws.Cells[1, col + 1].Value = headers[col];
                    ws.Cells[1, col + 1].Style.Font.Bold = true;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    int rowNum = i + 2;

                    ws.Cells[rowNum, 1].Value = r.Id;
                    ws.Cells[rowNum, 2].Value = r.CustomerID;
                    ws.Cells[rowNum, 3].Value = r.Customer_Name;
                    ws.Cells[rowNum, 4].Value = r.Customer_City;
                    ws.Cells[rowNum, 5].Value = r.Customer_Country;
                    ws.Cells[rowNum, 6].Value = r.Branch;
                    ws.Cells[rowNum, 7].Value = r.Active;
                    ws.Cells[rowNum, 8].Value = r.Deleted;
                    ws.Cells[rowNum, 9].Value = r.CreatedBy;

                    // ✅ Proper Excel Date Formatting
                    if (r.CreatedDate.HasValue)
                    {
                        ws.Cells[rowNum, 10].Value = r.CreatedDate.Value;
                        ws.Cells[rowNum, 10].Style.Numberformat.Format = "dd-MMM-yyyy HH:mm";
                    }

                    ws.Cells[rowNum, 11].Value = r.UpdatedBy;

                    if (r.UpdatedDate.HasValue)
                    {
                        ws.Cells[rowNum, 12].Value = r.UpdatedDate.Value;
                        ws.Cells[rowNum, 12].Style.Numberformat.Format = "dd-MMM-yyyy HH:mm";
                    }
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                return File(
                    pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "Customers.xlsx"
                );
            }
        }

        // GET: /Hr_Customers/ExportToPdf
        [HttpGet]
        [Route("exporttopdf")]
        public ActionResult ExportToPdf(string search = null, string activeFilter = null)
        {
            // 1) Base query + filters
            var query = db.Hr_Customers
                          .Include(c => c.Branches)
                          .Where(c => c.Deleted != true);

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(c =>
                    c.Customer_Name.Contains(search) ||
                    c.Customer_City.Contains(search) ||
                    c.Customer_Country.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(activeFilter))
            {
                bool want = activeFilter == "Yes";
                query = query.Where(c => (c.Active ?? false) == want);
            }

            // 2) Project into RowViewModel & materialize
            var rows = query
                .OrderBy(c => c.Customer_Name)
                .Select(c => new CustomerRowViewModel
                {
                    Id = c.Id,
                    CustomerId = c.Customer_Id,
                    Name = c.Customer_Name,
                    Customer_City = c.Customer_City,
                    Customer_Country = c.Customer_Country,
                    BranchName = c.Branches != null ? c.Branches.Name : "",
                    Active = (c.Active ?? false),
                    Deleted = (c.Deleted ?? false),
                    CreatedBy = c.CreatedBy,
                    CreatedDate = c.CreatedDate,
                    UpdatedBy = c.UpdatedBy,
                    UpdatedDate = c.UpdatedDate
                })
                .ToList();

            // 3) Wrap in a StaticPagedList
            int total = rows.Count();
            var vm = new CustomersPageViewModel
            {
                Customers = new StaticPagedList<CustomerRowViewModel>(rows, 1, total, total),
                ShowCreate = false,
                ShowEdit = false,
                Search = null,
                ActiveFilter = null
            };

            // 4) Render PDF via Rotativa
            return new ViewAsPdf("ExportCustomersTable", vm)
            {
                FileName = "Customers.pdf",
                PageSize = Rotativa.Options.Size.A4,
                PageOrientation = Rotativa.Options.Orientation.Portrait,
                CustomSwitches = "--print-media-type --load-error-handling ignore"
            };
        }
    }
}
