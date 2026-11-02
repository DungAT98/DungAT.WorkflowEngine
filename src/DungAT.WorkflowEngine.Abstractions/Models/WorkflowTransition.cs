
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>
/// "In state <see cref="FromStateCode"/>, command <see cref="CommandCode"/> moves the instance to
/// <see cref="ToStateCode"/>", optionally restricted by roles and guards, with effects during it and post actions after it.
/// </summary>
public sealed record WorkflowTransition(
    string FromStateCode,
    string CommandCode,
    string ToStateCode)
{
    public IReadOnlyList<string> AllowedRoles { get; init; } = [];
    public IReadOnlyList<string> GuardNames { get; init; } = [];
    public IReadOnlyList<string> EffectNames { get; init; } = [];

    /// <summary>Run after the transition is saved; they cannot undo it. See <c>IWorkflowPostAction</c>.</summary>
    public IReadOnlyList<string> PostActionNames { get; init; } = [];
    public bool RequireComment { get; init; }
}
