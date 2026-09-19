using Centra.Providers.RabbitMQ.Options;
using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class RedeliveryBudgetPolicyTests
{
    [Fact]
    public void Resolve_Defaults_To_Budget_Of_Three_With_Exponential_Backoff()
    {
        var policy = RedeliveryBudgetPolicy.Resolve(options: null, new RabbitMQProviderOptions());

        policy.MaxRetryAttempts.ShouldBe(3);
        policy.InitialBackoff.ShouldBe(TimeSpan.FromSeconds(1));
        policy.MaxBackoff.ShouldBe(TimeSpan.FromSeconds(30));
        policy.BackoffFor(1).ShouldBe(TimeSpan.FromSeconds(1));
        policy.BackoffFor(2).ShouldBe(TimeSpan.FromSeconds(2));
        policy.BackoffFor(3).ShouldBe(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void Resolve_Prefers_Subscription_Overrides_Over_Provider_Defaults()
    {
        var policy = RedeliveryBudgetPolicy.Resolve(
            new PubSubSubscribeOptions
            {
                MaxRetryAttempts = 5,
                RetryInitialBackoff = TimeSpan.FromMilliseconds(20),
                RetryMaxBackoff = TimeSpan.FromMilliseconds(50)
            },
            new RabbitMQProviderOptions());

        policy.MaxRetryAttempts.ShouldBe(5);
        policy.InitialBackoff.ShouldBe(TimeSpan.FromMilliseconds(20));
        policy.MaxBackoff.ShouldBe(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public void Resolve_Falls_Back_Per_Property_When_Only_Some_Overrides_Are_Set()
    {
        var providerOptions = new RabbitMQProviderOptions
        {
            DefaultMaxRetryAttempts = 7,
            DefaultRetryInitialBackoff = TimeSpan.FromSeconds(3),
            DefaultRetryMaxBackoff = TimeSpan.FromSeconds(9)
        };

        var policy = RedeliveryBudgetPolicy.Resolve(
            new PubSubSubscribeOptions { RetryInitialBackoff = TimeSpan.FromMilliseconds(5) },
            providerOptions);

        policy.MaxRetryAttempts.ShouldBe(7);
        policy.InitialBackoff.ShouldBe(TimeSpan.FromMilliseconds(5));
        policy.MaxBackoff.ShouldBe(TimeSpan.FromSeconds(9));
    }

    [Fact]
    public void Resolve_Throws_When_ProviderOptions_Is_Null()
    {
        Should.Throw<ArgumentNullException>(() => RedeliveryBudgetPolicy.Resolve(null, null!));
    }

    [Fact]
    public void BackoffFor_Clamps_Growth_To_MaxBackoff()
    {
        var policy = new RedeliveryBudgetPolicy(10, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));

        policy.BackoffFor(3).ShouldBe(TimeSpan.FromSeconds(4));
        policy.BackoffFor(4).ShouldBe(TimeSpan.FromSeconds(4));
        policy.BackoffFor(50).ShouldBe(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void BackoffFor_Grows_Without_Overflowing_When_No_Ceiling_Is_Set()
    {
        var policy = new RedeliveryBudgetPolicy(10, TimeSpan.FromDays(1), TimeSpan.Zero);

        policy.BackoffFor(int.MaxValue).ShouldBeGreaterThan(TimeSpan.FromDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BackoffFor_Returns_Zero_For_NonPositive_RetryNumbers(int retryNumber)
    {
        var policy = new RedeliveryBudgetPolicy(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        policy.BackoffFor(retryNumber).ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void BackoffFor_Returns_Zero_When_InitialBackoff_Is_Disabled()
    {
        var policy = new RedeliveryBudgetPolicy(3, TimeSpan.Zero, TimeSpan.FromSeconds(30));

        policy.BackoffFor(1).ShouldBe(TimeSpan.Zero);
        policy.BackoffFor(3).ShouldBe(TimeSpan.Zero);
    }
}
