using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>
/// Logic that runs after a transition has been saved: send the email, call another system, auto-approve.
/// Unlike an effect it cannot stop or undo the transition. If it throws, the failure is logged, stored and
/// returned as a warning, and the remaining actions still run. Referenced from transitions (or state entry) by name.
/// </summary>
public interface IWorkflowPostAction : IWorkflowEntityScoped
{
    string Name => GetType().Name;
    Task ExecuteAsync(WorkflowPostContext context, CancellationToken ct = default);
}
