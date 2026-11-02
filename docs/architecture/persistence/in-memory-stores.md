---
domain: persistence
spec: in-memory-stores
description: "Default process-local stores for definitions, instances and monitoring"
entry_points: [InMemoryWorkflowDefinitionStore, InMemoryWorkflowInstanceStore, InMemoryWorkflowMonitoringStore]
commands: []
entities: [WorkflowInstance, WorkflowDefinition]
events_in: []
events_out: []
classes: [IWorkflowInstanceStore, IWorkflowDefinitionStore, IWorkflowMonitoringStore, PagedResult, InstanceQuery]
tests: [tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/MonitoringTests.cs, tests/DungAT.WorkflowEngine.Tests/RaceAndOrderingTests.cs]
prerequisites: []
downstream: []
see_also: [persistence/ef-core-stores]
---

## Flow

1. `AddWorkflowEngine` registers these stores unless another store is configured.
2. They implement the same contracts as the EF stores: add instance with its first history entry, optimistic `TryUpdateAsync(expectedRowVersion)`, history, post action runs, paged instance queries, stats and activity.

## Gotchas

- Process-local: everything is lost on restart and not shared between app instances. For tests, demos and single-process use.
- They must keep the same observable rules as the EF stores (one instance per entity, deterministic history order); the shared engine tests cover both.
