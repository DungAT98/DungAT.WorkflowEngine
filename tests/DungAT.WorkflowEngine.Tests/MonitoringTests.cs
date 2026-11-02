using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.Tests;

public class MonitoringTests
{
    private static readonly WorkflowUser Manager = new("mgr", ["Manager"]);

    private static async Task<IServiceProvider> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddWorkflowEngine(wf => wf
            .AddGuard<HasAttachmentsGuard>()
            .AddDefinition(WorkflowDefinitionBuilder.Define("DOC", "Document")
                .State("DRAFT", WorkflowStateType.Initial)
                .State("SUBMITTED")
                .State("APPROVED", WorkflowStateType.Final)
                .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
                .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager")));
        var sp = services.BuildServiceProvider().CreateScope().ServiceProvider;
        await sp.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
        return sp;
    }

    [Fact]
    public async Task Stats_query_and_activity_reflect_executed_commands()
    {
        var sp = await CreateAsync();
        var engine = sp.GetRequiredService<IWorkflowEngine>();
        var monitoring = sp.GetRequiredService<IWorkflowMonitoringStore>();

        var a = (await engine.StartAsync("DOC", "Document", "A-1", Manager)).Value!;
        await engine.StartAsync("DOC", "Document", "B-2", Manager);
        await engine.ExecuteAsync(a.Id, "SUBMIT", Manager);
        await engine.ExecuteAsync(a.Id, "APPROVE", Manager);

        var stats = await monitoring.GetStatsAsync();
        Assert.Equal(2, stats.TotalInstances);
        Assert.Equal(2, stats.CommandsLast24Hours);
        Assert.Contains(new WorkflowStateCount("DOC", 1, "APPROVED", 1), stats.StateCounts);
        Assert.Contains(new WorkflowStateCount("DOC", 1, "DRAFT", 1), stats.StateCounts);

        var approved = await monitoring.QueryInstancesAsync(new InstanceQuery { StateCode = "APPROVED" });
        Assert.Equal(["A-1"], approved.Items.Select(i => i.EntityId));
        var search = await monitoring.QueryInstancesAsync(new InstanceQuery { Search = "b-" });
        Assert.Equal(["B-2"], search.Items.Select(i => i.EntityId));
        var paged = await monitoring.QueryInstancesAsync(new InstanceQuery { PageSize = 1, Page = 2 });
        Assert.Equal((2, 1, 2), (paged.Total, paged.Items.Count, paged.Page));

        var activity = await monitoring.GetRecentActivityAsync(10);
        Assert.Equal("APPROVE", activity[0].Entry.CommandCode);
        Assert.Equal("A-1", activity[0].EntityId);
        Assert.Equal(4, activity.Count);
    }

    [Fact]
    public async Task Definition_listing_returns_latest_and_all_versions()
    {
        var sp = await CreateAsync();
        var store = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var v1 = (await store.GetAsync("DOC"))!;
        await store.PublishAsync(v1 with { Description = "changed", Source = WorkflowDefinitionSource.Dashboard });

        Assert.Equal([2], (await store.ListAsync()).Select(d => d.Version));
        Assert.Equal([2, 1], (await store.ListVersionsAsync("DOC")).Select(d => d.Version));
        Assert.Equal(WorkflowDefinitionSource.Dashboard, (await store.GetAsync("DOC"))!.Source);
    }

    [Fact]
    public async Task Component_registry_lists_registered_names()
    {
        var sp = await CreateAsync();
        var registry = sp.GetRequiredService<IWorkflowComponentRegistry>();
        Assert.Equal(["HasAttachmentsGuard"], registry.Guards.Select(g => g.Name));
        Assert.True(registry.Guards[0].AppliesToAnyEntity);
        Assert.Empty(registry.Effects);
        Assert.Contains("DOC", registry.CodeDefinitionCodes);
        Assert.Contains("doc", registry.CodeDefinitionCodes);
    }
}
