import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { ConfirmService } from '../../core/confirm';
import { Permissions } from '../../core/auth';
import { RunsComponent } from './runs';

const ROOT = '/api/test-case-management';

const run = (id: string, executedItems: number) => ({
  id, testPlanId: null, title: `Run ${id}`, environment: 'Staging', assignedToUserId: null, status: 0, items: [],
  summary: { totalItems: 3, executedItems, passed: executedItems, failed: 0, blocked: 0, skipped: 0, untested: 3 - executedItems, completionPercentage: 0, firstTimePassRate: null },
});

describe('deleting a run that has no result yet', () => {
  let http: HttpTestingController;
  let asked: string[];

  const open = (...permissions: string[]) => {
    asked = [];
    localStorage.clear();
    localStorage.setItem('tcm.runsView', 'list');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(...permissions),
        { provide: ConfirmService, useValue: { ask: (options: { message: string }) => { asked.push(options.message); return of(true); } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RunsComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/runs`).flush({ totalCount: 2, items: [run('empty', 0), run('worked', 2)] });
    http.expectOne(r => r.url === `${ROOT}/plans`).flush({ totalCount: 0, items: [] });
    fixture.detectChanges();
    return fixture;
  };

  const buttons = (fixture: ReturnType<typeof open>) => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[data-test=delete-run]')];

  beforeEach(() => localStorage.clear());

  it('is offered only for a run with no result, and only to someone who manages plans', () => {
    const manager = open('*');
    expect(buttons(manager).length).toBe(1);
    expect((buttons(manager)[0].closest('tr') as HTMLElement).textContent).toContain('Run empty');

    const reader = open(Permissions.TestRuns.Default, Permissions.TestPlans.Default);
    expect(buttons(reader).length).toBe(0);
  });

  it('asks first, then deletes the run on the server and reads the list again', () => {
    const fixture = open('*');

    buttons(fixture)[0].click();

    expect(asked[0]).toContain('Run empty');
    http.expectOne(r => r.method === 'DELETE' && r.url === `${ROOT}/runs/empty`).flush(null);
    http.expectOne(r => r.url === `${ROOT}/runs`).flush({ totalCount: 1, items: [run('worked', 2)] });
    http.expectOne(r => r.url === `${ROOT}/plans`).flush({ totalCount: 0, items: [] });
    fixture.detectChanges();
    expect(buttons(fixture).length).toBe(0);
    http.verify();
  });
});
