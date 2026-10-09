import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TopologyStateService } from '../../core/services/topology-state.service';
import { ControlPlaneApiService } from '../../core/services/control-plane-api.service';
import { HashRingCanvasComponent } from './components/hash-ring-canvas.component';
import { PassivationDialogComponent } from './components/passivation-dialog.component';
import { ClusterSelectorComponent } from '../../shared/components/cluster-selector.component';

@Component({
  selector: 'app-actors',
  standalone: true,
  imports: [
    CommonModule,
    HashRingCanvasComponent,
    PassivationDialogComponent,
    ClusterSelectorComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="actors-page">
      <div class="header-bar">
        <div>
          <h2 class="page-title">Virtual Actor Ring & Density Inspector</h2>
          <p class="page-desc">
            Single-threaded actor turn mailbox coordination with consistent hash ring placement partitioned by <code>ClusterId</code> and <code>AppId</code>.
          </p>
        </div>
        <div class="header-actions">
          <app-cluster-selector></app-cluster-selector>
          <button class="btn btn-danger" (click)="isPassivationOpen.set(true)">
            ⚡ Passivate Actor
          </button>
        </div>
      </div>

      <!-- Stats Bar -->
      <div class="stats-row">
        <div class="stat-card">
          <div class="stat-title">Active Activations</div>
          <div class="stat-value text-accent">{{ activationCount() }}</div>
          <div class="stat-subtext">In-memory mailbox actors</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">Registered Actor Types</div>
          <div class="stat-value">{{ actorTypes().length }}</div>
          <div class="stat-subtext">{{ actorTypes().join(', ') || 'No types detected' }}</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">Cluster Partitions</div>
          <div class="stat-value text-success">{{ topologyState.filteredNodes().length * 64 }}</div>
          <div class="stat-subtext">64 virtual shards per node</div>
        </div>
      </div>

      <!-- Hash Ring Canvas Visualizer -->
      <app-hash-ring-canvas [nodes]="topologyState.filteredNodes()"></app-hash-ring-canvas>

      <!-- Passivation Dialog -->
      <app-passivation-dialog
        [isOpen]="isPassivationOpen()"
        [actorTypes]="actorTypes()"
        (close)="isPassivationOpen.set(false)"
        (passivated)="loadActorStats()">
      </app-passivation-dialog>
    </div>
  `,
  styles: [`
    .actors-page {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .header-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
      flex-wrap: wrap;
      gap: 12px;
    }

    .page-title {
      margin: 0;
      font-size: 1.25rem;
      font-weight: 700;
      color: #f8fafc;
    }

    .page-desc {
      margin: 4px 0 0;
      font-size: 0.85rem;
      color: #94a3b8;
    }

    .header-actions {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .btn {
      padding: 8px 16px;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }

    .btn-danger {
      background: rgba(239, 68, 68, 0.2);
      color: #f87171;
      border: 1px solid rgba(239, 68, 68, 0.4);
    }

    .btn-danger:hover {
      background: rgba(239, 68, 68, 0.3);
    }

    .stats-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 16px;
    }

    .stat-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
    }

    .stat-title {
      font-size: 0.8rem;
      font-weight: 600;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-bottom: 6px;
    }

    .stat-value {
      font-size: 1.8rem;
      font-weight: 700;
      color: #f8fafc;
    }

    .stat-subtext {
      font-size: 0.75rem;
      color: #64748b;
      margin-top: 4px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .text-accent { color: #38bdf8; }
    .text-success { color: #34d399; }
  `]
})
export class ActorsComponent implements OnInit {
  readonly topologyState = inject(TopologyStateService);
  private readonly api = inject(ControlPlaneApiService);

  readonly actorTypes = signal<string[]>([]);
  readonly activationCount = signal<number>(0);
  readonly isPassivationOpen = signal<boolean>(false);

  ngOnInit(): void {
    this.loadActorStats();
  }

  loadActorStats(): void {
    this.api.getActorTypes().subscribe({
      next: types => this.actorTypes.set(types),
      error: () => this.actorTypes.set([])
    });

    this.api.getActorActivations().subscribe({
      next: res => this.activationCount.set(res.count),
      error: () => this.activationCount.set(0)
    });
  }
}
