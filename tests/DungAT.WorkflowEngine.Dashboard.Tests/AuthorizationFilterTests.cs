using System.Net;
using DungAT.WorkflowEngine.Dashboard.Authorization;
using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public class AuthorizationFilterTests
{
    private static HttpContext Context(string? remote, string? proxyHeader = null, string? proxyValue = null)
    {
        var context = new DefaultHttpContext();
        if (remote is not null) context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (proxyHeader is not null) context.Request.Headers[proxyHeader] = proxyValue;
        return context;
    }

    private readonly LocalRequestsOnlyAuthorizationFilter _filter = new();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_connections_are_local(string remote) => Assert.True(_filter.Authorize(Context(remote)));

    [Fact]
    public void A_connection_without_a_remote_address_is_local_as_with_in_process_servers() =>
        Assert.True(_filter.Authorize(Context(null)));

    [Fact]
    public void Other_machines_are_not_local() => Assert.False(_filter.Authorize(Context("203.0.113.9")));

    [Theory]
    [InlineData("X-Forwarded-For", "203.0.113.9")]
    [InlineData("Forwarded", "for=203.0.113.9")]
    [InlineData("X-Real-IP", "203.0.113.9")]
    public void Requests_relayed_by_a_proxy_on_the_same_machine_are_not_local(string header, string value) =>
        Assert.False(_filter.Authorize(Context("127.0.0.1", header, value)));
}
