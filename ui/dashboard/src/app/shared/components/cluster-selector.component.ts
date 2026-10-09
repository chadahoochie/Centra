import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { TopologyStateService } from '../../core/services/topology-state.service';

@Component({
  selector: 'app-cluster-selector',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cluster-selector-container">
      <span class="selector-label">Cluster:</span>
      <select
        class="cluster-select"
        [value]="topologyState.activeCluster()"
        (change)="onClusterChange($event)">
        <option value="all">All Fleets ({{ topologyState.allNodes().length }} nodes)</option>
        @for (cluster of topologyState.clusterList(); track cluster) {
          <option [value]="cluster">{{ cluster }}</option>
        }
      </select>
    </div>
  `,
  styles: [`
    .cluster-selector-container {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 0.85rem;
    }

    .selector-label {
      color: #94a3b8;
      font-weight: 500;
    }

    .cluster-select {
      background-color: #1e293b;
      color: #f8fafc;
      border: 1px solid #334155;
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 0.85rem;
      cursor: pointer;
      outline: none;
      transition: border-color 0.15s ease;
    }

    .cluster-select:focus {
      border-color: #38bdf8;
    }
  `]
})
export class ClusterSelectorComponent {
  readonly topologyState = inject(TopologyStateService);

  onClusterChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (select) {
      this.topologyState.setCluster(select.value);
    }
  }
}
