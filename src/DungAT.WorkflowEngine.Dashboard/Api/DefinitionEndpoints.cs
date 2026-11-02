using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class DefinitionEndpoints
{
    public static void Map(RouteGroupBuilder api, WorkflowDashboardOptions options)
    {
        bool Editable(WorkflowDefinition d, IWorkflowComponentRegistry registry) =>
            !options.ReadOnly && d.Source == WorkflowDefinitionSource.Dashboard && !registry.CodeDefinitionCodes.Contains(d.Code);

        api.MapGet("/definitions", async (IWorkflowDefinitionStore store, IWorkflowComponentRegistry registry, CancellationToken ct) =>
            ApiResults.Ok((await store.ListAsync(ct)).Select(d => new DefinitionSummaryDto(
                d.Code, d.Name, d.Description, d.Version, d.Source.ToString(), d.EntityType,
                d.States.Count, d.Commands.Count, d.Transitions.Count, Editable(d, registry))).ToList()));

        api.MapGet("/definitions/{code}", async (
            string code, HttpContext context, IWorkflowDefinitionStore store, IWorkflowComponentRegistry registry, CancellationToken ct) =>
        {
            int? version = int.TryParse(context.Request.Query["version"], out var v) ? v : null;
            var definition = await store.GetAsync(code, version, ct);
            if (definition is null) return ApiResults.Error(StatusCodes.Status404NotFound, $"Workflow '{code}' was not found.");
            return ApiResults.Ok(await DetailAsync(definition, store, registry, Editable, ct));
        });

        api.MapGet("/definitions/{code}/versions", async (string code, IWorkflowDefinitionStore store, CancellationToken ct) =>
            ApiResults.Ok((await store.ListVersionsAsync(code, ct)).Select(d => new DefinitionVersionDto(d.Version, d.Source.ToString())).ToList()));

        api.MapPost("/definitions/validate", async (HttpContext context, IWorkflowComponentRegistry registry) =>
        {
            var (body, error) = await ApiResults.ReadBodyAsync<WorkflowDefinition>(context);
            if (error is not null) return error;
            var errors = DashboardDefinitionValidator.Validate(DashboardDefinitionValidator.Normalize(body!), registry);
            return ApiResults.Ok(new ValidationResultDto(errors));
        });

        api.MapPost("/definitions", async (
            HttpContext context, IWorkflowDefinitionStore store, IWorkflowComponentRegistry registry, CancellationToken ct) =>
        {
            var (body, error) = await ApiResults.ReadBodyAsync<WorkflowDefinition>(context);
            if (error is not null) return error;

            var definition = DashboardDefinitionValidator.Normalize(body!);
            var errors = DashboardDefinitionValidator.Validate(definition, registry);
            if (errors.Count > 0) return ApiResults.Validation(errors);

            var existing = await store.GetAsync(definition.Code, null, ct);
            if (registry.CodeDefinitionCodes.Contains(definition.Code) || existing?.Source == WorkflowDefinitionSource.Code)
                return ApiResults.Error(StatusCodes.Status409Conflict,
                    $"'{definition.Code}' is defined in code and is read-only here. Choose another code or duplicate it.");

            var published = await store.PublishAsync(definition with { Version = 0, Source = WorkflowDefinitionSource.Dashboard }, ct);
            return ApiResults.Ok(await DetailAsync(published, store, registry, Editable, ct), StatusCodes.Status201Created);
        });
    }

    private static async Task<DefinitionDetailDto> DetailAsync(
        WorkflowDefinition definition, IWorkflowDefinitionStore store, IWorkflowComponentRegistry registry,
        Func<WorkflowDefinition, IWorkflowComponentRegistry, bool> editable, CancellationToken ct)
    {
        var versions = (await store.ListVersionsAsync(definition.Code, ct))
            .Select(d => new DefinitionVersionDto(d.Version, d.Source.ToString())).ToList();
        return new DefinitionDetailDto(definition, editable(definition, registry), versions);
    }
}
