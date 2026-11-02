using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

public interface IWorkflowDefinitionStore
{
    /// <summary>Gets a specific version, or the latest when <paramref name="version"/> is null.</summary>
    Task<WorkflowDefinition?> GetAsync(string code, int? version = null, CancellationToken ct = default);

    /// <summary>The latest version of every published definition.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default);

    /// <summary>All published versions of one definition, newest first.</summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListVersionsAsync(string code, CancellationToken ct = default);

    /// <summary>Stores a new version of the definition and returns it with the assigned version.</summary>
    Task<WorkflowDefinition> PublishAsync(WorkflowDefinition definition, CancellationToken ct = default);
}
