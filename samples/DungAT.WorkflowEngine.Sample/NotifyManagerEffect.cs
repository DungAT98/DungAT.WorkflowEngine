using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Stand-in for sending an email; effects run before the new state is saved.</summary>
public sealed class NotifyManagerEffect(ILogger<NotifyManagerEffect> logger) : WorkflowEffect<Report>
{
    protected override Task ExecuteAsync(Report report, WorkflowContext context, CancellationToken ct)
    {
        logger.LogInformation("Notify manager: report '{Title}' ({Amount:N0}) -> {State}", report.Title, report.Amount, context.Transition.ToStateCode);
        return Task.CompletedTask;
    }
}
