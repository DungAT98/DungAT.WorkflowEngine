
namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>An immutable, versioned workflow definition. Version is assigned when published to a store.</summary>
public sealed record WorkflowDefinition(string Code, string Name)
{
    public string? Description { get; init; }
    public int Version { get; init; }
    public WorkflowDefinitionSource Source { get; init; } = WorkflowDefinitionSource.Code;

    /// <summary>Optional host entity type this workflow applies to (e.g. "Document").</summary>
    public string? EntityType { get; init; }

    public IReadOnlyList<WorkflowState> States { get; init; } = [];
    public IReadOnlyList<WorkflowCommand> Commands { get; init; } = [];
    public IReadOnlyList<WorkflowTransition> Transitions { get; init; } = [];

    public WorkflowState InitialState => States.Single(s => s.Type == WorkflowStateType.Initial);

    public WorkflowState? FindState(string code) =>
        States.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

    public WorkflowCommand? FindCommand(string code) =>
        Commands.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public WorkflowTransition? FindTransition(string fromStateCode, string commandCode) =>
        Transitions.FirstOrDefault(t =>
            string.Equals(t.FromStateCode, fromStateCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.CommandCode, commandCode, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<WorkflowTransition> TransitionsFrom(string stateCode) =>
        Transitions.Where(t => string.Equals(t.FromStateCode, stateCode, StringComparison.OrdinalIgnoreCase));
}
