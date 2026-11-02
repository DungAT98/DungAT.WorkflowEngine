using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Attached to the APPROVED state, so it runs however a report got approved (by a manager or automatically).</summary>
public sealed class ArchiveApprovedReportPostAction(ILogger<ArchiveApprovedReportPostAction> logger) : WorkflowPostAction<Report>
{
    protected override Task ExecuteAsync(Report report, WorkflowPostContext context, CancellationToken ct)
    {
        logger.LogInformation("Archived report '{Title}' ({Amount:N0})", report.Title, report.Amount);
        return Task.CompletedTask;
    }
}
