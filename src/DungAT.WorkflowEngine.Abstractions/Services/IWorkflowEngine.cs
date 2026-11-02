using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Entry point used by host applications.</summary>
public interface IWorkflowEngine
{
    Task<WorkflowResult<WorkflowInstance>> StartAsync(
        string definitionCode, string entityType, string entityId, WorkflowUser user,
        string? data = null, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowCommand>> GetAvailableCommandsAsync(
        Guid instanceId, WorkflowUser user, CancellationToken ct = default);

    Task<WorkflowResult<WorkflowInstance>> ExecuteAsync(
        Guid instanceId, string commandCode, WorkflowUser user,
        string? comment = null, string? payload = null, CancellationToken ct = default);

    /// <summary>Starts a workflow for an entity the host already has; its id comes from the entity's registration.</summary>
    Task<WorkflowResult<WorkflowInstance>> StartAsync<TEntity>(
        string definitionCode, TEntity entity, WorkflowUser user,
        string? data = null, CancellationToken ct = default) where TEntity : class;

    Task<WorkflowInstance?> GetInstanceForAsync<TEntity>(TEntity entity, CancellationToken ct = default) where TEntity : class;

    Task<WorkflowInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default);

    Task<WorkflowInstance?> GetInstanceByEntityAsync(
        string entityType, string entityId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>How each post action went for an instance, oldest first.</summary>
    Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid instanceId, CancellationToken ct = default);
}
