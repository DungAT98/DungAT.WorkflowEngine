namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Shared by the contexts handed to guards, effects and post actions.</summary>
internal static class EntityAccess
{
    public static T Get<T>(object? entity, string entityTypeName) where T : class =>
        entity is T typed
            ? typed
            : throw new InvalidOperationException(entity is null
                ? $"This instance is not linked to a registered entity. Register '{entityTypeName}' with AddEntity<T>()."
                : $"The linked entity is a {entity.GetType().Name}, not a {typeof(T).Name}.");
}
