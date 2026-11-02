using DungAT.WorkflowEngine.Abstractions.Models;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Describes what the host registered, so tools can offer it (e.g. editor dropdowns).</summary>
public interface IWorkflowComponentRegistry
{
    IReadOnlyList<WorkflowComponentInfo> Guards { get; }
    IReadOnlyList<WorkflowComponentInfo> Effects { get; }
    IReadOnlyList<WorkflowComponentInfo> PostActions { get; }

    /// <summary>Names of the registered entity types (see <c>AddEntity</c>).</summary>
    IReadOnlyList<string> Entities { get; }

    /// <summary>Codes of workflows defined in code; these are republished at startup and so are read-only in UIs.</summary>
    IReadOnlyCollection<string> CodeDefinitionCodes { get; }
}
