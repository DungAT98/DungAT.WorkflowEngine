using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Condition that must hold for a transition to run. Referenced from transitions by name.</summary>
public interface IWorkflowGuard : IWorkflowEntityScoped
{
    string Name => GetType().Name;
    Task<GuardResult> EvaluateAsync(WorkflowContext context, CancellationToken ct = default);
}
