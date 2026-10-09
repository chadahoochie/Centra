import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface SagaCompensationStep {
  stepNumber: number;
  activityName: string;
  compensationAction: string;
  status: 'Pending' | 'Compensated' | 'Failed';
  timestamp?: string;
}

@Component({
  selector: 'app-saga-compensation-tree',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="saga-tree-box">
      <div class="saga-header">
        <span class="saga-title">Distributed Saga Compensation Tree (LIFO Reverse Rollback)</span>
        <span class="saga-badge">Centra Saga Invariant #10</span>
      </div>

      <div class="saga-desc">
        When a distributed workflow activity fails, registered saga compensations execute strictly in reverse order of forward execution.
      </div>

      <div class="steps-flow">
        @for (step of steps(); track step.stepNumber) {
          <div class="step-card" [class]="'status-' + step.status.toLowerCase()">
            <div class="step-index">Rollback #{{ step.stepNumber }}</div>
            <div class="step-activity">{{ step.activityName }}</div>
            <div class="step-action">↶ {{ step.compensationAction }}</div>
            <div class="step-status">{{ step.status }}</div>
          </div>
          @if (!$last) {
            <div class="flow-arrow">➔</div>
          }
        }
      </div>
    </div>
  `,
  styles: [`
    .saga-tree-box {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px;
    }

    .saga-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 8px;
      flex-wrap: wrap;
      gap: 8px;
    }

    .saga-title {
      font-size: 0.85rem;
      font-weight: 700;
      color: #f8fafc;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .saga-badge {
      background: rgba(168, 85, 247, 0.15);
      color: #c084fc;
      border: 1px solid rgba(168, 85, 247, 0.3);
      padding: 2px 8px;
      border-radius: 4px;
      font-size: 0.75rem;
      font-family: monospace;
    }

    .saga-desc {
      font-size: 0.8rem;
      color: #94a3b8;
      margin-bottom: 14px;
    }

    .steps-flow {
      display: flex;
      align-items: center;
      gap: 12px;
      overflow-x: auto;
      padding-bottom: 8px;
    }

    .step-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 6px;
      padding: 12px 14px;
      min-width: 170px;
      flex-shrink: 0;
    }

    .step-card.status-compensated {
      border-color: #34d399;
      background: rgba(52, 211, 153, 0.05);
    }

    .step-card.status-failed {
      border-color: #f87171;
      background: rgba(248, 113, 113, 0.05);
    }

    .step-card.status-pending {
      border-color: #fbbf24;
    }

    .step-index {
      font-size: 0.7rem;
      font-weight: 600;
      color: #94a3b8;
      text-transform: uppercase;
    }

    .step-activity {
      font-size: 0.9rem;
      font-weight: 700;
      color: #f8fafc;
      margin: 4px 0;
    }

    .step-action {
      font-size: 0.75rem;
      color: #38bdf8;
      font-family: monospace;
    }

    .step-status {
      font-size: 0.75rem;
      font-weight: 600;
      margin-top: 6px;
      text-transform: uppercase;
    }

    .status-compensated .step-status { color: #34d399; }
    .status-failed .step-status { color: #f87171; }
    .status-pending .step-status { color: #fbbf24; }

    .flow-arrow {
      color: #64748b;
      font-size: 1.1rem;
      flex-shrink: 0;
    }
  `]
})
export class SagaCompensationTreeComponent {
  readonly steps = input<SagaCompensationStep[]>([
    {
      stepNumber: 1,
      activityName: 'ShipmentActivity',
      compensationAction: 'CancelShipmentAsync',
      status: 'Compensated'
    },
    {
      stepNumber: 2,
      activityName: 'PaymentActivity',
      compensationAction: 'RefundPaymentAsync',
      status: 'Compensated'
    },
    {
      stepNumber: 3,
      activityName: 'InventoryActivity',
      compensationAction: 'ReleaseInventoryReservationAsync',
      status: 'Compensated'
    }
  ]);
}
