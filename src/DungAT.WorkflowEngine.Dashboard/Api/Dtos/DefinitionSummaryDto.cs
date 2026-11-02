namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record DefinitionSummaryDto(
    string Code,
    string Name,
    string? Description,
    int Version,
    string Source,
    string? EntityType,
    int StateCount,
    int CommandCount,
    int TransitionCount,
    bool Editable);
