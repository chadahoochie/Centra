using Centra.Providers.RabbitMQ.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.Options;

public sealed class RabbitMQOptionsTests
{
    [Fact]
    public void Should_Have_Sensible_Defaults()
    {
        var options = new RabbitMQProviderOptions();

        options.HostName.ShouldBe("localhost");
        options.Port.ShouldBe(5672);
        options.UserName.ShouldBe("guest");
        options.Password.ShouldBe("guest");
        options.VirtualHost.ShouldBe("/");
        options.ExchangeName.ShouldBe("centra.pubsub");
        options.DefaultPubSubName.ShouldBe("pubsub");
        options.QueuePrefix.ShouldBe("centra");
    }
}
