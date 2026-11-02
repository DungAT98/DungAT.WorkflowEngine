using DungAT.WorkflowEngine.Abstractions.Models;


namespace DungAT.WorkflowEngine.Building;

/// <summary>Structural checks run when a definition is built or published.</summary>
public static class WorkflowDefinitionValidator
{
    public static IReadOnlyList<string> Validate(WorkflowDefinition d)
    {
        var errors = new List<string>();
        var cmp = StringComparer.OrdinalIgnoreCase;

        if (string.IsNullOrWhiteSpace(d.Code)) errors.Add("Definition code is required.");

        foreach (var g in d.States.GroupBy(s => s.Code, cmp).Where(g => g.Count() > 1))
            errors.Add($"Duplicate state '{g.Key}'.");
        foreach (var g in d.Commands.GroupBy(c => c.Code, cmp).Where(g => g.Count() > 1))
            errors.Add($"Duplicate command '{g.Key}'.");
        foreach (var g in d.Transitions.GroupBy(t => (t.FromStateCode.ToUpperInvariant(), t.CommandCode.ToUpperInvariant()))
                     .Where(g => g.Count() > 1))
            errors.Add($"Duplicate transition from '{g.Key.Item1}' on '{g.Key.Item2}'.");

        var initial = d.States.Count(s => s.Type == WorkflowStateType.Initial);
        if (initial != 1) errors.Add($"Exactly one Initial state is required (found {initial}).");

        foreach (var t in d.Transitions)
        {
            if (d.FindState(t.FromStateCode) is null) errors.Add($"Transition references unknown state '{t.FromStateCode}'.");
            if (d.FindState(t.ToStateCode) is null) errors.Add($"Transition references unknown state '{t.ToStateCode}'.");
            if (d.FindCommand(t.CommandCode) is null) errors.Add($"Transition references unknown command '{t.CommandCode}'.");
            if (d.FindState(t.FromStateCode)?.Type == WorkflowStateType.Final)
                errors.Add($"Final state '{t.FromStateCode}' cannot have outgoing transitions.");
        }

        if (errors.Count == 0)
        {
            var reached = new HashSet<string>(cmp) { d.InitialState.Code };
            var queue = new Queue<string>(reached);
            while (queue.Count > 0)
                foreach (var t in d.TransitionsFrom(queue.Dequeue()))
                    if (reached.Add(t.ToStateCode)) queue.Enqueue(t.ToStateCode);
            foreach (var s in d.States.Where(s => !reached.Contains(s.Code)))
                errors.Add($"State '{s.Code}' is unreachable from the initial state.");
        }

        return errors;
    }
}
