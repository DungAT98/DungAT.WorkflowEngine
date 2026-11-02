---
domain: dashboard
spec: authorization
description: "Dashboard authorization filters: local-only default, proxy-header refusal, delegate filter"
entry_points: [LocalRequestsOnlyAuthorizationFilter, DelegateAuthorizationFilter]
commands: []
entities: []
events_in: []
events_out: []
classes: [IWorkflowDashboardAuthorizationFilter, DashboardEndpointFilter, WorkflowDashboardOptions]
tests: [tests/DungAT.WorkflowEngine.Dashboard.Tests/AuthorizationFilterTests.cs]
prerequisites: []
downstream: []
see_also: [dashboard/api-endpoints]
---

## Flow

1. `options.Authorization` is a list of `IWorkflowDashboardAuthorizationFilter`; all must return true.
2. Default `LocalRequestsOnlyAuthorizationFilter`: refuse if `Forwarded`, `X-Forwarded-For` or `X-Real-IP` is present; allow when there is no remote address (in-process test servers, unix sockets); otherwise allow loopback or remote == local address.
3. `DelegateAuthorizationFilter(c => ...)` wraps a predicate, e.g. an admin-role check.

## Gotchas

- Behind a reverse proxy on the same machine every connection looks local, hence the proxy-header refusal. Replace the default filter before putting the dashboard behind a proxy.
- Chain `.RequireAuthorization("policy")` on the returned endpoint builder for ASP.NET Core policies in addition to these filters.
