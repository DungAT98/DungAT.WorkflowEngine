using System.Net;
using System.Text.RegularExpressions;
using DungAT.WorkflowEngine.Dashboard.Authorization;

namespace DungAT.WorkflowEngine.Dashboard.Tests;

public class StaticAssetTests
{
    [Fact]
    public async Task Index_is_served_with_base_path_and_escaped_title()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var response = await host.Client.GetAsync("/workflow/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<base href=\"/workflow/\">", html);
        Assert.Contains("Test &lt;Approvals&gt;", html);
        Assert.Contains("\"apiBase\":\"/workflow/api\"", html);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Prefix_without_slash_redirects_and_unknown_assets_404()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var redirect = await host.Client.GetAsync("/workflow");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/workflow/", redirect.Headers.Location!.OriginalString);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/workflow/nope.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/workflow/..%2Fsecret")).StatusCode);
    }

    [Fact]
    public async Task Etag_allows_not_modified_responses()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var first = await host.Client.GetAsync("/workflow/");
        var request = host.Request(HttpMethod.Get, "/workflow/");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await host.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Denying_filter_blocks_ui_and_api()
    {
        await using var host = await DashboardTestHost.StartAsync(o =>
            o.Authorization = [new DelegateAuthorizationFilter(_ => false)]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/workflow/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/workflow/api/meta")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/workflow/api/instances")).StatusCode);
    }

    [Fact]
    public async Task Mapping_without_registration_fails_fast()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        var app = builder.Build();
        var ex = Assert.Throws<InvalidOperationException>(() => app.MapWorkflowDashboard());
        Assert.Contains("AddWorkflowDashboard", ex.Message);
        await app.DisposeAsync();
    }

    [Fact]
    public async Task Every_asset_referenced_by_the_shell_and_every_page_template_is_embedded()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var html = await host.Client.GetStringAsync("/workflow/");

        var assets = Regex.Matches(html, "(?:src|href)=\"((?:js|vendor)/[^\"]+|app\\.css)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("vendor/alpine.min.js", assets);
        Assert.Contains("js/router.js", assets);
        Assert.Contains("app.css", assets);

        // Pages the router loads by name (js/router.js) and the script each one registers.
        var pages = new[] { "overview", "definitions", "definition", "editor", "instances", "instance" };
        assets.AddRange(pages.Select(p => $"pages/{p}.html"));
        Assert.All(pages, p => Assert.Contains($"js/pages/{p}.js", assets));

        foreach (var asset in assets)
        {
            var response = await host.Client.GetAsync("/workflow/" + asset);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{asset} returned {(int)response.StatusCode}");
            Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Validators_are_matched_even_when_weak_listed_or_wildcard()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var tag = (await host.Client.GetAsync("/workflow/")).Headers.ETag!.Tag;

        foreach (var header in new[] { $"W/{tag}", $"\"other\", {tag}", "*" })
        {
            var request = host.Request(HttpMethod.Get, "/workflow/");
            request.Headers.TryAddWithoutValidation("If-None-Match", header);
            Assert.Equal(HttpStatusCode.NotModified, (await host.Client.SendAsync(request)).StatusCode);
        }

        var stale = host.Request(HttpMethod.Get, "/workflow/");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"something-else\"");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(stale)).StatusCode);
    }

    [Fact]
    public async Task Placeholder_text_inside_the_title_is_not_expanded_a_second_time()
    {
        await using var host = await DashboardTestHost.StartAsync(o => o.Title = "Team {{CONFIG}} {{BASE}}");
        var html = await host.Client.GetStringAsync("/workflow/");

        Assert.Contains("<title>Team {{CONFIG}} {{BASE}}</title>", html);
        Assert.Contains("window.__WORKFLOW__ = {", html);
    }

    [Fact]
    public async Task Redirect_from_the_bare_prefix_keeps_the_query_string()
    {
        await using var host = await DashboardTestHost.StartAsync();
        var redirect = await host.Client.GetAsync("/workflow?definition=DOC");
        Assert.Equal("/workflow/?definition=DOC", redirect.Headers.Location!.OriginalString);
    }
}

