using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;

namespace DungAT.WorkflowEngine.Sample;

/// <summary>Typed guard: it receives the loaded <see cref="Report"/> row, no casting or JSON needed.</summary>
public sealed class ReportWithinBudgetGuard : WorkflowGuard<Report>
{
    public const decimal Limit = 7500m;

    protected override Task<GuardResult> EvaluateAsync(Report report, WorkflowContext context, CancellationToken ct) =>
        Task.FromResult(report.Amount <= Limit
            ? GuardResult.Pass
            : GuardResult.Fail($"{report.Amount:N0} is over the {Limit:N0} limit. Ask a manager to raise the budget."));
}
