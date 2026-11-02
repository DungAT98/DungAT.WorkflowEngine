using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

internal sealed class EfWorkflowDefinitionStore<TContext>(TContext db) : IWorkflowDefinitionStore
    where TContext : DbContext
{
    public async Task<WorkflowDefinition?> GetAsync(string code, int? version = null, CancellationToken ct = default)
    {
        var q = db.Set<StoredWorkflowDefinition>().AsNoTracking().Where(x => x.Code == code);
        var row = version is null
            ? await q.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            : await q.FirstOrDefaultAsync(x => x.Version == version, ct);
        return row is null ? null : JsonSerializer.Deserialize<WorkflowDefinition>(row.Json);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default)
    {
        var set = db.Set<StoredWorkflowDefinition>().AsNoTracking();
        var rows = await set
            .Where(d => d.Version == set.Where(x => x.Code == d.Code).Max(x => x.Version))
            .ToListAsync(ct);
        return rows.Select(Deserialize).OrderBy(d => d.Name).ToList();
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string code, CancellationToken ct = default)
    {
        var rows = await db.Set<StoredWorkflowDefinition>().AsNoTracking()
            .Where(x => x.Code == code).OrderByDescending(x => x.Version).ToListAsync(ct);
        return rows.Select(Deserialize).ToList();
    }

    private static WorkflowDefinition Deserialize(StoredWorkflowDefinition row) =>
        JsonSerializer.Deserialize<WorkflowDefinition>(row.Json)!;

    private const int MaxPublishAttempts = 5;

    public async Task<WorkflowDefinition> PublishAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var latest = await db.Set<StoredWorkflowDefinition>()
                .Where(x => x.Code == definition.Code).MaxAsync(x => (int?)x.Version, ct) ?? 0;
            var stored = definition with { Version = latest + 1 };
            var row = new StoredWorkflowDefinition { Code = stored.Code, Version = stored.Version, Json = JsonSerializer.Serialize(stored) };
            db.Set<StoredWorkflowDefinition>().Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                return stored;
            }
            catch (DbUpdateException)
            {
                db.Entry(row).State = EntityState.Detached;
                // Retry only when another publisher took this version number (e.g. two app instances starting together).
                var taken = await db.Set<StoredWorkflowDefinition>().AsNoTracking()
                    .AnyAsync(x => x.Code == stored.Code && x.Version == stored.Version, ct);
                if (!taken || attempt >= MaxPublishAttempts) throw;

                // If that publisher stored the very same definition, use it instead of creating a duplicate version.
                var winner = await GetAsync(definition.Code, null, ct);
                if (winner is not null && Fingerprint(winner) == Fingerprint(definition)) return winner;
            }
        }
    }

    private static string Fingerprint(WorkflowDefinition d) => JsonSerializer.Serialize(d with { Version = 0 });
}
