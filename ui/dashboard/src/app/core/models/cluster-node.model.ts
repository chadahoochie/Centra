export interface ClusterNode {
  appId: string;
  instanceId: string;
  status: string;
  registeredAtUtc: string;
  lastHeartbeatUtc: string;
  metadata?: Record<string, string>;
  clusterId: string;
}

export interface TopologySyncEvent {
  action: 'FullSync' | 'Joined' | 'Left';
  node?: ClusterNode;
  activeNodes?: ClusterNode[];
  clusterId?: string;
  timestampUtc?: string;
}
