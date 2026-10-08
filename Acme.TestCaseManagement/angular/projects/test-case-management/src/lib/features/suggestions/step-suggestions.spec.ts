import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AuthService, Permissions, TcmUser } from '../../core/auth';
import { I18nService } from '../../core/i18n/i18n';
import { StepSuggestionStatus, SuggestedStep } from '../../proxy/dtos';
import { TestCaseFormComponent } from '../repository/test-case-form';
import { StepSuggestionsComponent } from './step-suggestions';

const ROOT = '/api/test-case-management/step-suggestions';
const status = (patch: Partial<StepSuggestionStatus> = {}): StepSuggestionStatus => ({ enabled: true, maxRequirementLength: 4000, maxSteps: 20, defaultSteps: 8, ...patch });

let allowed = true;

@Injectable()
class FakeAuth extends AuthService {
  readonly user = signal<TcmUser | null>({ userId: 'u', userName: 'ann', roles: [] });
  readonly isAuthenticated = signal(true);
  can(permission: string): boolean { return allowed && permission === Permissions.TestCases.SuggestSteps; }
}

describe('StepSuggestionsComponent', () => {
  let http: HttpTestingController;
  let component: StepSuggestionsComponent;
  const call = <T>(name: string, ...args: unknown[]): T => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);
  const read = <T>(name: string): T => (component as unknown as Record<string, () => T>)[name]();
  const set = (name: string, value: unknown) => { (component as unknown as Record<string, unknown>)[name] = value; };

  const create = (inputs: { title?: string; description?: string | null } = {}) => {
    const fixture = TestBed.createComponent(StepSuggestionsComponent);
    fixture.componentRef.setInput('title', inputs.title ?? 'Pay by card');
    fixture.componentRef.setInput('description', inputs.description ?? 'As a customer I can pay by card.');
    component = fixture.componentInstance;
    component.ngOnInit();
  };

  beforeEach(() => {
    allowed = true;
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useClass: FakeAuth }] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(I18nService).use('en');
  });

  it('asks whether a model exists only for a user who may ask', () => {
    allowed = false;
    create();
    http.expectNone(`${ROOT}/status`);
    expect(read<StepSuggestionStatus | null>('status')).toBeNull();
  });

  it('shows no button when no model is configured', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status({ enabled: false }));

    expect(read<StepSuggestionStatus>('status').enabled).toBe(false);
  });

  it('starts the requirement from the description and title, and the number of steps from the default', () => {
    create({ title: 'Pay by card', description: 'As a customer I can pay by card.' });
    http.expectOne(`${ROOT}/status`).flush(status({ defaultSteps: 5 }));

    call('show');

    expect(read<boolean>('open')).toBe(true);
    expect((component as unknown as { requirement: string }).requirement).toBe('Pay by card\nAs a customer I can pay by card.');
    expect((component as unknown as { count: number }).count).toBe(5);
    expect(read<number[]>('counts')).toEqual([3, 5, 8, 12, 20]);
  });

  it('does not overwrite what the user already wrote when the dialog is opened again', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    set('requirement', 'My own text about the requirement');

    call('show');

    expect((component as unknown as { requirement: string }).requirement).toBe('My own text about the requirement');
  });

  it('sends the requirement, title, number of steps and language, and offers every proposal picked', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    TestBed.inject(I18nService).use('vi');
    call('show');
    set('requirement', '  Pay by card, then show a receipt  ');
    set('count', 3);

    call('generate');
    const request = http.expectOne(ROOT);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ requirementText: 'Pay by card, then show a receipt', title: 'Pay by card', maxSteps: 3, language: 'vi' });
    expect(read<boolean>('busy')).toBe(true);
    request.flush({ steps: [{ action: 'Open', expectedResult: 'Shown', testData: null }, { action: 'Pay', expectedResult: 'Paid', testData: '4111' }] });

    expect(read<boolean>('busy')).toBe(false);
    expect(read<number>('pickedCount')).toBe(2);
  });

  it('adds only the steps that are still picked, and closes', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    call('show');
    call('generate');
    http.expectOne(ROOT).flush({ steps: [{ action: 'A', expectedResult: 'a', testData: null }, { action: 'B', expectedResult: 'b', testData: null }, { action: 'C', expectedResult: 'c', testData: null }] });
    read<{ picked: boolean }[]>('proposals')[1].picked = false;
    expect(read<number>('pickedCount')).toBe(2);

    const chosen: SuggestedStep[][] = [];
    component.chosen.subscribe(steps => chosen.push(steps));
    call('add');

    expect(chosen.length).toBe(1);
    expect(chosen[0].map(s => s.action)).toEqual(['A', 'C']);
    expect(read<boolean>('open')).toBe(false);
  });

  it('adds nothing when nothing is picked', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    call('show');
    call('generate');
    http.expectOne(ROOT).flush({ steps: [{ action: 'A', expectedResult: 'a', testData: null }] });
    read<{ picked: boolean }[]>('proposals')[0].picked = false;
    const chosen: SuggestedStep[][] = [];
    component.chosen.subscribe(steps => chosen.push(steps));

    call('add');

    expect(chosen).toEqual([]);
  });

  it('keeps the title when a long description is cut at the limit', () => {
    create({ title: 'Pay by card', description: 'x'.repeat(5000) });
    http.expectOne(`${ROOT}/status`).flush(status({ maxRequirementLength: 100 }));

    call('show');

    const text = (component as unknown as { requirement: string }).requirement;
    expect(text.length).toBe(100);
    expect(text.startsWith('Pay by card\n')).toBe(true);
  });

  it('drops an answer that comes after the dialog was closed and opened again', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    call('show');
    call('generate');
    const late = http.expectOne(ROOT);

    call('close');
    call('show');
    late.flush({ steps: [{ action: 'Late', expectedResult: 'Late', testData: null }] });

    expect(read<unknown>('proposals')).toBeNull();
    expect(read<boolean>('busy')).toBe(false);
  });

  it('removes the earlier proposals when a new request starts and when it fails', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    call('show');
    call('generate');
    http.expectOne(ROOT).flush({ steps: [{ action: 'A', expectedResult: 'a', testData: null }] });
    expect(read<number>('pickedCount')).toBe(1);

    call('generate');
    expect(read<unknown>('proposals')).toBeNull();
    http.expectOne(ROOT).flush({ error: { code: 'x', message: 'down' } }, { status: 403, statusText: 'Forbidden' });

    expect(read<unknown>('proposals')).toBeNull();
    expect(read<boolean>('busy')).toBe(false);
  });

  it('keeps the dialog open and stops waiting when the request fails', () => {
    create();
    http.expectOne(`${ROOT}/status`).flush(status());
    call('show');
    call('generate');
    http.expectOne(ROOT).flush({ error: { code: 'x', message: 'down' } }, { status: 403, statusText: 'Forbidden' });

    expect(read<boolean>('busy')).toBe(false);
    expect(read<boolean>('open')).toBe(true);
    expect(read<unknown>('proposals')).toBeNull();
  });
});

describe('the suggested steps in the test case form', () => {
  let form: TestCaseFormComponent;
  const steps = () => (form as unknown as { model: { steps: { action: string; expectedResult: string; testData?: string | null; sharedStepGroupId?: string | null }[] } }).model.steps;
  const addSuggested = (list: SuggestedStep[]) => (form as unknown as { addSuggested: (s: SuggestedStep[]) => void }).addSuggested(list);

  beforeEach(() => {
    allowed = false;
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useClass: FakeAuth }] });
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'Suite' }]);
    form = fixture.componentInstance;
    form.ngOnInit();
    TestBed.inject(HttpTestingController).match(() => true).forEach(r => r.flush([]));
  });

  it('puts the steps in place of the blank step a new form starts with', () => {
    expect(steps().length).toBe(1);

    addSuggested([{ action: 'Open', expectedResult: 'Shown', testData: null }, { action: 'Pay', expectedResult: 'Paid', testData: '4111' }]);

    expect(steps().map(s => [s.action, s.testData])).toEqual([['Open', ''], ['Pay', '4111']]);
  });

  it('adds after the steps already written', () => {
    steps()[0] = { action: 'Existing', expectedResult: 'Fine', testData: '' };

    addSuggested([{ action: 'New', expectedResult: 'Ok', testData: null }]);

    expect(steps().map(s => s.action)).toEqual(['Existing', 'New']);
  });
});
