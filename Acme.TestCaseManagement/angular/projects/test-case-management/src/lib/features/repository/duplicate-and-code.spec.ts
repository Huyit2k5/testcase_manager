import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TestCase } from '../../proxy/dtos';
import { TestCaseDetailComponent } from './test-case-detail';
import { TestCaseFormComponent } from './test-case-form';

const ROOT = '/api/test-case-management';
const LIST = `${ROOT}/test-cases`;

const testCase = (code: string): TestCase => ({
  id: 'tc1', suiteId: 's1', code, title: 'Login', description: null, preconditions: null, postconditions: null,
  priority: 1, severity: 1, status: 1, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 1, tags: [],
  creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null, steps: [],
});

describe('duplicating a test case', () => {
  let http: HttpTestingController;
  let copies: TestCase[];

  const open = (permissions: string[] = ['*']) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase('EINV-001'));
    copies = [];
    fixture.componentInstance.copied.subscribe(copy => copies.push(copy));
    fixture.detectChanges();
    for (const request of http.match(r => r.method === 'GET')) { request.flush([]); }
    fixture.detectChanges();
    return fixture;
  };
  /** The copy is one of the choices under "more" at the foot of the dialog: open the menu, then look for it. */
  const menuItem = (fixture: ReturnType<typeof open>) => {
    const element = fixture.nativeElement as HTMLElement;
    element.querySelector<HTMLButtonElement>('app-row-menu button')?.click();
    fixture.detectChanges();
    return [...element.querySelectorAll<HTMLButtonElement>('.row-menu-item')].find(item => item.textContent!.includes('Duplicate')) ?? null;
  };
  const click = (fixture: ReturnType<typeof open>) => menuItem(fixture)!.click();

  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it('asks for the title of the copy, sends it, and hands over the copy that the server made', () => {
    vi.stubGlobal('prompt', () => '  Login with a link ');
    click(open());

    const request = http.expectOne(`${LIST}/tc1/duplicate`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ title: 'Login with a link' });
    request.flush({ ...testCase('EINV-002'), id: 'tc2', status: 0 });
    expect(copies.map(c => c.code)).toEqual(['EINV-002']);
  });

  it('lets the server choose the title when none is typed, and does nothing when the question is cancelled', () => {
    vi.stubGlobal('prompt', () => '');
    click(open());
    expect(http.expectOne(`${LIST}/tc1/duplicate`).request.body).toEqual({ title: null });

    vi.stubGlobal('prompt', () => null);
    click(open());
    http.expectNone(`${LIST}/tc1/duplicate`);
    expect(copies).toEqual([]);
  });

  it('is offered only to who may create test cases', () => {
    const allowed = open(['TestCaseManagement.TestCases', 'TestCaseManagement.TestCases.Update']);
    expect(menuItem(allowed)).toBeNull();
    expect(menuItem(open())).not.toBeNull();
  });
});

describe('the code of a test case in the form', () => {
  let http: HttpTestingController;

  const open = (existing: TestCase | null) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'S' }]);
    fixture.componentRef.setInput('existing', existing);
    fixture.detectChanges();
    http.expectOne(`${LIST}/tags`).flush([]);
    fixture.detectChanges();
    return fixture;
  };
  const form = (fixture: ReturnType<typeof open>) =>
    fixture.componentInstance as unknown as { model: { code?: string | null; title: string; steps: unknown[] }; save(): void };

  it('may be left empty for a new test case, which is sent as no code so that the project gives the next one', () => {
    const fixture = open(null);
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-test=code-input]')!;
    expect(input.required).toBe(false);
    expect(input.placeholder).toContain('next code');

    form(fixture).model.title = 'Pay';
    form(fixture).model.code = '   ';
    form(fixture).model.steps.push({ action: 'a', expectedResult: 'b' });
    form(fixture).save();

    expect(http.expectOne(r => r.url === LIST && r.method === 'POST').request.body.code).toBeNull();
  });

  it('is sent as it was typed, without the spaces around it', () => {
    const fixture = open(null);
    form(fixture).model.title = 'Pay';
    form(fixture).model.code = '  PAY-9 ';
    form(fixture).model.steps.push({ action: 'a', expectedResult: 'b' });
    form(fixture).save();

    expect(http.expectOne(r => r.url === LIST && r.method === 'POST').request.body.code).toBe('PAY-9');
  });

  it('is required when a test case is edited, and starts as the code it has', () => {
    const fixture = open(testCase('EINV-004'));
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('[data-test=code-input]')!;
    expect(input.required).toBe(true);
    expect(form(fixture).model.code).toBe('EINV-004');
  });
});
