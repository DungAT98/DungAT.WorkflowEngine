using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.Tests;

public class HasAttachmentsGuard : IWorkflowGuard
{
    public Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(context.Instance.Data == "attached" ? GuardResult.Pass : GuardResult.Fail("No attachments"));
}

public class EngineTests
{
    private static readonly WorkflowUser Employee = new("emp", ["Employee"]);
    private static readonly WorkflowUser Manager = new("mgr", ["Manager"]);

    private static WorkflowDefinitionBuilder.TransitionBuilder DocApproval() =>
        WorkflowDefinitionBuilder.Define("DOC_APPROVAL", "Document approval")
            .State("DRAFT", WorkflowStateType.Initial)
            .State("SUBMITTED")
            .State("APPROVED", WorkflowStateType.Final)
            .State("REJECTED", WorkflowStateType.Final)
            .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").Guard<HasAttachmentsGuard>()
            .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager")
            .On("SUBMITTED").Command("REJECT").GoTo("REJECTED").AllowRoles("Manager").RequireComment();

    private static async Task<(IWorkflowEngine Engine, IServiceProvider Sp)> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine(wf => wf.AddDefinition(DocApproval()));
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return (sp.GetRequiredService<IWorkflowEngine>(), sp);
    }

    [Fact]
    public async Task Full_flow_records_history()
    {
        var (engine, _) = await CreateAsync();
        var start = await engine.StartAsync("DOC_APPROVAL", "Document", "42", Employee, data: "attached");
        Assert.True(start.Succeeded);
        var id = start.Value!.Id;
        Assert.Equal("DRAFT", start.Value.CurrentStateCode);

        Assert.Equal(["SUBMIT"], (await engine.GetAvailableCommandsAsync(id, Employee)).Select(c => c.Code));
        Assert.True((await engine.ExecuteAsync(id, "SUBMIT", Employee)).Succeeded);

        Assert.Empty(await engine.GetAvailableCommandsAsync(id, Employee));
        Assert.Equal(["APPROVE", "REJECT"], (await engine.GetAvailableCommandsAsync(id, Manager)).Select(c => c.Code));

        var approve = await engine.ExecuteAsync(id, "APPROVE", Manager);
        Assert.Equal("APPROVED", approve.Value!.CurrentStateCode);
        Assert.Equal(["START", "SUBMIT", "APPROVE"], (await engine.GetHistoryAsync(id)).Select(h => h.CommandCode));
    }

    [Fact]
    public async Task Rejections_are_typed_results()
    {
        var (engine, _) = await CreateAsync();
        var noAttachment = (await engine.StartAsync("DOC_APPROVAL", "Document", "1", Employee)).Value!;
        Assert.Equal(WorkflowResultStatus.GuardFailed, (await engine.ExecuteAsync(noAttachment.Id, "SUBMIT", Employee)).Status);
        Assert.Equal(WorkflowResultStatus.InvalidTransition, (await engine.ExecuteAsync(noAttachment.Id, "APPROVE", Manager)).Status);
        Assert.Equal(WorkflowResultStatus.AlreadyExists, (await engine.StartAsync("DOC_APPROVAL", "Document", "1", Employee)).Status);
        Assert.Equal(WorkflowResultStatus.NotFound, (await engine.StartAsync("NOPE", "Document", "2", Employee)).Status);

        var ok = (await engine.StartAsync("DOC_APPROVAL", "Document", "3", Employee, data: "attached")).Value!;
        await engine.ExecuteAsync(ok.Id, "SUBMIT", Employee);
        Assert.Equal(WorkflowResultStatus.NotAllowed, (await engine.ExecuteAsync(ok.Id, "APPROVE", Employee)).Status);
        Assert.Equal(WorkflowResultStatus.CommentRequired, (await engine.ExecuteAsync(ok.Id, "REJECT", Manager)).Status);
        Assert.True((await engine.ExecuteAsync(ok.Id, "REJECT", Manager, comment: "incomplete")).Succeeded);
    }

    [Fact]
    public async Task Stale_row_version_is_a_conflict()
    {
        var (_, sp) = await CreateAsync();
        var store = sp.GetRequiredService<IWorkflowInstanceStore>();
        var engine = sp.GetRequiredService<IWorkflowEngine>();
        var instance = (await engine.StartAsync("DOC_APPROVAL", "Document", "9", Employee, data: "attached")).Value!;
        var history = new WorkflowHistoryEntry { Id = Guid.NewGuid(), InstanceId = instance.Id };

        Assert.True(await store.TryUpdateAsync(instance, instance.RowVersion, history));
        Assert.False(await store.TryUpdateAsync(instance, instance.RowVersion, history));
    }

    [Fact]
    public async Task Republishing_unchanged_definition_keeps_version()
    {
        var (_, sp) = await CreateAsync();
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        var latest = await sp.GetRequiredService<IWorkflowDefinitionStore>().GetAsync("DOC_APPROVAL");
        Assert.Equal(1, latest!.Version);
    }
}

public class ValidatorTests
{
    [Fact]
    public void Rejects_bad_definitions()
    {
        var ex = Assert.Throws<WorkflowDefinitionException>(() =>
            WorkflowDefinitionBuilder.Define("X", "X")
                .State("A", WorkflowStateType.Initial)
                .State("B", WorkflowStateType.Final)
                .State("ORPHAN")
                .On("B").Command("GO").GoTo("A")
                .On("A").Command("GO").GoTo("MISSING")
                .Build());
        Assert.Contains(ex.Errors, e => e.Contains("Final state 'B'"));
        Assert.Contains(ex.Errors, e => e.Contains("unknown state 'MISSING'"));
    }

    [Fact]
    public void Detects_unreachable_and_missing_initial()
    {
        var ex = Assert.Throws<WorkflowDefinitionException>(() =>
            WorkflowDefinitionBuilder.Define("X", "X").State("A").Build());
        Assert.Contains(ex.Errors, e => e.Contains("Exactly one Initial"));

        ex = Assert.Throws<WorkflowDefinitionException>(() =>
            WorkflowDefinitionBuilder.Define("X", "X").State("A", WorkflowStateType.Initial).State("B").Build());
        Assert.Contains(ex.Errors, e => e.Contains("'B' is unreachable"));
    }
}
