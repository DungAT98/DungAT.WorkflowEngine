using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>A guard written against the linked entity, e.g. <c>class BudgetGuard : WorkflowGuard&lt;Report&gt;</c>.</summary>
public abstract class WorkflowGuard<TEntity> : IWorkflowGuard where TEntity : class
{
    public virtual string Name => GetType().Name;

    public Type[]? EntityTypes => [typeof(TEntity)];

    Task<GuardResult> IWorkflowGuard.EvaluateAsync(WorkflowContext context, CancellationToken ct) =>
        EvaluateAsync(context.GetEntity<TEntity>(), context, ct);

    protected abstract Task<GuardResult> EvaluateAsync(TEntity entity, WorkflowContext context, CancellationToken ct);
}
