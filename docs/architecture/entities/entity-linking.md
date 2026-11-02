---
domain: entities
spec: entity-linking
description: "Linking instances to your own entity rows: registry, [ForEntity], state sync, IWorkflowStateful"
entry_points: [WorkflowEntityRegistry]
commands: []
entities: [WorkflowEntityDescriptor]
events_in: []
events_out: []
classes: [ForEntityAttribute, EntityCompatibility, IWorkflowStateful, WorkflowEntityOptions, WorkflowContext, WorkflowGuard, WorkflowEffect]
tests: [tests/DungAT.WorkflowEngine.Tests/EntityLinkTests.cs, tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfEntityLinkTests.cs]
prerequisites: []
downstream: []
see_also: [engine/start-instance, engine/execute-command]
---

## Flow

1. `AddEntity<T, TDbContext>(e => e.SyncState(...))` registers a descriptor: name, how to read its id, how to load the row, and how to write its state.
2. `WorkflowEntityRegistry` resolves descriptors by name (case-insensitive) or by CLR type.
3. The engine loads the row for guards, effects, post actions and permissions, and writes the state column on every transition.
4. `[ForEntity(typeof(...))]` or a typed base (`WorkflowGuard<T>`, `WorkflowEffect<T>`, `WorkflowPostAction<T>`) restricts a component; `EntityCompatibility.Ensure` throws when it is used with another entity.

## Gotchas

- Entities implementing `IWorkflowStateful` get `WorkflowState` set automatically; `SyncState(...)` covers your own column.
- With EF Core the entity is loaded through the same scoped `DbContext`, so the state column commits in the same `SaveChanges` as the instance and history rows.
- Components without `[ForEntity]` are generic and work with any workflow.
- `StartAsync<T>` on an unregistered type throws `InvalidOperationException`.
- `WorkflowEntityDescriptor.CaptureState` lets the engine undo the state write when a new instance cannot be saved. `IWorkflowStateful` entities get it automatically; use `SyncState(write, read)` for your own column.
