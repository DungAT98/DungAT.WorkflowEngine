using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>
/// Side effect run during a transition, before state is persisted. Throwing aborts the transition.
/// Because it runs before the save, it has already happened if the save then loses a concurrency race or fails,
/// and a retry runs it again: effects must be idempotent. Work that must happen exactly once, after the change is final
/// (emails, calls to other systems), belongs in an <see cref="IWorkflowPostAction"/>.
/// </summary>
public interface IWorkflowEffect : IWorkflowEntityScoped
{
    string Name => GetType().Name;
    Task ExecuteAsync(WorkflowContext context, CancellationToken ct = default);
}
