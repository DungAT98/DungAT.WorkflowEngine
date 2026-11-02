using DungAT.WorkflowEngine.Abstractions.Exceptions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;


namespace DungAT.WorkflowEngine.InMemory;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, WorkflowInstance> _instances = [];
    private readonly List<WorkflowHistoryEntry> _history = [];
    private readonly List<WorkflowPostActionRun> _runs = [];

    internal (IReadOnlyList<WorkflowInstance> Instances, IReadOnlyList<WorkflowHistoryEntry> History) Snapshot()
    {
        lock (_gate) return (_instances.Values.ToList(), _history.ToList());
    }

    public Task<WorkflowInstance?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate) return Task.FromResult(_instances.GetValueOrDefault(id));
    }

    public Task<WorkflowInstance?> GetByEntityAsync(string entityType, string entityId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_instances.Values.FirstOrDefault(i => i.EntityType == entityType && i.EntityId == entityId));
    }

    public Task AddAsync(WorkflowInstance instance, WorkflowHistoryEntry history, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_instances.Values.Any(i => i.EntityType == instance.EntityType && i.EntityId == instance.EntityId))
                throw new WorkflowInstanceAlreadyExistsException(instance.EntityType, instance.EntityId);
            _instances[instance.Id] = instance;
            _history.Add(history);
        }
        return Task.CompletedTask;
    }

    public Task<bool> TryUpdateAsync(
        WorkflowInstance updated, int expectedRowVersion, WorkflowHistoryEntry history, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_instances.TryGetValue(updated.Id, out var current) || current.RowVersion != expectedRowVersion)
                return Task.FromResult(false);
            _instances[updated.Id] = updated with { RowVersion = expectedRowVersion + 1 };
            _history.Add(history);
            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<WorkflowHistoryEntry>>(
                _history.Where(h => h.InstanceId == instanceId).OrderBy(h => h.Sequence).ThenBy(h => h.ExecutedAt).ToList());
    }

    public Task AddPostActionRunsAsync(IReadOnlyList<WorkflowPostActionRun> runs, CancellationToken ct = default)
    {
        lock (_gate) _runs.AddRange(runs);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid instanceId, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<WorkflowPostActionRun>>(
                _runs.Where(r => r.InstanceId == instanceId).OrderBy(r => r.StartedAt).ThenBy(r => r.Sequence).ToList());
    }
}
