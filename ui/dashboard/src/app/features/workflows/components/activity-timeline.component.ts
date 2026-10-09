import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorkflowHistoryEventDto } from '../../../core/models/workflow.model';

@Component({
  selector: 'app-activity-timeline',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="timeline-container">
      <div class="hipaa-guardrail">
        <span class="hipaa-badge">🛡️ HIPAA Zero-PHI Guardrail Enforced</span>
        <span class="hipaa-text">
          Control Plane sanitizes all activity inputs/outputs. 0 bytes of PHI payload traverse the coordination bus.
        </span>
      </div>

      @if (events().length === 0) {
        <div class="empty-timeline">No execution events recorded for this instance.</div>
      } @else {
        <div class="timeline-list">
          @for (event of events(); track $index) {
            <div class="timeline-item" [class.failed]="isFailed(event.eventType)">
              <div class="timeline-dot"></div>
              <div class="timeline-content">
                <div class="event-header">
                  <span class="event-type">{{ event.eventType }}</span>
                  <span class="event-time">{{ event.timestampUtc | date:'HH:mm:ss.SSS' }}</span>
                </div>
                @if (event.activityName) {
                  <div class="activity-name">Activity: <strong>{{ event.activityName }}</strong></div>
                }
                @if (event.details) {
                  <div class="event-details">{{ event.details }}</div>
                }
                <div class="redaction-badge">
                  <span>Payload: <code>[REDACTED_SECURE_PAYLOAD]</code></span>
                </div>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .timeline-container {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    .hipaa-guardrail {
      background: rgba(16, 185, 129, 0.1);
      border: 1px solid rgba(16, 185, 129, 0.3);
      border-radius: 6px;
      padding: 10px 14px;
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }

    .hipaa-badge {
      font-size: 0.75rem;
      font-weight: 700;
      color: #34d399;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .hipaa-text {
      font-size: 0.8rem;
      color: #94a3b8;
    }

    .empty-timeline {
      padding: 20px;
      text-align: center;
      color: #64748b;
      font-size: 0.85rem;
    }

    .timeline-list {
      position: relative;
      padding-left: 20px;
    }

    .timeline-list::before {
      content: '';
      position: absolute;
      top: 10px;
      bottom: 10px;
      left: 6px;
      width: 2px;
      background: #334155;
    }

    .timeline-item {
      position: relative;
      margin-bottom: 16px;
    }

    .timeline-dot {
      position: absolute;
      left: -20px;
      top: 6px;
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: #38bdf8;
      border: 2px solid #0f172a;
    }

    .timeline-item.failed .timeline-dot {
      background: #f87171;
    }

    .timeline-content {
      background: #0f172a;
      border: 1px solid #334155;
      border-radius: 6px;
      padding: 12px;
    }

    .event-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 6px;
    }

    .event-type {
      font-weight: 700;
      font-size: 0.85rem;
      color: #38bdf8;
    }

    .timeline-item.failed .event-type {
      color: #f87171;
    }

    .event-time {
      font-size: 0.75rem;
      color: #64748b;
      font-family: monospace;
    }

    .activity-name {
      font-size: 0.8rem;
      color: #f8fafc;
      margin-bottom: 4px;
    }

    .event-details {
      font-size: 0.8rem;
      color: #94a3b8;
      margin-bottom: 6px;
    }

    .redaction-badge {
      font-size: 0.75rem;
      color: #34d399;
      background: rgba(52, 211, 153, 0.1);
      padding: 2px 8px;
      border-radius: 4px;
      display: inline-block;
    }
  `]
})
export class ActivityTimelineComponent {
  readonly events = input<WorkflowHistoryEventDto[]>([]);

  isFailed(eventType: string): boolean {
    const t = eventType.toLowerCase();
    return t.includes('fail') || t.includes('error') || t.includes('abort');
  }
}
