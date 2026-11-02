using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Engine;

internal sealed class WorkflowComponentRegistry : IWorkflowComponentRegistry
{
    public WorkflowComponentRegistry(
        IEnumerable<IWorkflowGuard> guards,
        IEnumerable<IWorkflowEffect> effects,
        IEnumerable<IWorkflowPostAction> postActions,
        IEnumerable<CodeDefinition> codeDefinitions,
        IWorkflowEntityRegistry entities)
    {
        Guards = guards.Select(g => Describe(g.Name, g.EntityTypes, entities)).OrderBy(c => c.Name).ToList();
        Effects = effects.Select(e => Describe(e.Name, e.EntityTypes, entities)).OrderBy(c => c.Name).ToList();
        PostActions = postActions.Select(a => Describe(a.Name, a.EntityTypes, entities)).OrderBy(c => c.Name).ToList();
        Entities = entities.All.Select(e => e.Name).OrderBy(n => n).ToList();
        CodeDefinitionCodes = new HashSet<string>(codeDefinitions.Select(d => d.Definition.Code), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<WorkflowComponentInfo> Guards { get; }
    public IReadOnlyList<WorkflowComponentInfo> Effects { get; }
    public IReadOnlyList<WorkflowComponentInfo> PostActions { get; }
    public IReadOnlyList<string> Entities { get; }
    public IReadOnlyCollection<string> CodeDefinitionCodes { get; }

    private static WorkflowComponentInfo Describe(string name, Type[]? declared, IWorkflowEntityRegistry entities) =>
        declared is not { Length: > 0 }
            ? new WorkflowComponentInfo(name, true, [])
            : new WorkflowComponentInfo(name, false, entities.All
                .Where(e => declared.Any(t => t.IsAssignableFrom(e.ClrType))).Select(e => e.Name).OrderBy(n => n).ToList());
}
