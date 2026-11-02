using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungAT.WorkflowEngine.Dashboard.Api;

internal static class DashboardJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
