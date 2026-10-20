using System.Web.Mvc;
using System.Web.Routing;

namespace NexusFreelance.Web
{
    public static class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            routes.MapRoute("ApiProjects", "api/v1/projects", new { controller = "SubmissionApi", action = "Projects" }, new { httpMethod = new HttpMethodConstraint("GET") });
            routes.MapRoute("ApiProjectWrite", "api/v1/projects", new { controller = "ProjectApi", action = "Collection" });
            routes.MapRoute("ApiProject", "api/v1/projects/{id}", new { controller = "ProjectApi", action = "Resource" });
            routes.MapRoute("ApiNotification", "api/v1/notifications/{id}", new { controller = "SubmissionApi", action = "Notification" }, new { id = @"\d+" });
            routes.MapRoute("ApiNotifications", "api/v1/notifications", new { controller = "SubmissionApi", action = "Notifications" });
            routes.MapRoute("Default", "{controller}/{action}/{id}",
                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
