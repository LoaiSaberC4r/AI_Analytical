import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', title: 'Overview | Camera Analytics', data: { mode: 'overview' },
    loadComponent: () => import('./features/dashboard/monitoring-workspace').then(x => x.MonitoringWorkspace) },
  { path: 'live', title: 'Live monitoring | Camera Analytics', data: { mode: 'live' },
    loadComponent: () => import('./features/dashboard/monitoring-workspace').then(x => x.MonitoringWorkspace) },
  { path: 'live/:cameraId', title: 'Camera monitoring | Camera Analytics', data: { mode: 'live' },
    loadComponent: () => import('./features/dashboard/monitoring-workspace').then(x => x.MonitoringWorkspace) },
  { path: 'sites', title: 'Sites | Camera Analytics', data: { kind: 'sites' },
    loadComponent: () => import('./features/catalog/catalog').then(x => x.Catalog) },
  { path: 'cameras', title: 'Cameras | Camera Analytics', data: { kind: 'cameras' },
    loadComponent: () => import('./features/catalog/catalog').then(x => x.Catalog) },
  { path: 'cameras/:cameraId/zones', title: 'Zones | Camera Analytics', data: { kind: 'zones' },
    loadComponent: () => import('./features/catalog/catalog').then(x => x.Catalog) },
  { path: 'analytics', title: 'Analytics | Camera Analytics',
    loadComponent: () => import('./features/analytics/analytics').then(x => x.Analytics) },
  { path: 'sessions', title: 'Sessions | Camera Analytics',
    loadComponent: () => import('./features/sessions/sessions').then(x => x.Sessions) },
  { path: 'sessions/:id', title: 'Session report | Camera Analytics',
    loadComponent: () => import('./features/sessions/session-report').then(x => x.SessionReportPage) },
  { path: '**', redirectTo: '' },
];
