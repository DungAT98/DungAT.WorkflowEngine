namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

/// <summary>Result of starting an instance or running a command; warnings are failed post actions.</summary>
public sealed record CommandResultDto(InstanceSummaryDto Instance, IReadOnlyList<string> Warnings);
