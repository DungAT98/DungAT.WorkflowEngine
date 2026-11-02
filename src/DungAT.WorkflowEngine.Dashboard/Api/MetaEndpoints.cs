using DungAT.WorkflowEngine.Abstractions.Services;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class MetaEndpoints
{
    public static void Map(RouteGroupBuilder api, WorkflowDashboardOptions options) =>
        api.MapGet("/meta", (HttpContext context, IWorkflowComponentRegistry registry) =>
        {
            var user = options.UserResolver(context);
            return ApiResults.Ok(new DashboardMeta(
                options.Title, options.ReadOnly, user.Id, user.Roles ?? [],
                registry.Guards, registry.Effects, registry.PostActions, registry.Entities, registry.CodeDefinitionCodes.Order().ToList()));
        });
}
