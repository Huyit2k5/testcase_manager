import { Routes } from '@angular/router';
import { authGuard } from './core/auth';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'repository' },
  { path: 'dashboard', canActivate: [authGuard], title: 'title.dashboard', loadComponent: () => import('./features/dashboard/dashboard').then(m => m.DashboardComponent) },
  { path: 'repository', canActivate: [authGuard], title: 'title.repository', loadComponent: () => import('./features/repository/repository').then(m => m.RepositoryComponent) },
  { path: 'runs', canActivate: [authGuard], title: 'title.runs', loadComponent: () => import('./features/runs/runs').then(m => m.RunsComponent) },
  { path: 'runs/:id', canActivate: [authGuard], title: 'title.run', loadComponent: () => import('./features/runs/run-detail').then(m => m.RunDetailComponent) },
  { path: 'traceability', canActivate: [authGuard], title: 'title.traceability', loadComponent: () => import('./features/traceability/traceability').then(m => m.TraceabilityComponent) },
  { path: 'quality', canActivate: [authGuard], title: 'title.quality', loadComponent: () => import('./features/quality/quality').then(m => m.QualityComponent) },
  { path: 'automation', canActivate: [authGuard], title: 'title.automation', loadComponent: () => import('./features/automation/automation').then(m => m.AutomationComponent) },
  { path: 'login', title: 'title.login', loadComponent: () => import('./features/login/login').then(m => m.LoginComponent) },
  { path: '**', redirectTo: 'repository' },
];
