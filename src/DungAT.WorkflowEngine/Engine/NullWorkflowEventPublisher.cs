using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace DungAT.WorkflowEngine.Engine;

internal sealed class NullWorkflowEventPublisher : IWorkflowEventPublisher
{
    public Task PublishAsync(WorkflowStateChanged @event, CancellationToken ct = default) => Task.CompletedTask;
}
