namespace DungAT.WorkflowEngine.Abstractions.Models;

/// <summary>A registered guard or effect and the entities it can be used with.</summary>
/// <param name="Name">The name transitions reference.</param>
/// <param name="AppliesToAnyEntity">True for generic components (no <c>[ForEntity]</c>).</param>
/// <param name="EntityNames">Registered entity names the component is restricted to; empty when generic.</param>
public sealed record WorkflowComponentInfo(string Name, bool AppliesToAnyEntity, IReadOnlyList<string> EntityNames);
