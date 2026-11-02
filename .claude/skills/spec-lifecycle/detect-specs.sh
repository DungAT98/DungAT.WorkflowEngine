#!/usr/bin/env bash
# detect-specs.sh — Find affected specs from staged/unstaged src/ changes
# Usage:
#   bash .claude/skills/spec-lifecycle/detect-specs.sh [--staged|--unstaged] [--hook]
# Output: structured report of affected specs, uncovered symbols, stale refs
set -euo pipefail

ARCH_DIR="docs/architecture"
INDEX="$ARCH_DIR/INDEX.md"
GRAPH="$ARCH_DIR/GRAPH.md"

MODE="staged"
HOOK_MODE=false
for arg in "$@"; do
  case "$arg" in
    --staged)   MODE="staged" ;;
    --unstaged) MODE="unstaged" ;;
    --hook)     HOOK_MODE=true ;;
  esac
done

# --- Get changed src/ files ---
if [[ "$MODE" == "staged" ]]; then
  CHANGES=$(git diff --cached --name-status --find-renames=90 -- src/ 2>/dev/null || true)
  if [[ -z "$CHANGES" ]]; then
    CHANGES=$(git diff --name-status --find-renames=90 -- src/ 2>/dev/null || true)
  fi
else
  CHANGES=$(git diff --name-status --find-renames=90 -- src/ 2>/dev/null || true)
fi

if [[ -z "$CHANGES" ]]; then
  echo "NO_SRC_CHANGES"
  exit 0
fi

# --- Classify: tests-only / infra-only ---
SRC_FILES=$(echo "$CHANGES" | awk '{print $NF}')
NON_TEST_FILES=$(echo "$SRC_FILES" | grep -v '\.Tests\.' | grep -v '\.Tests/' || true)
TESTS_ONLY=false
if [[ -z "$NON_TEST_FILES" ]]; then
  TESTS_ONLY=true
fi

# --- Extract primary symbols from changed files ---
# Match: *Engine, *Endpoints, *Store, *Runner, *Publisher, *Builder, *Validator, *Registry, *AuthorizationFilter, *Extensions
# (excluding interfaces and base classes)
SYMBOLS_FILE=$(mktemp)
trap 'rm -f "$SYMBOLS_FILE"' EXIT

for src_file in $SRC_FILES; do
  [[ ! -f "$src_file" ]] && continue
  grep -oE 'class\s+[A-Z][A-Za-z0-9]+(Engine|Endpoints|Store|Runner|Publisher|Builder|Validator|Registry|AuthorizationFilter|Extensions)\b' "$src_file" 2>/dev/null \
    | sed 's/^class *//' \
    | grep -vE '^I[A-Z]' \
    | grep -v 'Base$' \
    | grep -v 'Abstract' \
    >> "$SYMBOLS_FILE" || true
done

SYMBOLS=$(sort -u "$SYMBOLS_FILE")

# --- Primary routing: lookup each symbol in INDEX.md ---
echo "=== AFFECTED OWNER SPECS ==="
OWNER_SPECS=""
UNCOVERED=""

if [[ -n "$SYMBOLS" ]]; then
  while IFS= read -r sym; do
    [[ -z "$sym" ]] && continue
    match=$(grep -F "\`$sym\`" "$INDEX" 2>/dev/null | head -1 || true)
    if [[ -n "$match" ]]; then
      spec=$(echo "$match" | sed 's/.*\[\(.*\)\.md\].*/\1/')
      echo "  $sym → $spec"
      OWNER_SPECS="$OWNER_SPECS $spec"
    else
      # Not an entry point of any spec: still covered if a spec lists it under classes: (same rule as the commit gate)
      covered=$(grep -lE "^classes:.*[[][^]]*\\b$sym\\b" "$ARCH_DIR"/*/*.md 2>/dev/null | head -1 || true)
      if [[ -n "$covered" ]]; then
        key="${covered#$ARCH_DIR/}"; key="${key%.md}"
        echo "  $sym → $key (via classes:)"
        OWNER_SPECS="$OWNER_SPECS $key"
      else
        echo "  $sym → UNCOVERED"
        UNCOVERED="$UNCOVERED $sym"
      fi
    fi
  done <<< "$SYMBOLS"
fi

# --- Also match by filename against INDEX.md ---
for src_file in $SRC_FILES; do
  basename=$(basename "$src_file" .cs)
  match=$(grep -F "\`$basename\`" "$INDEX" 2>/dev/null | head -1 || true)
  if [[ -n "$match" ]]; then
    spec=$(echo "$match" | sed 's/.*\[\(.*\)\.md\].*/\1/')
    if ! echo "$OWNER_SPECS" | grep -qF "$spec"; then
      echo "  $basename (filename) → $spec"
      OWNER_SPECS="$OWNER_SPECS $spec"
    fi
  fi
done

# --- Secondary: classes: reverse lookup ---
echo ""
echo "=== RELATED VIA CLASSES ==="
CLASSES_SPECS=""

for src_file in $SRC_FILES; do
  basename=$(basename "$src_file" .cs)
  [[ -z "$basename" ]] && continue
  # Search all spec files for this class name in classes: field
  while IFS= read -r spec_file; do
    classes_line=$(awk '/^classes:/' "$spec_file" 2>/dev/null || true)
    if echo "$classes_line" | grep -qF "$basename"; then
      rel="${spec_file#$ARCH_DIR/}"
      key="${rel%.md}"
      if ! echo "$OWNER_SPECS $CLASSES_SPECS" | grep -qF "$key"; then
        echo "  $basename → $key (via classes:)"
        CLASSES_SPECS="$CLASSES_SPECS $key"
      fi
    fi
  done < <(find "$ARCH_DIR" -mindepth 2 -name '*.md' ! -name 'INDEX.md' ! -name 'GRAPH.md' 2>/dev/null)
done

# --- Downstream/prerequisite neighbors of owner specs ---
echo ""
echo "=== DOWNSTREAM NEIGHBORS ==="
for spec_key in $OWNER_SPECS; do
  spec_file="$ARCH_DIR/${spec_key}.md"
  [[ ! -f "$spec_file" ]] && continue
  downstream=$(awk '/^downstream:/' "$spec_file" | sed 's/downstream: *\[//;s/\]//;s/, */\n/g' | sed '/^$/d')
  if [[ -n "$downstream" ]]; then
    while IFS= read -r ds; do
      echo "  $spec_key → $ds"
    done <<< "$downstream"
  fi
done

echo ""
echo "=== PREREQUISITE NEIGHBORS ==="
for spec_key in $OWNER_SPECS; do
  spec_file="$ARCH_DIR/${spec_key}.md"
  [[ ! -f "$spec_file" ]] && continue
  prereqs=$(awk '/^prerequisites:/' "$spec_file" | sed 's/prerequisites: *\[//;s/\]//;s/, */\n/g' | sed '/^$/d')
  if [[ -n "$prereqs" ]]; then
    while IFS= read -r pr; do
      echo "  $spec_key ← $pr"
    done <<< "$prereqs"
  fi
done

# --- Event change detection ---
echo ""
echo "=== EVENT CHANGES ==="
for src_file in $SRC_FILES; do
  [[ ! -f "$src_file" ]] && continue
  # Check staged diff for new event publish/consume
  events=$(git diff --cached -- "$src_file" 2>/dev/null | grep '^+' | grep -oE 'IWorkflowEventPublisher|PublishAsync' || true)
  if [[ -n "$events" ]]; then
    echo "  $src_file — new event publishing detected"
  fi
done

# --- Rename detection ---
echo ""
echo "=== RENAMES ==="
RENAMES=$(echo "$CHANGES" | grep '^R' || true)
if [[ -n "$RENAMES" ]]; then
  while IFS=$'\t' read -r status old_file new_file; do
    old_base=$(basename "$old_file" .cs)
    new_base=$(basename "$new_file" .cs)
    if [[ "$old_base" != "$new_base" ]]; then
      # Check if old symbol is still in any spec
      stale=$(grep -rl "$old_base" "$ARCH_DIR"/*/*.md 2>/dev/null | head -3 || true)
      if [[ -n "$stale" ]]; then
        echo "  STALE: $old_base → $new_base (still referenced in: $stale)"
      else
        echo "  OK: $old_base → $new_base (no stale refs)"
      fi
    fi
  done <<< "$RENAMES"
fi

# --- Uncovered primaries ---
echo ""
echo "=== UNCOVERED PRIMARY SYMBOLS ==="
if [[ -n "$UNCOVERED" ]]; then
  for sym in $UNCOVERED; do
    echo "  ⚠️  $sym — no spec found"
  done
else
  echo "  (none)"
fi

# --- Summary flags ---
echo ""
echo "=== FLAGS ==="
echo "  tests_only=$TESTS_ONLY"
[[ -n "$UNCOVERED" ]] && echo "  has_uncovered=true" || echo "  has_uncovered=false"
[[ -n "$RENAMES" ]] && echo "  has_renames=true" || echo "  has_renames=false"
