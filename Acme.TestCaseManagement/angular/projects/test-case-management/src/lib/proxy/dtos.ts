// Mirrors the DTOs of Acme.TestCaseManagement.Application.Contracts (camelCase JSON).
import {
  ExecutionType, ImportOutcome, PlanStatus, PriorityLevel, RequirementCoverageStatus, RunStatus, SeverityLevel, SignOffStatus,
  TestCaseStatus, TestKind, TestLayer, TestResultStatus, TransferFormat,
} from './enums';

export interface PagedResult<T> { items: T[]; totalCount: number }

export interface PagedRequest { skipCount?: number; maxResultCount?: number; sorting?: string }

// ---- Suites
export interface Project {
  id: string; key: string; name: string; description: string | null; isArchived: boolean;
  suiteCount: number; testCaseCount: number; planCount: number; requirementCount: number; runCount: number;
}
export interface SaveProject { name: string; description?: string | null }

export interface TestSuite { projectId: string; id: string; parentId: string | null; name: string; description: string | null; order: number }
export interface TestSuiteTree {
  id: string; parentId: string | null; name: string; description: string | null; order: number;
  testCaseCount: number; children: TestSuiteTree[];
}

// ---- Test cases
export interface TestStep {
  id?: string | null; stepOrder?: number; action: string; expectedResult: string; testData?: string | null;
  /** Set by the server when the step is a copy of a shared step; read-only. */
  sharedStepGroupId?: string | null; sharedStepRevision?: number | null; sharedStepGroupName?: string | null; sharedStepOutdated?: boolean;
}

export interface TestCase {
  id: string; suiteId: string; code: string; title: string; description: string | null;
  preconditions: string | null; postconditions: string | null;
  priority: PriorityLevel; severity: SeverityLevel; status: TestCaseStatus; executionType: ExecutionType;
  kind: TestKind; layer: TestLayer; automationId: string | null; isFlaky: boolean; currentVersion: number;
  tags: string[]; steps: TestStep[]; creationTime: string; lastModificationTime: string | null;
}

export interface SaveTestCase {
  suiteId: string; code: string; title: string; description?: string | null;
  preconditions?: string | null; postconditions?: string | null;
  priority: PriorityLevel; severity: SeverityLevel; executionType: ExecutionType; kind: TestKind; layer: TestLayer;
  automationId?: string | null; isFlaky: boolean; tags?: string[] | null; steps: TestStep[]; changeSummary?: string | null;
}

export interface TestCaseVersion {
  id: string; testCaseId: string; versionNumber: number; title: string; preconditions: string | null;
  postconditions: string | null; changeSummary: string | null; steps: TestStep[]; creationTime: string;
}

export interface TestCaseDefect {
  defectLinkId: string; externalSystem: string; issueKey: string; issueUrl: string | null; severity: SeverityLevel;
  isResolved: boolean; linkedTime: string; testExecutionId: string; attemptNumber: number; versionNumber: number;
  executionTime: string; testRunId: string; testRunTitle: string; environment: string;
}

export interface TestCaseListRequest extends PagedRequest {
  projectId?: string | null; filter?: string; suiteId?: string | null; includeDescendantSuites?: boolean;
  status?: TestCaseStatus | null; priority?: PriorityLevel | null; severity?: SeverityLevel | null;
  tags?: string[]; hasAutomationId?: boolean | null;
}

/** The filters of an export: those of the test case list, without paging. */
export interface TestCaseExportRequest {
  format: TransferFormat; projectId?: string | null; filter?: string; suiteId?: string | null; includeDescendantSuites?: boolean;
  status?: TestCaseStatus | null; priority?: PriorityLevel | null; tags?: string[]; hasAutomationId?: boolean | null;
}

export interface ImportItemResult { row: number; code: string | null; outcome: ImportOutcome; messages: string[] }
export interface ImportReport {
  dryRun: boolean; imported: boolean; total: number; created: number; updated: number; skipped: number; recorded: number; invalid: number;
  createdSuites: number; fileErrors: string[]; ignoredColumns: string[]; items: ImportItemResult[];
}

// ---- Plans
export interface TestPlan {
  projectId: string; id: string; name: string; description: string | null; milestoneId: string | null;
  startDate: string | null; endDate: string | null; status: PlanStatus; runCount: number;
}
export interface SavePlan { projectId?: string | null; name: string; description?: string | null; milestoneId?: string | null; startDate?: string | null; endDate?: string | null }

// ---- Runs
export interface DefectLink {
  id: string; testExecutionId: string; externalSystem: string; issueKey: string; issueUrl: string | null;
  severity: SeverityLevel; isResolved: boolean; resolvedTime: string | null;
}
export interface AddDefect { externalSystem: string; issueKey: string; issueUrl?: string | null; severity?: SeverityLevel | null }

export interface TestRunItem {
  id: string; testRunId: string; sequence: number; testCaseVersionId: string; versionNumber: number; testCaseId: string;
  testCaseCode: string | null; testCaseTitle: string; assignedUserId: string | null;
  currentStatus: TestResultStatus; attemptCount: number;
}
export interface TestRunSummary {
  totalItems: number; executedItems: number; passed: number; failed: number; blocked: number; skipped: number;
  untested: number; completionPercentage: number; firstTimePassRate: number | null;
}
export interface TestRun {
  projectId: string; id: string; testPlanId: string | null; title: string; environment: string; assignedToUserId: string | null;
  status: RunStatus; items: TestRunItem[]; summary: TestRunSummary;
}
export interface TestExecution {
  id: string; testRunItemId: string; attemptNumber: number; status: TestResultStatus; actualResult: string | null;
  durationSeconds: number; defectLinks: DefectLink[]; creationTime: string; creatorId: string | null;
}
export interface CreateRun { projectId?: string | null; testPlanId?: string | null; title: string; environment: string; testCaseIds: string[] }
export interface ExecuteItem { status: TestResultStatus; actualResult?: string | null; durationSeconds: number; defects: AddDefect[] }
export interface TestRunListRequest extends PagedRequest { projectId?: string | null; filter?: string; testPlanId?: string | null; noPlan?: boolean; status?: RunStatus | null; environment?: string }

// ---- Requirements and RTM
export interface Requirement {
  projectId: string; id: string; code: string; title: string; description: string | null; acceptanceCriteria: string | null;
  priority: PriorityLevel; milestoneId: string | null;
}
export interface SaveRequirement {
  projectId?: string | null; code: string; title: string; description?: string | null; acceptanceCriteria?: string | null;
  priority: PriorityLevel; milestoneId?: string | null;
}
export interface RtmSummary {
  totalRequirements: number; coveredRequirements: number; uncoveredRequirements: number; coveragePercentage: number;
  passedRequirements: number; passedPercentage: number; failedRequirements: number; blockedRequirements: number;
  notRunRequirements: number;
}
export interface RtmTestCase {
  testCaseId: string; code: string; title: string; status: TestCaseStatus; countsTowardCoverage: boolean;
  result: TestResultStatus; lastExecutedTime: string | null;
}
export interface RtmDefect {
  testCaseId: string; testCaseCode: string; testExecutionId: string; externalSystem: string; issueKey: string;
  issueUrl: string | null; severity: SeverityLevel;
}
export interface RtmRow {
  requirementId: string; code: string; title: string; priority: PriorityLevel; milestoneId: string | null;
  status: RequirementCoverageStatus; testCases: RtmTestCase[]; blockingDefects: RtmDefect[];
}
export interface RtmMatrix { summary: RtmSummary; requirements: RtmRow[]; totalCount: number }
export interface RtmRequest { projectId?: string | null; milestoneId?: string | null; filter?: string; testPlanId?: string | null; environment?: string; status?: RequirementCoverageStatus | null; maxResultCount?: number }

// ---- Quality gates and sign-off
export interface QualityGate {
  id: string; name: string; description: string | null; minPassRate: number; requiredApprovals: number; isDefault: boolean;
}
export interface SaveQualityGate { name: string; description?: string | null; minPassRate: number; requiredApprovals: number; isDefault: boolean }

export interface GateCriterion { code: string; label: string; operator: string; threshold: number; actual: number | null; passed: boolean }
export interface OpenDefectIssue { externalSystem: string; issueKey: string; issueUrl: string | null; severity: SeverityLevel }
export interface QualityMetrics {
  runCount: number; totalItems: number; passed: number; failed: number; blocked: number; skipped: number; untested: number;
  completionPercentage: number; passRate: number | null; firstTimePassRate: number | null;
  p1Total: number; p1Executed: number; p1ExecutionRate: number;
  openDefects: { critical: number; high: number; medium: number; low: number; total: number };
  openDefectIssues: OpenDefectIssue[];
}
export interface QualityGateEvaluation {
  passed: boolean;
  gate: { id: string | null; name: string; minPassRate: number; requiredApprovals: number; isBuiltIn: boolean };
  scope: { testPlanId: string | null; milestoneId: string | null; plans: { id: string; name: string }[] };
  metrics: QualityMetrics; criteria: GateCriterion[]; evaluatedTime: string;
}
export interface EvaluateInput { projectId?: string | null; testPlanId?: string | null; milestoneId?: string | null; qualityGateId?: string | null }

export interface SignOffApproval {
  id: string; approverUserId: string; approverName: string; approverRole: string | null; comment: string | null;
  approvedTime: string; signature: string;
}
export interface SignOffReport {
  projectId: string; id: string; testPlanId: string | null; milestoneId: string | null; title: string; qualityGateId: string | null;
  qualityGateName: string; minPassRate: number; requiredApprovals: number; status: SignOffStatus;
  approvedTime: string | null; snapshotHash: string; integrityVerified: boolean;
  summary: QualityGateEvaluation | null; approvals: SignOffApproval[]; creationTime: string;
}
export interface StartSignOff extends EvaluateInput { title?: string | null; approverRole?: string | null; comment?: string | null }

// ---- Automation (API keys for CI/CD) ------------------------------------------------------------------------------

export interface ApiKey {
  id: string;
  name: string;
  keyPrefix: string;
  expiresAt: string | null;
  revokedAt: string | null;
  lastUsedAt: string | null;
  creationTime: string;
  creatorId: string | null;
  isActive: boolean;
}

/** The only answer that carries the secret; it cannot be read again. */
export interface ApiKeyCreated extends ApiKey { key: string }

// ---- Insights (flaky tests and the dashboard) ---------------------------------------------------------------------

export interface FlakyTest {
  testCaseId: string;
  code: string;
  title: string;
  suiteId: string;
  automationId: string | null;
  observations: number;
  passes: number;
  failures: number;
  flips: number;
  score: number;
  level: number;
  isFlagged: boolean;
  lastResultAt: string | null;
  lastFailedAt: string | null;
}

export interface FlakySettings { windowSize: number; minimumObservations: number; watchScore: number; flakyScore: number; lookbackDays: number }

export interface FlakyTestList {
  items: FlakyTest[];
  totalCount: number;
  flakyCount: number;
  watchCount: number;
  settings: FlakySettings;
}

export interface ApplyFlakyFlagsResult { flagged: number; cleared: number; flaggedCodes: string[]; clearedCodes: string[] }

export interface DashboardProgress {
  totalItems: number; passed: number; failed: number; blocked: number; skipped: number; untested: number;
  completionPercentage: number; passRate: number | null; firstTimePassRate: number | null;
}
export interface VelocityPoint { date: string; attempts: number; itemsCompleted: number; passed: number; failed: number }
export interface Velocity { points: VelocityPoint[]; totalAttempts: number; averagePerDay: number; last7DaysAverage: number; trendPercent: number | null }
export interface BurnDownPoint { date: string; remaining: number | null; ideal: number }
export interface BurnDown {
  start: string; end: string; totalItems: number; remainingAtStart: number; remainingNow: number;
  points: BurnDownPoint[]; itemsPerDay: number; projectedFinish: string | null; onTrack: boolean | null;
}
export interface DefectDensity {
  defects: number; openDefects: number; resolvedDefects: number; executedTests: number; defectsPer100Executed: number | null;
  testsWithDefects: number; testsWithDefectsPercent: number | null;
  openCritical: number; openHigh: number; openMedium: number; openLow: number;
}
export interface FlakySummary { flaky: number; watch: number; scored: number }
export interface Dashboard {
  testPlanId: string | null; testPlanName: string | null; days: number; generatedAt: string; runCount: number;
  progress: DashboardProgress; velocity: Velocity; burnDown: BurnDown; defectDensity: DefectDensity; flaky: FlakySummary;
}

// ---- Attachments ----------------------------------------------------------------------------------------------------

export interface Attachment {
  id: string;
  ownerType: number;
  ownerId: string;
  fileName: string;
  contentType: string;
  size: number;
  sha256: string;
  description: string | null;
  creationTime: string;
  creatorId: string | null;
}

export interface TagSummary { name: string; count: number }

// ---- Shared steps -----------------------------------------------------------------------------------------------------

export interface SharedStep { id?: string | null; stepOrder?: number; action: string; expectedResult: string; testData?: string | null }
export interface SharedStepGroup {
  id: string; name: string; description: string | null; revision: number; steps: SharedStep[]; creationTime: string; lastModificationTime: string | null;
}
export interface SharedStepGroupSummary { id: string; name: string; description: string | null; revision: number; stepCount: number; usedByCount: number }
export interface SaveSharedStepGroup { name: string; description?: string | null; steps: SharedStep[] }
export interface SharedStepUsage {
  testCaseId: string; code: string; title: string; status: number; linkedRevision: number; linkedStepCount: number; isOutdated: boolean;
}
export interface UpdateSharedStepUsersResult { updated: number; codes: string[]; sentToReview: number }

// ---- Step suggestions (AI) ---------------------------------------------------------------------------------------------

export interface StepSuggestionStatus { enabled: boolean; maxRequirementLength: number; maxSteps: number; defaultSteps: number }
export interface SuggestStepsInput { requirementText: string; title?: string | null; maxSteps?: number | null; language?: string | null }
export interface SuggestedStep { action: string; expectedResult: string; testData: string | null }
export interface StepSuggestionResult { steps: SuggestedStep[] }
