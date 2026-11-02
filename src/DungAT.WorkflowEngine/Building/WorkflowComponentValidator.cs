using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Building;

/// <summary>
/// Checks that every guard and effect a definition references is registered and compatible with the entity
/// the workflow is bound to (see <c>ForEntityAttribute</c>).
/// </summary>
public static class WorkflowComponentValidator
{
    public static IReadOnlyList<string> Validate(WorkflowDefinition definition, IWorkflowComponentRegistry registry)
    {
        var errors = new List<string>();
        foreach (var t in definition.Transitions)
        {
            foreach (var name in t.GuardNames) Check("Guard", name, registry.Guards, definition, errors);
            foreach (var name in t.EffectNames) Check("Effect", name, registry.Effects, definition, errors);
            foreach (var name in t.PostActionNames) Check("Post action", name, registry.PostActions, definition, errors);
        }
        foreach (var state in definition.States)
            foreach (var name in state.PostActionNames) Check("Post action", name, registry.PostActions, definition, errors);
        return errors.Distinct().ToList();
    }

    private static void Check(
        string kind, string name, IReadOnlyList<WorkflowComponentInfo> registered, WorkflowDefinition definition, List<string> errors)
    {
        var info = registered.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            errors.Add($"{kind} '{name}' is not registered in the host application.");
            return;
        }
        if (info.AppliesToAnyEntity) return;

        var allowed = info.EntityNames.Count == 0 ? "entity types that are not registered" : string.Join(", ", info.EntityNames);
        if (string.IsNullOrEmpty(definition.EntityType))
            errors.Add($"{kind} '{name}' needs a workflow bound to an entity ({allowed}), but this workflow has no entity type.");
        else if (!info.EntityNames.Contains(definition.EntityType, StringComparer.OrdinalIgnoreCase))
            errors.Add($"{kind} '{name}' cannot be used with {definition.EntityType}; it is for {allowed}.");
    }
}
