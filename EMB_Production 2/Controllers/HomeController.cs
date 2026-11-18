using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;      // ← for FormsAuthentication
using EMB_Production.Models;
using EMB_Production.Infrastructure;


namespace EMB_Production.Controllers
{
   
    public class HomeController : Controller
    {
        private readonly EMB_ProductionEntities1 db = new EMB_ProductionEntities1();



        [AllowAnonymous]
        public ActionResult Angular()
        {
            string indexPath = Server.MapPath("~/Content/emb-products/index.html");
            if (System.IO.File.Exists(indexPath))
            {
                return File(indexPath, "text/html");
            }
            return Content("Angular app not found. Please run ng build -c production.", "text/html");
        }
    

           
        [AllowAnonymous]
        [HttpGet]
        public ActionResult Login(string returnUrl)
        {           
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string email, string password, string returnUrl)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ModelState.AddModelError("", "Please enter both email and password.");
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // look up the user
            var user = db.Adm_Users.SingleOrDefault(u => u.EmailAddress == email);
            if (user != null && PasswordHasher.Verify(password, user.Password))
            {
                // Lazy migration: upgrade legacy plaintext passwords to a proper
                // hash now that we know the password is correct (mirrors
                // AuthController.Login's rehashing behavior).
                if (!PasswordHasher.IsHashed(user.Password))
                {
                    user.Password = PasswordHasher.Hash(password);
                }

                // stamp the login time
                user.LastLoginDate = DateTime.UtcNow;
                db.SaveChanges();

                Session["UserID"] = user.Id;
                Session["EmployeeId"] = user.EmployeeId ?? user.Id;
                Session["Role"] = user.RoleId;
                Session["UserName"] = user.UserName;
                FormsAuthentication.SetAuthCookie(user.EmailAddress, false);

                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                else
                    return RedirectToAction("Index", "Home");
            }

            ModelState.AddModelError("", "Invalid email or password.");
            ViewBag.ReturnUrl = returnUrl;
            return View();

        }
            [Authorize]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();
            return RedirectToAction("Login", "Home");
        }

        // Now Index can remain [Authorize], because we’ve properly set the auth cookie
        [Authorize]
        public ActionResult Index()
        {
            return View();
        }

        [Authorize]
        public ActionResult About()
        {
            return View();
        }

        [Authorize]
        public ActionResult Contact()
        {
            return View();
        }       
    }
}
