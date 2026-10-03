using Centra.Events;
using Centra.PubSub;
using Centra.Sample.FlotillaSimulation.Contracts.Clients;
using Centra.Sample.FlotillaSimulation.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Centra.Sample.FlotillaSimulation.Consumer.Handlers;

[Topic("pubsub", "telemetry.v1", PrefetchCount = 20, MaxConcurrentCalls = 4)]
public sealed class TelemetryEventHandler : IEventHandler<TelemetryEvent>
{
    private readonly ITelemetryApiClient _apiClient;
    private readonly ILogger<TelemetryEventHandler> _logger;

    public TelemetryEventHandler(
        ITelemetryApiClient apiClient,
        ILogger<TelemetryEventHandler> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EventHandlingResult> HandleAsync(
        TelemetryEvent @event,
        EventContext context,
        CancellationToken cancellationToken = default)
    {
        var anomalyScore = Math.Round((@event.Temperature / 100.0 * 0.4) + (@event.Vibration / 10.0 * 0.6), 3);

        _logger.LogInformation(
            "[Consumer] Received Telemetry for device {DeviceId} from Flotilla Raft consensus stream (CloudEvent ID: {EventId}, CorrelationId: {CorrelationId}). Location: {Location}, Temp: {Temp:F1}°C, Pressure: {Pressure:F1} PSI, Vibration: {Vibration:F2} mm/s, Status: {Status}",
            @event.DeviceId,
            context.Id,
            context.CorrelationId ?? "N/A",
            @event.Location,
            @event.Temperature,
            @event.Pressure,
            @event.Vibration,
            @event.Status);

        var request = new TelemetryProcessRequest(
            DeviceId: @event.DeviceId,
            Status: @event.Status,
            AnomalyScore: anomalyScore,
            ProcessedAt: DateTimeOffset.UtcNow);

        try
        {
            _logger.LogInformation(
                "[Consumer] Invoking 'flotilla-api' service via Centra Service Invocation for device {DeviceId}...",
                @event.DeviceId);

            var response = await _apiClient.ProcessTelemetryAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Consumer] Centra Service Invocation SUCCESS for device {DeviceId}! Decision: {Decision}, TicketId: {TicketId}, Message: {Message}",
                response.DeviceId,
                response.Decision,
                string.IsNullOrWhiteSpace(response.IncidentTicketId) ? "None" : response.IncidentTicketId,
                response.Message);

            return EventHandlingResult.Success;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "[Consumer] Error during Centra Service Invocation for device {DeviceId}. Will be retried.",
                @event.DeviceId);

            return EventHandlingResult.Retry;
        }
    }
}
