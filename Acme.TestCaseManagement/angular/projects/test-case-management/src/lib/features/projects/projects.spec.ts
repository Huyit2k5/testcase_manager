import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { grant } from '../../core/auth-testing';
import { Permissions } from '../../core/auth';
import { ProjectContext } from '../../core/project-context';
import { Project } from '../../proxy/dtos';
import { RequirementService, TestCaseService, TestPlanService, TestSuiteService } from '../../proxy/services';
import { ProjectBarComponent } from './project-bar';
import { ProjectsComponent } from './projects';

const ROOT = '/api/test-case-management';

const project = (patch: Partial<Project> = {}): Project => ({
  id: 'p1', key: 'EINV', name: 'EasyInvoice', description: null, isArchived: false,
  suiteCount: 2, testCaseCount: 10, planCount: 1, requirementCount: 3, runCount: 1, ...patch,
});

describe('the project the user works in', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => { vi.unstubAllGlobals(); localStorage.clear(); });

  it('chooses the first project when none was chosen before, and keeps the choice for the next visit', () => {
    const context = TestBed.inject(ProjectContext);
    context.setProjects([project(), project({ id: 'p2', key: 'HRM', name: 'HR' })]);

    expect(context.currentId()).toBe('p1');
    expect(localStorage.getItem('tcm.project')).toBe('p1');

    context.select('p2');
    expect(context.current()?.key).toBe('HRM');
    expect(localStorage.getItem('tcm.project')).toBe('p2');
  });

  it('keeps the project chosen before while it is there, and falls back to the first one when it is gone', () => {
    localStorage.setItem('tcm.project', 'p2');
    const context = TestBed.inject(ProjectContext);
    const version = context.version();

    context.setProjects([project(), project({ id: 'p2' })]);
    expect(context.currentId()).toBe('p2');
    expect(context.version()).toBe(version);   // nothing changed, so the page is not built again

    context.setProjects([project()]);          // p2 was archived
    expect(context.currentId()).toBe('p1');
    expect(context.version()).toBe(version + 1);
  });

  it('has no project when the user may not read them, and then nothing is sent', () => {
    const context = TestBed.inject(ProjectContext);
    context.setProjects([]);
    expect(context.currentId()).toBeNull();

    TestBed.inject(TestSuiteService).tree().subscribe();
    const tree = http.expectOne(r => r.url === `${ROOT}/suites/tree`);
    expect(tree.request.params.has('projectId')).toBe(false);
    tree.flush([]);

    TestBed.inject(TestPlanService).create({ name: 'Plan' }).subscribe();
    expect(http.expectOne(`${ROOT}/plans`).request.body).toEqual({ name: 'Plan' });
  });

  it('asks for the lists of the project in use and makes new things in it', () => {
    TestBed.inject(ProjectContext).setProjects([project()]);

    TestBed.inject(TestSuiteService).tree().subscribe();
    expect(http.expectOne(r => r.url === `${ROOT}/suites/tree`).request.params.get('projectId')).toBe('p1');

    TestBed.inject(TestCaseService).list({ maxResultCount: 5 }).subscribe();
    expect(http.expectOne(r => r.url === `${ROOT}/test-cases`).request.params.get('projectId')).toBe('p1');

    TestBed.inject(TestPlanService).list().subscribe();
    expect(http.expectOne(r => r.url === `${ROOT}/plans`).request.params.get('projectId')).toBe('p1');

    TestBed.inject(RequirementService).list().subscribe();
    expect(http.expectOne(r => r.url === `${ROOT}/requirements`).request.params.get('projectId')).toBe('p1');

    TestBed.inject(TestPlanService).create({ name: 'Plan' }).subscribe();
    expect(http.expectOne(`${ROOT}/plans`).request.body).toEqual({ projectId: 'p1', name: 'Plan' });

    TestBed.inject(RequirementService).create({ code: 'R-1', title: 'One', priority: 1 }).subscribe();
    expect(http.expectOne(`${ROOT}/requirements`).request.body.projectId).toBe('p1');
  });

  it('puts a root suite in the project in use, and leaves a suite below another to the project of its parent', () => {
    TestBed.inject(ProjectContext).setProjects([project()]);
    const suites = TestBed.inject(TestSuiteService);

    suites.create({ name: 'Root' }).subscribe();
    expect(http.expectOne(`${ROOT}/suites`).request.body).toEqual({ name: 'Root', projectId: 'p1' });

    suites.create({ name: 'Child', parentId: 's1' }).subscribe();
    expect(http.expectOne(`${ROOT}/suites`).request.body).toEqual({ name: 'Child', parentId: 's1' });
  });
});

describe('the project bar', () => {
  let http: HttpTestingController;

  const open = (...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProjectBarComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/projects`).flush([project(), project({ id: 'p2', key: 'HRM', name: 'HR' })]);
    fixture.detectChanges();
    return fixture;
  };

  it('loads the projects and lists them all, with the one in use chosen', () => {
    const fixture = open();
    const element = fixture.nativeElement as HTMLElement;

    expect([...element.querySelectorAll('option')].map(o => o.textContent?.trim())).toEqual(['EINV - EasyInvoice', 'HRM - HR']);
    expect(TestBed.inject(ProjectContext).currentId()).toBe('p1');
    expect(TestBed.inject(ProjectContext).ready()).toBe(true);
  });

  it('shows the default project by its name only', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant()] });
    const fixture = TestBed.createComponent(ProjectBarComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne(r => r.url === `${ROOT}/projects`).flush([project({ key: 'DEFAULT', name: 'EasyInvoice' }), project({ id: 'p2', key: 'HRM', name: 'HR' })]);
    fixture.detectChanges();

    expect([...(fixture.nativeElement as HTMLElement).querySelectorAll('option')].map(o => o.textContent?.trim())).toEqual(['EasyInvoice', 'HRM - HR']);
  });

  it('changes the project in use when another one is chosen', () => {
    const fixture = open();
    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    const context = TestBed.inject(ProjectContext);
    const version = context.version();

    select.value = select.options[1].value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(context.currentId()).toBe('p2');
    expect(context.version()).toBe(version + 1);
  });

  it('offers the page that manages projects only to those who may manage them', () => {
    expect((open().nativeElement as HTMLElement).querySelector('[data-test=manage-projects]')).toBeNull();
    TestBed.resetTestingModule();
    expect((open(Permissions.Projects.Manage).nativeElement as HTMLElement).querySelector('[data-test=manage-projects]')).not.toBeNull();
  });
});

describe('the page that manages projects', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<ProjectsComponent>>;
  const element = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProjectsComponent);
    fixture.detectChanges();
    http.expectOne(r => r.url === `${ROOT}/projects`).flush([
      project(),
      project({ id: 'p2', key: 'HRM', name: 'HR', suiteCount: 0, testCaseCount: 0, planCount: 0, requirementCount: 0, runCount: 0 }),
    ]);
    fixture.detectChanges();
  });

  afterEach(() => vi.unstubAllGlobals());

  const rowOf = (key: string) => [...element().querySelectorAll('[data-test=project-row]')].find(r => r.textContent?.includes(key))!;

  it('lists the projects with what is in them, and offers deleting only an empty one', () => {
    expect(rowOf('EINV').textContent).toContain('10 test case(s)');
    expect(rowOf('EINV').querySelector('button[tcmIcon="trash"]')).toBeNull();
    expect(rowOf('HRM').querySelector('button[tcmIcon="trash"]')).not.toBeNull();
  });

  it('makes a project with its key in capitals, and chooses it', () => {
    (element().querySelector('[data-test=new-project]') as HTMLButtonElement).click();
    fixture.detectChanges();
    // The fields of a template-driven form are registered after a tick.
    return fixture.whenStable().then(() => {
      const set = (selector: string, value: string) => {
        const input = element().querySelector<HTMLInputElement>(selector)!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
      };
      set('#project-key', 'web');
      set('#project-name', 'Web shop');
      fixture.detectChanges();
      element().querySelector<HTMLFormElement>('#project-form')!.dispatchEvent(new Event('submit'));

      const request = http.expectOne(`${ROOT}/projects`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Web shop', description: null, key: 'WEB' });
      request.flush(project({ id: 'p3', key: 'WEB', name: 'Web shop' }));

      // Both lists are asked for again; the new project becomes the one in use.
      for (const list of http.match(r => r.url === `${ROOT}/projects`)) {
        list.flush([project(), project({ id: 'p3', key: 'WEB', name: 'Web shop' })]);
      }
      expect(TestBed.inject(ProjectContext).currentId()).toBe('p3');
    });
  });

  it('archives a project only after a question, and says so to the bar above', () => {
    vi.stubGlobal('confirm', () => false);
    (rowOf('EINV').querySelectorAll('button.btn')[0] as HTMLButtonElement).click();
    http.expectNone(`${ROOT}/projects/p1/archive`);

    vi.stubGlobal('confirm', () => true);
    (rowOf('EINV').querySelectorAll('button.btn')[0] as HTMLButtonElement).click();
    const request = http.expectOne(`${ROOT}/projects/p1/archive`);
    expect(request.request.method).toBe('POST');
    request.flush(project({ isArchived: true }));
    for (const list of http.match(r => r.url === `${ROOT}/projects`)) { list.flush([project({ id: 'p2', key: 'HRM' })]); }

    expect(TestBed.inject(ProjectContext).projects().map(p => p.key)).toEqual(['HRM']);
  });
});
