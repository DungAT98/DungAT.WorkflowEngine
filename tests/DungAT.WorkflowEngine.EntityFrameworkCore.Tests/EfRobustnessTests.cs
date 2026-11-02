using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Exceptions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore.Tests;

public sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Inserts a competing definition row just before the next save of a new one, as another app instance would.</summary>
public sealed class CompetingPublisherInterceptor : SaveChangesInterceptor
{
    public string? CompetingJson { get; set; }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var db = eventData.Context!;
        if (CompetingJson is { } json && db.ChangeTracker.Entries<StoredWorkflowDefinition>().Any(e => e.State == EntityState.Added))
        {
            CompetingJson = null;
            await db.Database.ExecuteSqlRawAsync("INSERT INTO WorkflowDefinitions (Code, Version, Json) VALUES ({0}, {1}, {2})", ["RACE", 1, json], ct);
        }
        return result;
    }
}

public sealed class EfRobustnessTests : IAsyncLifetime
{
    private static readonly WorkflowUser User = new("u", ["Employee"]);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CompetingPublisherInterceptor _competitor = new();
    private ServiceProvider _root = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FrozenTimeProvider());
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection).AddInterceptors(_competitor));
        services.AddWorkflowEngine(wf => wf
            .AddEntity<EfReport, AppDbContext>()
            .AddEntity<Ticket, AppDbContext>(e => e.SyncState((t, s) => t.Status = Enum.Parse<TicketStatus>(s, true)))
            .AddDefinition(WorkflowDefinitionBuilder.Define("REPORT", "Report").ForEntity("EfReport")
                .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED")
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").Guard<EfReportHasAttachmentsGuard>())
            .AddDefinition(WorkflowDefinitionBuilder.Define("TICKET", "Ticket").ForEntity("Ticket")
                .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED"))
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
    public async Task The_unique_index_turns_a_second_instance_for_an_entity_into_already_exists()
    {
        using var scope = _root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        WorkflowInstance Instance() => new() { Id = Guid.NewGuid(), EntityType = "Ticket", EntityId = "1", DefinitionCode = "TICKET", CurrentStateCode = "DRAFT" };
        WorkflowHistoryEntry History(Guid id) => new() { Id = Guid.NewGuid(), InstanceId = id, CommandCode = "START", ToStateCode = "DRAFT" };
        var first = Instance();
        await store.AddAsync(first, History(first.Id));

        var second = Instance();
        var ex = await Assert.ThrowsAsync<WorkflowInstanceAlreadyExistsException>(() => store.AddAsync(second, History(second.Id)));
        Assert.Equal(("Ticket", "1"), (ex.EntityType, ex.EntityId));
        Assert.Equal(first.Id, (await store.GetByEntityAsync("Ticket", "1"))!.Id); // the store is still usable and holds the first one
    }

    [Fact]
    public async Task History_keeps_its_order_when_every_timestamp_is_identical()
    {
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tickets.Add(new Ticket { Id = 61 });
            await db.SaveChangesAsync();
        }

        using var scope = _root.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
        var id = (await engine.StartAsync("TICKET", "Ticket", "61", User)).Value!.Id;
        await engine.ExecuteAsync(id, "SUBMIT", User);
        await engine.ExecuteAsync(id, "APPROVE", User);

        var history = await engine.GetHistoryAsync(id);
        Assert.Equal(["START", "SUBMIT", "APPROVE"], history.Select(h => h.CommandCode));
    }

    [Fact]
    public async Task A_concurrency_conflict_on_the_host_entity_reaches_the_host_and_saves_nothing()
    {
        var report = new EfReport { Title = "a", Attachments = 1 };
        Guid id;
        using (var seed = _root.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            id = (await seed.ServiceProvider.GetRequiredService<IWorkflowEngine>().StartAsync<EfReport>("REPORT", report, User)).Value!.Id;
        }

        using var scope = _root.CreateScope();
        var scopeDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await scopeDb.Reports.FindAsync(report.Id); // tracked with Title "a"
        using (var other = _root.CreateScope())
            await other.ServiceProvider.GetRequiredService<AppDbContext>().Reports
                .Where(r => r.Id == report.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Title, "b")); // someone else edits the report

        // The report's own token fails, not the workflow's RowVersion: this must not look like "instance modified by someone else".
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            scope.ServiceProvider.GetRequiredService<IWorkflowEngine>().ExecuteAsync(id, "SUBMIT", User));

        using var check = _root.CreateScope();
        var instance = (await check.ServiceProvider.GetRequiredService<IWorkflowEngine>().GetInstanceAsync(id))!;
        Assert.Equal(("DRAFT", 0), (instance.CurrentStateCode, instance.RowVersion));
        Assert.Equal("DRAFT", (await check.ServiceProvider.GetRequiredService<AppDbContext>().Reports.AsNoTracking().SingleAsync(r => r.Id == report.Id)).WorkflowState);
    }

    private static WorkflowDefinition Race(string name) =>
        WorkflowDefinitionBuilder.Define("RACE", name).State("A", WorkflowStateType.Initial).State("B", WorkflowStateType.Final)
            .On("A").Command("GO").GoTo("B").Build();

    [Fact]
    public async Task Two_publishers_of_the_same_definition_end_up_with_one_version()
    {
        using var scope = _root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        _competitor.CompetingJson = JsonSerializer.Serialize(Race("Race") with { Version = 1 }); // the other instance wins with identical content

        var published = await store.PublishAsync(Race("Race"));

        Assert.Equal(1, published.Version);
        Assert.Single(await store.ListVersionsAsync("RACE"));
    }

    [Fact]
    public async Task Losing_the_race_to_a_different_definition_publishes_the_next_version()
    {
        using var scope = _root.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();
        _competitor.CompetingJson = JsonSerializer.Serialize(Race("Someone else's edit") with { Version = 1 });

        var published = await store.PublishAsync(Race("My edit"));

        Assert.Equal(2, published.Version);
        Assert.Equal([2, 1], (await store.ListVersionsAsync("RACE")).Select(d => d.Version));
    }
}
