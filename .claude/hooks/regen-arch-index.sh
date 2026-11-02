#!/bin/bash
fp=$(python3 -c "import sys,json; print(json.load(sys.stdin)['tool_input']['file_path'])" 2>/dev/null)

# Only trigger for spec files in docs/architecture subdirectories (not INDEX/GRAPH).
# Accept both repo-relative paths (docs/architecture/...) and absolute paths (.../docs/architecture/...).
if ! echo "$fp" | grep -qE '(^|.*/)docs/architecture/[^/]+/[^/]+\.md$'; then
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
