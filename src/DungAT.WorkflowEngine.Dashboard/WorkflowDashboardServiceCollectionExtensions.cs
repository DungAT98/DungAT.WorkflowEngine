using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.Dashboard;

public static class WorkflowDashboardServiceCollectionExtensions
{
    /// <summary>Registers the dashboard. Call <c>AddWorkflowEngine</c> first, then <c>MapWorkflowDashboard</c> on the app.</summary>
    public static IServiceCollection AddWorkflowDashboard(this IServiceCollection services)
    {
        services.TryAddSingleton<DashboardMarker>();
        return services;
    }
}
