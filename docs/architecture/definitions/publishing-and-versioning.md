---
domain: definitions
spec: publishing-and-versioning
description: "Publishing code definitions at startup; a new version only when the structure changes"
entry_points: [WorkflowDefinitionPublisher]
commands: []
entities: [WorkflowDefinition, StoredWorkflowDefinition]
events_in: []
events_out: []
classes: [CodeDefinition, IWorkflowDefinitionStore, WorkflowDefinitionSource]
tests: [tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfRobustnessTests.cs, tests/DungAT.WorkflowEngine.Tests/EngineTests.cs]
prerequisites: [definitions/builder-and-validation]
downstream: []
see_also: [persistence/ef-core-stores]
---

## Flow

1. `PublishAllAsync()` validates every registered code definition against the component registry; any problem throws `WorkflowDefinitionException` listing all of them.
2. For each definition it loads the latest stored version and compares a JSON fingerprint (version number ignored).
3. If there is none or it differs, `IWorkflowDefinitionStore.PublishAsync` stores a new version.

## Gotchas

- Run once at startup, on every start. Unchanged definitions do not create new versions.
- Concurrent publishes from several app instances can race on the version number. The EF store handles `DbUpdateException` on publish.
- Code definitions are republished at every startup, so the dashboard editor treats them as read-only.
