using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.InMemory;

/// <summary>Monitoring queries over <see cref="InMemoryWorkflowInstanceStore"/>.</summary>
public sealed class InMemoryWorkflowMonitoringStore(InMemoryWorkflowInstanceStore instances, TimeProvider time)
    : IWorkflowMonitoringStore
{
    public Task<WorkflowStats> GetStatsAsync(CancellationToken ct = default)
    {
        var (all, history) = instances.Snapshot();
        var now = time.GetUtcNow();
        var commands = history.Where(h => h.CommandCode != "START").ToList();
        var counts = all
            .GroupBy(i => (i.DefinitionCode, i.DefinitionVersion, i.CurrentStateCode))
            .Select(g => new WorkflowStateCount(g.Key.DefinitionCode, g.Key.DefinitionVersion, g.Key.CurrentStateCode, g.Count()))
            .OrderBy(c => c.DefinitionCode).ThenBy(c => c.DefinitionVersion).ThenBy(c => c.StateCode)
            .ToList();
        return Task.FromResult(new WorkflowStats(
            all.Count,
            commands.Count(h => h.ExecutedAt >= now.AddHours(-24)),
            commands.Count(h => h.ExecutedAt >= now.AddDays(-7)),
            counts));
    }

    public Task<PagedResult<WorkflowInstance>> QueryInstancesAsync(InstanceQuery query, CancellationToken ct = default)
    {
        var (all, _) = instances.Snapshot();
        var cmp = StringComparison.OrdinalIgnoreCase;
        var q = all.AsEnumerable();
        if (query.DefinitionCode is { Length: > 0 } d) q = q.Where(i => string.Equals(i.DefinitionCode, d, cmp));
        if (query.StateCode is { Length: > 0 } s) q = q.Where(i => string.Equals(i.CurrentStateCode, s, cmp));
        if (query.EntityType is { Length: > 0 } t) q = q.Where(i => string.Equals(i.EntityType, t, cmp));
        if (query.Search is { Length: > 0 } text) q = q.Where(i => i.EntityId.Contains(text, cmp));
        if (query.UpdatedFrom is { } from) q = q.Where(i => i.UpdatedAt >= from);
        if (query.UpdatedTo is { } to) q = q.Where(i => i.UpdatedAt <= to);

        var matched = q.OrderByDescending(i => i.UpdatedAt).ToList();
        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        return Task.FromResult(new PagedResult<WorkflowInstance>(
            matched.Skip((page - 1) * size).Take(size).ToList(), matched.Count, page, size));
    }

    public Task<IReadOnlyList<WorkflowActivity>> GetRecentActivityAsync(int take, CancellationToken ct = default)
    {
        var (all, history) = instances.Snapshot();
        var byId = all.ToDictionary(i => i.Id);
        var result = history
            .OrderByDescending(h => h.ExecutedAt)
            .Where(h => byId.ContainsKey(h.InstanceId))
            .Take(Math.Clamp(take, 1, 200))
            .Select(h => new WorkflowActivity(h, byId[h.InstanceId].DefinitionCode, byId[h.InstanceId].EntityType, byId[h.InstanceId].EntityId))
            .ToList();
        return Task.FromResult<IReadOnlyList<WorkflowActivity>>(result);
    }
}
