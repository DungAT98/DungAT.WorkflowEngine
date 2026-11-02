using DungAT.WorkflowEngine.Abstractions.Attributes;

namespace DungAT.WorkflowEngine.Abstractions.Services;

/// <summary>Implemented by guards and effects that only make sense for certain entity types.</summary>
public interface IWorkflowEntityScoped
{
    /// <summary>The supported entity types, or null for a generic component. Defaults to <see cref="ForEntityAttribute"/>.</summary>
    Type[]? EntityTypes => ForEntityAttribute.For(GetType());
}
