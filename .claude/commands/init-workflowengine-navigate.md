---
name: init-workflowengine-navigate
description: One-time setup for codebase navigation — generates .tags ctags index, architecture spec indexes, adds auto-regen hooks, and verifies .gitignore. Run this once on a fresh clone.
---

# Initialize WorkflowEngine Navigate

Set up ctags index, architecture spec indexes, and auto-regen hooks for token-efficient codebase navigation. Idempotent — safe to run multiple times.

## Step 1 — Locate ctags

Find a working ctags binary. Universal Ctags is required (not the macOS built-in).

```bash
# Try common locations
for BIN in /opt/homebrew/bin/ctags /usr/local/bin/ctags ctags; do
  if command -v "$BIN" &>/dev/null && "$BIN" --version 2>&1 | grep -q "Universal Ctags"; then
    echo "Found Universal Ctags: $BIN"
    CTAGS_BIN="$BIN"
    break
  fi
done

if [ -z "$CTAGS_BIN" ]; then
  echo "ERROR: Universal Ctags not found. Install with: brew install universal-ctags"
  exit 1
fi
```

If ctags is not found, tell the user to install it (`brew install universal-ctags` on macOS) and stop.

## Step 2 — Generate .tags index

```bash
"$CTAGS_BIN" -R --languages=C# --fields=+nkz --exclude=obj --exclude=bin -f .tags src/
echo "Generated .tags with $(wc -l < .tags) symbols"
```

The `--exclude=obj --exclude=bin` flags keep generated code out of `.tags`.

## Step 3 — Ensure .tags is in .gitignore

Check if `.tags` is already in `.gitignore`. If not, append it:

```bash
if ! grep -qx '.tags' .gitignore 2>/dev/null; then
  echo '.tags' >> .gitignore
  echo "Added .tags to .gitignore"
else
  echo ".tags already in .gitignore"
fi
```

## Step 4 — Generate architecture spec indexes

Run the index generation script to build INDEX.md and GRAPH.md:

```bash
bash docs/architecture/generate-index.sh
```

This parses YAML frontmatter from all spec files in `docs/architecture/*/` and generates:
- `docs/architecture/INDEX.md` — master spec index
- `docs/architecture/GRAPH.md` — flow links + event pub/sub map

It also validates unique ownership, link targets, the 10-link cap and cycles. If it exits non-zero, fix the reported problems.

## Step 5 — Check PostToolUse hooks in .claude/settings.json

The repo ships `.claude/settings.json` with both hooks already wired (project-relative, via `$CLAUDE_PROJECT_DIR`). Verify with `jq -e '.hooks.PostToolUse' .claude/settings.json`. Only if it is missing, recreate it with the two entries below. Do not use absolute paths.

### Hook 1: ctags auto-regen on .cs file writes

```json
{
  "matcher": "Write",
  "hooks": [
    {
      "type": "command",
      "command": "jq -r '.tool_input.file_path // empty' | grep -qE '[.]cs$' && command -v ctags >/dev/null && ctags -R --languages=C# --fields=+nkz --exclude=obj --exclude=bin -f \"$CLAUDE_PROJECT_DIR/.tags\" \"$CLAUDE_PROJECT_DIR/src/\" 2>/dev/null || true",
      "async": true,
      "statusMessage": "Updating ctags index..."
    }
  ]
}
```

### Hook 2: architecture index auto-regen on spec file edits

First, ensure the hook script exists at `.claude/hooks/regen-arch-index.sh` and is executable. If it doesn't exist, create it:

```bash
#!/bin/bash
fp=$(python3 -c "import sys,json; print(json.load(sys.stdin)['tool_input']['file_path'])" 2>/dev/null)

# Only trigger for spec files in docs/architecture subdirectories (not INDEX/GRAPH)
if ! echo "$fp" | grep -qE 'docs/architecture/[^/]+/[^/]+\.md$'; then
  exit 0
fi
if echo "$fp" | grep -qE '(INDEX|GRAPH)\.md$'; then
  exit 0
fi

repo_root=$(git rev-parse --show-toplevel 2>/dev/null)
if [ -z "$repo_root" ]; then
  script_dir=$(cd "$(dirname "$0")" && pwd)
  repo_root=$(cd "$script_dir/../.." && pwd)
fi

cd "$repo_root" || exit 1
bash docs/architecture/generate-index.sh 2>&1 | tail -3
```

Then add the hook entry:

```json
{
  "matcher": "Edit|Write",
  "hooks": [
    {
      "type": "command",
      "command": "\"$CLAUDE_PROJECT_DIR\"/.claude/hooks/regen-arch-index.sh",
      "async": true,
      "statusMessage": "Regenerating architecture index..."
    }
  ]
}
```

**Important:** For each hook, if `settings.local.json` already has a `hooks.PostToolUse` array, check if a matching hook already exists. If it does, skip. If not, append the new entry to the existing array.

After writing, validate the JSON:
```bash
jq -e '.hooks.PostToolUse' .claude/settings.json
```

## Step 6 — Opt in to repo githooks (commit-msg)

If `.githooks/commit-msg` exists, point git at it. This enables the local spec-impact warning on every commit.

```bash
if [ -f .githooks/commit-msg ]; then
  current=$(git config --get core.hooksPath 2>/dev/null)
  if [ "$current" = ".githooks" ]; then
    echo "core.hooksPath already set to .githooks"
  else
    git config core.hooksPath .githooks
    echo "Set core.hooksPath = .githooks (was: ${current:-default})"
  fi
else
  echo ".githooks/commit-msg not present — skipping"
fi
```

## Step 7 — Verify skill and docs exist

Confirm these files are present (they should be checked into the repo):
- `.claude/skills/workflowengine-navigate/SKILL.md`
- `CLAUDE.md`
- `docs/architecture/ROUTING.md`
- `docs/architecture/INDEX.md`
- `docs/architecture/GRAPH.md`
- `docs/architecture/generate-index.sh`

Report which are found and which are missing. Missing files are not a blocker — the ctags index works independently.

## Done

Report:
- ctags binary location
- Number of symbols indexed
- Number of architecture spec files found
- Whether hooks were added or already existed
- Whether .gitignore was updated
- Whether architecture indexes generated successfully
- Whether `core.hooksPath` was set to `.githooks` (or already set / skipped)

Tell the user: "Navigation is ready. Claude will use `docs/architecture/INDEX.md` + `.tags` before grepping source files. Hooks auto-regenerate both indexes when you edit `.cs` or spec files. The spec-impact warning is active via `.githooks`."
