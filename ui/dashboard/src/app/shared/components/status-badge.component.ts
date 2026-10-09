import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="status-badge" [class]="badgeClass()">
      <span class="status-dot"></span>
      <span class="status-text">{{ label() || status() }}</span>
    </span>
  `,
  styles: [`
    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      font-size: 0.75rem;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: 9999px;
      text-transform: uppercase;
      letter-spacing: 0.04em;
    }

    .status-dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background-color: currentColor;
      box-shadow: 0 0 6px currentColor;
    }

    .badge-healthy, .badge-active {
      background-color: rgba(52, 211, 153, 0.15);
      color: #34d399;
      border: 1px solid rgba(52, 211, 153, 0.3);
    }

    .badge-standby, .badge-warning {
      background-color: rgba(251, 191, 36, 0.15);
      color: #fbbf24;
      border: 1px solid rgba(251, 191, 36, 0.3);
    }

    .badge-unhealthy, .badge-failed, .badge-danger {
      background-color: rgba(248, 113, 113, 0.15);
      color: #f87171;
      border: 1px solid rgba(248, 113, 113, 0.3);
    }

    .badge-info, .badge-neutral {
      background-color: rgba(56, 189, 248, 0.15);
      color: #38bdf8;
      border: 1px solid rgba(56, 189, 248, 0.3);
    }
  `]
})
export class StatusBadgeComponent {
  readonly status = input<string>('Healthy');
  readonly label = input<string | null>(null);

  readonly badgeClass = computed(() => {
    const s = this.status().toLowerCase();
    if (s === 'healthy' || s === 'active' || s === 'online') {
      return 'badge-healthy';
    }
    if (s === 'standby' || s === 'warning' || s === 'degraded') {
      return 'badge-standby';
    }
    if (s === 'unhealthy' || s === 'failed' || s === 'error' || s === 'dead') {
      return 'badge-unhealthy';
    }
    return 'badge-info';
  });
}
