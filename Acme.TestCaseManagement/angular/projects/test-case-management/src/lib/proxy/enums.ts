// Mirrors Acme.TestCaseManagement.Enums. The API sends and expects the numeric values.

export enum PriorityLevel { Low = 0, Medium = 1, High = 2, Urgent = 3 }
export enum SeverityLevel { Low = 0, Medium = 1, High = 2, Critical = 3 }
export enum TestCaseStatus { Draft = 0, UnderReview = 1, Approved = 2, Deprecated = 3 }
export enum TestResultStatus { Untested = 0, Passed = 1, Failed = 2, Blocked = 3, Skipped = 4 }
export enum ExecutionType { Manual = 0, Automated = 1, Hybrid = 2 }
export enum TestKind { Functional = 0, Performance = 1, Security = 2, Usability = 3 }
export enum TestLayer { Unit = 0, Integration = 1, E2E = 2, Acceptance = 3 }
export enum PlanStatus { Draft = 0, Active = 1, Completed = 2, Archived = 3 }
export enum RunStatus { Planned = 0, InProgress = 1, Completed = 2 }
export enum RequirementCoverageStatus { Uncovered = 0, NotRun = 1, Passed = 2, Failed = 3, Blocked = 4 }
export enum SignOffStatus { Pending = 0, Approved = 1, Superseded = 2 }
export enum TransferFormat { Csv = 0, Xlsx = 1 }
export enum ImportConflictMode { Skip = 0, Update = 1 }
export enum ImportOutcome { Created = 0, Updated = 1, Skipped = 2, Recorded = 3, Invalid = 4 }
export enum AttachmentOwnerType { TestCase = 0, TestExecution = 1 }
export enum FlakinessLevel { Insufficient = 0, Stable = 1, Watch = 2, Flaky = 3 }

export interface EnumOption { value: number; label: string }

/** Turns a numeric TypeScript enum into select options. */
export function enumOptions(enumType: object): EnumOption[] {
  return Object.entries(enumType)
    .filter(([, value]) => typeof value === 'number')
    .map(([label, value]) => ({ value: value as number, label }));
}

export function enumLabel(enumType: object, value: number | null | undefined): string {
  return value == null ? '' : ((enumType as Record<number, string>)[value] ?? String(value));
}

/** Allowed status moves, as enforced by the domain (TestCaseManager / TestPlan). */
export const TEST_CASE_TRANSITIONS: Record<TestCaseStatus, TestCaseStatus[]> = {
  [TestCaseStatus.Draft]: [TestCaseStatus.UnderReview, TestCaseStatus.Approved],
  [TestCaseStatus.UnderReview]: [TestCaseStatus.Draft, TestCaseStatus.Approved],
  [TestCaseStatus.Approved]: [TestCaseStatus.Draft, TestCaseStatus.Deprecated],
  [TestCaseStatus.Deprecated]: [TestCaseStatus.Draft],
};

export const PLAN_TRANSITIONS: Record<PlanStatus, PlanStatus[]> = {
  [PlanStatus.Draft]: [PlanStatus.Active, PlanStatus.Archived],
  [PlanStatus.Active]: [PlanStatus.Completed, PlanStatus.Archived],
  [PlanStatus.Completed]: [PlanStatus.Archived],
  [PlanStatus.Archived]: [],
};
