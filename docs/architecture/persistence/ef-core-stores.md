---
domain: persistence
spec: ef-core-stores
description: "EF Core stores: model configuration, optimistic concurrency, unique-instance rule, SQLite dates"
entry_points: [EfWorkflowDefinitionStore, EfWorkflowInstanceStore, EfWorkflowMonitoringStore, WorkflowModelBuilderExtensions, WorkflowEngineBuilderExtensions]
commands: []
entities: [WorkflowInstance, WorkflowHistoryEntry, WorkflowPostActionRun, StoredWorkflowDefinition]
events_in: []
events_out: []
classes: [EfEntityKeys, IWorkflowInstanceStore]
tests: [tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfEngineTests.cs, tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfEntityLinkTests.cs, tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfRobustnessTests.cs]
prerequisites: []
downstream: []
see_also: [persistence/in-memory-stores, definitions/publishing-and-versioning]
---

## Flow

1. `UseEntityFrameworkCore<TDbContext>()` swaps in the EF stores; the host's `OnModelCreating` calls `modelBuilder.ApplyWorkflowEngineConfiguration(schema, storeDatesAsBinary)`.
2. `AddAsync` saves instance and first history entry; a `DbUpdateException` from the unique (EntityType, EntityId) index becomes `WorkflowInstanceAlreadyExistsException`.
3. `TryUpdateAsync` sets `RowVersion` as the concurrency token (original = expected, current = expected + 1). A `DbUpdateConcurrencyException` on the instance returns `false`, which the engine reports as `ConcurrencyConflict`.
4. The linked entity is tracked in the same `DbContext`, so its state column commits in the same `SaveChanges`.
5. The monitoring store serves stats, activity and the instance search used by the dashboard.

## Gotchas

- SQLite cannot sort or compare `DateTimeOffset`. Use `ApplyWorkflowEngineConfiguration(storeDatesAsBinary: true)`.
- Post action runs live in the `WorkflowPostActionRuns` table and history has a `Sequence` column: add a migration when upgrading.
- `EfEntityKeys` converts the string entity id into the entity's key type; malformed ids are treated as not found.
