using System.Net;
using System.Text.Json;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public class DefinitionApiTests
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static object Leave(string code = "LEAVE", string[]? guards = null, string toState = "APPROVED", string? entityType = null, string[]? postActions = null) => new
    {
        code,
        name = "Leave request",
        entityType,
        states = new object[]
        {
            new { code = "OPEN", name = "Open", type = "Initial" },
            new { code = "APPROVED", name = "Approved", type = "Final" },
        },
        commands = new object[] { new { code = "APPROVE", name = "Approve" } },
        transitions = new object[]
        {
            new { fromStateCode = "OPEN", commandCode = "APPROVE", toStateCode = toState, allowedRoles = new[] { "Manager" }, guardNames = guards ?? [], postActionNames = postActions ?? [] },
        },
    };

    [Fact]
    public async Task Code_definitions_are_listed_read_only_with_details()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var list = await JsonAsync(await host.Client.GetAsync("/workflow/api/definitions"));
        var doc = list[0];
        Assert.Equal("DOC", doc.GetProperty("code").GetString());
        Assert.Equal("Code", doc.GetProperty("source").GetString());
        Assert.False(doc.GetProperty("editable").GetBoolean());
        Assert.Equal(3, doc.GetProperty("stateCount").GetInt32());

        var detail = await JsonAsync(await host.Client.GetAsync("/workflow/api/definitions/DOC"));
        Assert.Equal(3, detail.GetProperty("definition").GetProperty("transitions").GetArrayLength());
        Assert.Equal(1, detail.GetProperty("versions").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/workflow/api/definitions/NOPE")).StatusCode);
    }

    [Fact]
    public async Task Validate_reports_structural_and_registry_errors()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var ok = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions/validate", Leave())));
        Assert.True(ok.GetProperty("isValid").GetBoolean());

        var bad = await JsonAsync(await host.Client.SendAsync(host.Request(
            HttpMethod.Post, "/workflow/api/definitions/validate", Leave(guards: ["Missing"], toState: "NOWHERE"))));
        var errors = bad.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("Guard 'Missing' is not registered"));

        var blank = await JsonAsync(await host.Client.SendAsync(host.Request(
            HttpMethod.Post, "/workflow/api/definitions/validate", new { code = "", name = "" })));
        Assert.Contains(blank.GetProperty("errors").EnumerateArray().Select(e => e.GetString()), e => e!.Contains("name is required"));

        var structural = await JsonAsync(await host.Client.SendAsync(host.Request(
            HttpMethod.Post, "/workflow/api/definitions/validate", Leave(toState: "NOWHERE"))));
        Assert.Contains(structural.GetProperty("errors").EnumerateArray().Select(e => e.GetString()), e => e!.Contains("unknown state 'NOWHERE'"));
    }

    [Fact]
    public async Task Publish_creates_versions_and_can_start_instances()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var first = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave(guards: ["AlwaysPassGuard"])));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await JsonAsync(first);
        Assert.Equal(1, created.GetProperty("definition").GetProperty("version").GetInt32());
        Assert.Equal("Dashboard", created.GetProperty("definition").GetProperty("source").GetString());
        Assert.True(created.GetProperty("editable").GetBoolean());

        var second = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave())));
        Assert.Equal(2, second.GetProperty("definition").GetProperty("version").GetInt32());
        Assert.Equal(2, second.GetProperty("versions").GetArrayLength());

        var start = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "LEAVE", entityType = "Leave", entityId = "7" }));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        Assert.Equal(2, (await JsonAsync(start)).GetProperty("instance").GetProperty("definitionVersion").GetInt32());
    }

    [Fact]
    public async Task Publish_rejects_invalid_and_code_defined_workflows()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var invalid = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave(guards: ["Missing"])));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.False((await JsonAsync(invalid)).GetProperty("isValid").GetBoolean());

        var conflict = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave(code: "doc")));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Meta_tells_the_editor_which_entities_each_guard_is_for()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var meta = await JsonAsync(await host.Client.GetAsync("/workflow/api/meta"));
        var guards = meta.GetProperty("guards").EnumerateArray().ToDictionary(g => g.GetProperty("name").GetString()!);

        Assert.True(guards["AlwaysPassGuard"].GetProperty("appliesToAnyEntity").GetBoolean());
        Assert.False(guards["MemoGuard"].GetProperty("appliesToAnyEntity").GetBoolean());
        Assert.Equal(["Memo"], guards["MemoGuard"].GetProperty("entityNames").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Entity_specific_guards_are_only_accepted_for_the_matching_entity()
    {
        await using var host = await DashboardTestHost.StartAsync();
        async Task<JsonElement> ValidateAsync(string? entityType) => await JsonAsync(await host.Client.SendAsync(
            host.Request(HttpMethod.Post, "/workflow/api/definitions/validate", Leave(guards: ["MemoGuard", "AlwaysPassGuard"], entityType: entityType))));

        Assert.True((await ValidateAsync("Memo")).GetProperty("isValid").GetBoolean());

        var wrongEntity = await ValidateAsync("Invoice");
        Assert.Contains(wrongEntity.GetProperty("errors").EnumerateArray().Select(e => e.GetString()),
            e => e!.Contains("'MemoGuard' cannot be used with Invoice; it is for Memo"));

        var noEntity = await ValidateAsync(null);
        Assert.Contains(noEntity.GetProperty("errors").EnumerateArray().Select(e => e.GetString()),
            e => e!.Contains("'MemoGuard' needs a workflow bound to an entity"));

        var publish = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions",
            Leave(guards: ["MemoGuard"], entityType: "Invoice")));
        Assert.Equal(HttpStatusCode.BadRequest, publish.StatusCode);
    }

    [Fact]
    public async Task Instance_detail_links_to_the_entity_and_start_checks_the_entity_exists()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var created = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave(entityType: "Memo")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var missing = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "LEAVE", entityType = "Memo", entityId = "not-a-number" }));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var mismatch = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "LEAVE", entityType = "Other", entityId = "1" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatch.StatusCode);

        var start = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "LEAVE", entityType = "Memo", entityId = "12" })));
        var detail = await JsonAsync(await host.Client.GetAsync($"/workflow/api/instances/{start.GetProperty("instance").GetProperty("id").GetGuid()}"));
        Assert.Equal("/memos/12", detail.GetProperty("entityUrl").GetString());
    }

    [Fact]
    public async Task Post_actions_are_listed_validated_and_their_failures_come_back_as_warnings()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var meta = await JsonAsync(await host.Client.GetAsync("/workflow/api/meta"));
        var postActions = meta.GetProperty("postActions").EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!);
        Assert.True(postActions["FlakyPostAction"].GetProperty("appliesToAnyEntity").GetBoolean());
        Assert.Equal(["Memo"], postActions["MemoPostAction"].GetProperty("entityNames").EnumerateArray().Select(e => e.GetString()));

        var invalid = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions/validate",
            Leave(entityType: "Invoice", postActions: ["MemoPostAction", "Ghost"]))));
        var errors = invalid.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(errors, e => e.Contains("Post action 'MemoPostAction' cannot be used with Invoice"));
        Assert.Contains(errors, e => e.Contains("Post action 'Ghost' is not registered"));

        var created = await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/definitions", Leave(postActions: ["FlakyPostAction"])));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var start = await JsonAsync(await host.Client.SendAsync(host.Request(HttpMethod.Post, "/workflow/api/instances",
            new { definitionCode = "LEAVE", entityType = "Doc", entityId = "1" })));
        var id = start.GetProperty("instance").GetProperty("id").GetGuid();

        var run = await host.Client.SendAsync(host.Request(HttpMethod.Post, $"/workflow/api/instances/{id}/commands/APPROVE", roles: "Manager"));
        Assert.Equal(HttpStatusCode.OK, run.StatusCode); // the transition is saved even though the action failed
        var result = await JsonAsync(run);
        Assert.Equal("Final", result.GetProperty("instance").GetProperty("stateType").GetString());
        Assert.Equal(["FlakyPostAction failed: mail server down"], result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));

        var detail = await JsonAsync(await host.Client.GetAsync($"/workflow/api/instances/{id}"));
        var approve = detail.GetProperty("history").EnumerateArray().Single(h => h.GetProperty("commandCode").GetString() == "APPROVE");
        var action = approve.GetProperty("postActions")[0];
        Assert.Equal(("FlakyPostAction", false, "mail server down"),
            (action.GetProperty("name").GetString(), action.GetProperty("succeeded").GetBoolean(), action.GetProperty("error").GetString()));
    }
}

