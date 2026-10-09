import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../core/services/control-plane-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ComponentDefinition, ComponentEntryDto } from '../../core/models/component-definition.model';
import { ClusterSelectorComponent } from '../../shared/components/cluster-selector.component';
import { TopologyStateService } from '../../core/services/topology-state.service';

@Component({
  selector: 'app-components',
  standalone: true,
  imports: [CommonModule, FormsModule, ClusterSelectorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="components-page">
      <div class="header-box">
        <div>
          <h2 class="page-title">Infrastructure Component Catalog</h2>
          <p class="page-desc">
            Centralized State Stores, Pub/Sub Brokers, Distributed Locks, and External Bindings with hot-reloading SSE synchronization.
          </p>
        </div>
        <div class="header-actions">
          <app-cluster-selector></app-cluster-selector>
          <button class="btn btn-primary" (click)="openAddModal()">
            + Register Component
          </button>
        </div>
      </div>

      <div class="security-banner">
        <span class="security-badge">🔒 Secret Masking Active</span>
        <span class="security-desc">
          Sensitive connection string credentials (e.g. passwords, access keys) are redacted in dashboard telemetry.
        </span>
      </div>

      <!-- Components Table -->
      <div class="table-card">
        @if (isLoading()) {
          <div class="loading-state">Loading components...</div>
        } @else if (components().length === 0) {
          <div class="empty-state">
            <p>No components registered in catalog.</p>
            <span class="subtext">Register a component or declare in your application startup configuration.</span>
          </div>
        } @else {
          <table class="data-table">
            <thead>
              <tr>
                <th>Component Name</th>
                <th>Type</th>
                <th>Provider Driver</th>
                <th>Cluster Scope</th>
                <th>Revision</th>
                <th>Metadata & Configuration</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (entry of components(); track entry.definition.name) {
                <tr>
                  <td><strong>{{ entry.definition.name }}</strong></td>
                  <td><span class="type-badge">{{ getTypeName(entry.definition.type) }}</span></td>
                  <td><code>{{ entry.definition.provider }}</code></td>
                  <td>{{ entry.definition.clusterId || 'Global (All)' }}</td>
                  <td><span class="rev-badge">r{{ entry.revision }}</span></td>
                  <td>
                    @if (entry.definition.metadata) {
                      <div class="meta-preview">
                        @for (k of getMetadataKeys(entry.definition.metadata); track k) {
                          <span class="meta-item">
                            {{ k }}=<code>{{ maskSensitive(k, entry.definition.metadata[k]) }}</code>
                          </span>
                        }
                      </div>
                    } @else {
                      <span class="text-muted">None</span>
                    }
                  </td>
                  <td>
                    <button class="btn-delete" (click)="deleteComponent(entry.definition.name)">
                      Delete
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <!-- Add Component Modal -->
      @if (isModalOpen()) {
        <div class="modal-backdrop" (click)="isModalOpen.set(false)">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Register Infrastructure Component</h3>
              <button class="close-btn" (click)="isModalOpen.set(false)">✕</button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Component Name</label>
                <input type="text" class="form-control" [(ngModel)]="newComponent.name" placeholder="e.g. redis-statestore" />
              </div>
              <div class="form-group">
                <label>Type</label>
                <select class="form-control" [(ngModel)]="newComponent.type">
                  <option [value]="0">StateStore</option>
                  <option [value]="1">PubSub</option>
                  <option [value]="2">LockStore</option>
                  <option [value]="3">Binding</option>
                  <option [value]="4">Resilience</option>
                </select>
              </div>
              <div class="form-group">
                <label>Provider Driver</label>
                <input type="text" class="form-control" [(ngModel)]="newComponent.provider" placeholder="e.g. redis, postgresql, rabbitmq" />
              </div>
              <div class="form-group">
                <label>Cluster ID Scope (Optional)</label>
                <input type="text" class="form-control" [(ngModel)]="newComponent.clusterId" placeholder="Leave empty for Global" />
              </div>
              <div class="form-group">
                <label>Metadata Key-Values (JSON)</label>
                <textarea
                  class="form-control code-area"
                  rows="4"
                  [(ngModel)]="newMetadataJson"
                  placeholder='{"connectionString": "localhost:6379"}'>
                </textarea>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn btn-secondary" (click)="isModalOpen.set(false)">Cancel</button>
              <button class="btn btn-primary" (click)="saveComponent()" [disabled]="!newComponent.name || !newComponent.provider">
                Save Component
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .components-page {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .header-box {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
      display: flex;
      justify-content: space-between;
      align-items: center;
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

    .security-banner {
      background: rgba(56, 189, 248, 0.08);
      border: 1px solid rgba(56, 189, 248, 0.25);
      border-radius: 6px;
      padding: 10px 14px;
      display: flex;
      align-items: center;
      gap: 12px;
      font-size: 0.8rem;
    }

    .security-badge {
      color: #38bdf8;
      font-weight: 700;
    }

    .security-desc {
      color: #94a3b8;
    }

    .table-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 20px;
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.85rem;
    }

    .data-table th, .data-table td {
      padding: 10px 12px;
      text-align: left;
      border-bottom: 1px solid #334155;
    }

    .data-table th {
      color: #94a3b8;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .type-badge {
      background: rgba(56, 189, 248, 0.15);
      color: #38bdf8;
      padding: 3px 8px;
      border-radius: 4px;
      font-size: 0.75rem;
      font-weight: 600;
    }

    .rev-badge {
      background: #0f172a;
      border: 1px solid #334155;
      color: #94a3b8;
      padding: 2px 6px;
      border-radius: 4px;
      font-size: 0.75rem;
      font-family: monospace;
    }

    .meta-preview {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .meta-item {
      font-size: 0.75rem;
      color: #94a3b8;
      background: #0f172a;
      padding: 2px 6px;
      border-radius: 4px;
    }

    .meta-item code {
      color: #f8fafc;
    }

    .btn {
      padding: 8px 16px;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }

    .btn-primary { background: #0284c7; color: white; }
    .btn-secondary { background: #334155; color: #f8fafc; }

    .btn-delete {
      background: transparent;
      border: 1px solid rgba(239, 68, 68, 0.4);
      color: #f87171;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 0.75rem;
      cursor: pointer;
    }

    .btn-delete:hover {
      background: rgba(239, 68, 68, 0.2);
    }

    .empty-state, .loading-state {
      text-align: center;
      padding: 40px;
      color: #94a3b8;
    }

    .empty-state .subtext {
      font-size: 0.8rem;
      color: #64748b;
      margin-top: 6px;
      display: block;
    }

    .modal-backdrop {
      position: fixed;
      top: 0;
      left: 0;
      width: 100vw;
      height: 100vh;
      background: rgba(0, 0, 0, 0.7);
      display: flex;
      justify-content: center;
      align-items: center;
      z-index: 1000;
    }

    .modal-dialog {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      width: 90%;
      max-width: 520px;
      padding: 24px;
      color: #f8fafc;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5);
    }

    .modal-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid #334155;
      padding-bottom: 12px;
      margin-bottom: 16px;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 1.1rem;
      color: #38bdf8;
    }

    .close-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      font-size: 1.2rem;
      cursor: pointer;
    }

    .form-group {
      margin-bottom: 14px;
    }

    .form-group label {
      display: block;
      font-size: 0.8rem;
      font-weight: 600;
      color: #94a3b8;
      margin-bottom: 6px;
      text-transform: uppercase;
    }

    .form-control {
      width: 100%;
      background: #0f172a;
      border: 1px solid #334155;
      color: #f8fafc;
      padding: 8px 12px;
      border-radius: 6px;
      font-size: 0.85rem;
      outline: none;
      box-sizing: border-box;
    }

    .form-control:focus {
      border-color: #38bdf8;
    }

    .code-area {
      font-family: monospace;
      font-size: 0.8rem;
      resize: vertical;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      margin-top: 20px;
    }
  `]
})
export class ComponentsComponent implements OnInit {
  private readonly api = inject(ControlPlaneApiService);
  private readonly notifications = inject(NotificationService);
  readonly topologyState = inject(TopologyStateService);

  readonly components = signal<ComponentEntryDto[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly isModalOpen = signal<boolean>(false);

  newComponent: Partial<ComponentDefinition> = {
    name: '',
    type: 0,
    provider: '',
    clusterId: ''
  };
  newMetadataJson: string = '{}';

  ngOnInit(): void {
    this.loadComponents();
  }

  loadComponents(): void {
    this.isLoading.set(true);
    this.api.getComponents().subscribe({
      next: list => {
        this.components.set(list);
        this.isLoading.set(false);
      },
      error: err => {
        this.isLoading.set(false);
        this.notifications.error('Failed to load component catalog.');
      }
    });
  }

  getTypeName(type: number | string): string {
    const num = Number(type);
    switch (num) {
      case 0: return 'StateStore';
      case 1: return 'PubSub';
      case 2: return 'LockStore';
      case 3: return 'Binding';
      case 4: return 'Resilience';
      default: return String(type);
    }
  }

  getMetadataKeys(meta: Record<string, string>): string[] {
    return Object.keys(meta);
  }

  maskSensitive(key: string, value: string): string {
    const k = key.toLowerCase();
    if (k.includes('password') || k.includes('secret') || k.includes('key') || k.includes('token') || k.includes('conn')) {
      return '********';
    }
    return value;
  }

  openAddModal(): void {
    this.newComponent = {
      name: '',
      type: 0,
      provider: '',
      clusterId: ''
    };
    this.newMetadataJson = '{}';
    this.isModalOpen.set(true);
  }

  saveComponent(): void {
    let metadata: Record<string, string> = {};
    try {
      if (this.newMetadataJson.trim()) {
        metadata = JSON.parse(this.newMetadataJson);
      }
    } catch {
      this.notifications.error('Invalid JSON format for metadata.');
      return;
    }

    const def: ComponentDefinition = {
      name: this.newComponent.name!.trim(),
      type: Number(this.newComponent.type),
      provider: this.newComponent.provider!.trim(),
      clusterId: this.newComponent.clusterId?.trim() || undefined,
      metadata
    };

    this.api.upsertComponent(def).subscribe({
      next: res => {
        this.notifications.success(`Component ${def.name} registered successfully.`);
        this.isModalOpen.set(false);
        this.loadComponents();
      },
      error: err => {
        this.notifications.error(`Failed to register component: ${err.message || err.statusText}`);
      }
    });
  }

  deleteComponent(name: string): void {
    if (confirm(`Are you sure you want to delete component '${name}'?`)) {
      this.api.deleteComponent(name).subscribe({
        next: () => {
          this.notifications.success(`Component '${name}' removed.`);
          this.loadComponents();
        },
        error: err => {
          this.notifications.error(`Failed to delete component: ${err.message || err.statusText}`);
        }
      });
    }
  }
}
