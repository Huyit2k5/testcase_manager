import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { RequirementLink, RtmMatrix, RtmRow, TestCase } from '../../proxy/dtos';
import { TestCaseDetailComponent } from '../repository/test-case-detail';
import { TraceabilityComponent } from './traceability';

// Written with promise chains rather than async functions: see the note in run-step-results.spec.ts.

const ROOT = '/api/test-case-management';

const testCase = (code: string, extra: object = {}) => ({
  testCaseId: code, code, title: `Title ${code}`, status: 2, countsTowardCoverage: true, result: 0, lastExecutedTime: null,
  isSuspect: false, suspectSince: null, suspectReason: null, ...extra,
});

const row = (code: string, testCases: object[], suspect = 0) => ({
  requirementId: `id-${code}`, code, title: `Requirement ${code}`, priority: 1, milestoneId: null, status: 1,
  testCases, blockingDefects: [], suspectTestCaseCount: suspect,
}) as unknown as RtmRow;

const matrix = (rows: RtmRow[]): RtmMatrix => ({
  summary: {
    totalRequirements: rows.length, coveredRequirements: rows.length, uncoveredRequirements: 0, coveragePercentage: 100,
    passedRequirements: 0, passedPercentage: 0, failedRequirements: 0, blockedRequirements: 0, notRunRequirements: rows.length,
    suspectRequirements: rows.filter(r => r.suspectTestCaseCount > 0).length,
  },
  requirements: rows, totalCount: rows.length,
});

describe('the requirements whose tests are to be checked, on the traceability page', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<TraceabilityComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const page = () => fixture.componentInstance as unknown as Record<string, (...a: unknown[]) => unknown> & { onlySuspect: boolean };

  const open = (answer: RtmMatrix, permissions: string[] = ['*']) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TraceabilityComponent);
    fixture.detectChanges();
    http.match(r => r.url === `${ROOT}/plans`).forEach(r => r.flush({ items: [], totalCount: 0 }));
    http.expectOne(r => r.url === `${ROOT}/rtm`).flush(answer);
    fixture.detectChanges();
  };

  const changed = () => matrix([
    row('REQ-1', [testCase('TC-1', { isSuspect: true, suspectSince: '2026-10-10T09:00:00Z', suspectReason: 'Title, AcceptanceCriteria' }), testCase('TC-2')], 1),
    row('REQ-2', [testCase('TC-3')]),
  ]);

  beforeEach(() => localStorage.clear());

  it('counts the requirements to check, and marks the one that has tests to check and the test case itself', () => {
    open(changed());

    expect(element().querySelector('[data-test=suspect-stat] b')!.textContent!.trim()).toBe('1');
    const badges = element().querySelectorAll('[data-test=suspect-badge]');
    expect(badges.length).toBe(1);
    expect(badges[0].textContent).toContain('1 test case to check');
    const mark = element().querySelectorAll('[data-test=suspect-link]');
    expect(mark.length).toBe(1);
    expect(mark[0].getAttribute('title')).toContain('Title, Acceptance criteria');
  });

  it('asks only for the requirements with tests to check when the box is ticked', () => {
    open(changed());

    page().onlySuspect = true;
    page()['load']();

    expect(http.expectOne(r => r.url === `${ROOT}/rtm`).request.params.get('onlySuspect')).toBe('true');
  });

  it('does not send the filter when the box is not ticked', () => {
    open(changed());

    page()['load']();

    expect(http.expectOne(r => r.url === `${ROOT}/rtm`).request.params.has('onlySuspect')).toBe(false);
  });

  it('confirms one test case, and then reads the matrix again', () => {
    open(changed());

    element().querySelector<HTMLButtonElement>('[data-test=review-link]')!.click();

    const request = http.expectOne(`${ROOT}/requirements/id-REQ-1/test-cases/TC-1/review`);
    expect(request.request.method).toBe('POST');
    request.flush({ reviewed: 1 });
    http.expectOne(r => r.url === `${ROOT}/rtm`).flush(matrix([row('REQ-1', [testCase('TC-1'), testCase('TC-2')]), row('REQ-2', [testCase('TC-3')])]));
    fixture.detectChanges();
    expect(element().querySelector('[data-test=suspect-badge]')).toBeNull();
  });

  it('confirms all the test cases of a requirement at once', () => {
    open(changed());

    element().querySelector<HTMLButtonElement>('[data-test=review-all]')!.click();

    const request = http.expectOne(`${ROOT}/requirements/id-REQ-1/review`);
    expect(request.request.method).toBe('POST');
    request.flush({ reviewed: 1 });
    http.expectOne(r => r.url === `${ROOT}/rtm`).flush(matrix([]));
  });

  it('shows the mark but not the buttons to someone who may not update test cases', () => {
    open(changed(), ['TestCaseManagement.Requirements', 'TestCaseManagement.TestPlans']);

    expect(element().querySelector('[data-test=suspect-link]')).not.toBeNull();
    expect(element().querySelector('[data-test=review-link]')).toBeNull();
    expect(element().querySelector('[data-test=review-all]')).toBeNull();
  });

  it('does not offer to confirm a test case that is deprecated, because nobody is asking for it', () => {
    open(matrix([row('REQ-3', [testCase('TC-9', { isSuspect: true, countsTowardCoverage: false, suspectReason: 'Title' })], 0)]));

    expect(element().querySelector('[data-test=suspect-link]')).not.toBeNull();
    expect(element().querySelector('[data-test=review-link]')).toBeNull();
  });

  it('warns, when a requirement with tests is edited, that the tests will be marked to check', () => {
    open(changed());

    page()['editRequirement'](changed().requirements[0]);
    http.expectOne(r => r.url === `${ROOT}/requirements`).flush({
      items: [{ id: 'id-REQ-1', code: 'REQ-1', title: 'T', description: null, acceptanceCriteria: null, priority: 1, milestoneId: null }], totalCount: 1,
    });
    fixture.detectChanges();

    expect(element().querySelector('[data-test=change-warning]')!.textContent).toContain('2 linked test cases');
  });

  it('does not warn for a new requirement, nor for one with no test linked', () => {
    open(matrix([row('REQ-4', [])]));

    page()['newRequirement']();
    fixture.detectChanges();

    expect(element().querySelector('[data-test=change-warning]')).toBeNull();
  });
});

describe('the requirements of a test case, in its detail', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<TestCaseDetailComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;

  const tc = { id: 'tc1', suiteId: 's', code: 'TC-1', title: 'T', description: null, preconditions: null, postconditions: null, priority: 1, severity: 1, status: 2, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 1, tags: [], creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null, steps: [] } as TestCase;

  const links = (suspect: boolean): RequirementLink[] => [
    { requirementId: 'r1', code: 'REQ-1', title: 'Pay', isSuspect: suspect, suspectSince: suspect ? '2026-10-10T09:00:00Z' : null, suspectReason: suspect ? 'Description' : null },
    { requirementId: 'r2', code: 'REQ-2', title: 'Refund', isSuspect: false, suspectSince: null, suspectReason: null },
  ];

  const open = (answer: RequirementLink[], permissions: string[] = ['*']) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', tc);
    fixture.detectChanges();
    for (const request of http.match(r => r.method === 'GET')) {
      request.flush(request.request.url.endsWith('/requirements/for-test-case/tc1') ? answer : []);
    }
    fixture.detectChanges();
  };

  beforeEach(() => localStorage.clear());

  it('says at the top which requirement changed and in what, and lists the requirements with the one to check marked', () => {
    open(links(true));

    expect(element().querySelector('[data-test=requirement-banner]')!.textContent).toContain('REQ-1');
    expect(element().querySelector('[data-test=requirement-banner]')!.textContent).toContain('Description');
    const rows = [...element().querySelectorAll('[data-test=requirement-links] tbody tr')];
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('To check since');
    expect(rows[1].textContent).toContain('Up to date');
  });

  it('shows nothing to check when no requirement changed, and says so when none is linked', () => {
    open(links(false));
    expect(element().querySelector('[data-test=requirement-banner]')).toBeNull();
    expect(element().querySelector('[data-test=review-requirement]')).toBeNull();

    open([]);
    expect(element().querySelector('[data-test=requirement-links]')).toBeNull();
    expect(element().textContent).toContain('Not linked to any requirement.');
  });

  it('confirms the test case against the requirement, reads the links and the history again, and the banner goes', () => {
    open(links(true));

    element().querySelector<HTMLButtonElement>('[data-test=review-requirement]')!.click();

    const request = http.expectOne('/api/test-case-management/requirements/r1/test-cases/tc1/review');
    expect(request.request.method).toBe('POST');
    request.flush({ reviewed: 1 });
    http.expectOne('/api/test-case-management/requirements/for-test-case/tc1').flush(links(false));
    http.match(r => r.url.endsWith('/history')).forEach(r => r.flush([]));
    fixture.detectChanges();
    expect(element().querySelector('[data-test=requirement-banner]')).toBeNull();
  });

  it('does not ask for the requirements, nor show them, to someone who may not read requirements', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('TestCaseManagement.TestCases')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', tc);
    fixture.detectChanges();

    http.expectNone('/api/test-case-management/requirements/for-test-case/tc1');
    http.match(() => true).forEach(r => r.flush([]));
    fixture.detectChanges();
    expect(element().querySelector('[data-test=requirement-links]')).toBeNull();
  });

  it('shows the confirm button only to someone who may update test cases', () => {
    open(links(true), ['TestCaseManagement.TestCases', 'TestCaseManagement.Requirements']);

    expect(element().querySelector('[data-test=requirement-banner]')).not.toBeNull();
    expect(element().querySelector('[data-test=review-requirement]')).toBeNull();
  });
});
