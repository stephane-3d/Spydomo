using Hangfire.Dashboard;

namespace Spydomo.Web.Classes
{
    public sealed class AllowAllDashboardFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) => true;
    }
}
