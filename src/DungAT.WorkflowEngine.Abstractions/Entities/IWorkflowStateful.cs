namespace DungAT.WorkflowEngine.Abstractions.Entities;

/// <summary>
/// Implement on a business entity to have the engine write the current state code into
/// <see cref="WorkflowState"/> on every transition (in the same transaction when using EF Core).
/// </summary>
public interface IWorkflowStateful
{
    string? WorkflowState { get; set; }
}
