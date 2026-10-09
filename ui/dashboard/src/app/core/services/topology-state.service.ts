import { Injectable, signal, computed, inject, OnDestroy } from '@angular/core';
import { ControlPlaneApiService } from './control-plane-api.service';
import { SseStreamService } from './sse-stream.service';
import { ClusterNode, TopologySyncEvent } from '../models/cluster-node.model';
import { LeadershipState } from '../models/leadership-state.model';
import { Subscription } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class TopologyStateService implements OnDestroy {
  private readonly api = inject(ControlPlaneApiService);
  private readonly sse = inject(SseStreamService);

  // Core Signals
  readonly activeCluster = signal<string>('all');
  readonly allNodes = signal<ClusterNode[]>([]);
  readonly leadership = signal<LeadershipState>({
    role: 'Active',
    isLeader: true,
    leaseTtlSeconds: 4.0,
    status: 'Healthy'
  });
  readonly lastUpdated = signal<Date>(new Date());
  readonly isPolling = signal<boolean>(false);

  // Computed Signals
  readonly filteredNodes = computed(() => {
    const cluster = this.activeCluster();
    const nodes = this.allNodes();
    return cluster === 'all' ? nodes : nodes.filter(n => (n.clusterId || 'default') === cluster);
  });

  readonly clusterList = computed(() => {
    const clusters = new Set<string>();
    for (const node of this.allNodes()) {
      clusters.add(node.clusterId || 'default');
    }
    return Array.from(clusters).sort();
  });

  readonly healthyNodesCount = computed(() => {
    return this.filteredNodes().filter(n => n.status.toLowerCase() === 'healthy').length;
  });

  private pollIntervalId: any = null;
  private leaseCountdownIntervalId: any = null;
  private sseSub: Subscription | null = null;

  constructor() {
    this.refreshAll();
    this.startPolling();
    this.startLeaseCountdown();

    this.sse.connectToSyncStream();
    this.sse.connectToResilienceStream();
  }

  setCluster(cluster: string): void {
    this.activeCluster.set(cluster);
  }

  refreshAll(): void {
    this.isPolling.set(true);

    this.api.getHealth().subscribe({
      next: health => {
        const current = this.leadership();
        this.leadership.set({
          ...health,
          leaseTtlSeconds: health.isLeader ? 4.0 : 0
        });
        this.lastUpdated.set(new Date());
      },
      error: err => {
        console.warn('Failed to fetch health status:', err);
      }
    });

    this.api.getTopology().subscribe({
      next: nodes => {
        this.allNodes.set(nodes);
        this.lastUpdated.set(new Date());
        this.isPolling.set(false);
      },
      error: err => {
        console.warn('Failed to fetch topology:', err);
        this.isPolling.set(false);
      }
    });
  }

  handleTopologySyncEvent(event: TopologySyncEvent): void {
    if (event.action === 'FullSync' && event.activeNodes) {
      this.allNodes.set(event.activeNodes);
    } else if (event.action === 'Joined' && event.node) {
      this.allNodes.update(nodes => [
        ...nodes.filter(n => n.instanceId !== event.node!.instanceId),
        event.node!
      ]);
    } else if (event.action === 'Left' && event.node) {
      this.allNodes.update(nodes => nodes.filter(n => n.instanceId !== event.node!.instanceId));
    }
    this.lastUpdated.set(new Date());
  }

  private startPolling(): void {
    if (typeof window !== 'undefined') {
      this.pollIntervalId = setInterval(() => {
        this.refreshAll();
      }, 4000);
    }
  }

  private startLeaseCountdown(): void {
    if (typeof window !== 'undefined') {
      this.leaseCountdownIntervalId = setInterval(() => {
        const current = this.leadership();
        if (current.isLeader && current.leaseTtlSeconds !== undefined) {
          const next = Math.max(0, Number((current.leaseTtlSeconds - 0.2).toFixed(1)));
          // Reset to 4.0 after hitting 2.8s renewal cycle
          const resetTtl = next <= 2.8 ? 4.0 : next;
          this.leadership.set({
            ...current,
            leaseTtlSeconds: resetTtl
          });
        }
      }, 200);
    }
  }

  ngOnDestroy(): void {
    if (this.pollIntervalId) {
      clearInterval(this.pollIntervalId);
    }
    if (this.leaseCountdownIntervalId) {
      clearInterval(this.leaseCountdownIntervalId);
    }
    if (this.sseSub) {
      this.sseSub.unsubscribe();
    }
  }
}
