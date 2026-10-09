import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../core/services/control-plane-api.service';
import { NotificationService } from '../../core/services/notification.service';
import {
  WorkflowDefinitionDto,
  WorkflowActivityDto,
  WorkflowInstanceStateDto,
  WorkflowHistoryEventDto
} from '../../core/models/workflow.model';
import { ActivityTimelineComponent } from './components/activity-timeline.component';
import { SagaCompensationTreeComponent } from './components/saga-compensation-tree.component';
import { StatusBadgeComponent } from '../../shared/components/status-badge.component';

@Component({
  selector: 'app-workflows',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ActivityTimelineComponent,
    SagaCompensationTreeComponent,
    StatusBadgeComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="workflows-page">
      <div class="header-box">
        <div>
          <h2 class="page-title">Distributed Workflows & Sagas</h2>
          <p class="page-desc">
            Deterministic orchestration turns with durable activity execution and LIFO saga compensation rollback.
          </p>
        </div>
      </div>

      <!-- Overview Stats -->
      <div class="stats-row">
        <div class="stat-card">
          <div class="stat-title">Workflow Definitions</div>
          <div class="stat-value text-accent">{{ workflows().length }}</div>
          <div class="stat-subtext">Registered orchestrations</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">Registered Activities</div>
          <div class="stat-value text-success">{{ activities().length }}</div>
          <div class="stat-subtext">Idempotent side-effect workers</div>
        </div>
        <div class="stat-card">
          <div class="stat-title">Audit Boundary</div>
          <div class="stat-value text-purple">Zero PHI</div>
          <div class="stat-subtext">HIPAA compliant payload redaction</div>
        </div>
      </div>

      <!-- Instance Inspector Search -->
      <div class="section-card">
        <h3 class="section-title">Workflow Instance Inspector</h3>
        <div class="search-bar">
          <input
            type="text"
            class="instance-input"
            placeholder="Enter Workflow Instance ID (GUID)..."
            [(ngModel)]="searchInstanceId"
            (keyup.enter)="lookupInstance()" />
          <button class="btn btn-primary" (click)="lookupInstance()" [disabled]="isLoadingInstance()">
            {{ isLoadingInstance() ? 'Querying...' : 'Inspect Instance' }}
          </button>
        </div>

        @if (selectedInstance()) {
          <div class="instance-details">
            <div class="inst-grid">
              <div class="inst-field">
                <span class="field-label">Instance ID:</span>
                <code>{{ selectedInstance()!.instanceId }}</code>
              </div>
              <div class="inst-field">
                <span class="field-label">Workflow:</span>
                <strong>{{ selectedInstance()!.workflowName }}</strong>
              </div>
              <div class="inst-field">
                <span class="field-label">Status:</span>
                <app-status-badge [status]="selectedInstance()!.status"></app-status-badge>
              </div>
              <div class="inst-field">
                <span class="field-label">Created At:</span>
                <span>{{ selectedInstance()!.createdAt | date:'medium' }}</span>
              </div>
            </div>

            @if (selectedInstance()!.failureDetails) {
              <div class="failure-alert">
                <strong>Failure Details:</strong> {{ selectedInstance()!.failureDetails }}
              </div>
            }

            <!-- Execution Timeline -->
            <div class="sub-section">
              <h4>Activity Execution Timeline</h4>
              <app-activity-timeline [events]="instanceHistory()"></app-activity-timeline>
            </div>
          </div>
        }
      </div>

      <!-- Saga Compensation Tree -->
      <div class="section-card">
        <app-saga-compensation-tree></app-saga-compensation-tree>
      </div>

      <!-- Catalogs: Workflows and Activities -->
      <div class="catalogs-grid">
        <div class="catalog-card">
          <h3 class="section-title">Workflow Definitions</h3>
          @if (workflows().length === 0) {
            <div class="empty-list">No workflow definitions registered in this runtime.</div>
          } @else {
            <table class="simple-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Type</th>
                  <th>Input</th>
                  <th>Output</th>
                </tr>
              </thead>
              <tbody>
                @for (wf of workflows(); track wf.name) {
                  <tr>
                    <td><strong>{{ wf.name }}</strong></td>
                    <td><code>{{ wf.workflowType }}</code></td>
                    <td>{{ wf.inputType }}</td>
                    <td>{{ wf.outputType }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>

        <div class="catalog-card">
          <h3 class="section-title">Registered Activities</h3>
          @if (activities().length === 0) {
            <div class="empty-list">No activity workers registered.</div>
          } @else {
            <table class="simple-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Activity Type</th>
                  <th>Input</th>
                  <th>Output</th>
                </tr>
              </thead>
              <tbody>
                @for (act of activities(); track act.name) {
                  <tr>
                    <td><strong>{{ act.name }}</strong></td>
                    <td><code>{{ act.activityType }}</code></td>
                    <td>{{ act.inputType }}</td>
                    <td>{{ act.outputType }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      </div>
    </div>
  `,
  styles: [`
    .workflows-page {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .header-box {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px 20px;
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

    .text-accent { color: #38bdf8; }
    .text-success { color: #34d399; }
    .text-purple { color: #c084fc; }

    .stat-subtext {
      font-size: 0.75rem;
      color: #64748b;
      margin-top: 4px;
    }

    .section-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 20px;
    }

    .section-title {
      font-size: 1.05rem;
      font-weight: 700;
      color: #f8fafc;
      margin-top: 0;
      margin-bottom: 14px;
    }

    .search-bar {
      display: flex;
      gap: 10px;
      margin-bottom: 16px;
    }

    .instance-input {
      flex: 1;
      background: #0f172a;
      border: 1px solid #334155;
      color: #f8fafc;
      padding: 8px 14px;
      border-radius: 6px;
      font-size: 0.85rem;
      outline: none;
    }

    .instance-input:focus {
      border-color: #38bdf8;
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
    .btn-primary:hover { background: #0369a1; }

    .instance-details {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 6px;
      padding: 16px;
      margin-top: 14px;
    }

    .inst-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 12px;
      margin-bottom: 14px;
    }

    .inst-field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 0.85rem;
    }

    .field-label {
      font-size: 0.75rem;
      color: #94a3b8;
      text-transform: uppercase;
    }

    .failure-alert {
      background: rgba(239, 68, 68, 0.15);
      border: 1px solid rgba(239, 68, 68, 0.3);
      color: #f87171;
      padding: 10px;
      border-radius: 6px;
      font-size: 0.85rem;
      margin-bottom: 16px;
    }

    .sub-section h4 {
      font-size: 0.9rem;
      color: #94a3b8;
      margin-top: 16px;
      margin-bottom: 10px;
      text-transform: uppercase;
    }

    .catalogs-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(360px, 1fr));
      gap: 16px;
    }

    .catalog-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 20px;
    }

    .simple-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.85rem;
    }

    .simple-table th, .simple-table td {
      padding: 8px 10px;
      text-align: left;
      border-bottom: 1px solid #334155;
    }

    .simple-table th {
      color: #94a3b8;
      font-size: 0.75rem;
      text-transform: uppercase;
    }

    .empty-list {
      color: #64748b;
      font-size: 0.85rem;
      padding: 20px 0;
      text-align: center;
    }
  `]
})
export class WorkflowsComponent implements OnInit {
  private readonly api = inject(ControlPlaneApiService);
  private readonly notifications = inject(NotificationService);

  readonly workflows = signal<WorkflowDefinitionDto[]>([]);
  readonly activities = signal<WorkflowActivityDto[]>([]);

  searchInstanceId: string = '';
  readonly isLoadingInstance = signal<boolean>(false);
  readonly selectedInstance = signal<WorkflowInstanceStateDto | null>(null);
  readonly instanceHistory = signal<WorkflowHistoryEventDto[]>([]);

  ngOnInit(): void {
    this.api.getWorkflowDefinitions().subscribe({
      next: defs => this.workflows.set(defs),
      error: () => this.workflows.set([])
    });

    this.api.getWorkflowActivities().subscribe({
      next: acts => this.activities.set(acts),
      error: () => this.activities.set([])
    });
  }

  lookupInstance(): void {
    const id = this.searchInstanceId.trim();
    if (!id) return;

    this.isLoadingInstance.set(true);
    this.api.getWorkflowInstance(id).subscribe({
      next: (state: WorkflowInstanceStateDto) => {
        this.selectedInstance.set(state);
        this.api.getWorkflowHistory(id).subscribe({
          next: (history: WorkflowHistoryEventDto[]) => {
            this.instanceHistory.set(history);
            this.isLoadingInstance.set(false);
          },
          error: () => {
            this.instanceHistory.set([]);
            this.isLoadingInstance.set(false);
          }
        });
      },
      error: () => {
        this.isLoadingInstance.set(false);
        this.selectedInstance.set(null);
        this.notifications.warning(`Workflow instance ${id} not found.`);
      }
    });
  }
}
