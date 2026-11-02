using DungAT.WorkflowEngine.Abstractions.Exceptions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.Tests;

/// <summary>A clock that never moves, so every timestamp ties.</summary>
public sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Makes the engine's "does it already exist?" check miss once, as if another start slipped in right after it.</summary>
public sealed class BlindFirstLookupStore(IWorkflowInstanceStore inner) : IWorkflowInstanceStore
{
    public int BlindLookups { get; set; }

    public Task<WorkflowInstance?> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        if (BlindLookups > 0) { BlindLookups--; return Task.FromResult<WorkflowInstance?>(null); }
        return inner.GetByEntityAsync(entityType, entityId, ct);
    }

    public Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    public Task AddAsync(WorkflowInstance i, WorkflowHistoryEntry h, CancellationToken ct = default) => inner.AddAsync(i, h, ct);
    public Task<bool> TryUpdateAsync(WorkflowInstance u, int v, WorkflowHistoryEntry h, CancellationToken ct = default) => inner.TryUpdateAsync(u, v, h, ct);
    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken ct = default) => inner.GetHistoryAsync(id, ct);
    public Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default) => inner.AddPostActionRunsAsync(runs, ct);
    public Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid id, CancellationToken ct = default) => inner.GetPostActionRunsAsync(id, ct);
}

public class RaceAndOrderingTests
{
    private static readonly WorkflowUser User = new("u", ["Employee"]);

    private static WorkflowDefinitionBuilder.TransitionBuilder Flow() =>
        WorkflowDefinitionBuilder.Define("FLOW", "Flow").ForEntity("Report")
            .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
            .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
            .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED");

    private static async Task<(IWorkflowEngine Engine, IServiceProvider Sp)> CreateAsync(
        Dictionary<string, Report> reports, Action<ServiceCollection>? tweak = null)
    {
        var services = new ServiceCollection();
        tweak?.Invoke(services);
        services.AddWorkflowEngine(wf => wf
            .AddEntity<Report>(e => e.Load((_, id, _) => Task.FromResult(reports.GetValueOrDefault(id))))
            .AddDefinition(Flow()));
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return (sp.GetRequiredService<IWorkflowEngine>(), sp);
    }

    [Fact]
    public async Task A_start_that_loses_the_race_is_reported_as_already_exists_and_the_entity_keeps_the_winners_state()
    {
        var report = new Report { Id = 1 };
        var blind = new BlindFirstLookupStore(new InMemoryWorkflowInstanceStore());
        var (engine, _) = await CreateAsync(new() { ["1"] = report }, s => s.AddSingleton<IWorkflowInstanceStore>(blind));

        var winner = (await engine.StartAsync<Report>("FLOW", report, User)).Value!;
        await engine.ExecuteAsync(winner.Id, "SUBMIT", User);
        Assert.Equal("SUBMITTED", report.WorkflowState);

        blind.BlindLookups = 1; // the second start's existence check misses, so it reaches the insert
        var loser = await engine.StartAsync<Report>("FLOW", report, User);

        Assert.Equal(WorkflowResultStatus.AlreadyExists, loser.Status);
        Assert.Equal("SUBMITTED", report.WorkflowState); // not reset to DRAFT by the failed start
    }

    [Fact]
    public async Task The_in_memory_store_refuses_a_second_instance_for_the_same_entity()
    {
        var store = new InMemoryWorkflowInstanceStore();
        WorkflowInstance Instance() => new() { Id = Guid.NewGuid(), EntityType = "Report", EntityId = "1" };
        await store.AddAsync(Instance(), new WorkflowHistoryEntry { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<WorkflowInstanceAlreadyExistsException>(() =>
            store.AddAsync(Instance(), new WorkflowHistoryEntry { Id = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Entity_type_is_stored_as_registered_so_spelling_cannot_create_a_second_instance()
    {
        var (engine, _) = await CreateAsync(new() { ["1"] = new Report { Id = 1 } });

        var first = await engine.StartAsync("FLOW", "report", "1", User);
        Assert.Equal("Report", first.Value!.EntityType);
        Assert.Equal(WorkflowResultStatus.AlreadyExists, (await engine.StartAsync("FLOW", "REPORT", "1", User)).Status);
        Assert.Equal(first.Value.Id, (await engine.GetInstanceByEntityAsync("rEPORT", "1"))!.Id);
    }

    [Fact]
    public async Task History_keeps_its_order_when_every_timestamp_is_identical()
    {
        var report = new Report { Id = 1 };
        var (engine, _) = await CreateAsync(new() { ["1"] = report }, s => s.AddSingleton<TimeProvider>(new FrozenTimeProvider()));
        var id = (await engine.StartAsync<Report>("FLOW", report, User)).Value!.Id;
        await engine.ExecuteAsync(id, "SUBMIT", User);
        await engine.ExecuteAsync(id, "APPROVE", User);

        var history = await engine.GetHistoryAsync(id);

        Assert.Equal(["START", "SUBMIT", "APPROVE"], history.Select(h => h.CommandCode));
        Assert.Equal([0, 1, 2], history.Select(h => h.Sequence));
    }
}
