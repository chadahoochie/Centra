using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Centra.Sample.ControlPlane.Simulation;

public static class HaRoutingSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        HttpClient primaryClient,
        HttpClient standbyRawClient,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(1, "High Availability (HA) Active-Passive Clustering & Routing");

        // 1. Health check on primary node
        SimulationLogger.Info("Querying Primary Control Plane health (/api/v1/health)...");
        var primaryHealthResponse = await primaryClient.GetAsync("/api/v1/health", cancellationToken).ConfigureAwait(false);
        if (!primaryHealthResponse.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Primary health check failed with status: {primaryHealthResponse.StatusCode}");
            return false;
        }

        var primaryHealthJson = await primaryHealthResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var primaryDoc = JsonDocument.Parse(primaryHealthJson);
        var primaryRole = primaryDoc.RootElement.GetProperty("role").GetString();
        var primaryIsLeader = primaryDoc.RootElement.GetProperty("isLeader").GetBoolean();

        SimulationLogger.Success($"Primary Node: Role = '{primaryRole}', IsLeader = {primaryIsLeader}");
        if (primaryRole != "Active" || !primaryIsLeader)
        {
            SimulationLogger.Error("Primary node is not reporting as Active Leader.");
            return false;
        }

        // 2. Health check on standby node
        SimulationLogger.Info("Querying Standby Control Plane health (/api/v1/health)...");
        var standbyHealthResponse = await standbyRawClient.GetAsync("/api/v1/health", cancellationToken).ConfigureAwait(false);
        if (!standbyHealthResponse.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Standby health check failed with status: {standbyHealthResponse.StatusCode}");
            return false;
        }

        var standbyHealthJson = await standbyHealthResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var standbyDoc = JsonDocument.Parse(standbyHealthJson);
        var standbyRole = standbyDoc.RootElement.GetProperty("role").GetString();
        var standbyIsLeader = standbyDoc.RootElement.GetProperty("isLeader").GetBoolean();
        var standbyLeader = standbyDoc.RootElement.TryGetProperty("leaderEndpoint", out var lp) ? lp.GetString() : null;

        SimulationLogger.Success($"Standby Node: Role = '{standbyRole}', IsLeader = {standbyIsLeader}, ActiveLeader = '{standbyLeader}'");
        if (standbyRole != "Standby" || standbyIsLeader)
        {
            SimulationLogger.Error("Standby node is not reporting as Standby replica.");
            return false;
        }

        // 3. Traffic to standby triggers 307 Temporary Redirect pointing to primary
        SimulationLogger.Info("Sending request directly to Standby node endpoint (/api/v1/topology)...");
        var redirectResponse = await standbyRawClient.GetAsync("/api/v1/topology", cancellationToken).ConfigureAwait(false);

        if (redirectResponse.StatusCode != HttpStatusCode.TemporaryRedirect)
        {
            SimulationLogger.Error($"Expected HTTP 307 Temporary Redirect from Standby node, got: {redirectResponse.StatusCode}");
            return false;
        }

        var targetLocation = redirectResponse.Headers.Location?.ToString();
        var reportedRole = redirectResponse.Headers.Contains("X-Centra-Role")
            ? string.Join(", ", redirectResponse.Headers.GetValues("X-Centra-Role"))
            : "Unknown";
        var reportedLeader = redirectResponse.Headers.Contains("X-Centra-Leader")
            ? string.Join(", ", redirectResponse.Headers.GetValues("X-Centra-Leader"))
            : "Unknown";

        SimulationLogger.Success($"Standby emitted HTTP 307: Location = '{targetLocation}', X-Centra-Role = '{reportedRole}', X-Centra-Leader = '{reportedLeader}'");

        return true;
    }
}
