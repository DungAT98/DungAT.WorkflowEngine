using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class InstanceMapper
{
    public static IReadOnlyList<HistoryEntryDto> ToHistory(
        IReadOnlyList<WorkflowHistoryEntry> history, IReadOnlyList<WorkflowPostActionRun> runs)
    {
        var byHistory = runs.ToLookup(r => r.HistoryId);
        return history.Select(h => new HistoryEntryDto(
            h.Id, h.InstanceId, h.FromStateCode, h.CommandCode, h.ToStateCode, h.ExecutedBy, h.ExecutedAt, h.Comment, h.Payload,
            byHistory[h.Id].OrderBy(r => r.Sequence).Select(r => new PostActionRunDto(r.ActionName, r.Succeeded, r.Error, r.DurationMs)).ToList())).ToList();
    }

    public static InstanceSummaryDto ToSummary(WorkflowInstance i, WorkflowDefinition? definition)
    {
        var state = definition?.FindState(i.CurrentStateCode);
        return new InstanceSummaryDto(
            i.Id, i.DefinitionCode, i.DefinitionVersion, i.EntityType, i.EntityId,
            i.CurrentStateCode, state?.Name ?? i.CurrentStateCode,
            (state?.Type ?? WorkflowStateType.Intermediate).ToString(), i.CreatedBy, i.CreatedAt, i.UpdatedAt);
    }
}
