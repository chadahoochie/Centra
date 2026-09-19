using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Shouldly;
using Xunit;

namespace Centra.Providers.RabbitMQ.Tests.Unit.PubSub;

public sealed class BoundedRedeliveryBudgetTests
{
    private const string Queue = "centra.pubsub.orders.created";

    private static readonly RedeliveryBudgetPolicy DefaultPolicy =
        new(MaxRetryAttempts: 3, InitialBackoff: TimeSpan.FromSeconds(1), MaxBackoff: TimeSpan.FromSeconds(30));

    [Fact]
    public void ChargeFailure_Grants_Three_Retries_With_Doubling_Backoff_Then_DeadLetters()
    {
        var budget = new BoundedRedeliveryBudget();
        var key = new RedeliveryBudgetKey(Queue, "evt-1");

        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);
    }

    [Fact]
    public void ChargeFailure_Tracks_Each_Message_Independently()
    {
        var budget = new BoundedRedeliveryBudget();

        budget.ChargeFailure(new RedeliveryBudgetKey(Queue, "evt-1"), DefaultPolicy);
        budget.ChargeFailure(new RedeliveryBudgetKey(Queue, "evt-1"), DefaultPolicy);

        budget.ChargeFailure(new RedeliveryBudgetKey(Queue, "evt-2"), DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ChargeFailure_Tracks_The_Same_Message_Separately_Per_Subscription_Queue()
    {
        var budget = new BoundedRedeliveryBudget();
        var subscriptionA = new RedeliveryBudgetKey("centra.a.orders.created", "evt-1");
        var subscriptionB = new RedeliveryBudgetKey("centra.b.orders.created", "evt-1");

        // Both subscriptions receive the same event and both keep failing; interleaving their charges must
        // not let one spend the other's budget.
        budget.ChargeFailure(subscriptionA, DefaultPolicy);
        budget.ChargeFailure(subscriptionB, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
        budget.ChargeFailure(subscriptionA, DefaultPolicy);
        budget.ChargeFailure(subscriptionB, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
        budget.ChargeFailure(subscriptionA, DefaultPolicy);
        budget.ChargeFailure(subscriptionA, DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);

        // A settling on its own does not hand B a fresh budget mid-loop.
        budget.ChargeFailure(subscriptionB, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
        budget.ChargeFailure(subscriptionB, DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);
    }

    [Fact]
    public void Forget_On_One_Subscription_Leaves_Another_Subscriptions_Count_Intact()
    {
        var budget = new BoundedRedeliveryBudget();
        var subscriptionA = new RedeliveryBudgetKey("centra.a.orders.created", "evt-1");
        var subscriptionB = new RedeliveryBudgetKey("centra.b.orders.created", "evt-1");

        budget.ChargeFailure(subscriptionA, DefaultPolicy);
        budget.ChargeFailure(subscriptionB, DefaultPolicy);
        budget.ChargeFailure(subscriptionB, DefaultPolicy);

        budget.Forget(subscriptionA);

        budget.ChargeFailure(subscriptionB, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void ChargeFailure_Restores_The_Full_Budget_After_The_Budget_Is_Spent()
    {
        var budget = new BoundedRedeliveryBudget();
        var key = new RedeliveryBudgetKey(Queue, "evt-1");

        for (var i = 0; i < 4; i++)
        {
            budget.ChargeFailure(key, DefaultPolicy);
        }

        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ForgetQueue_Restores_The_Full_Budget_For_That_Queue_Only()
    {
        var budget = new BoundedRedeliveryBudget();
        var torndown = new RedeliveryBudgetKey("centra.a.orders.created", "evt-1");
        var surviving = new RedeliveryBudgetKey("centra.b.orders.created", "evt-1");

        budget.ChargeFailure(torndown, DefaultPolicy);
        budget.ChargeFailure(torndown, DefaultPolicy);
        budget.ChargeFailure(surviving, DefaultPolicy);
        budget.ChargeFailure(surviving, DefaultPolicy);

        budget.ForgetQueue(torndown.QueueName);

        budget.ChargeFailure(torndown, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
        budget.ChargeFailure(surviving, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void ForgetQueue_Rejects_A_Missing_QueueName()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.ForgetQueue(" "));
    }

    [Fact]
    public void Forget_Restores_The_Full_Budget_For_A_Reused_Message_Id()
    {
        var budget = new BoundedRedeliveryBudget();
        var key = new RedeliveryBudgetKey(Queue, "evt-1");
        budget.ChargeFailure(key, DefaultPolicy);
        budget.ChargeFailure(key, DefaultPolicy);

        budget.Forget(key);

        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ChargeFailure_Terminates_Every_Message_Under_A_Large_Poison_Backlog()
    {
        var budget = new BoundedRedeliveryBudget();
        const int backlog = 20_000;

        // Every message fails once before any of them is retried - the interleaving that a capacity-evicting
        // tracker resolved by discarding the oldest live counters, handing them a fresh budget forever.
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < backlog; i++)
            {
                budget.ChargeFailure(new RedeliveryBudgetKey(Queue, $"evt-{i}"), DefaultPolicy)
                    .Result.ShouldBe(EventHandlingResult.Retry);
            }
        }

        for (var i = 0; i < backlog; i++)
        {
            budget.ChargeFailure(new RedeliveryBudgetKey(Queue, $"evt-{i}"), DefaultPolicy)
                .ShouldBe(RedeliveryDecision.DeadLetterImmediately);
        }
    }

    [Fact]
    public void ChargeFailure_Rejects_A_Missing_MessageId()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.ChargeFailure(new RedeliveryBudgetKey(Queue, " "), DefaultPolicy));
    }

    [Fact]
    public void ChargeFailure_Rejects_A_Missing_QueueName()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.ChargeFailure(new RedeliveryBudgetKey(null!, "evt-1"), DefaultPolicy));
    }

    [Fact]
    public void Forget_Rejects_A_Missing_MessageId()
    {
        var budget = new BoundedRedeliveryBudget();

        Should.Throw<ArgumentException>(() => budget.Forget(new RedeliveryBudgetKey(Queue, null!)));
    }
}
