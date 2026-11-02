using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Building;
using DungAT.WorkflowEngine.Entities;
using DungAT.WorkflowEngine.Options;
using DungAT.WorkflowEngine.InMemory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine;

/// <summary>Configures the engine; returned by <c>AddWorkflowEngine</c>.</summary>
public sealed class WorkflowEngineBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    /// <summary>Registers a code-defined workflow. Publish it to the store with <see cref="WorkflowDefinitionPublisher"/>.</summary>
    public WorkflowEngineBuilder AddDefinition(WorkflowDefinitionBuilder.TransitionBuilder chain) => AddDefinition(chain.Owner);

    public WorkflowEngineBuilder AddDefinition(WorkflowDefinitionBuilder.StateEnterBuilder chain) => AddDefinition(chain.Owner);

    public WorkflowEngineBuilder AddDefinition(WorkflowDefinitionBuilder builder)
    {
        foreach (var type in builder.ServiceTypes.Distinct())
        {
            if (typeof(IWorkflowGuard).IsAssignableFrom(type))
                Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IWorkflowGuard), type));
            if (typeof(IWorkflowEffect).IsAssignableFrom(type))
                Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IWorkflowEffect), type));
            if (typeof(IWorkflowPostAction).IsAssignableFrom(type))
                Services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IWorkflowPostAction), type));
        }
        Services.AddSingleton(new CodeDefinition(builder.Build()));
        return this;
    }

    /// <summary>
    /// Links a host entity type (e.g. a Report table) to workflows. The engine loads it for guards and effects
    /// and can mirror the workflow state onto it.
    /// </summary>
    public WorkflowEngineBuilder AddEntity<T>(Action<WorkflowEntityOptions<T>>? configure = null) where T : class
    {
        var options = new WorkflowEntityOptions<T>();
        configure?.Invoke(options);
        Services.AddSingleton(options.Build());
        return this;
    }

    public WorkflowEngineBuilder AddGuard<T>() where T : class, IWorkflowGuard
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowGuard, T>());
        return this;
    }

    public WorkflowEngineBuilder AddEffect<T>() where T : class, IWorkflowEffect
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowEffect, T>());
        return this;
    }

    public WorkflowEngineBuilder AddPostAction<T>() where T : class, IWorkflowPostAction
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowPostAction, T>());
        return this;
    }

    /// <summary>Changes engine-wide settings such as <see cref="WorkflowEngineOptions.MaxChainDepth"/>.</summary>
    public WorkflowEngineBuilder Configure(Action<WorkflowEngineOptions> configure)
    {
        var options = Services.Select(d => d.ImplementationInstance).OfType<WorkflowEngineOptions>().First();
        configure(options);
        return this;
    }

    public WorkflowEngineBuilder UsePermissionEvaluator<T>() where T : class, IWorkflowPermissionEvaluator
    {
        Services.Replace(ServiceDescriptor.Scoped<IWorkflowPermissionEvaluator, T>());
        return this;
    }

    public WorkflowEngineBuilder UseEventPublisher<T>() where T : class, IWorkflowEventPublisher
    {
        Services.Replace(ServiceDescriptor.Scoped<IWorkflowEventPublisher, T>());
        return this;
    }

    internal static InMemoryWorkflowMonitoringStore InMemoryMonitoring(IServiceProvider sp) =>
        new(sp.GetRequiredService<IWorkflowInstanceStore>() as InMemoryWorkflowInstanceStore
                ?? throw new InvalidOperationException(
                    "The in-memory monitoring store requires the in-memory instance store. " +
                    "Register a matching IWorkflowMonitoringStore for your persistence provider."),
            sp.GetRequiredService<TimeProvider>());

    /// <summary>Uses the volatile in-memory stores (tests, demos).</summary>
    public WorkflowEngineBuilder UseInMemoryStores()
    {
        Services.Replace(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, InMemoryWorkflowDefinitionStore>());
        Services.Replace(ServiceDescriptor.Singleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>());
        Services.Replace(ServiceDescriptor.Singleton<IWorkflowMonitoringStore>(InMemoryMonitoring));
        return this;
    }
}
