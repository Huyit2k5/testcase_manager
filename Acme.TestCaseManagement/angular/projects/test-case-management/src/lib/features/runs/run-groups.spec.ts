import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { RunsComponent } from './runs';

const ROOT = '/api/test-case-management';

const run = (id: string, planId: string | null) => ({
  id, testPlanId: planId, title: `Run ${id}`, environment: 'Staging', assignedToUserId: null, status: 1, items: [],
  summary: { totalItems: 4, executedItems: 2, passed: 1, failed: 1, blocked: 0, skipped: 0, untested: 2, completionPercentage: 50, firstTimePassRate: 50 },
});
const plan = (n: number, runCount: number) => ({
  id: `p${n}`, name: `Plan ${n}`, description: n === 1 ? 'The first one' : null, milestoneId: null, startDate: null, endDate: null, status: 1, runCount,
});

describe('the runs under their plans', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<RunsComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const groups = () => [...element().querySelectorAll('[data-test=plan-group]')];
  const toggle = (group: Element) => (group.querySelector('.group-toggle') as HTMLButtonElement).click();
  const runsOf = (predicate: (params: { get(name: string): string | null }) => boolean) =>
    http.expectOne(r => r.url === `${ROOT}/runs` && predicate(r.params));

  /** The page asks for the plans, and for the runs without a plan; then for the runs of the first plan, which it opens. */
  const open = (noPlan = 0) => {
    http.expectOne(r => r.url === `${ROOT}/plans`).flush({ totalCount: 3, items: [plan(1, 2), plan(2, 1), plan(3, 0)] });
    http.expectOne(r => r.url === `${ROOT}/runs` && r.params.get('noPlan') === 'true')
      .flush({ totalCount: noPlan, items: Array.from({ length: noPlan }, (_, i) => run(`n${i}`, null)) });
    http.expectOne(r => r.url === `${ROOT}/runs` && r.params.get('testPlanId') === 'p1').flush({ totalCount: 2, items: [run('a', 'p1'), run('b', 'p1')] });
    fixture.detectChanges();
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunsComponent);
    fixture.detectChanges();
  });

  it('is the view that opens, with a group for each plan, its number of runs, and the runs of the first plan shown', () => {
    open();

    expect(groups().length).toBe(3);
    expect(groups()[0].querySelector('.group-name')?.textContent).toContain('Plan 1');
    expect(groups()[0].textContent).toContain('2 run(s)');
    expect(groups()[0].querySelector('.group-toggle')?.getAttribute('aria-expanded')).toBe('true');
    expect([...groups()[0].querySelectorAll('tbody tr')].map(r => r.textContent)).toEqual([expect.stringContaining('Run a'), expect.stringContaining('Run b')]);
    expect(groups()[0].textContent).toContain('The first one');
    // The other groups are closed and have asked for nothing yet.
    expect(groups()[1].querySelector('tbody')).toBeNull();
    http.verify();
  });

  it('asks for the runs of a plan when its group is opened, and hides them when it is closed again', () => {
    open();

    toggle(groups()[1]);
    const request = runsOf(p => p.get('testPlanId') === 'p2');
    expect(request.request.params.get('maxResultCount')).toBe('100');
    request.flush({ totalCount: 1, items: [run('c', 'p2')] });
    fixture.detectChanges();
    expect(groups()[1].querySelectorAll('tbody tr').length).toBe(1);

    toggle(groups()[1]);
    fixture.detectChanges();
    expect(groups()[1].querySelector('tbody')).toBeNull();
    http.verify();
  });

  it('says so for a plan that has no run, and when a group holds more runs than it shows', () => {
    open();

    toggle(groups()[2]);
    runsOf(p => p.get('testPlanId') === 'p3').flush({ totalCount: 0, items: [] });
    fixture.detectChanges();
    expect(groups()[2].querySelector('.empty')?.textContent).toContain('No run in this plan yet');

    toggle(groups()[1]);
    runsOf(p => p.get('testPlanId') === 'p2').flush({ totalCount: 250, items: [run('c', 'p2')] });
    fixture.detectChanges();
    expect(groups()[1].textContent).toContain('Showing 1 of 250 runs');
  });

  it('has a group for the runs without a plan only when there are some', () => {
    open(0);
    expect(element().querySelector('[data-test=no-plan-group]')).toBeNull();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunsComponent);
    fixture.detectChanges();
    open(2);
    const loose = element().querySelector('[data-test=no-plan-group]')!;
    expect(loose.textContent).toContain('Runs without a plan');
    expect(loose.textContent).toContain('2 run(s)');
  });

  it('switches to the list and remembers the choice', () => {
    open();
    const buttons = element().querySelectorAll<HTMLButtonElement>('[data-test=run-view] button');

    buttons[1].click();
    http.expectOne(r => r.url === `${ROOT}/runs` && r.params.get('maxResultCount') === '15' && !r.params.has('testPlanId') && !r.params.has('noPlan'))
      .flush({ totalCount: 0, items: [] });
    fixture.detectChanges();

    expect(localStorage.getItem('tcm.runsView')).toBe('list');
    expect(element().querySelector('[data-test=run-filters]')).not.toBeNull();
    expect(groups().length).toBe(0);
  });
});
