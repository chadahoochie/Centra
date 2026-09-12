using Centra.PubSub.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Centra.Hosting.HostedServices;

/// <summary>
/// Background service that continuously drains pending messages from the transactional outbox
/// and dispatches them to configured message brokers.
/// </summary>
public sealed class CentraOutboxHostedService : BackgroundService
{
    private readonly OutboxProcessor _processor;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CentraOutboxHostedService> _logger;

    public CentraOutboxHostedService(
        OutboxProcessor processor,
        IOptions<OutboxOptions>? options = null,
        TimeProvider? timeProvider = null,
        ILogger<CentraOutboxHostedService>? logger = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _options = options?.Value ?? new OutboxOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CentraOutboxHostedService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Centra Outbox processor started with polling interval {PollingInterval}", _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred during outbox message processing loop");
            }

            try
            {
                await Task.Delay(_options.PollingInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Centra Outbox processor stopped.");
    }
}
