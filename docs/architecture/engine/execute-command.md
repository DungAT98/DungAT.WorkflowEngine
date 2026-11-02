---
domain: engine
spec: execute-command
description: "Run a command on an instance: permission, guards, effects, optimistic save, events, post actions"
entry_points: [DefaultWorkflowEngine, ExecuteAsync]
commands: []
entities: [WorkflowInstance, WorkflowHistoryEntry, WorkflowTransition]
events_in: []
events_out: [WorkflowStateChanged]
classes: [EntityStateExtensions, NullWorkflowEventPublisher, EntityCompatibility, WorkflowResultStatus, WorkflowContext]
tests: [tests/DungAT.WorkflowEngine.Tests/ReviewRegressionTests.cs, tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/RaceAndOrderingTests.cs, tests/DungAT.WorkflowEngine.Tests/EntityLinkTests.cs]
prerequisites: [engine/permissions, entities/entity-linking, patterns/guard-effect-postaction]
downstream: [post-actions/post-action-runner]
see_also: [engine/start-instance]
---

## Flow

1. Load instance, then the definition version the instance was started on (`NotFound` if either is missing).
2. Find the transition for (current state, command): `InvalidTransition` if none; `CommentRequired` if the transition requires a comment and none was given.
3. Load the linked entity if its type is registered (`NotFound` if the row is gone).
4. Check: the permission evaluator first (`NotAllowed`), then each guard in order (`GuardFailed` with the guard's reason).
5. Run effects in order, before the save. An exception aborts the command.
6. Build the new instance (`RowVersion` + 1) and a history entry with `Sequence = old RowVersion + 1`. Set the entity's state column, then `TryUpdateAsync(updated, expectedRowVersion, history)`.
7. If the update lost a race: restore the entity's state and return `ConcurrencyConflict`. If it threw, restore and rethrow.
8. Publish `WorkflowStateChanged`, then run post actions (transition's, then the entered state's `OnEnter`). Failures become `result.Warnings`.

## Gotchas

- Effects run before the save. If the save then loses the race or fails, the effect already happened, and a retry runs it again. Effects must be idempotent; use post actions for exactly-once work.
- The event is published after the commit; `NullWorkflowEventPublisher` is the default (no-op). It is the hook for an outbox. A publisher that throws is logged and reported as a warning (`Event publishing failed: ...`): the change is already saved, so the call still succeeds and the post actions still run.
- Guards and effects are resolved by name, case-insensitive. A missing registration throws `InvalidOperationException`.
- An instance keeps running on the definition version it started with, even after a new version is published.
