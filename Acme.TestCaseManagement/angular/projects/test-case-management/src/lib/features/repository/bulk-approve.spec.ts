import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { ConfirmService } from '../../core/confirm';
import { TestCaseStatus } from '../../proxy/enums';
import { RepositoryComponent } from './repository';

const ROOT = '/api/test-case-management';
const row = (id: string, status: TestCaseStatus) => ({ id, code: id, title: id, status, currentVersion: 0, priority: 1, severity: 1, tags: [], steps: [] });

describe('approving test cases together from the list', () => {
  let http: HttpTestingController;
  let asked: unknown[];

  const open = (...permissions: string[]) => {
    asked = [];
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), grant(...permissions),
        { provide: ConfirmService, useValue: { askText: (options: unknown) => { asked.push(options); return of('Imported'); }, ask: () => of(true) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RepositoryComponent);
    fixture.detectChanges();
    http.match(() => true).forEach(request => {
      if (request.request.url === `${ROOT}/test-cases` && request.request.method === 'GET') {
        request.flush({ totalCount: 3, items: [row('D-1', TestCaseStatus.Draft), row('R-2', TestCaseStatus.UnderReview), row('A-3', TestCaseStatus.Approved)] });
      } else if (request.request.url.endsWith('/suites') || request.request.url.includes('/suites/')) { request.flush([]); }
      else if (request.request.url.endsWith('/tags')) { request.flush([]); }
      else { request.flush(null); }
    });
    fixture.detectChanges();
    return fixture;
  };
  const root = (f: ReturnType<typeof open>) => f.nativeElement as HTMLElement;
  const rows = (f: ReturnType<typeof open>) => [...root(f).querySelectorAll<HTMLInputElement>('[data-test=select-row]')];

  beforeEach(() => localStorage.clear());

  it('offers the tick boxes only to someone who may approve, and only on rows that can be approved', () => {
    const reader = open(Permissions.TestCases.Default);
    expect(rows(reader).length).toBe(0);
    expect(root(reader).querySelector('[data-test=select-all]')).toBeNull();

    const approver = open('*');
    expect(rows(approver).map(box => box.disabled)).toEqual([false, false, true]);   // draft, in review, approved
  });

  it('selects every row that can be approved, and shows the bar with the count', () => {
    const fixture = open('*');
    expect(root(fixture).querySelector('[data-test=bulk-bar]')).toBeNull();

    root(fixture).querySelector<HTMLInputElement>('[data-test=select-all]')!.click();
    fixture.detectChanges();

    expect(rows(fixture).map(box => box.checked)).toEqual([true, true, false]);
    expect(root(fixture).querySelector('[data-test=approve-selected]')?.textContent).toContain('(2)');
  });

  it('asks for a note, approves them in one call, and lists the ones that could not be approved', () => {
    const fixture = open('*');
    rows(fixture)[0].click();
    rows(fixture)[1].click();
    fixture.detectChanges();

    root(fixture).querySelector<HTMLButtonElement>('[data-test=approve-selected]')!.click();

    expect(asked.length).toBe(1);
    const call = http.expectOne(r => r.method === 'POST' && r.url === `${ROOT}/test-cases/approve`);
    expect(call.request.body).toEqual({ testCaseIds: ['D-1', 'R-2'], changeSummary: 'Imported' });
    call.flush({
      approved: 1, approvedCodes: ['D-1'],
      failed: [{ testCaseId: 'R-2', code: 'R-2', errorCode: 'TestCaseManagement:TestCaseHasNoSteps', message: "Test case 'R-2' needs at least one step." }],
    });
    http.match(r => r.url === `${ROOT}/test-cases` && r.method === 'GET').forEach(r => r.flush({ totalCount: 0, items: [] }));
    http.match(() => true).forEach(r => r.flush([]));
    fixture.detectChanges();

    const failures = root(fixture).querySelector('[data-test=approve-failures]');
    expect(failures?.textContent).toContain("needs at least one step");
    expect(root(fixture).querySelector('[data-test=bulk-bar]')).toBeNull();   // the selection was cleared
  });
});
