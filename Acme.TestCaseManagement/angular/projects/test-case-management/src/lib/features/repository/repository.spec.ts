import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TestCase } from '../../proxy/dtos';
import { RepositoryComponent } from './repository';
import { TestCaseFormComponent } from './test-case-form';

const ROOT = '/api/test-case-management';
const LIST = `${ROOT}/test-cases`;

const tc = (id: string) => ({ id, code: id, title: id, tags: [], steps: [] }) as unknown as TestCase;
const page = (ids: string[], totalCount = ids.length) => ({ items: ids.map(tc), totalCount });

describe('RepositoryComponent', () => {
  let http: HttpTestingController;
  let component: RepositoryComponent;
  const call = (name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);
  const read = <T>(name: string): T => (component as unknown as Record<string, () => T>)[name]();
  const sig = <T>(name: string) => (component as unknown as Record<string, WritableSignal<T>>)[name];
  const field = (name: string, value: unknown) => ((component as unknown as Record<string, unknown>)[name] = value);
  const lists = () => http.match(r => r.url === LIST && r.method === 'GET');

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(RepositoryComponent).componentInstance;
  });

  it('shows the answer to the latest filter even when an older answer arrives after it', () => {
    field('search', 'old');
    call('loadCases');
    field('search', 'new');
    call('loadCases');
    const [old, latest] = lists();

    latest.flush(page(['new']));
    old.flush(page(['old', 'old2']));

    expect(read<TestCase[]>('cases').map(c => c.id)).toEqual(['new']);
    expect(read<boolean>('loading')).toBe(false);
  });

  it('does not clear the loading flag when an old request fails after a newer one started', () => {
    call('loadCases');
    call('loadCases');
    const [old] = lists();
    old.flush('x', { status: 500, statusText: 'Server Error' });
    expect(read<boolean>('loading')).toBe(true);
  });

  it('goes to the last page when the page it was on no longer exists', () => {
    sig<number>('page').set(2);
    call('loadCases');
    lists()[0].flush({ items: [], totalCount: 21 });   // 21 cases: pages 0 and 1 (20 per page)

    const [again] = lists();
    expect(again.request.params.get('skipCount')).toBe('20');
    again.flush(page(['last'], 21));
    expect(read<number>('page')).toBe(1);
    expect(read<TestCase[]>('cases').map(c => c.id)).toEqual(['last']);
  });

  it('goes to the first page when nothing is left at all', () => {
    sig<number>('page').set(1);
    call('loadCases');
    lists()[0].flush({ items: [], totalCount: 0 });
    lists()[0].flush(page([], 0));
    expect(read<number>('page')).toBe(0);
  });

  it('loads the list again without a tag that no test case has any more', () => {
    field('tag', 'gone');
    call('loadCases');
    expect(lists()[0].request.params.getAll('tags')).toEqual(['gone']);
    lists();
    call('loadTagsList');
    http.expectOne(`${LIST}/tags`).flush([{ name: 'other', count: 1 }]);

    const [reload] = lists();
    expect(reload.request.params.has('tags')).toBe(false);
    expect((component as unknown as { tag: string }).tag).toBe('');
  });

  it('sends one suite when the dialog is submitted twice, and lets the user retry after an error', () => {
    sig<unknown>('suiteDialog').set({ name: 'Smoke', description: '' });
    call('createSuite');
    call('createSuite');
    const requests = http.match(`${ROOT}/suites`);
    expect(requests.length).toBe(1);

    requests[0].flush('x', { status: 500, statusText: 'Server Error' });
    call('createSuite');
    expect(http.match(`${ROOT}/suites`).length).toBe(1);
  });
});

describe('TestCaseFormComponent', () => {
  it('sends one request when Enter submits the form twice before the answer', () => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'S' }]);
    fixture.componentInstance.ngOnInit();
    http.expectOne(`${LIST}/tags`).flush([]);
    const form = fixture.componentInstance as unknown as { model: { steps: { action: string; expectedResult: string }[] }; save(): void };
    form.model.steps[0] = { action: 'a', expectedResult: 'b' } as never;

    form.save();
    form.save();
    const requests = http.match(r => r.url === LIST && r.method === 'POST');
    expect(requests.length).toBe(1);

    requests[0].flush('x', { status: 500, statusText: 'Server Error' });
    form.save();
    expect(http.match(r => r.url === LIST && r.method === 'POST').length).toBe(1);
  });
});
