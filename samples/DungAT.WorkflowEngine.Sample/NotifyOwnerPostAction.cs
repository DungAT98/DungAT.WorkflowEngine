using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Runs after the transition is saved, so the owner is only told about changes that really happened.</summary>
public sealed class NotifyOwnerPostAction(ILogger<NotifyOwnerPostAction> logger) : WorkflowPostAction<Report>
{
    protected override Task ExecuteAsync(Report report, WorkflowPostContext context, CancellationToken ct)
    {
        logger.LogInformation("Email to {Owner}: '{Title}' is now {State}", report.OwnerId, report.Title, context.ToStateCode);
        return Task.CompletedTask;
    }
}
