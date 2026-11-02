using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class InstanceEndpoints
{
    public static void Map(RouteGroupBuilder api, WorkflowDashboardOptions options)
    {
        api.MapGet("/instances", async (
            HttpContext context, IWorkflowMonitoringStore monitoring, IWorkflowDefinitionStore definitions, CancellationToken ct) =>
        {
            var q = context.Request.Query;
            var query = new InstanceQuery
            {
                DefinitionCode = q["definition"],
                StateCode = q["state"],
                EntityType = q["entityType"],
                Search = q["q"],
                Page = int.TryParse(q["page"], out var page) ? page : 1,
                PageSize = int.TryParse(q["pageSize"], out var size) ? size : 25,
            };
            var result = await monitoring.QueryInstancesAsync(query, ct);
            var cache = new DefinitionCache(definitions);
            var items = new List<InstanceSummaryDto>();
            foreach (var i in result.Items)
                items.Add(InstanceMapper.ToSummary(i, await cache.GetAsync(i.DefinitionCode, i.DefinitionVersion, ct)));
            return ApiResults.Ok(new PagedResult<InstanceSummaryDto>(items, result.Total, result.Page, result.PageSize));
        });

        api.MapGet("/instances/{id:guid}", async (
            Guid id, HttpContext context, IWorkflowEngine engine, IWorkflowDefinitionStore definitions, CancellationToken ct) =>
        {
            var instance = await engine.GetInstanceAsync(id, ct);
            if (instance is null) return ApiResults.Error(StatusCodes.Status404NotFound, "Instance not found.");
            var definition = await definitions.GetAsync(instance.DefinitionCode, instance.DefinitionVersion, ct);
            if (definition is null) return ApiResults.Error(StatusCodes.Status404NotFound, "The instance's workflow version no longer exists.");

            var user = options.UserResolver(context);
            IReadOnlyList<WorkflowCommand> commands = options.ReadOnly ? [] : await engine.GetAvailableCommandsAsync(id, user, ct);
            return ApiResults.Ok(new InstanceDetailDto(
                InstanceMapper.ToSummary(instance, definition), instance.Data, definition,
                InstanceMapper.ToHistory(await engine.GetHistoryAsync(id, ct), await engine.GetPostActionRunsAsync(id, ct)),
                commands, !options.ReadOnly,
                options.EntityLink?.Invoke(instance.EntityType, instance.EntityId)));
        });

        api.MapPost("/instances", async (
            HttpContext context, IWorkflowEngine engine, IWorkflowDefinitionStore definitions, CancellationToken ct) =>
        {
            var (body, error) = await ApiResults.ReadBodyAsync<StartInstanceRequest>(context);
            if (error is not null) return error;
            if (string.IsNullOrWhiteSpace(body!.DefinitionCode) || string.IsNullOrWhiteSpace(body.EntityType) || string.IsNullOrWhiteSpace(body.EntityId))
                return ApiResults.Error(StatusCodes.Status400BadRequest, "definitionCode, entityType and entityId are required.");

            var result = await engine.StartAsync(
                body.DefinitionCode.Trim(), body.EntityType.Trim(), body.EntityId.Trim(), options.UserResolver(context), body.Data, ct);
            if (!result.Succeeded) return ApiResults.FromFailure(result);

            var definition = await definitions.GetAsync(result.Value!.DefinitionCode, result.Value.DefinitionVersion, ct);
            return ApiResults.Ok(new CommandResultDto(InstanceMapper.ToSummary(result.Value, definition), result.Warnings), StatusCodes.Status201Created);
        });

        api.MapPost("/instances/{id:guid}/commands/{command}", async (
            Guid id, string command, HttpContext context, IWorkflowEngine engine, IWorkflowDefinitionStore definitions, CancellationToken ct) =>
        {
            // The body is optional: commands that need no comment can be posted without one.
            ExecuteCommandRequest request = new(null, null);
            // A chunked request has no Content-Length but does have a body, so absence of a length is not "no body".
            var hasBody = context.Request.ContentLength > 0
                          || (context.Request.ContentLength is null && context.Request.Headers.ContainsKey("Transfer-Encoding"));
            // Reject rather than ignore: a comment sent as text/plain would otherwise vanish without a trace.
            if (hasBody && !context.Request.HasJsonContentType())
                return ApiResults.Error(StatusCodes.Status415UnsupportedMediaType, "Send the command body as application/json.");
            if (hasBody)
            {
                var (body, error) = await ApiResults.ReadBodyAsync<ExecuteCommandRequest>(context);
                if (error is not null) return error;
                request = body!;
            }

            var result = await engine.ExecuteAsync(id, command, options.UserResolver(context), request.Comment, request.Payload, ct);
            if (!result.Succeeded) return ApiResults.FromFailure(result);

            var definition = await definitions.GetAsync(result.Value!.DefinitionCode, result.Value.DefinitionVersion, ct);
            return ApiResults.Ok(new CommandResultDto(InstanceMapper.ToSummary(result.Value, definition), result.Warnings));
        });
    }
}
