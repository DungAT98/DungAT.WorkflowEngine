---
domain: post-actions
spec: post-action-runner
description: "Post actions run after a saved transition: failure isolation, recorded runs, follow-up commands"
entry_points: [WorkflowPostActionRunner]
commands: []
entities: [WorkflowPostActionRun]
events_in: []
events_out: []
classes: [WorkflowPostContext, WorkflowPostAction, IWorkflowPostAction, DefaultWorkflowEngine, WorkflowEngineOptions]
tests: [tests/DungAT.WorkflowEngine.Tests/PostActionTests.cs, tests/DungAT.WorkflowEngine.EntityFrameworkCore.Tests/EfEngineTests.cs]
prerequisites: []
downstream: []
see_also: [patterns/guard-effect-postaction, persistence/ef-core-stores]
---

## Flow

1. After the commit (and the event), the engine collects the transition's post action names, then the entered state's `OnEnter` ones.
2. `WorkflowPostActionRunner.RunAsync` runs them one after another with `CancellationToken.None`. Each outcome (name, success, error truncated to 1900 chars, start, duration, sequence) becomes a `WorkflowPostActionRun`.
3. Runs are saved with `AddPostActionRunsAsync`; a failure to save them is logged only.
4. Failed actions become `result.Warnings`.
5. `ctx.FollowUpAsync(command)` runs another command through the engine as `WorkflowUser.System` (or a given user), one level deeper.

## Gotchas

- A failing action never undoes the transition and never stops the rest.
- Chains stop at `MaxChainDepth`; the follow-up returns `ChainDepthExceeded` and logs a warning. Warnings from follow-ups bubble up to the original result.
- A missing registration is recorded as a failed run, not thrown.
- Actions are in-process and run right after the commit; a crash in between loses them. For guaranteed delivery use an outbox in an `IWorkflowEventPublisher`.
- Order is deterministic: runs are stored with a `Sequence` and read back in that order.
