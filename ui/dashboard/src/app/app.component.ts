import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { TopologyStateService } from './core/services/topology-state.service';
import { AuthService } from './core/services/auth.service';
import { NotificationService } from './core/services/notification.service';
import { ClusterSelectorComponent } from './shared/components/cluster-selector.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    FormsModule,
    ClusterSelectorComponent
  ],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppComponent {
  readonly topologyState = inject(TopologyStateService);
  readonly auth = inject(AuthService);
  readonly notifications = inject(NotificationService);

  readonly isAuthModalOpen = signal<boolean>(false);
  tempClusterToken = this.auth.clusterToken();
  tempAdminToken = this.auth.adminToken();

  saveAuthTokens(): void {
    this.auth.setClusterToken(this.tempClusterToken);
    this.auth.setAdminToken(this.tempAdminToken);
    this.isAuthModalOpen.set(false);
    this.notifications.success('Security tokens updated successfully.');
    this.topologyState.refreshAll();
  }
}
