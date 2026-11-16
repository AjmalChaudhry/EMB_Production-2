using System.Web.Mvc;

namespace EMB_Production.Infrastructure
{
    /// <summary>
    /// Controller/action gate for this app's actual, proven "logged in" signal.
    ///
    /// Why not plain [Authorize]: Web.config has no &lt;authentication mode="Forms"&gt;
    /// section configured at all, so there is no guarantee FormsAuthenticationModule
    /// ever populates HttpContext.User from the ticket cookie AuthController.Login sets
    /// via FormsAuthentication.SetAuthCookie(...) — i.e. User.Identity.IsAuthenticated
    /// is not a reliable signal in this codebase. What IS reliable, and used
    /// pervasively across nearly every controller (Gen_* controllers, Hr_Customers,
    /// the pre-existing Adm_Users/Branches inline "update-field" actions, etc.) as the
    /// de-facto "is this caller logged in" check, is Session["UserID"] — set at login
    /// in AuthController.Login / HomeController.Login and checked everywhere with
    /// `if (Session["UserID"] == null) return ...`.
    ///
    /// This attribute applies that same, already-working check at the controller
    /// level instead of relying on an authentication mode that isn't actually wired
    /// up, so it can't silently lock everyone out (or silently do nothing).
    /// </summary>
    public class SessionAuthorizeAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            // Respect [AllowAnonymous] on the action or controller, same as the
            // built-in [Authorize] does, so explicitly public endpoints (e.g. a
            // logo image served for an unauthenticated login page) keep working.
            bool allowAnonymous =
                filterContext.ActionDescriptor.IsDefined(typeof(AllowAnonymousAttribute), true) ||
                filterContext.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(AllowAnonymousAttribute), true);

            if (allowAnonymous)
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            var session = filterContext.HttpContext?.Session;

            if (session == null || session["UserID"] == null)
            {
                filterContext.HttpContext.Response.StatusCode = 401;
                filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;

                filterContext.Result = new JsonResult
                {
                    Data = new { success = false, message = "Not authenticated." },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet
                };
            }

            base.OnActionExecuting(filterContext);
        }
    }
}
