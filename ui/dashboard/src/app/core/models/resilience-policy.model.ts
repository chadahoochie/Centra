export interface ResiliencePolicyDto {
  policyName: string;
  timeoutSeconds?: number | null;
  maxRetryAttempts?: number | null;
  circuitBreakerFailureRatio?: number | null;
  circuitBreakerDurationSeconds?: number | null;
  rateLimitPermits?: number | null;
  rateLimitPeriodSeconds?: number | null;
  scope?: string | null;
}

export interface ResilienceSyncEventDto {
  action: string;
  policyName: string;
  policy?: ResiliencePolicyDto | null;
  revision: number;
  timestampUtc: string;
}
