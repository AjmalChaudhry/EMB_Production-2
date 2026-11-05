using System;
using System.Linq;
using System.Web.Mvc;
using System.Web.Security;
using EMB_Production.Models;
using EMB_Production.ViewModels;


namespace EMB_Production.Controllers
{
    [RoutePrefix("emb-products/auth")]
    public class AuthController : Controller
    {
        private readonly EMB_ProductionEntities1 db = new EMB_ProductionEntities1();

        // ================== REQUEST MODELS ==================
        public class LoginRequest
        {
            public string Email { get; set; }
            public string Password { get; set; }
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
        }

        // ================== DASHBOARD ==================
        [HttpGet]
        [Route("dashboard")]
        public ActionResult Dashboard()
        {
            try
            {
                DateTime today = DateTime.Today;
                DateTime tomorrow = today.AddDays(1);

                // =====================================================
                // Production Summary
                // =====================================================

                int activeJobOrders = db.Gen_JobOrders.Count(x =>
                    x.Deleted != true &&
                    x.Active == true &&
                    x.IsPosted == true);

                int runningWorkOrders = db.WorkOrders.Count(x =>
                    x.Deleted != true &&
                    x.Active == true &&
                    x.IsPosted == true &&
                    x.Status != "Completed");

                decimal todayProductionQty = db.DailyProduction
                    .Where(x =>
                        x.Deleted != true &&
                        x.CreatedDate >= today &&
                        x.CreatedDate < tomorrow &&
                        x.IsReversed != true)
                    .Sum(x => (decimal?)x.AcceptedQty) ?? 0m;

                decimal plannedQtyToday = db.DailyProduction
                    .Where(x =>
                        x.Deleted != true &&
                        x.CreatedDate >= today &&
                        x.CreatedDate < tomorrow &&
                        x.IsReversed != true)
                    .Sum(x => (decimal?)x.PlannedQty) ?? 0m;

                decimal dailyEfficiency = plannedQtyToday > 0
                    ? Math.Round((todayProductionQty / plannedQtyToday) * 100m, 2)
                    : 0m;

                // =====================================================
                // QC Summary
                // =====================================================

                decimal pendingQcQty = db.QC_Inspection
                .Where(x =>
                    x.Deleted != true &&
                    x.Status != "Reversed" &&
                    x.IsPosted != true)
                .ToList()
                .Sum(x =>
                {
                    decimal batch = x.BatchQty ?? 0m;
                    decimal inspected = x.InspectedQty ?? 0m;

                    decimal pending = batch - inspected;

                    return pending > 0 ? pending : 0m;
                });

                decimal inspectedQty = db.QC_Inspection
                    .Where(x =>
                        x.Deleted != true &&
                        x.Status != "Reversed")
                    .Sum(x => (decimal?)x.InspectedQty) ?? 0m;

                decimal passedQty = db.QC_Inspection
                    .Where(x =>
                        x.Deleted != true &&
                        x.Status != "Reversed")
                    .Sum(x => (decimal?)x.PassedQty) ?? 0m;

                decimal failedQty = db.QC_Inspection
                    .Where(x =>
                        x.Deleted != true &&
                        x.Status != "Reversed")
                    .Sum(x => (decimal?)x.FailedQty) ?? 0m;

                decimal reworkQty = db.QC_Inspection
                    .Where(x =>
                        x.Deleted != true &&
                        x.Status != "Reversed")
                    .Sum(x => (decimal?)x.ReworkQty) ?? 0m;

                int reversedQcCount = db.QC_Inspection.Count(x =>
                    x.Deleted != true &&
                    x.Status == "Reversed");

                decimal passRatio = inspectedQty > 0
                    ? Math.Round((passedQty / inspectedQty) * 100m, 2)
                    : 0m;

                decimal rejectRatio = inspectedQty > 0
                    ? Math.Round((failedQty / inspectedQty) * 100m, 2)
                    : 0m;

                // =====================================================
                // Finished Stock
                // =====================================================

                var finishedStockQuery = db.Gen_Stocks.Where(x =>
                    x.Deleted != true &&
                    x.StockStage == "FINISHED" &&
                    (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED"));

                decimal finishedStockInQty =
                    finishedStockQuery.Sum(x => (decimal?)x.InQty) ?? 0m;

                decimal finishedStockOutQty =
                    finishedStockQuery.Sum(x => (decimal?)x.OutQty) ?? 0m;

                // Draft dispatches haven't written OUT rows to Gen_Stocks yet
                // (that happens on Post), but they HAVE earmarked finished
                // stock. The Stock module subtracts them — do the same here so
                // the dashboard KPI matches the Stock page.
                decimal draftDispatchQty = db.DispatchItems
                    .Where(di => di.Deleted != true
                              && di.Dispatch.Deleted != true
                              && di.Dispatch.IsPosted != true
                              && di.Dispatch.IsReversed != true)
                    .Sum(di => (decimal?)di.DispatchQty) ?? 0m;

                decimal finishedStockQty =
                    finishedStockInQty - finishedStockOutQty - draftDispatchQty;

                if (finishedStockQty < 0m) finishedStockQty = 0m;
                // =====================================================
                // Dispatch Summary
                // =====================================================

                decimal dispatchQty = db.Gen_Stocks
                    .Where(x =>
                        x.Deleted != true &&
                        x.StockStage == "FINISHED" &&
                        (x.ItemType == "PRODUCT" || x.ItemType == "FINISHED") &&
                        x.TransactionType == "OUT" &&
                        x.ReferenceType == "DISPATCH")
                    .Sum(x => (decimal?)x.OutQty) ?? 0m;

                // =====================================================
                // Machine Utilization
                // =====================================================

                int machineCount = db.Gen_Machines.Count(x =>
                    x.Deleted != true &&
                    x.Active == true);

                int activeMachinesToday = db.DailyProduction
                    .Where(x =>
                        x.Deleted != true &&
                        x.CreatedDate >= today &&
                        x.CreatedDate < tomorrow &&
                        x.MachineId != null)
                    .Select(x => x.MachineId)
                    .Distinct()
                    .Count();

                decimal machineUtilization = machineCount > 0
                    ? Math.Round(((decimal)activeMachinesToday / machineCount) * 100m, 2)
                    : 0m;

                // =====================================================
                // Operator Performance
                // =====================================================

                int activeOperatorsToday = db.DailyProduction
                    .Where(x =>
                        x.Deleted != true &&
                        x.CreatedDate >= today &&
                        x.CreatedDate < tomorrow &&
                        x.OperatorId != null)
                    .Select(x => x.OperatorId)
                    .Distinct()
                    .Count();

                // =====================================================
                // Workflow Pending Transactions
                // -----------------------------------------------------
                // One count per pipeline stage where work can stall.
                // These drive the dashboard's Workflow Pipeline widget.
                // Order matches the physical production flow:
                //   WO → Material Issue → DP draft → DP awaiting QC
                //   → QC pending → QC awaiting rework → QC awaiting re-insp
                //   → Dispatch draft
                // =====================================================

                // Set of WO ids that already have any MATERIAL_ISSUE OUT row
                // — used to identify WOs still waiting for material issue.
                var issuedWoIds = db.Gen_Store
                    .Where(s => s.Deleted != true
                                && s.ReferenceType == "MATERIAL_ISSUE"
                                && s.OutQty > 0m
                                && s.ReferenceId.HasValue)
                    .Select(s => s.ReferenceId.Value)
                    .Distinct()
                    .ToList();

                int wosAwaitingMaterialIssue = db.WorkOrders.Count(x =>
                    x.Deleted != true
                    && x.Active == true
                    && x.IsPosted == true
                    && x.Status != "Completed"
                    && !issuedWoIds.Contains(x.Id));

                int dpDrafts = db.DailyProduction.Count(x =>
                    x.Deleted != true
                    && (x.IsPosted == false || x.IsPosted == null)
                    && x.IsReversed != true);

                int dpAwaitingQcSend = db.DailyProduction.Count(x =>
                    x.Deleted != true
                    && x.IsPosted == true
                    && x.IsReversed != true
                    && (x.IsSentToQC == false || x.IsSentToQC == null)
                    && x.Status != "Rework"
                    && x.Status != "ReworkPosted");

                int qcPendingInspection = db.QC_Inspection.Count(x =>
                    x.Deleted != true
                    && x.IsPosted != true
                    && (x.Status == "Pending" || x.Status == "InProgress"));

                int qcAwaitingRework = db.QC_Inspection.Count(x =>
                    x.Deleted != true
                    && x.Status == "AwaitingRework");

                int qcAwaitingReinspection = db.QC_Inspection.Count(x =>
                    x.Deleted != true
                    && x.Status == "AwaitingReinspection");

                int dispatchDrafts = db.Dispatch.Count(x =>
                    x.Deleted != true
                    && x.IsPosted != true
                    && x.IsReversed != true);

                int totalPending = wosAwaitingMaterialIssue
                                 + dpDrafts
                                 + dpAwaitingQcSend
                                 + qcPendingInspection
                                 + qcAwaitingRework
                                 + qcAwaitingReinspection
                                 + dispatchDrafts;

                // =====================================================
                // Final Response
                // =====================================================

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        productionSummary = new
                        {
                            activeJobOrders,
                            runningWorkOrders,
                            todayProductionQty,
                            dailyEfficiency
                        },

                        qcSummary = new
                        {
                            pendingQcQty,
                            inspectedQty,
                            passedQty,
                            failedQty,
                            reworkQty,
                            reversedQcCount,
                            passRatio,
                            rejectRatio
                        },

                        stockSummary = new
                        {
                            finishedStockQty
                        },

                        dispatchSummary = new
                        {
                            dispatchQty
                        },

                        machineSummary = new
                        {
                            machineCount,
                            activeMachinesToday,
                            machineUtilization
                        },

                        operatorSummary = new
                        {
                            activeOperatorsToday
                        },

                        workflowSummary = new
                        {
                            totalPending,
                            wosAwaitingMaterialIssue,
                            dpDrafts,
                            dpAwaitingQcSend,
                            qcPendingInspection,
                            qcAwaitingRework,
                            qcAwaitingReinspection,
                            dispatchDrafts
                        }
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ================== LOGIN ==================
        [HttpPost]
        [AllowAnonymous]
        [Route("login")]
        public ActionResult Login(LoginRequest model)
        {
            try
            {
                if (model == null || String.IsNullOrWhiteSpace(model.Email) || String.IsNullOrWhiteSpace(model.Password))
                    return Json(new { success = false, message = "Email and password required." },
                        JsonRequestBehavior.AllowGet);

                var user = db.Adm_Users.SingleOrDefault(u =>
                    u.EmailAddress == model.Email && u.Password == model.Password);

                if (user == null)
                    return Json(new { success = false, message = "Invalid credentials." }, JsonRequestBehavior.AllowGet);

                // Update last login
                user.LastLoginDate = DateTime.UtcNow;
                db.SaveChanges();

                // Create auth cookie
                FormsAuthentication.SetAuthCookie(user.EmailAddress, false);

                // Determine EmployeeId
                int empId = user.EmployeeId ?? 0;

                if (empId == 0)
                {
                    var linked = db.Adm_Users.FirstOrDefault(u =>
                        u.EmailAddress == user.EmailAddress || u.UserName == user.UserName);

                    if (linked != null && linked.EmployeeId.HasValue)
                    {
                        empId = linked.EmployeeId.Value;
                        user.EmployeeId = empId;
                        db.SaveChanges();
                    }
                }

                // Save session
                Session["UserID"] = user.Id;
                Session["EmployeeId"] = empId;
                Session["RoleId"] = user.RoleId;
                Session["UserName"] = user.UserName;

                // Phase: role-aware sidebar — return RoleName so the SPA
                // can gate menu items without an extra round-trip.
                string roleName = null;
                if (user.RoleId.HasValue)
                {
                    roleName = db.Adm_Roles
                        .Where(r => r.Id == user.RoleId.Value)
                        .Select(r => r.Name)
                        .FirstOrDefault();
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        Id = user.Id,
                        EmployeeId = empId,
                        UserName = user.UserName,
                        Name = (user.FirstName + " " + user.LastName).Trim(),
                        EmailAddress = user.EmailAddress,
                        RoleId = user.RoleId,
                        RoleName = roleName,
                        BranchId = user.BranchId,
                        CompanyId = user.CompanyId,
                        FacialRecognition = user.FacialRecognition,
                        LastLoginDate = user.LastLoginDate
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Login failed",
                    error = ex.Message,
                    inner = ex.InnerException?.Message,
                    inner2 = ex.InnerException?.InnerException?.Message,
                    stack = ex.StackTrace
                }, JsonRequestBehavior.AllowGet);
            }
        }

        // ================== LOGOUT ==================
        [Authorize]
        [HttpPost]
        [Route("logout")]
        public ActionResult Logout()
        {
            try
            {
                FormsAuthentication.SignOut();
                Session.Clear();
                Session.Abandon();

                return Json(new { success = true, message = "Logged out successfully." },
                    JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Logout failed: " + ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }

        // ================== CHECK AUTH ==================
        [HttpGet]
        [AllowAnonymous]
        [Route("check-auth")]
        public ActionResult CheckAuth()
        {
            try
            {
                var isAuthenticated = User.Identity.IsAuthenticated;

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        IsAuthenticated = isAuthenticated,
                        UserName = User.Identity.Name,
                        UserId = Session["UserID"]
                    }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Auth check failed: " + ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }

        // ================== CHANGE PASSWORD ==================
        [Authorize]
        [HttpPost]
        [Route("change-password")]
        public ActionResult ChangePassword(ChangePasswordViewModel vm)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage);

                    return Json(new { success = false, message = String.Join(", ", errors) },
                        JsonRequestBehavior.AllowGet);
                }

                var userId = Convert.ToInt32(Session["UserID"]);
                var user = db.Adm_Users.Find(userId);

                if (user == null)
                    return Json(new { success = false, message = "User not found." });

                if (user.Password != vm.OldPassword)
                    return Json(new { success = false, message = "Old password incorrect." });

                user.Password = vm.NewPassword;
                db.SaveChanges();

                return Json(new { success = true, message = "Password changed successfully." },
                    JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Password change failed: " + ex.Message
                });
            }
        }
    }
}
