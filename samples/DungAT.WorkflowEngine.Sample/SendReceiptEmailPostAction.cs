using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>
/// Deliberately flaky demo: fails for every fifth report. The transition stays done; the dashboard shows
/// a red chip on the history entry and a warning when the command is run.
/// </summary>
public sealed class SendReceiptEmailPostAction : WorkflowPostAction<Report>
{
    protected override Task ExecuteAsync(Report report, WorkflowPostContext context, CancellationToken ct) =>
        report.Id % 5 == 0
            ? throw new TimeoutException("The SMTP server timed out.")
            : Task.CompletedTask;
}
