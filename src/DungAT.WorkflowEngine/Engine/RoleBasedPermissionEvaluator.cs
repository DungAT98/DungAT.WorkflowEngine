using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace DungAT.WorkflowEngine.Engine;

internal sealed class RoleBasedPermissionEvaluator : IWorkflowPermissionEvaluator
{
    public Task<bool> CanExecuteAsync(WorkflowContext context, CancellationToken ct = default) =>
        Task.FromResult(context.User.IsSystem || context.Transition.AllowedRoles.Count == 0 || context.User.IsInAnyRole(context.Transition.AllowedRoles));
}
