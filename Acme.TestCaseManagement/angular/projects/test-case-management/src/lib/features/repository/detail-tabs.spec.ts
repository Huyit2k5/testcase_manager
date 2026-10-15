import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TestCase } from '../../proxy/dtos';
import { TestCaseDetailComponent } from './test-case-detail';

const testCase = (status: number): TestCase => ({
  id: 'tc1', suiteId: 's1', code: 'EINV-001', title: 'Login', description: null, preconditions: null, postconditions: null,
  priority: 1, severity: 1, status, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 1, tags: [],
  creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null,
  steps: [{ id: 's1', stepOrder: 1, action: 'Open', expectedResult: 'Opens', testData: null }, { id: 's2', stepOrder: 2, action: 'Type', expectedResult: 'Typed', testData: null }],
} as unknown as TestCase);

describe('the dialog of a test case: tabs and the bar of actions', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<TestCaseDetailComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const panel = (name: string) => element().querySelector<HTMLElement>(`[data-panel=${name}]`)!;

  const open = (status = 1, permissions: string[] = ['*']) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    const http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TestCaseDetailComponent);
    fixture.componentRef.setInput('testCase', testCase(status));
    fixture.detectChanges();
    for (const request of http.match(r => r.method === 'GET')) { request.flush([]); }
    fixture.detectChanges();
  };
  const menuTexts = () => {
    element().querySelector<HTMLButtonElement>('app-row-menu button')?.click();
    fixture.detectChanges();
    return [...element().querySelectorAll('.row-menu-item')].map(i => i.textContent!.trim());
  };

  beforeEach(() => localStorage.clear());

  it('opens on the overview and shows one panel at a time', () => {
    open();
    expect(panel('overview').hidden).toBe(false);
    expect(panel('steps').hidden).toBe(true);
    expect(panel('links').hidden).toBe(true);
    expect(panel('history').hidden).toBe(true);

    element().querySelector<HTMLButtonElement>('[data-tab=steps]')!.click();
    fixture.detectChanges();
    expect(panel('overview').hidden).toBe(true);
    expect(panel('steps').hidden).toBe(false);
    expect(element().querySelector('[data-tab=steps]')!.getAttribute('aria-selected')).toBe('true');
  });

  it('counts the steps on their tab', () => {
    open();
    expect(element().querySelector('[data-tab=steps] .count')!.textContent).toBe('2');
  });

  it('previews the first steps on the overview, and the button for the rest opens the steps', () => {
    open();
    expect(element().querySelectorAll('[data-test=step-preview] li').length).toBe(2);
    expect(element().querySelector('[data-test=show-all-steps]')).toBeNull();

    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    const http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TestCaseDetailComponent);
    const many = testCase(1);
    many.steps = [1, 2, 3, 4, 5].map(n => ({ id: `s${n}`, stepOrder: n, action: `Step ${n}`, expectedResult: 'ok', testData: null })) as TestCase['steps'];
    fixture.componentRef.setInput('testCase', many);
    fixture.detectChanges();
    for (const request of http.match(r => r.method === 'GET')) { request.flush([]); }
    fixture.detectChanges();

    expect(element().querySelectorAll('[data-test=step-preview] li').length).toBe(3);
    const all = element().querySelector<HTMLButtonElement>('[data-test=show-all-steps]')!;
    expect(all.textContent).toContain('Show all 5 steps');
    all.click();
    fixture.detectChanges();
    expect(panel('steps').hidden).toBe(false);
  });

  it('keeps the step forward on the bar and puts the other moves, the copy and the delete under "more"', () => {
    open(1);
    const onBar = [...element().querySelectorAll('.btn')].map(b => b.textContent!.trim());
    expect(onBar).toContain('Approve');
    expect(onBar).toContain('Edit');
    expect(onBar).not.toContain('Delete');
    expect(onBar).not.toContain('Duplicate');
    expect(menuTexts()).toEqual(['Back to draft', 'Duplicate', 'Delete']);
  });

  it('has no "more" menu for someone who may do none of that', () => {
    open(2, ['TestCaseManagement.TestCases']);
    expect(element().querySelector('[data-test=more-actions]')).toBeNull();
  });
});
