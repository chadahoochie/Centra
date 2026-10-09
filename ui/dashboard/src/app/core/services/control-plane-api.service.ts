import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';
import { ClusterNode } from '../models/cluster-node.model';
import { LeadershipState } from '../models/leadership-state.model';
import { ComponentDefinition, ComponentEntryDto } from '../models/component-definition.model';
import { ResiliencePolicyDto } from '../models/resilience-policy.model';
import { ActorActivationCountResponse, ActorPassivateResponse } from '../models/actor.model';
import {
  WorkflowDefinitionDto,
  WorkflowActivityDto,
  WorkflowInstanceStateDto,
  WorkflowHistoryEventDto
} from '../models/workflow.model';

@Injectable({
  providedIn: 'root'
})
export class ControlPlaneApiService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = '/api/v1';

  getHealth(): Observable<LeadershipState> {
    return this.http.get<LeadershipState>(`${this.baseUrl}/health`, {
      headers: this.auth.getHeaders()
    });
  }

  getTopology(clusterId?: string): Observable<ClusterNode[]> {
    let params = new HttpParams();
    if (clusterId && clusterId !== 'all') {
      params = params.set('clusterId', clusterId);
    }
    return this.http.get<ClusterNode[]>(`${this.baseUrl}/topology`, {
      headers: this.auth.getHeaders(),
      params
    });
  }

  getComponents(): Observable<ComponentEntryDto[]> {
    return this.http.get<ComponentEntryDto[]>(`${this.baseUrl}/components`, {
      headers: this.auth.getHeaders()
    });
  }

  getComponent(name: string): Observable<ComponentDefinition> {
    return this.http.get<ComponentDefinition>(`${this.baseUrl}/components/${encodeURIComponent(name)}`, {
      headers: this.auth.getHeaders()
    });
  }

  upsertComponent(definition: ComponentDefinition): Observable<ComponentEntryDto> {
    return this.http.post<ComponentEntryDto>(`${this.baseUrl}/components`, definition, {
      headers: this.auth.getHeaders()
    });
  }

  deleteComponent(name: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/components/${encodeURIComponent(name)}`, {
      headers: this.auth.getHeaders()
    });
  }

  getResiliencePolicies(): Observable<ResiliencePolicyDto[]> {
    return this.http.get<ResiliencePolicyDto[]>(`${this.baseUrl}/resilience`, {
      headers: this.auth.getHeaders()
    });
  }

  getResiliencePolicy(name: string): Observable<ResiliencePolicyDto> {
    return this.http.get<ResiliencePolicyDto>(`${this.baseUrl}/resilience/${encodeURIComponent(name)}`, {
      headers: this.auth.getHeaders()
    });
  }

  upsertResiliencePolicy(policy: ResiliencePolicyDto): Observable<ResiliencePolicyDto> {
    return this.http.post<ResiliencePolicyDto>(`${this.baseUrl}/resilience`, policy, {
      headers: this.auth.getHeaders()
    });
  }

  deleteResiliencePolicy(name: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/resilience/${encodeURIComponent(name)}`, {
      headers: this.auth.getHeaders()
    });
  }

  getActorTypes(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/actors/types`, {
      headers: this.auth.getHeaders()
    });
  }

  getActorActivations(): Observable<ActorActivationCountResponse> {
    return this.http.get<ActorActivationCountResponse>(`${this.baseUrl}/actors/activations`, {
      headers: this.auth.getHeaders()
    });
  }

  passivateActor(actorType: string, actorId: string): Observable<ActorPassivateResponse> {
    return this.http.post<ActorPassivateResponse>(
      `${this.baseUrl}/actors/${encodeURIComponent(actorType)}/${encodeURIComponent(actorId)}/passivate`,
      {},
      { headers: this.auth.getHeaders() }
    );
  }

  getWorkflowDefinitions(): Observable<WorkflowDefinitionDto[]> {
    return this.http.get<WorkflowDefinitionDto[]>(`${this.baseUrl}/workflows/definitions`, {
      headers: this.auth.getHeaders()
    });
  }

  getWorkflowActivities(): Observable<WorkflowActivityDto[]> {
    return this.http.get<WorkflowActivityDto[]>(`${this.baseUrl}/workflows/activities`, {
      headers: this.auth.getHeaders()
    });
  }

  getWorkflowInstance(instanceId: string): Observable<WorkflowInstanceStateDto> {
    return this.http.get<WorkflowInstanceStateDto>(
      `${this.baseUrl}/workflows/instances/${encodeURIComponent(instanceId)}`,
      { headers: this.auth.getHeaders() }
    );
  }

  getWorkflowHistory(instanceId: string): Observable<WorkflowHistoryEventDto[]> {
    return this.http.get<WorkflowHistoryEventDto[]>(
      `${this.baseUrl}/workflows/instances/${encodeURIComponent(instanceId)}/history`,
      { headers: this.auth.getHeaders() }
    );
  }
}
