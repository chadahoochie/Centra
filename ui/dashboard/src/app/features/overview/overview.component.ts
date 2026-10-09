import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TopologyStateService } from '../../core/services/topology-state.service';
import { LeaseTimerComponent } from './components/lease-timer.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';

@Component({
  selector: 'app-overview',
  standalone: true,
  imports: [CommonModule, RouterLink, LeaseTimerComponent, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="overview-page">
      <!-- HA Leadership Status Banner -->
      <div class="ha-banner" [class.is-leader]="topologyState.leadership().isLeader">
        <div class="ha-info">
          <div class="ha-title">
            <span class="ha-indicator"></span>
            Control Plane High-Availability Cluster:
            <strong>{{ topologyState.leadership().role }}</strong>
          </div>
          <div class="ha-details">
            @if (topologyState.leadership().isLeader) {
              Active Leader node serving multi-cluster fleets with sub-millisecond heartbeat ingestion
            } @else {
              Standby replica (hot backup) • Fast 307 temporary redirect to active leader at:
              <code>{{ topologyState.leadership().leaderEndpoint || 'Primary' }}</code>
            }
          </div>
        </div>
        <div class="ha-tags">
          <span class="dd-tag">service: centra-controlplane</span>
          <span class="dd-tag">env: production</span>
          <span class="dd-tag">ver: 1.0.0</span>
          <span class="dd-tag dd-otel">OTLP Datadog Ready</span>
        </div>
      </div>

      <!-- KPI Metrics Grid -->
      <div class="kpi-grid">
        <div class="kpi-card">
          <div class="kpi-title">Cluster HA Role</div>
          <div class="kpi-value">
            <app-status-badge
              [status]="topologyState.leadership().role"
              [label]="topologyState.leadership().role">
            </app-status-badge>
          </div>
          <div class="kpi-subtext">Active-Passive Distributed Lock</div>
        </div>

        <div class="kpi-card">
          <div class="kpi-title">Active Clusters (N Fleets)</div>
          <div class="kpi-value text-accent">{{ topologyState.clusterList().length || 1 }}</div>
          <div class="kpi-subtext">Isolated topologies & actor rings</div>
        </div>

        <div class="kpi-card">
          <div class="kpi-title">Connected Nodes</div>
          <div class="kpi-value text-success">{{ topologyState.filteredNodes().length }}</div>
          <div class="kpi-subtext">{{ topologyState.healthyNodesCount() }} healthy heartbeats</div>
        </div>

        <div class="kpi-card">
          <div class="kpi-title">Control Plane Status</div>
          <div class="kpi-value">
            <span class="status-indicator-text text-success">
              {{ topologyState.leadership().status || 'Healthy' }}
            </span>
          </div>
          <div class="kpi-subtext">Last sync: {{ topologyState.lastUpdated() | date:'HH:mm:ss' }}</div>
        </div>
      </div>

      <!-- Distributed Lease Countdown -->
      <div class="section-container">
        <app-lease-timer></app-lease-timer>
      </div>

      <!-- Quick Fleet Summary -->
      <div class="section-card">
        <div class="section-card-header">
          <h2 class="section-card-title">Multi-Cluster Fleet Snapshot</h2>
          <a routerLink="/topology" class="view-all-link">Explore Full Topology →</a>
        </div>

        @if (topologyState.filteredNodes().length === 0) {
          <div class="empty-state">
            <p>No active nodes detected yet.</p>
            <span class="empty-subtext">Nodes register automatically when emitting heartbeats to <code>/api/v1/heartbeat</code>.</span>
          </div>
        } @else {
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Cluster ID</th>
                  <th>App ID</th>
                  <th>Instance ID</th>
                  <th>Status</th>
                  <th>Last Heartbeat</th>
                </tr>
              </thead>
              <tbody>
                @for (node of topologyState.filteredNodes().slice(0, 8); track node.instanceId) {
                  <tr>
                    <td><strong>{{ node.clusterId || 'default' }}</strong></td>
                    <td>{{ node.appId }}</td>
                    <td><code class="instance-code">{{ node.instanceId }}</code></td>
                    <td><app-status-badge [status]="node.status"></app-status-badge></td>
                    <td>{{ node.lastHeartbeatUtc | date:'HH:mm:ss' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .overview-page {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .ha-banner {
      background: #1e293b;
      border: 1px solid #334155;
      border-left: 5px solid #fbbf24;
      border-radius: 8px;
      padding: 16px 20px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
    }

    .ha-banner.is-leader {
      border-left-color: #34d399;
    }

    .ha-title {
      font-size: 1.1rem;
      color: #f8fafc;
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .ha-indicator {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: #fbbf24;
      box-shadow: 0 0 8px #fbbf24;
    }

    .ha-banner.is-leader .ha-indicator {
      background: #34d399;
      box-shadow: 0 0 8px #34d399;
    }

    .ha-details {
      font-size: 0.85rem;
      color: #94a3b8;
      margin-top: 4px;
    }

    .ha-tags {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
    }

    .dd-tag {
      font-size: 0.75rem;
      background: #0f172a;
      color: #94a3b8;
      border: 1px solid #334155;
      padding: 4px 8px;
      border-radius: 4px;
      font-family: monospace;
    }

    .dd-otel {
      background: rgba(99, 102, 241, 0.15);
      color: #818cf8;
      border-color: rgba(99, 102, 241, 0.3);
    }

    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
      gap: 16px;
    }

    .kpi-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
    }

    .kpi-title {
      font-size: 0.8rem;
      font-weight: 600;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-bottom: 8px;
    }

    .kpi-value {
      font-size: 2rem;
      font-weight: 700;
      color: #f8fafc;
      line-height: 1.2;
    }

    .text-accent { color: #38bdf8; }
    .text-success { color: #34d399; }

    .kpi-subtext {
      font-size: 0.75rem;
      color: #64748b;
      margin-top: 6px;
    }

    .section-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 20px;
    }

    .section-card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }

    .section-card-title {
      font-size: 1.15rem;
      font-weight: 600;
      color: #f8fafc;
      margin: 0;
    }

    .view-all-link {
      font-size: 0.85rem;
      color: #38bdf8;
      text-decoration: none;
      font-weight: 500;
    }

    .view-all-link:hover {
      text-decoration: underline;
    }

    .table-container {
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.875rem;
    }

    .data-table th, .data-table td {
      padding: 10px 14px;
      text-align: left;
      border-bottom: 1px solid #334155;
    }

    .data-table th {
      color: #94a3b8;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .instance-code {
      background: #0f172a;
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 0.8rem;
      color: #38bdf8;
    }

    .empty-state {
      text-align: center;
      padding: 30px;
      color: #94a3b8;
    }

    .empty-subtext {
      font-size: 0.8rem;
      color: #64748b;
      display: block;
      margin-top: 6px;
    }
  `]
})
export class OverviewComponent {
  readonly topologyState = inject(TopologyStateService);
}
