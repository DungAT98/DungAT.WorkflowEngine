---
name: spec-lifecycle
description: Full spec update lifecycle for the WorkflowEngine architecture specs in docs/architecture. Use when committing code, before commit, when asked to check spec impact, or when adding new engine/store/endpoint/validator/registry classes. Also use when the user says "review spec impact", "update the specs" or "write a spec".
---

# Spec Lifecycle

Detects affected specs, reviews impact, scaffolds new specs, validates indexes, and reports commit tags — all locally.

## When to Use

- Before committing any `src/` changes
- When adding a new Engine, Endpoints, Store, Runner, Publisher, Builder, Validator, Registry, AuthorizationFilter or Extensions class
- When renaming/moving those classes
- When adding/removing event publishing (`IWorkflowEventPublisher`) or post action behavior
- When the commit-msg hook warns and suggests running this skill

## Stage A — Detect

**Goal:** Find all affected specs from staged (or unstaged) `src/` changes. `samples/` and `tests/` are outside `src/` and are ignored.

### Step A1 — Get changed files

```bash
# Prefer staged; fall back to working tree
STAGED=$(git diff --cached --name-status --find-renames=90 -- src/)
if [ -z "$STAGED" ]; then
  STAGED=$(git diff --name-status --find-renames=90 -- src/)
fi
```

If no `src/` changes, report "No spec impact — no source changes" and **stop**.

### Step A2 — Fast-path: tests-only / infra-only

- If ALL changed files match `*.Tests.*` or `*.Tests/` → default `likely no-impact`, but still check Step A3 in case a new handler was added in a test project (rare).
- If ALL changed files are DI/Autofac modules only (no new handler/job class) → default `likely no-impact`.

### Step A3 — Primary routing lookup via INDEX.md

For each changed `src/` file, extract class names that match primary entry point patterns:

- `*Engine`, `*Endpoints`, `*Store`, `*Runner`, `*Publisher`, `*Builder`, `*Validator`, `*Registry`, `*AuthorizationFilter`, `*Extensions`
- not interfaces (`I*`) and not `*Base`

Look up each symbol in `docs/architecture/INDEX.md` → `## By Entry Point / Command` section:

```bash
grep -i "SymbolName" docs/architecture/INDEX.md
```

- **Found** → add owner spec to affected list
- **Not found** → candidate **uncovered primary flow** (go to Stage C)

### Step A4 — Secondary impact via classes: reverse lookup

Run the shared detector to find specs where changed classes appear in `classes:` field:

```bash
bash .claude/skills/spec-lifecycle/detect-specs.sh --staged
```

This parses all spec frontmatter and builds a reverse map of `classes:` → spec. Any match adds that spec to the affected list.

### Step A5 — Event change detection

In changed files, look for added/removed lines containing:
- `IWorkflowEventPublisher` / `PublishAsync` → new event publishing
- a new `WorkflowStateChanged`-style event type → new `events_out:` entry

If a new event is being published, check `GRAPH.md` for consumers that may need spec updates.

### Step A6 — Rename/move detection

If `git diff --name-status` shows `R` (rename):
- Extract old and new symbol names
- Check if old symbol still appears in any spec's `entry_points:`, `commands:`, or `classes:`
- Flag stale frontmatter references

### Step A7 — Detect output

Collect into a structured summary:

| Category | Items |
|---|---|
| **Owner specs** | Specs that own the changed handler/command via INDEX.md |
| **Related via classes:** | Specs that reference changed classes as secondary symbols |
| **Downstream neighbors** | 1-hop `downstream:` specs of owner specs |
| **Prerequisite neighbors** | 1-hop `prerequisites:` specs of owner specs |
| **Event neighbors** | Specs from GRAPH.md that publish/consume changed events |
| **Uncovered primaries** | New entry-point classes with no spec |
| **Stale references** | Spec frontmatter pointing to renamed/deleted symbols |

---

## Stage B — Review

**Goal:** Classify each affected spec.

### Step B1 — Load specs (token-efficient)

For each owner spec: read the full spec (~20-40 lines).
For each downstream/prerequisite neighbor: read only if the code change affects the interface between them (event shape, flow order, guard conditions).

**Cap:** Load at most 8 specs total. If more are affected, prioritize owner specs + specs with stale references.

### Step B2 — Read the code diff

```bash
git diff --cached -- <affected_src_files>   # or git diff for unstaged
```

### Step B3 — Classify each spec

| Classification | Criteria |
|---|---|
| `no-impact` | Code matches spec; or tests-only; or DI-only; or refactor with no flow/entity/event change |
| `spec-update-needed` | Entry point changed; event published changed; flow steps changed; gotcha/invariant changed; new entry-point class with no spec |
| `code-bug` | Code contradicts spec AND spec matches intended behavior (tests, linked specs agree with spec) |
| `spec-bug` | Spec is stale/wrong AND code/tests show current behavior is intentional |

**If code and spec disagree:** Do NOT silently pick one. State what changed, why it looks like code-bug vs spec-bug, and require the developer to classify it. Per CLAUDE.md: "If code and spec disagree: do not silently pick one — classify: code bug / spec bug / behavior change."

### Step B4 — Typed link review

If the code change affects:
- A new dependency on another flow → check if `prerequisites:` needs updating
- A new downstream trigger → check if `downstream:` needs updating
- A new event → check if `events_out:` / `events_in:` needs updating

Enforce cap: `prerequisites + downstream ≤ 10` per spec.

### Step B5 — Propose edits

For each spec needing updates, show:
1. The **exact lines** that need to change
2. The **proposed edit** (new text)
3. Wait for developer approval before making changes

**NEVER edit a spec without showing the proposed change and getting dev approval.**

---

## Stage C — Scaffold

**Goal:** Create new specs for uncovered primary flows.

### Step C1 — Decide: new spec vs merge into existing

Before creating a new file, check whether the new symbol belongs in an existing spec:
- Same flow root (e.g., a new command in the same mutation resolver)
- Same entity lifecycle
- Same primary trigger

If yes → update that existing spec's `entry_points:` or `commands:` instead of creating a new file.

### Step C2 — Infer domain folder

| Source path contains | Domain folder |
|---|---|
| `Dashboard` | `dashboard/` |
| `EntityFrameworkCore`, `InMemory` | `persistence/` |
| `PostAction` | `post-actions/` |
| `Building/`, `Definition`, `Publisher` | `definitions/` |
| `Entities/`, `EntityCompatibility`, `ForEntity` | `entities/` |
| `Engine/`, `WorkflowEngine*` registration | `engine/` |
| Cross-cutting convention | `patterns/` |

### Step C3 — Use template

Use the template at `.claude/skills/spec-lifecycle/spec-template.md` with proper frontmatter. Fill in:
- `domain:` from Step C2
- `spec:` kebab-case name derived from the class name
- `entry_points:` the new class (or the public method names it owns)
- `commands:` workflow command codes, if the class introduces any
- `events_out:` / `events_in:` from code inspection

Leave `## Flow` and `## Gotchas` as TODOs — the developer fills these during review.

### Step C4 — Propose scaffold

Show the proposed new spec file. Wait for developer approval before creating.

---

## Stage D — Validate

**Goal:** Ensure indexes are fresh and specs are internally valid.

### Step D1 — Run generate-index.sh

```bash
bash docs/architecture/generate-index.sh
```

This validates:
- Unique command/entry_point ownership (no duplicates)
- Typed link targets exist
- `prerequisites + downstream ≤ 10` cap
- No cycles in prerequisites/downstream DAGs

### Step D2 — Check freshness

If `INDEX.md` or `GRAPH.md` changed after regeneration → stage them.
If generate-index.sh fails → report errors and stop.

### Step D3 — Stale reference check

For each symbol in spec `entry_points:` and `commands:`, verify the class exists in `src/`:

```bash
bash .claude/skills/workflowengine-navigate/sym.sh "^SymbolName$"
```

If symbol not found in source → flag stale reference.

---

## Stage E — Report

### Step E1 — Output format

```
## Spec Lifecycle Review

### Changed Files
- src/DungAT.WorkflowEngine/Engine/NewRunner.cs (added)
- src/DungAT.WorkflowEngine/Engine/DefaultWorkflowEngine.cs (modified)

### Affected Specs
1. 📄 docs/architecture/engine/execute-command.md
   Status: ✅ No impact — code matches spec

2. 📄 docs/architecture/engine/new-flow.md
   Status: 🆕 New spec scaffolded (pending approval)

3. 📄 docs/architecture/post-actions/post-action-runner.md
   Status: ⚠️ Stale reference: `OldRunner` → `NewRunner`

### Uncovered Symbols
- ⚠️ NewRunner — no spec found (scaffolded above)

### Validation
- ✅ generate-index.sh passed
- ✅ INDEX.md/GRAPH.md regenerated and staged

### Gate
bash .githooks/check_spec_impact.sh --base origin/main --staged → spec-impact: OK
```

### Step E2 — Stage files

After developer approves all changes:
```bash
git add docs/architecture/<changed-specs>
git add docs/architecture/INDEX.md docs/architecture/GRAPH.md
```

### Step E3 — Gate check

Run `bash .githooks/check_spec_impact.sh --base origin/main --staged` (the `commit-msg` hook runs the same). No commit-message tag exists or is needed. If every affected spec is `no-impact` (bug fix that matches the spec, pure refactor), say so in the PR description and ask the reviewer to apply the `no-spec-change` label; CI fails the PR otherwise.

---

## Constraints

- **NEVER** edit a spec without showing the proposed change and getting dev approval
- **NEVER** assume code is wrong — if code and spec disagree, ASK the developer
- **NEVER** load full domain spec files (>200 lines) in one read — use Quick Reference headers
- **Cap** spec loading at 8 specs per review session
- **Cap** prerequisites + downstream ≤ 10 per spec
- Follow CLAUDE.md: fix spec + code in the same commit
