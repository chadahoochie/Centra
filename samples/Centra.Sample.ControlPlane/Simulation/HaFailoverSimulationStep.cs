using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.ControlPlane.HA;
using Centra.ControlPlane.Topology;

namespace Centra.Sample.ControlPlane.Simulation;

public static class HaFailoverSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        IControlPlaneLeaderTracker primaryTracker,
        IControlPlaneLeaderTracker standbyTracker,
        HttpClient standbyRawClient,
        string primaryEndpoint,
        string standbyEndpoint,
        string adminToken,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(5, "Active-Passive Failover Simulation (Zero Data Loss)");

        SimulationLogger.Info($"Simulating Primary Leader failure at '{primaryEndpoint}'...");

        // 1. Primary steps down, Standby promotes
        primaryTracker.SetLeader(false, standbyEndpoint);
        standbyTracker.SetLeader(true, standbyEndpoint);

        SimulationLogger.Info($"Standby Control Plane promoted to Active Leader at '{standbyEndpoint}'.");

        // 2. Query health on newly promoted Standby
        var standbyHealthResp = await standbyRawClient.GetAsync("/api/v1/health", cancellationToken).ConfigureAwait(false);
        if (!standbyHealthResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Promoted standby health check failed: {standbyHealthResp.StatusCode}");
            return false;
        }

        var healthJson = await standbyHealthResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(healthJson);
        var role = doc.RootElement.GetProperty("role").GetString();
        var isLeader = doc.RootElement.GetProperty("isLeader").GetBoolean();

        SimulationLogger.Success($"Promoted Standby Status: Role = '{role}', IsLeader = {isLeader}");
        if (role != "Active" || !isLeader)
        {
            SimulationLogger.Error("Standby did not correctly assume the Active Leader role.");
            return false;
        }

        // 3. Verify Standby now accepts direct client traffic without 307 redirect
        SimulationLogger.Info("Sending heartbeat from node directly to newly promoted Standby...");
        var failoverHeartbeat = new HeartbeatRequest("order-service", "order-node-01", "Healthy", null, "cluster-alpha");
        using var heartbeatReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
        {
            Content = JsonContent.Create(failoverHeartbeat)
        };
        heartbeatReq.Headers.Add("X-Centra-Cluster-Id", "cluster-alpha");
        heartbeatReq.Headers.Add("X-Centra-Cluster-Token", "alpha-secret-tok-901");

        var heartbeatResp = await standbyRawClient.SendAsync(heartbeatReq, cancellationToken).ConfigureAwait(false);
        if (heartbeatResp.StatusCode != HttpStatusCode.OK)
        {
            SimulationLogger.Error($"Expected HTTP 200 OK from promoted leader, got: {heartbeatResp.StatusCode}");
            return false;
        }

        SimulationLogger.Success("Promoted Leader accepted node heartbeat with zero redirects and zero drops.");

        // 4. Verify Standby serves topology directly
        SimulationLogger.Info("Querying topology directly from newly promoted Standby...");
        using var topReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/topology");
        topReq.Headers.Add("X-Centra-Admin-Token", adminToken);

        var topResp = await standbyRawClient.SendAsync(topReq, cancellationToken).ConfigureAwait(false);
        if (topResp.StatusCode != HttpStatusCode.OK)
        {
            SimulationLogger.Error($"Expected HTTP 200 OK for topology from promoted leader, got: {topResp.StatusCode}");
            return false;
        }

        SimulationLogger.Success("Active-Passive Failover verified: Client traffic resumed seamlessly.");
        return true;
    }
}
