using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Dashboard.Api.Dtos;

public sealed record DashboardMeta(
    string Title,
    bool ReadOnly,
    string UserId,
    IReadOnlyCollection<string> UserRoles,
    IReadOnlyList<WorkflowComponentInfo> Guards,
    IReadOnlyList<WorkflowComponentInfo> Effects,
    IReadOnlyList<WorkflowComponentInfo> PostActions,
    IReadOnlyList<string> Entities,
    IReadOnlyList<string> CodeDefinitions);
