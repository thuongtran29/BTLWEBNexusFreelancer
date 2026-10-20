using System.Data.Entity;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using NexusFreelance.Web.Data;

namespace NexusFreelance.Web
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            Database.SetInitializer(new MigrateDatabaseToLatestVersion<AppDbContext, Migrations.Configuration>());
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            Services.FinanceSettlementRunner.Start();
        }
        protected void Application_End() { Services.FinanceSettlementRunner.Stop(); }
    }
}
