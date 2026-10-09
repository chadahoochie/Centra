export interface WorkflowDefinitionDto {
  name: string;
  workflowType: string;
  inputType: string;
  outputType: string;
}

export interface WorkflowActivityDto {
  name: string;
  activityType: string;
  inputType: string;
  outputType: string;
}

export interface WorkflowInstanceStateDto {
  instanceId: string;
  workflowName: string;
  status: string;
  customStatus?: string | null;
  createdAt: string;
  lastUpdatedAt: string;
  failureDetails?: string | null;
}

export interface WorkflowHistoryEventDto {
  id?: string;
  eventType: string;
  activityName?: string;
  status?: string;
  timestampUtc: string;
  details?: string;
  isRedacted?: boolean;
}
