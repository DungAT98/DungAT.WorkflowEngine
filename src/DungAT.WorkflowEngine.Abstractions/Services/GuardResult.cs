
namespace DungAT.WorkflowEngine.Abstractions.Services;

public sealed record GuardResult(bool Passed, string? Reason = null)
{
    public static GuardResult Pass { get; } = new(true);
    public static GuardResult Fail(string reason) => new(false, reason);
}
