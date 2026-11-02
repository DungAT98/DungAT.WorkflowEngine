#!/usr/bin/env bash
# sym.sh — compact symbol lookup against repo-root .tags
# Usage:
#   sym.sh <regex>                     — anchored at column 1 (symbol name)
#   sym.sh <regex> --class=<substring> — further filter by owning class (literal substring match
#                                        against the ctags `class:` field; safe with dots in FQNs)
# Examples:
#   sym.sh PolicyService
#   sym.sh '^AddPolicyMemberReinstatement'
#   sym.sh Build --class=PolicyServiceFactory
#   sym.sh Build --class=CoverGo.Policies.Domain
# Output: one line per hit — "name\trelpath:line\tkind[  class:Owner]" (capped at 20)
# Side-effect: appends a JSONL telemetry line to .claude/logs/nav-metrics.jsonl
set -euo pipefail

pat=""
class_filter=""
for arg in "$@"; do
  case "$arg" in
    --class=*) class_filter="${arg#--class=}" ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *)         pat="$arg" ;;
  esac
done

[ -n "$pat" ] || { echo "sym.sh: symbol regex required (see --help)" >&2; exit 2; }

root="$(git rev-parse --show-toplevel 2>/dev/null || echo "$PWD")"
tags="$root/.tags"
log_dir="$root/.claude/logs"
wc_tmp="$root/.tmp.sym.wc.$$"

[ -f "$tags" ] || { echo "sym.sh: no .tags at $tags — run init-workflowengine-navigate" >&2; exit 1; }

# A git worktree carries no .tags of its own (it is a gitignored build artifact), so it is normally a
# symlink to the primary checkout's index. Paths inside that index are absolute against whichever
# checkout generated it — strip THAT root, not $root, or every hit in a worktree comes back pointing
# at the other tree's copy of the file.
tags_real="$(readlink -f "$tags" 2>/dev/null || echo "$tags")"
tags_root="$(dirname "$tags_real")/"

# Single streaming pipeline: grep → awk (formats + applies literal class filter) → head → tee.
# Class filter uses awk's index() against the ctags `class:` field, so substring matches are
# literal — dots in fully-qualified names ('CoverGo.Policies.Domain') do not regex-match.
grep -E "^${pat}" "$tags" 2>/dev/null \
  | awk -F'\t' -v root="$tags_root" -v cf="$class_filter" '
      NF >= 2 {
        kind=""; cls=""; line=""
        for (i=4; i<=NF; i++) {
          if ($i ~ /^kind:/)       kind=substr($i,6)
          else if ($i ~ /^class:/) cls=substr($i,7)
          else if ($i ~ /^line:/)  line=substr($i,6)
        }
        if (cf != "" && index(cls, cf) == 0) next
        p = $2; sub(root, "", p)
        suffix = cls ? ("  class:" cls) : ""
        printf "%s\t%s:%s\t%s%s\n", $1, p, line, kind, suffix
      }' \
  | head -20 \
  | tee >(wc -lc > "$wc_tmp" 2>/dev/null) \
  || true

# --- Telemetry ------------------------------------------------------------
# One JSONL line per invocation → .claude/logs/nav-metrics.jsonl
# Fields: ts, branch, pattern, class_filter, hits (capped), bytes
ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
branch="$(git -C "$root" rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"
if [ -s "$wc_tmp" ]; then
  read -r hits bytes < "$wc_tmp"
else
  hits=0; bytes=0
fi
rm -f "$wc_tmp"

if mkdir -p "$log_dir" 2>/dev/null; then
  esc() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }
  printf '{"ts":"%s","branch":"%s","pattern":"%s","class":"%s","hits":%s,"bytes":%s}\n' \
    "$ts" "$(esc "$branch")" "$(esc "$pat")" "$(esc "$class_filter")" "${hits:-0}" "${bytes:-0}" \
    >> "$log_dir/nav-metrics.jsonl" 2>/dev/null || true
fi
