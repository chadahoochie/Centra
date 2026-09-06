using Centra.Providers.Redis.Options;
using Shouldly;
using Xunit;

namespace Centra.Providers.Redis.Tests.Unit.Options;

public sealed class RedisOptionsTests
{
    [Fact]
    public void Should_Have_Sensible_Defaults()
    {
        var options = new RedisProviderOptions();

        options.ConnectionString.ShouldBe("localhost:6379");
        options.DefaultStateStoreName.ShouldBe("statestore");
        options.DefaultPubSubName.ShouldBe("pubsub");
        options.DefaultLockStoreName.ShouldBe("lockstore");
        options.KeyPrefix.ShouldBe("centra:");
    }
}
