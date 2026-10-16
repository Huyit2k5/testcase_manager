import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { MyWork, MyWorkItem, TestRun } from '../../proxy/dtos';
import { RunDetailComponent } from '../runs/run-detail';
import { MyWorkComponent } from './my-work';

const ROOT = '/api/test-case-management';
const row = (id: string, currentStatus: number, attemptCount = 0): MyWorkItem => ({
  runItemId: id, runId: 'R' + id, runTitle: 'Run ' + id, environment: 'Staging', projectId: 'p1',
  testCaseId: 't' + id, testCaseCode: 'TC-' + id, testCaseTitle: 'Case ' + id, versionNumber: 1, currentStatus, attemptCount,
});
const work = (items: MyWorkItem[], extra: Partial<MyWork> = {}): MyWork =>
  ({ items, total: items.length, untested: items.filter(i => i.currentStatus === 0).length, needAttention: 0, truncated: false, ...extra });

describe('My work', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<MyWorkComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;

  function open(answer: MyWork, ...permissions: string[]) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(...(permissions.length ? permissions : ['*']))] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyWorkComponent);
    fixture.detectChanges();
    const request = http.expectOne(r => r.url === `${ROOT}/runs/my-work`);
    expect(request.request.params.has('includeDone')).toBe(false);
    request.flush(answer);
    fixture.detectChanges();
  }

  beforeEach(() => localStorage.clear());

  it('lists what is assigned, with the figures, and an action that opens the item in its run', () => {
    open(work([row('1', 0), row('2', 2, 1)], { needAttention: 1 }));
    const stats = [...element().querySelectorAll('[data-test=stats] .stat')].map(s => s.querySelector('b')?.textContent);
    expect(stats).toEqual(['2', '1', '1']);

    const rows = element().querySelectorAll('[data-test=my-work-table] tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('TC-1');
    expect(rows[0].textContent).toContain('Run 1');
    const links = element().querySelectorAll<HTMLAnchorElement>('[data-test=run-item]');
    expect(links[0].getAttribute('href')).toBe('/runs/R1?item=1');
    expect(links[0].textContent?.trim()).toBe('Execute');
    expect(links[1].textContent?.trim()).toBe('Retest');
  });

  it('asks for the finished items too when the box is ticked', () => {
    open(work([row('1', 0)]));
    const box = element().querySelector<HTMLInputElement>('[data-test=include-done]')!;
    box.click();
    const request = http.expectOne(r => r.url === `${ROOT}/runs/my-work`);
    expect(request.request.params.get('includeDone')).toBe('true');
    request.flush(work([row('1', 0), row('2', 1, 1)]));
    fixture.detectChanges();
    expect(element().querySelectorAll('[data-test=my-work-table] tbody tr').length).toBe(2);
  });

  it('says so when nothing is assigned, and when everything assigned is done', () => {
    open(work([]));
    expect(element().querySelector('[data-test=my-work-empty]')?.textContent).toContain('Nothing is assigned');

    open(work([], { total: 3 }));
    expect(element().querySelector('[data-test=my-work-empty]')?.textContent).toContain('Nothing left to run');
  });

  it('shows no action to someone who may not execute, and warns when the list was cut', () => {
    open(work([row('1', 0)], { truncated: true }), Permissions.TestRuns.Default);
    expect(element().querySelector('[data-test=run-item]')).toBeNull();
    expect(element().querySelector('[data-test=truncated]')).not.toBeNull();
  });

  it('shows an error when the call fails', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyWorkComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/runs/my-work`).flush(null, { status: 500, statusText: 'Server error' });
    fixture.detectChanges();
    expect(element().querySelector('.alert.bad')).not.toBeNull();
  });
});

describe('The run page, opened from My work', () => {
  const run = (status = 1) => ({
    id: 'A', title: 'Run A', environment: 'Staging', status,
    items: [{ id: 'i1', sequence: 1, testCaseId: 't1', testCaseCode: 'TC-1', testCaseTitle: 'Case 1', versionNumber: 1, assignedUserId: null, currentStatus: 0, attemptCount: 0 }],
    summary: { totalItems: 1, passed: 0, failed: 0, blocked: 0, skipped: 0, untested: 1, completionPercentage: 0, firstTimePassRate: null },
  }) as unknown as TestRun;

  function open(item: string | null, answer: TestRun, permission = '*') {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(permission),
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: 'A' })), queryParamMap: new BehaviorSubject(convertToParamMap(item ? { item } : {})) } },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RunDetailComponent);
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush(answer);
    fixture.detectChanges();
    http.match(() => true);
    return (fixture.componentInstance as unknown as Record<string, WritableSignal<{ item: { id: string } } | null>>)['executeForm']();
  }

  it('opens the form of the requested item', () => {
    expect(open('i1', run())?.item.id).toBe('i1');
  });

  it('opens nothing without the request, for an item that is not in the run, on a completed run, or without the right to execute', () => {
    expect(open(null, run())).toBeNull();
    expect(open('zzz', run())).toBeNull();
    expect(open('i1', run(2))).toBeNull();
    expect(open('i1', run(), Permissions.TestRuns.Default)).toBeNull();
  });
});
