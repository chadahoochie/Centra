using Centra.PubSub;
using Centra.Sample.RabbitSimulation.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.RabbitSimulation.Producer.Services;

public sealed class OrderProducerHostedService : BackgroundService
{
    private readonly IPubSubClient _pubSubClient;
    private readonly BogusOrderGenerator _generator;
    private readonly TimeSpan _interval;
    private readonly ILogger<OrderProducerHostedService> _logger;

    public OrderProducerHostedService(
        IPubSubClient pubSubClient,
        BogusOrderGenerator generator,
        IConfiguration configuration,
        ILogger<OrderProducerHostedService> logger)
    {
        _pubSubClient = pubSubClient ?? throw new ArgumentNullException(nameof(pubSubClient));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var intervalMs = configuration.GetValue("Producer:IntervalMilliseconds", 1000);
        _interval = TimeSpan.FromMilliseconds(Math.Max(250, intervalMs));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[Producer] Starting Bogus Order Producer background service (cadence: {IntervalMs}ms / 1 msg/s)...",
            _interval.TotalMilliseconds);

        // Allow broker connection and consumer bindings to stabilize at startup
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var order = _generator.Generate();
                var total = order.Quantity * order.UnitPrice;

                _logger.LogInformation(
                    "[Producer] Publishing Bogus Order {OrderId} for customer '{CustomerName}' ({Quantity}x {Item} @ ${UnitPrice:F2} = ${Total:F2}) to topic 'orders.new'...",
                    order.OrderId,
                    order.CustomerName,
                    order.Quantity,
                    order.ItemDescription,
                    order.UnitPrice,
                    total);

                await _pubSubClient.PublishAsync("orders.new", order, cancellationToken: stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("[Producer] Successfully published Order {OrderId} to RabbitMQ.", order.OrderId);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[Producer] Error publishing bogus order to RabbitMQ. Will retry next turn.");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("[Producer] Bogus Order Producer background service stopped.");
    }
}
