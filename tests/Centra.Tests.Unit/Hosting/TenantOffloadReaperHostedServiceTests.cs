using Centra.Hosting.HostedServices;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class TenantOffloadReaperHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_TriggersCleanupOnCoordinator()
    {
        var coordinator = Substitute.For<ITenantOffloadCoordinator>();
        var options = Microsoft.Extensions.Options.Options.Create(new TenantOffloadOptions
        {
            LaneIdleTimeout = TimeSpan.FromMilliseconds(20)
        });

        var service = new TenantOffloadReaperHostedService(
            coordinator,
            options,
            NullLogger<TenantOffloadReaperHostedService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await service.StartAsync(cts.Token);
        await Task.Delay(40);
        await service.StopAsync(CancellationToken.None);

        await coordinator.Received().CleanupIdleResourcesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_GracefullyHandlesCancellation()
    {
        var coordinator = Substitute.For<ITenantOffloadCoordinator>();
        var options = Microsoft.Extensions.Options.Options.Create(new TenantOffloadOptions
        {
            LaneIdleTimeout = TimeSpan.FromSeconds(60)
        });

        var service = new TenantOffloadReaperHostedService(
            coordinator,
            options,
            NullLogger<TenantOffloadReaperHostedService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }
}
