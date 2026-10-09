using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.ControlPlane.Topology;

namespace Centra.Sample.ControlPlane.Simulation;

public static class RogueNodeDefenseSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        HttpClient primaryClient,
        string clusterId,
        string validToken,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(2, "Zero-Trust Dynamic Admission & Rogue Node Defense");

        // 1. Rogue Node: Missing Token
        SimulationLogger.Info($"Rogue node attempting unauthenticated heartbeat into '{clusterId}' (missing token)...");
        var unauthenticatedHeartbeat = new HeartbeatRequest("rogue-service", "rogue-inst-01", "Healthy", null, clusterId);
        using var missingTokenReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(unauthenticatedHeartbeat)
        };
        missingTokenReq.Headers.Add("X-Centra-Cluster-Id", clusterId);

        var missingTokenResp = await primaryClient.SendAsync(missingTokenReq, cancellationToken).ConfigureAwait(false);
        if (missingTokenResp.StatusCode != HttpStatusCode.Unauthorized)
        {
            SimulationLogger.Error($"Expected HTTP 401 Unauthorized for unauthenticated node, got: {missingTokenResp.StatusCode}");
            return false;
        }

        var missingErrJson = await missingTokenResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SimulationLogger.Success($"Admission Gate Rejected Unauthenticated Node (HTTP 401): {missingErrJson.Trim()}");

        // 2. Rogue Node: Forged / Invalid Token
        SimulationLogger.Info($"Rogue node attempting spoofed heartbeat into '{clusterId}' with invalid token...");
        using var badTokenReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(unauthenticatedHeartbeat)
        };
        badTokenReq.Headers.Add("X-Centra-Cluster-Id", clusterId);
        badTokenReq.Headers.Add("X-Centra-Cluster-Token", "fake-token-xyz-000");

        var badTokenResp = await primaryClient.SendAsync(badTokenReq, cancellationToken).ConfigureAwait(false);
        if (badTokenResp.StatusCode != HttpStatusCode.Unauthorized)
        {
            SimulationLogger.Error($"Expected HTTP 401 Unauthorized for spoofed node, got: {badTokenResp.StatusCode}");
            return false;
        }

        var badErrJson = await badTokenResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        SimulationLogger.Success($"Admission Gate Rejected Spoofed Token (HTTP 401): {badErrJson.Trim()}");

        // 3. Legitimate Node: Valid Cluster Token
        SimulationLogger.Info($"Legitimate node presenting authentic cluster token into '{clusterId}'...");
        var validHeartbeat = new HeartbeatRequest("order-service", "order-node-01", "Healthy", null, clusterId);
        using var validReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(validHeartbeat)
        };
        validReq.Headers.Add("X-Centra-Cluster-Id", clusterId);
        validReq.Headers.Add("X-Centra-Cluster-Token", validToken);

        var validResp = await primaryClient.SendAsync(validReq, cancellationToken).ConfigureAwait(false);
        if (!validResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Expected HTTP 200 OK for authentic node, got: {validResp.StatusCode}");
            return false;
        }

        SimulationLogger.Success($"Admission Gate Admitted Authorized Node (HTTP 200): Instance 'order-node-01' joined '{clusterId}'");
        return true;
    }
}
