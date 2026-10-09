import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TestCase } from '../../proxy/dtos';
import { TestCaseStatus } from '../../proxy/enums';
import { ConfirmService } from '../../core/confirm';
import { TestCaseDetailComponent } from './test-case-detail';

const URL_OF = '/api/test-case-management/test-cases/tc1';

const testCase = (status: TestCaseStatus, currentVersion: number): TestCase => ({
  id: 'tc1', suiteId: 's', code: 'TC-1', title: 'T', description: null, preconditions: null, postconditions: null,
  priority: 1, severity: 1, status, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion, tags: [],
  creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null, steps: [],
});

describe('an approved test case that was edited waits for its review', () => {
  let http: HttpTestingController;

  const open = (status: TestCaseStatus, currentVersion: number) => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase(status, currentVersion));
    fixture.detectChanges();
    for (const request of http.match(() => true)) { request.flush([]); }
    fixture.detectChanges();
    return fixture;
  };

  const approve = (fixture: ReturnType<typeof open>) =>
    (fixture.componentInstance as unknown as { changeStatus(t: TestCaseStatus): void }).changeStatus(TestCaseStatus.Approved);

  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it('says so, with the version the runs keep using, only for a test case that was approved before', () => {
    expect(open(TestCaseStatus.UnderReview, 1).nativeElement.querySelector('[data-test=review-banner]')?.textContent).toContain('version 1');
    TestBed.resetTestingModule();
    expect(open(TestCaseStatus.UnderReview, 0).nativeElement.querySelector('[data-test=review-banner]')).toBeNull();
    TestBed.resetTestingModule();
    expect(open(TestCaseStatus.Approved, 1).nativeElement.querySelector('[data-test=review-banner]')).toBeNull();
  });

  it('asks for an optional note when it is approved again, and sends it', () => {
    vi.stubGlobal('prompt', () => '  Added the 3-D Secure step ');
    approve(open(TestCaseStatus.UnderReview, 1));
    const request = http.expectOne(`${URL_OF}/status`);
    expect(request.request.body).toEqual({ targetStatus: TestCaseStatus.Approved, changeSummary: 'Added the 3-D Secure step' });
    request.flush(testCase(TestCaseStatus.Approved, 2));
  });

  it('approves without a note when the person leaves it empty, and not at all when they cancel', () => {
    vi.stubGlobal('prompt', () => '');
    approve(open(TestCaseStatus.UnderReview, 1));
    const request = http.expectOne(`${URL_OF}/status`);
    expect(request.request.body).toEqual({ targetStatus: TestCaseStatus.Approved, changeSummary: null });
    request.flush(testCase(TestCaseStatus.Approved, 2));

    TestBed.resetTestingModule();
    vi.stubGlobal('prompt', () => null);
    approve(open(TestCaseStatus.UnderReview, 1));
    http.expectNone(`${URL_OF}/status`);
  });

  it('does not ask anything the first time a test case is approved', () => {
    const asked = vi.fn();
    vi.stubGlobal('prompt', asked);
    approve(open(TestCaseStatus.Draft, 0));
    expect(asked).not.toHaveBeenCalled();
    http.expectOne(`${URL_OF}/status`).flush(testCase(TestCaseStatus.Approved, 1));
  });

  it('lets a prompt stay empty only when the question says it is optional', () => {
    const service = TestBed.inject(ConfirmService);
    vi.stubGlobal('prompt', () => '');
    const answers: (string | null)[] = [];
    service.askText({ message: 'Note?', optional: true }).subscribe(a => answers.push(a));
    service.askText({ message: 'Key?' }).subscribe(a => answers.push(a));
    expect(answers).toEqual(['', null]);
  });
});
