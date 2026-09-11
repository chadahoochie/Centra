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

    [Theory, AutoNSubstituteData]
    public async Task SingleActiveConsumer_Should_Round_Robin_Instead_Of_Broadcasting(string pubsub, string topic, string message)
    {
        // Arrange: three single-active subscribers on the same topic should each get roughly a
        // third of the traffic (exactly one handler per publish), not all three every time.
        var counts = new int[3];
        var options = new PubSubSubscribeOptions { ConsumerMode = ConsumerMode.SingleActiveConsumer };

        for (var i = 0; i < 3; i++)
        {
            var index = i;
            await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
            {
                Interlocked.Increment(ref counts[index]);
                return ValueTask.FromResult(EventHandlingResult.Success);
            }, options: options);
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        var headers = new Dictionary<string, string>();

        // Act
        for (var i = 0; i < 6; i++)
        {
            await _driver.PublishAsync(pubsub, topic, bytes, headers);
        }

        // Assert: exactly one handler invoked per publish (6 total), spread across subscribers.
        counts.Sum().ShouldBe(6);
        counts.ShouldAllBe(c => c > 0);
    }

    [Theory, AutoNSubstituteData]
    public async Task CompetingConsumer_Should_Still_Broadcast_To_All_Subscribers(string pubsub, string topic, string message)
    {
        // Arrange: default/legacy behavior - every competing-consumer subscriber gets every message.
        var counts = new int[2];

        for (var i = 0; i < 2; i++)
        {
            var index = i;
            await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
            {
                Interlocked.Increment(ref counts[index]);
                return ValueTask.FromResult(EventHandlingResult.Success);
            });
        }

        var bytes = Encoding.UTF8.GetBytes(message);

        // Act
        await _driver.PublishAsync(pubsub, topic, bytes, new Dictionary<string, string>());

        // Assert
        counts[0].ShouldBe(1);
        counts[1].ShouldBe(1);
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Not_Deliver_After_Unsubscribe(string pubsub, string topic, string message)
    {
        var received = false;
        await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
        {
            received = true;
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        await _driver.UnsubscribeAsync(pubsub, topic);
        await _driver.PublishAsync(pubsub, topic, Encoding.UTF8.GetBytes(message), new Dictionary<string, string>());

        received.ShouldBeFalse();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Route_To_DeadLetter_When_Handler_Throws_With_DeadLetterTopic(string pubsub, string topic, string dlTopic, string message)
    {
        var dlReceived = false;
        await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
        {
            throw new InvalidOperationException("Handler exploded");
        }, deadLetterTopic: dlTopic);

        await _driver.SubscribeAsync(pubsub, dlTopic, (payload, headers, ct) =>
        {
            dlReceived = true;
            return ValueTask.FromResult(EventHandlingResult.Success);
        });

        await _driver.PublishAsync(pubsub, topic, Encoding.UTF8.GetBytes(message), new Dictionary<string, string>());

        dlReceived.ShouldBeTrue();
    }

    [Theory, AutoNSubstituteData]
    public async Task Should_Rethrow_When_Handler_Throws_Without_DeadLetterTopic(string pubsub, string topic, string message)
    {
        await _driver.SubscribeAsync(pubsub, topic, (payload, headers, ct) =>
        {
            throw new InvalidOperationException("No DLQ available");
        });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _driver.PublishAsync(pubsub, topic, Encoding.UTF8.GetBytes(message), new Dictionary<string, string>()).AsTask());
    }

    [Fact]
    public async Task Should_Do_Nothing_When_Publishing_To_Unsubscribed_Topic()
    {
        await _driver.PublishAsync("unknown-pubsub", "no-subscribers", Encoding.UTF8.GetBytes("payload"), new Dictionary<string, string>());
        // Should complete without error
    }
}
