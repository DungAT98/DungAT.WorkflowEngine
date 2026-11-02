using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore.Tests;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<EfReport> Reports => Set<EfReport>();
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyWorkflowEngineConfiguration(storeDatesAsBinary: true);
        modelBuilder.Entity<EfReport>().Property(r => r.Title).IsConcurrencyToken(); // the host's own optimistic concurrency
    }
}

public sealed class EfEngineTests : IAsyncLifetime
{
    private static readonly WorkflowUser Employee = new("emp", ["Employee"]);
    private static readonly WorkflowUser Manager = new("mgr", ["Manager"]);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _root = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddWorkflowEngine(wf => wf
            .AddDefinition(WorkflowDefinitionBuilder.Define("DOC", "Document")
                .State("DRAFT", WorkflowStateType.Initial)
                .State("SUBMITTED")
                .State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager"))
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

    private IWorkflowEngine NewEngine(out IServiceScope scope)
    {
        scope = _root.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
    }

    [Fact]
    public async Task Full_flow_persists_state_and_history()
    {
        var engine = NewEngine(out var scope);
        using var _ = scope;

        var id = (await engine.StartAsync("DOC", "Document", "42", Employee)).Value!.Id;
        Assert.True((await engine.ExecuteAsync(id, "SUBMIT", Employee)).Succeeded);
        Assert.Equal(WorkflowResultStatus.NotAllowed, (await engine.ExecuteAsync(id, "APPROVE", Employee)).Status);
        var approved = await engine.ExecuteAsync(id, "APPROVE", Manager);
        Assert.Equal("APPROVED", approved.Value!.CurrentStateCode);

        // A fresh scope (new DbContext) sees the persisted data.
        var reader = NewEngine(out var readerScope);
        using var __ = readerScope;
        var loaded = await reader.GetInstanceAsync(id);
        Assert.Equal("APPROVED", loaded!.CurrentStateCode);
        Assert.Equal(2, loaded.RowVersion);
        Assert.Equal(["START", "SUBMIT", "APPROVE"], (await reader.GetHistoryAsync(id)).Select(h => h.CommandCode));
        Assert.Equal(WorkflowResultStatus.AlreadyExists, (await reader.StartAsync("DOC", "Document", "42", Employee)).Status);
    }

    [Fact]
    public async Task Concurrent_updates_yield_one_conflict()
    {
        var first = NewEngine(out var s1);
        using var _1 = s1;
        var id = (await first.StartAsync("DOC", "Document", "7", Employee)).Value!.Id;

        // Both engines read the same RowVersion before either writes.
        var second = NewEngine(out var s2);
        using var _2 = s2;
        var store1 = s1.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var store2 = s2.ServiceProvider.GetRequiredService<IWorkflowInstanceStore>();
        var a = (await store1.GetAsync(id))!;
        var b = (await store2.GetAsync(id))!;
        var h = () => new WorkflowHistoryEntry { Id = Guid.NewGuid(), InstanceId = id, CommandCode = "X", ToStateCode = "SUBMITTED" };

        Assert.True(await store1.TryUpdateAsync(a with { CurrentStateCode = "SUBMITTED" }, a.RowVersion, h()));
        Assert.False(await store2.TryUpdateAsync(b with { CurrentStateCode = "SUBMITTED" }, b.RowVersion, h()));
        Assert.Equal(1, (await second.GetInstanceAsync(id))!.RowVersion);
    }

    [Fact]
    public async Task Monitoring_and_definition_listing_work_against_the_database()
    {
        var engine = NewEngine(out var scope);
        using var _ = scope;
        var sp = scope.ServiceProvider;
        var a = (await engine.StartAsync("DOC", "Document", "A-1", Manager)).Value!;
        await engine.StartAsync("DOC", "Document", "B-2", Manager);
        await engine.ExecuteAsync(a.Id, "SUBMIT", Manager);
        await engine.ExecuteAsync(a.Id, "APPROVE", Manager);

        var monitoring = sp.GetRequiredService<IWorkflowMonitoringStore>();
        var stats = await monitoring.GetStatsAsync();
        Assert.Equal(2, stats.TotalInstances);
        Assert.Equal(2, stats.CommandsLast24Hours);
        Assert.Contains(new WorkflowStateCount("DOC", 1, "APPROVED", 1), stats.StateCounts);

        var page = await monitoring.QueryInstancesAsync(new InstanceQuery { Search = "A-", PageSize = 5 });
        Assert.Equal(["A-1"], page.Items.Select(i => i.EntityId));
        var all = await monitoring.QueryInstancesAsync(new InstanceQuery { UpdatedFrom = DateTimeOffset.UtcNow.AddHours(-1) });
        Assert.Equal("A-1", all.Items[0].EntityId); // most recently updated first

        var activity = await monitoring.GetRecentActivityAsync(10);
        Assert.Equal("APPROVE", activity[0].Entry.CommandCode);
        Assert.Equal(4, activity.Count);

        var store = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var v1 = (await store.GetAsync("DOC"))!;
        await store.PublishAsync(v1 with { Source = WorkflowDefinitionSource.Dashboard });
        Assert.Equal([2], (await store.ListAsync()).Select(d => d.Version));
        Assert.Equal([2, 1], (await store.ListVersionsAsync("DOC")).Select(d => d.Version));
    }
}
