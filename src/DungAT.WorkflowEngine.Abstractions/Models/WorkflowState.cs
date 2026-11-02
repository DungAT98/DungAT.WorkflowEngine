namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>A state an instance can be in (e.g. DRAFT, APPROVED).</summary>
public sealed record WorkflowState(
    string Code,
    string Name,
    WorkflowStateType Type = WorkflowStateType.Intermediate,
    int Order = 0)
{
    /// <summary>Post actions that run whenever an instance enters this state, whichever command got it there.</summary>
    public IReadOnlyList<string> PostActionNames { get; init; } = [];
}
