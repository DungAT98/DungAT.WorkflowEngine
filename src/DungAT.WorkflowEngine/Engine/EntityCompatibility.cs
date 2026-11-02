namespace DungAT.WorkflowEngine.Engine;

internal static class EntityCompatibility
{
    /// <summary>A guard, effect or post action for a different entity is a configuration error, not something to skip silently.</summary>
    public static void Ensure(string kind, string name, Type[]? declared, object? entity, string entityTypeName)
    {
        if (declared is not { Length: > 0 }) return;
        var actual = entity?.GetType();
        if (actual is not null && declared.Any(t => t.IsAssignableFrom(actual))) return;
        throw new InvalidOperationException(
            $"{kind} '{name}' is for {string.Join(", ", declared.Select(t => t.Name))}, but this instance's entity is " +
            $"{entityTypeName}{(actual is null ? " (not registered with AddEntity)" : "")}.");
    }
}
