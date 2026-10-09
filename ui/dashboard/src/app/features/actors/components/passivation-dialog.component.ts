import { Component, ChangeDetectionStrategy, input, output, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ControlPlaneApiService } from '../../../core/services/control-plane-api.service';
import { NotificationService } from '../../../core/services/notification.service';

@Component({
  selector: 'app-passivation-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isOpen()) {
      <div class="modal-backdrop" (click)="close.emit()">
        <div class="modal-dialog" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h3>Targeted Virtual Actor Passivation</h3>
            <button class="close-btn" (click)="close.emit()">✕</button>
          </div>
          <div class="modal-body">
            <p class="modal-desc">
              Gracefully evicts the selected virtual actor from in-memory runtime mailbox across the cluster, committing dirty state stage to persistent storage via ETag CAS retries.
            </p>

            <div class="form-group">
              <label for="actorType">Actor Type</label>
              <select id="actorType" class="form-control" [(ngModel)]="selectedType">
                @for (type of actorTypes(); track type) {
                  <option [value]="type">{{ type }}</option>
                }
              </select>
            </div>

            <div class="form-group">
              <label for="actorId">Actor ID</label>
              <input
                id="actorId"
                type="text"
                class="form-control"
                placeholder="e.g. order-88319"
                [(ngModel)]="actorId" />
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" (click)="close.emit()">Cancel</button>
            <button
              class="btn btn-danger"
              [disabled]="isSubmitting() || !actorId.trim()"
              (click)="onPassivate()">
              {{ isSubmitting() ? 'Passivating...' : 'Passivate Actor' }}
            </button>
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
      max-width: 480px;
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
      color: #f87171;
    }

    .close-btn {
      background: transparent;
      border: none;
      color: #94a3b8;
      font-size: 1.2rem;
      cursor: pointer;
    }

    .modal-desc {
      font-size: 0.85rem;
      color: #94a3b8;
      margin-bottom: 16px;
      line-height: 1.4;
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
      font-size: 0.9rem;
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

    .btn {
      padding: 8px 16px;
      border-radius: 6px;
      font-size: 0.85rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }

    .btn-secondary { background: #334155; color: #f8fafc; }
    .btn-danger { background: #dc2626; color: #ffffff; }
    .btn-danger:disabled { opacity: 0.5; cursor: not-allowed; }
  `]
})
export class PassivationDialogComponent {
  readonly isOpen = input<boolean>(false);
  readonly actorTypes = input<string[]>([]);
  readonly close = output<void>();
  readonly passivated = output<void>();

  private readonly api = inject(ControlPlaneApiService);
  private readonly notifications = inject(NotificationService);

  selectedType: string = '';
  actorId: string = '';
  readonly isSubmitting = signal<boolean>(false);

  onPassivate(): void {
    const type = this.selectedType || this.actorTypes()[0];
    const id = this.actorId.trim();
    if (!type || !id) return;

    this.isSubmitting.set(true);
    this.api.passivateActor(type, id).subscribe({
      next: res => {
        this.isSubmitting.set(false);
        if (res.passivated) {
          this.notifications.success(`Actor ${type}/${id} successfully passivated.`);
        } else {
          this.notifications.info(`Actor ${type}/${id} was not actively in memory.`);
        }
        this.actorId = '';
        this.passivated.emit();
        this.close.emit();
      },
      error: err => {
        this.isSubmitting.set(false);
        this.notifications.error(`Failed to passivate actor: ${err.message || err.statusText}`);
      }
    });
  }
}
