import { inject } from '@angular/core';
import { CanActivateFn, ResolveFn, Router, Routes } from '@angular/router';
import { AuthService, Permissions } from './core/auth';
import { tcmMenu } from './menu';
import { TCM_BASE_PATH, TCM_FEATURES, TcmFeatures } from './core/host';
import { I18nService } from './core/i18n/i18n';
import { TcmShellComponent } from './shell';

/** The title of a page: the text in the language of the user at the time of the navigation, and the key for hosts that retitle on a language switch. */
const titleOf = (key: string): { title: ResolveFn<string>; data: { titleKey: string } } => ({
  title: () => inject(I18nService).t(key),
  data: { titleKey: key },
});

/** Lets the page open only when its part of the module is on; otherwise the user goes to the repository. */
const featureOn = (feature: keyof TcmFeatures): CanActivateFn => () =>
  inject(TCM_FEATURES)[feature] || inject(Router).parseUrl(`${inject(TCM_BASE_PATH)}/repository`);

/**
 * Lets the page open only for someone who may use it (the API refuses the data anyway; this keeps the page from opening
 * as an empty screen full of "no permission" messages). Otherwise the user goes to the first page of the module that
 * is allowed, or, with none, out of the module.
 */
const allowedBy = (permission: string): CanActivateFn => () => {
  const auth = inject(AuthService);
  if (auth.can(permission)) { return true; }
  const base = inject(TCM_BASE_PATH);
  const router = inject(Router);
  const other = tcmMenu(inject(TCM_FEATURES)).find(item => item.permission && auth.can(item.permission));
  if (other) { return router.parseUrl(`${base}/${other.path}`); }
  // Out of the module; a module mounted at the root has nowhere else to send the user, so it stays closed.
  return base ? router.parseUrl('/') : false;
};

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
        { path: 'dashboard', canActivate: [...canActivate, allowedBy(Permissions.TestRuns.Default)], ...titleOf('title.dashboard'), loadComponent: () => import('./features/dashboard/dashboard').then(m => m.DashboardComponent) },
        { path: 'my-work', canActivate: [...canActivate, allowedBy(Permissions.TestRuns.Default)], ...titleOf('title.myWork'), loadComponent: () => import('./features/my-work/my-work').then(m => m.MyWorkComponent) },
        { path: 'projects', canActivate: [...canActivate, allowedBy(Permissions.Projects.Default)], ...titleOf('title.projects'), loadComponent: () => import('./features/projects/projects').then(m => m.ProjectsComponent) },
        { path: 'repository', canActivate: [...canActivate, allowedBy(Permissions.TestCases.Default)], ...titleOf('title.repository'), loadComponent: () => import('./features/repository/repository').then(m => m.RepositoryComponent) },
        { path: 'shared-steps', canActivate: [...canActivate, allowedBy(Permissions.SharedSteps.Default)], ...titleOf('title.sharedSteps'), loadComponent: () => import('./features/shared-steps/shared-steps').then(m => m.SharedStepsComponent) },
        { path: 'runs', canActivate: [...canActivate, allowedBy(Permissions.TestPlans.Default)], ...titleOf('title.runs'), loadComponent: () => import('./features/runs/runs').then(m => m.RunsComponent) },
        { path: 'runs/:id', canActivate: [...canActivate, allowedBy(Permissions.TestPlans.Default)], ...titleOf('title.run'), loadComponent: () => import('./features/runs/run-detail').then(m => m.RunDetailComponent) },
        { path: 'traceability', canActivate: [...canActivate, allowedBy(Permissions.Requirements.Default)], ...titleOf('title.traceability'), loadComponent: () => import('./features/traceability/traceability').then(m => m.TraceabilityComponent) },
        { path: 'quality', canActivate: [...canActivate, allowedBy(Permissions.QualityGates.Default)], ...titleOf('title.quality'), loadComponent: () => import('./features/quality/quality').then(m => m.QualityComponent) },
        { path: 'automation', canActivate: [...canActivate, featureOn('automation'), allowedBy(Permissions.ApiKeys.Default)], ...titleOf('title.automation'), loadComponent: () => import('./features/automation/automation').then(m => m.AutomationComponent) },
      ],
    },
  ];
}
