using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.Tests;

public sealed class ActionLog
{
    public List<string> Entries { get; } = [];
}

public sealed class FirstAction(ActionLog log) : WorkflowPostAction<Report>
{
    protected override Task ExecuteAsync(Report report, WorkflowPostContext ctx, CancellationToken ct)
    {
        log.Entries.Add($"first:{report.WorkflowState}"); // the entity already shows the new state
        return Task.CompletedTask;
    }
}

public sealed class ThrowingAction : IWorkflowPostAction
{
    public Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default) =>
        throw new InvalidOperationException("boom");
}

public sealed class LastAction(ActionLog log) : IWorkflowPostAction
{
    public Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default)
    {
        log.Entries.Add("last");
        return Task.CompletedTask;
    }
}

public sealed class EnteredAction(ActionLog log) : IWorkflowPostAction
{
    public Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default)
    {
        log.Entries.Add($"entered:{context.ToStateCode}:{(context.Transition is null ? "start" : context.Transition.CommandCode)}");
        return Task.CompletedTask;
    }
}

public sealed class AutoApproveAction(ActionLog log) : WorkflowPostAction<Report>
{
    protected override async Task ExecuteAsync(Report report, WorkflowPostContext ctx, CancellationToken ct)
    {
        var result = await ctx.FollowUpAsync("APPROVE", "Auto-approved.");
        log.Entries.Add($"approve:{result.Status}");
    }
}

public sealed class PingPongAction(ActionLog log) : IWorkflowPostAction
{
    public async Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default)
    {
        var result = await context.FollowUpAsync("PING");
        log.Entries.Add($"ping:{result.Status}");
    }
}

public sealed class InvoiceOnlyPostAction : WorkflowPostAction<Invoice>
{
    protected override Task ExecuteAsync(Invoice invoice, WorkflowPostContext ctx, CancellationToken ct) => Task.CompletedTask;
}

public class PostActionTests
{
    private static readonly WorkflowUser Employee = new("emp", ["Employee"]);

    private sealed class Rig(IServiceProvider sp, ActionLog log, Dictionary<string, Report> reports)
    {
        public IWorkflowEngine Engine { get; } = sp.GetRequiredService<IWorkflowEngine>();
        public IServiceProvider Services { get; } = sp;
        public ActionLog Log { get; } = log;
        public Dictionary<string, Report> Reports { get; } = reports;
    }

    private static async Task<Rig> CreateAsync(Action<WorkflowEngineBuilder> configure, Action<ServiceCollection>? tweak = null, params Report[] seed)
    {
        var reports = seed.ToDictionary(r => r.Id.ToString());
        var log = new ActionLog();
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddWorkflowEngine(wf =>
        {
            wf.AddEntity<Report>(e => e.Load((_, id, _) => Task.FromResult(reports.GetValueOrDefault(id))));
            configure(wf);
        });
        tweak?.Invoke(services);
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return new Rig(sp, log, reports);
    }

    private static WorkflowDefinitionBuilder.TransitionBuilder Simple(string code, Func<WorkflowDefinitionBuilder.TransitionBuilder, WorkflowDefinitionBuilder.TransitionBuilder> go) =>
        go(WorkflowDefinitionBuilder.Define(code, code).ForEntity("Report")
            .State("DRAFT", WorkflowStateType.Initial).State("DONE", WorkflowStateType.Final)
            .On("DRAFT").Command("GO").GoTo("DONE"));

    [Fact]
    public async Task Actions_run_after_the_save_in_order_and_a_failure_is_only_a_warning()
    {
        var report = new Report { Id = 1 };
        var rig = await CreateAsync(wf => wf.AddDefinition(
            Simple("PA", t => t.PostAction<FirstAction>().PostAction<ThrowingAction>().PostAction<LastAction>())), seed: report);
        var id = (await rig.Engine.StartAsync<Report>("PA", report, Employee)).Value!.Id;

        var result = await rig.Engine.ExecuteAsync(id, "GO", Employee);

        Assert.True(result.Succeeded);
        Assert.Equal(["ThrowingAction failed: boom"], result.Warnings);
        Assert.Equal(["first:DONE", "last"], rig.Log.Entries); // later actions still run
        Assert.Equal("DONE", (await rig.Engine.GetInstanceAsync(id))!.CurrentStateCode); // the transition stayed

        var runs = await rig.Engine.GetPostActionRunsAsync(id);
        Assert.Equal(["FirstAction", "ThrowingAction", "LastAction"], runs.Select(r => r.ActionName));
        Assert.Equal([true, false, true], runs.Select(r => r.Succeeded));
        Assert.Equal("boom", runs[1].Error);
        var goEntry = (await rig.Engine.GetHistoryAsync(id)).Single(h => h.CommandCode == "GO");
        Assert.All(runs, r => Assert.Equal(goEntry.Id, r.HistoryId));
    }

    [Fact]
    public async Task Actions_do_not_run_when_a_guard_blocks_or_the_save_loses_the_race()
    {
        var expensive = new Report { Id = 1, Amount = 99999, Attachments = 1 };
        var guarded = await CreateAsync(wf => wf.AddDefinition(
            Simple("PA", t => t.Guard<ReportWithinBudgetGuard>().PostAction<LastAction>())), seed: expensive);
        var id = (await guarded.Engine.StartAsync<Report>("PA", expensive, Employee)).Value!.Id;
        Assert.Equal(WorkflowResultStatus.GuardFailed, (await guarded.Engine.ExecuteAsync(id, "GO", Employee)).Status);
        Assert.Empty(guarded.Log.Entries);

        var cheap = new Report { Id = 2, Amount = 1 };
        var racing = await CreateAsync(wf => wf.AddDefinition(Simple("PA", t => t.PostAction<LastAction>())),
            s => s.Replace(ServiceDescriptor.Singleton<IWorkflowInstanceStore>(new ConflictingInstanceStore(new InMemory.InMemoryWorkflowInstanceStore()))),
            cheap);
        var raceId = (await racing.Engine.StartAsync<Report>("PA", cheap, Employee)).Value!.Id;
        Assert.Equal(WorkflowResultStatus.ConcurrencyConflict, (await racing.Engine.ExecuteAsync(raceId, "GO", Employee)).Status);
        Assert.Empty(racing.Log.Entries);
    }

    private static WorkflowDefinitionBuilder.TransitionBuilder AutoApproval() =>
        WorkflowDefinitionBuilder.Define("AUTO", "Auto").ForEntity("Report")
            .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
            .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").PostAction<AutoApproveAction>()
            .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager").Guard<ReportWithinBudgetGuard>();

    [Fact]
    public async Task A_follow_up_runs_as_the_system_user_but_still_has_to_pass_guards()
    {
        var cheap = new Report { Id = 1, Amount = 100 };
        var expensive = new Report { Id = 2, Amount = 50000 };
        var rig = await CreateAsync(wf => wf.AddDefinition(AutoApproval()), seed: [cheap, expensive]);

        var cheapId = (await rig.Engine.StartAsync<Report>("AUTO", cheap, Employee)).Value!.Id;
        var submitted = await rig.Engine.ExecuteAsync(cheapId, "SUBMIT", Employee); // Employee has no Manager role
        Assert.True(submitted.Succeeded);
        Assert.Equal("APPROVED", (await rig.Engine.GetInstanceAsync(cheapId))!.CurrentStateCode);
        Assert.Equal("APPROVED", cheap.WorkflowState);
        var approve = (await rig.Engine.GetHistoryAsync(cheapId)).Single(h => h.CommandCode == "APPROVE");
        Assert.Equal(("system", "Auto-approved."), (approve.ExecutedBy, approve.Comment));

        var expensiveId = (await rig.Engine.StartAsync<Report>("AUTO", expensive, Employee)).Value!.Id;
        await rig.Engine.ExecuteAsync(expensiveId, "SUBMIT", Employee);
        Assert.Equal("SUBMITTED", (await rig.Engine.GetInstanceAsync(expensiveId))!.CurrentStateCode);
        Assert.Equal(["approve:Success", "approve:GuardFailed"], rig.Log.Entries);

        // Only the engine's own system user bypasses roles; a person who happens to be called "system" does not.
        Assert.Equal(WorkflowResultStatus.NotAllowed, (await rig.Engine.ExecuteAsync(expensiveId, "APPROVE", new WorkflowUser("system"))).Status);
        Assert.Equal(WorkflowResultStatus.GuardFailed, (await rig.Engine.ExecuteAsync(expensiveId, "APPROVE", WorkflowUser.System)).Status);
        Assert.True(WorkflowUser.System.IsSystem);
        Assert.False(new WorkflowUser("system").IsSystem);
    }

    [Fact]
    public async Task Chains_of_follow_ups_stop_at_the_depth_limit()
    {
        var report = new Report { Id = 1 };
        var rig = await CreateAsync(wf => wf
            .Configure(o => o.MaxChainDepth = 3)
            .AddDefinition(WorkflowDefinitionBuilder.Define("PING", "Ping").ForEntity("Report")
                .State("A", WorkflowStateType.Initial).State("B")
                .On("A").Command("PING").GoTo("B").PostAction<PingPongAction>()
                .On("B").Command("PING").GoTo("A").PostAction<PingPongAction>()), seed: report);
        var id = (await rig.Engine.StartAsync<Report>("PING", report, Employee)).Value!.Id;

        var result = await rig.Engine.ExecuteAsync(id, "PING", Employee);

        Assert.True(result.Succeeded);
        // START + the host's PING + three follow-ups, then the fourth is refused.
        Assert.Equal(5, (await rig.Engine.GetHistoryAsync(id)).Count);
        Assert.Equal("ping:ChainDepthExceeded", rig.Log.Entries[0]);
        Assert.Equal(["ping:ChainDepthExceeded", "ping:Success", "ping:Success", "ping:Success"], rig.Log.Entries);
    }

    [Fact]
    public async Task State_entry_actions_run_after_the_transition_actions_and_on_start()
    {
        var report = new Report { Id = 1 };
        var rig = await CreateAsync(wf => wf.AddDefinition(
            WorkflowDefinitionBuilder.Define("ENTER", "Enter").ForEntity("Report")
                .State("DRAFT", WorkflowStateType.Initial).State("DONE", WorkflowStateType.Final)
                .On("DRAFT").Command("GO").GoTo("DONE").PostAction<LastAction>()
                .OnEnter("DRAFT").PostAction<EnteredAction>()
                .OnEnter("DONE").PostAction<EnteredAction>()), seed: report);

        var id = (await rig.Engine.StartAsync<Report>("ENTER", report, Employee)).Value!.Id;
        Assert.Equal(["entered:DRAFT:start"], rig.Log.Entries);

        await rig.Engine.ExecuteAsync(id, "GO", Employee);
        Assert.Equal(["entered:DRAFT:start", "last", "entered:DONE:GO"], rig.Log.Entries);
    }

    [Fact]
    public async Task Publishing_rejects_unknown_or_wrong_entity_post_actions_and_unknown_enter_states()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine(wf => wf
            .AddEntity<Report>(e => e.Load((_, _, _) => Task.FromResult<Report?>(null)))
            .AddEntity<Invoice>(e => e.Load((_, _, _) => Task.FromResult<Invoice?>(null)))
            .AddDefinition(Simple("WRONG", t => t.PostAction<InvoiceOnlyPostAction>().PostAction("Nope"))));
        using var scope = services.BuildServiceProvider().CreateScope();
        var ex = await Assert.ThrowsAsync<WorkflowDefinitionException>(() =>
            scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync());
        Assert.Contains(ex.Errors, e => e.Contains("Post action 'InvoiceOnlyPostAction' cannot be used with Report; it is for Invoice"));
        Assert.Contains(ex.Errors, e => e.Contains("Post action 'Nope' is not registered"));

        var unknown = Assert.Throws<WorkflowDefinitionException>(() => WorkflowDefinitionBuilder.Define("X", "X")
            .State("A", WorkflowStateType.Initial).OnEnter("MISSING").PostAction("Any").Build());
        Assert.Contains(unknown.Errors, e => e.Contains("OnEnter references unknown state 'MISSING'"));
    }

    [Fact]
    public void Definitions_stored_before_post_actions_existed_still_load()
    {
        const string old = """
            {"Code":"OLD","Name":"Old","Version":1,"States":[{"Code":"A","Name":"A","Type":1,"Order":0},{"Code":"B","Name":"B","Type":2,"Order":1}],
             "Commands":[{"Code":"GO","Name":"GO"}],
             "Transitions":[{"FromStateCode":"A","CommandCode":"GO","ToStateCode":"B","AllowedRoles":[],"GuardNames":[],"EffectNames":[],"RequireComment":false}]}
            """;
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(old)!;
        Assert.Empty(definition.Transitions[0].PostActionNames);
        Assert.Empty(definition.States[0].PostActionNames);
    }
}
