import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SharedStepGroup, SharedStepGroupSummary, SharedStepUsage, TestCase } from '../../proxy/dtos';
import { TestCaseDetailComponent } from '../repository/test-case-detail';
import { SharedStepsComponent, toForm } from './shared-steps';

const ROOT = '/api/test-case-management';

const summary = (patch: Partial<SharedStepGroupSummary> = {}): SharedStepGroupSummary =>
  ({ id: 'g1', name: 'Log in', description: null, revision: 2, stepCount: 2, usedByCount: 3, ...patch });

const usage = (patch: Partial<SharedStepUsage>): SharedStepUsage =>
  ({ testCaseId: 'tc', code: 'TC-1', title: 'T', status: 0, linkedRevision: 1, linkedStepCount: 2, isOutdated: true, ...patch });

describe('toForm', () => {
  it('turns a group into the form, with blank for missing text', () => {
    const group: SharedStepGroup = {
      id: 'g1', name: 'Log in', description: null, revision: 3, creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null,
      steps: [{ id: 's1', stepOrder: 1, action: 'Open', expectedResult: 'Shown', testData: null }],
    };
    expect(toForm(group)).toEqual({ id: 'g1', name: 'Log in', description: '', steps: [{ id: 's1', action: 'Open', expectedResult: 'Shown', testData: '' }] });
  });
});

describe('SharedStepsComponent', () => {
  let http: HttpTestingController;
  let component: SharedStepsComponent;
  const call = <T>(name: string, ...args: unknown[]): T => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);
  const read = <T>(name: string): T => (component as unknown as Record<string, () => T>)[name]();

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(SharedStepsComponent).componentInstance;
  });

  afterEach(() => vi.unstubAllGlobals());

  it('lists the groups, searching by the text typed', () => {
    component.ngOnInit();
    http.expectOne(r => r.url === `${ROOT}/shared-step-groups` && !r.params.has('Filter')).flush([summary()]);
    expect(read<SharedStepGroupSummary[]>('groups').length).toBe(1);

    (component as unknown as { search: string }).search = 'log';
    call('reload');
    expect(http.expectOne(r => r.url === `${ROOT}/shared-step-groups`).request.params.get('Filter')).toBe('log');
  });

  it('creates a group from the form, with the steps in order, and reloads', () => {
    call('newGroup');
    const form = read<{ name: string; description: string; steps: { action: string; expectedResult: string; testData: string }[] }>('form');
    form.name = 'Pay';
    form.description = '';
    form.steps[0] = { action: 'Pay by card', expectedResult: 'Paid', testData: '' };
    call('addStep', form);
    form.steps[1] = { action: 'Show the receipt', expectedResult: 'Shown', testData: '4111' };
    call('move', form, 1, -1);

    call('save');
    const request = http.expectOne(`${ROOT}/shared-step-groups`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body.description).toBeNull();
    expect(request.request.body.steps.map((s: { action: string; testData: string | null }) => [s.action, s.testData])).toEqual([['Show the receipt', '4111'], ['Pay by card', null]]);
    request.flush({ id: 'g2', revision: 1 });
    http.expectOne(r => r.method === 'GET' && r.url === `${ROOT}/shared-step-groups`).flush([]);
    expect(read<unknown>('form')).toBeNull();
  });

  it('will not save a group without a step', () => {
    call('newGroup');
    const form = read<{ steps: unknown[] }>('form');
    call('removeStep', form, 0);

    call('save');

    http.expectNone(`${ROOT}/shared-step-groups`);
    expect(read<unknown>('form')).not.toBeNull();
  });

  it('opens the usage with the test cases that are behind selected, and updates only the selected ones', () => {
    call('openUsage', summary());
    http.expectOne(`${ROOT}/shared-step-groups/g1/usage`).flush([
      usage({ testCaseId: 'a', code: 'TC-A' }), usage({ testCaseId: 'b', code: 'TC-B' }), usage({ testCaseId: 'c', code: 'TC-C', isOutdated: false, linkedRevision: 2 }),
    ]);
    const dialog = read<{ selected: Set<string>; items: SharedStepUsage[] }>('usage');
    expect([...dialog.selected].sort()).toEqual(['a', 'b']);
    expect(call<number>('behind', dialog)).toBe(2);

    call('toggle', dialog, 'b');
    call('updateSelected');
    const request = http.expectOne(`${ROOT}/shared-step-groups/g1/update-test-cases`);
    expect(request.request.body).toEqual({ testCaseIds: ['a'] });
    request.flush({ updated: 1, codes: ['TC-A'], newVersions: 0 });
    http.expectOne(r => r.method === 'GET' && r.url === `${ROOT}/shared-step-groups`).flush([]);
    expect(read<unknown>('usage')).toBeNull();
  });

  it('deletes a group after a confirmation only', () => {
    vi.stubGlobal('confirm', () => false);
    call('remove', summary());
    http.expectNone(`${ROOT}/shared-step-groups/g1`);

    vi.stubGlobal('confirm', () => true);
    call('remove', summary());
    const request = http.expectOne(`${ROOT}/shared-step-groups/g1`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    http.expectOne(r => r.method === 'GET').flush([]);
  });
});

describe('the shared steps in the test case dialog', () => {
  let http: HttpTestingController;
  let component: TestCaseDetailComponent;
  const call = <T>(name: string, ...args: unknown[]): T => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);
  const read = <T>(name: string): T => (component as unknown as Record<string, () => T>)[name]();

  const testCase = (status = 0): TestCase => ({
    id: 'tc1', suiteId: 's', code: 'TC-1', title: 'T', description: null, preconditions: null, postconditions: null,
    priority: 1, severity: 1, status, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 1, tags: [],
    creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null,
    steps: [
      { id: 'a', stepOrder: 1, action: 'Open', expectedResult: 'Ok', sharedStepGroupId: 'g1', sharedStepRevision: 1, sharedStepGroupName: 'Log in', sharedStepOutdated: true },
      { id: 'b', stepOrder: 2, action: 'Sign in', expectedResult: 'Ok', sharedStepGroupId: 'g1', sharedStepRevision: 2, sharedStepGroupName: 'Log in', sharedStepOutdated: false },
      { id: 'c', stepOrder: 3, action: 'Search', expectedResult: 'Found' },
    ],
  });

  const flushLoads = () => {
    for (const request of http.match(() => true)) {
      request.flush(request.request.url.endsWith('/shared-step-groups') ? [] : []);
    }
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase());
    fixture.detectChanges();
    component = fixture.componentInstance;
    flushLoads();
  });

  afterEach(() => vi.unstubAllGlobals());

  it('sums up the groups behind the steps, with the oldest revision and whether any copy is behind', () => {
    expect(read<{ id: string; name: string; count: number; revision: number; outdated: boolean }[]>('groupsUsed'))
      .toEqual([{ id: 'g1', name: 'Log in', count: 2, revision: 1, outdated: true }]);
  });

  it('refreshes a group and hands the new test case up, after asking when the test case is approved', () => {
    const emitted: TestCase[] = [];
    component.stepsChanged.subscribe(t => emitted.push(t));

    call('refreshGroup', 'g1');
    const request = http.expectOne('/api/test-case-management/test-cases/tc1/shared-steps/g1/refresh');
    expect(request.request.method).toBe('POST');
    request.flush(testCase());
    expect(emitted.length).toBe(1);

    // An approved test case asks first, and a refusal sends nothing.
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase(2));
    fixture.detectChanges();
    component = fixture.componentInstance;
    flushLoads();
    vi.stubGlobal('confirm', () => false);
    call('refreshGroup', 'g1');
    http.expectNone('/api/test-case-management/test-cases/tc1/shared-steps/g1/refresh');
  });

  it('inserts the chosen group and detaches only after a confirmation', () => {
    (component as unknown as { pick: string }).pick = 'g9';
    call('insertGroup');
    const insert = http.expectOne('/api/test-case-management/test-cases/tc1/shared-steps');
    expect(insert.request.body).toEqual({ sharedStepGroupId: 'g9', position: null, changeSummary: null });
    insert.flush(testCase());

    vi.stubGlobal('confirm', () => false);
    call('detachGroup', 'g1');
    http.expectNone('/api/test-case-management/test-cases/tc1/shared-steps/g1');

    vi.stubGlobal('confirm', () => true);
    call('detachGroup', 'g1');
    const detach = http.expectOne('/api/test-case-management/test-cases/tc1/shared-steps/g1');
    expect(detach.request.method).toBe('DELETE');
    detach.flush(testCase());
  });
});
