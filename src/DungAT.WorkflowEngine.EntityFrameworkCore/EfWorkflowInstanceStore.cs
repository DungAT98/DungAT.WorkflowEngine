using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Exceptions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

internal sealed class EfWorkflowInstanceStore<TContext>(TContext db) : IWorkflowInstanceStore
    where TContext : DbContext
{
    public Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Set<WorkflowInstance>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<WorkflowInstance?> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default) =>
        db.Set<WorkflowInstance>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntityType == entityType && x.EntityId == entityId, ct);

    public async Task AddAsync(WorkflowInstance instance, WorkflowHistoryEntry history, CancellationToken ct = default)
    {
        db.Set<WorkflowInstance>().Add(instance);
        db.Set<WorkflowHistoryEntry>().Add(history);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            Detach(instance, history);
            // The unique (EntityType, EntityId) index fired: another start got there first. Anything else is a real error.
            if (await db.Set<WorkflowInstance>().AsNoTracking()
                    .AnyAsync(x => x.EntityType == instance.EntityType && x.EntityId == instance.EntityId && x.Id != instance.Id, ct))
                throw new WorkflowInstanceAlreadyExistsException(instance.EntityType, instance.EntityId);
            throw;
        }
        finally { Detach(instance, history); }
    }

    // Stores hand out untracked snapshots, so never leave workflow rows in the host's change tracker.
    private void Detach(WorkflowInstance instance, WorkflowHistoryEntry history)
    {
        db.Entry(instance).State = EntityState.Detached;
        db.Entry(history).State = EntityState.Detached;
    }

    public async Task<bool> TryUpdateAsync(
        WorkflowInstance updated, int expectedRowVersion, WorkflowHistoryEntry history, CancellationToken ct = default)
    {
        var entry = db.Entry(updated);
        if (entry.State == EntityState.Detached) entry.State = EntityState.Unchanged;
        entry.State = EntityState.Modified;
        entry.Property(x => x.RowVersion).OriginalValue = expectedRowVersion;
        entry.Property(x => x.RowVersion).CurrentValue = expectedRowVersion + 1;
        db.Set<WorkflowHistoryEntry>().Add(history);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        // Only a conflict on the workflow row is a lost race. The same SaveChanges also writes the host entity, so a
        // concurrency failure on *its* token must reach the host unchanged instead of being reported as a workflow conflict.
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is WorkflowInstance))
        {
            return false;
        }
        finally
        {
            Detach(updated, history);
        }
    }

    public async Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default)
    {
        if (runs.Count == 0) return;
        db.Set<WorkflowPostActionRun>().AddRange(runs);
        try { await db.SaveChangesAsync(ct); }
        finally { foreach (var run in runs) db.Entry(run).State = EntityState.Detached; }
    }

    public async Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid instanceId, CancellationToken ct = default)
    {
        var rows = await db.Set<WorkflowPostActionRun>().AsNoTracking().Where(x => x.InstanceId == instanceId).ToListAsync(ct);
        return rows.OrderBy(x => x.StartedAt).ThenBy(x => x.Sequence).ToList(); // client-side: SQLite cannot ORDER BY DateTimeOffset
    }

    public async Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default)
    {
        // Ordered client-side: SQLite cannot ORDER BY DateTimeOffset, and histories are small.
        var rows = await db.Set<WorkflowHistoryEntry>().AsNoTracking()
            .Where(x => x.InstanceId == instanceId).ToListAsync(ct);
        return rows.OrderBy(x => x.Sequence).ThenBy(x => x.ExecutedAt).ToList();
    }
}
