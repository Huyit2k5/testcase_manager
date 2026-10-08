import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { BurnDownPoint, Dashboard, FlakyTestList, VelocityPoint } from '../../proxy/dtos';
import { CHART, burnDownChart, linePath, niceMax, velocityChart } from './chart';
import { DashboardComponent } from './dashboard';

const label = (iso: string) => iso.slice(5, 10);

describe('niceMax', () => {
  it('rounds up to 1, 2, 5 or 10 times a power of ten', () => {
    expect([0, 1, 2, 3, 5, 6, 10, 11, 23, 70, 101].map(niceMax)).toEqual([1, 1, 2, 5, 5, 10, 10, 20, 50, 100, 200]);
  });
});

describe('linePath', () => {
  it('lifts the pen at a missing value, so a line ends where the data does', () => {
    const path = linePath([4, 2, null, null, 1], 4);

    expect(path.match(/M/g)?.length).toBe(2);
    expect(path.startsWith('M')).toBe(true);
    expect(path).not.toContain('NaN');
  });

  it('puts the maximum at the top of the plot and zero at the bottom', () => {
    const top = linePath([4], 4);
    const bottom = linePath([0], 4);

    expect(top).toContain(`,${CHART.top}`);
    expect(bottom).toContain(`,${CHART.height - CHART.bottom}`);
  });
});

describe('burnDownChart', () => {
  const points = (remaining: (number | null)[], ideal: number[]): BurnDownPoint[] =>
    remaining.map((r, i) => ({ date: `2026-03-${String(i + 1).padStart(2, '0')}T00:00:00`, remaining: r, ideal: ideal[i] }));

  it('scales to the highest of both lines and marks the last day with data', () => {
    const chart = burnDownChart(points([10, 7, 7, null, null], [10, 7.5, 5, 2.5, 0]), label);

    expect(chart.max).toBe(10);
    expect(chart.yTicks.at(-1)?.label).toBe('10');
    expect(chart.yTicks[0].label).toBe('0');
    expect(chart.actual.match(/M/g)?.length).toBe(1);
    expect(chart.actual.split('L').length).toBe(3);   // three days of data
    expect(chart.ideal.split('L').length).toBe(5);
    expect(chart.todayX).not.toBeNull();
    expect(chart.xTicks[0].label).toBe('03-01');
  });

  it('has no today marker without data and draws an empty scope without NaN', () => {
    const none = burnDownChart(points([null, null], [0, 0]), label);
    expect(none.todayX).toBeNull();
    expect(none.max).toBe(1);

    const empty = burnDownChart([], label);
    expect(empty.actual).toBe('');
    expect(JSON.stringify(empty)).not.toContain('NaN');
  });
});

describe('velocityChart', () => {
  const day = (i: number, attempts: number, passed: number, failed: number): VelocityPoint =>
    ({ date: `2026-03-${String(i + 1).padStart(2, '0')}T00:00:00`, attempts, itemsCompleted: 0, passed, failed });

  it('stacks failed at the bottom, then other results, then passed, to the height of the attempts', () => {
    const chart = velocityChart([day(0, 10, 6, 3), day(1, 0, 0, 0)], label);

    expect(chart.max).toBe(10);
    const [first, second] = chart.bars;
    const base = CHART.height - CHART.bottom;
    expect(first.failed.y + first.failed.height).toBeCloseTo(base, 0);
    expect(first.other.y + first.other.height).toBeCloseTo(first.failed.y, 0);
    expect(first.passed.y + first.passed.height).toBeCloseTo(first.other.y, 0);
    // Passed is 6 of 10, failed 3, other 1: the heights follow the counts.
    expect(first.passed.height / first.failed.height).toBeCloseTo(2, 0);
    expect(second.passed.height + second.failed.height + second.other.height).toBe(0);
  });

  it('keeps every bar inside the plot and labels the first and the last day', () => {
    const chart = velocityChart(Array.from({ length: 30 }, (_, i) => day(i, i, i, 0)), label);

    expect(chart.bars.every(b => b.x >= CHART.left && b.x + b.width <= CHART.width - CHART.right)).toBe(true);
    expect(chart.xTicks[0].label).toBe('03-01');
    expect(chart.xTicks.at(-1)?.label).toBe('03-30');
    expect(chart.xTicks.length).toBeLessThanOrEqual(10);
  });
});

describe('DashboardComponent', () => {
  let http: HttpTestingController;
  let component: DashboardComponent;
  const read = <T>(name: string) => (component as unknown as Record<string, () => T>)[name]();
  const call = <T>(name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);

  const dashboard = (): Dashboard => ({
    testPlanId: null, testPlanName: null, days: 14, generatedAt: '2026-03-20T10:00:00', runCount: 3,
    progress: { totalItems: 5, passed: 3, failed: 1, blocked: 0, skipped: 0, untested: 1, completionPercentage: 80, passRate: 75, firstTimePassRate: 50 },
    velocity: { points: [], totalAttempts: 0, averagePerDay: 0, last7DaysAverage: 0, trendPercent: null },
    burnDown: { start: '2026-03-07T00:00:00', end: '2026-03-20T00:00:00', totalItems: 5, remainingAtStart: 5, remainingNow: 1, points: [], itemsPerDay: 0, projectedFinish: null, onTrack: null },
    defectDensity: { defects: 0, openDefects: 0, resolvedDefects: 0, executedTests: 4, defectsPer100Executed: 0, testsWithDefects: 0, testsWithDefectsPercent: 0, openCritical: 0, openHigh: 0, openMedium: 0, openLow: 0 },
    flaky: { flaky: 1, watch: 0, scored: 2 },
  });

  const flakyList = (): FlakyTestList => ({
    items: [], totalCount: 0, flakyCount: 0, watchCount: 0,
    settings: { windowSize: 20, minimumObservations: 5, watchScore: 0.15, flakyScore: 0.3, lookbackDays: 90 },
  });

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(DashboardComponent).componentInstance;
  });

  it('asks for the dashboard of the chosen plan and period, and for the flaky tests on watch or worse', () => {
    component.ngOnInit();
    http.expectOne(r => r.url === '/api/test-case-management/plans').flush({ items: [], totalCount: 0 });
    const first = http.expectOne(r => r.url === '/api/test-case-management/dashboard');
    expect(first.request.params.get('Days')).toBe('14');
    expect(first.request.params.has('TestPlanId')).toBe(false);
    first.flush(dashboard());
    const flaky = http.expectOne(r => r.url === '/api/test-case-management/flaky-tests');
    expect(flaky.request.params.get('MinimumLevel')).toBe('2');
    flaky.flush(flakyList());

    expect(read<Dashboard>('dashboard').progress.passRate).toBe(75);
    expect(read<boolean>('loading')).toBe(false);

    (component as unknown as { planId: string; days: number }).planId = 'plan-1';
    (component as unknown as { planId: string; days: number }).days = 30;
    call('reload');
    const second = http.expectOne(r => r.url === '/api/test-case-management/dashboard');
    expect(second.request.params.get('TestPlanId')).toBe('plan-1');
    expect(second.request.params.get('Days')).toBe('30');
    second.flush(dashboard());
    http.expectOne(r => r.url === '/api/test-case-management/flaky-tests').flush(flakyList());
  });

  it('applies the flags, with or without clearing recovered tests', () => {
    (component as unknown as { clearRecovered: boolean }).clearRecovered = true;
    call('apply');

    const request = http.expectOne('/api/test-case-management/flaky-tests/apply');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ clearRecovered: true });
    request.flush({ flagged: 2, cleared: 1, flaggedCodes: [], clearedCodes: [] });
    http.expectOne(r => r.url === '/api/test-case-management/flaky-tests').flush(flakyList());
  });

  it('colours the levels and writes the trend with its sign', () => {
    expect(call<string>('levelClass', 3)).toBe('bad');
    expect(call<string>('levelClass', 2)).toBe('warn');
    expect(call<string>('levelClass', 1)).toBe('muted');
    expect(call<string>('trendText', 12.5)).toBe('+12.5%');
    expect(call<string>('trendText', -4)).toBe('-4%');
    expect(call<string>('trendText', null)).toBe('');
    expect(call<string>('scoreWidth', 0.456)).toBe('46%');
  });

  it('ignores a slow answer for an old filter that arrives after the answer for the current one', () => {
    call('reload');
    const [old] = http.match(r => r.url === '/api/test-case-management/dashboard');
    http.match(r => r.url === '/api/test-case-management/flaky-tests');
    call('reload');
    const [latest] = http.match(r => r.url === '/api/test-case-management/dashboard');
    http.match(r => r.url === '/api/test-case-management/flaky-tests');

    latest.flush({ ...dashboard(), runCount: 2 });
    old.flush({ ...dashboard(), runCount: 1 });

    expect(read<Dashboard>('dashboard').runCount).toBe(2);
    expect(read<boolean>('loading')).toBe(false);
  });
});

describe('DashboardComponent without the secondary permissions', () => {
  it('does not ask for the plans or the flaky tests, and hides the flaky card', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(Permissions.TestRuns.Default)] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();

    http.expectNone(r => r.url === '/api/test-case-management/plans');
    http.expectNone(r => r.url === '/api/test-case-management/flaky-tests');
    http.expectOne(r => r.url === '/api/test-case-management/dashboard');
    expect(fixture.nativeElement.querySelector('[data-test=flaky]')).toBeNull();
  });
});
