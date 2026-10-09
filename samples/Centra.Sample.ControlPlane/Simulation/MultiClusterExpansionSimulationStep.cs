using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.ControlPlane.Topology;
using Centra.Sample.ControlPlane.Domain;

namespace Centra.Sample.ControlPlane.Simulation;

public static class MultiClusterExpansionSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        HttpClient primaryClient,
        IReadOnlyList<SimulatedClusterNode> fleet,
        string adminToken,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(3, "Multi-Cluster Fleet Expansion (N Heterogeneous Clusters)");

        SimulationLogger.Info($"Registering {fleet.Count} distributed nodes across {fleet.Select(static n => n.ClusterId).Distinct().Count()} independent clusters...");

        // 1. Send heartbeats for all nodes in the fleet
        foreach (var node in fleet)
        {
            var req = new HeartbeatRequest(node.AppId, node.InstanceId, node.Status, node.Metadata, node.ClusterId);
            using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/heartbeat")
            {
                Content = JsonContent.Create(req)
            };
            msg.Headers.Add("X-Centra-Cluster-Id", node.ClusterId);
            if (!string.IsNullOrWhiteSpace(node.Token))
            {
                msg.Headers.Add("X-Centra-Cluster-Token", node.Token);
            }

            var resp = await primaryClient.SendAsync(msg, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                SimulationLogger.Error($"Failed to register node [{node.InstanceId}] in cluster [{node.ClusterId}]: {resp.StatusCode}");
                return false;
            }
        }

        SimulationLogger.Success($"All {fleet.Count} nodes across all clusters successfully registered heartbeats.");

        // 2. Validate Scoped Topology Isolation for each cluster
        var clusters = fleet.Select(static n => (n.ClusterId, n.Token)).DistinctBy(static x => x.ClusterId).ToList();

        foreach (var (clusterId, token) in clusters)
        {
            var expectedNodes = fleet.Where(n => n.ClusterId == clusterId).ToList();
            SimulationLogger.Info($"Querying scoped topology for [{clusterId}] (Expect: {expectedNodes.Count} nodes)...");

            using var scopeReq = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/topology?clusterId={clusterId}");
            scopeReq.Headers.Add("X-Centra-Cluster-Id", clusterId);
            if (!string.IsNullOrWhiteSpace(token))
            {
                scopeReq.Headers.Add("X-Centra-Cluster-Token", token);
            }

            var scopeResp = await primaryClient.SendAsync(scopeReq, cancellationToken).ConfigureAwait(false);
            if (!scopeResp.IsSuccessStatusCode)
            {
                SimulationLogger.Error($"Scoped topology query failed for [{clusterId}]: {scopeResp.StatusCode}");
                return false;
            }

            var json = await scopeResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var nodes = JsonSerializer.Deserialize<List<ClientNodeInfo>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            if (nodes.Count != expectedNodes.Count)
            {
                SimulationLogger.Error($"Cluster [{clusterId}] node count mismatch. Expected: {expectedNodes.Count}, Got: {nodes.Count}");
                return false;
            }

            var hasForeignNodes = nodes.Any(n => !string.Equals(n.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
            if (hasForeignNodes)
            {
                SimulationLogger.Error($"Cluster [{clusterId}] contains foreign nodes from another cluster! Topology partition leak detected.");
                return false;
            }

            SimulationLogger.Success($"Cluster [{clusterId}] Topology Isolated: {nodes.Count} active nodes ({string.Join(", ", nodes.Select(static n => n.InstanceId))})");
        }

        // 3. Validate Global Multi-Cluster Topology via Admin / Operator View
        SimulationLogger.Info("Querying Global Fleet Topology via Operator Channel (/api/v1/topology)...");
        using var globalReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/topology");
        globalReq.Headers.Add("X-Centra-Admin-Token", adminToken);

        var globalResp = await primaryClient.SendAsync(globalReq, cancellationToken).ConfigureAwait(false);
        if (!globalResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Global topology query failed: {globalResp.StatusCode}");
            return false;
        }

        var globalJson = await globalResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var globalNodes = JsonSerializer.Deserialize<List<ClientNodeInfo>>(globalJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        if (globalNodes.Count != fleet.Count)
        {
            SimulationLogger.Error($"Global topology node count mismatch. Expected: {fleet.Count}, Got: {globalNodes.Count}");
            return false;
        }

        SimulationLogger.Success($"Global Operator View: {globalNodes.Count} total nodes across {clusters.Count} clusters verified without crosstalk.");
        return true;
    }
}
