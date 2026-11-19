using System;
using System.Linq;
using System.Web.Mvc;
using EMB_Production.Models;

namespace EMB_Production.Infrastructure
{
    /// <summary>
    /// Controller/action gate for this app's actual, proven "logged in" signal.
    ///
    /// Why not plain [Authorize]: historically this app's Web.config had no
    /// &lt;authentication mode="Forms"&gt; section at all, so User.Identity.IsAuthenticated
    /// wasn't a reliable signal. Forms Auth is wired up now, but Session["UserID"] —
    /// set at login in AuthController.Login / HomeController.Login — remains the
    /// de-facto "is this caller logged in" check used pervasively across nearly
    /// every controller, so this attribute still gates on it first.
    ///
    /// Session-loss fallback: an app-pool / AppDomain recycle (any deploy touches
    /// bin\, which triggers one) wipes in-memory (InProc) session state for every
    /// already-open browser tab, but the signed Forms Auth cookie those tabs carry
    /// is untouched and still valid. Without a fallback, Session["UserID"] being
    /// empty looked identical to "never logged in" — every WorkOrdersController
    /// action would 401, and because FormsAuthenticationModule rewrites any 401
    /// response into a 302 redirect to the login page (see SuppressFormsAuthenticationRedirect
    /// below), the XHR got back an HTML login page instead of JSON and silently failed
    /// to parse — no visible error, the action just looked like it did nothing.
    /// Mirror BaseController.GetCurrentUserId()'s identity-based resolution here, at
    /// the filter level, so a stale session self-heals from the still-valid cookie
    /// instead of rejecting a legitimately logged-in user.
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

            if (session != null && session["UserID"] != null)
            {
                base.OnActionExecuting(filterContext);
                return;
            }

            // Session cache miss. Before treating this as "not logged in", try
            // resolving from the Forms Auth identity — same EmailAddress-first,
            // then UserName, lookup BaseController.GetCurrentUserId() uses — and
            // re-seed the session so later actions in this request's lifetime
            // (and the rest of this browser tab's visit) hit the fast path again.
            if (session != null)
            {
                var identityName = (filterContext.HttpContext?.User?.Identity?.Name ?? "").Trim();
                if (!string.IsNullOrEmpty(identityName))
                {
                    try
                    {
                        using (var db = new EMB_ProductionEntities1())
                        {
                            var user = db.Adm_Users.FirstOrDefault(u => u.EmailAddress == identityName)
                                   ?? db.Adm_Users.FirstOrDefault(u => u.UserName == identityName);
                            if (user != null && user.Id > 0)
                            {
                                session["UserID"] = user.Id;
                                base.OnActionExecuting(filterContext);
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // A DB hiccup here shouldn't turn into a cryptic 500 on
                        // every action — fall through to the real 401 below.
                    }
                }
            }

            filterContext.HttpContext.Response.StatusCode = 401;
            filterContext.HttpContext.Response.TrySkipIisCustomErrors = true;
            // Without this, FormsAuthenticationModule rewrites our JSON 401 into
            // a 302 redirect to the login page before the response reaches the
            // client — an XHR caller (Angular) gets back HTML it can't parse
            // instead of the {success:false,...} body below, and the failure
            // looks like the action silently did nothing.
            filterContext.HttpContext.Response.SuppressFormsAuthenticationRedirect = true;

            filterContext.Result = new JsonResult
            {
                Data = new { success = false, message = "Not authenticated." },
                JsonRequestBehavior = JsonRequestBehavior.AllowGet
            };

            base.OnActionExecuting(filterContext);
        }
    }
}
