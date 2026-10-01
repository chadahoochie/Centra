using System.Net;
using Centra.Providers.Flotilla.Udp.Client;
using Centra.Providers.Flotilla.Udp.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Flotilla.Tests.Unit;

public sealed class FlotillaUdpClientTests
{
    [Fact]
    public async Task ProposeAsync_SubmitsPacketAndAppendsToCommitStream()
    {
        using var udpServer = new System.Net.Sockets.UdpClient(0);
        var port = ((IPEndPoint)udpServer.Client.LocalEndPoint!).Port;

        var options = Microsoft.Extensions.Options.Options.Create(new FlotillaUdpOptions
        {
            ClusterNodes = [$"127.0.0.1:{port}"],
            ClientTimeoutMs = 100
        });

        await using var client = new FlotillaUdpClient(options);
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var result = await client.ProposeAsync(payload);
        result.IsSuccess.ShouldBeTrue();
        result.LogIndex.ShouldBe(1UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var commits = new List<ulong>();
        await foreach (var commit in client.SubscribeCommitsAsync(cts.Token))
        {
            commits.Add(commit.LogIndex);
            break;
        }

        commits.Count.ShouldBe(1);
        commits[0].ShouldBe(1UL);
    }

    [Fact]
    public void EndpointResolver_ParsesVariousFormats()
    {
        var ep1 = FlotillaUdpEndpointResolver.ResolveTargetEndpoint(["10.0.0.1:9050"]);
        ep1.ShouldNotBeNull();
        ep1.Address.ToString().ShouldBe("10.0.0.1");
        ep1.Port.ShouldBe(9050);

        var ep2 = FlotillaUdpEndpointResolver.ResolveTargetEndpoint([]);
        ep2.ShouldBeNull();

        var ep3 = FlotillaUdpEndpointResolver.ResolveTargetEndpoint(["127.0.0.1"]);
        ep3.ShouldNotBeNull();
        ep3.Port.ShouldBe(9001);
    }
}
