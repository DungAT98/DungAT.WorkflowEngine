---
domain: engine
spec: registration-and-di
description: "AddWorkflowEngine: the fluent builder, DI registration, options"
entry_points: [WorkflowEngineBuilder, WorkflowEngineServiceCollectionExtensions]
commands: []
entities: []
events_in: []
events_out: []
classes: [WorkflowEngineOptions, WorkflowEntityOptions, CodeDefinition]
tests: [tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/EntityLinkTests.cs]
prerequisites: []
downstream: []
see_also: [definitions/publishing-and-versioning, persistence/ef-core-stores]
---

## Flow

1. `services.AddWorkflowEngine(wf => ...)` creates a `WorkflowEngineBuilder` and registers the engine, post action runner, registries, in-memory stores (the default) and `WorkflowDefinitionPublisher`.
2. The builder collects: `AddDefinition`, `AddEntity<T, TDbContext>`, guards, effects, post actions, `Configure(o => ...)` for `WorkflowEngineOptions`, and a store (`UseEntityFrameworkCore<TDbContext>()` replaces the in-memory ones).
3. `WorkflowEngineOptions.MaxChainDepth` (default 5) caps follow-up chains.

## Gotchas

- Registering a definition does not store it: call `WorkflowDefinitionPublisher.PublishAllAsync()` at startup.
- Registering only a guard or effect type does not link it to a workflow; the definition refers to it by name.
