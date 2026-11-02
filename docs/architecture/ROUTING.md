# Architecture routing

Start here, then `grep -i <keyword> docs/architecture/INDEX.md`, then read the one matching spec (20-40 lines).

| Package | Domain folder |
|---|---|
| `DungAT.WorkflowEngine` (Engine/, WorkflowEngine*.cs) | `engine/` |
| `DungAT.WorkflowEngine` (Building/, WorkflowDefinitionPublisher) | `definitions/` |
| `DungAT.WorkflowEngine` (Entities/, EntityCompatibility) | `entities/` |
| `DungAT.WorkflowEngine` (WorkflowPostActionRunner) | `post-actions/` |
| `DungAT.WorkflowEngine` (InMemory/), `DungAT.WorkflowEngine.EntityFrameworkCore` | `persistence/` |
| `DungAT.WorkflowEngine.Dashboard` | `dashboard/` |
| Cross-cutting conventions | `patterns/` |

`DungAT.WorkflowEngine.Abstractions` holds the models and interfaces; its types appear in the `classes:` of the specs that use them.

Spec format: see `.claude/skills/spec-lifecycle/spec-template.md`. `INDEX.md` and `GRAPH.md` are generated: `bash docs/architecture/generate-index.sh`.
