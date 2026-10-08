import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ResolveFn, RouterStateSnapshot, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { RepositoryComponent } from '../features/repository/repository';
import { TestSuiteService } from '../proxy/services';
import { TCM_MENU } from '../menu';
import { createTestCaseManagementRoutes } from '../routes';
import { AuthService, Permissions, TcmUser } from './auth';
import { ToastService } from './core';
import { TCM_API_URL, TCM_BASE_PATH, TCM_LANGUAGE, TCM_NOTIFIER, TcmNotifier } from './host';
import { I18nService, TranslatedTitleStrategy } from './i18n/i18n';

describe('the module inside a host', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('follows the language of the host, in any spelling of it, and leaves the switch to the host', () => {
    const hostLanguage = signal('vi-VN');
    TestBed.configureTestingModule({ providers: [{ provide: TCM_LANGUAGE, useValue: hostLanguage }] });
    const i18n = TestBed.inject(I18nService);

    expect(i18n.lang()).toBe('vi');
    expect(i18n.t('nav.repository')).toBe('Kho test case');

    i18n.use('en');
    expect(i18n.lang()).toBe('vi');
    expect(localStorage.getItem('tcm.lang')).toBeNull();

    hostLanguage.set('en-US');
    expect(i18n.lang()).toBe('en');
    expect(i18n.t('nav.repository')).toBe('Test repository');

    hostLanguage.set('fr');
    expect(i18n.lang()).toBe('en');
  });

  it('keeps its own language choice when the host has none', () => {
    TestBed.configureTestingModule({});
    const i18n = TestBed.inject(I18nService);
    i18n.use('vi');
    expect(i18n.lang()).toBe('vi');
    expect(localStorage.getItem('tcm.lang')).toBe('vi');
  });

  it('sends messages to the notifications of the host and shows no toast of its own', () => {
    const received: string[] = [];
    const notifier: TcmNotifier = {
      success: text => received.push('success:' + text),
      info: text => received.push('info:' + text),
      error: text => received.push('error:' + text),
    };
    TestBed.configureTestingModule({ providers: [{ provide: TCM_NOTIFIER, useValue: notifier }] });
    const toast = TestBed.inject(ToastService);

    toast.success('a');
    toast.info('b');
    toast.error('c');

    expect(received).toEqual(['success:a', 'info:b', 'error:c']);
    expect(toast.toasts()).toEqual([]);
  });

  it('shows its own toasts when the host has no notifications', () => {
    TestBed.configureTestingModule({});
    const toast = TestBed.inject(ToastService);
    toast.success('a');
    expect(toast.toasts().map(t => t.text)).toEqual(['a']);
  });

  it('calls the API at the address the host gives, and at the same origin when it gives none', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: TCM_API_URL, useValue: 'https://api.example.com' }],
    });
    TestBed.inject(TestSuiteService).tree().subscribe();
    TestBed.inject(HttpTestingController).expectOne('https://api.example.com/api/test-case-management/suites/tree');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    TestBed.inject(TestSuiteService).tree().subscribe();
    TestBed.inject(HttpTestingController).expectOne('/api/test-case-management/suites/tree');
  });

  it('treats nobody as signed in, and allows nothing, until the host says who is', () => {
    TestBed.configureTestingModule({});
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.user()).toBeNull();
    expect(auth.can(Permissions.TestCases.Default)).toBe(false);
    expect(auth.roleLabel()).toBe('');
  });

  it('lets the host provide who is signed in, and derives the role label from it', () => {
    @Injectable()
    class HostAuth extends AuthService {
      readonly user = signal<TcmUser | null>({ userId: 'u', userName: 'ann', roles: [] });
      readonly isAuthenticated = signal(true);
      can(permission: string): boolean { return permission === Permissions.TestCases.Default; }
    }
    TestBed.configureTestingModule({ providers: [{ provide: AuthService, useClass: HostAuth }] });
    const auth = TestBed.inject(AuthService);
    expect(auth.can(Permissions.TestCases.Default)).toBe(true);
    expect(auth.can(Permissions.TestCases.Create)).toBe(false);
    expect(auth.roleLabel()).toBe('ann');
  });

  it('has a route for every entry of the menu, all behind the guards of the host', () => {
    const guard = () => true;
    const [shell] = createTestCaseManagementRoutes({ canActivate: [guard] });
    const children = shell.children ?? [];

    for (const item of TCM_MENU) {
      const route = children.find(r => r.path === item.path);
      expect(route, item.path).toBeDefined();
      expect(route?.canActivate, item.path).toEqual([guard]);
    }
    expect(children.find(r => r.path === 'runs/:id')?.canActivate).toEqual([guard]);
    expect(children.find(r => r.path === '')?.redirectTo).toBe('repository');
  });

  it('gives every menu entry a policy, a label key and an ABP name, and unique paths', () => {
    for (const item of TCM_MENU) {
      expect(item.policy, item.path).toMatch(/^TestCaseManagement\./);
      // The standalone top bar hides what the user cannot open, so every page that has a policy has to name its permission too.
      expect(item.permission, item.path).toBe(item.policy);
      expect(item.abpName, item.path).toMatch(/^TestCaseManagement::Menu:/);
      expect(item.label, item.path).toMatch(/^nav\./);
    }
    expect(new Set(TCM_MENU.map(i => i.path)).size).toBe(TCM_MENU.length);
    expect(new Set(TCM_MENU.map(i => i.order)).size).toBe(TCM_MENU.length);
  });

  it('does not ask for the suites of a person who may not read them', () => {
    @Injectable()
    class Reader extends AuthService {
      readonly user = signal<TcmUser | null>({ userId: 'u', userName: 'reader', roles: [] });
      readonly isAuthenticated = signal(true);
      can(permission: string): boolean { return permission === Permissions.TestCases.Default; }
    }
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useClass: Reader }, { provide: TCM_BASE_PATH, useValue: '' }],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RepositoryComponent);
    fixture.detectChanges();

    http.expectNone(r => r.url.endsWith('/suites/tree'));
    http.match(() => true).forEach(r => r.flush([]));
  });

  it('titles each page in the language of the user, and keeps the key for a host that retitles on a language switch', () => {
    TestBed.configureTestingModule({});
    const i18n = TestBed.inject(I18nService);
    i18n.use('vi');
    const [shell] = createTestCaseManagementRoutes();
    const dashboard = shell.children!.find(r => r.path === 'dashboard')!;

    const title = TestBed.runInInjectionContext(() => (dashboard.title as ResolveFn<string>)(null as never, null as never));

    expect(title).toBe(i18n.t('title.dashboard'));
    expect(title).not.toBe('title.dashboard');
    expect(dashboard.data).toEqual({ titleKey: 'title.dashboard' });
  });

  it('retitles the browser tab from the key of the page when the language changes', () => {
    TestBed.configureTestingModule({});
    const i18n = TestBed.inject(I18nService);
    const strategy = TestBed.inject(TranslatedTitleStrategy);
    i18n.use('en');
    const snapshot = { root: { firstChild: { firstChild: null, data: { titleKey: 'title.dashboard' } }, data: {} } } as unknown as RouterStateSnapshot;

    strategy.updateTitle(snapshot);
    TestBed.tick();
    expect(document.title).toBe(`${i18n.t('title.dashboard')} - ${i18n.t('app.name')}`);

    i18n.use('vi');
    TestBed.tick();
    expect(document.title).toBe(`${i18n.t('title.dashboard')} - ${i18n.t('app.name')}`);
    expect(document.title).toContain('Bảng điều khiển');
  });
});
