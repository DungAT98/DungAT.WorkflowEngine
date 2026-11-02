using System.Net;
using System.Text.Json;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public class ApiTests
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<Guid> StartAsync(DashboardTestHost host, string entityId, string roles = "Employee")
    {
        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "DOC", entityType = "Document", entityId }, roles));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("instance").GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> RunAsync(DashboardTestHost host, Guid id, string command, string roles, object? body = null) =>
        host.Client.SendAsync(host.Request(HttpMethod.Post, $"/workflow/api/instances/{id}/commands/{command}", body, roles));

    [Fact]
    public async Task Meta_reports_user_and_registered_components()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var meta = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Get, "/workflow/api/meta", roles: "Manager,Auditor")));
        Assert.Equal("tester", meta.GetProperty("userId").GetString());
        Assert.Equal(["Manager", "Auditor"], meta.GetProperty("userRoles").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["AlwaysPassGuard", "MemoGuard"], meta.GetProperty("guards").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        Assert.Equal(["Memo"], meta.GetProperty("entities").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["DOC"], meta.GetProperty("codeDefinitions").EnumerateArray().Select(e => e.GetString()));
        Assert.False(meta.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Instance_lifecycle_with_typed_error_statuses()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var id = await StartAsync(host, "42");

        // Same entity twice -> 409
        var duplicate = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "DOC", entityType = "Document", entityId = "42" }));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var detail = await JsonAsync(await host.Client.GetAsync($"/workflow/api/instances/{id}"));
        Assert.Equal("DRAFT", detail.GetProperty("instance").GetProperty("currentStateCode").GetString());
        Assert.Equal("Initial", detail.GetProperty("instance").GetProperty("stateType").GetString());
        Assert.Equal(["SUBMIT"], detail.GetProperty("availableCommands").EnumerateArray().Select(c => c.GetProperty("code").GetString()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RunAsync(host, id, "APPROVE", "Manager")).StatusCode); // invalid in DRAFT
        Assert.Equal(HttpStatusCode.OK, (await RunAsync(host, id, "SUBMIT", "Employee")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RunAsync(host, id, "APPROVE", "Employee")).StatusCode);   // not a manager
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await RunAsync(host, id, "REJECT", "Employee")).StatusCode); // comment required
        var approved = await RunAsync(host, id, "APPROVE", "Manager");
        var result = await JsonAsync(approved);
        Assert.Equal("Final", result.GetProperty("instance").GetProperty("stateType").GetString());
        Assert.Equal(0, result.GetProperty("warnings").GetArrayLength());

        var history = (await JsonAsync(await host.Client.GetAsync($"/workflow/api/instances/{id}"))).GetProperty("history");
        Assert.Equal(["START", "SUBMIT", "APPROVE"], history.EnumerateArray().Select(h => h.GetProperty("commandCode").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(host, Guid.NewGuid(), "SUBMIT", "Employee")).StatusCode);
    }

    [Fact]
    public async Task Stats_list_and_activity_reflect_instances()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var a = await StartAsync(host, "A-1");
        await StartAsync(host, "B-2");
        await RunAsync(host, a, "SUBMIT", "Employee");
        await RunAsync(host, a, "APPROVE", "Manager");

        var stats = await JsonAsync(await host.Client.GetAsync("/workflow/api/stats"));
        Assert.Equal(2, stats.GetProperty("totalInstances").GetInt32());
        Assert.Equal(1, stats.GetProperty("finishedInstances").GetInt32());
        Assert.Equal(1, stats.GetProperty("activeInstances").GetInt32());
        Assert.Equal(2, stats.GetProperty("commandsLast24Hours").GetInt32());
        var states = stats.GetProperty("definitions")[0].GetProperty("states");
        Assert.Equal(1, states.EnumerateArray().Single(s => s.GetProperty("code").GetString() == "APPROVED").GetProperty("count").GetInt32());

        var list = await JsonAsync(await host.Client.GetAsync("/workflow/api/instances?state=APPROVED&pageSize=10"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("A-1", list.GetProperty("items")[0].GetProperty("entityId").GetString());
        var search = await JsonAsync(await host.Client.GetAsync("/workflow/api/instances?q=b-"));
        Assert.Equal(1, search.GetProperty("total").GetInt32());

        var activity = await JsonAsync(await host.Client.GetAsync("/workflow/api/activity?take=3"));
        Assert.Equal(3, activity.GetArrayLength());
        Assert.Equal("APPROVE", activity[0].GetProperty("entry").GetProperty("commandCode").GetString());
    }

    [Fact]
    public async Task Mutations_require_header_and_respect_read_only()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var noHeader = host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "DOC", entityType = "Document", entityId = "1" }, mutation: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.SendAsync(noHeader)).StatusCode);

        await using var readOnly = await DashboardTestHost.StartAsync(o => o.ReadOnly = true);
        var attempt = readOnly.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "DOC", entityType = "Document", entityId = "1" });
        Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.Client.SendAsync(attempt)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await readOnly.Client.GetAsync("/workflow/api/definitions")).StatusCode);
        Assert.True((await JsonAsync(await readOnly.Client.GetAsync("/workflow/api/meta"))).GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Invalid_bodies_return_client_errors()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var missing = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances", new { definitionCode = "DOC" }));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var request = host.Request(HttpMethod.Post, "/workflow/api/instances");
        request.Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/workflow/api/instances/{Guid.NewGuid()}")).StatusCode);
    }

    /// <summary>A body whose length is not known up front, so it is sent chunked without a Content-Length header.</summary>
    private sealed class UnknownLengthJson(string json) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json)).AsTask();

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task A_command_body_sent_chunked_is_still_read()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var id = await StartAsync(host, "chunked");
        await RunAsync(host, id, "SUBMIT", "Employee");

        var request = host.Request(HttpMethod.Post, $"/workflow/api/instances/{id}/commands/REJECT");
        request.Content = new UnknownLengthJson("""{"comment":"too vague"}""");
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.TransferEncodingChunked = true;
        Assert.Null(request.Content.Headers.ContentLength);

        var response = await host.Client.SendAsync(request); // REJECT requires a comment
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await JsonAsync(await host.Client.GetAsync($"/workflow/api/instances/{id}"));
        Assert.Equal("too vague", detail.GetProperty("history").EnumerateArray()
            .Single(h => h.GetProperty("commandCode").GetString() == "REJECT").GetProperty("comment").GetString());
    }
}

