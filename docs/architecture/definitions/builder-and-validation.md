---
domain: definitions
spec: builder-and-validation
description: "Fluent definition builder, structural validation, and guard/effect/entity compatibility validation"
entry_points: [WorkflowDefinitionBuilder, WorkflowDefinitionValidator, WorkflowComponentValidator]
commands: []
entities: [WorkflowDefinition, WorkflowState, WorkflowTransition, WorkflowCommand]
events_in: []
events_out: []
classes: [WorkflowComponentRegistry, WorkflowDefinitionException]
tests: [tests/DungAT.WorkflowEngine.Tests/EngineTests.cs, tests/DungAT.WorkflowEngine.Tests/EntityLinkTests.cs]
prerequisites: []
downstream: [definitions/publishing-and-versioning]
see_also: [dashboard/static-assets-and-editor]
---

## Flow

1. `WorkflowDefinitionBuilder.Define(code, name)` then `.State(...)`, `.On(state).Command(...).GoTo(...)`, `.Guard<T>()`, `.Effect<T>()`, `.PostAction<T>()`, `.AllowRoles(...)`, `.OnEnter(state).PostAction<T>()`, `.ForEntity(name)`.
2. `WorkflowDefinitionValidator.Validate(definition)` checks structure (states, one initial state, transitions that point at existing states).
3. `WorkflowComponentValidator.Validate(definition, registry)` checks that every named guard, effect and post action is registered and is meant for the definition's entity. `WorkflowComponentRegistry` is that registry.
4. Problems are returned as a list of strings; `WorkflowDefinitionException` carries them.

## Gotchas

- Structural and component validation are separate: the dashboard editor runs both before publishing; the startup publisher runs the component check.
- A guard used with the wrong entity is rejected here, and again at runtime by `EntityCompatibility`.
