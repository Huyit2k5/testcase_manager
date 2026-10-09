import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { TestCase, TestRun } from '../../proxy/dtos';
import { CasePickerComponent } from './case-picker';
import { RunDetailComponent } from './run-detail';
import { RunsComponent } from './runs';

const ROOT = '/api/test-case-management';
const run = (id: string) => ({ id, title: `Run ${id}`, environment: 'Staging', status: 0, items: [], summary: { totalItems: 0, passed: 0, failed: 0, blocked: 0, skipped: 0, untested: 0 } }) as unknown as TestRun;
const fail = { status: 500, statusText: 'Server Error' };

describe('RunsComponent', () => {
  let http: HttpTestingController;
  let component: RunsComponent;
  const call = (name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);
  const sig = <T>(name: string) => (component as unknown as Record<string, WritableSignal<T>>)[name];

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('tcm.runsView', 'list');   // these tests are about the list view; the default shows the runs under their plans
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(RunsComponent).componentInstance;
  });

  it('saves a plan once when the form is submitted twice, and again after an error', () => {
    sig('planForm').set({ id: null, name: 'P', description: '', milestoneId: '', startDate: '', endDate: '' });
    call('savePlan');
    call('savePlan');
    const requests = http.match(r => r.url === `${ROOT}/plans` && r.method === 'POST');
    expect(requests.length).toBe(1);
    requests[0].flush('x', fail);
    call('savePlan');
    expect(http.match(r => r.url === `${ROOT}/plans` && r.method === 'POST').length).toBe(1);
  });

  it('creates a run once when the form is submitted twice', () => {
    sig('runForm').set({ testPlanId: '', title: 'R', environment: 'Staging', testCaseIds: [] });
    call('createRun');
    call('createRun');
    expect(http.match(r => r.url === `${ROOT}/runs` && r.method === 'POST').length).toBe(1);
  });
});

describe('RunDetailComponent', () => {
  let http: HttpTestingController;
  let route: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let fixture: ReturnType<typeof TestBed.createComponent<RunDetailComponent>>;
  const call = (name: string, ...args: unknown[]) => (fixture.componentInstance as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);
  const sig = <T>(name: string) => (fixture.componentInstance as unknown as Record<string, WritableSignal<T>>)[name];
  const open = (id: string) => { route.next(convertToParamMap({ id })); fixture.detectChanges(); };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    route = new BehaviorSubject(convertToParamMap({ id: 'A' }));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*'), { provide: ActivatedRoute, useValue: { paramMap: route } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunDetailComponent);
  });

  it('keeps the run of the address when an older run answers late (/runs/A to /runs/B)', () => {
    fixture.detectChanges();
    const a = http.expectOne(`${ROOT}/runs/A`);
    open('B');
    const b = http.expectOne(`${ROOT}/runs/B`);

    b.flush(run('B'));
    a.flush(run('A'));

    expect(sig<TestRun>('run')().id).toBe('B');
  });

  it('shows an error with a link back to the runs, not Loading forever, when the run cannot be loaded', () => {
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush({}, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const failed = (fixture.nativeElement as HTMLElement).querySelector('[data-test=run-load-failed]');
    expect(failed).not.toBeNull();
    expect(failed?.querySelector('a')?.getAttribute('href')).toContain('/runs');
    expect(sig<boolean>('loadFailed')()).toBe(true);
  });

  it('clears the error when the next run loads', () => {
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush({}, { status: 403, statusText: 'Forbidden' });
    open('B');
    http.expectOne(`${ROOT}/runs/B`).flush(run('B'));
    expect(sig<boolean>('loadFailed')()).toBe(false);
  });

  it('records a result once when the form is submitted twice, and again after an error', () => {
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush(run('A'));
    sig('executeForm').set({ item: { id: 'i1', testCaseCode: 'TC-1' }, status: 2, actualResult: '', durationSeconds: 0, defects: [] });
    call('execute');
    call('execute');
    const requests = http.match(`${ROOT}/runs/A/items/i1/executions`);
    expect(requests.length).toBe(1);
    requests[0].flush('x', fail);
    call('execute');
    expect(http.match(`${ROOT}/runs/A/items/i1/executions`).length).toBe(1);
  });
});

describe('CasePickerComponent', () => {
  let http: HttpTestingController;
  const setup = (...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(CasePickerComponent);
  };
  const list = () => http.match(r => r.url === `${ROOT}/test-cases`);
  const page = (id: string) => ({ items: [{ id, code: id, title: id, priority: 1 } as unknown as TestCase], totalCount: 1 });

  it('shows the answer to the latest search even when an older answer arrives after it', () => {
    const fixture = setup(Permissions.TestCases.Default);
    const component = fixture.componentInstance as unknown as { load(): void; cases: () => TestCase[] };
    component.load();
    const [old] = list();
    component.load();
    const [latest] = list();

    latest.flush(page('new'));
    old.flush(page('old'));

    expect(component.cases().map(c => c.id)).toEqual(['new']);
  });

  it('does not ask for test cases without TestCases, and says so instead of showing an empty panel', () => {
    const fixture = setup(Permissions.TestPlans.Default);
    fixture.detectChanges();

    expect(list().length).toBe(0);
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-test=picker-denied]')).not.toBeNull();
    expect(root.querySelector('table')).toBeNull();
  });
});
