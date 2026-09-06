using Centra.Providers.AzureServiceBus.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.AzureServiceBus.Tests.Unit.Options;

public sealed class AzureServiceBusProviderOptionsTests
{
    [Fact]
    public void Default_Options_Should_Have_Expected_Values()
    {
        var options = new AzureServiceBusProviderOptions();

        options.ConnectionString.ShouldNotBeNullOrWhiteSpace();
        options.DefaultPubSubName.ShouldBe("pubsub");
        options.TopicPrefix.ShouldBe("");
        options.SubscriptionName.ShouldBe("centra-sub");
        options.MaxConcurrentCalls.ShouldBe(1);
        options.AutoCompleteMessages.ShouldBeFalse();
    }

    [Fact]
    public void Options_Should_Allow_Property_Mutations()
    {
        var options = new AzureServiceBusProviderOptions
        {
            ConnectionString = "Endpoint=sb://prod.servicebus.windows.net/;",
            DefaultPubSubName = "custom-bus",
            TopicPrefix = "dev.",
            SubscriptionName = "orders-worker",
            MaxConcurrentCalls = 8,
            AutoCompleteMessages = true
        };

        options.ConnectionString.ShouldBe("Endpoint=sb://prod.servicebus.windows.net/;");
        options.DefaultPubSubName.ShouldBe("custom-bus");
        options.TopicPrefix.ShouldBe("dev.");
        options.SubscriptionName.ShouldBe("orders-worker");
        options.MaxConcurrentCalls.ShouldBe(8);
        options.AutoCompleteMessages.ShouldBeTrue();
    }
}
