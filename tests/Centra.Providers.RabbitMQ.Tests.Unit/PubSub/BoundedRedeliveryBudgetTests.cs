using Centra.Providers.RabbitMQ.PubSub;
using Centra.PubSub;
using Microsoft.Extensions.Time.Testing;
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

    [Fact]
    public void An_Entry_For_A_Message_That_Never_Comes_Back_Is_Reclaimed_Once_It_Goes_Stale()
    {
        var time = new FakeTimeProvider();
        var budget = new BoundedRedeliveryBudget(time);
        var abandoned = new RedeliveryBudgetKey(Queue, "evt-taken-by-another-replica");

        budget.ChargeFailure(abandoned, DefaultPolicy);
        budget.TrackedMessageCount.ShouldBe(1);

        // The requeued message is picked up by a different replica, or expires under x-message-ttl: this
        // consumer never settles it and never charges it again.
        time.Advance(DefaultPolicy.StaleAfter + TimeSpan.FromMinutes(1));
        budget.ChargeFailure(new RedeliveryBudgetKey(Queue, "evt-unrelated"), DefaultPolicy);

        budget.TrackedMessageCount.ShouldBe(1);
    }

    [Fact]
    public void A_Stale_Message_That_Does_Come_Back_Starts_A_Fresh_Budget()
    {
        var time = new FakeTimeProvider();
        var budget = new BoundedRedeliveryBudget(time);
        var key = new RedeliveryBudgetKey(Queue, "evt-1");

        budget.ChargeFailure(key, DefaultPolicy);
        budget.ChargeFailure(key, DefaultPolicy);

        time.Advance(DefaultPolicy.StaleAfter + TimeSpan.FromMinutes(1));

        budget.ChargeFailure(key, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_Live_Entry_Is_Never_Reclaimed_While_Its_Message_Could_Still_Come_Back()
    {
        var time = new FakeTimeProvider();
        var budget = new BoundedRedeliveryBudget(time);
        var live = new RedeliveryBudgetKey(Queue, "evt-still-retrying");

        budget.ChargeFailure(live, DefaultPolicy);
        budget.ChargeFailure(live, DefaultPolicy);

        // Time passes and a flood of other messages arrives, but never enough time for this one's next
        // redelivery to be impossible.
        time.Advance(DefaultPolicy.StaleAfter - TimeSpan.FromMinutes(1));
        for (var i = 0; i < 5_000; i++)
        {
            budget.ChargeFailure(new RedeliveryBudgetKey(Queue, $"evt-flood-{i}"), DefaultPolicy);
        }

        budget.ChargeFailure(live, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(4)));
        budget.ChargeFailure(live, DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);
    }

    [Fact]
    public void At_Capacity_A_New_Message_Is_DeadLettered_Rather_Than_Costing_A_Tracked_Message_Its_Count()
    {
        var time = new FakeTimeProvider();
        var budget = new BoundedRedeliveryBudget(time, maxTrackedMessages: 2);
        var first = new RedeliveryBudgetKey(Queue, "evt-1");
        var second = new RedeliveryBudgetKey(Queue, "evt-2");

        budget.ChargeFailure(first, DefaultPolicy);
        budget.ChargeFailure(second, DefaultPolicy);

        budget.ChargeFailure(new RedeliveryBudgetKey(Queue, "evt-3"), DefaultPolicy)
            .ShouldBe(RedeliveryDecision.DeadLetterImmediately);

        budget.ChargeFailure(first, DefaultPolicy)
            .ShouldBe(new RedeliveryDecision(EventHandlingResult.Retry, TimeSpan.FromSeconds(2)));
        budget.TrackedMessageCount.ShouldBe(2);
    }

    [Fact]
    public void Settling_A_Delivery_Releases_Its_Tracked_Entry()
    {
        var budget = new BoundedRedeliveryBudget();
        var key = new RedeliveryBudgetKey(Queue, "evt-1");

        budget.ChargeFailure(key, DefaultPolicy);
        budget.TrackedMessageCount.ShouldBe(1);

        budget.Forget(key);

        budget.TrackedMessageCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_Rejects_A_NonPositive_Capacity()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new BoundedRedeliveryBudget(maxTrackedMessages: 0));
    }
}
