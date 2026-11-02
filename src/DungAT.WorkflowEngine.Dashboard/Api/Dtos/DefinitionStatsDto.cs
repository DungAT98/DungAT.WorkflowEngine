namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record DefinitionStatsDto(
    string Code, string Name, int Version, int Total, IReadOnlyList<StateStatsDto> States);
