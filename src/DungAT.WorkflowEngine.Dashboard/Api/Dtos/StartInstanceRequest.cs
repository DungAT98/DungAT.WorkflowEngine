namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record StartInstanceRequest(string? DefinitionCode, string? EntityType, string? EntityId, string? Data);
