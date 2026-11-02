#!/usr/bin/env bash
# Spec-impact gate. Production src/ changes ship with a docs/architecture spec change.
#   CI:    check_spec_impact.sh --base origin/main [--waived]
#   Local: check_spec_impact.sh --base origin/main --staged   (branch commits + staged index)
#
# Rules (test projects — *.Tests*, *.Testing — are exempt; samples/ and tests/ are outside src/):
#   1. New *Engine / *Endpoints / *Store / *Runner / *Publisher / *Builder / *Validator / *Registry /
#      *AuthorizationFilter / *Extensions files are listed in some spec's entry_points/commands/classes. No waiver.
#   2. generate-index.sh passes and the committed INDEX.md/GRAPH.md are fresh.                            No waiver.
#   3. A docs/architecture/<domain>/*.md spec changed.  --waived (PR label `no-spec-change`).
#   --staged (commit-msg hook) only warns: the PR may still fix it in a later commit. CI fails.
#
# No base to compare with (first commit, no remote, unrelated history): every staged/tracked file counts as added.
set -euo pipefail

base="" staged=0 waived=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --base) base="$2"; shift 2 ;;
    --staged) staged=1; shift ;;
    --waived) waived=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[[ -z "$base" ]] && { echo "usage: $0 --base <ref> [--staged] [--waived]" >&2; exit 2; }

ARCH_DIR="docs/architecture"
TEST_PATH='(^|/)[^/]*\.Test(s|ing)(\.[^/]*)?/'
PRIMARY='(Engine|Endpoints|Store|Runner|Publisher|Builder|Validator|Registry|AuthorizationFilter|Extensions)$'
EMPTY_TREE=4b825dc642cb6eb9a060e54bf8d69288fbee4904

mb=$(git merge-base "$base" HEAD 2>/dev/null || true)
if [[ -z "$mb" ]]; then
  if (( ! staged )) && ! git rev-parse -q --verify HEAD >/dev/null; then
    echo "spec-impact: no commits yet and nothing staged to check."
    exit 0
  fi
  mb=$EMPTY_TREE
fi
if (( staged )); then
  changes=$(git diff --cached --name-status --no-renames "$mb")
  # Commits already on the branch count too (a no-op when there is no HEAD or the base is the empty tree).
  if [[ "$mb" != "$EMPTY_TREE" ]]; then
    changes=$(printf '%s\n%s' "$(git diff --name-status --no-renames "$mb" HEAD)" "$changes" | sed '/^$/d' | sort -u)
  fi
else
  changes=$(git diff --name-status --no-renames "$mb" HEAD)
fi

prod=$(awk -F'\t' '$2 ~ /^src\// { print $1 "\t" $2 }' <<<"$changes" | grep -vE "$TEST_PATH" || true)
arch=$(awk -F'\t' -v d="$ARCH_DIR/" 'index($2, d) == 1' <<<"$changes")
if [[ -z "$prod" && -z "$arch" ]]; then
  echo "spec-impact: no production src/ or $ARCH_DIR changes."
  exit 0
fi

errors=()

# Index-side snapshot of docs/architecture, so the check never touches the working tree
snap=$(mktemp -d)
trap 'rm -rf "$snap"' EXIT
git ls-files -z -- "$ARCH_DIR" | xargs -0 git checkout-index --prefix="$snap/" -- 2>/dev/null || true

# Rule 1 — new primary entry points are owned by a spec
frontmatter_symbols=""
if [[ -d "$snap/$ARCH_DIR" ]]; then
  frontmatter_symbols=$(find "$snap/$ARCH_DIR" -mindepth 2 -name '*.md' -exec \
    awk 'FNR == 1 { fm = 0 } /^---$/ { if (++fm == 2) nextfile; next } fm == 1 && /^(entry_points|commands|classes):/' {} + || true)
fi
while IFS=$'\t' read -r status path; do
  [[ "$status" == A && "$path" == *.cs ]] || continue
  name=$(basename "$path" .cs)
  [[ "$name" =~ $PRIMARY && ! "$name" =~ ^I[A-Z] && "$name" != *Base ]] || continue
  grep -qw -- "$name" <<<"$frontmatter_symbols" ||
    errors+=("new entry point \`$name\` ($path) is in no spec — add it to the owning spec's entry_points/commands/classes")
done <<<"$prod"

# Rule 2 — index generator passes and committed outputs are fresh
if [[ ! -f "$snap/$ARCH_DIR/generate-index.sh" ]]; then
  errors+=("$ARCH_DIR/generate-index.sh is not tracked — stage it")
else
  for f in INDEX.md GRAPH.md; do cp "$snap/$ARCH_DIR/$f" "$snap/$f.committed" 2>/dev/null || : > "$snap/$f.committed"; done
  if ! gen_out=$(cd "$snap" && bash "$ARCH_DIR/generate-index.sh" 2>&1); then
    details=$(grep -E 'WARNING|DUPLICATE|CYCLE' <<<"$gen_out" || true)
    errors+=("generate-index.sh failed:"$'\n'"$(sed 's/^ */      /' <<<"${details:-$gen_out}")")
  else
    for f in INDEX.md GRAPH.md; do
      cmp -s "$snap/$f.committed" "$snap/$ARCH_DIR/$f" ||
        errors+=("$ARCH_DIR/$f is stale — run: bash $ARCH_DIR/generate-index.sh, then stage it")
    done
  fi
fi

# Rule 3 — a spec changed alongside production code
spec_changed=$(awk -F'\t' -v d="$ARCH_DIR/" 'index($2, d) == 1 && $2 ~ /\.md$/ {
  rel = substr($2, length(d) + 1); if (rel ~ /\// && rel !~ /(^|\/)(INDEX|GRAPH)\.md$/) print $2 }' <<<"$changes")
if [[ -n "$prod" && -z "$spec_changed" ]] && (( ! waived )); then
  errors+=("production src/ changed but no $ARCH_DIR/<domain>/*.md spec changed (run the spec-lifecycle skill; if no spec change is genuinely needed, the reviewer applies the PR label \`no-spec-change\`)")
fi

if (( ${#errors[@]} )); then
  echo ""
  echo "══════════════════════════════════════════════════════════"
  if (( staged )); then
    echo "  ⚠️  spec-impact: fix before merge — CI fails the PR on these"
  else
    echo "  ❌  spec-impact gate failed"
  fi
  echo "══════════════════════════════════════════════════════════"
  for e in "${errors[@]}"; do printf '  • %s\n' "$e"; done
  echo ""
  (( staged )) && exit 0
  exit 1
fi
[[ -n "$prod" ]] && (( waived )) && [[ -z "$spec_changed" ]] && echo "spec-impact: no spec changed — waived by PR label \`no-spec-change\`."
echo "spec-impact: OK"
