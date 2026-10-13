import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { Dashboard, FlakyTestList, SprintTrend, SprintTrendPoint, Workload } from '../../proxy/dtos';
import { trendChart } from './chart';
import { DashboardComponent } from './dashboard';

const ROOT = '/api/test-case-management';

const point = (name: string, passRate: number | null, extra: Partial<SprintTrendPoint> = {}): SprintTrendPoint => ({
  planId: name, planName: name, status: 1, milestoneId: null, startDate: '2026-03-01T00:00:00', endDate: null,
  runCount: 2, totalItems: 10, passed: 8, failed: 2, blocked: 0, skipped: 0, untested: 0, completionPercentage: 100,
  passRate, firstTimePassRate: passRate === null ? null : passRate - 10, defects: 3, openDefects: 1, defectsPer100Executed: 30, ...extra,
});

const trend = (points: SprintTrendPoint[], change: number | null = null): SprintTrend => ({
  generatedAt: '2026-03-20T10:00:00', points, passRateChange: change, firstTimePassRateChange: null, defectDensityChange: null, averagePassRate: points.length ? 70 : null,
});

const tester = (userId: string | null, attempts: number, extra: object = {}) => ({
  userId, assignedItems: 4, assignedDone: 3, assignedPassed: 2, assignedFailed: 1, attempts, passed: 2, failed: 1, blocked: 1, skipped: 0,
  itemsExecuted: 3, durationSeconds: 4500, activeDays: 2, attemptsPerDay: 0.5, passRate: 66.66, ...extra,
});

const workload = (testers: object[]): Workload => ({
  testPlanId: null, testPlanName: null, days: 14, generatedAt: '2026-03-20T10:00:00', testers: testers as Workload['testers'],
  unassignedItems: 3, unassignedUntested: 2, totalItems: 12, totalAttempts: 9,
});

describe('the chart of the trend over sprints', () => {
  it('puts 100% at the top and 0% at the bottom, and keeps a gap where a plan has no rate', () => {
    const chart = trendChart([point('A', 100), point('B', null), point('C', 0)]);

    expect(chart.passRate.match(/M/g)?.length).toBe(2);   // the line stops at B and starts again at C
    expect(chart.dots.map(d => d.planName)).toEqual(['A', 'C']);
    expect(chart.dots[0].y).toBeLessThan(chart.dots[1].y);
    expect(chart.yTicks.map(t => t.label)).toEqual(['0%', '25%', '50%', '75%', '100%']);
    expect(chart.passRate).not.toContain('NaN');
  });

  it('shortens a long plan name under its point', () => {
    const chart = trendChart([point('Sprint 24 - Hóa đơn điện tử', 50), point('S2', 60)]);

    expect(chart.xTicks[0].label.length).toBeLessThanOrEqual(14);
    expect(chart.xTicks[0].label.endsWith('…')).toBe(true);
    expect(chart.xTicks[1].label).toBe('S2');
  });
});

describe('the trend and the workload on the dashboard', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<DashboardComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const page = () => fixture.componentInstance as unknown as Record<string, (...a: unknown[]) => unknown> & { sprints: number; planId: string; days: number };

  const setup = (permissions: string[] = ['*']) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
  };

  /** Answers the calls that are not the subject of the test, so that only the trend and the workload are left. */
  const answerOthers = () => {
    for (const request of http.match(r => !r.url.endsWith('/dashboard/trend') && !r.url.endsWith('/dashboard/workload'))) {
      if (request.request.url.endsWith('/plans')) { request.flush({ items: [], totalCount: 0 }); }
      else if (request.request.url.endsWith('/dashboard')) { request.flush({ progress: { totalItems: 0 }, velocity: { points: [] }, burnDown: { points: [] }, defectDensity: {}, flaky: {} } as unknown as Dashboard); }
      else if (request.request.url.endsWith('/flaky-tests')) { request.flush({ items: [], totalCount: 0, settings: {} } as unknown as FlakyTestList); }
      else { request.flush([]); }
    }
  };

  beforeEach(() => setup());

  it('asks for the latest sprints and shows each plan with its rates, and what the last one moved by', () => {
    const request = http.expectOne(r => r.url === `${ROOT}/dashboard/trend`);
    expect(request.request.params.get('Sprints')).toBe('8');
    request.flush(trend([point('Sprint 1', 60), point('Sprint 2', 75)], 15));
    answerOthers();
    fixture.detectChanges();

    const rows = [...element().querySelectorAll('[data-test=trend-table] tbody tr')];
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('Sprint 2');
    expect(rows[1].textContent).toContain('75%');
    expect(element().querySelector('[data-test=trend-chart]')).not.toBeNull();
    const summary = element().querySelector('[data-test=trend-summary]')!;
    expect(summary.textContent).toContain('+15 pts');
    expect(summary.querySelector('em')!.className).toContain('good');
  });

  it('shows a fall in red, and a rise of the defect density as a fall too', () => {
    http.expectOne(r => r.url === `${ROOT}/dashboard/trend`).flush({ ...trend([point('A', 80), point('B', 70)], -10), defectDensityChange: 5 });
    answerOthers();
    fixture.detectChanges();

    const marks = [...element().querySelectorAll('[data-test=trend-summary] em')].map(e => e.className);
    expect(marks).toEqual(['low', 'low']);
  });

  it('says there is nothing to show when no plan has run items', () => {
    http.expectOne(r => r.url === `${ROOT}/dashboard/trend`).flush(trend([]));
    answerOthers();
    fixture.detectChanges();

    expect(element().querySelector('[data-test=trend-empty]')).not.toBeNull();
    expect(element().querySelector('[data-test=trend-table]')).toBeNull();
  });

  it('asks again when the number of sprints changes, and keeps the answer to the last question when an older one comes late', () => {
    const first = http.expectOne(r => r.url === `${ROOT}/dashboard/trend`);
    answerOthers();

    page().sprints = 20;
    page()['reloadTrend']();
    const second = http.expectOne(r => r.url === `${ROOT}/dashboard/trend`);
    expect(second.request.params.get('Sprints')).toBe('20');
    second.flush(trend([point('New', 90), point('Newer', 95)]));
    first.flush(trend([point('Old', 10)]));
    fixture.detectChanges();

    expect([...element().querySelectorAll('[data-test=trend-table] tbody tr')].map(r => r.textContent)).toEqual([
      expect.stringContaining('New'), expect.stringContaining('Newer'),
    ]);
  });

  it('shows who has what: the items given and done, the attempts and their pass rate, the time, and the items given to nobody', () => {
    http.expectOne(r => r.url === `${ROOT}/dashboard/trend`).flush(trend([]));
    const request = http.expectOne(r => r.url === `${ROOT}/dashboard/workload`);
    expect(request.request.params.get('Days')).toBe('14');
    expect(request.request.params.has('TestPlanId')).toBe(false);
    request.flush(workload([tester('u1', 6), tester(null, 3, { assignedItems: 0, assignedDone: 0 })]));
    answerOthers();
    fixture.detectChanges();

    const rows = [...element().querySelectorAll('[data-test=workload-table] tbody tr')];
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('3 / 4');
    expect(rows[0].textContent).toContain('6');
    expect(rows[0].textContent).toContain('1h 15m');
    expect(rows[0].textContent).toContain('66.66%');
    expect(rows[1].textContent).toContain('Pipeline or unknown');
    expect(element().querySelector('[data-test=workload-unassigned]')!.textContent).toContain('3 of 12');
    expect(element().querySelector('[data-test=workload-unassigned]')!.textContent).toContain('2 of them');
  });

  it('asks for the workload of the chosen plan and period', () => {
    http.expectOne(r => r.url === `${ROOT}/dashboard/workload`);
    answerOthers();

    page().planId = 'plan-9';
    page().days = 30;
    page()['reload']();
    const request = http.expectOne(r => r.url === `${ROOT}/dashboard/workload`);

    expect(request.request.params.get('TestPlanId')).toBe('plan-9');
    expect(request.request.params.get('Days')).toBe('30');
  });

  it('says so when nobody has work or attempts in the period', () => {
    http.expectOne(r => r.url === `${ROOT}/dashboard/workload`).flush(workload([]));
    answerOthers();
    fixture.detectChanges();

    expect(element().querySelector('[data-test=workload-empty]')).not.toBeNull();
  });

  it('does not ask for the workload, nor show it, to someone who may not manage plans, but still shows the trend', () => {
    setup(['TestCaseManagement.TestRuns', 'TestCaseManagement.TestPlans', 'TestCaseManagement.TestCases']);

    http.expectOne(r => r.url === `${ROOT}/dashboard/trend`).flush(trend([point('A', 50), point('B', 60)]));
    answerOthers();
    fixture.detectChanges();

    http.expectNone(r => r.url === `${ROOT}/dashboard/workload`);
    expect(element().querySelector('[data-test=workload]')).toBeNull();
    expect(element().querySelector('[data-test=trend]')).not.toBeNull();
  });

  it('writes a time, a change and its colour in a form that reads at a glance', () => {
    answerOthers();
    http.match(() => true);
    const say = (name: string, ...args: unknown[]) => page()[name](...args);

    expect([0, 40, 720, 3900, 7200].map(s => say('duration', s))).toEqual(['-', '40s', '12m', '1h 05m', '2h 00m']);
    expect(say('points', 4.5)).toBe('+4.5 pts');
    expect(say('points', -3)).toBe('-3 pts');
    expect(say('points', null)).toBe('');
    expect([say('changeClass', 2), say('changeClass', -2), say('changeClass', 0), say('changeClass', null)]).toEqual(['good', 'low', 'muted', 'muted']);
    expect([say('changeClass', 2, true), say('changeClass', -2, true)]).toEqual(['low', 'good']);
  });
});
