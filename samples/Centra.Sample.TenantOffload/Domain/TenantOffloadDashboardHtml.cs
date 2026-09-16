namespace Centra.Sample.TenantOffload.Domain;

public static class TenantOffloadDashboardHtml
{
    public static string Render(ITenantConsumerNodeState nodeState)
    {
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Centra - Dynamic Noisy Neighbor Tenant Offloading</title>
    <style>
        :root {
            --bg-primary: #0b0f19;
            --bg-secondary: #131b2e;
            --bg-card: #1c2744;
            --accent: #38bdf8;
            --accent-glow: rgba(56, 189, 248, 0.25);
            --text: #f1f5f9;
            --text-muted: #94a3b8;
            --border: #2e3d64;
            --success: #34d399;
            --warning: #fbbf24;
            --danger: #f87171;
            --radius: 10px;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
        body { background: var(--bg-primary); color: var(--text); padding: 2rem; line-height: 1.5; }
        .container { max-width: 1100px; margin: 0 auto; }
        header { margin-bottom: 2rem; border-bottom: 1px solid var(--border); padding-bottom: 1.5rem; display: flex; justify-content: space-between; align-items: center; }
        h1 { font-size: 1.8rem; font-weight: 700; color: #fff; }
        h1 span { color: var(--accent); }
        .badge { display: inline-flex; align-items: center; gap: 0.5rem; padding: 0.35rem 0.75rem; border-radius: 9999px; font-size: 0.85rem; font-weight: 600; background: var(--bg-secondary); border: 1px solid var(--border); }
        .badge-online { background: rgba(52, 211, 153, 0.15); border-color: var(--success); color: var(--success); }
        .grid { display: grid; grid-template-columns: 2fr 1fr; gap: 1.5rem; margin-bottom: 1.5rem; }
        @media (max-width: 850px) { .grid { grid-template-columns: 1fr; } }
        .card { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: var(--radius); padding: 1.5rem; box-shadow: 0 4px 20px rgba(0,0,0,0.3); }
        .card h2 { font-size: 1.2rem; font-weight: 600; margin-bottom: 1rem; display: flex; align-items: center; gap: 0.5rem; color: #fff; }
        .btn-group { display: flex; flex-direction: column; gap: 0.75rem; }
        button, .btn { display: inline-flex; align-items: center; justify-content: center; gap: 0.5rem; padding: 0.75rem 1.25rem; font-size: 0.95rem; font-weight: 600; border-radius: 6px; border: none; cursor: pointer; transition: all 0.15s ease; text-decoration: none; }
        .btn-primary { background: #2563eb; color: #fff; }
        .btn-primary:hover { background: #1d4ed8; }
        .btn-surge { background: #e11d48; color: #fff; }
        .btn-surge:hover { background: #be123c; }
        .btn-honest { background: #059669; color: #fff; }
        .btn-honest:hover { background: #047857; }
        .info-box { background: var(--bg-card); border-left: 4px solid var(--accent); padding: 1rem; border-radius: 4px; margin-bottom: 1rem; font-size: 0.9rem; color: var(--text-muted); }
        .info-box strong { color: #fff; }
        .queue-tag { font-family: monospace; background: rgba(0,0,0,0.3); padding: 0.15rem 0.4rem; border-radius: 4px; font-size: 0.85rem; border: 1px solid var(--border); }
        table { width: 100%; border-collapse: collapse; margin-top: 0.5rem; }
        th, td { text-align: left; padding: 0.75rem; border-bottom: 1px solid var(--border); font-size: 0.9rem; }
        th { color: var(--text-muted); font-size: 0.8rem; text-transform: uppercase; letter-spacing: 0.05em; }
        .status-offloaded { color: var(--danger); font-weight: 700; }
        .status-normal { color: var(--success); font-weight: 600; }
        pre { background: #070a12; border: 1px solid var(--border); border-radius: 6px; padding: 1rem; font-family: monospace; font-size: 0.85rem; overflow-x: auto; color: #cbd5e1; max-height: 250px; }
        .links { display: flex; gap: 1rem; margin-top: 1rem; }
        .links a { color: var(--accent); text-decoration: none; font-size: 0.85rem; }
        .links a:hover { text-decoration: underline; }
    </style>
</head>
<body>
<div class="container">
    <header>
        <div>
            <h1>Centra <span>Tenant Offload</span></h1>
            <p style="color: var(--text-muted); font-size: 0.9rem; margin-top: 0.25rem;">Dynamic Noisy Neighbor Isolation & Fair Scheduling Dashboard</p>
        </div>
        <div style="display: flex; gap: 0.5rem; align-items: center;">
            <span class="badge badge-online">● {{nodeState.BrokerType}} Connected</span>
            <span class="badge">Node: {{nodeState.InstanceId}}</span>
        </div>
    </header>

    <div class="info-box">
        <strong>💡 Understanding RabbitMQ Queues in this Sample:</strong><br>
        • <span class="queue-tag">centra.pubsub.tenant.orders</span> + <span class="queue-tag">...offload.0-3</span>: These 5 queues are <strong>static partition shards</strong> created at application startup by Centra's attribute scanner.<br>
        • <span class="queue-tag">centra.pubsub.tenant.orders.offload.tenant-mega</span>: This is the <strong>dynamic ephemeral queue</strong>! It does not exist at startup. It is spun up dynamically by the framework <em>only</em> when a tenant surge is detected, and automatically reaped by the background cleaner after cooldown.
    </div>

    <div class="grid">
        <div class="card">
            <h2>⚡ Interactive Actions</h2>
            <div class="btn-group">
                <button class="btn-primary" onclick="runSim(15)">
                    🚀 Run 6-Step Simulation (Hold 15s for RabbitMQ Inspection)
                </button>
                <button class="btn-surge" onclick="triggerSurge()">
                    🔥 Trigger Noisy Neighbor Burst (60 orders from tenant-mega + 10 honest)
                </button>
                <button class="btn-honest" onclick="sendHonest()">
                    ✅ Send 5 Honest Orders (tenant-alpha)
                </button>
            </div>

            <div style="margin-top: 1.5rem;">
                <h3 style="font-size: 1rem; margin-bottom: 0.5rem; color: #fff;">Simulation Console Output</h3>
                <pre id="consoleLog">Ready. Click one of the action buttons above to trigger live offload activity...</pre>
            </div>
        </div>

        <div class="card">
            <h2>📊 Node Handled Totals</h2>
            <div style="margin-bottom: 1rem;">
                <p style="font-size: 0.85rem; color: var(--text-muted);">Orders processed by this replica:</p>
                <div style="font-size: 2.5rem; font-weight: 800; color: var(--accent); margin: 0.25rem 0;" id="totalHandled">{{nodeState.TotalHandled}}</div>
            </div>
            <h3 style="font-size: 0.95rem; margin-bottom: 0.5rem; color: #fff;">Handled Counts by Tenant</h3>
            <table id="handledTable">
                <thead>
                    <tr>
                        <th>Tenant</th>
                        <th>Handled</th>
                    </tr>
                </thead>
                <tbody>
                    <tr><td colspan="2" style="color: var(--text-muted);">Loading...</td></tr>
                </tbody>
            </table>
            <div class="links">
                <a href="/tenants" target="_blank">View /tenants JSON</a>
                <a href="/instance" target="_blank">View /instance JSON</a>
            </div>
        </div>
    </div>

    <div class="card">
        <h2>📈 Real-Time Tenant Sliding-Window Metrics (/tenants)</h2>
        <table id="metricsTable">
            <thead>
                <tr>
                    <th>Tenant ID</th>
                    <th>Message Count</th>
                    <th>Traffic Share</th>
                    <th>Avg Latency</th>
                    <th>State</th>
                    <th>Reason</th>
                </tr>
            </thead>
            <tbody>
                <tr><td colspan="6" style="color: var(--text-muted);">Loading live window metrics...</td></tr>
            </tbody>
        </table>
    </div>
</div>

<script>
    async function refreshData() {
        try {
            const instRes = await fetch('/instance');
            if (instRes.ok) {
                const inst = await instRes.json();
                document.getElementById('totalHandled').innerText = inst.totalHandled || 0;
                const tbody = document.querySelector('#handledTable tbody');
                tbody.innerHTML = '';
                const counts = inst.tenantHandledCounts || {};
                const keys = Object.keys(counts);
                if (keys.length === 0) {
                    tbody.innerHTML = '<tr><td colspan="2" style="color: var(--text-muted);">No orders handled yet</td></tr>';
                } else {
                    for (const t of keys) {
                        tbody.innerHTML += `<tr><td><strong>${t}</strong></td><td>${counts[t]}</td></tr>`;
                    }
                }
            }

            const tenRes = await fetch('/tenants');
            if (tenRes.ok) {
                const tenants = await tenRes.json();
                const tbody = document.querySelector('#metricsTable tbody');
                tbody.innerHTML = '';
                for (const t of tenants) {
                    const sharePct = (t.trafficShareRatio * 100).toFixed(1) + '%';
                    const latency = t.averageDurationMs.toFixed(2) + ' ms';
                    const stateClass = t.isOffloaded ? 'status-offloaded' : 'status-normal';
                    const stateText = t.isOffloaded ? 'OFFLOADED' : 'Normal';
                    tbody.innerHTML += `<tr>
                        <td><strong>${t.tenantId}</strong></td>
                        <td>${t.messageCount}</td>
                        <td>${sharePct}</td>
                        <td>${latency}</td>
                        <td class="${stateClass}">${stateText}</td>
                        <td>${t.offloadReason || 'None'}</td>
                    </tr>`;
                }
            }
        } catch (e) {
            console.error('Failed refreshing stats:', e);
        }
    }

    async function runSim(holdSeconds) {
        const log = document.getElementById('consoleLog');
        log.innerText = `Starting 6-step simulation with ${holdSeconds}s hold on ephemeral broker queue...\nCheck your RabbitMQ Management UI during Step 4 to see 'centra.pubsub.tenant.orders.offload.tenant-mega' appear!\nWaiting for result...`;
        try {
            const res = await fetch(`/simulate?hold=${holdSeconds}`, { method: 'POST' });
            const data = await res.json();
            log.innerText = JSON.stringify(data, null, 2);
            refreshData();
        } catch (e) {
            log.innerText = 'Simulation call failed: ' + e;
        }
    }

    async function triggerSurge() {
        const log = document.getElementById('consoleLog');
        log.innerText = 'Dispatching burst of 60 orders for tenant-mega and 10 orders for tenant-alpha...';
        try {
            const res = await fetch('/orders/batch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ noisyCount: 60, honestCount: 10 })
            });
            const data = await res.json();
            log.innerText = 'Surge Dispatched:\n' + JSON.stringify(data, null, 2);
            refreshData();
        } catch (e) {
            log.innerText = 'Surge dispatch failed: ' + e;
        }
    }

    async function sendHonest() {
        const log = document.getElementById('consoleLog');
        log.innerText = 'Sending 5 honest orders for tenant-alpha...';
        try {
            const res = await fetch('/orders/batch', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ noisyCount: 0, honestCount: 5 })
            });
            const data = await res.json();
            log.innerText = 'Orders Dispatched:\n' + JSON.stringify(data, null, 2);
            refreshData();
        } catch (e) {
            log.innerText = 'Orders dispatch failed: ' + e;
        }
    }

    setInterval(refreshData, 2000);
    refreshData();
</script>
</body>
</html>
""";
    }
}
