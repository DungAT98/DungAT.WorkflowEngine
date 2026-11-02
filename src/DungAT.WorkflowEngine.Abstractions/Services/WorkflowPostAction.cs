using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>A post action written against the linked entity.</summary>
public abstract class WorkflowPostAction<TEntity> : IWorkflowPostAction where TEntity : class
{
    public virtual string Name => GetType().Name;

    public Type[]? EntityTypes => [typeof(TEntity)];

    Task IWorkflowPostAction.ExecuteAsync(WorkflowPostContext context, CancellationToken ct) =>
        ExecuteAsync(context.GetEntity<TEntity>(), context, ct);

    protected abstract Task ExecuteAsync(TEntity entity, WorkflowPostContext context, CancellationToken ct);
}
