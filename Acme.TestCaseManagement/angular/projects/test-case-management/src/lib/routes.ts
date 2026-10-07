import { inject } from '@angular/core';
import { CanActivateFn, ResolveFn, Routes } from '@angular/router';
import { I18nService } from './core/i18n/i18n';
import { TcmShellComponent } from './shell';

/** The title of a page: the text in the language of the user at the time of the navigation, and the key for hosts that retitle on a language switch. */
const titleOf = (key: string): { title: ResolveFn<string>; data: { titleKey: string } } => ({
  title: () => inject(I18nService).t(key),
  data: { titleKey: key },
});

export interface TcmRouteOptions {
  /** Guards that every page of the module sits behind: the host's own sign-in check (and, in ABP, its permission guard). */
  canActivate?: CanActivateFn[];
}

/**
 * The routes of the module. Mount them wherever the host wants (at the root, or under a path such as
 * 'test-case-management', in which case TCM_BASE_PATH must say so).
 */
export function createTestCaseManagementRoutes(options: TcmRouteOptions = {}): Routes {
  const canActivate = options.canActivate ?? [];
  return [
    {
      path: '',
      component: TcmShellComponent,
      children: [
        { path: '', pathMatch: 'full', redirectTo: 'repository' },
        { path: 'dashboard', canActivate, ...titleOf('title.dashboard'), loadComponent: () => import('./features/dashboard/dashboard').then(m => m.DashboardComponent) },
        { path: 'repository', canActivate, ...titleOf('title.repository'), loadComponent: () => import('./features/repository/repository').then(m => m.RepositoryComponent) },
        { path: 'shared-steps', canActivate, ...titleOf('title.sharedSteps'), loadComponent: () => import('./features/shared-steps/shared-steps').then(m => m.SharedStepsComponent) },
        { path: 'runs', canActivate, ...titleOf('title.runs'), loadComponent: () => import('./features/runs/runs').then(m => m.RunsComponent) },
        { path: 'runs/:id', canActivate, ...titleOf('title.run'), loadComponent: () => import('./features/runs/run-detail').then(m => m.RunDetailComponent) },
        { path: 'traceability', canActivate, ...titleOf('title.traceability'), loadComponent: () => import('./features/traceability/traceability').then(m => m.TraceabilityComponent) },
        { path: 'quality', canActivate, ...titleOf('title.quality'), loadComponent: () => import('./features/quality/quality').then(m => m.QualityComponent) },
        { path: 'automation', canActivate, ...titleOf('title.automation'), loadComponent: () => import('./features/automation/automation').then(m => m.AutomationComponent) },
      ],
    },
  ];
}
