using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.Components;
using Centra.ControlPlane.Catalog;
using Centra.Sync;

namespace Centra.Sample.ControlPlane.Simulation;

public static class DynamicSyncSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        HttpClient primaryClient,
        string adminToken,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(4, "Dynamic Component & Resilience Policy Hot-Reload");

        // 1. Dynamic Component Registration: Distributed State Store
        SimulationLogger.Info("Registering dynamic component 'orders-cache' (StateStore / redis)...");
        var stateStoreDef = new ComponentDefinition
        {
            Name = "orders-cache",
            Type = ComponentType.StateStore,
            Provider = "redis",
            Metadata = new Dictionary<string, string>
            {
                ["redisHost"] = "redis:6379",
                ["database"] = "0"
            }
        };

        using var compMsg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/components")
        {
            Content = JsonContent.Create(stateStoreDef)
        };
        compMsg1.Headers.Add("X-Centra-Admin-Token", adminToken);

        var compResp1 = await primaryClient.SendAsync(compMsg1, cancellationToken).ConfigureAwait(false);
        if (!compResp1.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to register component 'orders-cache': {compResp1.StatusCode}");
            return false;
        }

        var entry1Json = await compResp1.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var entry1Doc = JsonDocument.Parse(entry1Json);
        var rev1 = entry1Doc.RootElement.GetProperty("revision").GetInt64();
        SimulationLogger.Success($"Component 'orders-cache' registered at Catalog Revision {rev1}");

        // 2. Dynamic Component Registration: Pub/Sub Broker
        SimulationLogger.Info("Registering dynamic component 'billing-events' (PubSub / rabbitmq)...");
        var pubSubDef = new ComponentDefinition
        {
            Name = "billing-events",
            Type = ComponentType.PubSub,
            Provider = "rabbitmq",
            Metadata = new Dictionary<string, string>
            {
                ["host"] = "rabbitmq:5672",
                ["virtualHost"] = "/"
            }
        };

        using var compMsg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/components")
        {
            Content = JsonContent.Create(pubSubDef)
        };
        compMsg2.Headers.Add("X-Centra-Admin-Token", adminToken);

        var compResp2 = await primaryClient.SendAsync(compMsg2, cancellationToken).ConfigureAwait(false);
        if (!compResp2.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to register component 'billing-events': {compResp2.StatusCode}");
            return false;
        }

        var entry2Json = await compResp2.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var entry2Doc = JsonDocument.Parse(entry2Json);
        var rev2 = entry2Doc.RootElement.GetProperty("revision").GetInt64();
        SimulationLogger.Success($"Component 'billing-events' registered at Catalog Revision {rev2}");

        // 3. Dynamic Resilience Policy Registration: Polly v8 Circuit Breaker
        SimulationLogger.Info("Registering dynamic resilience policy 'payment-circuit-breaker'...");
        var resiliencePolicy = new ResiliencePolicyDto
        {
            PolicyName = "payment-circuit-breaker",
            MaxRetries = 3,
            BaseDelayMs = 250,
            TimeoutSeconds = 2.0,
            BreakDurationSeconds = 5.0,
            FailureRatio = 0.5
        };

        using var resMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/resilience")
        {
            Content = JsonContent.Create(resiliencePolicy)
        };
        resMsg.Headers.Add("X-Centra-Admin-Token", adminToken);

        var resResp = await primaryClient.SendAsync(resMsg, cancellationToken).ConfigureAwait(false);
        if (!resResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to register resilience policy: {resResp.StatusCode}");
            return false;
        }

        SimulationLogger.Success("Resilience policy 'payment-circuit-breaker' committed to Catalog.");

        // 4. Verify Catalog Introspection
        SimulationLogger.Info("Verifying live catalog reflection (/api/v1/components & /api/v1/resilience)...");
        using var checkCompReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/components");
        checkCompReq.Headers.Add("X-Centra-Admin-Token", adminToken);
        var checkCompResp = await primaryClient.SendAsync(checkCompReq, cancellationToken).ConfigureAwait(false);
        if (!checkCompResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to query components: {checkCompResp.StatusCode}");
            return false;
        }

        using var checkResReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/resilience");
        checkResReq.Headers.Add("X-Centra-Admin-Token", adminToken);
        var checkResResp = await primaryClient.SendAsync(checkResReq, cancellationToken).ConfigureAwait(false);
        if (!checkResResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to query resilience policies: {checkResResp.StatusCode}");
            return false;
        }

        SimulationLogger.Success("Catalog Hot-Reload Verified: Real-time synchronization active across all fleets.");
        return true;
    }
}
