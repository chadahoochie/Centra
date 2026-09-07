using Centra.Drivers;
using Centra.PubSub;
using Centra.Registry;
using Centra.Resilience;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Resilience;

public sealed class ResilientPubSubTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();

    public ResilientPubSubTests()
    {
        _registry.RegisterPubSubDriver("messagebus", _driver);
    }

    [Fact]
    public async Task Should_Retry_Transient_Driver_Failure_On_PublishAsync()
    {
        // Arrange
        var pipelineRegistry = new PollyResiliencePipelineRegistry();
        pipelineRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "pubsub:messagebus",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var client = new CentraPubSubClient(_registry, "test-app", "messagebus", pipelineRegistry);

        var calls = 0;
        _driver.PublishAsync(
            "messagebus",
            "orders.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new TimeoutException("Broker temporarily unreachable");
                }
                return ValueTask.CompletedTask;
            });

        // Act
        await client.PublishAsync("messagebus", "orders.created", new { OrderId = 123 });

        // Assert
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Should_Bypass_Resilience_When_DisableResilience_Is_Set_On_PublishAsync()
    {
        // Arrange
        var pipelineRegistry = new PollyResiliencePipelineRegistry();
        pipelineRegistry.RegisterPolicy(new CentraResiliencePolicyDefinition(
            PolicyName: "pubsub:messagebus",
            Retry: new RetryPolicyOptions(
                MaxRetries: 3,
                BackoffType: CentraBackoffType.Constant,
                BaseDelay: TimeSpan.FromMilliseconds(1),
                UseJitter: false)));

        var client = new CentraPubSubClient(_registry, "test-app", "messagebus", pipelineRegistry);

        var calls = 0;
        _driver.PublishAsync(
            "messagebus",
            "orders.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;
                throw new TimeoutException("Broker down");
            });

        // Act & Assert
        await Should.ThrowAsync<TimeoutException>(async () =>
        {
            await client.PublishAsync(
                "messagebus",
                "orders.created",
                new { OrderId = 123 },
                options: new PubSubPublishOptions { DisableResilience = true });
        });

        calls.ShouldBe(1);
    }
}
