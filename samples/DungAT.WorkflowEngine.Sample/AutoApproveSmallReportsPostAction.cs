using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Reports under the threshold need no human: right after SUBMIT the engine runs APPROVE as the system user.</summary>
public sealed class AutoApproveSmallReportsPostAction : WorkflowPostAction<Report>
{
    public const decimal Threshold = 500m;

    protected override async Task ExecuteAsync(Report report, WorkflowPostContext context, CancellationToken ct)
    {
        if (report.Amount < Threshold)
            await context.FollowUpAsync("APPROVE", $"Auto-approved: under {Threshold:N0}.");
    }
}
