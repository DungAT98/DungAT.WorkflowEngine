using System.Net;
using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Authorization;

/// <summary>
/// Allows only requests that originate from the machine hosting the app (the default).
/// A request that carries proxy headers (<c>Forwarded</c>, <c>X-Forwarded-For</c>, <c>X-Real-IP</c>) is not treated as local:
/// behind a reverse proxy on the same machine every connection comes from loopback, so the connection address alone proves nothing.
/// Replace this filter before exposing the dashboard through a proxy.
/// </summary>
public sealed class LocalRequestsOnlyAuthorizationFilter : IWorkflowDashboardAuthorizationFilter
{
    private static readonly string[] ProxyHeaders = ["Forwarded", "X-Forwarded-For", "X-Real-IP"];

    public bool Authorize(HttpContext context)
    {
        if (ProxyHeaders.Any(h => context.Request.Headers.ContainsKey(h))) return false;

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return true; // in-process test servers and unix-socket hosting have no remote address
        return IPAddress.IsLoopback(remote) || remote.Equals(context.Connection.LocalIpAddress);
    }
}
