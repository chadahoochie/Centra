using Centra.Drivers;
using Centra.Registry;
using Centra.Resilience;
using Centra.Serialization;
using Centra.State;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class ResilientStateStoreTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IStateStoreDriver _driver = Substitute.For<IStateStoreDriver>();
    private readonly ICentraSerializer _serializer = JsonCentraSerializer.Default;

    public ResilientStateStoreTests()
    {
        _registry.RegisterStateStoreDriver("orders-state", _driver);
    }

    [Fact]
    public async Task Should_Retry_Transient_Database_Error_On_GetAsync()
    {
        // Arrange
        var pipelineRegistry = new PollyResiliencePipelineRegistry();
        pipelineRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "state:orders-state",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var stateStore = new CentraStateStore(_registry, _serializer, pipelineRegistry);

        var calls = 0;
        _driver.GetAsync("orders-state", "key-1", Arg.Any<StateOptions?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new TimeoutException("Database connection timed out");
                }

                var bytes = _serializer.Serialize("stored-value");
                return new ValueTask<StateEntry<byte[]>?>((StateEntry<byte[]>?)new StateEntry<byte[]>("key-1", bytes, "etag-1"));
            });

        // Act
        var result = await stateStore.GetAsync<string>("orders-state", "key-1");

        // Assert
        calls.ShouldBe(2);
        result.ShouldNotBeNull();
        result.Value.Value.ShouldBe("stored-value");
    }

    [Fact]
    public async Task Should_Retry_Transient_Database_Error_On_SetAsync()
    {
        // Arrange
        var pipelineRegistry = new PollyResiliencePipelineRegistry();
        pipelineRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "state:orders-state",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var stateStore = new CentraStateStore(_registry, _serializer, pipelineRegistry);

        var calls = 0;
        _driver.SetAsync(
            "orders-state",
            "key-1",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new InvalidOperationException("Deadlock detected");
                }
                return ValueTask.CompletedTask;
            });

        // Act
        await stateStore.SetAsync("orders-state", "key-1", "test-val");

        // Assert
        calls.ShouldBe(3);
    }

    [Fact]
    public async Task Should_Bypass_Resilience_When_DisableResilience_Is_Set()
    {
        // Arrange
        var pipelineRegistry = new PollyResiliencePipelineRegistry();
        pipelineRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "state:orders-state",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var stateStore = new CentraStateStore(_registry, _serializer, pipelineRegistry);

        var calls = 0;
        _driver.SetAsync(
            "orders-state",
            "key-1",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<StateOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                throw new InvalidOperationException("Fail immediately");
            });

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await stateStore.SetAsync("orders-state", "key-1", "test-val", options: new StateOptions { DisableResilience = true });
        });

        calls.ShouldBe(1);
    }
}
