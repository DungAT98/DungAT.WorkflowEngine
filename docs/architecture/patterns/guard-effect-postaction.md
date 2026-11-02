---
domain: patterns
spec: guard-effect-postaction
description: "When to use a guard, an effect or a post action: timing, failure and idempotency rules"
entry_points: []
commands: []
entities: []
events_in: []
events_out: []
classes: [WorkflowGuard, WorkflowEffect, WorkflowPostAction, IWorkflowGuard, IWorkflowEffect, IWorkflowPostAction]
tests: [tests/DungAT.WorkflowEngine.Tests/PostActionTests.cs, tests/DungAT.WorkflowEngine.Tests/EngineTests.cs]
prerequisites: []
downstream: []
see_also: [engine/execute-command, post-actions/post-action-runner]
---

## Flow

| | Runs | Can stop the transition | On failure |
|---|---|---|---|
| Guard | before, during permission check | yes (`GuardFailed`) | transition rejected |
| Effect | before the save | yes (exception) | transition aborted; may have already happened |
| Post action | after the save | no | recorded, warning, the rest still run |

## Gotchas

- Guards also run for `GetAvailableCommandsAsync`: keep them side-effect free.
- Effects must be idempotent: a lost concurrency race or failed save runs them again on retry.
- Exactly-once work (emails, calls to other systems) belongs in a post action, or an outbox in `IWorkflowEventPublisher`.
- When adding a component, give it a unique name and (if it needs an entity) a `[ForEntity]` or typed base class.
