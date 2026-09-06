using System.Text;
using Centra.Events;
using Centra.Providers.InMemory.PubSub;
using Centra.PubSub;
using Centra.Tests.Unit.Common;
using Shouldly;
using Xunit;

namespace Centra.Tests.Unit.Providers;

public sealed class InMemoryPubSubDriverTests
{
    private readonly InMemoryPubSubDriver _driver = new();

    [Theory, AutoNSubstituteData]
    public async Task Should_Deliver_Message_To_Subscribers(string pubsub, string topic, string message)
    {
        // Arrange
        var received = false;
        string? receivedPayload = null;
        IReadOnlyDictionary<string, string>? receivedHeaders = null;

        await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
        {
            received = true;
            receivedPayload = Encoding.UTF8.GetString(payload.Span);
            receivedHeaders = headers;
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        var bytes = Encoding.UTF8.GetBytes(message);
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.IdHeader] = "msg-1",
            [CloudEventConstants.TypeHeader] = "test-type"
        };

        // Act
        await _driver.PublishAsync(pubsub, topic, bytes, headers);

        // Assert
        received.ShouldBeTrue();
        receivedPayload.ShouldBe(message);
        receivedHeaders.ShouldNotBeNull();
        receivedHeaders[CloudEventConstants.IdHeader].ShouldBe("msg-1");
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Route_To_DeadLetter_When_Subscriber_Requests(string pubsub, string topic, string dlTopic, string message)
    {
        // Arrange
        var dlReceived = false;
        string? dlPayload = null;

        await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
        {
            return ValueTask.FromResult(EventHandlingResult.DeadLetter);
        }, deadLetterTopic: dlTopic);

        await _driver.SubscribeAsync(pubsub, dlTopic, (payload, headers, ct) =>
        {
            dlReceived = true;
            dlPayload = Encoding.UTF8.GetString(payload.Span);
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        var bytes = Encoding.UTF8.GetBytes(message);
        var headers = new Dictionary<string, string>
        {
            [CloudEventConstants.IdHeader] = "msg-dl",
            [CloudEventConstants.TypeHeader] = "test-type"
        };

        // Act
        await _driver.PublishAsync(pubsub, topic, bytes, headers);

        // Assert
        dlReceived.ShouldBeTrue();
        dlPayload.ShouldBe(message);
    }
}
