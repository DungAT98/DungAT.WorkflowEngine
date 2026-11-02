using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Creates demo data in assorted states so the dashboard has something to show.</summary>
public static class SampleSeeder
{
    private static readonly WorkflowUser Employee = new("sam.employee", ["Employee"]);
    private static readonly WorkflowUser Manager = new("maria.manager", ["Employee", "Manager"]);
    private static readonly WorkflowUser Hr = new("harry.hr", ["HR"]);

    public static async Task SeedAsync(SampleDbContext db, IWorkflowEngine engine)
    {
        var random = new Random(42);

        // Business rows first: they exist (and are editable) independently of any workflow.
        var reports = Enumerable.Range(1, 24).Select(i => new Report
        {
            Title = $"Expense report {1000 + i}",
            Amount = random.Next(4) == 0 ? random.Next(1, 5) * 100 : random.Next(5, 100) * 100, // a quarter are small enough to auto-approve
            AttachmentCount = random.Next(0, 4),
            OwnerId = Employee.Id,
        }).ToList();
        db.Reports.AddRange(reports);
        await db.SaveChangesAsync();

        foreach (var report in reports)
        {
            var id = (await engine.StartAsync<Report>("REPORT_APPROVAL", report, Employee)).Value!.Id;
            switch (random.Next(5))
            {
                case 0: break; // stays in draft
                case 1: await engine.ExecuteAsync(id, "SUBMIT", Employee); break;
                case 2:
                    if ((await engine.ExecuteAsync(id, "SUBMIT", Employee)).Succeeded)
                        await engine.ExecuteAsync(id, "APPROVE", Manager);
                    break;
                case 3:
                    if ((await engine.ExecuteAsync(id, "SUBMIT", Employee)).Succeeded)
                        await engine.ExecuteAsync(id, "REJECT", Manager, "Missing the signed cover page.");
                    break;
                default:
                    if ((await engine.ExecuteAsync(id, "SUBMIT", Employee)).Succeeded)
                    {
                        await engine.ExecuteAsync(id, "REJECT", Manager, "Figures do not add up.");
                        await engine.ExecuteAsync(id, "REVISE", Employee);
                    }
                    break;
            }
        }

        for (var i = 1; i <= 12; i++)
        {
            var id = (await engine.StartAsync("LEAVE_REQUEST", "LeaveRequest", $"L-{200 + i}", Employee)).Value!.Id;
            var depth = random.Next(5);
            if (depth >= 1) await engine.ExecuteAsync(id, "REQUEST", Employee);
            if (depth >= 2) await engine.ExecuteAsync(id, "ENDORSE", Manager);
            if (depth == 3) await engine.ExecuteAsync(id, "GRANT", Hr);
            if (depth == 4) await engine.ExecuteAsync(id, "DECLINE", Hr, "Blackout period.");
        }
    }
}
