namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>The outcome of one post action for one history entry.</summary>
public sealed record WorkflowPostActionRun
{
    public Guid Id { get; init; }
    public Guid InstanceId { get; init; }

    /// <summary>The history entry (transition or start) that triggered the action.</summary>
    public Guid HistoryId { get; init; }

    /// <summary>Position among the actions of the same history entry (0-based); timestamps alone can tie.</summary>
    public int Sequence { get; init; }

    public string ActionName { get; init; } = "";
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public int DurationMs { get; init; }
}
