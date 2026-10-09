import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    redirectTo: 'overview'
  },
  {
    path: 'overview',
    loadComponent: () =>
      import('./features/overview/overview.component').then(m => m.OverviewComponent)
  },
  {
    path: 'topology',
    loadComponent: () =>
      import('./features/topology/topology.component').then(m => m.TopologyComponent)
  },
  {
    path: 'actors',
    loadComponent: () =>
      import('./features/actors/actors.component').then(m => m.ActorsComponent)
  },
  {
    path: 'workflows',
    loadComponent: () =>
      import('./features/workflows/workflows.component').then(m => m.WorkflowsComponent)
  },
  {
    path: 'components',
    loadComponent: () =>
      import('./features/components/components.component').then(m => m.ComponentsComponent)
  },
  {
    path: 'resilience',
    loadComponent: () =>
      import('./features/resilience/resilience-editor.component').then(
        m => m.ResilienceEditorComponent
      )
  },
  {
    path: 'events',
    loadComponent: () =>
      import('./features/events/event-log.component').then(m => m.EventLogComponent)
  },
  {
    path: '**',
    redirectTo: 'overview'
  }
];
