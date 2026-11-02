using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.InMemory;
using Microsoft.EntityFrameworkCore;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

/// <remarks>
/// Date filters and ordering run in the database. On SQLite, build the model with
/// <c>ApplyWorkflowEngineConfiguration(storeDatesAsBinary: true)</c>.
/// </remarks>
internal sealed class EfWorkflowMonitoringStore<TContext>(TContext db, TimeProvider time) : IWorkflowMonitoringStore
    where TContext : DbContext
{
    public async Task<WorkflowStats> GetStatsAsync(CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var since24h = now.AddHours(-24);
        var since7d = now.AddDays(-7);
        var instances = db.Set<WorkflowInstance>().AsNoTracking();
        var commands = db.Set<WorkflowHistoryEntry>().AsNoTracking().Where(h => h.CommandCode != "START");

        var total = await instances.CountAsync(ct);
        var last24h = await commands.CountAsync(h => h.ExecutedAt >= since24h, ct);
        var last7d = await commands.CountAsync(h => h.ExecutedAt >= since7d, ct);
        var counts = await instances
            .GroupBy(i => new { i.DefinitionCode, i.DefinitionVersion, i.CurrentStateCode })
            .Select(g => new WorkflowStateCount(g.Key.DefinitionCode, g.Key.DefinitionVersion, g.Key.CurrentStateCode, g.Count()))
            .ToListAsync(ct);

        return new WorkflowStats(total, last24h, last7d,
            counts.OrderBy(c => c.DefinitionCode).ThenBy(c => c.DefinitionVersion).ThenBy(c => c.StateCode).ToList());
    }

    public async Task<PagedResult<WorkflowInstance>> QueryInstancesAsync(InstanceQuery query, CancellationToken ct = default)
    {
        var q = db.Set<WorkflowInstance>().AsNoTracking().AsQueryable();
        if (query.DefinitionCode is { Length: > 0 } d) q = q.Where(i => i.DefinitionCode == d);
        if (query.StateCode is { Length: > 0 } s) q = q.Where(i => i.CurrentStateCode == s);
        if (query.EntityType is { Length: > 0 } t) q = q.Where(i => i.EntityType == t);
        if (query.Search is { Length: > 0 } text) q = q.Where(i => i.EntityId.Contains(text));
        if (query.UpdatedFrom is { } from) q = q.Where(i => i.UpdatedAt >= from);
        if (query.UpdatedTo is { } to) q = q.Where(i => i.UpdatedAt <= to);

        var (page, size) = Paging.Normalize(query.Page, query.PageSize);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(i => i.UpdatedAt).ThenBy(i => i.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<WorkflowInstance>(items, total, page, size);
    }

    public async Task<IReadOnlyList<WorkflowActivity>> GetRecentActivityAsync(int take, CancellationToken ct = default)
    {
        var rows = await (
                from h in db.Set<WorkflowHistoryEntry>().AsNoTracking()
                join i in db.Set<WorkflowInstance>().AsNoTracking() on h.InstanceId equals i.Id
                orderby h.ExecutedAt descending
                select new { Entry = h, i.DefinitionCode, i.EntityType, i.EntityId })
            .Take(Math.Clamp(take, 1, Paging.MaxPageSize))
            .ToListAsync(ct);
        return rows.Select(r => new WorkflowActivity(r.Entry, r.DefinitionCode, r.EntityType, r.EntityId)).ToList();
    }
}
