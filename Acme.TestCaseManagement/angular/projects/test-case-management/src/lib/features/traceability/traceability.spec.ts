import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { RtmMatrix } from '../../proxy/dtos';
import { TraceabilityComponent } from './traceability';

const ROOT = '/api/test-case-management';
const matrix = (marker: number) => ({ rows: [], marker }) as unknown as RtmMatrix;

describe('TraceabilityComponent', () => {
  let http: HttpTestingController;
  let component: TraceabilityComponent;
  const call = (name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);
  const sig = <T>(name: string) => (component as unknown as Record<string, WritableSignal<T>>)[name];
  const setup = (...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(TraceabilityComponent).componentInstance;
  };

  beforeEach(() => setup('*'));

  it('shows the matrix of the latest filter even when an older answer arrives after it', () => {
    const rtm = () => http.match(r => r.url === `${ROOT}/rtm`);
    call('load');
    const [old] = rtm();
    call('load');
    const [latest] = rtm();
    latest.flush(matrix(2));
    old.flush(matrix(1));
    expect((sig<{ marker: number }>('matrix')()).marker).toBe(2);
  });

  it('does not ask for plans without TestPlans', () => {
    setup(Permissions.Requirements.Default);
    component.ngOnInit();
    http.expectNone(r => r.url === `${ROOT}/plans`);
    http.expectOne(r => r.url === `${ROOT}/rtm`);
  });

  it('saves a requirement once when submitted twice, and links once when clicked twice', () => {
    sig('form').set({ id: null, code: 'R1', title: 'T', description: '', acceptanceCriteria: '', priority: 1, milestoneId: '' });
    call('saveRequirement');
    call('saveRequirement');
    const saves = http.match(`${ROOT}/requirements`);
    expect(saves.length).toBe(1);
    saves[0].flush({});
    http.match(r => r.url === `${ROOT}/rtm`);

    sig('linkForm').set({ row: { requirementId: 'r1' }, testCaseIds: ['t1'] });
    call('link');
    call('link');
    expect(http.match(`${ROOT}/requirements/r1/test-cases`).length).toBe(1);
  });
});
