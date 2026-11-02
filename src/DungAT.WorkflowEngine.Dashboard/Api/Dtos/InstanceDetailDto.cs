using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record InstanceDetailDto(
    InstanceSummaryDto Instance,
    string? Data,
    WorkflowDefinition Definition,
    IReadOnlyList<HistoryEntryDto> History,
    IReadOnlyList<WorkflowCommand> AvailableCommands,
    bool CanExecute,
    string? EntityUrl);
