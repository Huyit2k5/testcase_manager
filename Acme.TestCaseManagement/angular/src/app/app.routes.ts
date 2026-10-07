import { Routes } from '@angular/router';
import { createTestCaseManagementRoutes } from 'test-case-management';
import { authGuard } from './core/local-auth';

export const routes: Routes = [
  { path: 'login', title: 'title.login', loadComponent: () => import('./login/login').then(m => m.LoginComponent) },
  ...createTestCaseManagementRoutes({ canActivate: [authGuard] }),
  { path: '**', redirectTo: 'repository' },
];
