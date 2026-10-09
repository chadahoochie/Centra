using System.Net;
using Centra.ControlPlane.Endpoints;
using Centra.ControlPlane.Extensions;
using Centra.ControlPlane.HA;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.Endpoints;

public sealed class ControlPlaneLeadershipEndpointsTests : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly IControlPlaneLeaderTracker _tracker;

    public ControlPlaneLeadershipEndpointsTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCentraControlPlane();
        builder.Services.AddCentraControlPlaneLeadership(opt =>
        {
            opt.Enabled = true;
            opt.PublicEndpoint = "http://cp-2:8081";
        });

        _app = builder.Build();
        _app.MapCentraControlPlaneEndpoints();
        _app.StartAsync().GetAwaiter().GetResult();

        _tracker = _app.Services.GetRequiredService<IControlPlaneLeaderTracker>();
        _tracker.SetLeader(false, "http://cp-1:8080");

        var handler = _app.GetTestServer().CreateHandler();
        _client = new HttpClient(new NonRedirectingHandler(handler))
        {
            BaseAddress = new Uri("http://localhost/")
        };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Health_On_Standby_Node_Should_Return_200_With_Role_And_Leader_Headers()
    {
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Centra-Role").First().ShouldBe("Standby");
        response.Headers.GetValues("X-Centra-Leader").First().ShouldBe("http://cp-1:8080");
    }

    [Fact]
    public async Task Topology_On_Standby_Node_Should_Return_307_Temporary_Redirect()
    {
        var response = await _client.GetAsync("/api/v1/topology");

        response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.ShouldNotBeNull();
        response.Headers.Location.ToString().ShouldBe("http://cp-1:8080/api/v1/topology");
    }
}
