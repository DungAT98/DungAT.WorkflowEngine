---
domain: engine
spec: start-instance
description: "Start a workflow instance for an entity: validation, duplicate-start rule, initial history, post actions"
entry_points: [StartAsync]
commands: []
entities: [WorkflowInstance, WorkflowHistoryEntry]
events_in: []
events_out: [WorkflowStateChanged]
classes: [DefaultWorkflowEngine, WorkflowInstanceAlreadyExistsException, WorkflowEntityDescriptor]
tests: [tests/DungAT.WorkflowEngine.Tests/ReviewRegressionTests.cs, tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/RaceAndOrderingTests.cs, tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfRobustnessTests.cs]
prerequisites: [entities/entity-linking]
downstream: [post-actions/post-action-runner]
see_also: [engine/execute-command]
---

## Flow

1. `StartAsync(code, entityType, entityId, user)` or `StartAsync<TEntity>(code, entity, user)`; the typed overload takes name and id from the registered entity descriptor.
2. The entity type is canonicalized to its registered spelling, so `order` and `Order` are one entity.
3. Latest definition is loaded. Fails with `NotFound` (no such workflow) or `EntityMismatch` (definition is for another entity).
4. If the entity already has an instance, fails with `AlreadyExists`.
5. A registered entity is loaded (`NotFound` if the row is gone) and its state column is set to the initial state before saving.
6. Instance and a `START` history entry (from `null` to initial state) are saved together by `IWorkflowInstanceStore.AddAsync`.
7. `WorkflowStateChanged` is published, then the initial state's `OnEnter` post actions run. A publisher that throws becomes a warning (`Event publishing failed: ...`) and does not stop the post actions.

## Gotchas

- One workflow instance per entity, enforced twice: an up-front lookup and a unique (EntityType, EntityId) index. If two starts race, the loser gets `WorkflowInstanceAlreadyExistsException` from the store, the entity's state is reset to the winner's state, and the result is `AlreadyExists`.
- If saving the instance fails with anything other than the duplicate-start race, the entity's previous state is put back through `WorkflowEntityDescriptor.CaptureState` (automatic for `IWorkflowStateful`; for `SyncState(write)` pass a read function too: `SyncState(write, read)`). Without one, a custom status column stays changed on the tracked entity.
- Start has no guards and no permission check.
- Post actions on the initial state run on start, with no transition (`ctx.Transition` is null).
