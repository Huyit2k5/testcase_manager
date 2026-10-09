import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { RunsComponent } from './runs';

const ROOT = '/api/test-case-management';

const run = (n: number) => ({
  id: `r${n}`, testPlanId: 'p1', title: `Run ${n}`, environment: 'Staging', assignedToUserId: null, status: 1, items: [],
  summary: { totalItems: 4, executedItems: 2, passed: 1, failed: 1, blocked: 0, skipped: 0, untested: 2, completionPercentage: 50, firstTimePassRate: 50 },
});
const plan = (n: number) => ({ id: `p${n}`, name: `Plan ${n}`, description: null, milestoneId: null, startDate: null, endDate: null, status: n % 4 });

describe('the lists of runs and plans when there are many', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<RunsComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;

  const runList = () => http.expectOne(r => r.url === `${ROOT}/runs`);
  const answerRuns = (request: ReturnType<typeof runList>, count: number, total: number) =>
    request.flush({ totalCount: total, items: Array.from({ length: count }, (_, i) => run(i + 1)) });

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem('tcm.runsView', 'list');   // these tests are about the list view; the default shows the runs under their plans
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunsComponent);
    fixture.detectChanges();
  });

  const open = (runs: number, total: number, plans = 3) => {
    answerRuns(runList(), runs, total);
    http.expectOne(r => r.url === `${ROOT}/plans`).flush({ totalCount: plans, items: Array.from({ length: plans }, (_, i) => plan(i + 1)) });
    fixture.detectChanges();
  };

  it('asks for one page of runs at a time and shows the progress that comes with them', () => {
    const first = runList();
    expect(first.request.params.get('SkipCount') ?? first.request.params.get('skipCount')).toBe('0');
    expect(first.request.params.get('maxResultCount')).toBe('15');
    answerRuns(first, 15, 40);
    http.expectOne(r => r.url === `${ROOT}/plans`).flush({ totalCount: 0, items: [] });
    fixture.detectChanges();

    expect(element().querySelector('[data-test=run-pager]')?.textContent).toContain('Showing 1-15 of 40');
    expect(element().querySelector('[role=progressbar]')?.getAttribute('aria-valuenow')).toBe('50');
  });

  it('goes to the next page, and starts again from the first one when a filter changes', () => {
    open(15, 40);
    const buttons = () => [...element().querySelectorAll<HTMLButtonElement>('[data-test=run-pager] button')];

    buttons()[1].click();   // Next
    const second = runList();
    expect(second.request.params.get('skipCount')).toBe('15');
    answerRuns(second, 15, 40);
    fixture.detectChanges();
    expect(element().querySelector('[data-test=run-pager]')?.textContent).toContain('Showing 16-30 of 40');

    const search = element().querySelector<HTMLInputElement>('[data-test=run-filters] input')!;
    search.value = 'smoke';
    search.dispatchEvent(new Event('input'));
    element().querySelector<HTMLFormElement>('[data-test=run-filters]')!.dispatchEvent(new Event('submit'));
    const filtered = runList();
    expect(filtered.request.params.get('filter')).toBe('smoke');
    expect(filtered.request.params.get('skipCount')).toBe('0');
    answerRuns(filtered, 2, 2);
    fixture.detectChanges();
    expect(element().querySelector('[data-test=run-pager]')?.textContent).toContain('Showing 1-2 of 2');
  });

  it('filters by plan and by status on the server', () => {
    open(3, 3);
    const selects = element().querySelectorAll<HTMLSelectElement>('[data-test=run-filters] select');

    selects[0].value = selects[0].options[2].value;
    selects[0].dispatchEvent(new Event('change'));
    const byPlan = runList();
    expect(byPlan.request.params.get('testPlanId')).toBe('p2');
    answerRuns(byPlan, 1, 1);
    fixture.detectChanges();

    selects[1].value = selects[1].options[2].value;
    selects[1].dispatchEvent(new Event('change'));
    const byStatus = runList();
    expect(byStatus.request.params.get('status')).toBe('1');
    expect(byStatus.request.params.get('testPlanId')).toBe('p2');
    answerRuns(byStatus, 0, 0);
    fixture.detectChanges();
    expect(element().querySelector('.empty')?.textContent).toContain('No run matches');
  });

  it('keeps the plans in a table of its own that is filtered here, and pages only when there are many', () => {
    open(0, 0, 25);
    const pager = () => element().querySelector('[data-test=plan-pager]');
    const names = () => [...element().querySelectorAll('tbody')][1].querySelectorAll('tr').length;

    expect(names()).toBe(10);
    expect(pager()?.textContent).toContain('Showing 1-10 of 25');

    const search = element().querySelector<HTMLInputElement>('[data-test=plan-filters] input')!;
    search.value = 'Plan 2';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(names()).toBe(7);   // Plan 2, Plan 20 to Plan 25 — and no pager, so few of them
    expect(pager()).toBeNull();
  });
});
