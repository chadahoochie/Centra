export enum ComponentType {
  StateStore = 0,
  PubSub = 1,
  LockStore = 2,
  Binding = 3,
  Resilience = 4
}

export interface ComponentDefinition {
  name: string;
  type: number | string;
  provider: string;
  version?: string;
  metadata?: Record<string, string>;
  clusterId?: string;
}

export interface ComponentEntryDto {
  definition: ComponentDefinition;
  revision: number;
  updatedAtUtc: string;
}

export interface ComponentSyncEventDto {
  action: number | string;
  definition?: ComponentDefinition | null;
  componentName: string;
  revision: number;
  timestampUtc: string;
}
