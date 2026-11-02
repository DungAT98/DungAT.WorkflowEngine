
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Audit record of one executed command (or the start of the instance).</summary>
public sealed record WorkflowHistoryEntry
{
    public Guid Id { get; init; }
    public Guid InstanceId { get; init; }
    /// <summary>
    /// Position in the instance's history (0 for START, then the instance's RowVersion after each transition).
    /// Timestamps alone can tie, e.g. for follow-up commands, so this decides the order.
    /// </summary>
    public int Sequence { get; init; }

    public string? FromStateCode { get; init; }
    public string CommandCode { get; init; } = "";
    public string ToStateCode { get; init; } = "";
    public string ExecutedBy { get; init; } = "";
    public DateTimeOffset ExecutedAt { get; init; }
    public string? Comment { get; init; }
    public string? Payload { get; init; }
}
