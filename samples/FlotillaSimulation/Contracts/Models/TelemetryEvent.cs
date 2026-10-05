using Centra.Events;

namespace Centra.Sample.FlotillaSimulation.Contracts.Models;

[EventContract("telemetry.v1", Version = "1")]
public sealed record TelemetryEvent(
    string DeviceId,
    string Location,
    double Temperature,
    double Pressure,
    double Vibration,
    string Status,
    DateTimeOffset Timestamp);
