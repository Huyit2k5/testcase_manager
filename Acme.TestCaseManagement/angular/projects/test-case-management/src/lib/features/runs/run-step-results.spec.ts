import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TestCaseVersion, TestExecution, TestRun } from '../../proxy/dtos';
import { RunDetailComponent } from './run-detail';

// The tests chain promises instead of using async functions: a spec file with an async function that also uses grant() makes
// the other specs that use it fail to start in this build (the class that grant() extends is then undefined).

const ROOT = '/api/test-case-management';

const item = { id: 'i1', sequence: 1, testCaseId: 't1', testCaseCode: 'PAY-1', testCaseTitle: 'Pay', versionNumber: 2, assignedUserId: null, currentStatus: 0, attemptCount: 1 };
const run = {
  id: 'A', title: 'Run A', environment: 'Staging', status: 1, items: [item],
  summary: { totalItems: 1, passed: 0, failed: 0, blocked: 0, skipped: 0, untested: 1, completionPercentage: 0, firstTimePassRate: null },
} as unknown as TestRun;
const step = (n: number) => ({ id: `s${n}`, stepOrder: n, action: `Action ${n}`, expectedResult: `Expected ${n}`, testData: null });
const version = { id: 'v2', testCaseId: 't1', versionNumber: 2, title: 'Pay', steps: [step(2), step(1), step(3)] } as unknown as TestCaseVersion;

const PASSED = 1, FAILED = 2, BLOCKED = 3;

describe('the result of single steps when a test is run', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<RunDetailComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;
  const page = () => fixture.componentInstance as unknown as {
    startExecute(i: unknown): void; execute(): void; openHistory(i: unknown): void;
    executeForm(): { status: number };
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*'),
        { provide: ActivatedRoute, useValue: { paramMap: new BehaviorSubject(convertToParamMap({ id: 'A' })) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunDetailComponent);
    fixture.detectChanges();
    http.expectOne(`${ROOT}/runs/A`).flush(run);
    fixture.detectChanges();
  });

  /** A field that was just drawn is joined to its form a moment later, so the page is let settle before and after a change. */
  const settle = () => {
    fixture.detectChanges();
    return fixture.whenStable().then(() => fixture.detectChanges());
  };

  /** Opens the execute form and answers the request for the steps of the version the item runs. */
  const startExecute = () => {
    page().startExecute(item);
    http.expectOne(`${ROOT}/test-cases/t1/versions/2`).flush(version);
    return settle();
  };

  const statusSelects = () => [...element().querySelectorAll<HTMLSelectElement>('[data-test=step-status]')];
  const choose = (select: HTMLSelectElement, status: number) => {
    // The first option is "not run"; the others follow the order of the results (Passed, Failed, Blocked, Skipped).
    select.selectedIndex = status;
    select.dispatchEvent(new Event('change'));
    return settle();
  };

  it('lists the steps of the version being run, in order, each with a result that starts as not run', () =>
    startExecute().then(() => {
      const rows = [...element().querySelectorAll('[data-test=step-rows] tbody tr')];
      expect(rows.map(r => r.getAttribute('data-step'))).toEqual(['1', '2', '3']);
      expect(rows[0].textContent).toContain('Action 1');
      expect(rows[0].textContent).toContain('Expected 1');
      expect(statusSelects().map(s => s.selectedIndex)).toEqual([0, 0, 0]);
    }));

  it('sends the results of the steps that were given one, and none for the steps that were not run', () =>
    startExecute()
      .then(() => choose(statusSelects()[0], PASSED))
      .then(() => choose(statusSelects()[1], PASSED))
      .then(() => {
        page().execute();

        const request = http.expectOne(`${ROOT}/runs/A/items/i1/executions`);
        expect(request.request.body.status).toBe(PASSED);
        expect(request.request.body.stepResults).toEqual([
          { stepId: 's1', status: PASSED, actualResult: null },
          { stepId: 's2', status: PASSED, actualResult: null },
        ]);
      }));

  it('makes the attempt Failed when a step fails, asks what was seen at that step, and sends it', () =>
    startExecute()
      .then(() => {
        expect(page().executeForm().status).toBe(PASSED);
        return choose(statusSelects()[1], FAILED);
      })
      .then(() => {
        expect(page().executeForm().status).toBe(FAILED);
        const seen = element().querySelector<HTMLInputElement>('tr[data-step="2"] input')!;
        seen.value = 'Error 402';
        seen.dispatchEvent(new Event('input'));
        return settle();
      })
      .then(() => {
        page().execute();

        const body = http.expectOne(`${ROOT}/runs/A/items/i1/executions`).request.body;
        expect(body.status).toBe(FAILED);
        expect(body.stepResults).toEqual([{ stepId: 's2', status: FAILED, actualResult: 'Error 402' }]);
      }));

  it('makes the attempt Blocked when a step is blocked and none failed, and warns when Passed is chosen against a failed step', () =>
    startExecute()
      .then(() => choose(statusSelects()[0], BLOCKED))
      .then(() => {
        expect(page().executeForm().status).toBe(BLOCKED);
        return choose(statusSelects()[2], FAILED);
      })
      .then(() => {
        // The tester then sets the result of the whole attempt back to Passed (the first option of that list).
        const overall = element().querySelector<HTMLSelectElement>('#ex-status')!;
        overall.selectedIndex = 0;
        overall.dispatchEvent(new Event('change'));
        return settle();
      })
      .then(() => {
        expect(page().executeForm().status).toBe(PASSED);
        expect(element().querySelector('[data-test=steps-against-pass]')).not.toBeNull();
      }));

  it('passes every step at once with the button', () =>
    startExecute().then(() => {
      element().querySelector<HTMLButtonElement>('[data-test=pass-all-steps]')!.click();
      page().execute();

      expect(http.expectOne(`${ROOT}/runs/A/items/i1/executions`).request.body.stepResults.map((r: { status: number }) => r.status)).toEqual([PASSED, PASSED, PASSED]);
    }));

  it('still records an attempt for a version whose steps could not be loaded, with no step results', () => {
    page().startExecute(item);
    http.expectOne(`${ROOT}/test-cases/t1/versions/2`).flush('x', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(element().querySelector('[data-test=step-rows]')).toBeNull();
    page().execute();
    expect(http.expectOne(`${ROOT}/runs/A/items/i1/executions`).request.body.stepResults).toEqual([]);
  });

  it('shows the result of each step under the attempt in the history, with the text of the step', () => {
    page().openHistory(item);
    const attempt = {
      id: 'e1', testRunItemId: 'i1', attemptNumber: 1, status: FAILED, actualResult: 'Refused', durationSeconds: 5, defectLinks: [], creationTime: '2026-10-13T10:00:00Z', creatorId: null,
      stepResults: [{ stepId: 's1', stepOrder: 1, status: PASSED, actualResult: null }, { stepId: 's2', stepOrder: 2, status: FAILED, actualResult: 'Error 402' }],
    } as unknown as TestExecution;
    http.expectOne(`${ROOT}/runs/A/items/i1/executions`).flush([attempt]);
    http.expectOne(`${ROOT}/test-cases/t1/versions/2`).flush(version);
    fixture.detectChanges();

    const rows = [...element().querySelectorAll('[data-test=attempt-steps] tbody tr')];
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('Action 2');
    expect(rows[1].textContent).toContain('Error 402');
  });

  it('does not ask for the version when no attempt has step results', () => {
    page().openHistory(item);
    http.expectOne(`${ROOT}/runs/A/items/i1/executions`).flush([
      { id: 'e1', testRunItemId: 'i1', attemptNumber: 1, status: PASSED, actualResult: null, durationSeconds: 0, defectLinks: [], stepResults: [], creationTime: '2026-10-13T10:00:00Z', creatorId: null },
    ]);

    http.expectNone(`${ROOT}/test-cases/t1/versions/2`);
  });
});
