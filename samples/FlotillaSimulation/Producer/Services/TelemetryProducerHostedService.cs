using Centra.PubSub;
using Centra.Sample.FlotillaSimulation.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.FlotillaSimulation.Producer.Services;

public sealed class TelemetryProducerHostedService : BackgroundService
{
    private readonly IPubSubClient _pubSubClient;
    private readonly BogusTelemetryGenerator _generator;
    private readonly TimeSpan _interval;
    private readonly ILogger<TelemetryProducerHostedService> _logger;

    public TelemetryProducerHostedService(
        IPubSubClient pubSubClient,
        BogusTelemetryGenerator generator,
        IConfiguration configuration,
        ILogger<TelemetryProducerHostedService> logger)
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
            "[Producer] Starting Bogus Telemetry Producer background service (cadence: {IntervalMs}ms / 1 msg/s)...",
            _interval.TotalMilliseconds);

        // Allow Flotilla consensus server and subscriber connections to initialize
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
                var telemetry = _generator.Generate();

                _logger.LogInformation(
                    "[Producer] Proposing Telemetry for device {DeviceId} (Temp: {Temp:F1}°C, Press: {Press:F1} PSI, Vib: {Vib:F2} mm/s, Status: {Status}) to topic 'telemetry.v1' via Flotilla...",
                    telemetry.DeviceId,
                    telemetry.Temperature,
                    telemetry.Pressure,
                    telemetry.Vibration,
                    telemetry.Status);

                await _pubSubClient.PublishAsync("telemetry.v1", telemetry, cancellationToken: stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("[Producer] Successfully committed Telemetry for device {DeviceId} to Flotilla Raft log.", telemetry.DeviceId);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "[Producer] Error proposing telemetry to Flotilla. Retrying on next turn.");
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

        _logger.LogInformation("[Producer] Bogus Telemetry Producer background service stopped.");
    }
}
