using Azure.Messaging.ServiceBus;
using Centra.PubSub;
using Centra.Providers.AzureServiceBus.PubSub;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Centra.Providers.AzureServiceBus.Tests.Unit.PubSub;

public sealed class ServiceBusMessageSettlerTests
{
    [Fact]
    public async Task SettleMessageAsync_ThrowsArgumentNullException_WhenArgsIsNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            ServiceBusMessageSettler.Instance.SettleMessageAsync(null!, EventHandlingResult.Success));
    }

    [Fact]
    public async Task SettleSessionMessageAsync_ThrowsArgumentNullException_WhenArgsIsNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(() =>
            ServiceBusMessageSettler.Instance.SettleSessionMessageAsync(null!, EventHandlingResult.Success));
    }

    [Theory]
    [InlineData(EventHandlingResult.Success)]
    [InlineData(EventHandlingResult.Drop)]
    public async Task SettleMessageAsync_CompletesMessage_OnSuccessOrDrop(EventHandlingResult result)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var args = Substitute.For<ProcessMessageEventArgs>(
            message,
            Substitute.For<ServiceBusReceiver>(),
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleMessageAsync(args, result);

        await args.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleMessageAsync_DeadLettersMessage_OnDeadLetter()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var args = Substitute.For<ProcessMessageEventArgs>(
            message,
            Substitute.For<ServiceBusReceiver>(),
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleMessageAsync(args, EventHandlingResult.DeadLetter);

        await args.Received(1).DeadLetterMessageAsync(
            message,
            "DeadLetter",
            "Handler requested dead-lettering",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleMessageAsync_AbandonsMessage_OnRetry()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"));
        var args = Substitute.For<ProcessMessageEventArgs>(
            message,
            Substitute.For<ServiceBusReceiver>(),
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleMessageAsync(args, EventHandlingResult.Retry);

        await args.Received(1).AbandonMessageAsync(message, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EventHandlingResult.Success)]
    [InlineData(EventHandlingResult.Drop)]
    public async Task SettleSessionMessageAsync_CompletesMessage_OnSuccessOrDrop(EventHandlingResult result)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"), sessionId: "session-1");
        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        var args = Substitute.For<ProcessSessionMessageEventArgs>(
            message,
            sessionReceiver,
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleSessionMessageAsync(args, result);

        await args.Received(1).CompleteMessageAsync(message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleSessionMessageAsync_DeadLettersMessage_OnDeadLetter()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"), sessionId: "session-1");
        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        var args = Substitute.For<ProcessSessionMessageEventArgs>(
            message,
            sessionReceiver,
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleSessionMessageAsync(args, EventHandlingResult.DeadLetter);

        await args.Received(1).DeadLetterMessageAsync(
            message,
            "DeadLetter",
            "Handler requested dead-lettering",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettleSessionMessageAsync_AbandonsMessage_OnRetry()
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(BinaryData.FromString("body"), sessionId: "session-1");
        var sessionReceiver = Substitute.For<ServiceBusSessionReceiver>();
        var args = Substitute.For<ProcessSessionMessageEventArgs>(
            message,
            sessionReceiver,
            CancellationToken.None);

        await ServiceBusMessageSettler.Instance.SettleSessionMessageAsync(args, EventHandlingResult.Retry);

        await args.Received(1).AbandonMessageAsync(message, cancellationToken: Arg.Any<CancellationToken>());
    }
}
