using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.Tests;

public sealed class ThrowingPublisher : IWorkflowEventPublisher
{
    public Task PublishAsync(WorkflowStateChanged @event, CancellationToken ct = default) =>
        throw new InvalidOperationException("bus down");
}

/// <summary>Fails the save of a new instance with something other than the duplicate-start exception.</summary>
public sealed class FailingAddStore(IWorkflowInstanceStore inner) : IWorkflowInstanceStore
{
    public Task<WorkflowInstance?> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default) => inner.GetByEntityAsync(entityType, entityId, ct);
    public Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    public Task AddAsync(WorkflowInstance i, WorkflowHistoryEntry h, CancellationToken ct = default) => throw new TimeoutException("db timeout");
    public Task<bool> TryUpdateAsync(WorkflowInstance u, int v, WorkflowHistoryEntry h, CancellationToken ct = default) => inner.TryUpdateAsync(u, v, h, ct);
    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken ct = default) => inner.GetHistoryAsync(id, ct);
    public Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default) => inner.AddPostActionRunsAsync(runs, ct);
    public Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid id, CancellationToken ct = default) => inner.GetPostActionRunsAsync(id, ct);
}

public class ReviewRegressionTests
{
    private static readonly WorkflowUser User = new("u", ["Employee"]);

    private static async Task<(IWorkflowEngine Engine, ActionLog Log)> CreateAsync(Report report, Action<ServiceCollection> tweak)
    {
        var log = new ActionLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        tweak(services);
        services.AddWorkflowEngine(wf => wf
            .AddEntity<Report>(e => e.Load((_, id, _) => Task.FromResult<Report?>(id == report.Id.ToString() ? report : null)))
            .AddDefinition(WorkflowDefinitionBuilder.Define("FLOW", "Flow").ForEntity("Report")
                .State("DRAFT", WorkflowStateType.Initial).State("DONE", WorkflowStateType.Final)
                .On("DRAFT").Command("GO").GoTo("DONE").PostAction<LastAction>()));
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return (sp.GetRequiredService<IWorkflowEngine>(), log);
    }

    [Fact]
    public async Task A_publisher_that_throws_after_the_commit_is_a_warning_and_post_actions_still_run()
    {
        var report = new Report { Id = 1 };
        var (engine, log) = await CreateAsync(report, s => s.Replace(ServiceDescriptor.Singleton<IWorkflowEventPublisher, ThrowingPublisher>()));

        var started = await engine.StartAsync<Report>("FLOW", report, User);
        Assert.True(started.Succeeded);
        Assert.Contains(started.Warnings, w => w.Contains("Event publishing failed") && w.Contains("bus down"));

        var result = await engine.ExecuteAsync(started.Value!.Id, "GO", User);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("Event publishing failed"));
        Assert.Equal(["last"], log.Entries);
        Assert.Equal("DONE", (await engine.GetInstanceAsync(started.Value.Id))!.CurrentStateCode);
    }

    [Fact]
    public async Task A_failed_start_save_puts_the_entitys_previous_state_back()
    {
        var report = new Report { Id = 1, WorkflowState = "LEGACY" };
        var (engine, _) = await CreateAsync(report, s => s.AddSingleton<IWorkflowInstanceStore>(new FailingAddStore(new InMemory.InMemoryWorkflowInstanceStore())));

        await Assert.ThrowsAsync<TimeoutException>(() => engine.StartAsync<Report>("FLOW", report, User));

        Assert.Equal("LEGACY", report.WorkflowState);
    }
}
