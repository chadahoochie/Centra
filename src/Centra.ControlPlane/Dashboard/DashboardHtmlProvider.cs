namespace Centra.ControlPlane.Dashboard;

public static class DashboardHtmlProvider
{
    public static string GetDashboardHtml(string title)
    {
        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}}</title>
            <style>
                :root {
                    --bg-primary: #0f172a;
                    --bg-card: #1e293b;
                    --text-primary: #f8fafc;
                    --text-muted: #94a3b8;
                    --accent: #38bdf8;
                    --accent-success: #34d399;
                    --accent-warning: #fbbf24;
                    --border: #334155;
                }
                body {
                    margin: 0;
                    padding: 0;
                    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
                    background-color: var(--bg-primary);
                    color: var(--text-primary);
                }
                header {
                    padding: 1.5rem 2rem;
                    border-bottom: 1px solid var(--border);
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                }
                h1 { margin: 0; font-size: 1.5rem; display: flex; align-items: center; gap: 0.75rem; }
                .badge {
                    font-size: 0.75rem;
                    padding: 0.25rem 0.6rem;
                    border-radius: 9999px;
                    background: #0284c7;
                    color: white;
                    text-transform: uppercase;
                    letter-spacing: 0.05em;
                }
                main { padding: 2rem; max-width: 1400px; margin: 0 auto; }
                .stats-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
                    gap: 1.25rem;
                    margin-bottom: 2rem;
                }
                .card {
                    background: var(--bg-card);
                    border: 1px solid var(--border);
                    border-radius: 0.75rem;
                    padding: 1.25rem;
                }
                .card-title { font-size: 0.875rem; color: var(--text-muted); text-transform: uppercase; margin-bottom: 0.5rem; }
                .card-value { font-size: 1.75rem; font-weight: bold; color: var(--accent); }
                .node-table { width: 100%; border-collapse: collapse; margin-top: 1rem; }
                .node-table th, .node-table td {
                    padding: 0.75rem 1rem;
                    text-align: left;
                    border-bottom: 1px solid var(--border);
                }
                .node-table th { color: var(--text-muted); font-size: 0.85rem; }
                .status-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 6px; }
                .status-healthy { background: var(--accent-success); }
            </style>
        </head>
        <body>
            <header>
                <h1><span>⚡</span> {{title}} <span class="badge" id="role-badge">Leader</span></h1>
                <div id="health-info" style="color: var(--text-muted); font-size: 0.9rem;">Connecting...</div>
            </header>
            <main>
                <div class="stats-grid">
                    <div class="card">
                        <div class="card-title">Cluster Role</div>
                        <div class="card-value" id="val-role">Active</div>
                    </div>
                    <div class="card">
                        <div class="card-title">Active Clusters</div>
                        <div class="card-value" id="val-clusters">1</div>
                    </div>
                    <div class="card">
                        <div class="card-title">Connected Nodes</div>
                        <div class="card-value" id="val-nodes">0</div>
                    </div>
                    <div class="card">
                        <div class="card-title">Status</div>
                        <div class="card-value" style="color: var(--accent-success);" id="val-status">Healthy</div>
                    </div>
                </div>

                <div class="card">
                    <h2 style="margin-top:0; font-size: 1.25rem;">Multi-Cluster Node Topology</h2>
                    <table class="node-table">
                        <thead>
                            <tr>
                                <th>Cluster ID</th>
                                <th>App ID</th>
                                <th>Instance ID</th>
                                <th>Status</th>
                                <th>Last Heartbeat</th>
                            </tr>
                        </thead>
                        <tbody id="nodes-body">
                            <tr><td colspan="5" style="text-align:center; color: var(--text-muted);">Loading topology...</td></tr>
                        </tbody>
                    </table>
                </div>
            </main>
            <script>
                async function updateDashboard() {
                    try {
                        const hRes = await fetch('/api/v1/health');
                        if (hRes.ok) {
                            const health = await hRes.json();
                            document.getElementById('val-role').innerText = health.role || 'Active';
                            document.getElementById('role-badge').innerText = health.role || 'Active';
                            document.getElementById('health-info').innerText = 'Last updated: ' + new Date().toLocaleTimeString();
                        }

                        const tRes = await fetch('/api/v1/topology');
                        if (tRes.ok) {
                            const nodes = await tRes.json();
                            document.getElementById('val-nodes').innerText = nodes.length;
                            const clusters = new Set(nodes.map(n => n.clusterId || 'default'));
                            document.getElementById('val-clusters').innerText = clusters.size || 1;

                            const tbody = document.getElementById('nodes-body');
                            if (nodes.length === 0) {
                                tbody.innerHTML = '<tr><td colspan="5" style="text-align:center; color: var(--text-muted);">No active nodes detected</td></tr>';
                            } else {
                                tbody.innerHTML = nodes.map(n => `
                                    <tr>
                                        <td><strong>${n.clusterId || 'default'}</strong></td>
                                        <td>${n.appId}</td>
                                        <td><code>${n.instanceId}</code></td>
                                        <td><span class="status-dot status-healthy"></span>${n.status}</td>
                                        <td>${new Date(n.lastHeartbeatUtc).toLocaleTimeString()}</td>
                                    </tr>
                                `).join('');
                            }
                        }
                    } catch (e) {
                        console.error('Failed to update dashboard:', e);
                    }
                }
                updateDashboard();
                setInterval(updateDashboard, 3000);
            </script>
        </body>
        </html>
        """;
    }
}
