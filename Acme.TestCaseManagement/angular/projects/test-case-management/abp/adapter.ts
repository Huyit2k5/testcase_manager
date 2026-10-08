import { HTTP_INTERCEPTORS, HttpClient, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { EnvironmentInjector, EnvironmentProviders, Injectable, computed, inject, makeEnvironmentProviders, provideAppInitializer, runInInjectionContext } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ConfigStateService, EnvironmentService, PermissionService, RoutesService, SessionStateService, eLayoutType } from '@abp/ng.core';
import { ToasterService } from '@abp/ng.theme.shared';
import { Observable, catchError, map } from 'rxjs';
import { AuthService, TCM_API_URL, TCM_BASE_PATH, TCM_LANGUAGE, TCM_MENU, TCM_NOTIFIER, TCM_USER_DIRECTORY, TcmDirectoryUser, TcmUser, TcmUserDirectory, errorInterceptor } from 'test-case-management';

/** Where the module is mounted in the ABP application's router (see the route in app.routes.ts). */
export const TCM_ABP_BASE_PATH = '/test-case-management';
const ROOT_MENU = 'TestCaseManagement::Menu:TestCaseManagement';

/** Who is signed in and what they may do, read from what ABP already knows (its application configuration). */
@Injectable()
export class AbpAuthAdapter extends AuthService {
  private readonly config = inject(ConfigStateService);
  private readonly permissions = inject(PermissionService);

  private readonly current = toSignal(this.config.getOne$('currentUser'), { initialValue: this.config.getOne('currentUser') });

  readonly isAuthenticated = computed(() => this.current()?.isAuthenticated === true);
  readonly user = computed<TcmUser | null>(() => {
    const u = this.current();
    return u?.isAuthenticated ? { userId: u.id ?? '', userName: u.userName ?? '', roles: u.roles ?? [] } : null;
  });

  can(permission: string): boolean { return this.permissions.getGrantedPolicy(permission); }
}

/**
 * Shows the error of a failed call to the module's API (its message, in the user's language) through the host's
 * notifications. ABP's own error dialog only reacts to calls made with its RestService, and the module uses HttpClient.
 */
@Injectable()
export class TcmErrorInterceptor implements HttpInterceptor {
  private readonly injector = inject(EnvironmentInjector);

  intercept(request: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    if (!request.url.includes('/api/test-case-management/')) {
      return next.handle(request);
    }
    return runInInjectionContext(this.injector, () => errorInterceptor(request, r => next.handle(r)));
  }
}

/**
 * The people a test can be assigned to, from ABP's own APIs (the first 100 users by name): the user lookup, and when the
 * caller may not use it (it has a permission of its own, "AbpIdentity.UserLookup", that a standard application does not
 * define or grant) the list of Identity users, which needs "AbpIdentity.Users". For someone who holds neither the list is
 * empty, and the module hides the assignment.
 */
export function abpUserDirectory(): TcmUserDirectory {
  const http = inject(HttpClient);
  const api = inject(EnvironmentService).getApiUrl(undefined).replace(/\/+$/, '');
  type Person = { id: string; userName: string; name?: string | null; surname?: string | null; isActive?: boolean };
  const params = { maxResultCount: 100, sorting: 'userName' };
  const people = (items: Person[]): TcmDirectoryUser[] => items
    .filter(u => u.isActive !== false)
    .map(u => ({ id: u.id, userName: u.userName, displayName: [u.name, u.surname].filter(Boolean).join(' ').trim() || u.userName }));
  return {
    list: () => http.get<{ items: Person[] }>(`${api}/api/identity/users/lookup/search`, { params }).pipe(
      map(result => people(result.items)),
      catchError(() => http.get<{ items: Person[] }>(`${api}/api/identity/users`, { params }).pipe(map(result => people(result.items)))),
    ),
  };
}

/**
 * Connects the module to an ABP Angular application: its sign-in and permissions, its language switch, its toasts and
 * the address of its API. Add it to the application config, next to provideAbpCore().
 */
export function provideTestCaseManagementForAbp(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: AuthService, useClass: AbpAuthAdapter },
    { provide: HTTP_INTERCEPTORS, useClass: TcmErrorInterceptor, multi: true },
    { provide: TCM_API_URL, useFactory: () => inject(EnvironmentService).getApiUrl(undefined).replace(/\/+$/, '') },
    { provide: TCM_BASE_PATH, useValue: TCM_ABP_BASE_PATH },
    { provide: TCM_USER_DIRECTORY, useFactory: abpUserDirectory },
    {
      provide: TCM_LANGUAGE,
      useFactory: () => {
        const session = inject(SessionStateService);
        return toSignal(session.getLanguage$(), { initialValue: session.getLanguage() });
      },
    },
    {
      provide: TCM_NOTIFIER,
      useFactory: () => {
        const toaster = inject(ToasterService);
        return { success: (text: string) => toaster.success(text), info: (text: string) => toaster.info(text), error: (text: string) => toaster.error(text) };
      },
    },
  ]);
}

/** Adds the module's group and pages to the sidebar of an ABP application; each entry shows only for those who hold its permission. */
export function provideTestCaseManagementMenu(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      const routes = inject(RoutesService);
      routes.add([
        { name: ROOT_MENU, iconClass: 'fas fa-vial', order: 20, layout: eLayoutType.application },
        ...TCM_MENU.map(item => ({
          path: `${TCM_ABP_BASE_PATH}/${item.path}`,
          name: item.abpName,
          parentName: ROOT_MENU,
          order: item.order,
          layout: eLayoutType.application,
          requiredPolicy: item.policy,
        })),
      ]);
    }),
  ]);
}
