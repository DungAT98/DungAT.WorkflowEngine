
namespace DungAT.WorkflowEngine.Building;

public sealed class WorkflowDefinitionException(IReadOnlyList<string> errors)
    : Exception("Invalid workflow definition: " + string.Join("; ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
