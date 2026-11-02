using DungAT.WorkflowEngine.Abstractions.Attributes;
using DungAT.WorkflowEngine.Abstractions.Entities;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.Tests;

public interface IHasAmount { decimal Amount { get; } }

public sealed class Report : IHasAmount, IWorkflowStateful
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public int Attachments { get; set; }
    public string? WorkflowState { get; set; }
}

public sealed class Invoice : IHasAmount
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ReportWithinBudgetGuard : WorkflowGuard<Report>
{
    protected override Task<GuardResult> EvaluateAsync(Report report, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(report.Amount <= 1000 ? GuardResult.Pass : GuardResult.Fail("Over budget."));
}

[ForEntity(typeof(Report))]
public sealed class ReportHasAttachmentsGuard : IWorkflowGuard
{
    public Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(context.GetEntity<Report>().Attachments > 0 ? GuardResult.Pass : GuardResult.Fail("Attach a file."));
}

public sealed class InvoiceOnlyGuard : WorkflowGuard<Invoice>
{
    protected override Task<GuardResult> EvaluateAsync(Invoice invoice, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(GuardResult.Pass);
}

/// <summary>Matches any registered entity that implements the interface (Report and Invoice).</summary>
[ForEntity(typeof(IHasAmount))]
public sealed class PositiveAmountGuard : IWorkflowGuard
{
    public Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(context.GetEntity<IHasAmount>().Amount > 0 ? GuardResult.Pass : GuardResult.Fail("Amount must be positive."));
}

public sealed class AlwaysOpenGuard : IWorkflowGuard
{
    public Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(GuardResult.Pass);
}

/// <summary>Pretends another user won the race for every update.</summary>
public sealed class ConflictingInstanceStore(IWorkflowInstanceStore inner) : IWorkflowInstanceStore
{
    public Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default) => inner.GetAsync(id, ct);
    public Task<WorkflowInstance?> GetByEntityAsync(string t, string id, CancellationToken ct = default) => inner.GetByEntityAsync(t, id, ct);
    public Task AddAsync(WorkflowInstance i, WorkflowHistoryEntry h, CancellationToken ct = default) => inner.AddAsync(i, h, ct);
    public Task<bool> TryUpdateAsync(WorkflowInstance u, int v, WorkflowHistoryEntry h, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid id, CancellationToken ct = default) => inner.GetHistoryAsync(id, ct);
    public Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default) => inner.AddPostActionRunsAsync(runs, ct);
    public Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid id, CancellationToken ct = default) => inner.GetPostActionRunsAsync(id, ct);
}

public class EntityLinkTests
{
    private static readonly WorkflowUser User = new("u", ["Employee"]);

    private sealed class Rig(IServiceProvider sp, Dictionary<string, Report> reports)
    {
        public IWorkflowEngine Engine { get; } = sp.GetRequiredService<IWorkflowEngine>();
        public IServiceProvider Services { get; } = sp;
        public Dictionary<string, Report> Reports { get; } = reports;
    }

    private static WorkflowDefinitionBuilder.TransitionBuilder ReportWorkflow(string code = "REPORT", string? entity = "Report") =>
        WorkflowDefinitionBuilder.Define(code, "Report approval").ForEntity(entity!)
            .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
            .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .Guard<ReportWithinBudgetGuard>().Guard<ReportHasAttachmentsGuard>().Guard<PositiveAmountGuard>().Guard<AlwaysOpenGuard>()
            .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED");

    private static async Task<Rig> CreateAsync(Action<ServiceCollection>? tweak = null, bool publish = true, params Report[] seed)
    {
        var reports = seed.ToDictionary(r => r.Id.ToString());
        var services = new ServiceCollection();
        services.AddWorkflowEngine(wf => wf
            .AddEntity<Report>(e => e.Load((_, id, _) => Task.FromResult(reports.GetValueOrDefault(id))))
            .AddEntity<Invoice>(e => e.Load((_, _, _) => Task.FromResult<Invoice?>(null)))
            .AddGuard<InvoiceOnlyGuard>()
            .AddDefinition(ReportWorkflow()));
        tweak?.Invoke(services);
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        if (publish) await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return new Rig(sp, reports);
    }

    [Fact]
    public async Task Guards_receive_the_loaded_entity_typed_and_untyped()
    {
        var report = new Report { Id = 1, Amount = 5000, Attachments = 0 };
        var rig = await CreateAsync(seed: report);
        var id = (await rig.Engine.StartAsync<Report>("REPORT", report, User)).Value!.Id;

        var overBudget = await rig.Engine.ExecuteAsync(id, "SUBMIT", User);
        Assert.Equal((WorkflowResultStatus.GuardFailed, "Over budget."), (overBudget.Status, overBudget.Message));

        report.Amount = 500;
        var noFile = await rig.Engine.ExecuteAsync(id, "SUBMIT", User);
        Assert.Equal("Attach a file.", noFile.Message);

        report.Attachments = 1;
        Assert.True((await rig.Engine.ExecuteAsync(id, "SUBMIT", User)).Succeeded);
    }

    [Fact]
    public async Task Entity_state_is_mirrored_on_start_and_every_transition()
    {
        var report = new Report { Id = 7, Amount = 10, Attachments = 1 };
        var rig = await CreateAsync(seed: report);
        var id = (await rig.Engine.StartAsync("REPORT", "Report", "7", User)).Value!.Id; // string overload also loads the entity
        Assert.Equal("DRAFT", report.WorkflowState);

        await rig.Engine.ExecuteAsync(id, "SUBMIT", User);
        Assert.Equal("SUBMITTED", report.WorkflowState);
        await rig.Engine.ExecuteAsync(id, "APPROVE", User);
        Assert.Equal("APPROVED", report.WorkflowState);
    }

    [Fact]
    public async Task Entity_state_is_restored_when_the_save_loses_a_race()
    {
        var report = new Report { Id = 3, Amount = 10, Attachments = 1 };
        var rig = await CreateAsync(s => s.Replace(ServiceDescriptor.Singleton<IWorkflowInstanceStore>(
            new ConflictingInstanceStore(new InMemory.InMemoryWorkflowInstanceStore()))), seed: report);
        var id = (await rig.Engine.StartAsync<Report>("REPORT", report, User)).Value!.Id;

        var result = await rig.Engine.ExecuteAsync(id, "SUBMIT", User);
        Assert.Equal(WorkflowResultStatus.ConcurrencyConflict, result.Status);
        Assert.Equal("DRAFT", report.WorkflowState);
    }

    [Fact]
    public async Task Starting_validates_the_entity_and_the_workflows_entity_type()
    {
        var rig = await CreateAsync(seed: new Report { Id = 1 });
        Assert.Equal(WorkflowResultStatus.NotFound, (await rig.Engine.StartAsync("REPORT", "Report", "404", User)).Status);
        Assert.Equal(WorkflowResultStatus.EntityMismatch, (await rig.Engine.StartAsync("REPORT", "Invoice", "1", User)).Status);

        var start = await rig.Engine.StartAsync<Report>("REPORT", new Report { Id = 1 }, User);
        Assert.Equal(("Report", "1"), (start.Value!.EntityType, start.Value.EntityId));
        Assert.Equal(start.Value.Id, (await rig.Engine.GetInstanceForAsync(new Report { Id = 1 }))!.Id);
        Assert.Equal(WorkflowResultStatus.AlreadyExists, (await rig.Engine.StartAsync<Report>("REPORT", new Report { Id = 1 }, User)).Status);
    }

    [Fact]
    public async Task Unregistered_entity_types_cannot_be_used_with_the_typed_overloads()
    {
        var rig = await CreateAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.StartAsync("REPORT", new object(), User));
        Assert.Contains("AddEntity", ex.Message);
    }

    [Fact]
    public async Task Publishing_rejects_guards_made_for_another_entity()
    {
        var wrong = WorkflowDefinitionBuilder.Define("BAD", "Bad").ForEntity("Report")
            .State("A", WorkflowStateType.Initial).State("B", WorkflowStateType.Final)
            .On("A").Command("GO").GoTo("B").Guard<InvoiceOnlyGuard>();
        var entityless = WorkflowDefinitionBuilder.Define("LOOSE", "Loose")
            .State("A", WorkflowStateType.Initial).State("B", WorkflowStateType.Final)
            .On("A").Command("GO").GoTo("B").Guard<ReportWithinBudgetGuard>();

        var ex = await Assert.ThrowsAsync<WorkflowDefinitionException>(() => CreateWithAsync(wrong, entityless));
        Assert.Contains(ex.Errors, e => e.Contains("BAD") && e.Contains("'InvoiceOnlyGuard' cannot be used with Report; it is for Invoice"));
        Assert.Contains(ex.Errors, e => e.Contains("LOOSE") && e.Contains("has no entity type"));
    }

    private static async Task CreateWithAsync(params WorkflowDefinitionBuilder.TransitionBuilder[] definitions)
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine(wf =>
        {
            wf.AddEntity<Report>(e => e.Load((_, _, _) => Task.FromResult<Report?>(null)))
              .AddEntity<Invoice>(e => e.Load((_, _, _) => Task.FromResult<Invoice?>(null)));
            foreach (var d in definitions) wf.AddDefinition(d);
        });
        using var scope = services.BuildServiceProvider().CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
    }

    [Fact]
    public async Task A_mismatched_guard_that_slips_past_publishing_fails_loudly_at_runtime()
    {
        var report = new Report { Id = 9, Amount = 10, Attachments = 1 };
        var rig = await CreateAsync(seed: report);
        var store = rig.Services.GetRequiredService<IWorkflowDefinitionStore>();
        var bad = WorkflowDefinitionBuilder.Define("SNEAKY", "Sneaky").ForEntity("Report")
            .State("A", WorkflowStateType.Initial).State("B", WorkflowStateType.Final)
            .On("A").Command("GO").GoTo("B").Guard<InvoiceOnlyGuard>().Build();
        await store.PublishAsync(bad); // bypasses WorkflowDefinitionPublisher validation

        var id = (await rig.Engine.StartAsync<Report>("SNEAKY", report, User)).Value!.Id;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.ExecuteAsync(id, "GO", User));
        Assert.Contains("'InvoiceOnlyGuard' is for Invoice", ex.Message);
    }

    [Fact]
    public async Task Registry_reports_which_entities_each_component_supports()
    {
        var rig = await CreateAsync();
        var registry = rig.Services.GetRequiredService<IWorkflowComponentRegistry>();

        Assert.Equal(["Invoice", "Report"], registry.Entities);
        var byName = registry.Guards.ToDictionary(g => g.Name);
        Assert.Equal(["Report"], byName["ReportWithinBudgetGuard"].EntityNames);
        Assert.Equal(["Invoice"], byName["InvoiceOnlyGuard"].EntityNames);
        Assert.Equal(["Invoice", "Report"], byName["PositiveAmountGuard"].EntityNames); // via the interface
        Assert.True(byName["AlwaysOpenGuard"].AppliesToAnyEntity);
        Assert.False(byName["ReportHasAttachmentsGuard"].AppliesToAnyEntity);
    }

    [Fact]
    public void Entity_without_an_id_property_or_loader_is_rejected_when_registering()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddWorkflowEngine(wf => wf.AddEntity<Report>()));
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddWorkflowEngine(wf => wf.AddEntity<NoId>(e => e.Load((_, _, _) => Task.FromResult<NoId?>(null)))));
    }

    private sealed class NoId { }
}
