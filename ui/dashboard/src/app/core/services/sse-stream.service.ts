import { Injectable, signal, OnDestroy } from '@angular/core';
import { Observable, Subject } from 'rxjs';
import { ComponentSyncEventDto } from '../models/component-definition.model';
import { ResilienceSyncEventDto } from '../models/resilience-policy.model';

export interface SseEventWrapper {
  type: 'component' | 'resilience' | 'raw';
  timestamp: Date;
  payload: any;
}

@Injectable({
  providedIn: 'root'
})
export class SseStreamService implements OnDestroy {
  readonly isSyncConnected = signal<boolean>(false);
  readonly isResilienceConnected = signal<boolean>(false);

  private syncEventSource: EventSource | null = null;
  private resilienceEventSource: EventSource | null = null;

  private readonly componentEventsSubject = new Subject<ComponentSyncEventDto>();
  private readonly resilienceEventsSubject = new Subject<ResilienceSyncEventDto>();
  private readonly allEventsSubject = new Subject<SseEventWrapper>();

  readonly componentEvents$ = this.componentEventsSubject.asObservable();
  readonly resilienceEvents$ = this.resilienceEventsSubject.asObservable();
  readonly allEvents$ = this.allEventsSubject.asObservable();

  connectToSyncStream(): Observable<ComponentSyncEventDto> {
    if (!this.syncEventSource && typeof window !== 'undefined' && typeof EventSource !== 'undefined') {
      const url = '/api/v1/sync/stream?appId=centra-dashboard';
      this.syncEventSource = new EventSource(url);

      this.syncEventSource.onopen = () => {
        this.isSyncConnected.set(true);
      };

      this.syncEventSource.onmessage = (event: MessageEvent) => {
        try {
          const parsed = JSON.parse(event.data) as ComponentSyncEventDto;
          this.componentEventsSubject.next(parsed);
          this.allEventsSubject.next({
            type: 'component',
            timestamp: new Date(),
            payload: parsed
          });
        } catch (err) {
          console.warn('Failed to parse sync SSE message:', err);
        }
      };

      this.syncEventSource.onerror = () => {
        this.isSyncConnected.set(false);
      };
    }

    return this.componentEvents$;
  }

  connectToResilienceStream(): Observable<ResilienceSyncEventDto> {
    if (!this.resilienceEventSource && typeof window !== 'undefined' && typeof EventSource !== 'undefined') {
      const url = '/api/v1/resilience/stream?appId=centra-dashboard';
      this.resilienceEventSource = new EventSource(url);

      this.resilienceEventSource.onopen = () => {
        this.isResilienceConnected.set(true);
      };

      this.resilienceEventSource.onmessage = (event: MessageEvent) => {
        try {
          const parsed = JSON.parse(event.data) as ResilienceSyncEventDto;
          this.resilienceEventsSubject.next(parsed);
          this.allEventsSubject.next({
            type: 'resilience',
            timestamp: new Date(),
            payload: parsed
          });
        } catch (err) {
          console.warn('Failed to parse resilience SSE message:', err);
        }
      };

      this.resilienceEventSource.onerror = () => {
        this.isResilienceConnected.set(false);
      };
    }

    return this.resilienceEvents$;
  }

  disconnectAll(): void {
    if (this.syncEventSource) {
      this.syncEventSource.close();
      this.syncEventSource = null;
      this.isSyncConnected.set(false);
    }
    if (this.resilienceEventSource) {
      this.resilienceEventSource.close();
      this.resilienceEventSource = null;
      this.isResilienceConnected.set(false);
    }
  }

  ngOnDestroy(): void {
    this.disconnectAll();
    this.componentEventsSubject.complete();
    this.resilienceEventsSubject.complete();
    this.allEventsSubject.complete();
  }
}
