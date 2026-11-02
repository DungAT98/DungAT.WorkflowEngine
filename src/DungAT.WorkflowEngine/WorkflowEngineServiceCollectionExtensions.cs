using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Entities;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Entities;
using DungAT.WorkflowEngine.Engine;
using DungAT.WorkflowEngine.InMemory;
using DungAT.WorkflowEngine.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine;

public static class WorkflowEngineServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(
        this IServiceCollection services, Action<WorkflowEngineBuilder>? configure = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new WorkflowEngineOptions());
        services.TryAddSingleton<IWorkflowDefinitionStore, InMemoryWorkflowDefinitionStore>();
        services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
        services.TryAddSingleton<IWorkflowMonitoringStore>(WorkflowEngineBuilder.InMemoryMonitoring);
        services.TryAddSingleton<IWorkflowEntityRegistry>(sp => new WorkflowEntityRegistry(sp.GetServices<WorkflowEntityDescriptor>()));
        services.TryAddScoped<IWorkflowComponentRegistry, WorkflowComponentRegistry>();
        services.TryAddScoped<IWorkflowPermissionEvaluator, RoleBasedPermissionEvaluator>();
        services.TryAddScoped<IWorkflowEventPublisher, NullWorkflowEventPublisher>();
        services.TryAddScoped<WorkflowPostActionRunner>();
        services.TryAddScoped<IWorkflowEngine, DefaultWorkflowEngine>();
        services.TryAddScoped(sp => new WorkflowDefinitionPublisher(
            sp.GetServices<CodeDefinition>(), sp.GetRequiredService<IWorkflowDefinitionStore>(),
            sp.GetRequiredService<IWorkflowComponentRegistry>()));
        configure?.Invoke(new WorkflowEngineBuilder(services));
        return services;
    }
}
