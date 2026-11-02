
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>A running workflow bound to one host business entity.</summary>
public sealed record WorkflowInstance
{
    public Guid Id { get; init; }
    public string DefinitionCode { get; init; } = "";
    public int DefinitionVersion { get; init; }
    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string CurrentStateCode { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Optimistic concurrency token, incremented on every successful update.</summary>
    public int RowVersion { get; init; }

    /// <summary>Optional host data (JSON) available to guards and effects.</summary>
    public string? Data { get; init; }
}
