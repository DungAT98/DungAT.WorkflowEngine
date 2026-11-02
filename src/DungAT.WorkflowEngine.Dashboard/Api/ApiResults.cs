using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Models;
using DungAT.WorkflowEngine.Dashboard.Api.Dtos;
using Microsoft.AspNetCore.Http;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class ApiResults
{
    public static IResult Ok<T>(T value, int status = StatusCodes.Status200OK) =>
        Results.Json(value, DashboardJson.Options, statusCode: status);

    public static IResult Error(int status, string message, string? code = null) =>
        Ok(new ErrorDto(message, code), status);

    public static IResult Validation(IReadOnlyList<string> errors) =>
        Ok(new ValidationResultDto(errors), StatusCodes.Status400BadRequest);

    /// <summary>Maps an engine rejection to an HTTP response.</summary>
    public static IResult FromFailure(WorkflowResult result) => Error(StatusFor(result.Status), result.Message ?? result.Status.ToString(), result.Status.ToString());

    public static int StatusFor(WorkflowResultStatus status) => status switch
    {
        WorkflowResultStatus.Success => StatusCodes.Status200OK,
        WorkflowResultStatus.NotFound => StatusCodes.Status404NotFound,
        WorkflowResultStatus.NotAllowed => StatusCodes.Status403Forbidden,
        WorkflowResultStatus.AlreadyExists or WorkflowResultStatus.ConcurrencyConflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity, // InvalidTransition, GuardFailed, CommentRequired
    };

    /// <summary>Reads a JSON body; when it is missing or malformed the value is null and Error holds the 4xx response.</summary>
    public static async Task<(T? Value, IResult? Error)> ReadBodyAsync<T>(HttpContext context) where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(DashboardJson.Options, context.RequestAborted);
            return value is null ? (null, Error(StatusCodes.Status400BadRequest, "Request body is required.")) : (value, null);
        }
        catch (JsonException ex)
        {
            return (null, Error(StatusCodes.Status400BadRequest, "Invalid JSON: " + ex.Message));
        }
        catch (InvalidOperationException)
        {
            return (null, Error(StatusCodes.Status415UnsupportedMediaType, "Content-Type must be application/json."));
        }
    }
}
