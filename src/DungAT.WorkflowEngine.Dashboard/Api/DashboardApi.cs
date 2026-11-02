using Microsoft.AspNetCore.Routing;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class DashboardApi
{
    public static void Map(RouteGroupBuilder api, WorkflowDashboardOptions options)
    {
        MetaEndpoints.Map(api, options);
        StatsEndpoints.Map(api);
        DefinitionEndpoints.Map(api, options);
        InstanceEndpoints.Map(api, options);
    }
}
