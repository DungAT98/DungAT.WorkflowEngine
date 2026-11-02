using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>An effect written against the linked entity.</summary>
public abstract class WorkflowEffect<TEntity> : IWorkflowEffect where TEntity : class
{
    public virtual string Name => GetType().Name;

    public Type[]? EntityTypes => [typeof(TEntity)];

    Task IWorkflowEffect.ExecuteAsync(WorkflowContext context, CancellationToken ct) =>
        ExecuteAsync(context.GetEntity<TEntity>(), context, ct);

    protected abstract Task ExecuteAsync(TEntity entity, WorkflowContext context, CancellationToken ct);
}
