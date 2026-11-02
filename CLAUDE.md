# DungAT.WorkflowEngine

A persistence-agnostic workflow (state machine) engine for .NET with EF Core stores and an embedded dashboard. See `README.md` for usage.

## Navigating

Architecture specs live in `docs/architecture/<domain>/*.md`. Before opening source:

1. Start at `docs/architecture/ROUTING.md`, then `grep -i <keyword> docs/architecture/INDEX.md`; read the matching spec.
2. Find a symbol with `bash .claude/skills/workflowengine-navigate/sym.sh <regex>` (needs Universal Ctags; set up once with `/init-workflowengine-navigate`).
3. Read only the lines you need.

## Specs

- Before committing `src/` changes, run the `spec-lifecycle` skill. The `commit-msg` hook warns and CI (`spec-impact`) fails when a production change has no spec change.
- Fix spec and code in the same commit.
- If code and spec disagree, do not silently pick one: classify it as a code bug, a spec bug, or a behavior change, and ask.
- `INDEX.md` and `GRAPH.md` are generated: `bash docs/architecture/generate-index.sh`. Never edit them by hand.
- Spec format: `.claude/skills/spec-lifecycle/spec-template.md`.

## Build and test

```bash
dotnet build -c Release
dotnet test -c Release --no-build
```
