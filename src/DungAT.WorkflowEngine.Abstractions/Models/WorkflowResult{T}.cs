namespace DungAT.WorkflowEngine.Abstractions.Models;

public sealed class WorkflowResult<T> : WorkflowResult
{
    private WorkflowResult(WorkflowResultStatus status, string? message, T? value, IReadOnlyList<string>? warnings = null)
        : base(status, message, warnings) => Value = value;

    public T? Value { get; }

    public static WorkflowResult<T> Success(T value, IReadOnlyList<string>? warnings = null) =>
        new(WorkflowResultStatus.Success, null, value, warnings);

    public static new WorkflowResult<T> Failure(WorkflowResultStatus status, string? message = null) =>
        new(status, message, default);
}
