namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record PostActionRunDto(string Name, bool Succeeded, string? Error, int DurationMs);
