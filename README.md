# DungAT.WorkflowEngine

A lightweight, persistence-agnostic workflow (state machine) engine for .NET, with an optional
Hangfire-style web dashboard.

| Package | What it is |
|---|---|
| `DungAT.WorkflowEngine.Abstractions` | Models, interfaces and result types (no dependencies) |
| `DungAT.WorkflowEngine` | Engine, fluent builder, validation, in-memory stores, DI |
| `DungAT.WorkflowEngine.EntityFrameworkCore` | EF Core stores (any relational provider) |
| `DungAT.WorkflowEngine.Dashboard` | Embedded web UI: monitor, run commands, edit workflows |

## Quick start

```csharp
services.AddWorkflowEngine(wf => wf
    .AddDefinition(WorkflowDefinitionBuilder
        .Define("DOC_APPROVAL", "Document approval")
        .State("DRAFT", WorkflowStateType.Initial)
        .State("SUBMITTED")
        .State("APPROVED", WorkflowStateType.Final)
        .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
        .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager"))
    .UseEntityFrameworkCore<AppDbContext>());

// In your DbContext:  modelBuilder.ApplyWorkflowEngineConfiguration();
// At startup:         await scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();

var start = await engine.StartAsync("DOC_APPROVAL", "Document", "42", user);
var result = await engine.ExecuteAsync(start.Value!.Id, "SUBMIT", user);
if (!result.Succeeded) { /* result.Status: NotAllowed, GuardFailed, ConcurrencyConflict, ... */ }
```

## Linking to your own tables

An instance stores `EntityType` + `EntityId`. Register the entity and the engine loads the real row for
guards, effects and permission checks, and can keep its status column in step with the workflow:

```csharp
services.AddWorkflowEngine(wf => wf
    .AddEntity<Report, AppDbContext>(e => e.SyncState((r, state) => r.Status = Enum.Parse<ReportStatus>(state, true)))
    .AddDefinition(WorkflowDefinitionBuilder.Define("REPORT_APPROVAL", "Report approval").ForEntity("Report")
        .State("DRAFT", WorkflowStateType.Initial).State("SUBMITTED").State("APPROVED", WorkflowStateType.Final)
        .On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED").Guard<ReportWithinBudgetGuard>()
        .On("SUBMITTED").Command("APPROVE").GoTo("APPROVED").AllowRoles("Manager"))
    .UseEntityFrameworkCore<AppDbContext>());

await engine.StartAsync<Report>("REPORT_APPROVAL", report, user);   // id comes from the entity's primary key
```

```csharp
// Typed guard: receives the Report row, no cast needed.
public sealed class ReportWithinBudgetGuard : WorkflowGuard<Report>
{
    protected override Task<GuardResult> EvaluateAsync(Report report, WorkflowContext ctx, CancellationToken ct) =>
        Task.FromResult(report.Amount <= 5000 ? GuardResult.Pass : GuardResult.Fail("Over budget."));
}

// Or keep IWorkflowGuard and restrict it with an attribute (several entities, base classes and interfaces work too).
[ForEntity(typeof(Report), typeof(Invoice))]
public sealed class HasAttachmentsGuard : IWorkflowGuard { /* ctx.GetEntity<IHasAttachments>() */ }
```

- Guards and effects **without** `[ForEntity]` are generic and work with any workflow.
- A guard used with the wrong entity is rejected when definitions are published at startup (and by the dashboard editor),
  and fails loudly at runtime if it somehow gets through.
- With EF Core the entity is loaded through the same scoped `DbContext`, so the status column is written in the
  same `SaveChanges` as the instance and history rows. If another user wins the race, nothing is saved.
- Entities that implement `IWorkflowStateful` get `WorkflowState` written automatically; use `SyncState(...)` for your own column (pass a read function too, `SyncState(write, read)`, so a failed start can put the old value back).
- One workflow instance per entity.

## After the transition (post actions)

Guards run **before** a transition and can stop it. Effects run **during** it, before the save, and a failure aborts it.
Post actions run **after** it was saved: send the email, call another system, auto-approve. They cannot undo the transition.

```csharp
.On("DRAFT").Command("SUBMIT").GoTo("SUBMITTED")
    .PostAction<NotifyOwner>().PostAction<AutoApproveSmallReports>()
.OnEnter("APPROVED").PostAction<ArchiveReport>()      // runs however an instance gets into APPROVED (and on start, for the initial state)

public sealed class AutoApproveSmallReports : WorkflowPostAction<Report>
{
    protected override async Task ExecuteAsync(Report report, WorkflowPostContext ctx, CancellationToken ct)
    {
        if (report.Amount < 500) await ctx.FollowUpAsync("APPROVE", "Auto-approved.");   // runs as WorkflowUser.System
    }
}
```

- The entity already shows the new state; `ctx.Instance` is the saved instance.
- If the event publisher throws after the save, that is reported in `result.Warnings` too and the post actions still run.
- If an action throws, the transition stays, the remaining actions still run, the failure is logged and stored
  (`engine.GetPostActionRunsAsync`), and `result.Warnings` says what failed. The dashboard shows ✓/✗ chips per history entry and a warning toast.
- `FollowUpAsync` runs another command. It passes **role** checks (as `WorkflowUser.System`, the engine itself) but still has to satisfy
  guards. Chains are limited by `wf.Configure(o => o.MaxChainDepth = 5)`; past the limit you get `ChainDepthExceeded`.
- Actions run in-process, in order, right after the commit. If the app crashes in between, they are lost; for guaranteed delivery
  use an outbox in an `IWorkflowEventPublisher`. Custom permission evaluators should treat `user.IsSystem` as allowed.
- EF Core: post action runs live in a new table `WorkflowPostActionRuns` and history entries have a new `Sequence` column
  (it keeps history ordered when timestamps tie): add a migration when upgrading.

### Effects vs post actions
An effect runs **before** the save, so it has already happened if the save then loses a concurrency race or fails, and a retry runs it again.
Keep effects idempotent (cheap, repeatable work such as filling a field); put anything that must happen exactly once, after the change is final
(emails, calls to other systems), in a post action.

## Dashboard

```bash
dotnet add package DungAT.WorkflowEngine.Dashboard
```

```csharp
builder.Services.AddWorkflowEngine(...);
builder.Services.AddWorkflowDashboard();

app.MapWorkflowDashboard("/workflow", o =>
{
    o.Title = "Approvals";
    o.ReadOnly = false;                                   // true hides every action that changes data
    o.UserResolver = ctx => new WorkflowUser(ctx.User.Identity!.Name!, roles);
});
```

The dashboard is served from files embedded in the assembly and makes no external requests.

- **Overview**: totals, instances per state, live activity feed.
- **Workflows**: browse definitions and versions, state diagrams, what changed between versions.
- **Instances**: search and filter, full history, run the commands the current user is allowed to.
- **Editor**: create workflows and publish new versions with live validation and a diagram preview.
  Workflows defined in code are read-only here, because they are republished at every startup.

**Security.** By default only requests from the local machine are allowed. Requests that carry proxy headers
(`X-Forwarded-For`, `Forwarded`, `X-Real-IP`) are refused: behind a reverse proxy on the same machine every connection
looks local, so replace the default filter before putting the dashboard behind a proxy. Before exposing the
dashboard, set `o.Authorization` (e.g. `new DelegateAuthorizationFilter(c => c.User.IsInRole("Admin"))`)
and/or chain `.RequireAuthorization("policy")` on the returned builder. Commands run as the user
returned by `UserResolver`, so role and guard checks still apply. State-changing calls also require an
`X-Workflow-Dashboard` header, which blocks cross-site request forgery.

On SQLite use `ApplyWorkflowEngineConfiguration(storeDatesAsBinary: true)`: SQLite cannot sort or
compare `DateTimeOffset`, which the dashboard's queries need.

A runnable demo lives in `samples/DungAT.WorkflowEngine.Sample`:
`dotnet run --project samples/DungAT.WorkflowEngine.Sample` and open <http://localhost:5057/workflow>.
