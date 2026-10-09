using Centra.ControlPlane.HA;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Centra.ControlPlane.Tests.Unit.HA;

public sealed class ControlPlaneLeadershipTests
{
    [Fact]
    public void LeaderTracker_Should_Update_And_Report_State()
    {
        var tracker = new ControlPlaneLeaderTracker();
        tracker.IsLeader.ShouldBeFalse();
        tracker.LeaderEndpoint.ShouldBeNull();

        tracker.SetLeader(true, "http://cp-1:8080");
        tracker.IsLeader.ShouldBeTrue();
        tracker.LeaderEndpoint.ShouldBe("http://cp-1:8080");

        tracker.SetLeader(false, "http://cp-2:8080");
        tracker.IsLeader.ShouldBeFalse();
        tracker.LeaderEndpoint.ShouldBe("http://cp-2:8080");
    }

    [Fact]
    public async Task LeadershipFilter_When_Leader_Should_Invoke_Next()
    {
        var options = new ControlPlaneLeadershipOptions { Enabled = true, PublicEndpoint = "http://cp-1:8080" };
        var tracker = new ControlPlaneLeaderTracker();
        tracker.SetLeader(true, "http://cp-1:8080");

        var filter = new ControlPlaneLeadershipEndpointFilter(options, tracker);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/components";

        var invokedNext = false;
        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        var result = await filter.InvokeAsync(context, _ =>
        {
            invokedNext = true;
            return ValueTask.FromResult<object?>("success");
        });

        invokedNext.ShouldBeTrue();
        result.ShouldBe("success");
    }

    [Fact]
    public async Task LeadershipFilter_When_Standby_Should_Return_307_Redirect()
    {
        var options = new ControlPlaneLeadershipOptions { Enabled = true, PublicEndpoint = "http://cp-2:8081" };
        var tracker = new ControlPlaneLeaderTracker();
        tracker.SetLeader(false, "http://cp-1:8080");

        var filter = new ControlPlaneLeadershipEndpointFilter(options, tracker);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/heartbeat";
        httpContext.Request.QueryString = new QueryString("?clusterId=cluster-1");

        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should-not-reach"));

        httpContext.Response.Headers["Location"].ToString().ShouldBe("http://cp-1:8080/api/v1/heartbeat?clusterId=cluster-1");
        httpContext.Response.Headers["X-Centra-Role"].ToString().ShouldBe("Standby");
        httpContext.Response.Headers["X-Centra-Leader"].ToString().ShouldBe("http://cp-1:8080");
    }

    [Fact]
    public async Task LeadershipFilter_When_Standby_And_Leader_Unknown_Should_Return_503()
    {
        var options = new ControlPlaneLeadershipOptions { Enabled = true, PublicEndpoint = "http://cp-2:8081" };
        var tracker = new ControlPlaneLeaderTracker();
        tracker.SetLeader(false, null);

        var filter = new ControlPlaneLeadershipEndpointFilter(options, tracker);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/components";

        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should-not-reach"));

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }
}
