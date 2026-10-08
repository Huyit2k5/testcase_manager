import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ApiKey } from '../../proxy/dtos';
import { ApiKeyService } from '../../proxy/services';
import { AutomationComponent, SAMPLE_BODY, curlExample, githubActionsExample } from './automation';

const key = (patch: Partial<ApiKey>): ApiKey => ({
  id: 'k1', name: 'CI', keyPrefix: 'tcm_ab12cd34', expiresAt: null, revokedAt: null, lastUsedAt: null,
  creationTime: '2026-01-01T00:00:00Z', creatorId: null, isActive: true, ...patch,
});

describe('examples', () => {
  it('put the key in the X-Api-Key header and use the real route', () => {
    const text = curlExample('https://tcm.example', 'tcm_ab12cd34_secret');
    expect(text).toContain('https://tcm.example/api/test-case-management/automation/results');
    expect(text).toContain('X-Api-Key: tcm_ab12cd34_secret');
    expect(text).toContain('Idempotency-Key');
  });

  it('send a body the API accepts: a new run, or an existing one, and results with an automation id', () => {
    expect(SAMPLE_BODY.run.title).toBeTruthy();
    expect(SAMPLE_BODY.results.every(r => r.automationId && r.status)).toBe(true);
  });

  it('keep the secret of the pipeline out of the workflow file', () => {
    const text = githubActionsExample('https://tcm.example');
    expect(text).toContain('secrets.TCM_API_KEY');
    expect(text).not.toContain('tcm_ab12cd34');
  });
});

describe('AutomationComponent', () => {
  let http: HttpTestingController;
  let component: AutomationComponent;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), ApiKeyService] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(AutomationComponent).componentInstance;
  });

  const keys = () => (component as unknown as { keys: () => ApiKey[] }).keys();
  const call = <T>(name: string, ...args: unknown[]): T => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);
  const signalOf = <T>(name: string) => (component as unknown as Record<string, () => T>)[name]();

  it('says whether a key is active, revoked or expired', () => {
    expect(call('state', key({}))).toBe('active');
    expect(call('state', key({ isActive: false, revokedAt: '2026-02-01T00:00:00Z' }))).toBe('revoked');
    expect(call('state', key({ isActive: false, expiresAt: '2026-02-01T00:00:00Z' }))).toBe('expired');
  });

  it('lists the keys when it starts', () => {
    component.ngOnInit();
    http.expectOne('/api/test-case-management/api-keys').flush([key({}), key({ id: 'k2', isActive: false })]);

    expect(keys().length).toBe(2);
    expect(signalOf<boolean>('loading')).toBe(false);
  });

  it('creates a key with the end of the chosen day in local time, and keeps the secret for the dialog', () => {
    call('newKey');
    const form = signalOf<{ name: string; expiresOn: string }>('form');
    form.name = '  GitHub  ';
    form.expiresOn = '2030-05-01';

    call('save');
    const request = http.expectOne('/api/test-case-management/api-keys');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'GitHub', expiresAt: new Date('2030-05-01T23:59:59').toISOString() });
    request.flush({ ...key({ name: 'GitHub' }), key: 'tcm_ab12cd34_secret' });
    http.expectOne('/api/test-case-management/api-keys').flush([key({})]);

    expect(signalOf<{ key: string } | null>('created')?.key).toBe('tcm_ab12cd34_secret');
    expect(signalOf<unknown>('form')).toBeNull();

    call('closeCreated');
    expect(signalOf<unknown>('created')).toBeNull();
  });

  it('sends no expiry when the date is left empty, and does not send an empty name', () => {
    call('newKey');
    call('save');
    http.expectNone('/api/test-case-management/api-keys');

    signalOf<{ name: string }>('form').name = 'No expiry';
    call('save');
    expect(http.expectOne('/api/test-case-management/api-keys').request.body).toEqual({ name: 'No expiry', expiresAt: null });
  });

  it('ends the key at 23:59:59 of the chosen day in the time zone of the user, not in UTC', () => {
    // The runner is Node, where the zone can be switched at run time; there is no node typing in this project.
    const env = (globalThis as unknown as { process?: { env: Record<string, string | undefined> } }).process?.env;
    if (!env) { return; }
    const before = env['TZ'];
    env['TZ'] = 'Asia/Ho_Chi_Minh';   // UTC+7: the end of the day there is 16:59:59Z
    try {
      call('newKey');
      const form = signalOf<{ name: string; expiresOn: string }>('form');
      form.name = 'Local';
      form.expiresOn = '2030-05-01';
      call('save');
      expect(http.expectOne('/api/test-case-management/api-keys').request.body.expiresAt).toBe('2030-05-01T16:59:59.000Z');
    } finally {
      if (before === undefined) { delete env['TZ']; } else { env['TZ'] = before; }
    }
  });

  it('sends one request when the form is submitted twice before the answer', () => {
    call('newKey');
    signalOf<{ name: string }>('form').name = 'Twice';
    call('save');
    call('save');
    const requests = http.match('/api/test-case-management/api-keys');
    expect(requests.length).toBe(1);

    // After an error the form can be sent again.
    requests[0].flush({ error: { message: 'no' } }, { status: 500, statusText: 'Server Error' });
    call('save');
    expect(http.match('/api/test-case-management/api-keys').length).toBe(1);
  });
});
