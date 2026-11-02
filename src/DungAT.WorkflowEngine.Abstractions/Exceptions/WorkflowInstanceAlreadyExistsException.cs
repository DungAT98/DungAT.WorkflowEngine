namespace DungAT.WorkflowEngine.Abstractions.Exceptions;

/// <summary>
/// Thrown by an instance store when an entity already has a workflow instance (two starts raced past the engine's check).
/// The engine turns it into <c>WorkflowResultStatus.AlreadyExists</c>.
/// </summary>
public sealed class WorkflowInstanceAlreadyExistsException(string entityType, string entityId)
    : Exception($"{entityType} {entityId} already has a workflow instance.")
{
    public string EntityType { get; } = entityType;
    public string EntityId { get; } = entityId;
}
