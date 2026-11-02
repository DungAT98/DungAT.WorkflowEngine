using System.Security.Claims;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Dashboard.Authorization;
using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard;

public sealed class WorkflowDashboardOptions
{
    /// <summary>Shown in the sidebar and browser tab.</summary>
    public string Title { get; set; } = "Workflow";

    /// <summary>Hides and rejects everything that changes data: running commands, starting instances, editing definitions.</summary>
    public bool ReadOnly { get; set; }

    /// <summary>All filters must pass. Defaults to local requests only; replace it before exposing the dashboard.</summary>
    public IList<IWorkflowDashboardAuthorizationFilter> Authorization { get; set; } =
        [new LocalRequestsOnlyAuthorizationFilter()];

    /// <summary>
    /// Optional: turns (entityType, entityId) into a URL in your app, shown as a link on the instance page,
    /// e.g. <c>(type, id) =&gt; type == "Report" ? $"/reports/{id}" : null</c>.
    /// </summary>
    public Func<string, string, string?>? EntityLink { get; set; }

    /// <summary>Maps the HTTP caller to the <see cref="WorkflowUser"/> commands run as.</summary>
    public Func<HttpContext, WorkflowUser> UserResolver { get; set; } = DefaultUserResolver;

    private static WorkflowUser DefaultUserResolver(HttpContext context)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true) return new WorkflowUser("anonymous");
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return new WorkflowUser(principal.Identity.Name ?? "unknown", roles);
    }
}
