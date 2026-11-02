using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Read-only queries used by monitoring UIs such as the dashboard.</summary>
public interface IWorkflowMonitoringStore
{
    Task<WorkflowStats> GetStatsAsync(CancellationToken ct = default);

    Task<PagedResult<WorkflowInstance>> QueryInstancesAsync(InstanceQuery query, CancellationToken ct = default);

    /// <summary>The most recent history entries across all instances, newest first.</summary>
    Task<IReadOnlyList<WorkflowActivity>> GetRecentActivityAsync(int take, CancellationToken ct = default);
}
