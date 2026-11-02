---
domain: engine
spec: queries
description: "Read side of the engine: available commands, instance lookup, history, post action runs"
entry_points: [GetAvailableCommandsAsync, GetInstanceAsync, GetInstanceByEntityAsync, GetInstanceForAsync, GetHistoryAsync, GetPostActionRunsAsync]
commands: []
entities: [WorkflowInstance, WorkflowHistoryEntry, WorkflowPostActionRun]
events_in: []
events_out: []
classes: [DefaultWorkflowEngine, IWorkflowEngine, IWorkflowInstanceStore]
tests: [tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/RaceAndOrderingTests.cs, tests/DungAT.WorkflowEngine.Tests/PostActionTests.cs]
prerequisites: []
downstream: []
see_also: [engine/execute-command]
---

## Flow

1. `GetAvailableCommandsAsync(instanceId, user)`: for each transition out of the current state, builds a context and keeps the command if the permission check and every guard pass. Returns empty if the instance, definition or linked entity is missing.
2. `GetInstanceAsync(id)`, `GetInstanceByEntityAsync(type, id)` and `GetInstanceForAsync(entity)` are thin lookups; the by-entity ones canonicalize the entity type name first.
3. `GetHistoryAsync` and `GetPostActionRunsAsync` delegate to the instance store.

## Gotchas

- `GetAvailableCommandsAsync` runs guards, so guards must be side-effect free.
- History order is by `Sequence`, because timestamps can tie. Stores must keep that order deterministic.
