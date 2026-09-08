using Centra.Invocation;
using Centra.Sync;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Invocation;

public sealed class ServiceEndpointResolverTests
{
    private readonly IClusterTopologyProvider _topologyProvider = Substitute.For<IClusterTopologyProvider>();

    [Fact]
    public async Task PassThroughResolver_Should_Return_Http_Uri()
    {
        var resolver = PassThroughServiceEndpointResolver.Instance;

        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://order-service/");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PassThroughResolver_Should_Throw_On_Invalid_AppId(string? appId)
    {
        var resolver = PassThroughServiceEndpointResolver.Instance;

        await Should.ThrowAsync<ArgumentException>(async () =>
            await resolver.ResolveEndpointAsync(appId!));
    }

    [Fact]
    public void ControlPlaneResolver_Should_Throw_On_Null_Client()
    {
        Should.Throw<ArgumentNullException>(() => new ControlPlaneServiceEndpointResolver(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ControlPlaneResolver_Should_Throw_On_Invalid_AppId(string? appId)
    {
        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await resolver.ResolveEndpointAsync(appId!));
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Fallback_When_No_Matching_Nodes()
    {
        _topologyProvider.GetSnapshot().Returns(new List<ServiceNodeDto>());

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);
        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://order-service/");
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Fallback_When_Nodes_Are_Unhealthy()
    {
        var nodes = new List<ServiceNodeDto>
        {
            new("order-service", "node-1", "Unhealthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["address"] = "http://10.0.0.1:5000" })
        };

        _topologyProvider.GetSnapshot().Returns(nodes);

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);
        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://order-service/");
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Resolve_From_Metadata_Address()
    {
        var nodes = new List<ServiceNodeDto>
        {
            new("order-service", "node-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["address"] = "http://10.0.0.1:5000" })
        };

        _topologyProvider.GetSnapshot().Returns(nodes);

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);
        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://10.0.0.1:5000/");
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Resolve_From_Metadata_Endpoint_Or_Url()
    {
        var nodes = new List<ServiceNodeDto>
        {
            new("inventory-service", "node-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["endpoint"] = "https://10.0.0.2:5001" }),
            new("payment-service", "node-2", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["url"] = "http://10.0.0.3:5002" })
        };

        _topologyProvider.GetSnapshot().Returns(nodes);

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);

        var uri1 = await resolver.ResolveEndpointAsync("inventory-service");
        var uri2 = await resolver.ResolveEndpointAsync("payment-service");

        uri1.ShouldNotBeNull();
        uri1.ToString().ShouldBe("https://10.0.0.2:5001/");

        uri2.ShouldNotBeNull();
        uri2.ToString().ShouldBe("http://10.0.0.3:5002/");
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Round_Robin_Across_Healthy_Replicas()
    {
        var nodes = new List<ServiceNodeDto>
        {
            new("worker-service", "node-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["address"] = "http://10.0.0.1:5000" }),
            new("worker-service", "node-2", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["address"] = "http://10.0.0.2:5000" })
        };

        _topologyProvider.GetSnapshot().Returns(nodes);

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);

        var uri1 = await resolver.ResolveEndpointAsync("worker-service");
        var uri2 = await resolver.ResolveEndpointAsync("worker-service");
        var uri3 = await resolver.ResolveEndpointAsync("worker-service");

        uri1.ShouldNotBeNull();
        uri2.ShouldNotBeNull();
        uri3.ShouldNotBeNull();

        // Round robin should alternatingly pick node-1, node-2, node-1
        uri1.ToString().ShouldBe("http://10.0.0.1:5000/");
        uri2.ToString().ShouldBe("http://10.0.0.2:5000/");
        uri3.ToString().ShouldBe("http://10.0.0.1:5000/");
    }

    [Fact]
    public async Task ControlPlaneResolver_Should_Fallback_When_Snapshot_Throws()
    {
        _topologyProvider.GetSnapshot().Returns(_ => throw new InvalidOperationException("Topology unavailable"));

        var resolver = new ControlPlaneServiceEndpointResolver(_topologyProvider);
        var uri = await resolver.ResolveEndpointAsync("order-service");

        uri.ShouldNotBeNull();
        uri.ToString().ShouldBe("http://order-service/");
    }
}
