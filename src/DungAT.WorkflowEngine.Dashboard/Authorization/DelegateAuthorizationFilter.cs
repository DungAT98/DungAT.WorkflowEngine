using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Authorization;

/// <summary>Adapts a delegate, e.g. <c>new DelegateAuthorizationFilter(c => c.User.IsInRole("Admin"))</c>.</summary>
public sealed class DelegateAuthorizationFilter(Func<HttpContext, bool> authorize) : IWorkflowDashboardAuthorizationFilter
{
    public bool Authorize(HttpContext context) => authorize(context);
}
