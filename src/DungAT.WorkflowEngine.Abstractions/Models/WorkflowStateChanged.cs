
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Raised after an instance changed state.</summary>
public sealed record WorkflowStateChanged(
    WorkflowInstance Instance,
    WorkflowHistoryEntry History);
