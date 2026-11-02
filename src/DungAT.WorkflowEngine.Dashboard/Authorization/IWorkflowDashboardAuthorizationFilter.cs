using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Authorization;

/// <summary>Decides whether a request may use the dashboard. Every filter must return true.</summary>
public interface IWorkflowDashboardAuthorizationFilter
{
    bool Authorize(HttpContext context);
}
