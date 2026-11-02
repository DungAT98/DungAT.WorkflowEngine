namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record DashboardStats(
    int TotalInstances,
    int ActiveInstances,
    int FinishedInstances,
    int CommandsLast24Hours,
    int CommandsLast7Days,
    IReadOnlyList<DefinitionStatsDto> Definitions);
