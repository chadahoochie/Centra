namespace Centra.Sample.FlotillaSimulation.Contracts.Models;

public sealed record TelemetryProcessRequest(
    string DeviceId,
    string Status,
    double AnomalyScore,
    DateTimeOffset ProcessedAt);
