using DungAT.WorkflowEngine.Dashboard.Authorization;
using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Api;

/// <summary>Applies to every dashboard route: authorization filters, CSRF header and read-only mode.</summary>
internal sealed class DashboardEndpointFilter(WorkflowDashboardOptions options) : IEndpointFilter
{
    public const string RequestHeader = "X-Workflow-Dashboard";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";

        foreach (IWorkflowDashboardAuthorizationFilter filter in options.Authorization)
            if (!filter.Authorize(context))
                return ApiResults.Error(StatusCodes.Status401Unauthorized, "You are not authorized to use the workflow dashboard.");

        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            // A custom header cannot be sent cross-site without a CORS preflight, which blocks CSRF.
            if (!context.Request.Headers.ContainsKey(RequestHeader))
                return ApiResults.Error(StatusCodes.Status403Forbidden, $"Missing {RequestHeader} header.");
            if (options.ReadOnly)
                return ApiResults.Error(StatusCodes.Status403Forbidden, "The dashboard is read-only.");
        }

        return await next(invocation);
    }
}
