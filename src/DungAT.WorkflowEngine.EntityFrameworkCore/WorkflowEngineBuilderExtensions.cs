using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DungAT.WorkflowEngine.EntityFrameworkCore;

public static class WorkflowEngineBuilderExtensions
{
    /// <summary>Stores definitions, instances and history in <typeparamref name="TContext"/>.</summary>
    public static WorkflowEngineBuilder UseEntityFrameworkCore<TContext>(this WorkflowEngineBuilder builder)
        where TContext : DbContext
    {
        builder.Services.Replace(ServiceDescriptor.Scoped<IWorkflowDefinitionStore, EfWorkflowDefinitionStore<TContext>>());
        builder.Services.Replace(ServiceDescriptor.Scoped<IWorkflowInstanceStore, EfWorkflowInstanceStore<TContext>>());
        builder.Services.Replace(ServiceDescriptor.Scoped<IWorkflowMonitoringStore, EfWorkflowMonitoringStore<TContext>>());
        return builder;
    }

    /// <summary>
    /// Links an entity of your DbContext to workflows. It is loaded by primary key through the same scoped context,
    /// so a state column written by the engine (see <c>SyncState</c> / <c>IWorkflowStateful</c>) is saved in the
    /// same transaction as the workflow instance and history.
    /// </summary>
    public static WorkflowEngineBuilder AddEntity<TEntity, TContext>(
        this WorkflowEngineBuilder builder, Action<WorkflowEntityOptions<TEntity>>? configure = null)
        where TEntity : class where TContext : DbContext =>
        builder.AddEntity<TEntity>(options =>
        {
            options.Load(EfEntityKeys.FindAsync<TEntity, TContext>).Id(EfEntityKeys.GetId<TEntity, TContext>);
            configure?.Invoke(options);
        });
}
