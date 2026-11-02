namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>Outcome of an engine operation. Business rejections are returned, not thrown.</summary>
public class WorkflowResult
{
    protected WorkflowResult(WorkflowResultStatus status, string? message, IReadOnlyList<string>? warnings = null)
    {
        Status = status;
        Message = message;
        Warnings = warnings ?? [];
    }

    public WorkflowResultStatus Status { get; }
    public string? Message { get; }
    public bool Succeeded => Status == WorkflowResultStatus.Success;

    /// <summary>
    /// Problems that did not stop the operation, e.g. a post action that failed after the transition was saved
    /// ("SendEmail failed: timeout"). Always empty for failures.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    public static WorkflowResult Success() => new(WorkflowResultStatus.Success, null);
    public static WorkflowResult Failure(WorkflowResultStatus status, string? message = null) => new(status, message);
}
