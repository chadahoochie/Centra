using System.Net;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneSecurityEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    public ControlPlaneSecurityEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();
        builder.Services.AddCentraControlPlaneSecurity(opt =>
        {
            opt.Enabled = true;
            opt.ClusterTokens["cluster-1"] = "cluster-secret-999";
        });

        _app = builder.Build();
        _app.MapCentraControlPlaneEndpoints();
        _app.StartAsync().GetAwaiter().GetResult();

        _client = _app.GetTestServer().CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Health_Should_Bypass_Security_Filter()
    {
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unauthenticated_Topology_Query_Should_Be_Rejected_With_401()
    {
        var response = await _client.GetAsync("/api/v1/topology?clusterId=cluster-1");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_Topology_Query_Should_Succeed()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/topology?clusterId=cluster-1");
        request.Headers.Add("X-Centra-Cluster-Token", "cluster-secret-999");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
