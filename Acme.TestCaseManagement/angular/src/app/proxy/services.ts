import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AddDefect, CreateRun, DefectLink, EvaluateInput, ExecuteItem, PagedResult, QualityGate, QualityGateEvaluation,
  Requirement, RtmMatrix, RtmRequest, SavePlan, SaveQualityGate, SaveRequirement, SaveTestCase, SignOffReport, StartSignOff,
  TestCase, TestCaseDefect, TestCaseListRequest, TestCaseVersion, TestExecution, TestPlan, TestRun, TestRunListRequest,
  TestSuite, TestSuiteTree,
} from './dtos';
import { PlanStatus, SeverityLevel, SignOffStatus, TestCaseStatus } from './enums';

const ROOT = '/api/test-case-management';

/** Builds a query string, leaving out null, undefined and empty values. */
function query(values: object | undefined): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values ?? {})) {
    if (value !== null && value !== undefined && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class TestSuiteService {
  private readonly http = inject(HttpClient);

  tree(): Observable<TestSuiteTree[]> { return this.http.get<TestSuiteTree[]>(`${ROOT}/suites/tree`); }
  create(input: { name: string; description?: string | null; parentId?: string | null }): Observable<TestSuite> {
    return this.http.post<TestSuite>(`${ROOT}/suites`, input);
  }
  update(id: string, input: { name: string; description?: string | null }): Observable<TestSuite> {
    return this.http.put<TestSuite>(`${ROOT}/suites/${id}`, input);
  }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${ROOT}/suites/${id}`); }
}

@Injectable({ providedIn: 'root' })
export class TestCaseService {
  private readonly http = inject(HttpClient);

  list(request: TestCaseListRequest): Observable<PagedResult<TestCase>> {
    return this.http.get<PagedResult<TestCase>>(`${ROOT}/test-cases`, { params: query(request) });
  }
  get(id: string): Observable<TestCase> { return this.http.get<TestCase>(`${ROOT}/test-cases/${id}`); }
  create(input: SaveTestCase): Observable<TestCase> { return this.http.post<TestCase>(`${ROOT}/test-cases`, input); }
  update(id: string, input: SaveTestCase): Observable<TestCase> { return this.http.put<TestCase>(`${ROOT}/test-cases/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${ROOT}/test-cases/${id}`); }
  changeStatus(id: string, targetStatus: TestCaseStatus, changeSummary?: string | null): Observable<TestCase> {
    return this.http.post<TestCase>(`${ROOT}/test-cases/${id}/status`, { targetStatus, changeSummary });
  }
  versions(id: string): Observable<TestCaseVersion[]> { return this.http.get<TestCaseVersion[]>(`${ROOT}/test-cases/${id}/versions`); }
  defects(id: string): Observable<TestCaseDefect[]> { return this.http.get<TestCaseDefect[]>(`${ROOT}/test-cases/${id}/defects`); }
}

@Injectable({ providedIn: 'root' })
export class TestPlanService {
  private readonly http = inject(HttpClient);

  list(request: { filter?: string; status?: PlanStatus | null; maxResultCount?: number } = {}): Observable<PagedResult<TestPlan>> {
    return this.http.get<PagedResult<TestPlan>>(`${ROOT}/plans`, { params: query({ maxResultCount: 100, ...request }) });
  }
  create(input: SavePlan): Observable<TestPlan> { return this.http.post<TestPlan>(`${ROOT}/plans`, input); }
  update(id: string, input: SavePlan): Observable<TestPlan> { return this.http.put<TestPlan>(`${ROOT}/plans/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${ROOT}/plans/${id}`); }
  changeStatus(id: string, targetStatus: PlanStatus): Observable<TestPlan> {
    return this.http.post<TestPlan>(`${ROOT}/plans/${id}/status`, { targetStatus });
  }
}

@Injectable({ providedIn: 'root' })
export class TestRunService {
  private readonly http = inject(HttpClient);

  list(request: TestRunListRequest = {}): Observable<PagedResult<TestRun>> {
    return this.http.get<PagedResult<TestRun>>(`${ROOT}/runs`, { params: query({ maxResultCount: 100, ...request }) });
  }
  get(id: string): Observable<TestRun> { return this.http.get<TestRun>(`${ROOT}/runs/${id}`); }
  create(input: CreateRun): Observable<TestRun> { return this.http.post<TestRun>(`${ROOT}/runs`, input); }
  addItems(id: string, testCaseIds: string[]): Observable<TestRun> {
    return this.http.post<TestRun>(`${ROOT}/runs/${id}/items`, { testCaseIds });
  }
  complete(id: string): Observable<TestRun> { return this.http.post<TestRun>(`${ROOT}/runs/${id}/complete`, null); }
  execute(runId: string, itemId: string, input: ExecuteItem): Observable<TestExecution> {
    return this.http.post<TestExecution>(`${ROOT}/runs/${runId}/items/${itemId}/executions`, input);
  }
  executions(runId: string, itemId: string): Observable<TestExecution[]> {
    return this.http.get<TestExecution[]>(`${ROOT}/runs/${runId}/items/${itemId}/executions`);
  }
  defects(executionId: string): Observable<DefectLink[]> { return this.http.get<DefectLink[]>(`${ROOT}/executions/${executionId}/defects`); }
  addDefect(executionId: string, input: AddDefect): Observable<DefectLink> {
    return this.http.post<DefectLink>(`${ROOT}/executions/${executionId}/defects`, input);
  }
  updateDefect(executionId: string, defectId: string, severity: SeverityLevel, isResolved: boolean): Observable<DefectLink> {
    return this.http.put<DefectLink>(`${ROOT}/executions/${executionId}/defects/${defectId}`, { severity, isResolved });
  }
  removeDefect(executionId: string, defectId: string): Observable<void> {
    return this.http.delete<void>(`${ROOT}/executions/${executionId}/defects/${defectId}`);
  }
}

@Injectable({ providedIn: 'root' })
export class RequirementService {
  private readonly http = inject(HttpClient);

  list(request: { filter?: string; maxResultCount?: number } = {}): Observable<PagedResult<Requirement>> {
    return this.http.get<PagedResult<Requirement>>(`${ROOT}/requirements`, { params: query({ maxResultCount: 200, ...request }) });
  }
  create(input: SaveRequirement): Observable<Requirement> { return this.http.post<Requirement>(`${ROOT}/requirements`, input); }
  update(id: string, input: SaveRequirement): Observable<Requirement> { return this.http.put<Requirement>(`${ROOT}/requirements/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${ROOT}/requirements/${id}`); }
  link(id: string, testCaseIds: string[]): Observable<void> {
    return this.http.post<void>(`${ROOT}/requirements/${id}/test-cases`, { testCaseIds });
  }
  unlink(id: string, testCaseId: string): Observable<void> {
    return this.http.delete<void>(`${ROOT}/requirements/${id}/test-cases/${testCaseId}`);
  }
}

@Injectable({ providedIn: 'root' })
export class RtmService {
  private readonly http = inject(HttpClient);

  matrix(request: RtmRequest = {}): Observable<RtmMatrix> {
    return this.http.get<RtmMatrix>(`${ROOT}/rtm`, { params: query({ maxResultCount: 500, ...request }) });
  }
}

@Injectable({ providedIn: 'root' })
export class QualityGateService {
  private readonly http = inject(HttpClient);

  list(): Observable<QualityGate[]> { return this.http.get<QualityGate[]>(`${ROOT}/quality-gates`); }
  create(input: SaveQualityGate): Observable<QualityGate> { return this.http.post<QualityGate>(`${ROOT}/quality-gates`, input); }
  update(id: string, input: SaveQualityGate): Observable<QualityGate> { return this.http.put<QualityGate>(`${ROOT}/quality-gates/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${ROOT}/quality-gates/${id}`); }
  evaluate(input: EvaluateInput): Observable<QualityGateEvaluation> {
    return this.http.post<QualityGateEvaluation>(`${ROOT}/quality-gates/evaluate`, input);
  }
}

@Injectable({ providedIn: 'root' })
export class SignOffService {
  private readonly http = inject(HttpClient);

  list(request: { testPlanId?: string | null; status?: SignOffStatus | null } = {}): Observable<PagedResult<SignOffReport>> {
    return this.http.get<PagedResult<SignOffReport>>(`${ROOT}/sign-off`, { params: query({ maxResultCount: 100, ...request }) });
  }
  start(input: StartSignOff): Observable<SignOffReport> { return this.http.post<SignOffReport>(`${ROOT}/sign-off`, input); }
  approve(id: string, approverRole: string | null, comment: string | null): Observable<SignOffReport> {
    return this.http.post<SignOffReport>(`${ROOT}/sign-off/${id}/approvals`, { approverRole, comment });
  }
}
