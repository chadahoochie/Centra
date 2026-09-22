using Centra.Events;
using Centra.PubSub.Routing;
using Centra.PubSub;
using Centra.PubSub.Inbox;
using Centra.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Centra.PubSub.HostedServices;

internal static class CentraSubscriptionPipelineExecutor
{
    public static async ValueTask<EventHandlingResult> ExecuteAsync(
        IServiceProvider serviceProvider,
        CentraTopicRegistration reg,
        ReadOnlyMemory<byte> payload,
        IReadOnlyDictionary<string, string> headers,
        string? causeId,
        CancellationToken cancellationToken)
    {
        var inboxStore = serviceProvider.GetService<IInboxStore>();
        var messageId = causeId ?? (headers.TryGetValue(CloudEventConstants.IdHeader, out var id) ? id : null);
        var consumerId = reg.HandlerType.FullName ?? reg.HandlerType.Name;

        if (inboxStore is not null && !string.IsNullOrWhiteSpace(messageId))
        {
            if (await inboxStore.HasBeenProcessedAsync(messageId, consumerId, cancellationToken).ConfigureAwait(false))
            {
                Diagnostics.CentraMeters.RecordPubSubConsumed(reg.PubSubName, reg.Topic, "duplicate_skipped", 0);
                return EventHandlingResult.Success;
            }
        }

        using var scope = serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService(reg.HandlerType);
        if (handler is null)
        {
            return EventHandlingResult.Drop;
        }

        var resilienceProvider = serviceProvider.GetService<IResiliencePipelineProvider>();
        EventHandlingResult result;
        if (resilienceProvider is not null)
        {
            var pipeline = resilienceProvider.GetPubSubPipeline(reg.PubSubName);
            result = await pipeline.ExecuteAsync(async ct =>
            {
                return await reg.Invoker(handler, payload, headers, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            result = await reg.Invoker(handler, payload, headers, cancellationToken).ConfigureAwait(false);
        }

        if (inboxStore is not null && !string.IsNullOrWhiteSpace(messageId) && result == EventHandlingResult.Success)
        {
            await inboxStore.MarkProcessedAsync(messageId, consumerId, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
