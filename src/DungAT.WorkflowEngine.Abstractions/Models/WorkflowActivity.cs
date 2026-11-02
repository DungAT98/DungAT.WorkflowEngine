namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>A history entry together with the instance it belongs to, for activity feeds.</summary>
public sealed record WorkflowActivity(
    WorkflowHistoryEntry Entry,
    string DefinitionCode,
    string EntityType,
    string EntityId);
