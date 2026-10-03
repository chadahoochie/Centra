using Centra.Invocation;
using Centra.Sample.FlotillaSimulation.Contracts.Models;

namespace Centra.Sample.FlotillaSimulation.Contracts.Clients;

[ServiceClient("flotilla-api")]
public interface ITelemetryApiClient
{
    [ServiceMethod("telemetry/process", "POST")]
    Task<TelemetryProcessResponse> ProcessTelemetryAsync(TelemetryProcessRequest request, CancellationToken cancellationToken = default);
}
