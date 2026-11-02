using System.Collections.Concurrent;
using System.Reflection;

namespace DungAT.WorkflowEngine.Abstractions.Attributes;

/// <summary>
/// Restricts a guard or effect to workflows bound to the given entity types, e.g.
/// <c>[ForEntity(typeof(Report))]</c>. A base class or interface also matches every registered entity that derives from it.
/// Components without the attribute are generic and work with any workflow.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ForEntityAttribute(params Type[] entityTypes) : Attribute
{
    private static readonly ConcurrentDictionary<Type, Type[]?> Cache = new();

    public Type[] EntityTypes { get; } = entityTypes;

    /// <summary>The entity types declared on <paramref name="componentType"/>, or null when it is generic.</summary>
    public static Type[]? For(Type componentType) =>
        Cache.GetOrAdd(componentType, t => t.GetCustomAttribute<ForEntityAttribute>()?.EntityTypes);
}
