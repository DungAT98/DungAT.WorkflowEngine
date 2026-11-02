using DungAT.WorkflowEngine.Abstractions.Entities;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Entities;

internal sealed class WorkflowEntityRegistry : IWorkflowEntityRegistry
{
    private readonly Dictionary<string, WorkflowEntityDescriptor> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, WorkflowEntityDescriptor> _byType = [];

    public WorkflowEntityRegistry(IEnumerable<WorkflowEntityDescriptor> descriptors)
    {
        foreach (var d in descriptors)
        {
            if (!_byName.TryAdd(d.Name, d))
                throw new InvalidOperationException($"Two workflow entities are registered with the name '{d.Name}'. Give one a different .Name(...).");
            if (!_byType.TryAdd(d.ClrType, d))
                throw new InvalidOperationException($"{d.ClrType.Name} is registered as a workflow entity more than once.");
        }
        All = _byName.Values.OrderBy(d => d.Name).ToList();
    }

    public IReadOnlyCollection<WorkflowEntityDescriptor> All { get; }

    public bool TryGetByName(string name, out WorkflowEntityDescriptor descriptor) =>
        _byName.TryGetValue(name, out descriptor!);

    public bool TryGetByType(Type type, out WorkflowEntityDescriptor descriptor)
    {
        if (_byType.TryGetValue(type, out descriptor!)) return true;
        foreach (var candidate in _byType.Values)
            if (candidate.ClrType.IsAssignableFrom(type)) { descriptor = candidate; return true; }
        return false;
    }
}
