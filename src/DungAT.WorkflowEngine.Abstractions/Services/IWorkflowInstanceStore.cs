using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

public interface IWorkflowInstanceStore
{
    Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkflowInstance?> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default);
    /// <exception cref="Exceptions.WorkflowInstanceAlreadyExistsException">The entity already has an instance.</exception>
    Task AddAsync(WorkflowInstance instance, WorkflowHistoryEntry history, CancellationToken ct = default);

    /// <summary>
    /// Persists <paramref name="updated"/> and <paramref name="history"/> atomically if the stored
    /// RowVersion equals <paramref name="expectedRowVersion"/>; returns false on conflict.
    /// The store increments RowVersion.
    /// </summary>
    Task<bool> TryUpdateAsync(
        WorkflowInstance updated, int expectedRowVersion, WorkflowHistoryEntry history, CancellationToken ct = default);

    /// <summary>The instance's history in order (by <see cref="WorkflowHistoryEntry.Sequence"/>, then time).</summary>
    Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>Records how each post action went (after the transition was saved).</summary>
    Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid instanceId, CancellationToken ct = default);
}
