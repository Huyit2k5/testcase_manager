import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { I18nService, Permissions, ToastService } from 'test-case-management';
import { LocalAuthService, authInterceptor } from './local-auth';

const session = (expiresAt: string) => ({ accessToken: 't', expiresAt, userId: 'u1', userName: 'tester', roles: ['Tester'] });
const validSession = () => JSON.stringify(session(new Date(Date.now() + 3600_000).toISOString()));

describe('LocalAuthService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
  });

  it('starts signed out and allows nothing', () => {
    const auth = TestBed.inject(LocalAuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.can(Permissions.TestCases.Create)).toBe(false);
  });

  it('keeps the session after login and reads the granted policies', async () => {
    const auth = TestBed.inject(LocalAuthService);
    const http = TestBed.inject(HttpTestingController);

    auth.login('tester', 'pw').subscribe();
    http.expectOne('/api/auth/login').flush(session(new Date(Date.now() + 3600_000).toISOString()));
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.ready()).toBe(false); // the permissions are not known yet
    expect(auth.roleLabel()).toBe('Tester');

    const loaded = auth.loadPermissions();
    http.expectOne('/api/abp/application-configuration?includeLocalizationResources=false')
      .flush({ auth: { grantedPolicies: { [Permissions.TestCases.Create]: true, [Permissions.SignOff.Approve]: false } } });
    await loaded;

    expect(auth.can(Permissions.TestCases.Create)).toBe(true);
    expect(auth.can(Permissions.SignOff.Approve)).toBe(false);
    expect(auth.ready()).toBe(true);

    auth.logout(false);
    expect(auth.ready()).toBe(false);
  });

  it('drops an expired session on start-up without calling the API', async () => {
    localStorage.setItem('tcm.session', JSON.stringify(session(new Date(Date.now() - 1000).toISOString())));
    const auth = TestBed.inject(LocalAuthService);

    await auth.restore();

    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('tcm.session')).toBeNull();
    TestBed.inject(HttpTestingController).verify();
  });

  it('signs out and forgets the permissions', () => {
    const auth = TestBed.inject(LocalAuthService);
    localStorage.setItem('tcm.session', validSession());
    auth.logout(false);
    expect(auth.token).toBeNull();
  });
});

describe('authInterceptor', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
  });

  it('sends the token to the API', () => {
    localStorage.setItem('tcm.session', validSession());
    TestBed.inject(HttpClient).get('/api/x').subscribe();
    expect(TestBed.inject(HttpTestingController).expectOne('/api/x').request.headers.get('Authorization')).toBe('Bearer t');
  });

  it('signs the user out and says so, in the chosen language, when the API answers 401', () => {
    localStorage.setItem('tcm.session', validSession());
    TestBed.inject(I18nService).use('vi');
    const auth = TestBed.inject(LocalAuthService);
    expect(auth.isAuthenticated()).toBe(true);

    TestBed.inject(HttpClient).get('/api/x').subscribe({ error: () => undefined });
    TestBed.inject(HttpTestingController).expectOne('/api/x').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('tcm.session')).toBeNull();
    expect(TestBed.inject(ToastService).toasts().map(t => t.text)).toEqual(['Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.']);
  });
});
