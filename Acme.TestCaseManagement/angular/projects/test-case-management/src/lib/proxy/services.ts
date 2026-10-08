import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiRoot } from '../core/host';
import { Observable, map } from 'rxjs';
import {
  StepSuggestionResult, StepSuggestionStatus, SuggestStepsInput, AddDefect, ApiKey, Attachment, SaveSharedStepGroup, SharedStepGroup, SharedStepGroupSummary, SharedStepUsage, TagSummary, UpdateSharedStepUsersResult, ApiKeyCreated, ApplyFlakyFlagsResult, CreateRun, Dashboard, FlakyTestList, DefectLink, EvaluateInput, ExecuteItem, PagedResult, QualityGate, QualityGateEvaluation,
  Requirement, RtmMatrix, RtmRequest, SavePlan, SaveQualityGate, SaveRequirement, SaveTestCase, SignOffReport, StartSignOff,
  TestCase, TestCaseDefect, TestCaseListRequest, TestCaseVersion, TestExecution, TestPlan, TestRun, TestRunListRequest,
  TestSuite, TestSuiteTree,
} from './dtos';
import { PlanStatus, SeverityLevel, SignOffStatus, TestCaseStatus } from './enums';

/** Builds a query string, leaving out null, undefined and empty values. */
function query(values: object | undefined): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values ?? {})) {
    if (Array.isArray(value)) {
      // A list is sent as a repeated parameter (Tags=a&Tags=b), which is what the API binds.
      for (const item of value) { params = params.append(key, String(item)); }
    } else if (value !== null && value !== undefined && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class TestSuiteService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  tree(): Observable<TestSuiteTree[]> { return this.http.get<TestSuiteTree[]>(`${this.root}/suites/tree`); }
  create(input: { name: string; description?: string | null; parentId?: string | null }): Observable<TestSuite> {
    return this.http.post<TestSuite>(`${this.root}/suites`, input);
  }
  update(id: string, input: { name: string; description?: string | null }): Observable<TestSuite> {
    return this.http.put<TestSuite>(`${this.root}/suites/${id}`, input);
  }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/suites/${id}`); }
}

@Injectable({ providedIn: 'root' })
export class TestCaseService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: TestCaseListRequest): Observable<PagedResult<TestCase>> {
    return this.http.get<PagedResult<TestCase>>(`${this.root}/test-cases`, { params: query(request) });
  }
  get(id: string): Observable<TestCase> { return this.http.get<TestCase>(`${this.root}/test-cases/${id}`); }
  create(input: SaveTestCase): Observable<TestCase> { return this.http.post<TestCase>(`${this.root}/test-cases`, input); }
  update(id: string, input: SaveTestCase): Observable<TestCase> { return this.http.put<TestCase>(`${this.root}/test-cases/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/test-cases/${id}`); }
  changeStatus(id: string, targetStatus: TestCaseStatus, changeSummary?: string | null): Observable<TestCase> {
    return this.http.post<TestCase>(`${this.root}/test-cases/${id}/status`, { targetStatus, changeSummary });
  }
  insertSharedSteps(id: string, sharedStepGroupId: string, position?: number | null, changeSummary?: string | null): Observable<TestCase> {
    return this.http.post<TestCase>(`${this.root}/test-cases/${id}/shared-steps`, { sharedStepGroupId, position: position ?? null, changeSummary: changeSummary ?? null });
  }
  refreshSharedSteps(id: string, groupId: string): Observable<TestCase> {
    return this.http.post<TestCase>(`${this.root}/test-cases/${id}/shared-steps/${groupId}/refresh`, {});
  }
  detachSharedSteps(id: string, groupId: string): Observable<TestCase> {
    return this.http.delete<TestCase>(`${this.root}/test-cases/${id}/shared-steps/${groupId}`);
  }
  setTags(id: string, tags: string[]): Observable<TestCase> { return this.http.put<TestCase>(`${this.root}/test-cases/${id}/tags`, { tags }); }
  tags(): Observable<TagSummary[]> { return this.http.get<TagSummary[]>(`${this.root}/test-cases/tags`); }
  versions(id: string): Observable<TestCaseVersion[]> { return this.http.get<TestCaseVersion[]>(`${this.root}/test-cases/${id}/versions`); }
  defects(id: string): Observable<TestCaseDefect[]> { return this.http.get<TestCaseDefect[]>(`${this.root}/test-cases/${id}/defects`); }
}

@Injectable({ providedIn: 'root' })
export class TestPlanService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: { filter?: string; status?: PlanStatus | null; maxResultCount?: number } = {}): Observable<PagedResult<TestPlan>> {
    return this.http.get<PagedResult<TestPlan>>(`${this.root}/plans`, { params: query({ maxResultCount: 100, ...request }) });
  }
  create(input: SavePlan): Observable<TestPlan> { return this.http.post<TestPlan>(`${this.root}/plans`, input); }
  update(id: string, input: SavePlan): Observable<TestPlan> { return this.http.put<TestPlan>(`${this.root}/plans/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/plans/${id}`); }
  changeStatus(id: string, targetStatus: PlanStatus): Observable<TestPlan> {
    return this.http.post<TestPlan>(`${this.root}/plans/${id}/status`, { targetStatus });
  }
}

@Injectable({ providedIn: 'root' })
export class TestRunService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: TestRunListRequest = {}): Observable<PagedResult<TestRun>> {
    return this.http.get<PagedResult<TestRun>>(`${this.root}/runs`, { params: query({ maxResultCount: 100, ...request }) });
  }
  get(id: string): Observable<TestRun> { return this.http.get<TestRun>(`${this.root}/runs/${id}`); }
  create(input: CreateRun): Observable<TestRun> { return this.http.post<TestRun>(`${this.root}/runs`, input); }
  addItems(id: string, testCaseIds: string[], assignedUserId: string | null = null): Observable<TestRun> {
    return this.http.post<TestRun>(`${this.root}/runs/${id}/items`, { testCaseIds, assignedUserId });
  }
  /** Null clears the assignment. */
  assignTester(runId: string, itemId: string, assignedUserId: string | null): Observable<TestRun> {
    return this.http.put<TestRun>(`${this.root}/runs/${runId}/items/${itemId}/assignee`, { assignedUserId });
  }
  complete(id: string): Observable<TestRun> { return this.http.post<TestRun>(`${this.root}/runs/${id}/complete`, null); }
  execute(runId: string, itemId: string, input: ExecuteItem): Observable<TestExecution> {
    return this.http.post<TestExecution>(`${this.root}/runs/${runId}/items/${itemId}/executions`, input);
  }
  executions(runId: string, itemId: string): Observable<TestExecution[]> {
    return this.http.get<TestExecution[]>(`${this.root}/runs/${runId}/items/${itemId}/executions`);
  }
  defects(executionId: string): Observable<DefectLink[]> { return this.http.get<DefectLink[]>(`${this.root}/executions/${executionId}/defects`); }
  addDefect(executionId: string, input: AddDefect): Observable<DefectLink> {
    return this.http.post<DefectLink>(`${this.root}/executions/${executionId}/defects`, input);
  }
  updateDefect(executionId: string, defectId: string, severity: SeverityLevel, isResolved: boolean): Observable<DefectLink> {
    return this.http.put<DefectLink>(`${this.root}/executions/${executionId}/defects/${defectId}`, { severity, isResolved });
  }
  removeDefect(executionId: string, defectId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/executions/${executionId}/defects/${defectId}`);
  }
}

@Injectable({ providedIn: 'root' })
export class RequirementService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: { filter?: string; maxResultCount?: number } = {}): Observable<PagedResult<Requirement>> {
    return this.http.get<PagedResult<Requirement>>(`${this.root}/requirements`, { params: query({ maxResultCount: 200, ...request }) });
  }
  create(input: SaveRequirement): Observable<Requirement> { return this.http.post<Requirement>(`${this.root}/requirements`, input); }
  update(id: string, input: SaveRequirement): Observable<Requirement> { return this.http.put<Requirement>(`${this.root}/requirements/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/requirements/${id}`); }
  link(id: string, testCaseIds: string[]): Observable<void> {
    return this.http.post<void>(`${this.root}/requirements/${id}/test-cases`, { testCaseIds });
  }
  unlink(id: string, testCaseId: string): Observable<void> {
    return this.http.delete<void>(`${this.root}/requirements/${id}/test-cases/${testCaseId}`);
  }
}

@Injectable({ providedIn: 'root' })
export class RtmService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  matrix(request: RtmRequest = {}): Observable<RtmMatrix> {
    return this.http.get<RtmMatrix>(`${this.root}/rtm`, { params: query({ maxResultCount: 500, ...request }) });
  }
}

@Injectable({ providedIn: 'root' })
export class QualityGateService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(): Observable<QualityGate[]> { return this.http.get<QualityGate[]>(`${this.root}/quality-gates`); }
  create(input: SaveQualityGate): Observable<QualityGate> { return this.http.post<QualityGate>(`${this.root}/quality-gates`, input); }
  update(id: string, input: SaveQualityGate): Observable<QualityGate> { return this.http.put<QualityGate>(`${this.root}/quality-gates/${id}`, input); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/quality-gates/${id}`); }
  evaluate(input: EvaluateInput): Observable<QualityGateEvaluation> {
    return this.http.post<QualityGateEvaluation>(`${this.root}/quality-gates/evaluate`, input);
  }
}

@Injectable({ providedIn: 'root' })
export class SignOffService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: { testPlanId?: string | null; status?: SignOffStatus | null } = {}): Observable<PagedResult<SignOffReport>> {
    return this.http.get<PagedResult<SignOffReport>>(`${this.root}/sign-off`, { params: query({ maxResultCount: 100, ...request }) });
  }
  start(input: StartSignOff): Observable<SignOffReport> { return this.http.post<SignOffReport>(`${this.root}/sign-off`, input); }
  approve(id: string, approverRole: string | null, comment: string | null): Observable<SignOffReport> {
    return this.http.post<SignOffReport>(`${this.root}/sign-off/${id}/approvals`, { approverRole, comment });
  }
}

@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(): Observable<ApiKey[]> { return this.http.get<ApiKey[]>(`${this.root}/api-keys`); }
  create(input: { name: string; expiresAt?: string | null }): Observable<ApiKeyCreated> {
    return this.http.post<ApiKeyCreated>(`${this.root}/api-keys`, input);
  }
  revoke(id: string): Observable<ApiKey> { return this.http.post<ApiKey>(`${this.root}/api-keys/${id}/revoke`, {}); }
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  get(request: { testPlanId?: string | null; days: number }): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.root}/dashboard`, { params: query({ TestPlanId: request.testPlanId, Days: request.days }) });
  }
}

@Injectable({ providedIn: 'root' })
export class FlakyTestService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(request: { minimumLevel?: number; filter?: string; maxResultCount?: number }): Observable<FlakyTestList> {
    return this.http.get<FlakyTestList>(`${this.root}/flaky-tests`, {
      params: query({ MinimumLevel: request.minimumLevel, Filter: request.filter, MaxResultCount: request.maxResultCount }),
    });
  }
  apply(clearRecovered: boolean): Observable<ApplyFlakyFlagsResult> {
    return this.http.post<ApplyFlakyFlagsResult>(`${this.root}/flaky-tests/apply`, { clearRecovered });
  }
}

@Injectable({ providedIn: 'root' })
export class AttachmentService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(ownerType: number, ownerIds: string[]): Observable<Attachment[]> {
    let params = new HttpParams().set('OwnerType', String(ownerType));
    for (const id of ownerIds) { params = params.append('OwnerIds', id); }
    return this.http.get<Attachment[]>(`${this.root}/attachments`, { params });
  }

  upload(ownerType: number, ownerId: string, file: File, description?: string | null): Observable<Attachment> {
    const form = new FormData();
    form.append('OwnerType', String(ownerType));
    form.append('OwnerId', ownerId);
    if (description) { form.append('Description', description); }
    form.append('File', file, file.name);
    return this.http.post<Attachment>(`${this.root}/attachments`, form);
  }

  /** The bytes of a file. The request carries the token, so a link to the address would not work; the screen saves or shows the blob. */
  content(id: string): Observable<Blob> {
    return this.http.get(`${this.root}/attachments/${id}/content`, { responseType: 'blob' }).pipe(map(body => body as Blob));
  }

  remove(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/attachments/${id}`); }
}

@Injectable({ providedIn: 'root' })
export class SharedStepGroupService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  list(filter?: string): Observable<SharedStepGroupSummary[]> {
    return this.http.get<SharedStepGroupSummary[]>(`${this.root}/shared-step-groups`, { params: query({ Filter: filter }) });
  }
  get(id: string): Observable<SharedStepGroup> { return this.http.get<SharedStepGroup>(`${this.root}/shared-step-groups/${id}`); }
  create(input: SaveSharedStepGroup): Observable<SharedStepGroup> { return this.http.post<SharedStepGroup>(`${this.root}/shared-step-groups`, input); }
  update(id: string, input: SaveSharedStepGroup): Observable<SharedStepGroup> { return this.http.put<SharedStepGroup>(`${this.root}/shared-step-groups/${id}`, input); }
  remove(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/shared-step-groups/${id}`); }
  usage(id: string): Observable<SharedStepUsage[]> { return this.http.get<SharedStepUsage[]>(`${this.root}/shared-step-groups/${id}/usage`); }
  updateTestCases(id: string, testCaseIds?: string[] | null): Observable<UpdateSharedStepUsersResult> {
    return this.http.post<UpdateSharedStepUsersResult>(`${this.root}/shared-step-groups/${id}/update-test-cases`, { testCaseIds: testCaseIds ?? null });
  }
}

@Injectable({ providedIn: 'root' })
export class StepSuggestionService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  status(): Observable<StepSuggestionStatus> { return this.http.get<StepSuggestionStatus>(`${this.root}/step-suggestions/status`); }
  suggest(input: SuggestStepsInput): Observable<StepSuggestionResult> { return this.http.post<StepSuggestionResult>(`${this.root}/step-suggestions`, input); }
}
