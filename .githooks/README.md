# Git hooks (`.githooks/`)

Activated automatically on the first local `dotnet build` (`Directory.Build.targets`); manual fallback:

```bash
git config core.hooksPath .githooks
```

| Hook | Purpose |
|------|---------|
| `commit-msg` | Spec-impact gate (local mirror of `.github/workflows/spec-impact.yml`). Runs `check_spec_impact.sh`; warns, never blocks. |

## Spec-impact gate

Production code under `src/` ships with a spec change in `docs/architecture/`. `tests/` and `samples/` are outside `src/` and exempt.

| Rule | Waiver |
|------|--------|
| A new `*Engine` / `*Endpoints` / `*Store` / `*Runner` / `*Publisher` / `*Builder` / `*Validator` / `*Registry` / `*AuthorizationFilter` / `*Extensions` file (not `I*`, not `*Base`) is listed in some spec's `entry_points` / `commands` / `classes` | none |
| `docs/architecture/generate-index.sh` passes and the committed `INDEX.md` / `GRAPH.md` match its output | none |
| A `docs/architecture/<domain>/*.md` spec changed | PR label `no-spec-change`, counted only if someone other than the PR author added it |

Locally the hook checks the branch diff plus the staged index and only **warns**: code and spec may land in separate commits. CI fails the PR.
With no base to compare against (first commit, no remote), every staged file counts as added.

```bash
bash .githooks/check_spec_impact.sh --base origin/main --staged   # what commit-msg runs
bash .githooks/check_spec_impact.sh --base origin/main            # CI equivalent
```

> Mark the `Spec impact guard` job as required in the `main` branch ruleset, otherwise it only surfaces a red check.
