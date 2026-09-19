using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class BoundedRedeliveryBudgetTests
{
    private static readonly RedeliveryBudgetPolicy DefaultPolicy =
        new(MaxRetryAttempts: 3, InitialBackoff: TimeSpan.FromSeconds(1), MaxBackoff: TimeSpan.FromSeconds(30));

    [Fact]
    public void ChargeFailure_Grants_Three_Retries_With_Doubling_Backoff_Then_DeadLetters()
    {
        var budget = new BoundedRedeliveryBudget();

        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);
    }

    [Fact]
    public void ChargeFailure_Tracks_Each_Message_Independently()
    {
        var budget = new BoundedRedeliveryBudget();

        budget.ChargeFailure("evt-1", DefaultPolicy);
        budget.ChargeFailure("evt-1", DefaultPolicy);

        budget.ChargeFailure("evt-2", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ChargeFailure_Restores_The_Full_Budget_After_The_Budget_Is_Spent()
    {
        var budget = new BoundedRedeliveryBudget();

        for (var i = 0; i < 4; i++)
        {
            budget.ChargeFailure("evt-1", DefaultPolicy);
        }

        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ChargeFailure_DeadLetters_Immediately_When_The_Budget_Is_Zero()
    {
        var budget = new BoundedRedeliveryBudget();
        var noRetries = DefaultPolicy with { MaxRetryAttempts = 0 };

        budget.ChargeFailure("evt-1", noRetries).ShouldBe(RedeliveryDecision.DeadLetterImmediately);
        budget.ChargeFailure("evt-1", noRetries).ShouldBe(RedeliveryDecision.DeadLetterImmediately);
    }

    [Fact]
    public void Forget_Restores_The_Full_Budget_For_A_Reused_Message_Id()
    {
        var budget = new BoundedRedeliveryBudget();
        budget.ChargeFailure("evt-1", DefaultPolicy);
        budget.ChargeFailure("evt-1", DefaultPolicy);

        budget.Forget("evt-1");

        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ChargeFailure_Evicts_The_Oldest_Entries_Once_Capacity_Is_Exceeded()
    {
        var budget = new BoundedRedeliveryBudget(capacity: 2);

        budget.ChargeFailure("evt-1", DefaultPolicy);
        budget.ChargeFailure("evt-1", DefaultPolicy);
        budget.ChargeFailure("evt-2", DefaultPolicy);
        budget.ChargeFailure("evt-3", DefaultPolicy);

        // evt-1 was the oldest tracked message, so its attempt history is the first discarded.
        budget.ChargeFailure("evt-1", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
        budget.ChargeFailure("evt-3", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void ChargeFailure_Keeps_Tracking_Bounded_Under_A_Flood_Of_Distinct_Messages()
    {
        var budget = new BoundedRedeliveryBudget(capacity: 8);

        for (var i = 0; i < 5_000; i++)
        {
            budget.ChargeFailure($"evt-{i}", DefaultPolicy);
        }

        // Nothing observable leaked: the most recent message still has its full budget accounted for.
        budget.ChargeFailure("evt-4999", DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Rejects_NonPositive_Capacity(int capacity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundedRedeliveryBudget(capacity));
    }

    [Fact]
    public void ChargeFailure_Rejects_A_Missing_MessageId()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.ChargeFailure(" ", DefaultPolicy));
    }

    [Fact]
    public void Forget_Rejects_A_Missing_MessageId()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.Forget(null!));
    }
}
