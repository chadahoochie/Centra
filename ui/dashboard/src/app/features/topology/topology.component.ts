import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TopologyStateService } from '../../core/services/topology-state.service';
import { NodeCardComponent } from './components/node-card.component';
import { NodeActionsModalComponent } from './components/node-actions-modal.component';
import { ClusterSelectorComponent } from '../../shared/components/cluster-selector.component';
import { ClusterNode } from '../../core/models/cluster-node.model';

@Component({
  selector: 'app-topology',
  standalone: true,
  imports: [CommonModule, NodeCardComponent, NodeActionsModalComponent, ClusterSelectorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="topology-page">
      <div class="toolbar">
        <div class="toolbar-left">
          <h2 class="page-title">Multi-Cluster Fleet Topology</h2>
          <app-cluster-selector></app-cluster-selector>
        </div>

        <div class="toolbar-right">
          <input
            type="text"
            class="search-input"
            placeholder="Search by App ID or Instance ID..."
            [value]="searchQuery()"
            (input)="onSearchInput($event)" />

          <button class="refresh-btn" (click)="topologyState.refreshAll()">
            ↻ Refresh
          </button>
        </div>
      </div>

      <div class="summary-bar">
        <span>Showing <strong>{{ matchingNodes().length }}</strong> of <strong>{{ topologyState.filteredNodes().length }}</strong> nodes in cluster <strong>{{ topologyState.activeCluster() }}</strong></span>
      </div>

      @if (matchingNodes().length === 0) {
        <div class="empty-state">
          <p>No nodes match the selected criteria.</p>
          <span class="subtext">Verify that worker microservices are configured with this Control Plane endpoint and cluster token.</span>
        </div>
      } @else {
        <div class="nodes-grid">
          @for (node of matchingNodes(); track node.instanceId) {
            <app-node-card
              [node]="node"
              (inspect)="selectedNode.set($event)">
            </app-node-card>
          }
        </div>
      }

      <!-- Node Inspector Modal -->
      <app-node-actions-modal
        [node]="selectedNode()"
        (close)="selectedNode.set(null)">
      </app-node-actions-modal>
    </div>
  `,
  styles: [`
    .topology-page {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .toolbar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
    }

    .toolbar-left, .toolbar-right {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }

    .page-title {
      margin: 0;
      font-size: 1.25rem;
      font-weight: 700;
      color: #f8fafc;
    }

    .search-input {
      background: #0f172a;
      border: 1px solid #334155;
      color: #f8fafc;
      padding: 8px 14px;
      border-radius: 6px;
      font-size: 0.85rem;
      width: 260px;
      outline: none;
      transition: border-color 0.15s ease;
    }

    .search-input:focus {
      border-color: #38bdf8;
    }

    .refresh-btn {
      background: #0f172a;
      border: 1px solid #334155;
      color: #38bdf8;
      padding: 8px 14px;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 600;
      cursor: pointer;
    }

    .refresh-btn:hover {
      background: #334155;
    }

    .summary-bar {
      font-size: 0.85rem;
      color: #94a3b8;
    }

    .summary-bar strong {
      color: #f8fafc;
    }

    .nodes-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
    }

    .empty-state {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      text-align: center;
      padding: 40px 20px;
      color: #94a3b8;
    }

    .empty-state .subtext {
      font-size: 0.85rem;
      color: #64748b;
      margin-top: 6px;
      display: block;
    }
  `]
})
export class TopologyComponent {
  readonly topologyState = inject(TopologyStateService);
  readonly searchQuery = signal<string>('');
  readonly selectedNode = signal<ClusterNode | null>(null);

  readonly matchingNodes = computed(() => {
    const q = this.searchQuery().toLowerCase().trim();
    const nodes = this.topologyState.filteredNodes();
    if (!q) return nodes;

    return nodes.filter(n =>
      n.appId.toLowerCase().includes(q) ||
      n.instanceId.toLowerCase().includes(q) ||
      (n.clusterId && n.clusterId.toLowerCase().includes(q))
    );
  });

  onSearchInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input) {
      this.searchQuery.set(input.value);
    }
  }
}
