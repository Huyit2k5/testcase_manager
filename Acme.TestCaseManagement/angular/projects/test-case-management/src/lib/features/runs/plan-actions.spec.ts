import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { RunsComponent } from './runs';

const ROOT = '/api/test-case-management';
const paged = (items: unknown[]) => ({ totalCount: items.length, items });

describe('The actions of a plan in the list', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<RunsComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RunsComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/runs`).flush(paged([]));
    http.expectOne(r => r.url === `${ROOT}/plans`).flush(paged([
      { id: 'p1', name: 'Draft plan', status: 0, description: null, milestoneId: null, startDate: null, endDate: null },
      { id: 'p2', name: 'Archived plan', status: 3, description: null, milestoneId: null, startDate: null, endDate: null },
    ]));
    fixture.detectChanges();
  });

  const rowOf = (name: string) => [...element().querySelectorAll('tbody tr')].find(r => r.textContent?.includes(name))!;
  const menuButton = (row: Element) => row.querySelector<HTMLButtonElement>('app-row-menu button')!;

  it('names the edit and delete icons for people who do not see them', () => {
    const draft = rowOf('Draft plan');
    expect(draft.querySelector('button[tcmIcon="edit"]')?.getAttribute('aria-label')).toBeTruthy();
    expect(draft.querySelector('button[tcmIcon="trash"]')?.getAttribute('title')).toBeTruthy();
    expect(menuButton(draft).getAttribute('aria-label')).toBeTruthy();
  });

  it('has no menu for an archived plan, which has no next state', () => {
    expect(rowOf('Archived plan').querySelector('app-row-menu')).toBeNull();
  });

  it('opens the next states in a menu, and moves the plan to the one that is chosen', () => {
    menuButton(rowOf('Draft plan')).click();
    fixture.detectChanges();
    const items = [...element().querySelectorAll<HTMLButtonElement>('[role=menuitem]')];
    expect(items.map(i => i.textContent?.trim())).toEqual(['Active', 'Archived']);

    items[0].click();
    fixture.detectChanges();

    const request = http.expectOne(`${ROOT}/plans/p1/status`);
    expect(request.request.body).toEqual({ targetStatus: 1 });
    expect(element().querySelector('[role=menu]')).toBeNull();
    request.flush({});
  });

  it('closes the menu on Escape and on a click elsewhere, without moving anything', () => {
    const button = menuButton(rowOf('Draft plan'));
    button.click();
    fixture.detectChanges();
    expect(element().querySelector('[role=menu]')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(element().querySelector('[role=menu]')).toBeNull();

    button.click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();
    expect(element().querySelector('[role=menu]')).toBeNull();
    http.expectNone(`${ROOT}/plans/p1/status`);
  });
});
