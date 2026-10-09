import { Component, ChangeDetectionStrategy, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SseStreamService, SseEventWrapper } from '../../core/services/sse-stream.service';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-event-log',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="events-page">
      <div class="header-box">
        <div>
          <h2 class="page-title">Real-Time SSE Event Ticker</h2>
          <p class="page-desc">
            Direct high-speed streaming from <code>/api/v1/sync/stream</code> and <code>/api/v1/resilience/stream</code> via native browser EventSource.
          </p>
        </div>
        <div class="stream-controls">
          <div class="connection-pill" [class.connected]="sse.isSyncConnected()">
            <span class="dot"></span>
            {{ sse.isSyncConnected() ? 'Sync SSE Live' : 'Connecting...' }}
          </div>
          <div class="connection-pill" [class.connected]="sse.isResilienceConnected()">
            <span class="dot"></span>
            {{ sse.isResilienceConnected() ? 'Resilience SSE Live' : 'Connecting...' }}
          </div>
          <button class="btn btn-secondary" (click)="togglePause()">
            {{ isPaused() ? '▶ Resume Stream' : '⏸ Pause Stream' }}
          </button>
          <button class="btn btn-secondary" (click)="clearEvents()">
            Clear
          </button>
        </div>
      </div>

      <!-- Filter Bar -->
      <div class="filter-bar">
        <div class="filter-group">
          <label>Filter Channel:</label>
          <select class="select-control" [value]="selectedFilter()" (change)="onFilterChange($event)">
            <option value="all">All Event Streams</option>
            <option value="component">Component Catalog Sync</option>
            <option value="resilience">Resilience Policy Mutations</option>
          </select>
        </div>
        <span class="ring-buffer-info">
          Fixed Ring Buffer: {{ filteredEvents().length }} / {{ maxCapacity }} items (Zero Memory Drag)
        </span>
      </div>

      <!-- Events List -->
      <div class="events-card">
        @if (filteredEvents().length === 0) {
          <div class="empty-state">
            <p>Waiting for real-time events...</p>
            <span class="subtext">Mutate a component or resilience policy to observe instant SSE dispatch.</span>
          </div>
        } @else {
          <div class="events-list">
            @for (item of filteredEvents(); track $index) {
              <div class="event-row" [class]="'event-' + item.type">
                <div class="row-header">
                  <span class="event-badge">{{ item.type | uppercase }}</span>
                  <span class="event-timestamp">{{ item.timestamp | date:'HH:mm:ss.SSS' }}</span>
                </div>
                <div class="event-json">
                  <code>{{ formatJson(item.payload) }}</code>
                </div>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .events-page {
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

    .stream-controls {
      display: flex;
      align-items: center;
      gap: 10px;
      flex-wrap: wrap;
    }

    .connection-pill {
      display: flex;
      align-items: center;
      gap: 6px;
      background: #0f172a;
      border: 1px solid #334155;
      padding: 4px 10px;
      border-radius: 9999px;
      font-size: 0.75rem;
      color: #94a3b8;
      font-weight: 600;
    }

    .connection-pill.connected {
      border-color: rgba(52, 211, 153, 0.4);
      color: #34d399;
    }

    .connection-pill .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: #94a3b8;
    }

    .connection-pill.connected .dot {
      background: #34d399;
      box-shadow: 0 0 6px #34d399;
    }

    .btn {
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 0.8rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }

    .btn-secondary { background: #334155; color: #f8fafc; }
    .btn-secondary:hover { background: #475569; }

    .filter-bar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 10px 16px;
      font-size: 0.85rem;
      flex-wrap: wrap;
      gap: 10px;
    }

    .filter-group {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .filter-group label {
      color: #94a3b8;
    }

    .select-control {
      background: #0f172a;
      border: 1px solid #334155;
      color: #f8fafc;
      padding: 4px 10px;
      border-radius: 6px;
      outline: none;
    }

    .ring-buffer-info {
      font-size: 0.75rem;
      color: #64748b;
      font-family: monospace;
    }

    .events-card {
      background: #1e293b;
      border: 1px solid #334155;
      border-radius: 8px;
      padding: 16px;
    }

    .empty-state {
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

    .events-list {
      display: flex;
      flex-direction: column;
      gap: 10px;
      max-height: 600px;
      overflow-y: auto;
    }

    .event-row {
      background: #0f172a;
      border: 1px solid #334155;
      border-left: 4px solid #38bdf8;
      border-radius: 6px;
      padding: 10px 14px;
    }

    .event-row.event-resilience {
      border-left-color: #c084fc;
    }

    .row-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 6px;
    }

    .event-badge {
      font-size: 0.7rem;
      font-weight: 700;
      color: #38bdf8;
      letter-spacing: 0.05em;
    }

    .event-row.event-resilience .event-badge {
      color: #c084fc;
    }

    .event-timestamp {
      font-size: 0.75rem;
      color: #64748b;
      font-family: monospace;
    }

    .event-json code {
      font-size: 0.8rem;
      color: #e2e8f0;
      word-break: break-all;
    }
  `]
})
export class EventLogComponent implements OnInit, OnDestroy {
  readonly sse = inject(SseStreamService);

  readonly maxCapacity = 150;
  readonly events = signal<SseEventWrapper[]>([]);
  readonly selectedFilter = signal<string>('all');
  readonly isPaused = signal<boolean>(false);

  private eventSub: Subscription | null = null;

  readonly filteredEvents = computed(() => {
    const f = this.selectedFilter();
    const list = this.events();
    if (f === 'all') return list;
    return list.filter(e => e.type === f);
  });

  ngOnInit(): void {
    this.sse.connectToSyncStream();
    this.sse.connectToResilienceStream();

    this.eventSub = this.sse.allEvents$.subscribe(evt => {
      if (!this.isPaused()) {
        // High-performance ring buffer update: strictly bounded to maxCapacity
        this.events.update(list => [evt, ...list].slice(0, this.maxCapacity));
      }
    });
  }

  togglePause(): void {
    this.isPaused.update(p => !p);
  }

  clearEvents(): void {
    this.events.set([]);
  }

  onFilterChange(event: Event): void {
    const select = event.target as HTMLSelectElement;
    if (select) {
      this.selectedFilter.set(select.value);
    }
  }

  formatJson(payload: any): string {
    if (typeof payload === 'string') return payload;
    try {
      return JSON.stringify(payload);
    } catch {
      return String(payload);
    }
  }

  ngOnDestroy(): void {
    if (this.eventSub) {
      this.eventSub.unsubscribe();
    }
  }
}
