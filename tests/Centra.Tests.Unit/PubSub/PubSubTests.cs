using System.Diagnostics;
using Centra.Diagnostics;
using Centra.Drivers;
using Centra.Events;
using Centra.PubSub;
using Centra.Registry;
using Centra.Tests.Unit.Common;
using Centra.Tests.Unit.Events;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.PubSub;

public sealed class PubSubTests
{
    private readonly ComponentRegistry _registry = new();
    private readonly IPubSubDriver _driver = Substitute.For<IPubSubDriver>();

    public PubSubTests()
    {
        _registry.RegisterPubSubDriver("event-bus", _driver);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Publish_Event_In_Binary_Mode_By_Default(
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        // Arrange
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);
        var pubSubClient = new CentraPubSubClient(_registry, "orders-service", "event-bus");

        // Act
        await pubSubClient.PublishAsync(topic, evt);

        // Assert
        await _driver.Received(1).PublishAsync(
            "event-bus",
            topic,
            Arg.Is<ReadOnlyMemory<byte>>(m => m.Length > 0),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers[CloudEventConstants.SpecVersionHeader] == CloudEventConstants.SpecVersion10 &&
                headers[CloudEventConstants.TypeHeader] == "orders.created" &&
                headers[CloudEventConstants.SourceHeader] == "orders-service" &&
                headers.ContainsKey(CloudEventConstants.IdHeader)),
            Arg.Any<CancellationToken>());
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Publish_Event_With_Explicit_PubSub_Name(
        string customPubSub,
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        // Arrange
        var customDriver = Substitute.For<IPubSubDriver>();
        _registry.RegisterPubSubDriver(customPubSub, customDriver);

        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);
        var pubSubClient = new CentraPubSubClient(_registry, "orders-service", "event-bus");

        // Act
        await pubSubClient.PublishAsync(customPubSub, topic, evt);

        // Assert
        await customDriver.Received(1).PublishAsync(
            customPubSub,
            topic,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Publish_Event_In_Structured_Mode_When_Specified(
        string topic,
        string orderId,
        string productId,
        int quantity)
    {
        // Arrange
        var evt = new TestOrderCreatedEvent(orderId, productId, quantity);
        var pubSubClient = new CentraPubSubClient(_registry, "orders-service", "event-bus");
        var options = new PubSubPublishOptions { Mode = CloudEventMode.Structured };

        // Act
        await pubSubClient.PublishAsync(topic, evt, options);

        // Assert
        await _driver.Received(1).PublishAsync(
            "event-bus",
            topic,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers[CloudEventConstants.DataContentTypeHeader] == "application/cloudevents+json"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Inject_Traceparent_Header_From_Publish_Activity()
    {
        // Arrange
        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CentraDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = a =>
            {
                if (a.OperationName == "Centra.PubSub.Publish")
                {
                    capturedActivity = a;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var evt = new TestOrderCreatedEvent("ord-123", "prod-456", 5);
        var pubSubClient = new CentraPubSubClient(_registry, "orders-service", "event-bus");

        // Act
        await pubSubClient.PublishAsync("orders.created", evt);

        // Assert
        capturedActivity.ShouldNotBeNull();
        await _driver.Received(1).PublishAsync(
            "event-bus",
            "orders.created",
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Is<IReadOnlyDictionary<string, string>>(headers =>
                headers.ContainsKey("traceparent") &&
                headers["traceparent"].Contains(capturedActivity.TraceId.ToHexString())),
            Arg.Any<CancellationToken>());
    }
}
