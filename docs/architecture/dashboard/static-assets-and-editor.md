---
domain: dashboard
spec: static-assets-and-editor
description: "Embedded UI assets and the definition editor's normalization, validation and cache"
entry_points: [DashboardDefinitionValidator, StaticAssetHandler]
commands: []
entities: [WorkflowDefinition]
events_in: []
events_out: []
classes: [DefinitionCache, DefinitionEndpoints, WorkflowDefinitionValidator, WorkflowComponentValidator]
tests: [tests/DungAT.WorkflowEngine.Dashboard.Tests/ReviewRegressionTests.cs, tests/DungAT.WorkflowEngine.Dashboard.Tests/StaticAssetTests.cs, tests/DungAT.WorkflowEngine.Dashboard.Tests/DefinitionApiTests.cs]
prerequisites: [dashboard/authorization]
downstream: []
see_also: [definitions/builder-and-validation, dashboard/api-endpoints]
---

## Flow

1. `StaticAssetHandler.Handle` serves the UI from files embedded in the assembly; no external requests are made.
2. The editor posts a definition to `/definitions/validate` for live validation and to `/definitions` to publish a new version.
3. `DashboardDefinitionValidator.Normalize` cleans the input, and `Validate` adds dashboard checks on top of the structural and component validators.
4. `DefinitionCache` caches definition versions for the instance views.

## Gotchas

- Definitions defined in code are read-only in the editor, because they are republished at every startup.
- Asset requests have edge cases (bad paths, methods); `StaticAssetTests` pins them.
- Mounting at `/` is supported: the mount path is trimmed so the base path is `/` and `apiBase` is `/api` (not `//api`, which a browser reads as another host).
