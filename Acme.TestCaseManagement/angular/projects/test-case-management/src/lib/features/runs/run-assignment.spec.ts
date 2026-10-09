import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Provider, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { TCM_USER_DIRECTORY, TcmDirectoryUser } from '../../core/host';
import { UserNames } from '../../core/users';
import { TestRun } from '../../proxy/dtos';
import { RunDetailComponent } from './run-detail';

const ROOT = '/api/test-case-management';
const people: TcmDirectoryUser[] = [
  { id: 'u1', userName: 'tester', displayName: 'Tester One' },
  { id: 'u2', userName: 'ann', displayName: 'Ann Lee' },
];
const item = (id: string, assignedUserId: string | null, currentStatus = 0) =>
  ({ id, sequence: 1, testCaseId: 't' + id, testCaseCode: 'TC-' + id, testCaseTitle: 'Case ' + id, versionNumber: 1, assignedUserId, currentStatus, attemptCount: 0 });
const run = (items: unknown[], status = 1) =>
  ({ id: 'A', title: 'Run A', environment: 'Staging', status, items, summary: { totalItems: items.length, passed: 0, failed: 0, blocked: 0, skipped: 0, untested: items.length, completionPercentage: 0, firstTimePassRate: null } }) as unknown as TestRun;

describe('Assigning testers in a run', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<RunDetailComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const sig = <T>(name: string) => (fixture.componentInstance as unknown as Record<string, WritableSignal<T>>)[name];

  function open(providers: Provider[], answer: TestRun, permissions: string[] = ['*']) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(...permissions),
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: 'A' })) } },
        ...providers,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunDetailComponent);
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush(answer);
    fixture.detectChanges();
  }
  const withPeople = (): Provider => ({ provide: TCM_USER_DIRECTORY, useValue: { list: () => of(people) } });

  beforeEach(() => localStorage.clear());

  it('offers a person per item and sends the choice, then shows the run the server returns', () => {
    open([withPeople()], run([item('1', null), item('2', 'u2')]));
    // ngModel writes the value of a select after the first change detection.
    return fixture.whenStable().then(() => {
      const selects = element().querySelectorAll<HTMLSelectElement>('[data-test=assign-select]');
      expect(selects.length).toBe(2);
      expect(selects[1].value).toBe('u2');

      selects[0].value = 'u1';
      selects[0].dispatchEvent(new Event('change'));

      const request = http.expectOne(`${ROOT}/runs/A/items/1/assignee`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ assignedUserId: 'u1' });
      request.flush(run([item('1', 'u1'), item('2', 'u2')]));
      expect(sig<TestRun>('run')().items[0].assignedUserId).toBe('u1');
    });
  });

  it('clears the assignment with null', () => {
    open([withPeople()], run([item('1', 'u2')]));
    const select = element().querySelector<HTMLSelectElement>('[data-test=assign-select]')!;
    select.value = '';
    select.dispatchEvent(new Event('change'));
    expect(http.expectOne(`${ROOT}/runs/A/items/1/assignee`).request.body).toEqual({ assignedUserId: null });
  });

  it('does not offer the assignment, but still names the person, to someone who may not manage plans', () => {
    open([withPeople()], run([item('1', 'u2')]), [Permissions.TestRuns.Execute]);
    expect(element().querySelector('[data-test=assign-select]')).toBeNull();
    expect(element().querySelector('tbody')?.textContent).toContain('Ann Lee');
  });

  it('does not offer the assignment on a completed run', () => {
    open([withPeople()], run([item('1', 'u2')], 2));
    expect(element().querySelector('[data-test=assign-select]')).toBeNull();
  });

  it('works without a directory: nothing to pick, the filter keeps what needs no names, and no error', () => {
    open([], run([item('1', 'u2'), item('2', 'u1')]));
    expect(element().querySelector('[data-test=assign-select]')).toBeNull();
    expect(element().querySelector('tbody')?.textContent).toContain('Assigned');
    // Mine, the unassigned and all still work; a person nobody can name is not offered.
    expect([...element().querySelectorAll('#tester-filter option')].length).toBe(3);
    sig<string>('testerFilter').set('me');
    fixture.detectChanges();
    expect(element().querySelectorAll('tbody tr').length).toBe(1);
  });

  it('treats a directory that fails (no permission to list users) like no directory', () => {
    open([{ provide: TCM_USER_DIRECTORY, useValue: { list: () => throwError(() => new Error('403')) } }], run([item('1', null)]));
    expect(TestBed.inject(UserNames).available()).toBe(false);
    expect(element().querySelector('[data-test=assign-select]')).toBeNull();
  });

  it('filters the items to mine, to the unassigned, or to one person, and counts the work of each', () => {
    open([withPeople()], run([item('1', 'u1', 2), item('2', 'u1'), item('3', 'u2'), item('4', null)]));
    const rows = () => element().querySelectorAll('tbody tr').length;
    expect(rows()).toBe(4);

    sig<string>('testerFilter').set('me');
    fixture.detectChanges();
    expect(rows()).toBe(2);

    sig<string>('testerFilter').set('none');
    fixture.detectChanges();
    expect(rows()).toBe(1);

    sig<string>('testerFilter').set('u2');
    fixture.detectChanges();
    expect(rows()).toBe(1);

    const options = [...element().querySelectorAll('#tester-filter option')].map(o => o.textContent?.trim());
    expect(options).toContain('Ann Lee (0/1)');
    expect(options).toContain('Tester One (1/2)');
  });

  it('assigns the test cases added to the run to the chosen person', () => {
    open([withPeople()], run([]));
    sig('addForm').set({ testCaseIds: ['c1', 'c2'], assignedUserId: 'u2' });
    (fixture.componentInstance as unknown as { addItems(): void }).addItems();
    const request = http.expectOne(`${ROOT}/runs/A/items`);
    expect(request.request.body).toEqual({ testCaseIds: ['c1', 'c2'], assignedUserId: 'u2' });
  });

  it('puts the choice back when the server refuses it', () => {
    open([withPeople()], run([item('1', null)]));
    const select = element().querySelector<HTMLSelectElement>('[data-test=assign-select]')!;
    select.value = 'u1';
    select.dispatchEvent(new Event('change'));
    http.expectOne(`${ROOT}/runs/A/items/1/assignee`).flush({}, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    expect(sig<TestRun>('run')().items[0].assignedUserId).toBeNull();
    expect(sig<boolean>('busy')()).toBe(false);
  });
});
