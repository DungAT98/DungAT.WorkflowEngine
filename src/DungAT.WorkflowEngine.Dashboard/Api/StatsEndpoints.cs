using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class StatsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/stats", async (
            IWorkflowMonitoringStore monitoring, IWorkflowDefinitionStore definitions, CancellationToken ct) =>
        {
            var stats = await monitoring.GetStatsAsync(ct);
            var latest = await definitions.ListAsync(ct);
            var cache = new DefinitionCache(definitions);

            var finished = 0;
            var countsByCode = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var typeByState = new Dictionary<(string, string), WorkflowStateType>();
            foreach (var c in stats.StateCounts)
            {
                var state = (await cache.GetAsync(c.DefinitionCode, c.DefinitionVersion, ct))?.FindState(c.StateCode);
                if (state?.Type == WorkflowStateType.Final) finished += c.Count;
                typeByState[(c.DefinitionCode.ToUpperInvariant(), c.StateCode.ToUpperInvariant())] = state?.Type ?? WorkflowStateType.Intermediate;

                if (!countsByCode.TryGetValue(c.DefinitionCode, out var byState))
                    countsByCode[c.DefinitionCode] = byState = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                byState[c.StateCode] = byState.GetValueOrDefault(c.StateCode) + c.Count;
            }

            var perDefinition = latest.Select(d =>
            {
                var counts = countsByCode.GetValueOrDefault(d.Code) ?? [];
                var states = d.States.Select(s => new StateStatsDto(s.Code, s.Name, s.Type.ToString(), counts.GetValueOrDefault(s.Code))).ToList();
                // States that only exist in older versions still hold instances; show them rather than hide them.
                states.AddRange(counts.Where(kv => d.FindState(kv.Key) is null)
                    .Select(kv => new StateStatsDto(kv.Key, kv.Key, typeByState[(d.Code.ToUpperInvariant(), kv.Key.ToUpperInvariant())].ToString(), kv.Value)));
                return new DefinitionStatsDto(d.Code, d.Name, d.Version, states.Sum(s => s.Count), states);
            }).ToList();

            return ApiResults.Ok(new DashboardStats(
                stats.TotalInstances, stats.TotalInstances - finished, finished,
                stats.CommandsLast24Hours, stats.CommandsLast7Days, perDefinition));
        });

        api.MapGet("/activity", async (HttpContext context, IWorkflowMonitoringStore monitoring, CancellationToken ct) =>
        {
            var take = int.TryParse(context.Request.Query["take"], out var n) ? n : 20;
            return ApiResults.Ok(await monitoring.GetRecentActivityAsync(take, ct));
        });
    }
}
