namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record InstanceSummaryDto(
    Guid Id,
    string DefinitionCode,
    int DefinitionVersion,
    string EntityType,
    string EntityId,
    string CurrentStateCode,
    string CurrentStateName,
    string StateType,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
