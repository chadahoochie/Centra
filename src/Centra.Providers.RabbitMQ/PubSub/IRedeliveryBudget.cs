namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Tracks per-message redelivery attempts so a consumer can bound its own retry loop. Brokers cannot do this:
/// RabbitMQ advances <c>x-delivery-count</c> only when a delivery is returned by consumer or channel failure,
/// never on an application <c>basic.nack(requeue=true)</c>.
/// </summary>
public interface IRedeliveryBudget
{
    /// <summary>
    /// Charges one failed delivery of <paramref name="key"/> against <paramref name="policy"/> and
    /// returns how the delivery should be settled. Implementations forget the message once its budget is spent.
    /// Only meaningful for an enabled policy; a subscription that opted out of the budget never charges it.
    /// </summary>
    RedeliveryDecision ChargeFailure(in RedeliveryBudgetKey key, in RedeliveryBudgetPolicy policy);

    /// <summary>
    /// Discards any attempt history for <paramref name="key"/>, called once a delivery is settled
    /// terminally so a later message reusing the id starts with a full budget.
    /// </summary>
    void Forget(in RedeliveryBudgetKey key);

    /// <summary>
    /// Discards the attempt history of every message tracked for <paramref name="queueName"/>, called when a
    /// subscription is torn down and its in-flight deliveries will never be settled by this consumer.
    /// </summary>
    void ForgetQueue(string queueName);
}
