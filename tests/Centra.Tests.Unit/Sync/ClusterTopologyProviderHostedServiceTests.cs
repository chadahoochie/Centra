using Centra.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Sync;

public sealed class ClusterTopologyProviderHostedServiceTests
{
    private readonly IControlPlaneClient _client = Substitute.For<IControlPlaneClient>();

    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ClusterTopologyProviderHostedService(null!));
    }

    [Fact]
    public async Task Service_PollsTopology_AndFiresTopologyChanged_OnChanges()
    {
        var node1 = new ServiceNodeDto("orders", "inst-1", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>());
        var node2 = new ServiceNodeDto("orders", "inst-2", "Healthy", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new Dictionary<string, string>());

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ClusterTopologyChangedEventArgs? changedArgs = null;

        _client.GetTopologyAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ServiceNodeDto> { node1, node2 });

        var service = new ClusterTopologyProviderHostedService(_client, NullLogger<ClusterTopologyProviderHostedService>.Instance);
        service.TopologyChanged += (s, args) =>
        {
            changedArgs = args;
            tcs.TrySetResult();
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await Task.WhenAny(tcs.Task, Task.Delay(1000, cts.Token));
        await service.StopAsync(CancellationToken.None);

        changedArgs.ShouldNotBeNull();
        changedArgs.AddedNodes.Count.ShouldBe(2);
        service.GetSnapshot().Count.ShouldBe(2);
    }

    [Fact]
    public async Task Service_HandlesGetTopologyException_Gracefully()
    {
        _client.GetTopologyAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyCollection<ServiceNodeDto>>>(_ => throw new HttpRequestException("Topology down"));

        var service = new ClusterTopologyProviderHostedService(_client, NullLogger<ClusterTopologyProviderHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Should.NotThrowAsync(async () =>
        {
            await service.StartAsync(cts.Token);
            await service.StopAsync(CancellationToken.None);
        });
    }
}
