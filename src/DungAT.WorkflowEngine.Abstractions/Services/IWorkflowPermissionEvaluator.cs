using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Host hook for authorization. The default checks <see cref="WorkflowTransition.AllowedRoles"/>.</summary>
public interface IWorkflowPermissionEvaluator
{
    Task<bool> CanExecuteAsync(WorkflowContext context, CancellationToken ct = default);
}
