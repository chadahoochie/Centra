using System.Net;
using Centra.Providers.Flotilla.Tcp.Client;
using Centra.Providers.Flotilla.Udp.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaEndpointResolverTests
{
    [Theory]
    [InlineData("127.0.0.1:9100", 9100)]
    [InlineData("tcp://127.0.0.1:9100", 9100)]
    [InlineData("tcp://localhost:9100", 9100)]
    [InlineData("localhost:9100", 9100)]
    [InlineData("127.0.0.1", 9001)]
    [InlineData("localhost", 9001)]
    public void TcpEndpointResolver_ShouldResolve_SupportedNodeFormats(string node, int expectedPort)
    {
        var ep = FlotillaTcpEndpointResolver.ResolveTargetEndpoint([node], 9001);

        ep.ShouldNotBeNull();
        ep.Address.ShouldBe(IPAddress.Loopback);
        ep.Port.ShouldBe(expectedPort);
    }

    [Theory]
    [InlineData("127.0.0.1:9200", 9200)]
    [InlineData("udp://127.0.0.1:9200", 9200)]
    [InlineData("udp://localhost:9200", 9200)]
    [InlineData("localhost:9200", 9200)]
    [InlineData("127.0.0.1", 9001)]
    [InlineData("localhost", 9001)]
    public void UdpEndpointResolver_ShouldResolve_SupportedNodeFormats(string node, int expectedPort)
    {
        var ep = FlotillaUdpEndpointResolver.ResolveTargetEndpoint([node], 9001);

        ep.ShouldNotBeNull();
        ep.Address.ShouldBe(IPAddress.Loopback);
        ep.Port.ShouldBe(expectedPort);
    }
}
