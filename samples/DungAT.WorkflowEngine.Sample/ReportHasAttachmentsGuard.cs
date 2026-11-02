using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

public sealed class ReportHasAttachmentsGuard : WorkflowGuard<Report>
{
    protected override Task<GuardResult> EvaluateAsync(Report report, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(report.AttachmentCount > 0
            ? GuardResult.Pass
            : GuardResult.Fail("Attach at least one receipt before submitting."));
}
