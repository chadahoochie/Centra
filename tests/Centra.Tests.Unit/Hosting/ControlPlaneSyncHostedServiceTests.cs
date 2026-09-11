using Centra.Components;
using Centra.Hosting.HostedServices;
using Centra.Hosting.Options;
using Centra.Resilience;
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
    public async Task Should_Return_Immediately_When_No_ControlPlane_Endpoint_Configured()
    {
        var client = Substitute.For<IControlPlaneClient>();
        var registry = Substitute.For<IComponentRegistry>();
        var options = Options.Create(new CentraOptions
        {
            AppId = "orders-service",
            ControlPlaneEndpoint = null
        });

        var service = new CentraControlPlaneSyncHostedService(
            client,
            registry,
            options,
            NullLogger<CentraControlPlaneSyncHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        await client.DidNotReceiveWithAnyArgs().GetComponentsAsync(default);
    }

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

    [Fact]
    public async Task Should_Sync_Resilience_Policies_And_Stream_Updates()
    {
        var client = Substitute.For<IControlPlaneClient>();
        var registry = Substitute.For<IComponentRegistry>();
        var resilienceRegistry = Substitute.For<IResiliencePolicyRegistry>();
        var options = Options.Create(new CentraOptions
        {
            AppId = "orders-service",
            ControlPlaneEndpoint = "http://controlplane.local"
        });

        client.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(new List<ComponentDefinition>());
        client.StreamUpdatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ToAsyncEnumerable(new List<ComponentSyncEventDto>(), callInfo.Arg<CancellationToken>()));

        var policy1 = new ResiliencePolicyDto
        {
            PolicyName = "full-policy",
            MaxRetries = 3,
            BackoffType = "Constant",
            BaseDelayMs = 50,
            MaxDelayMs = 500,
            UseJitter = false,
            FailureRatio = 0.6,
            SamplingDurationSeconds = 15,
            MinimumThroughput = 10,
            BreakDurationSeconds = 8,
            TimeoutSeconds = 2.5,
            PermitLimit = 100,
            QueueLimit = 5,
            WindowSeconds = 1,
            MaxParallelism = 10,
            MaxQueuedActions = 25
        };

        var policy2 = new ResiliencePolicyDto
        {
            PolicyName = "linear-policy",
            MaxRetries = 2,
            BackoffType = "Linear"
        };

        client.GetResiliencePoliciesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ResiliencePolicyDto> { policy1, policy2 });

        var streamEvents = new List<ResilienceSyncEventDto>
        {
            new() { Action = "Deleted", PolicyName = "full-policy" },
            new() { Action = "Upserted", PolicyName = "linear-policy", Policy = policy2 }
        };

        client.StreamResilienceUpdatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ToAsyncEnumerable(streamEvents, callInfo.Arg<CancellationToken>()));

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resilienceRegistry.When(r => r.RemovePolicy("full-policy")).Do(_ => tcs.TrySetResult());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var service = new CentraControlPlaneSyncHostedService(
            client,
            registry,
            options,
            NullLogger<CentraControlPlaneSyncHostedService>.Instance,
            null,
            resilienceRegistry);

        // Act
        await service.StartAsync(cts.Token);
        await Task.WhenAny(tcs.Task, Task.Delay(2000, cts.Token));
        await service.StopAsync(CancellationToken.None);

        // Assert
        resilienceRegistry.Received().RegisterPolicy(Arg.Is<CentraResiliencePolicyDefinition>(p => p.PolicyName == "full-policy"));
        resilienceRegistry.Received().RegisterPolicy(Arg.Is<CentraResiliencePolicyDefinition>(p => p.PolicyName == "linear-policy"));
        resilienceRegistry.Received(1).RemovePolicy("full-policy");
    }

    [Fact]
    public async Task Should_Handle_Initial_Sync_Exception_Gracefully()
    {
        var client = Substitute.For<IControlPlaneClient>();
        var registry = Substitute.For<IComponentRegistry>();
        var options = Options.Create(new CentraOptions
        {
            AppId = "orders-service",
            ControlPlaneEndpoint = "http://controlplane.local"
        });

        client.GetComponentsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyCollection<ComponentDefinition>>>(_ => throw new HttpRequestException("ControlPlane unreachable"));
        client.StreamUpdatesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => ToAsyncEnumerable(new List<ComponentSyncEventDto>(), callInfo.Arg<CancellationToken>()));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var service = new CentraControlPlaneSyncHostedService(
            client,
            registry,
            options,
            NullLogger<CentraControlPlaneSyncHostedService>.Instance);

        await Should.NotThrowAsync(async () =>
        {
            await service.StartAsync(cts.Token);
            await service.StopAsync(CancellationToken.None);
        });
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
