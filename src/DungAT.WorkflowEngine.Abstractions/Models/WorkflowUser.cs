namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>The caller executing a command.</summary>
public sealed record WorkflowUser(string Id, IReadOnlyCollection<string>? Roles = null)
{
    /// <summary>
    /// The engine itself, used for follow-up commands run by post actions. It passes role checks (it is not a person)
    /// but still has to satisfy guards. Only this instance is a system user; a person named "system" is not.
    /// </summary>
    public static WorkflowUser System { get; } = new("system") { IsSystem = true };

    public bool IsSystem { get; private init; }

    public bool IsInAnyRole(IEnumerable<string> roles) =>
        Roles is { Count: > 0 } && roles.Any(r => Roles.Contains(r, StringComparer.OrdinalIgnoreCase));
}
