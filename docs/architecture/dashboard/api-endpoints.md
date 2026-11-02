---
domain: dashboard
spec: api-endpoints
description: "Dashboard JSON API and hosting: routes, endpoint filter, CSRF header, read-only mode"
entry_points: [InstanceEndpoints, DefinitionEndpoints, StatsEndpoints, MetaEndpoints, WorkflowDashboardEndpointRouteBuilderExtensions, WorkflowDashboardServiceCollectionExtensions]
commands: []
entities: [WorkflowInstance, WorkflowDefinition]
events_in: []
events_out: []
classes: [DashboardApi, DashboardEndpointFilter, WorkflowDashboardOptions, InstanceMapper, ApiResults]
tests: [tests/DungAT.WorkflowEngine.Dashboard.Tests/ReviewRegressionTests.cs, tests/DungAT.WorkflowEngine.Dashboard.Tests/ApiTests.cs, tests/DungAT.WorkflowEngine.Dashboard.Tests/DefinitionApiTests.cs]
prerequisites: [dashboard/authorization]
downstream: [engine/start-instance, engine/execute-command]
see_also: [dashboard/static-assets-and-editor]
---

## Flow

1. `AddWorkflowDashboard()` registers services; `MapWorkflowDashboard(path, options)` maps the API and static UI under the path.
2. Routes under `<path>/api`: GET `/instances`, `/instances/{id}`, `/definitions`, `/definitions/{code}`, `/definitions/{code}/versions`, `/stats`, `/activity`, `/meta`; POST `/instances`, `/instances/{id}/commands/{command}`, `/definitions/validate`, `/definitions`.
3. `DashboardEndpointFilter` wraps every route: security headers, then each authorization filter (`401`), then for non-GET requests the `X-Workflow-Dashboard` header (`403`) and read-only mode (`403`).
4. The command body is optional, but if one is sent it must be `application/json`; any other content type returns `415` instead of silently dropping the comment.
5. Commands run as the user returned by `options.UserResolver`, so role and guard checks apply. Engine results map to DTOs; post action warnings reach the UI as a toast.

## Gotchas

- `ReadOnly = true` blocks every state-changing call, including definition publishing.
- The header check is the CSRF defence: a custom header cannot be sent cross-site without a CORS preflight.
- Instance search relies on date comparison; see the SQLite note in persistence/ef-core-stores.
