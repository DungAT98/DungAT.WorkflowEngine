using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record DefinitionDetailDto(
    WorkflowDefinition Definition, bool Editable, IReadOnlyList<DefinitionVersionDto> Versions);
