namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

/// <summary>A history entry together with how its post actions went.</summary>
public sealed record HistoryEntryDto(
    Guid Id,
    Guid InstanceId,
    string? FromStateCode,
    string CommandCode,
    string ToStateCode,
    string ExecutedBy,
    DateTimeOffset ExecutedAt,
    string? Comment,
    string? Payload,
    IReadOnlyList<PostActionRunDto> PostActions);
