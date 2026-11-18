using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;

namespace EMB_Production.Controllers
{
    public abstract class BaseController : Controller
    {
        protected readonly EMB_ProductionEntities1 db = new EMB_ProductionEntities1();

        // ================================================================
        // EMPLOYEE ID (DISPLAY PURPOSE ONLY)
        // ================================================================
        protected int? GetCurrentEmployeeId()
        {
            try
            {
                if (Session?["EmployeeId"] != null &&
                    int.TryParse(Session["EmployeeId"].ToString(), out int emp) &&
                    emp > 0)
                {
                    return emp;
                }

                // Identity.Name is the user's email (AuthController.Login /
                // HomeController.Login call SetAuthCookie(user.EmailAddress, ...)) —
                // match EmailAddress first, UserName as a fallback.
                var identityName = User?.Identity?.Name ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(identityName))
                {
                    var user = db.Adm_Users.FirstOrDefault(u => u.EmailAddress == identityName)
                           ?? db.Adm_Users.FirstOrDefault(u => u.UserName == identityName);

                    if (user != null && user.EmployeeId.HasValue)
                    {
                        Session["EmployeeId"] = user.EmployeeId.Value;
                        return user.EmployeeId.Value;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        protected int? GetEmployeeId(int? userId)
        {
            if (!userId.HasValue)
                return null;

            return db.Adm_Users
                     .Where(u => u.Id == userId.Value)
                     .Select(u => (int?)u.EmployeeId)
                     .FirstOrDefault();
        }

        protected int GetCurrentUserId()
        {
            // 1) Session cache. "UserID" (capital D) is the key AuthController.Login
            // and HomeController.Login actually set, and what every controller's
            // own inline `Session["UserID"]` check reads. This used to read a
            // differently-cased key ("UserId"), which never matched anything —
            // every call fell straight through to the fallback below.
            var sessionVal = Session?["UserID"];
            if (sessionVal != null &&
                int.TryParse(sessionVal.ToString(), out int cachedId) &&
                cachedId > 0 &&
                db.Adm_Users.Any(u => u.Id == cachedId))
            {
                return cachedId;
            }

            // 2) Resolve from the Forms Auth identity. SetAuthCookie(...) is called
            // with the user's EmailAddress (AuthController.Login / HomeController.Login),
            // so User.Identity.Name is an email, not UserName — match EmailAddress
            // first, then fall back to UserName/normalized for safety.
            var identityName = (User?.Identity?.Name ?? string.Empty).Trim();

            if (!string.IsNullOrEmpty(identityName))
            {
                // Optional normalization if identity ever comes back as "DOMAIN\user"
                var normalized = identityName.Contains("\\")
                    ? identityName.Substring(identityName.LastIndexOf("\\", StringComparison.Ordinal) + 1)
                    : identityName;

                var user = db.Adm_Users.FirstOrDefault(u => u.EmailAddress == identityName)
                       ?? db.Adm_Users.FirstOrDefault(u => u.UserName == identityName)
                       ?? db.Adm_Users.FirstOrDefault(u => u.UserName == normalized);

                if (user != null && user.Id > 0)
                {
                    Session["UserID"] = user.Id; // ✅ cache REAL Adm_Users.Id
                    return user.Id;
                }
            }

            // 3) Safe fallback: pick an existing admin/system user. With (1) and (2)
            // fixed, this should now be unreachable for any real, authenticated
            // request — if it ever fires, log it loudly instead of silently
            // mis-attributing the action to an arbitrary user.
            System.Diagnostics.Trace.TraceError(
                "BaseController.GetCurrentUserId(): could not resolve the current user from " +
                "session or identity — falling back to the default user. SessionUserID=" +
                (Session?["UserID"] ?? "(null)") + ", IdentityName=" + (User?.Identity?.Name ?? "(null)"));

            var fallback = db.Adm_Users
                .Where(u => u.Id > 0)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .FirstOrDefault();

            if (fallback > 0)
            {
                Session["UserID"] = fallback;
                return fallback;
            }

            // 4) Absolute last resort (should never happen if Adm_Users has data)
            throw new InvalidOperationException("No users exist in Adm_Users. Cannot resolve CreatedBy/UpdatedBy.");
        }


        protected int GetSafeCurrentUserId()
        {
            int uid = GetCurrentUserId();

            if (!db.Adm_Users.Any(u => u.Id == uid))
                throw new Exception("Invalid UserId for audit. User not found.");

            return uid;
        }


        protected int GetCurrentBranchId()
        {
            // 1️⃣ From session (preferred)
            if (Session?["BranchId"] != null)
                return Convert.ToInt32(Session["BranchId"]);

            // 2️⃣ From logged-in user. Identity.Name is the user's email
            // (AuthController.Login / HomeController.Login) — match EmailAddress
            // first, UserName as a fallback.
            var identityName = User?.Identity?.Name;
            if (!string.IsNullOrEmpty(identityName))
            {
                var user = db.Adm_Users.FirstOrDefault(u => u.EmailAddress == identityName)
                       ?? db.Adm_Users.FirstOrDefault(u => u.UserName == identityName);
                if (user?.BranchId != null)
                {
                    Session["BranchId"] = user.BranchId;
                    return user.BranchId.Value;
                }
            }

            // 3️⃣ Final fallback (default branch)
            return db.Branches
                .Where(b => b.IsActive == true && b.Deleted != true)
                .Select(b => b.Id)
                .FirstOrDefault();
        }


        // =================================================================
        // HELPERS
        // =================================================================
        protected int? ToInt(object input)
        {
            if (input == null) return null;
            if (int.TryParse(input.ToString(), out int v)) return v;
            return null;
        }

        protected bool ToBool(object input)
        {
            if (input == null) return false;
            var s = input.ToString().Trim().ToLower();
            return s == "true" || s == "1" || s == "yes" || s == "on";
        }

        protected string ToFormattedDate(DateTime? date)
        {
            return date?.ToString("yyyy-MM-dd HH:mm") ?? "-";
        }

        // =================================================================
        // JSON HELPERS
        // =================================================================
        protected JsonResult JsonSuccess(string message, object data = null)
        {
            return Json(new { success = true, message, data },
                JsonRequestBehavior.AllowGet);
        }

        protected JsonResult JsonError(string message, Exception ex = null)
        {
            return Json(new { success = false, message, detail = ex?.Message },
                JsonRequestBehavior.AllowGet);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }

        protected DateTime? ParsePurchaseDate(object value)
        {
            if (value == null)
                return null;

            // Case 1: already DateTime
            if (value is DateTime dt)
                return dt;

            var str = value.ToString();

            // Case 2: ISO string (Angular default)
            if (DateTime.TryParse(str, out var parsed))
                return parsed;

            // Case 3: /Date(1766162701030)/
            if (str.StartsWith("/Date("))
            {
                var digits = new string(str.Where(char.IsDigit).ToArray());
                if (long.TryParse(digits, out var ms))
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }

            return null;
        }

        // ================= COMMON ERP HELPERS =================

        protected object ValidateFinishedStockBalance(int productId,int branchId,decimal newQty,
                                             string saleType, int? excludeDispatchItemId = null)
        {
            if (productId <= 0)
            {
                return new
                {
                    success = false,
                    message = "Product is required."
                };
            }

            if (newQty <= 0)
            {
                return new
                {
                    success = false,
                    message = "Dispatch quantity must be greater than zero."
                };
            }

            saleType = string.IsNullOrWhiteSpace(saleType)
                ? "CUSTOMER_DEMAND"
                : saleType.Trim().ToUpper();

            decimal availableQty = saleType == "DIRECT_SALE"
                ? GetFreeCompanyStockBalance(productId, branchId)
                : GetCustomerReservedStockBalance(productId, branchId);

            var q = db.DispatchItems.Where(x =>
                x.ProductId == productId &&
                x.BranchId == branchId &&
                x.Deleted != true &&
                x.Dispatch.IsPosted != true &&
                x.Dispatch.IsReversed != true);

            if (excludeDispatchItemId.HasValue)
                q = q.Where(x => x.Id != excludeDispatchItemId.Value);

            decimal draftReservedQty = q
                .Select(x => (decimal?)x.DispatchQty)
                .DefaultIfEmpty(0)
                .Sum() ?? 0m;

            decimal remaining = availableQty - draftReservedQty;

            if (remaining < newQty)
            {
                return new
                {
                    success = false,
                    message =
                        saleType == "DIRECT_SALE"
                            ? $"Dispatch qty exceeds free company stock. Available={availableQty}, Draft={draftReservedQty}, Remaining={remaining}."
                            : $"Dispatch qty exceeds customer reserved stock. Available={availableQty}, Draft={draftReservedQty}, Remaining={remaining}."
                };
            }

            return new { success = true };
        }

        
        protected object ValidateQcApprovalForDispatch(int dispatchId)
        {
            var dispatch = db.Dispatch.FirstOrDefault(x =>
                x.Id == dispatchId &&
                x.Deleted != true);

            if (dispatch == null)
            {
                return new
                {
                    success = false,
                    message = "Dispatch not found."
                };
            }

            var items = db.DispatchItems
                .Where(x =>
                    x.DispatchId == dispatchId &&
                    x.Deleted != true)
                .ToList();

            if (!items.Any())
            {
                return new
                {
                    success = false,
                    message = "No items found in dispatch."
                };
            }

            foreach (var item in items)
            {
                if (!item.ProductId.HasValue || item.ProductId.Value <= 0)
                {
                    return new
                    {
                        success = false,
                        message = "Invalid product found in dispatch items."
                    };
                }

                bool hasFinishedStock = db.Gen_Stocks.Any(s =>
                    s.Deleted != true &&
                    s.StockStage == "FINISHED" &&
                    s.BranchId == dispatch.BranchId &&
                    s.TransactionType == "IN" &&
                    s.ReferenceType == "QC_INSPECTION" &&
                    (
                        s.ProdId == item.ProductId.Value ||
                        s.ItemId == item.ProductId.Value
                    ));

                if (!hasFinishedStock)
                {
                    return new
                    {
                        success = false,
                        message = $"Finished stock / QC posted stock not found for product ID {item.ProductId.Value}."
                    };
                }
            }

            return new { success = true };
        }

        protected decimal GetCustomerReservedStockBalance(int productId, int branchId)
        {
            decimal customerIn, customerOut, freeIn, freeOut;
            ComputeStockBuckets(productId, branchId,
                out customerIn, out customerOut, out freeIn, out freeOut);

            return Math.Max(0m, customerIn - customerOut);
        }

        protected decimal GetFreeCompanyStockBalance(int productId, int branchId)
        {
            decimal customerIn, customerOut, freeIn, freeOut;
            ComputeStockBuckets(productId, branchId,
                out customerIn, out customerOut, out freeIn, out freeOut);

            return Math.Max(0m, freeIn - freeOut);
        }

        // ================================================================
        // THREAD STOCK — Phase E: threads share the Gen_Store ledger
        // via Gen_Threads.RawItemId. If unlinked, returns 0.
        // ================================================================
        protected decimal GetThreadStockBalance(int threadId, int branchId)
        {
            int? rawId = db.Gen_Threads
                .Where(t => t.Id == threadId && t.Deleted != true)
                .Select(t => t.RawItemId)
                .FirstOrDefault();

            if (!rawId.HasValue || rawId.Value <= 0)
                return 0m;

            var rows = db.Gen_Store
                .Where(x =>
                    x.Deleted == false &&
                    x.ItemId == rawId.Value &&
                    x.BranchId == branchId);

            decimal totalIn  = rows.Select(x => (decimal?)x.InQty).DefaultIfEmpty(0m).Sum() ?? 0m;
            decimal totalOut = rows.Select(x => (decimal?)x.OutQty).DefaultIfEmpty(0m).Sum() ?? 0m;

            return totalIn - totalOut;
        }

        /// <summary>
        /// Splits Gen_Stocks IN/OUT for a product+branch into customer-reserved and free buckets.
        /// OUT rows are routed by DISPATCH.SaleType (CUSTOMER_DEMAND vs DIRECT_SALE) so a direct
        /// sale drains free stock and a customer-demand sale drains customer-reserved stock.
        /// </summary>
        protected void ComputeStockBuckets(int productId, int branchId,
            out decimal customerIn, out decimal customerOut,
            out decimal freeIn, out decimal freeOut)
        {
            var rows = db.Gen_Stocks
                .Where(x =>
                    x.Deleted != true &&
                    x.StockStage == "FINISHED" &&
                    x.BranchId == branchId &&
                    (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED") &&
                    (
                        (x.ProdId.HasValue && x.ProdId.Value == productId) ||
                        (x.ItemId.HasValue && x.ItemId.Value == productId)
                    ))
                .ToList();

            var dispatchIds = rows
                .Where(s => s.ReferenceType == "DISPATCH" && s.ReferenceId.HasValue)
                .Select(s => s.ReferenceId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, string> dispatchSaleTypeMap;
            if (dispatchIds.Count > 0)
            {
                dispatchSaleTypeMap = db.Dispatch
                    .Where(d => dispatchIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.SaleType })
                    .ToList()
                    .ToDictionary(d => d.Id, d => (d.SaleType ?? "").Trim().ToUpper());
            }
            else
            {
                dispatchSaleTypeMap = new Dictionary<int, string>();
            }

            customerIn = 0m; customerOut = 0m;
            freeIn = 0m;     freeOut = 0m;

            foreach (var s in rows)
            {
                decimal inQty = s.InQty ?? 0m;
                decimal outQty = s.OutQty ?? 0m;

                if (inQty > 0)
                {
                    if (IsCustomerReservedStock(s))
                        customerIn += inQty;
                    else
                        freeIn += inQty;
                }

                if (outQty > 0)
                {
                    string refType = (s.ReferenceType ?? "").Trim().ToUpper();
                    bool isCustomerDrain = false;

                    if (refType == "DISPATCH" && s.ReferenceId.HasValue)
                    {
                        string st;
                        if (dispatchSaleTypeMap.TryGetValue(s.ReferenceId.Value, out st))
                            isCustomerDrain = (st == "CUSTOMER_DEMAND");
                    }
                    else if (refType == "QC_INSPECTION" && IsCustomerReservedStock(s))
                    {
                        isCustomerDrain = true;
                    }

                    if (isCustomerDrain)
                        customerOut += outQty;
                    else
                        freeOut += outQty;
                }
            }
        }

        /// <summary>
        /// Weighted-average material cost per unit for a product at a branch, derived
        /// from FINISHED-stage Gen_Stocks IN rows. Returns 0 if no costed stock exists.
        /// </summary>
        protected decimal GetProductMaterialPerUnitCost(int productId, int branchId)
        {
            if (productId <= 0) return 0m;

            var rows = db.Gen_Stocks
                .Where(x => x.Deleted != true
                         && x.StockStage == "FINISHED"
                         && x.BranchId == branchId
                         && (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED")
                         && ((x.ProdId.HasValue && x.ProdId.Value == productId)
                             || (x.ItemId.HasValue && x.ItemId.Value == productId)))
                .ToList();

            decimal totalCostAmount = 0m;
            decimal totalCostQty = 0m;

            foreach (var s in rows)
            {
                decimal inQty = s.InQty ?? 0m;
                if (inQty <= 0m) continue;

                decimal rate = s.LastKnownRate ?? s.Rate ?? 0m;
                if (rate <= 0m && s.Amount.HasValue)
                    rate = s.Amount.Value / inQty;

                if (rate > 0m)
                {
                    totalCostAmount += inQty * rate;
                    totalCostQty += inQty;
                }
            }

            return totalCostQty > 0m ? totalCostAmount / totalCostQty : 0m;
        }

        /// <summary>
        /// Per-unit service cost for a product, combining two layers:
        ///   1) Per-product Service Rate Charge_Fector (Gen_ServiceRates).
        ///   2) Catalog Service rates (Gen_TheServices.Service_RateCharge) for every
        ///      Service that belongs to a Service Type referenced by the product's rates.
        /// The two layers are summed and added to material cost in the stock screens.
        /// </summary>
        protected decimal GetProductServicePerUnitCost(int productId, int branchId)
        {
            if (productId <= 0) return 0m;

            // Layer 1 — per-product Service Rate charge factors
            var rates = db.Gen_ServiceRates
                .Where(r => r.ProductId == productId
                            && r.BranchId == branchId
                            && r.Active == true
                            && (r.Deleted == false || r.Deleted == null))
                .Select(r => new { r.Charge_Fector, r.Service_Id })
                .ToList();

            decimal total = 0m;
            var typeIds = new HashSet<int>();

            foreach (var rc in rates)
            {
                if (!string.IsNullOrWhiteSpace(rc.Charge_Fector))
                {
                    decimal v;
                    if (decimal.TryParse(rc.Charge_Fector, out v))
                        total += v;
                }

                if (rc.Service_Id.HasValue && rc.Service_Id.Value > 0)
                    typeIds.Add(rc.Service_Id.Value);
            }

            // Layer 2 — Services in the same branch whose Service_Type matches any of
            // the product's Service Rate types.
            if (typeIds.Count > 0)
            {
                var typeList = typeIds.ToList();

                decimal catalogSum = db.Gen_TheServices
                    .Where(s => s.Service_Type.HasValue
                             && typeList.Contains(s.Service_Type.Value)
                             && s.BranchId == branchId
                             && s.Active == true
                             && (s.Deleted == false || s.Deleted == null))
                    .Sum(s => (decimal?)s.Service_RateCharge) ?? 0m;

                total += catalogSum;
            }

            return total;
        }

        /// <summary>
        /// BOM-based material cost per unit of a design. Walks every component
        /// linked to the design, then every Gen_ComponentMaterials BOM line
        /// under those components, valuing each line at the raw item's latest
        /// known purchase rate from Gen_Store. Used as a fallback for design
        /// costing when no finished stock exists yet (i.e. before any QC).
        /// </summary>
        protected decimal GetDesignBomMaterialPerUnitCost(int designId, int branchId)
        {
            if (designId <= 0) return 0m;

            var componentIds = db.Gen_Components
                .Where(c => c.D_Code == designId
                         && (c.Deleted == false || c.Deleted == null))
                .Select(c => c.Id)
                .ToList();

            if (componentIds.Count == 0) return 0m;

            var bomLines = db.Gen_ComponentMaterials
                .Where(cm => componentIds.Contains(cm.CompId) && cm.Deleted == false)
                .Select(cm => new { cm.RawItemId, cm.QtyPerUnit })
                .ToList();

            if (bomLines.Count == 0) return 0m;

            decimal total = 0m;
            foreach (var line in bomLines)
            {
                decimal rate = GetRawItemLatestRate(line.RawItemId, branchId);
                total += line.QtyPerUnit * rate;
            }

            return total;
        }

        /// <summary>
        /// Most recent non-zero purchase rate for a raw item, scoped to a
        /// branch when available. Falls back to any branch if the current
        /// branch has no rate yet.
        /// </summary>
        protected decimal GetRawItemLatestRate(int rawItemId, int branchId)
        {
            if (rawItemId <= 0) return 0m;

            decimal rate = db.Gen_Store
                .Where(s => s.ItemId == rawItemId
                         && s.BranchId == branchId
                         && s.Deleted == false
                         && s.Rate.HasValue
                         && s.Rate.Value > 0m)
                .OrderByDescending(s => s.Id)
                .Select(s => (decimal?)s.Rate)
                .FirstOrDefault() ?? 0m;

            if (rate > 0m) return rate;

            rate = db.Gen_Store
                .Where(s => s.ItemId == rawItemId
                         && s.Deleted == false
                         && s.Rate.HasValue
                         && s.Rate.Value > 0m)
                .OrderByDescending(s => s.Id)
                .Select(s => (decimal?)s.Rate)
                .FirstOrDefault() ?? 0m;

            return rate;
        }

        protected bool IsCustomerReservedStock(Gen_Stocks s)
        {
            if (s == null)
                return false;

            if (!string.Equals(s.ReferenceType, "QC_INSPECTION", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!s.ReferenceId.HasValue)
                return false;

            var qc = db.QC_Inspection
                .Include("WorkOrders")
                .FirstOrDefault(x =>
                    x.Id == s.ReferenceId.Value &&
                    (x.Deleted == false || x.Deleted == null));

            return qc != null &&
                   qc.WorkOrders != null &&
                   qc.WorkOrders.CustomerId.HasValue &&
                   qc.WorkOrders.CustomerId.Value > 0;
        }

        protected bool CanPostDispatch()
            {
                int roleId = GetCurrentRoleId();

                // 🔐 Adjust IDs according to your Roles table
                // Example:
                // 1 = Admin
                // 2 = StoreManager
                return roleId == 1 || roleId == 2;
            }

            protected int GetCurrentRoleId()
            {
                try
                {
                    // 1️⃣ Session (fast path)
                    if (Session?["RoleId"] != null)
                        return Convert.ToInt32(Session["RoleId"]);

                    // 2️⃣ Resolve from logged-in user
                    var username = User?.Identity?.Name;
                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        var user = db.Adm_Users.FirstOrDefault(u => u.UserName == username);
                        if (user?.RoleId != null)
                        {
                            Session["RoleId"] = user.RoleId.Value;
                            return user.RoleId.Value;
                        }
                    }
                }
                catch
                {
                    // swallow and return 0
                }

                return 0;
            }
          
            protected ActionResult JsonError(string message)
            {
                return Json(new
                {
                    success = false,
                    message
                }, JsonRequestBehavior.AllowGet);
            }
                     
            protected int? ToIntSafe(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return null;

                int v;
                return int.TryParse(s.Trim(), out v) ? v : (int?)null;
            }

            protected decimal? ToDecimalSafe(string s)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return null;

                decimal v;
                return decimal.TryParse(s.Trim(), out v) ? v : (decimal?)null;
            }

            protected bool ToBoolSafe(string s, bool defaultValue = false)
            {
                if (string.IsNullOrWhiteSpace(s))
                    return defaultValue;

                s = s.Trim().ToLowerInvariant();

                if (s == "true" || s == "1" || s == "yes" || s == "on")
                    return true;

                if (s == "false" || s == "0" || s == "no" || s == "off")
                    return false;

                return defaultValue;
            }        
    }
}
