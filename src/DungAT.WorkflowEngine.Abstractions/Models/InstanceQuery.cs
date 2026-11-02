namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Filter for <c>IWorkflowMonitoringStore.QueryInstancesAsync</c>. Results are newest-updated first.</summary>
public sealed record InstanceQuery
{
    public string? DefinitionCode { get; init; }
    public string? StateCode { get; init; }
    public string? EntityType { get; init; }

    /// <summary>Case-insensitive match on the entity id.</summary>
    public string? Search { get; init; }

    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedTo { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
