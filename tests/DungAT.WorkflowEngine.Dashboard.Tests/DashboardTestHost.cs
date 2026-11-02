using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public sealed class AlwaysPassGuard : IWorkflowGuard
{
    public Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(GuardResult.Pass);
}

public sealed class Memo
{
    public int Id { get; set; }
}

public sealed class MemoGuard : WorkflowGuard<Memo>
{
    protected override Task<GuardResult> EvaluateAsync(Memo memo, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(GuardResult.Pass);
}

public sealed class FlakyPostAction : IWorkflowPostAction
{
    public Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default) =>
        throw new InvalidOperationException("mail server down");
}

public sealed class MemoPostAction : WorkflowPostAction<Memo>
{
    protected override Task ExecuteAsync(Memo memo, WorkflowPostContext context, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>An in-process app with a code-defined DOC workflow and the dashboard mounted at /workflow.</summary>
public sealed class DashboardTestHost : IAsyncDisposable
{
    public const string MutationHeader = "X-Workflow-Dashboard";

    private readonly WebApplication _app;
    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;

    private DashboardTestHost(WebApplication app)
    {
        _app = app;
        Client = app.GetTestClient();
    }

    public static async Task<DashboardTestHost> StartAsync(Action<WorkflowDashboardOptions>? configure = null, string mountPath = "/workflow")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddWorkflowEngine(wf => wf
            .AddGuard<AlwaysPassGuard>()
            .AddGuard<MemoGuard>()
            .AddPostAction<FlakyPostAction>()
            .AddPostAction<MemoPostAction>()
            .AddEntity<Memo>(e => e.Load((_, id, _) => Task.FromResult(int.TryParse(id, out var n) ? new Memo { Id = n } : null)))
            .AddDefinition(WorkflowDefinitionBuilder.Define("DOC", "Document approval")
                .State("DRAFT", WorkflowStateType.Initial)
                .State("SUBMITTED")
                .State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager")
                .On("SUBMITTED").Command("REJECT").GoTo("DRAFT").RequireComment()));
        builder.Services.AddWorkflowDashboard();

        var app = builder.Build();
        app.MapWorkflowDashboard(mountPath, o =>
        {
            o.Title = "Test <Approvals>";
            o.EntityLink = (type, id) => type == "Memo" ? $"/memos/{id}" : null;
            // Tests choose the caller's roles with a header.
            o.UserResolver = ctx => new WorkflowUser("tester",
                ctx.Request.Headers["X-Test-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries));
            configure?.Invoke(o);
        });
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        await app.StartAsync();
        return new DashboardTestHost(app);
    }

    public HttpRequestMessage Request(HttpMethod method, string url, object? body = null, string? roles = null, bool mutation = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (mutation && method != HttpMethod.Get) request.Headers.Add(MutationHeader, "1");
        if (roles is not null) request.Headers.Add("X-Test-Roles", roles);
        if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
