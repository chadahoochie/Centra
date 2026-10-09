import { Injectable, signal, computed } from '@angular/core';
import { HttpHeaders } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly clusterTokenKey = 'centra_cluster_token';
  private readonly adminTokenKey = 'centra_admin_token';

  readonly clusterToken = signal<string>(
    (typeof window !== 'undefined' && localStorage.getItem(this.clusterTokenKey)) || ''
  );

  readonly adminToken = signal<string>(
    (typeof window !== 'undefined' && localStorage.getItem(this.adminTokenKey)) || ''
  );

  readonly hasClusterToken = computed(() => this.clusterToken().trim().length > 0);
  readonly hasAdminToken = computed(() => this.adminToken().trim().length > 0);

  setClusterToken(token: string): void {
    const clean = token.trim();
    this.clusterToken.set(clean);
    if (typeof window !== 'undefined') {
      if (clean) {
        localStorage.setItem(this.clusterTokenKey, clean);
      } else {
        localStorage.removeItem(this.clusterTokenKey);
      }
    }
  }

  setAdminToken(token: string): void {
    const clean = token.trim();
    this.adminToken.set(clean);
    if (typeof window !== 'undefined') {
      if (clean) {
        localStorage.setItem(this.adminTokenKey, clean);
      } else {
        localStorage.removeItem(this.adminTokenKey);
      }
    }
  }

  getHeaders(): HttpHeaders {
    let headers = new HttpHeaders();
    const cToken = this.clusterToken();
    if (cToken) {
      headers = headers.set('X-Centra-Cluster-Token', cToken);
    }
    const aToken = this.adminToken();
    if (aToken) {
      headers = headers.set('X-Centra-Admin-Token', aToken);
    }
    return headers;
  }
}
