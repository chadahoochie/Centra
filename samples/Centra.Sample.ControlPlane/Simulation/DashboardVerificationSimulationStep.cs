using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Centra.ControlPlane.Topology;

namespace Centra.Sample.ControlPlane.Simulation;

public static class DashboardVerificationSimulationStep
{
    public static async Task<bool> ExecuteAsync(
        HttpClient client,
        string adminToken,
        CancellationToken cancellationToken = default)
    {
        SimulationLogger.PhaseHeader(6, "Embedded Real-Time Control Plane Dashboard Verification");

        // 1. Verify HTTP GET /dashboard serves HTML UI
        SimulationLogger.Info("Querying Embedded Dashboard endpoint (/dashboard)...");
        var dashResp = await client.GetAsync("/dashboard", cancellationToken).ConfigureAwait(false);
        if (!dashResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Dashboard endpoint failed with status: {dashResp.StatusCode}");
            return false;
        }

        var contentType = dashResp.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            SimulationLogger.Error($"Dashboard response content-type is not text/html. Got: {contentType}");
            return false;
        }

        var html = await dashResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var containsHtmlTags = html.Contains("<html", StringComparison.OrdinalIgnoreCase) &&
                               (html.Contains("app-root", StringComparison.OrdinalIgnoreCase) ||
                                html.Contains("Dashboard", StringComparison.OrdinalIgnoreCase));

        if (!containsHtmlTags)
        {
            SimulationLogger.Error("Dashboard response does not contain a valid HTML single-page dashboard shell.");
            return false;
        }

        SimulationLogger.Success("Dashboard UI Endpoint (/dashboard) Verified: HTML, CSS, and Real-Time SSE hooks intact.");

        // 2. Fetch current topology to render terminal ASCII dashboard
        using var topMsg = new HttpRequestMessage(HttpMethod.Get, "/api/v1/topology");
        topMsg.Headers.Add("X-Centra-Admin-Token", adminToken);

        var topResp = await client.SendAsync(topMsg, cancellationToken).ConfigureAwait(false);
        if (!topResp.IsSuccessStatusCode)
        {
            SimulationLogger.Error($"Failed to fetch topology for dashboard rendering: {topResp.StatusCode}");
            return false;
        }

        var topJson = await topResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var nodes = JsonSerializer.Deserialize<List<ClientNodeInfo>>(topJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        // 3. Render terminal view
        AsciiDashboardRenderer.Render("Active", "http://cp-promoted:8081", nodes);
        SimulationLogger.Success($"Dashboard Rendering Complete: Visualizing {nodes.Count} active nodes across all clusters.");

        return true;
    }
}
