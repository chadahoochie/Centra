export interface LeadershipState {
  status?: string;
  service?: string;
  version?: string;
  timestampUtc?: string;
  role: 'Active' | 'Standby';
  isLeader: boolean;
  leaderEndpoint?: string | null;
  leaseTtlSeconds?: number;
}
