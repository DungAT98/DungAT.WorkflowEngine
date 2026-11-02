namespace DungAT.WorkflowEngine.Abstractions.Entities;

/// <summary>Describes one host entity type that workflow instances can be linked to.</summary>
public sealed class WorkflowEntityDescriptor
{
    /// <summary>Stored in <c>WorkflowInstance.EntityType</c> and referenced by <c>WorkflowDefinition.EntityType</c>.</summary>
    public required string Name { get; init; }

    public required Type ClrType { get; init; }

    /// <summary>Loads the entity by its id; null when it does not exist. Receives the current scope's services.</summary>
    public required Func<IServiceProvider, string, CancellationToken, Task<object?>> Load { get; init; }

    /// <summary>Gets the id stored as <c>WorkflowInstance.EntityId</c>.</summary>
    public required Func<IServiceProvider, object, string> GetId { get; init; }

    /// <summary>Writes a state code onto the entity; null when the entity does not mirror the workflow state.</summary>
    public Action<object, string>? SetState { get; init; }

    /// <summary>
    /// Captures the entity's current state and returns an action that puts it back; null when the previous state cannot be read.
    /// The engine uses it to undo <see cref="SetState"/> when saving a new instance fails.
    /// </summary>
    public Func<object, Action>? CaptureState { get; init; }
}
