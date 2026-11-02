---
domain: engine
spec: permissions
description: "Role-based permission check for commands and the engine's own system user"
entry_points: [RoleBasedPermissionEvaluator, IWorkflowPermissionEvaluator]
commands: []
entities: [WorkflowUser]
events_in: []
events_out: []
classes: [WorkflowUser, WorkflowTransition]
tests: [tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/PostActionTests.cs]
prerequisites: []
downstream: []
see_also: [engine/execute-command]
---

## Flow

1. `IWorkflowPermissionEvaluator.CanExecuteAsync(context)` is called before guards, for every command and for `GetAvailableCommandsAsync`.
2. The default `RoleBasedPermissionEvaluator` allows when any of: `user.IsSystem`, the transition has no `AllowedRoles`, or the user is in one of the allowed roles.

## Gotchas

- Follow-up commands from post actions run as `WorkflowUser.System`, so they pass role checks but still have to satisfy guards.
- A custom evaluator must treat `user.IsSystem` as allowed, or follow-ups will fail with `NotAllowed`.
- `AllowedRoles` empty means open to everyone, not nobody.
