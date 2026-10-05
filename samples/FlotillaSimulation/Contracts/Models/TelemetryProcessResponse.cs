namespace Centra.Sample.FlotillaSimulation.Contracts.Models;

public sealed record TelemetryProcessResponse(
    string DeviceId,
    string Decision,
    string IncidentTicketId,
    DateTimeOffset ResolvedAt,
    string Message);
