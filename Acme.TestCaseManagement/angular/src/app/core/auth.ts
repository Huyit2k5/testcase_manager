import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, firstValueFrom, tap, throwError } from 'rxjs';
import { ToastService } from './core';
import { I18nService } from './i18n/i18n';

export interface Session { accessToken: string; expiresAt: string; userId: string; userName: string; roles: string[] }

/** The permission names of the module, as ABP defines them (TestCaseManagementPermissions). */
export const Permissions = {
  TestCases: { Default: 'TestCaseManagement.TestCases', Create: 'TestCaseManagement.TestCases.Create', Update: 'TestCaseManagement.TestCases.Update', Delete: 'TestCaseManagement.TestCases.Delete', Approve: 'TestCaseManagement.TestCases.Approve' },
  TestSuites: { Default: 'TestCaseManagement.TestSuites', Manage: 'TestCaseManagement.TestSuites.Manage' },
  TestPlans: { Default: 'TestCaseManagement.TestPlans', Manage: 'TestCaseManagement.TestPlans.Manage' },
  TestRuns: { Default: 'TestCaseManagement.TestRuns', Execute: 'TestCaseManagement.TestRuns.Execute' },
  Requirements: { Default: 'TestCaseManagement.Requirements', Manage: 'TestCaseManagement.Requirements.Manage' },
  QualityGates: { Default: 'TestCaseManagement.QualityGates', Manage: 'TestCaseManagement.QualityGates.Manage' },
  SignOff: { Default: 'TestCaseManagement.SignOff', Approve: 'TestCaseManagement.SignOff.Approve' },
  SharedSteps: { Default: 'TestCaseManagement.SharedSteps', Manage: 'TestCaseManagement.SharedSteps.Manage' },
  ApiKeys: { Default: 'TestCaseManagement.ApiKeys', Manage: 'TestCaseManagement.ApiKeys.Manage' },
} as const;

const STORAGE_KEY = 'tcm.session';
const LOGIN_URL = '/api/auth/login';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  readonly session = signal<Session | null>(this.read());
  /** Policies granted to the signed-in user, from ABP's application configuration. */
  private readonly granted = signal<ReadonlySet<string>>(new Set());

  readonly isAuthenticated = computed(() => this.session() !== null);
  /**
   * True once the permissions of the signed-in user are known. The screen shows its chrome (menu, buttons) only then,
   * so that nothing can be clicked while the sign-in is still finishing, and no button appears and then disappears.
   */
  readonly ready = signal(false);
  readonly user = computed(() => this.session());
  /** What to show as the user's role: the first role, or the user name when there is none. */
  readonly roleLabel = computed(() => this.session()?.roles[0] ?? this.session()?.userName ?? '');

  get token(): string | null { return this.session()?.accessToken ?? null; }

  can(permission: string): boolean { return this.granted().has(permission); }

  login(userName: string, password: string) {
    return this.http.post<Session>(LOGIN_URL, { userName, password }).pipe(
      tap(session => {
        this.session.set(session);
        this.write(session);
      }),
    );
  }

  /** Reads the permissions of the signed-in user; call after login and when the app starts. */
  async loadPermissions(): Promise<void> {
    if (!this.session()) {
      this.granted.set(new Set());
      this.ready.set(false);
      return;
    }
    const config = await firstValueFrom(
      this.http.get<{ auth: { grantedPolicies: Record<string, boolean> } }>('/api/abp/application-configuration?includeLocalizationResources=false'),
    );
    this.granted.set(new Set(Object.entries(config.auth.grantedPolicies).filter(([, v]) => v).map(([k]) => k)));
    this.ready.set(true);
  }

  /** Called on app start: drops an expired session, loads permissions for a valid one. */
  async restore(): Promise<void> {
    const session = this.session();
    if (session && new Date(session.expiresAt).getTime() <= Date.now()) {
      this.clear();
      return;
    }
    try {
      await this.loadPermissions();
    } catch {
      this.clear();
    }
  }

  logout(redirect = true): void {
    this.clear();
    if (redirect) {
      void this.router.navigate(['/login']);
    }
  }

  private clear(): void {
    this.session.set(null);
    this.granted.set(new Set());
    this.ready.set(false);
    try { localStorage.removeItem(STORAGE_KEY); } catch { /* storage may be unavailable */ }
  }

  private read(): Session | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as Session) : null;
    } catch {
      return null;
    }
  }

  private write(session: Session): void {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(session)); } catch { /* storage may be unavailable */ }
  }
}

/** Sends the bearer token to the API and signs the user out when the API says the token is no longer good. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  // Resolved here: the error callback below runs after the injection context is gone.
  const toast = inject(ToastService);
  const i18n = inject(I18nService);
  const token = auth.token;
  const outgoing = token && request.url.startsWith('/api/') && request.url !== LOGIN_URL
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(outgoing).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && request.url !== LOGIN_URL && auth.isAuthenticated()) {
        toast.info(i18n.t('auth.sessionEnded'));
        auth.logout();
      }
      return throwError(() => error);
    }),
  );
};

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
