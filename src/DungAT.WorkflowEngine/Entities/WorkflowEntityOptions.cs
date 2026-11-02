using System.Globalization;
using System.Reflection;
using DungAT.WorkflowEngine.Abstractions.Entities;

namespace DungAT.WorkflowEngine.Entities;

/// <summary>
/// Describes how to find, identify and (optionally) mirror workflow state onto a host entity.
/// Used with <c>AddEntity&lt;T&gt;</c>.
/// </summary>
public sealed class WorkflowEntityOptions<T> where T : class
{
    private string? _name;
    private Func<IServiceProvider, T, string>? _id;
    private Func<IServiceProvider, string, CancellationToken, Task<T?>>? _load;
    private Action<T, string>? _setState;
    private Func<T, string?>? _readState;

    /// <summary>The name stored on instances and referenced by <c>ForEntity(...)</c> in definitions. Defaults to the type name.</summary>
    public WorkflowEntityOptions<T> Name(string name) { _name = name; return this; }

    /// <summary>How to turn an entity into the id stored on the instance. Defaults to its <c>Id</c> property.</summary>
    public WorkflowEntityOptions<T> Id(Func<T, string> selector) { _id = (_, entity) => selector(entity); return this; }

    /// <summary>Like <see cref="Id(Func{T, string})"/> with access to the current scope's services.</summary>
    public WorkflowEntityOptions<T> Id(Func<IServiceProvider, T, string> selector) { _id = selector; return this; }

    /// <summary>How to load the entity by id. Return null when it does not exist. Required unless a provider package sets it.</summary>
    public WorkflowEntityOptions<T> Load(Func<IServiceProvider, string, CancellationToken, Task<T?>> loader) { _load = loader; return this; }

    /// <summary>
    /// Writes the workflow state code onto the entity on every transition, e.g.
    /// <c>(report, state) =&gt; report.Status = Enum.Parse&lt;ReportStatus&gt;(state, true)</c>.
    /// Entities that implement <see cref="IWorkflowStateful"/> get this automatically.
    /// </summary>
    public WorkflowEntityOptions<T> SyncState(Action<T, string> write) { _setState = write; return this; }

    /// <summary>
    /// Like <see cref="SyncState(Action{T, string})"/>, plus a way to read the current value, so the engine can put it back
    /// if saving a new instance fails. Without it a failed start leaves your own status column changed on the tracked entity.
    /// </summary>
    public WorkflowEntityOptions<T> SyncState(Action<T, string> write, Func<T, string?> read) { _setState = write; _readState = read; return this; }

    internal WorkflowEntityDescriptor Build()
    {
        var name = _name ?? typeof(T).Name;
        var load = _load ?? throw new InvalidOperationException(
            $"AddEntity<{typeof(T).Name}> needs .Load(...) to fetch the entity by id (or use the EF Core overload AddEntity<{typeof(T).Name}, TContext>()).");
        var id = _id ?? ConventionalId();
        var setState = _setState;
        Func<object, Action>? capture = null;
        if (_readState is { } read)
        {
            capture = entity =>
            {
                var previous = read((T)entity);
                return () => { if (previous is not null) setState!((T)entity, previous); };
            };
        }
        else if (setState is null && typeof(IWorkflowStateful).IsAssignableFrom(typeof(T)))
        {
            setState = (entity, state) => ((IWorkflowStateful)entity).WorkflowState = state;
            capture = entity =>
            {
                var stateful = (IWorkflowStateful)entity;
                var previous = stateful.WorkflowState;
                return () => stateful.WorkflowState = previous;
            };
        }

        return new WorkflowEntityDescriptor
        {
            Name = name,
            ClrType = typeof(T),
            Load = async (sp, entityId, ct) => await load(sp, entityId, ct),
            GetId = (sp, entity) => id(sp, (T)entity),
            SetState = setState is null ? null : (entity, state) => setState((T)entity, state),
            CaptureState = capture,
        };
    }

    private static Func<IServiceProvider, T, string> ConventionalId()
    {
        var property = typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)
                       ?? typeof(T).GetProperty(typeof(T).Name + "Id", BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException(
                           $"{typeof(T).Name} has no Id property. Specify how to get its id with .Id(e => ...).");
        return (_, entity) => Convert.ToString(property.GetValue(entity), CultureInfo.InvariantCulture)!;
    }
}
