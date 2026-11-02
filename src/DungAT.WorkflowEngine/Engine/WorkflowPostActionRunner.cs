using System.Diagnostics;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace DungAT.WorkflowEngine.Engine;

/// <summary>Runs post actions one after another. A failing action is recorded and never stops the others.</summary>
internal sealed class WorkflowPostActionRunner(
    IEnumerable<IWorkflowPostAction> actions, TimeProvider time, ILogger<WorkflowPostActionRunner>? logger = null)
{
    public async Task<IReadOnlyList<WorkflowPostActionRun>> RunAsync(IReadOnlyList<string> names, WorkflowPostContext context)
    {
        var runs = new List<WorkflowPostActionRun>(names.Count);
        foreach (var (name, sequence) in names.Select((n, i) => (n, i)))
        {
            var started = time.GetUtcNow();
            var clock = Stopwatch.StartNew();
            string? error = null;
            try
            {
                // The transition is already saved, so a bad registration must not throw; it is recorded like any other failure.
                var action = actions.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                             ?? throw new InvalidOperationException($"No post action is registered with the name '{name}'.");
                EntityCompatibility.Ensure("Post action", name, action.EntityTypes, context.Entity, context.Instance.EntityType);
                // CancellationToken.None: a closed browser tab must not skip the email after the data is saved.
                await action.ExecuteAsync(context, CancellationToken.None);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                logger?.LogError(ex, "Post action {Action} failed for workflow instance {Instance} ({From} -> {To})",
                    name, context.Instance.Id, context.FromStateCode, context.ToStateCode);
            }

            runs.Add(new WorkflowPostActionRun
            {
                Id = Guid.NewGuid(), InstanceId = context.Instance.Id, HistoryId = context.History.Id,
                Sequence = sequence, ActionName = name, Succeeded = error is null, Error = error is { Length: > 1900 } ? error[..1900] : error,
                StartedAt = started, DurationMs = (int)clock.ElapsedMilliseconds,
            });
        }
        return runs;
    }
}
