using DungAT.WorkflowEngine.Abstractions.Entities;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore.Tests;

/// <summary>Guid key, state mirrored through <see cref="IWorkflowStateful"/>.</summary>
public class EfReport : IWorkflowStateful
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public int Attachments { get; set; }
    public string? WorkflowState { get; set; }
}

public enum TicketStatus { Draft, Submitted, Approved }

/// <summary>Int key, state mirrored into an enum column through <c>SyncState</c>.</summary>
public class Ticket
{
    public int Id { get; set; }
    public TicketStatus Status { get; set; }
}

public sealed class EfReportHasAttachmentsGuard : WorkflowGuard<EfReport>
{
    protected override Task<GuardResult> EvaluateAsync(EfReport report, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(report.Attachments > 0 ? GuardResult.Pass : GuardResult.Fail("Attach a file."));
}

public sealed class EfAutoApprove : WorkflowPostAction<Ticket>
{
    protected override async Task ExecuteAsync(Ticket ticket, WorkflowPostContext context, CancellationToken ct) =>
        await context.FollowUpAsync("APPROVE", "Auto-approved.");
}

public sealed class EfFlakyAction : IWorkflowPostAction
{
    public Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default) =>
        throw new TimeoutException("SMTP server timed out");
}

public sealed class EfEntityLinkTests : IAsyncLifetime
{
    private static readonly WorkflowUser User = new("u", ["Employee"]);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _root = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddWorkflowEngine(wf => wf
            .AddEntity<EfReport, AppDbContext>()
            .AddEntity<Ticket, AppDbContext>(e => e.SyncState((t, state) => t.Status = Enum.Parse<TicketStatus>(state, ignoreCase: true)))
            .AddDefinition(WorkflowDefinitionBuilder.Define("REPORT", "Report").ForEntity("EfReport")
                .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").Guard<EfReportHasAttachmentsGuard>()
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED"))
            .AddDefinition(WorkflowDefinitionBuilder.Define("TICKET", "Ticket").ForEntity("Ticket")
                .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED"))
            .AddDefinition(WorkflowDefinitionBuilder.Define("TICKET_AUTO", "Ticket with post actions").ForEntity("Ticket")
                .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").PostAction<EfAutoApprove>()
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").PostAction<EfFlakyAction>())
            .UseEntityFrameworkCore<AppDbContext>());
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
    }

    public async Task DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Guid_keyed_entity_is_loaded_for_guards_and_its_status_is_saved_with_the_workflow()
    {
        var report = new EfReport { Title = "Q3", Attachments = 0 };
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reports.Add(report);
            await db.SaveChangesAsync();
        }

        Guid id;
        using (var scope = _root.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            id = (await engine.StartAsync("REPORT", "EfReport", report.Id.ToString(), User)).Value!.Id; // loaded by Guid key
            var rejected = await engine.ExecuteAsync(id, "SUBMIT", User);
            Assert.Equal("Attach a file.", rejected.Message);
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Reports.FindAsync(report.Id))!.Attachments = 2;
            await db.SaveChangesAsync();
        }

        using (var scope = _root.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            Assert.True((await engine.ExecuteAsync(id, "SUBMIT", User)).Succeeded);
            Assert.True((await engine.ExecuteAsync(id, "APPROVE", User)).Succeeded); // second call in one scope reuses the tracked row
        }

        using (var scope = _root.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("APPROVED", (await db.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).WorkflowState);
            var instance = (await scope.ServiceProvider.GetRequiredService<IWorkflowEngine>().GetInstanceAsync(id))!;
            Assert.Equal("APPROVED", instance.CurrentStateCode);
        }
    }

    [Fact]
    public async Task Int_keyed_entity_mirrors_state_into_an_enum_column()
    {
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tickets.Add(new Ticket { Id = 5 });
            await db.SaveChangesAsync();
        }

        using var scope = _root.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var ticket = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tickets.FindAsync(5);
        var id = (await engine.StartAsync<Ticket>("TICKET", ticket!, User)).Value!.Id; // id read from the primary key
        await engine.ExecuteAsync(id, "SUBMIT", User);

        using var check = _root.CreateScope();
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().Tickets.AsNoTracking().SingleAsync(t => t.Id == 5);
        Assert.Equal(TicketStatus.Submitted, stored.Status);
    }

    [Fact]
    public async Task Unknown_or_malformed_ids_are_not_found()
    {
        using var scope = _root.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        Assert.Equal(WorkflowResultStatus.NotFound, (await engine.StartAsync("REPORT", "EfReport", Guid.NewGuid().ToString(), User)).Status);
        Assert.Equal(WorkflowResultStatus.NotFound, (await engine.StartAsync("REPORT", "EfReport", "not-a-guid", User)).Status);
        Assert.Equal(WorkflowResultStatus.NotFound, (await engine.StartAsync("TICKET", "Ticket", "99", User)).Status);
    }

    [Fact]
    public async Task A_lost_race_commits_neither_the_workflow_nor_the_entity_status()
    {
        var report = new EfReport { Title = "Race", Attachments = 1 };
        Guid id;
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            id = (await seed.ServiceProvider.GetRequiredService<IWorkflowEngine>().StartAsync<EfReport>("REPORT", report, User)).Value!.Id;
        }

        // Two users load the same instance before either saves.
        using var loser = _root.CreateScope();
        var loserStore = loser.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var stale = (await loserStore.GetAsync(id))!;
        using (var winner = _root.CreateScope())
            Assert.True((await winner.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(id, "SUBMIT", User)).Succeeded);

        var loserDb = loser.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracked = (await loserDb.Reports.FindAsync(report.Id))!;
        tracked.WorkflowState = "SUBMITTED"; // what the engine would have written
        var history = new WorkflowHistoryEntry { Id = Guid.NewGuid(), InstanceId = id, CommandCode = "SUBMIT", ToStateCode = "SUBMITTED" };
        Assert.False(await loserStore.TryUpdateAsync(stale with { CurrentStateCode = "SUBMITTED" }, stale.RowVersion, history));

        using var check = _root.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var instance = (await check.ServiceProvider.GetRequiredService<IWorkflowEngine>().GetInstanceAsync(id))!;
        Assert.Equal(1, instance.RowVersion); // only the winner's update exists
        Assert.Equal(2, (await check.ServiceProvider.GetRequiredService<IWorkflowEngine>().GetHistoryAsync(id)).Count); // START + the winner's SUBMIT
        Assert.Equal("SUBMITTED", (await checkDb.Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).WorkflowState); // winner's write, not duplicated
    }

    [Fact]
    public async Task Follow_up_chain_commits_every_step_and_post_action_outcomes_are_stored()
    {
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tickets.Add(new Ticket { Id = 21 });
            await db.SaveChangesAsync();
        }

        Guid id;
        WorkflowResult<WorkflowInstance> result;
        using (var scope = _root.CreateScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            id = (await engine.StartAsync("TICKET_AUTO", "Ticket", "21", User)).Value!.Id;
            result = await engine.ExecuteAsync(id, "SUBMIT", User); // SUBMIT -> auto APPROVE (follow-up) -> flaky action fails
        }

        Assert.True(result.Succeeded);
        Assert.Equal(["EfFlakyAction failed: SMTP server timed out"], result.Warnings); // surfaced from the nested step

        using var check = _root.CreateScope();
        var checkEngine = check.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var instance = (await checkEngine.GetInstanceAsync(id))!;
        Assert.Equal(("APPROVED", 2), (instance.CurrentStateCode, instance.RowVersion)); // two committed steps
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().Tickets.AsNoTracking().SingleAsync(t => t.Id == 21);
        Assert.Equal(TicketStatus.Approved, stored.Status);

        var history = await checkEngine.GetHistoryAsync(id);
        Assert.Equal(["START", "SUBMIT", "APPROVE"], history.Select(h => h.CommandCode));
        var runs = await checkEngine.GetPostActionRunsAsync(id);
        Assert.Equal(new[] { "EfAutoApprove", "EfFlakyAction" }, runs.Select(r => r.ActionName).Order());
        var flaky = runs.Single(r => r.ActionName == "EfFlakyAction");
        Assert.False(flaky.Succeeded);
        Assert.Equal("SMTP server timed out", flaky.Error);
        Assert.Equal(history.Single(h => h.CommandCode == "APPROVE").Id, flaky.HistoryId);
        Assert.Equal(history.Single(h => h.CommandCode == "SUBMIT").Id, runs.Single(r => r.ActionName == "EfAutoApprove").HistoryId);
    }
}
