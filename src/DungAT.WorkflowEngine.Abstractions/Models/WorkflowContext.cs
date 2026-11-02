namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>
/// Information passed to guards, effects and permission evaluators. <c>Entity</c> is the host entity the instance
/// is linked to (e.g. the Report row), or null when its type is not registered with <c>AddEntity</c>;
/// it is loaded once per engine call.
/// </summary>
public sealed record WorkflowContext(
    WorkflowDefinition Definition,
    WorkflowInstance Instance,
    WorkflowTransition Transition,
    WorkflowUser User,
    string? Comment,
    string? Payload,
    IServiceProvider Services,
    object? Entity = null)
{
    /// <summary>Returns the linked entity as <typeparamref name="T"/> (a class or interface it implements).</summary>
    public T GetEntity<T>() where T : class => EntityAccess.Get<T>(Entity, Instance.EntityType);

    public bool TryGetEntity<T>(out T entity) where T : class
    {
        if (Entity is T typed) { entity = typed; return true; }
        entity = null!;
        return false;
    }
}
