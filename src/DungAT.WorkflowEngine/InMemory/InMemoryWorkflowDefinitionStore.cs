using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;


namespace DungAT.WorkflowEngine.InMemory;

public sealed class InMemoryWorkflowDefinitionStore : IWorkflowDefinitionStore
{
    private readonly object _gate = new();
    private readonly List<WorkflowDefinition> _items = [];

    public Task<WorkflowDefinition?> GetAsync(string code, int? version = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var q = _items.Where(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(version is null ? q.MaxBy(d => d.Version) : q.FirstOrDefault(d => d.Version == version));
        }
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<WorkflowDefinition>>(
                _items.GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.MaxBy(d => d.Version)!).OrderBy(d => d.Name).ToList());
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string code, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<WorkflowDefinition>>(
                _items.Where(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.Version).ToList());
    }

    public Task<WorkflowDefinition> PublishAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var next = _items.Where(d => string.Equals(d.Code, definition.Code, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Version).DefaultIfEmpty(0).Max() + 1;
            var stored = definition with { Version = next };
            _items.Add(stored);
            return Task.FromResult(stored);
        }
    }
}
