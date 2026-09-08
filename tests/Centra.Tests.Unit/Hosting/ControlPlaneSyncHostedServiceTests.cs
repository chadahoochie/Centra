using Centra.Components;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Hosting;

public sealed class ControlPlaneSyncHostedServiceTests
{
    [Fact]
    public async Task Should_Sync_Initial_Components_On_Startup()
    {
        // Arrange
        var client = Substitute.For<IControlPlaneClient>();
        var registry = Substitute.For<IComponentRegistry>();
        var options = Options.Create(new CentraOptions
        {
            AppId = "orders-service",
            ControlPlaneEndpoint = "http://controlplane.local"
        });

        var defs = new List<ComponentDefinition>
        {
            new() { Name = "state1", Type = ComponentType.StateStore, Provider = "in-memory" },
            new() { Name = "pubsub1", Type = ComponentType.PubSub, Provider = "in-memory" }
        };

        client.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(defs);
        client.StreamUpdatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ToAsyncEnumerable(new List<ComponentSyncEventDto>(), callInfo.Arg<CancellationToken>()));

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registeredCount = 0;
        registry.When(r => r.RegisterComponent(Arg.Any<ComponentDefinition>())).Do(_ =>
        {
            if (Interlocked.Increment(ref registeredCount) >= 2)
            {
                tcs.TrySetResult();
            }
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var service = new CentraControlPlaneSyncHostedService(
            client,
            registry,
            options,
            NullLogger<CentraControlPlaneSyncHostedService>.Instance);

        // Act
        await service.StartAsync(cts.Token);
        await Task.WhenAny(tcs.Task, Task.Delay(2000, cts.Token));
        await service.StopAsync(CancellationToken.None);

        // Assert
        registry.Received(1).RegisterComponent(Arg.Is<ComponentDefinition>(d => d.Name == "state1"));
        registry.Received(1).RegisterComponent(Arg.Is<ComponentDefinition>(d => d.Name == "pubsub1"));
    }

    [Fact]
    public async Task Should_Update_ComponentRegistry_When_SyncEvent_Received()
    {
        // Arrange
        var client = Substitute.For<IControlPlaneClient>();
        var registry = Substitute.For<IComponentRegistry>();
        var options = Options.Create(new CentraOptions
        {
            AppId = "orders-service",
            ControlPlaneEndpoint = "http://controlplane.local"
        });

        client.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(new List<ComponentDefinition>());

        var newDef = new ComponentDefinition { Name = "dynamic-store", Type = ComponentType.StateStore, Provider = "in-memory" };
        var events = new List<ComponentSyncEventDto>
        {
            new() { Action = ComponentSyncAction.Added, Definition = newDef, ComponentName = "dynamic-store", Revision = 1 },
            new() { Action = ComponentSyncAction.Removed, Definition = null, ComponentName = "old-store", Revision = 2 }
        };

        client.StreamUpdatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ToAsyncEnumerable(events, callInfo.Arg<CancellationToken>()));

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.When(r => r.RemoveComponent("old-store")).Do(_ => tcs.TrySetResult());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var service = new CentraControlPlaneSyncHostedService(
            client,
            registry,
            options,
            NullLogger<CentraControlPlaneSyncHostedService>.Instance);

        // Act
        await service.StartAsync(cts.Token);
        await Task.WhenAny(tcs.Task, Task.Delay(2000, cts.Token));
        await service.StopAsync(CancellationToken.None);

        // Assert
        registry.Received(1).RegisterComponent(Arg.Is<ComponentDefinition>(d => d.Name == "dynamic-store"));
        registry.Received(1).RemoveComponent("old-store");
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException ex)
        {
            // Expected on cancellation
            System.Diagnostics.Debug.WriteLine($"[Test] Stream enumeration cancelled as expected: {ex.Message}");
        }
    }
}
