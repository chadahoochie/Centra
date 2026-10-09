using Centra.Sync;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Sync;

public sealed class ControlPlaneEndpointSelectorTests
{
    [Fact]
    public void Should_Return_First_Endpoint_As_Primary()
    {
        var selector = new ControlPlaneEndpointSelector(new[] { "http://cp-1:8080", "http://cp-2:8081" });

        selector.GetCurrentEndpoint().ShouldBe("http://cp-1:8080");
    }

    [Fact]
    public void Should_Failover_To_Next_Endpoint_When_Marked_Failed()
    {
        var selector = new ControlPlaneEndpointSelector(new[] { "http://cp-1:8080", "http://cp-2:8081" });

        selector.MarkEndpointFailed("http://cp-1:8080");

        selector.GetCurrentEndpoint().ShouldBe("http://cp-2:8081");
    }

    [Fact]
    public void Should_Pin_Current_Leader_When_Redirected()
    {
        var selector = new ControlPlaneEndpointSelector(new[] { "http://cp-1:8080", "http://cp-2:8081" });

        selector.SetCurrentLeader("http://cp-2:8081");

        selector.GetCurrentEndpoint().ShouldBe("http://cp-2:8081");
    }
}
