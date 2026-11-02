using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Receives state-change notifications after they are persisted.</summary>
public interface IWorkflowEventPublisher
{
    Task PublishAsync(WorkflowStateChanged @event, CancellationToken ct = default);
}
