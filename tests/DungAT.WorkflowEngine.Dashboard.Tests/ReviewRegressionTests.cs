using System.Net;
using System.Text;
using System.Text.Json;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public class ReviewRegressionTests
{
    [Fact]
    public async Task Dashboard_mounted_at_the_root_gets_a_single_slash_base_path()
    {
        await using var host = await DashboardTestHost.StartAsync(mountPath: "/");

        var html = await host.Client.GetStringAsync("/");

        Assert.Contains("\"apiBase\":\"/api\"", html);
        Assert.Contains("<base href=\"/\"", html);
        Assert.DoesNotContain("//api", html);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/meta")).StatusCode);
    }

    [Fact]
    public async Task A_command_body_that_is_not_json_is_rejected_instead_of_silently_dropped()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var started = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "DOC", entityType = "Document", entityId = "7" }, "Employee"));
        var id = JsonDocument.Parse(await started.Content.ReadAsStringAsync()).RootElement.GetProperty("instance").GetProperty("id").GetGuid();

        var request = host.Request(HttpMethod.Post, $"/workflow/api/instances/{id}/commands/SUBMIT", roles: "Employee");
        request.Content = new StringContent("{\"comment\":\"because\"}", Encoding.UTF8, "text/plain");
        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        // And a command without any body still works.
        var bodiless = await host.Client.SendAsync(host.Request(HttpMethod.Post, $"/workflow/api/instances/{id}/commands/SUBMIT", roles: "Employee"));
        Assert.Equal(HttpStatusCode.OK, bodiless.StatusCode);
    }
}
