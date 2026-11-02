using DungAT.WorkflowEngine.Dashboard.Api;
using DungAT.WorkflowEngine.Dashboard.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.Dashboard;

public static class WorkflowDashboardEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the dashboard UI and its JSON API under <paramref name="pathPrefix"/> (default <c>/workflow</c>).
    /// The returned builder accepts normal endpoint conventions such as <c>RequireAuthorization()</c>.
    /// </summary>
    public static IEndpointConventionBuilder MapWorkflowDashboard(
        this IEndpointRouteBuilder endpoints, string pathPrefix = "/workflow", Action<WorkflowDashboardOptions>? configure = null)
    {
        if (endpoints.ServiceProvider.GetService<DashboardMarker>() is null)
            throw new InvalidOperationException("Call services.AddWorkflowDashboard() (and AddWorkflowEngine) before MapWorkflowDashboard().");

        var options = new WorkflowDashboardOptions();
        configure?.Invoke(options);

        var mount = "/" + pathPrefix.Trim('/');
        var group = endpoints.MapGroup(mount).ExcludeFromDescription();
        group.AddEndpointFilter(new DashboardEndpointFilter(options));

        // Routing ignores a trailing slash, so "" matches both "/workflow" and "/workflow/". The bare form is
        // redirected so the SPA's relative URLs resolve against the right base; the slashed form is the app.
        group.MapGet("", (HttpContext context) => context.Request.Path.Value!.EndsWith('/')
            ? StaticAssetHandler.Handle(context, null, mount, options)
            : Results.Redirect($"{context.Request.PathBase}{mount.TrimEnd('/')}/{context.Request.QueryString}"));
        DashboardApi.Map(group.MapGroup("/api"), options);
        group.MapGet("/{**path}", (HttpContext context, string? path) => StaticAssetHandler.Handle(context, path, mount, options));
        return group;
    }
}
