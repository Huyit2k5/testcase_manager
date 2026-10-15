import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { describeError } from '../../core/core';
import { grant } from '../../core/auth-testing';
import { I18nService } from '../../core/i18n/i18n';
import { TestCase } from '../../proxy/dtos';
import { TestCaseDetailComponent } from './test-case-detail';
import { TestCaseFormComponent } from './test-case-form';

const ROOT = '/api/test-case-management';

const testCase = (stamp: string): TestCase => ({
  id: 'tc1', suiteId: 's1', code: 'EINV-001', title: 'Login', description: null, preconditions: null, postconditions: null,
  priority: 1, severity: 1, status: 0, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 0, tags: [],
  creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null, concurrencyStamp: stamp,
  steps: [{ id: 'st1', stepOrder: 1, action: 'Open', expectedResult: 'Opens', testData: null }],
} as unknown as TestCase);

describe('two people editing the same test case', () => {
  let http: HttpTestingController;

  const openForm = (existing: TestCase | null) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'S' }]);
    fixture.componentRef.setInput('existing', existing);
    const saved: TestCase[] = [];
    fixture.componentInstance.saved.subscribe(t => saved.push(t));
    fixture.detectChanges();
    http.expectOne(`${ROOT}/test-cases/tags`).flush([]);
    fixture.detectChanges();
    return { fixture, saved, element: fixture.nativeElement as HTMLElement, form: fixture.componentInstance as unknown as { save(): void } };
  };

  beforeEach(() => localStorage.clear());

  it('sends the stamp it read when a test case is edited, so that the server can tell whether it changed meanwhile', () => {
    const { form } = openForm(testCase('stamp-1'));
    form.save();

    const request = http.expectOne(`${ROOT}/test-cases/tc1`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.concurrencyStamp).toBe('stamp-1');
  });

  it('sends no stamp when a test case is created', () => {
    const { form } = openForm(null);
    (form as unknown as { model: { title: string; steps: { action: string; expectedResult: string }[] } }).model.title = 'New';
    (form as unknown as { model: { steps: { action: string; expectedResult: string }[] } }).model.steps[0] = { action: 'a', expectedResult: 'b' };
    form.save();

    const request = http.expectOne(r => r.method === 'POST' && r.url === `${ROOT}/test-cases`);
    expect(request.request.body.concurrencyStamp ?? null).toBeNull();
  });

  it('says that nothing was saved when the server answers 409, keeps the form open, and lets the person try again', () => {
    const { fixture, saved, element, form } = openForm(testCase('stamp-1'));
    expect(element.querySelector('[data-test=conflict-banner]')).toBeNull();

    form.save();
    http.expectOne(`${ROOT}/test-cases/tc1`).flush({ error: { message: 'x' } }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(saved.length).toBe(0);
    expect(element.querySelector('[data-test=conflict-banner]')!.textContent).toContain('NOT saved');
    expect(element.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(false);
  });

  it('does not show that banner for another kind of failure', () => {
    const { fixture, element, form } = openForm(testCase('stamp-1'));

    form.save();
    http.expectOne(`${ROOT}/test-cases/tc1`).flush({ error: { message: 'No' } }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(element.querySelector('[data-test=conflict-banner]')).toBeNull();
  });

  it('turns a 409 into a sentence the person can act on, in both languages', () => {
    TestBed.resetTestingModule();
    const i18n = TestBed.inject(I18nService);
    const conflict = new HttpErrorResponse({ status: 409, error: { error: { message: 'Database operation failed' } } });

    expect(describeError(conflict, i18n)).toContain('Someone else');
    i18n.use('vi');
    expect(describeError(conflict, i18n)).toContain('người khác');
  });

  it('hands over the test case that the server returned after the tags were saved, with its new stamp', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase('stamp-1'));
    const seen: TestCase[] = [];
    fixture.componentInstance.tagsSaved.subscribe(t => seen.push(t));
    fixture.detectChanges();
    for (const request of http.match(r => r.method === 'GET')) { request.flush([]); }

    (fixture.componentInstance as unknown as { saveTags(tags: string[]): void }).saveTags(['smoke']);
    http.expectOne(`${ROOT}/test-cases/tc1/tags`).flush({ ...testCase('stamp-2'), tags: ['smoke'] });

    expect(seen.map(t => t.concurrencyStamp)).toEqual(['stamp-2']);
  });
});
