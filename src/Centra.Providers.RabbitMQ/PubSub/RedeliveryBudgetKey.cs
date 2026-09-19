namespace Centra.Providers.RabbitMQ.PubSub;

/// <summary>
/// Identifies the attempt counter for one message on one subscription. The queue name is part of the key
/// because a driver instance serves many subscriptions: the same event, fanned out to two queues, is two
/// independent retry loops, and charging both against one counter would let each subscription spend - or
/// reset - the other's budget.
/// </summary>
/// <param name="QueueName">The queue the delivery arrived on, which is the subscription's identity.</param>
/// <param name="MessageId">The identity the message keeps across redeliveries.</param>
public readonly record struct RedeliveryBudgetKey(string QueueName, string MessageId);
