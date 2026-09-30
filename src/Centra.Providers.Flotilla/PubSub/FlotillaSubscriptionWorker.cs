using Centra.Providers.Flotilla.Client;
using Centra.Providers.Flotilla.Protocol;
using Centra.PubSub;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Centra.Providers.Flotilla.PubSub;

/// <summary>
/// Background worker processing sequentially committed entries from the Flotilla Raft cluster
/// and dispatching them to registered topic event handlers.
/// </summary>
public sealed class FlotillaSubscriptionWorker : BackgroundService
{
    private readonly IFlotillaClient _client;
    private readonly FlotillaPubSubDriver _driver;
    private readonly ILogger<FlotillaSubscriptionWorker> _logger;

    public FlotillaSubscriptionWorker(
        IFlotillaClient client,
        FlotillaPubSubDriver driver,
        ILogger<FlotillaSubscriptionWorker>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _logger = logger ?? NullLogger<FlotillaSubscriptionWorker>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Flotilla subscription worker started, tailing commit stream");

        try
        {
            await foreach (var entry in _client.SubscribeCommitsAsync(stoppingToken).ConfigureAwait(false))
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    var (topic, metadata, payload) = FlotillaWireProtocol.DecodeMessage(entry.Data);
                    var subscriptions = _driver.GetSubscriptions(topic);

                    if (subscriptions.Count == 0)
                    {
                        continue;
                    }

                    foreach (var sub in subscriptions)
                    {
                        try
                        {
                            var result = await sub.Handler(payload, metadata, stoppingToken).ConfigureAwait(false);
                            if (result == EventHandlingResult.DeadLetter && !string.IsNullOrWhiteSpace(sub.DeadLetterTopic))
                            {
                                await _driver.PublishAsync(
                                    "default",
                                    sub.DeadLetterTopic,
                                    payload,
                                    metadata,
                                    stoppingToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogError(ex, "Error executing event handler for topic {Topic} at log index {LogIndex}", topic, entry.LogIndex);
                        }
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Failed to decode committed Flotilla entry at log index {LogIndex}", entry.LogIndex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Flotilla subscription worker stream");
        }

        _logger.LogInformation("Flotilla subscription worker stopped");
    }
}
