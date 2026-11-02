---
name: workflowengine-navigate
description: Use when investigating bugs, exploring unfamiliar code, or finding where a class/method/flow lives in the DungAT.WorkflowEngine codebase. Prevents expensive grep-across-src patterns that waste tokens.
---

# Navigate WorkflowEngine Codebase

Spec index + compact ctags lookup before opening any source file. Saves ~70% tokens vs `grep -r src/`.

## Pattern: INDEX.md → sym.sh → targeted Read

**Parallelise the discovery phase.** INDEX grep, subdomain INDEX grep, and `sym.sh` calls are independent — fire them in a single message with multiple tool calls, don't chain them sequentially.

### Step 1 — Find the spec

Start from `docs/architecture/ROUTING.md` for orientation, then use lookup tables:

```bash
grep -i "<keyword>" docs/architecture/INDEX.md                    # class/method → spec
grep "WorkflowStateChanged" docs/architecture/GRAPH.md            # event → publishers/consumers
grep -r "ClassName" docs/architecture/ --include="*.md" -l         # which spec covers X
```

`patterns/` covers cross-cutting conventions (e.g. `patterns/guard-effect-postaction.md`). Hit it when a ticket talks about what belongs in a guard, an effect or a post action.

Read the matched spec file (~20–40 lines) — flow steps, entry points, and gotchas are usually enough.

### Step 2 — Symbol lookup (use `sym.sh` first)

```bash
# Compact output: "name\trelpath:line\tkind[  class:Owner]" — capped at 20 hits
bash .claude/skills/workflowengine-navigate/sym.sh DefaultWorkflowEngine
bash .claude/skills/workflowengine-navigate/sym.sh '^WorkflowPostActionRunner'
bash .claude/skills/workflowengine-navigate/sym.sh '^IWorkflowInstanceStore$'

# Filter by owning class (substring match against ctags `class:` field)
bash .claude/skills/workflowengine-navigate/sym.sh TryUpdateAsync --class=InMemoryWorkflowInstanceStore
```

Universal Ctags mis-parses some C# 12 primary-constructor classes (e.g. `DefaultWorkflowEngine` shows as kind `m`, and members of classes such as `EfWorkflowInstanceStore` can be missing). If `sym.sh` finds nothing, use INDEX.md (it lists the owning spec per class) or a targeted `Grep` for the symbol.

Fall back to raw `.tags` grep only when a regex can't express the filter. The `.tags` index excludes `obj/` and `bin/` so generated code does not pollute hits.

Every `sym.sh` invocation appends a JSONL telemetry line to `.claude/logs/nav-metrics.jsonl` (ts, branch, pattern, class, hits, bytes). Useful for measuring whether the nav layer keeps paying off as the codebase grows; not required reading for normal use.

### Step 3 — Targeted Read

```
Read(file_path="src/...", offset=<line-from-sym.sh>, limit=30)
```

Never read a whole file when 20 lines will do.

## Updating architecture specs

After domain changes (new command, flow, gotcha): edit or create `docs/architecture/<domain>/<spec>.md` with YAML frontmatter (`domain`, `spec`, `description`, `entry_points`, `commands`, `entities`, `events_in`, `events_out`, `classes`, `tests`, `prerequisites`, `downstream`, `see_also`). Then regenerate indexes: `bash docs/architecture/generate-index.sh`. If the change is a recurring convention rather than a single flow, add or extend a spec under `patterns/`.

## Regenerating indexes (rare)

`.tags` and arch docs auto-regen via hooks (`.claude/settings.json`). For manual runs see the `init-workflowengine-navigate` command.

## Common mistakes

- `Grep` across `src/` first — check INDEX.md or `sym.sh` first.
- Reading a 300-line file for one method — use `offset`+`limit` after `sym.sh`.
- Skipping spec files — the spec often already answers the question.
- Chaining independent INDEX/`sym.sh` calls sequentially — batch them in one turn.
