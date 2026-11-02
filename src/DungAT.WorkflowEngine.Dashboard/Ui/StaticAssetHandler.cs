using System.Net;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DungAT.WorkflowEngine.Dashboard.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace DungAT.WorkflowEngine.Dashboard.Ui;

/// <summary>Serves the SPA embedded in the assembly. <c>index.html</c> gets the mount path and title injected.</summary>
internal static partial class StaticAssetHandler
{
    [GeneratedRegex(@"\{\{(BASE|TITLE|CONFIG)\}\}")]
    private static partial Regex Placeholder();

    // Resource names are "<AssemblyName>.wwwroot.<path with dots>", so keep folder names free of dashes and spaces.
    private static readonly EmbeddedFileProvider Files =
        new(typeof(StaticAssetHandler).Assembly, "DungAT.WorkflowEngine.Dashboard.wwwroot");
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly string BuildId = typeof(StaticAssetHandler).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    public static IResult Handle(HttpContext context, string? path, string mountPath, WorkflowDashboardOptions options)
    {
        path = string.IsNullOrEmpty(path) ? "index.html" : path;
        if (path.Contains("..") || path.Contains('\\') || path.StartsWith('/'))
            return Results.NotFound();

        var file = Files.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory) return Results.NotFound();

        var isIndex = path == "index.html";
        // Mounted at "/" the mount path is "/": trim it so the base is "/" and not "//" (which a browser reads as another host).
        var basePath = $"{context.Request.PathBase}{mountPath.TrimEnd('/')}/";
        var etag = $"\"{Hash(BuildId + path + (isIndex ? basePath + options.Title + options.ReadOnly : ""))}\"";

        context.Response.Headers[HeaderNames.CacheControl] = "no-cache"; // always revalidate; the ETag makes it cheap
        context.Response.Headers[HeaderNames.ETag] = etag;
        if (IsNotModified(context.Request, etag)) return Results.StatusCode(StatusCodes.Status304NotModified);

        if (isIndex)
        {
            using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
            var config = JsonSerializer.Serialize(new { basePath, apiBase = basePath + "api", title = options.Title, readOnly = options.ReadOnly },
                DashboardJson.Options);
            // "<" is escaped so a title like "</script>" cannot break out of the inline script.
            var safeConfig = config.Replace("<", "\\u003c");
            // One pass, so text inserted for one placeholder (say a title containing "{{CONFIG}}") is never expanded again.
            var html = Placeholder().Replace(reader.ReadToEnd(), m => m.Groups[1].Value switch
            {
                "BASE" => WebUtility.HtmlEncode(basePath),
                "TITLE" => WebUtility.HtmlEncode(options.Title),
                _ => safeConfig,
            });
            return Results.Content(html, "text/html; charset=utf-8");
        }

        if (!ContentTypes.TryGetContentType(path, out var contentType)) contentType = "application/octet-stream";
        return Results.Stream(file.CreateReadStream(), contentType);
    }

    /// <summary>Honours lists of validators, <c>*</c> and weak validators (<c>W/"..."</c>), which proxies and CDNs add.</summary>
    private static bool IsNotModified(HttpRequest request, string etag)
    {
        var current = new EntityTagHeaderValue(etag);
        return request.GetTypedHeaders().IfNoneMatch.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(current, useStrongComparison: false));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
