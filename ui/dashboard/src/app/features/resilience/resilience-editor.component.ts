import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../core/services/control-plane-api.service';
import { NotificationService } from '../../core/services/notification.service';
import { ResiliencePolicyDto } from '../../core/models/resilience-policy.model';

@Component({
  selector: 'app-resilience-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="resilience-page">
      <div class="header-box">
        <div>
          <h2 class="page-title">Dynamic Resilience Policy Live Tuner</h2>
          <p class="page-desc">
            Edit Polly v8 composite policies (retries, timeouts, circuit breakers, rate limiters) with instant SSE hot-reloading push to worker nodes.
          </p>
        </div>
        <div>
          <button class="btn btn-primary" (click)="openCreateModal()">
            + Add Resilience Policy
          </button>
        </div>
      </div>

      <!-- Policies Grid -->
      <div class="policies-grid">
        @if (isLoading()) {
          <div class="loading-state">Loading resilience policies...</div>
        } @else if (policies().length === 0) {
          <div class="empty-state">
            <p>No resilience policies configured.</p>
            <span class="subtext">Create a policy to dynamically control fault-tolerance thresholds across all services.</span>
          </div>
        } @else {
          @for (policy of policies(); track policy.policyName) {
            <div class="policy-card">
              <div class="card-header">
                <span class="policy-name">{{ policy.policyName }}</span>
                <div class="card-actions">
                  <button class="action-btn" (click)="editPolicy(policy)">Edit</button>
                  <button class="action-btn delete" (click)="deletePolicy(policy.policyName)">Delete</button>
                </div>
              </div>

              <div class="policy-scope">
                Scope: <strong>{{ policy.scope || 'Global' }}</strong>
              </div>

              <div class="metrics-grid">
                <div class="metric-item">
                  <span class="metric-label">Max Retries</span>
                  <span class="metric-val">{{ policy.maxRetryAttempts ?? 'None' }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">Timeout</span>
                  <span class="metric-val">{{ policy.timeoutSeconds ? policy.timeoutSeconds + 's' : 'None' }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">CB Failure Ratio</span>
                  <span class="metric-val">{{ policy.circuitBreakerFailureRatio ? (policy.circuitBreakerFailureRatio * 100) + '%' : 'None' }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">CB Duration</span>
                  <span class="metric-val">{{ policy.circuitBreakerDurationSeconds ? policy.circuitBreakerDurationSeconds + 's' : 'None' }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">Rate Limit</span>
                  <span class="metric-val">
                    {{ policy.rateLimitPermits ? policy.rateLimitPermits + ' req / ' + (policy.rateLimitPeriodSeconds || 1) + 's' : 'None' }}
                  </span>
                </div>
              </div>
            </div>
          }
        }
      </div>

      <!-- Policy Editor Modal -->
      @if (isModalOpen()) {
        <div class="modal-backdrop" (click)="isModalOpen.set(false)">
          <div class="modal-dialog" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ isEditing ? 'Edit Policy: ' + currentPolicy.policyName : 'New Resilience Policy' }}</h3>
              <button class="close-btn" (click)="isModalOpen.set(false)">✕</button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Policy Name</label>
                <input
                  type="text"
                  class="form-control"
                  [disabled]="isEditing"
                  [(ngModel)]="currentPolicy.policyName"
                  placeholder="e.g. default-http-policy" />
              </div>

              <div class="form-group">
                <label>Scope (Optional)</label>
                <input
                  type="text"
                  class="form-control"
                  [(ngModel)]="currentPolicy.scope"
                  placeholder="e.g. inventory-service or leave blank" />
              </div>

              <div class="form-row">
                <div class="form-group col">
                  <label>Max Retry Attempts</label>
                  <input
                    type="number"
                    class="form-control"
                    [(ngModel)]="currentPolicy.maxRetryAttempts"
                    placeholder="3" />
                </div>
                <div class="form-group col">
                  <label>Timeout (Seconds)</label>
                  <input
                    type="number"
                    class="form-control"
                    [(ngModel)]="currentPolicy.timeoutSeconds"
                    placeholder="5.0" />
                </div>
              </div>

              <div class="form-row">
                <div class="form-group col">
                  <label>CB Failure Ratio (0.1 - 1.0)</label>
                  <input
                    type="number"
                    step="0.05"
                    class="form-control"
                    [(ngModel)]="currentPolicy.circuitBreakerFailureRatio"
                    placeholder="0.5" />
                </div>
                <div class="form-group col">
                  <label>CB Break Duration (s)</label>
                  <input
                    type="number"
                    class="form-control"
                    [(ngModel)]="currentPolicy.circuitBreakerDurationSeconds"
                    placeholder="10.0" />
                </div>
              </div>

              <div class="form-row">
                <div class="form-group col">
                  <label>Rate Limit Permits</label>
                  <input
                    type="number"
                    class="form-control"
                    [(ngModel)]="currentPolicy.rateLimitPermits"
                    placeholder="100" />
                </div>
                <div class="form-group col">
                  <label>Rate Limit Period (s)</label>
                  <input
                    type="number"
                    class="form-control"
                    [(ngModel)]="currentPolicy.rateLimitPeriodSeconds"
                    placeholder="1.0" />
                </div>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn btn-secondary" (click)="isModalOpen.set(false)">Cancel</button>
              <button class="btn btn-primary" (click)="savePolicy()" [disabled]="!currentPolicy.policyName">
                Deploy Policy Live
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .resilience-page {
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

    .policies-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 16px;
    }

    .policy-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 18px;
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    .policy-name {
      font-size: 1.05rem;
      font-weight: 700;
      color: #38bdf8;
    }

    .card-actions {
      display: flex;
      gap: 6px;
    }

    .action-btn {
      background: #0f172a;
      border: 1px solid #334155;
      color: #94a3b8;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 0.75rem;
      cursor: pointer;
    }

    .action-btn:hover { color: #f8fafc; border-color: #38bdf8; }
    .action-btn.delete { color: #f87171; }
    .action-btn.delete:hover { border-color: #f87171; }

    .policy-scope {
      font-size: 0.8rem;
      color: #94a3b8;
    }

    .metrics-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 8px;
      background: #0f172a;
      border-radius: 6px;
      padding: 10px;
    }

    .metric-item {
      display: flex;
      flex-direction: column;
      font-size: 0.75rem;
    }

    .metric-label {
      color: #64748b;
      text-transform: uppercase;
    }

    .metric-val {
      color: #f8fafc;
      font-weight: 600;
      margin-top: 2px;
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

    .empty-state, .loading-state {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 40px;
      text-align: center;
      color: #94a3b8;
      grid-column: 1 / -1;
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
      max-width: 500px;
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
      margin-bottom: 12px;
    }

    .form-row {
      display: flex;
      gap: 12px;
    }

    .form-group.col {
      flex: 1;
    }

    .form-group label {
      display: block;
      font-size: 0.75rem;
      font-weight: 600;
      color: #94a3b8;
      margin-bottom: 4px;
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

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      margin-top: 20px;
    }
  `]
})
export class ResilienceEditorComponent implements OnInit {
  private readonly api = inject(ControlPlaneApiService);
  private readonly notifications = inject(NotificationService);

  readonly policies = signal<ResiliencePolicyDto[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly isModalOpen = signal<boolean>(false);
  isEditing = false;

  currentPolicy: Partial<ResiliencePolicyDto> = {};

  ngOnInit(): void {
    this.loadPolicies();
  }

  loadPolicies(): void {
    this.isLoading.set(true);
    this.api.getResiliencePolicies().subscribe({
      next: list => {
        this.policies.set(list);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.notifications.error('Failed to load resilience policies.');
      }
    });
  }

  openCreateModal(): void {
    this.isEditing = false;
    this.currentPolicy = {
      policyName: '',
      maxRetryAttempts: 3,
      timeoutSeconds: 5.0,
      circuitBreakerFailureRatio: 0.5,
      circuitBreakerDurationSeconds: 10.0,
      rateLimitPermits: 100,
      rateLimitPeriodSeconds: 1.0,
      scope: ''
    };
    this.isModalOpen.set(true);
  }

  editPolicy(p: ResiliencePolicyDto): void {
    this.isEditing = true;
    this.currentPolicy = { ...p };
    this.isModalOpen.set(true);
  }

  savePolicy(): void {
    const dto: ResiliencePolicyDto = {
      policyName: this.currentPolicy.policyName!.trim(),
      maxRetryAttempts: this.currentPolicy.maxRetryAttempts ? Number(this.currentPolicy.maxRetryAttempts) : null,
      timeoutSeconds: this.currentPolicy.timeoutSeconds ? Number(this.currentPolicy.timeoutSeconds) : null,
      circuitBreakerFailureRatio: this.currentPolicy.circuitBreakerFailureRatio ? Number(this.currentPolicy.circuitBreakerFailureRatio) : null,
      circuitBreakerDurationSeconds: this.currentPolicy.circuitBreakerDurationSeconds ? Number(this.currentPolicy.circuitBreakerDurationSeconds) : null,
      rateLimitPermits: this.currentPolicy.rateLimitPermits ? Number(this.currentPolicy.rateLimitPermits) : null,
      rateLimitPeriodSeconds: this.currentPolicy.rateLimitPeriodSeconds ? Number(this.currentPolicy.rateLimitPeriodSeconds) : null,
      scope: this.currentPolicy.scope?.trim() || null
    };

    this.api.upsertResiliencePolicy(dto).subscribe({
      next: () => {
        this.notifications.success(`Resilience policy '${dto.policyName}' deployed live.`);
        this.isModalOpen.set(false);
        this.loadPolicies();
      },
      error: err => {
        this.notifications.error(`Failed to save policy: ${err.message || err.statusText}`);
      }
    });
  }

  deletePolicy(name: string): void {
    if (confirm(`Are you sure you want to delete policy '${name}'?`)) {
      this.api.deleteResiliencePolicy(name).subscribe({
        next: () => {
          this.notifications.success(`Policy '${name}' deleted.`);
          this.loadPolicies();
        },
        error: err => {
          this.notifications.error(`Failed to delete policy: ${err.message || err.statusText}`);
        }
      });
    }
  }
}
