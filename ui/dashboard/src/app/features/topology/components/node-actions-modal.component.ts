import { Component, ChangeDetectionStrategy, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ClusterNode } from '../../../core/models/cluster-node.model';
import { StatusBadgeComponent } from '../../../shared/components/status-badge.component';

@Component({
  selector: 'app-node-actions-modal',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (node()) {
      <div class="modal-backdrop" (click)="close.emit()">
        <div class="modal-dialog" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>Node Inspector: {{ node()!.instanceId }}</h3>
            <button class="close-btn" (click)="close.emit()">✕</button>
          </div>
          <div class="modal-body">
            <div class="prop-row">
              <span class="prop-label">Cluster ID:</span>
              <span class="prop-val">{{ node()!.clusterId || 'default' }}</span>
            </div>
            <div class="prop-row">
              <span class="prop-label">App ID:</span>
              <span class="prop-val">{{ node()!.appId }}</span>
            </div>
            <div class="prop-row">
              <span class="prop-label">Instance ID:</span>
              <span class="prop-val"><code>{{ node()!.instanceId }}</code></span>
            </div>
            <div class="prop-row">
              <span class="prop-label">Status:</span>
              <span class="prop-val"><app-status-badge [status]="node()!.status"></app-status-badge></span>
            </div>
            <div class="prop-row">
              <span class="prop-label">Registered At:</span>
              <span class="prop-val">{{ node()!.registeredAtUtc | date:'medium' }}</span>
            </div>
            <div class="prop-row">
              <span class="prop-label">Last Heartbeat:</span>
              <span class="prop-val">{{ node()!.lastHeartbeatUtc | date:'medium' }}</span>
            </div>

            <div class="section-title">Metadata & Tags</div>
            @if (node()!.metadata && hasMetadata()) {
              <div class="meta-tags">
                @for (entry of metadataEntries(); track entry.key) {
                  <div class="meta-tag">
                    <span class="meta-k">{{ entry.key }}:</span>
                    <span class="meta-v">{{ entry.value }}</span>
                  </div>
                }
              </div>
            } @else {
              <div class="no-meta">No additional metadata registered for this node.</div>
            }

            <div class="section-title">Operator Diagnostics</div>
            <div class="curl-box">
              <code>curl -X POST http://localhost:8080/api/v1/heartbeat -H "Content-Type: application/json" -d '{{"{\\"clusterId\\": \\"" + (node()!.clusterId || "default") + "\\", \\"appId\\": \\"" + node()!.appId + "\\", \\"instanceId\\": \\"" + node()!.instanceId + "\\", \\"status\\": \\"Healthy\\"}"}}'</code>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" (click)="close.emit()">Close</button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
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
      max-width: 600px;
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

    .prop-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      border-bottom: 1px solid #0f172a;
      font-size: 0.85rem;
    }

    .prop-label { color: #94a3b8; }
    .prop-val { font-weight: 500; }

    .section-title {
      font-size: 0.8rem;
      font-weight: 600;
      color: #94a3b8;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      margin-top: 16px;
      margin-bottom: 8px;
    }

    .meta-tags {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .meta-tag {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 4px;
      padding: 4px 8px;
      font-size: 0.75rem;
    }

    .meta-k { color: #94a3b8; }
    .meta-v { color: #38bdf8; font-weight: 600; }
    .no-meta { font-size: 0.8rem; color: #64748b; }

    .curl-box {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 4px;
      padding: 10px;
      font-size: 0.75rem;
      overflow-x: auto;
      color: #a7f3d0;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      margin-top: 20px;
    }

    .btn {
      padding: 8px 16px;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }

    .btn-secondary {
      background: #334155;
      color: #f8fafc;
    }
  `]
})
export class NodeActionsModalComponent {
  readonly node = input<ClusterNode | null>(null);
  readonly close = output<void>();

  hasMetadata(): boolean {
    const meta = this.node()?.metadata;
    return !!meta && Object.keys(meta).length > 0;
  }

  metadataEntries(): { key: string; value: string }[] {
    const meta = this.node()?.metadata;
    if (!meta) return [];
    return Object.entries(meta).map(([key, value]) => ({ key, value }));
  }
}
