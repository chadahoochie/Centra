using Centra.PubSub;
using Centra.PubSub.Inbox;
using Centra.PubSub.Outbox;
using Centra.PubSub.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Centra;

/// <summary>
/// Extension methods for configuring Pub/Sub message handlers, outbox, and inbox on <see cref="ICentraBuilder"/>.
/// </summary>
public static class CentraBuilderPubSubExtensions
{
    /// <summary>
    /// Registers an event handler for a specific CloudEvent type and topic.
    /// </summary>
    public static ICentraBuilder AddEventHandler<THandler, TEvent>(
        this ICentraBuilder builder,
        string? pubSubName = null,
        string? topic = null,
        string? ruleFilter = null,
        int priority = 0)
        where THandler : class, IEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraEventHandler<THandler, TEvent>(
            pubSubName: pubSubName,
            topic: topic,
            ruleFilter: ruleFilter,
            priority: priority);
        return builder;
    }

    /// <summary>
    /// Adds transactional outbox publisher background service and repository.
    /// </summary>
    public static ICentraBuilder AddOutbox(
        this ICentraBuilder builder,
        Action<OutboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraOutbox(configure);
        return builder;
    }

    /// <summary>
    /// Adds idempotent inbox deduplication background cleanup and repository.
    /// </summary>
    public static ICentraBuilder AddInbox(
        this ICentraBuilder builder,
        Action<InboxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraInbox(configure);
        return builder;
    }

    /// <summary>
    /// Adds multi-tenant noisy neighbor offload coordinator and dynamic subscriber.
    /// </summary>
    public static ICentraBuilder AddTenantOffload(
        this ICentraBuilder builder,
        Action<TenantOffloadOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCentraTenantOffload(configure);
        return builder;
    }
}
