import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { Permissions } from '../../core/auth';
import { grant } from '../../core/auth-testing';
import { QualityGateEvaluation } from '../../proxy/dtos';
import { QualityComponent } from './quality';

const ROOT = '/api/test-case-management';
const evaluation = { passed: true, gate: { name: 'G', minPassRate: 95, requiredApprovals: 1, isBuiltIn: false }, criteria: [], metrics: {} } as unknown as QualityGateEvaluation;

describe('QualityComponent', () => {
  let http: HttpTestingController;
  let component: QualityComponent;
  const call = (name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);
  const sig = <T>(name: string) => (component as unknown as Record<string, WritableSignal<T>>)[name];
  const field = (name: string, value: unknown) => ((component as unknown as Record<string, unknown>)[name] = value);
  const setup = (...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    http = TestBed.inject(HttpTestingController);
    component = TestBed.createComponent(QualityComponent).componentInstance;
  };

  beforeEach(() => setup('*'));

  it('signs off the plan and gate that were evaluated, not the ones the selects show now', () => {
    field('planId', 'plan-A');
    field('gateId', 'gate-A');
    call('evaluate');
    http.expectOne(`${ROOT}/quality-gates/evaluate`).flush(evaluation);

    // The selects move on without a new evaluation (as if the old result were still on screen).
    field('planId', 'plan-B');
    call('startSignOff');
    call('submitApproval');

    const start = http.expectOne(`${ROOT}/sign-off`);
    expect(start.request.body.testPlanId).toBe('plan-A');
    expect(start.request.body.qualityGateId).toBe('gate-A');
  });

  it('clears the result when another plan or gate is chosen, and refuses to start a sign-off without one', () => {
    field('planId', 'plan-A');
    call('evaluate');
    http.expectOne(`${ROOT}/quality-gates/evaluate`).flush(evaluation);
    expect(sig('evaluation')()).not.toBeNull();

    call('selectionChanged');
    expect(sig('evaluation')()).toBeNull();
    call('startSignOff');
    expect(sig('approvalForm')()).toBeNull();
  });

  it('drops an evaluation that comes back after the choice changed', () => {
    field('planId', 'plan-A');
    call('evaluate');
    const request = http.expectOne(`${ROOT}/quality-gates/evaluate`);
    call('selectionChanged');
    request.flush(evaluation);
    expect(sig('evaluation')()).toBeNull();
  });

  it('sends a sign-off once when submitted twice, and again after an error', () => {
    field('planId', 'plan-A');
    call('evaluate');
    http.expectOne(`${ROOT}/quality-gates/evaluate`).flush(evaluation);
    call('startSignOff');
    call('submitApproval');
    call('submitApproval');
    const requests = http.match(`${ROOT}/sign-off`);
    expect(requests.length).toBe(1);
    requests[0].flush('x', { status: 500, statusText: 'Server Error' });
    call('submitApproval');
    expect(http.match(`${ROOT}/sign-off`).length).toBe(1);
  });

  it('saves a gate once when submitted twice', () => {
    sig('gateForm').set({ id: null, name: 'G', description: '', minPassRate: 90, requiredApprovals: 1, isDefault: false });
    call('saveGate');
    call('saveGate');
    expect(http.match(`${ROOT}/quality-gates`).filter(r => r.request.method === 'POST').length).toBe(1);
  });

  it('does not ask for plans without TestPlans, nor for sign-offs without SignOff', () => {
    setup(Permissions.QualityGates.Default);
    component.ngOnInit();
    http.expectNone(r => r.url === `${ROOT}/plans`);
    http.expectNone(r => r.url === `${ROOT}/sign-off`);
    http.expectOne(`${ROOT}/quality-gates`);
  });

  it('asks for plans and sign-offs when they are allowed', () => {
    component.ngOnInit();
    http.expectOne(r => r.url === `${ROOT}/plans`);
    http.expectOne(r => r.url === `${ROOT}/sign-off`);
  });
});
