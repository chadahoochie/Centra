import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ClusterNode } from '../../../core/models/cluster-node.model';
import { StatusBadgeComponent } from '../../../shared/components/status-badge.component';

@Component({
  selector: 'app-node-card',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="node-card" [class.unhealthy]="node().status.toLowerCase() !== 'healthy'">
      <div class="card-header">
        <div class="cluster-badge">{{ node().clusterId || 'default' }}</div>
        <app-status-badge [status]="node().status"></app-status-badge>
      </div>

      <div class="app-id">{{ node().appId }}</div>
      <div class="instance-id" title="Instance ID">
        <code>{{ node().instanceId }}</code>
      </div>

      <div class="heartbeat-info">
        <span class="label">Heartbeat:</span>
        <span class="val">{{ node().lastHeartbeatUtc | date:'HH:mm:ss' }}</span>
      </div>

      <div class="card-footer">
        <button class="inspect-btn" (click)="inspect.emit(node())">
          Inspect Node
        </button>
      </div>
    </div>
  `,
  styles: [`
    .node-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 10px;
      transition: transform 0.15s ease, border-color 0.15s ease;
    }

    .node-card:hover {
      border-color: #38bdf8;
      transform: translateY(-2px);
    }

    .node-card.unhealthy {
      border-color: #f87171;
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .cluster-badge {
      background: #0f172a;
      border: 1px solid #334155;
      padding: 3px 8px;
      border-radius: 4px;
      font-size: 0.75rem;
      font-weight: 600;
      color: #38bdf8;
    }

    .app-id {
      font-size: 1.05rem;
      font-weight: 700;
      color: #f8fafc;
    }

    .instance-id code {
      font-size: 0.75rem;
      color: #94a3b8;
      word-break: break-all;
    }

    .heartbeat-info {
      display: flex;
      justify-content: space-between;
      font-size: 0.8rem;
      padding-top: 6px;
      border-top: 1px solid #0f172a;
    }

    .heartbeat-info .label { color: #64748b; }
    .heartbeat-info .val { color: #f8fafc; font-family: monospace; }

    .card-footer {
      margin-top: 4px;
    }

    .inspect-btn {
      width: 100%;
      background: #0f172a;
      border: 1px solid #334155;
      color: #38bdf8;
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 0.8rem;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s ease, border-color 0.15s ease;
    }

    .inspect-btn:hover {
      background: #334155;
      border-color: #38bdf8;
    }
  `]
})
export class NodeCardComponent {
  readonly node = input.required<ClusterNode>();
  readonly inspect = output<ClusterNode>();
}
