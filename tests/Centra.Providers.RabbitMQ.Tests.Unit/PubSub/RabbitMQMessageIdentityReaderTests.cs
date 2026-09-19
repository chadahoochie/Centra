using Centra.Providers.RabbitMQ.PubSub;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RabbitMQMessageIdentityReaderTests
{
    [Fact]
    public void ResolveIdentity_Prefers_The_CloudEvents_Id_Header()
    {
        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.MessageId.Returns("amqp-message-id");

        var identity = RabbitMQMessageIdentityReader.Instance.ResolveIdentity(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ce-id"] = "evt-1" },
            properties);

        identity.ShouldBe("evt-1");
    }

    [Fact]
    public void ResolveIdentity_Falls_Back_To_The_Amqp_MessageId()
    {
        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.MessageId.Returns("amqp-message-id");

        var identity = RabbitMQMessageIdentityReader.Instance.ResolveIdentity(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            properties);

        identity.ShouldBe("amqp-message-id");
    }

    [Fact]
    public void ResolveIdentity_Falls_Back_When_The_CloudEvents_Id_Header_Is_Blank()
    {
        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.MessageId.Returns("amqp-message-id");

        var identity = RabbitMQMessageIdentityReader.Instance.ResolveIdentity(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ce-id"] = "  " },
            properties);

        identity.ShouldBe("amqp-message-id");
    }

    [Fact]
    public void ResolveIdentity_Returns_Null_When_No_Identity_Survives_Redelivery()
    {
        var properties = Substitute.For<IReadOnlyBasicProperties>();
        properties.MessageId.Returns((string?)null);

        RabbitMQMessageIdentityReader.Instance.ResolveIdentity(null, properties).ShouldBeNull();
        RabbitMQMessageIdentityReader.Instance.ResolveIdentity(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null).ShouldBeNull();
    }
}
