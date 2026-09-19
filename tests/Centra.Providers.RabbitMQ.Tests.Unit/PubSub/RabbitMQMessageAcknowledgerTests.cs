using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RabbitMQMessageAcknowledgerTests
{
    [Fact]
    public async Task AcknowledgeMessageAsync_ThrowsArgumentNullException_WhenChannelIsNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            RabbitMQMessageAcknowledger.Instance.AcknowledgeMessageAsync(null!, 12345, EventHandlingResult.Success).AsTask());
    }

    [Fact]
    public async Task AcknowledgeMessageAsync_CallsBasicAck_OnSuccess()
    {
        var channel = Substitute.For<IChannel>();
        const ulong tag = 42;

        await RabbitMQMessageAcknowledger.Instance.AcknowledgeMessageAsync(channel, tag, EventHandlingResult.Success);

        await channel.Received(1).BasicAckAsync(tag, multiple: false);
    }

    [Fact]
    public async Task AcknowledgeMessageAsync_CallsBasicReject_OnDeadLetter()
    {
        var channel = Substitute.For<IChannel>();
        const ulong tag = 43;

        await RabbitMQMessageAcknowledger.Instance.AcknowledgeMessageAsync(channel, tag, EventHandlingResult.DeadLetter);

        await channel.Received(1).BasicRejectAsync(tag, requeue: false);
    }

    [Fact]
    public async Task AcknowledgeMessageAsync_CallsBasicAck_OnDrop_SoTheMessageIsDiscardedNotDeadLettered()
    {
        var channel = Substitute.For<IChannel>();
        const ulong tag = 45;

        await RabbitMQMessageAcknowledger.Instance.AcknowledgeMessageAsync(channel, tag, EventHandlingResult.Drop);

        await channel.Received(1).BasicAckAsync(tag, multiple: false);
        await channel.DidNotReceive().BasicRejectAsync(Arg.Any<ulong>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task AcknowledgeMessageAsync_CallsBasicNack_OnRetry()
    {
        var channel = Substitute.For<IChannel>();
        const ulong tag = 44;

        await RabbitMQMessageAcknowledger.Instance.AcknowledgeMessageAsync(channel, tag, EventHandlingResult.Retry);

        await channel.Received(1).BasicNackAsync(tag, multiple: false, requeue: true);
    }
}
