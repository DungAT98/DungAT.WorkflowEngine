namespace DungAT.WorkflowEngine.Abstractions.Models;

public sealed record WorkflowStats(
    int TotalInstances,
    int CommandsLast24Hours,
    int CommandsLast7Days,
    IReadOnlyList<WorkflowStateCount> StateCounts);
