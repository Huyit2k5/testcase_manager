import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { TCM_USER_DIRECTORY } from '../../core/host';
import { Project } from '../../proxy/dtos';
import { ProjectsComponent } from './projects';

const ROOT = '/api/test-case-management';

const project = (patch: Partial<Project> = {}): Project => ({
  id: 'p1', key: 'EINV', name: 'EasyInvoice', description: null, isArchived: false,
  suiteCount: 2, testCaseCount: 10, planCount: 1, requirementCount: 3, runCount: 1, isRestricted: false, memberCount: 0, ...patch,
});
const users = [
  { id: 'u1', userName: 'lan', displayName: 'Lan Nguyen' },
  { id: 'u2', userName: 'minh', displayName: 'Minh Tran' },
  { id: 'u3', userName: 'thuy', displayName: 'Thuy Pham' },
];

describe('who may see a project', () => {
  let http: HttpTestingController;

  const open = (directory: unknown[], ...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(...permissions),
        { provide: TCM_USER_DIRECTORY, useValue: { list: () => of(directory) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProjectsComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/projects`).flush([project({ isRestricted: true, memberCount: 2 }), project({ id: 'p2', key: 'HRM', name: 'HR' })]);
    fixture.detectChanges();
    return fixture;
  };
  const root = (f: ReturnType<typeof open>) => f.nativeElement as HTMLElement;

  beforeEach(() => localStorage.clear());

  it('marks a restricted project with its members, and offers the access dialog only to someone who manages projects', () => {
    const manager = open(users, '*');
    expect(root(manager).querySelectorAll('[data-test=restricted-badge]').length).toBe(1);
    expect(root(manager).querySelector('[data-test=restricted-badge]')?.textContent).toContain('2 members');
    expect(root(manager).querySelectorAll('[data-test=project-access]').length).toBe(2);

    const reader = open(users, Permissions.Projects.Default);
    expect(root(reader).querySelectorAll('[data-test=project-access]').length).toBe(0);
  });

  it('shows the members by name, lets a person be added from the directory, and saves the whole list', () => {
    const fixture = open(users, '*');
    root(fixture).querySelector<HTMLButtonElement>('[data-test=project-access]')!.click();
    http.expectOne(`${ROOT}/projects/p1/access`).flush({ isRestricted: true, userIds: ['u1'] });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-test=members]')?.textContent).toContain('Lan Nguyen');
    const pick = root(fixture).querySelector<HTMLSelectElement>('[data-test=member-pick]')!;
    expect([...pick.options].map(o => o.textContent?.trim())).not.toContain('Lan Nguyen');   // already a member
    pick.value = 'u3';
    pick.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    root(fixture).querySelector<HTMLButtonElement>('[data-test=member-add]')!.click();
    fixture.detectChanges();
    expect(root(fixture).querySelector('[data-test=members]')?.textContent).toContain('Thuy Pham');

    root(fixture).querySelector<HTMLButtonElement>('[data-test=access-save]')!.click();
    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${ROOT}/projects/p1/access`);
    expect(put.request.body).toEqual({ isRestricted: true, userIds: ['u1', 'u3'] });
    put.flush({ isRestricted: true, userIds: ['u1', 'u3'] });
    http.match(r => r.url === `${ROOT}/projects`).forEach(r => r.flush([]));
    fixture.detectChanges();
    expect(root(fixture).querySelector('[data-test=members]')).toBeNull();   // the dialog closed
  });

  it('can open a project to everyone again, keeping the list', () => {
    const fixture = open(users, '*');
    root(fixture).querySelector<HTMLButtonElement>('[data-test=project-access]')!.click();
    http.expectOne(`${ROOT}/projects/p1/access`).flush({ isRestricted: true, userIds: ['u2'] });
    fixture.detectChanges();

    root(fixture).querySelector<HTMLInputElement>('[data-test=restrict]')!.click();
    root(fixture).querySelector<HTMLButtonElement>('[data-test=access-save]')!.click();

    const put = http.expectOne(r => r.method === 'PUT' && r.url === `${ROOT}/projects/p1/access`);
    expect(put.request.body).toEqual({ isRestricted: false, userIds: ['u2'] });
  });

  it('says so when the application gives no list of users, but still lets the project be restricted', () => {
    const fixture = open([], '*');
    root(fixture).querySelector<HTMLButtonElement>('[data-test=project-access]')!.click();
    http.expectOne(`${ROOT}/projects/p1/access`).flush({ isRestricted: false, userIds: [] });
    fixture.detectChanges();

    expect(root(fixture).querySelector('[data-test=member-pick]')).toBeNull();
    expect(root(fixture).textContent).toContain('gives no list of users');
    expect(root(fixture).querySelector('[data-test=restrict]')).not.toBeNull();
  });
});
