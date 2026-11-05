using System.Web.Mvc;
using System.Web.Routing;

namespace EMB_Production
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            routes.IgnoreRoute("favicon.ico");
            routes.IgnoreRoute("Scripts/{*pathInfo}");

            routes.MapMvcAttributeRoutes();

            // ✅ Allow API routes to bypass Angular
            routes.MapRoute(
                name: "ApiRoutes",
                url: "emb-products/api/{controller}/{action}/{id}",
                defaults: new { id = UrlParameter.Optional }
            );

            // ✅ Auth API (if any)
            routes.MapRoute(
                name: "AuthApi",
                url: "emb-products/auth/{action}",
                defaults: new { controller = "Auth" }
            );

            // ✅ Serve Angular for ALL /Content/emb-products/ URLs
            routes.MapRoute(
                name: "AngularApp",
                url: "Content/emb-products/{*pathInfo}",
                defaults: new { controller = "Home", action = "Angular" }
            );

            // ⚠ Do NOT point to Login anymore
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Angular", id = UrlParameter.Optional }
            );
        }
    }
}
