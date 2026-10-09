import { Component, ChangeDetectionStrategy, inject, computed } from '@angular/core';
import { TopologyStateService } from '../../../core/services/topology-state.service';

@Component({
  selector: 'app-lease-timer',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="lease-timer-box">
      <div class="lease-header">
        <span class="lease-title">HA Distributed Lease Renewal</span>
        <span class="lease-seconds">
          @if (topologyState.leadership().isLeader) {
            {{ ttlDisplay() }}s / 4.0s
          } @else {
            Standby (0s)
          }
        </span>
      </div>
      <div class="progress-bar-bg">
        <div
          class="progress-bar-fill"
          [style.width.%]="progressPercent()"
          [class.standby]="!topologyState.leadership().isLeader">
        </div>
      </div>
      <div class="lease-subtext">
        @if (topologyState.leadership().isLeader) {
          Active Leader Lease: 4.0s duration • Background renewal every 1.2s
        } @else {
          Standby Replica • Monitoring leader at: {{ topologyState.leadership().leaderEndpoint || 'Unknown' }}
        }
      </div>
    </div>
  `,
  styles: [`
    .lease-timer-box {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 14px 18px;
    }

    .lease-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
    }

    .lease-title {
      font-size: 0.85rem;
      font-weight: 600;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .lease-seconds {
      font-family: monospace;
      font-size: 0.95rem;
      font-weight: 700;
      color: #38bdf8;
    }

    .progress-bar-bg {
      height: 8px;
      background: #0f172a;
      border-radius: 4px;
      overflow: hidden;
      margin-bottom: 8px;
    }

    .progress-bar-fill {
      height: 100%;
      background: linear-gradient(90deg, #38bdf8, #34d399);
      transition: width 0.2s linear;
    }

    .progress-bar-fill.standby {
      background: #fbbf24;
      width: 0%;
    }

    .lease-subtext {
      font-size: 0.75rem;
      color: #64748b;
    }
  `]
})
export class LeaseTimerComponent {
  readonly topologyState = inject(TopologyStateService);

  readonly ttlDisplay = computed(() => {
    const ttl = this.topologyState.leadership().leaseTtlSeconds;
    return ttl !== undefined ? ttl.toFixed(1) : '4.0';
  });

  readonly progressPercent = computed(() => {
    const isLeader = this.topologyState.leadership().isLeader;
    if (!isLeader) return 0;
    const ttl = this.topologyState.leadership().leaseTtlSeconds ?? 4.0;
    return Math.min(100, Math.max(0, (ttl / 4.0) * 100));
  });
}
