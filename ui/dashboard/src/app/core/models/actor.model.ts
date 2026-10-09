export interface ActorActivationCountResponse {
  count: number;
}

export interface ActorPassivateResponse {
  passivated: boolean;
}

export interface HashRingNodePartition {
  nodeId: string;
  clusterId: string;
  hash: number;
  angle: number;
  isVirtual: boolean;
  virtualIndex: number;
}
