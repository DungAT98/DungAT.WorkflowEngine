
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Kind of a workflow state. Exactly one state must be <see cref="Initial"/>.</summary>
public enum WorkflowStateType
{
    Intermediate = 0,
    Initial = 1,
    Final = 2,
}
