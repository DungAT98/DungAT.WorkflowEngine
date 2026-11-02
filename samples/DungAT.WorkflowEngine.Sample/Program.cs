using DungAT.WorkflowEngine;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Dashboard;
using DungAT.WorkflowEngine.EntityFrameworkCore;
using DungAT.WorkflowEngine.Sample;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// An in-memory SQLite database that lives as long as the process; swap for your real provider.
var connection = new SqliteConnection("DataSource=:memory:");
connection.Open();
builder.Services.AddDbContext<SampleDbContext>(o => o.UseSqlite(connection));

builder.Services.AddWorkflowEngine(wf => wf
    // Link the Reports table: the engine loads the row by id for guards/effects and keeps Report.Status in step.
    .AddEntity<Report, SampleDbContext>(e => e.SyncState((report, state) => report.Status = Enum.Parse<ReportStatus>(state, ignoreCase: true)))
    .AddDefinition(SampleWorkflows.ReportApproval())
    .AddDefinition(SampleWorkflows.LeaveRequest())
    .UseEntityFrameworkCore<SampleDbContext>());
builder.Services.AddWorkflowDashboard();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<WorkflowDefinitionPublisher>().PublishAllAsync();
    await SampleSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<SampleDbContext>(), scope.ServiceProvider.GetRequiredService<IWorkflowEngine>());
}

app.MapGet("/", () => Results.Redirect("/workflow"));

// The business side of the link: a plain endpoint over the Reports table. Status is maintained by the workflow engine.
app.MapGet("/reports", (SampleDbContext db) => db.Reports.AsNoTracking().OrderBy(r => r.Id).ToList());
app.MapGet("/reports/{id:int}", async (int id, SampleDbContext db) =>
    await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id) is { } report ? Results.Ok(report) : Results.NotFound());

app.MapWorkflowDashboard("/workflow", o =>
{
    o.Title = "Acme Approvals";
    o.EntityLink = (type, id) => type == "Report" ? $"/reports/{id}" : null; // shown as "Open Report" on the instance page
    // Local requests only by default. In a real app add o.Authorization filters or .RequireAuthorization().
    // The demo user can run every command.
    o.UserResolver = _ => new WorkflowUser("demo.admin", ["Employee", "Manager", "HR"]);
});

app.Run();
