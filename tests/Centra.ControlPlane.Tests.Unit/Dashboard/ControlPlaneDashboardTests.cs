using System.Net;
using System.Threading.Tasks;
using Centra.ControlPlane.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Dashboard;

public sealed class ControlPlaneDashboardTests
{
    [Fact]
    public void DashboardHtmlProvider_Should_Generate_Html_With_Title_And_Topology_Hooks()
    {
        var html = DashboardHtmlProvider.GetDashboardHtml("Centra Cluster Brain");

        html.ShouldNotBeNull();
        html.ShouldContain("Centra Cluster Brain");
        html.ShouldContain("<!DOCTYPE html>");
        html.ShouldContain("/api/v1/topology");
        html.ShouldContain("/api/v1/health");
    }

    [Fact]
    public void DashboardOptions_Default_Values_Should_Be_Configured()
    {
        var options = new ControlPlaneDashboardOptions();

        options.Enabled.ShouldBeTrue();
        options.Path.ShouldBe("/dashboard");
        options.Title.ShouldBe("Centra Control Plane Dashboard");
    }

    [Theory]
    [InlineData("main.js", "application/javascript")]
    [InlineData("styles.css", "text/css")]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("favicon.ico", "image/x-icon")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("manifest.json", "application/json")]
    [InlineData("unknown.xyz", "application/octet-stream")]
    public void DashboardContentTypeResolver_Should_Resolve_Correct_Mime_Type(string fileName, string expectedMime)
    {
        var mime = DashboardContentTypeResolver.GetContentType(fileName);
        mime.ShouldBe(expectedMime);
    }

    [Fact]
    public void DashboardFileLocator_Should_Locate_Dashboard_Root()
    {
        var root = DashboardFileLocator.FindDashboardRoot();
        root.ShouldNotBeNull();
    }

    [Fact]
    public async Task MapCentraDashboard_Should_Serve_Dashboard_Html()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.MapCentraDashboard();
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("<html");
    }

    [Fact]
    public async Task MapCentraDashboard_WhenDisabled_Should_Return_NotFound()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();

        await using var app = builder.Build();
        app.MapCentraDashboard(new ControlPlaneDashboardOptions { Enabled = false });
        await app.StartAsync();

        var client = app.GetTestClient();
        var response = await client.GetAsync("/dashboard");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
