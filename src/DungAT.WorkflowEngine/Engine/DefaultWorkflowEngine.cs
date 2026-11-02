using DungAT.WorkflowEngine.Abstractions.Entities;
using DungAT.WorkflowEngine.Abstractions.Exceptions;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Options;
using Microsoft.Extensions.Logging;

namespace DungAT.WorkflowEngine.Engine;

internal sealed class DefaultWorkflowEngine(
    IWorkflowDefinitionStore definitions,
    IWorkflowInstanceStore instances,
    IWorkflowEntityRegistry entities,
    IWorkflowPermissionEvaluator permissions,
    IEnumerable<IWorkflowGuard> guards,
    IEnumerable<IWorkflowEffect> effects,
    IWorkflowEventPublisher events,
    WorkflowPostActionRunner postActions,
    WorkflowEngineOptions options,
    IServiceProvider services,
    TimeProvider time,
    ILogger<DefaultWorkflowEngine>? logger = null) : IWorkflowEngine
{
    public Task<WorkflowResult<WorkflowInstance>> StartAsync(
        string definitionCode, string entityType, string entityId, WorkflowUser user,
        string? data = null, CancellationToken ct = default) =>
        StartCoreAsync(definitionCode, entityType, entityId, null, user, data, ct);

    public Task<WorkflowResult<WorkflowInstance>> StartAsync<TEntity>(
        string definitionCode, TEntity entity, WorkflowUser user,
        string? data = null, CancellationToken ct = default) where TEntity : class
    {
        var descriptor = Describe(entity);
        return StartCoreAsync(definitionCode, descriptor.Name, descriptor.GetId(services, entity), entity, user, data, ct);
    }

    public Task<WorkflowInstance?> GetInstanceForAsync<TEntity>(TEntity entity, CancellationToken ct = default) where TEntity : class
    {
        var descriptor = Describe(entity);
        return instances.GetByEntityAsync(descriptor.Name, descriptor.GetId(services, entity), ct);
    }

    private async Task<WorkflowResult<WorkflowInstance>> StartCoreAsync(
        string definitionCode, string entityType, string entityId, object? entity, WorkflowUser user, string? data, CancellationToken ct)
    {
        // "order" and "Order" are the same registered entity: store the registered spelling so lookups and the unique index agree.
        entityType = CanonicalEntityType(entityType);
        var definition = await definitions.GetAsync(definitionCode, null, ct);
        if (definition is null)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.NotFound, $"Workflow '{definitionCode}' not found.");
        if (definition.EntityType is { Length: > 0 } expected && !string.Equals(expected, entityType, StringComparison.OrdinalIgnoreCase))
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.EntityMismatch,
                $"Workflow '{definitionCode}' is for {expected}, not {entityType}.");
        if (await instances.GetByEntityAsync(entityType, entityId, ct) is not null)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.AlreadyExists, $"{entityType} {entityId} already has a workflow.");

        // A registered entity must exist; its state column is set before saving so it commits together with the instance.
        var initial = definition.InitialState;
        WorkflowEntityDescriptor? descriptor = null;
        Action? undoState = null;
        if (entities.TryGetByName(entityType, out var registered))
        {
            descriptor = registered;
            entity ??= await descriptor.Load(services, entityId, ct);
            if (entity is null)
                return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.NotFound, $"{entityType} {entityId} was not found.");
            undoState = descriptor.CaptureState?.Invoke(entity);
            descriptor.SetState?.Invoke(entity, initial.Code);
        }

        var now = time.GetUtcNow();
        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            DefinitionCode = definition.Code,
            DefinitionVersion = definition.Version,
            EntityType = entityType,
            EntityId = entityId,
            CurrentStateCode = initial.Code,
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
            Data = data,
        };
        var history = new WorkflowHistoryEntry
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, FromStateCode = null,
            CommandCode = "START", ToStateCode = initial.Code, ExecutedBy = user.Id, ExecutedAt = now,
        };
        try { await instances.AddAsync(instance, history, ct); }
        catch (WorkflowInstanceAlreadyExistsException)
        {
            // Another start won the race after our check. Its instance is the real one, so the entity must show its state.
            if (await instances.GetByEntityAsync(entityType, entityId, ct) is { } winner)
                entity.SetState(descriptor, winner.CurrentStateCode);
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.AlreadyExists, $"{entityType} {entityId} already has a workflow.");
        }
        catch
        {
            // The instance was not created, so the entity must not keep showing a workflow state it never entered.
            undoState?.Invoke();
            throw;
        }
        var publishWarning = await PublishAsync(instance, history, ct);
        var warnings = await RunPostActionsAsync(definition, instance, null, null, initial.Code, user, null, null, history, entity, 0);
        return WorkflowResult<WorkflowInstance>.Success(instance, WithWarning(publishWarning, warnings));
    }

    public async Task<IReadOnlyList<WorkflowCommand>> GetAvailableCommandsAsync(
        Guid instanceId, WorkflowUser user, CancellationToken ct = default)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null) return [];
        var definition = await definitions.GetAsync(instance.DefinitionCode, instance.DefinitionVersion, ct);
        if (definition is null) return [];
        var (entity, _, missing) = await LoadEntityAsync(instance, ct);
        if (missing) return [];

        var result = new List<WorkflowCommand>();
        foreach (var t in definition.TransitionsFrom(instance.CurrentStateCode))
        {
            var context = new WorkflowContext(definition, instance, t, user, null, null, services, entity);
            if (await CheckAsync(context, ct) is null && definition.FindCommand(t.CommandCode) is { } command)
                result.Add(command);
        }
        return result;
    }

    public Task<WorkflowResult<WorkflowInstance>> ExecuteAsync(
        Guid instanceId, string commandCode, WorkflowUser user,
        string? comment = null, string? payload = null, CancellationToken ct = default) =>
        ExecuteCoreAsync(instanceId, commandCode, user, comment, payload, depth: 0, ct);

    // depth: how many follow-up commands deep this call is; 0 for a call from the host.
    private async Task<WorkflowResult<WorkflowInstance>> ExecuteCoreAsync(
        Guid instanceId, string commandCode, WorkflowUser user, string? comment, string? payload, int depth, CancellationToken ct)
    {
        var instance = await instances.GetAsync(instanceId, ct);
        if (instance is null)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.NotFound, "Instance not found.");
        var definition = await definitions.GetAsync(instance.DefinitionCode, instance.DefinitionVersion, ct);
        if (definition is null)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.NotFound, "Definition version not found.");

        var transition = definition.FindTransition(instance.CurrentStateCode, commandCode);
        if (transition is null)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.InvalidTransition,
                $"Command '{commandCode}' is not valid in state '{instance.CurrentStateCode}'.");
        if (transition.RequireComment && string.IsNullOrWhiteSpace(comment))
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.CommentRequired, "A comment is required.");

        var (entity, descriptor, missing) = await LoadEntityAsync(instance, ct);
        if (missing)
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.NotFound,
                $"{instance.EntityType} {instance.EntityId} no longer exists.");

        var context = new WorkflowContext(definition, instance, transition, user, comment, payload, services, entity);
        if (await CheckAsync(context, ct) is { } rejected)
            return WorkflowResult<WorkflowInstance>.Failure(rejected.Status, rejected.Message);

        foreach (var name in transition.EffectNames)
        {
            var effect = Resolve(effects, name, e => e.Name);
            EntityCompatibility.Ensure("Effect", name, effect.EntityTypes, context.Entity, context.Instance.EntityType);
            await effect.ExecuteAsync(context, ct);
        }

        var now = time.GetUtcNow();
        var updated = instance with { CurrentStateCode = transition.ToStateCode, UpdatedAt = now };
        var history = new WorkflowHistoryEntry
        {
            Id = Guid.NewGuid(), InstanceId = instance.Id, Sequence = instance.RowVersion + 1, FromStateCode = instance.CurrentStateCode,
            CommandCode = transition.CommandCode, ToStateCode = transition.ToStateCode,
            ExecutedBy = user.Id, ExecutedAt = now, Comment = comment, Payload = payload,
        };

        // The entity's state column changes in the same unit of work as the instance (one SaveChanges with EF Core),
        // and is put back if that save does not happen.
        entity.SetState(descriptor, transition.ToStateCode);
        bool saved;
        try { saved = await instances.TryUpdateAsync(updated, instance.RowVersion, history, ct); }
        catch { entity.SetState(descriptor, instance.CurrentStateCode); throw; }
        if (!saved)
        {
            entity.SetState(descriptor, instance.CurrentStateCode);
            return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.ConcurrencyConflict,
                "The instance was modified by someone else. Reload and retry.");
        }

        updated = updated with { RowVersion = instance.RowVersion + 1 };
        logger?.LogInformation("Workflow {Instance}: {From} --{Command}--> {To}",
            instance.Id, instance.CurrentStateCode, transition.CommandCode, transition.ToStateCode);
        var publishWarning = await PublishAsync(updated, history, ct);

        // The transition is final now: post actions can fail without undoing it.
        var warnings = await RunPostActionsAsync(
            definition, updated, transition, instance.CurrentStateCode, transition.ToStateCode, user, comment, payload, history, entity, depth);
        return WorkflowResult<WorkflowInstance>.Success(updated, WithWarning(publishWarning, warnings));
    }

    /// <summary>
    /// Publishes after the commit. A publisher that throws must not turn a saved change into an exception (the caller would retry
    /// something that already happened, and the post actions would be skipped), so the failure is logged and reported as a warning.
    /// </summary>
    private async Task<string?> PublishAsync(WorkflowInstance instance, WorkflowHistoryEntry history, CancellationToken ct)
    {
        try
        {
            await events.PublishAsync(new WorkflowStateChanged(instance, history), ct);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Publishing the state change of workflow instance {Instance} failed after it was saved", instance.Id);
            return $"Event publishing failed: {ex.Message}";
        }
    }

    private static IReadOnlyList<string> WithWarning(string? warning, IReadOnlyList<string> warnings) =>
        warning is null ? warnings : [warning, .. warnings];

    /// <summary>
    /// Runs the transition's post actions, then those of the state that was entered. Returns warnings for the ones that failed
    /// (plus warnings from follow-up commands they ran), and records every outcome.
    /// </summary>
    private async Task<IReadOnlyList<string>> RunPostActionsAsync(
        WorkflowDefinition definition, WorkflowInstance instance, WorkflowTransition? transition, string? fromState, string toState,
        WorkflowUser user, string? comment, string? payload, WorkflowHistoryEntry history, object? entity, int depth)
    {
        var names = (transition?.PostActionNames ?? []).Concat(definition.FindState(toState)?.PostActionNames ?? []).ToList();
        if (names.Count == 0) return [];

        var chainWarnings = new List<string>();
        async Task<WorkflowResult<WorkflowInstance>> FollowUp(string command, string? followComment, WorkflowUser? followUser)
        {
            if (depth >= options.MaxChainDepth)
            {
                logger?.LogWarning("Follow-up '{Command}' on {Instance} not run: chain depth limit {Limit} reached", command, instance.Id, options.MaxChainDepth);
                return WorkflowResult<WorkflowInstance>.Failure(WorkflowResultStatus.ChainDepthExceeded,
                    $"Follow-up '{command}' was not run: post actions may chain at most {options.MaxChainDepth} commands.");
            }
            var result = await ExecuteCoreAsync(instance.Id, command, followUser ?? WorkflowUser.System, followComment, null, depth + 1, CancellationToken.None);
            chainWarnings.AddRange(result.Warnings);
            return result;
        }

        var context = new WorkflowPostContext(
            definition, instance, transition, fromState, toState, user, comment, payload, history, services, entity, FollowUp);
        var runs = await postActions.RunAsync(names, context);
        try { await instances.AddPostActionRunsAsync(runs, CancellationToken.None); }
        catch (Exception ex) { logger?.LogError(ex, "Could not record post action runs for workflow instance {Instance}", instance.Id); }

        return runs.Where(r => !r.Succeeded).Select(r => $"{r.ActionName} failed: {r.Error}").Concat(chainWarnings).ToList();
    }

    public Task<WorkflowInstance?> GetInstanceAsync(Guid instanceId, CancellationToken ct = default) =>
        instances.GetAsync(instanceId, ct);

    public Task<WorkflowInstance?> GetInstanceByEntityAsync(string entityType, string entityId, CancellationToken ct = default) =>
        instances.GetByEntityAsync(CanonicalEntityType(entityType), entityId, ct);

    private string CanonicalEntityType(string entityType) =>
        entities.TryGetByName(entityType, out var descriptor) ? descriptor.Name : entityType;

    public Task<IReadOnlyList<WorkflowHistoryEntry>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default) =>
        instances.GetHistoryAsync(instanceId, ct);

    public Task<IReadOnlyList<WorkflowPostActionRun>> GetPostActionRunsAsync(Guid instanceId, CancellationToken ct = default) =>
        instances.GetPostActionRunsAsync(instanceId, ct);

    private WorkflowEntityDescriptor Describe(object entity) =>
        entities.TryGetByType(entity.GetType(), out var descriptor)
            ? descriptor
            : throw new InvalidOperationException(
                $"{entity.GetType().Name} is not registered as a workflow entity. Call AddEntity<{entity.GetType().Name}>() when configuring the engine.");

    /// <summary>Loads the instance's linked entity. <c>Missing</c> means its type is registered but the row is gone.</summary>
    private async Task<(object? Entity, WorkflowEntityDescriptor? Descriptor, bool Missing)> LoadEntityAsync(
        WorkflowInstance instance, CancellationToken ct)
    {
        if (!entities.TryGetByName(instance.EntityType, out var descriptor)) return (null, null, false);
        var entity = await descriptor.Load(services, instance.EntityId, ct);
        return (entity, descriptor, entity is null);
    }

    /// <summary>Returns null when permitted and all guards pass; otherwise the rejection.</summary>
    private async Task<WorkflowResult?> CheckAsync(WorkflowContext context, CancellationToken ct)
    {
        if (!await permissions.CanExecuteAsync(context, ct))
            return WorkflowResult.Failure(WorkflowResultStatus.NotAllowed, "You are not allowed to run this command.");
        foreach (var name in context.Transition.GuardNames)
        {
            var guard = Resolve(guards, name, g => g.Name);
            EntityCompatibility.Ensure("Guard", name, guard.EntityTypes, context.Entity, context.Instance.EntityType);
            var result = await guard.EvaluateAsync(context, ct);
            if (!result.Passed)
                return WorkflowResult.Failure(WorkflowResultStatus.GuardFailed, result.Reason ?? $"Guard '{name}' failed.");
        }
        return null;
    }

    private static T Resolve<T>(IEnumerable<T> candidates, string name, Func<T, string> nameOf) where T : class =>
        candidates.FirstOrDefault(c => string.Equals(nameOf(c), name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No {typeof(T).Name} registered with name '{name}'.");
}

internal static class EntityStateExtensions
{
    public static void SetState(this object? entity, WorkflowEntityDescriptor? descriptor, string stateCode)
    {
        if (entity is not null) descriptor?.SetState?.Invoke(entity, stateCode);
    }
}
