using DungAT.WorkflowEngine.Abstractions.Entities;

namespace DungAT.WorkflowEngine.Abstractions.Services;

public interface IWorkflowEntityRegistry
{
    IReadOnlyCollection<WorkflowEntityDescriptor> All { get; }

    bool TryGetByName(string name, out WorkflowEntityDescriptor descriptor);

    /// <summary>Matches the exact type first, then any registered base type (e.g. for proxies of an entity).</summary>
    bool TryGetByType(Type type, out WorkflowEntityDescriptor descriptor);
}
