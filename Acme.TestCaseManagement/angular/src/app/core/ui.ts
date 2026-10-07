import {
  ImportOutcome, PlanStatus, PriorityLevel, RequirementCoverageStatus, RunStatus, SeverityLevel, SignOffStatus, TestCaseStatus, TestResultStatus,
} from '../proxy/enums';

/** CSS class of a status badge: ok, bad, warn, info or muted. */
export function badge(kind: 'result' | 'case' | 'plan' | 'run' | 'rtm' | 'signoff' | 'severity' | 'priority' | 'outcome', value: number): string {
  switch (kind) {
    case 'result':
      return ['muted', 'ok', 'bad', 'warn', 'muted'][value] ?? 'muted';
    case 'case':
      return { [TestCaseStatus.Draft]: 'muted', [TestCaseStatus.UnderReview]: 'warn', [TestCaseStatus.Approved]: 'ok', [TestCaseStatus.Deprecated]: 'bad' }[value as TestCaseStatus] ?? 'muted';
    case 'plan':
      return { [PlanStatus.Draft]: 'muted', [PlanStatus.Active]: 'info', [PlanStatus.Completed]: 'ok', [PlanStatus.Archived]: 'muted' }[value as PlanStatus] ?? 'muted';
    case 'run':
      return { [RunStatus.Planned]: 'muted', [RunStatus.InProgress]: 'info', [RunStatus.Completed]: 'ok' }[value as RunStatus] ?? 'muted';
    case 'rtm':
      return {
        [RequirementCoverageStatus.Uncovered]: 'muted', [RequirementCoverageStatus.NotRun]: 'warn',
        [RequirementCoverageStatus.Passed]: 'ok', [RequirementCoverageStatus.Failed]: 'bad', [RequirementCoverageStatus.Blocked]: 'warn',
      }[value as RequirementCoverageStatus] ?? 'muted';
    case 'signoff':
      return { [SignOffStatus.Pending]: 'warn', [SignOffStatus.Approved]: 'ok', [SignOffStatus.Superseded]: 'muted' }[value as SignOffStatus] ?? 'muted';
    case 'severity':
      return ['muted', 'info', 'warn', 'bad'][value as SeverityLevel] ?? 'muted';
    case 'priority':
      return ['muted', 'info', 'warn', 'bad'][value as PriorityLevel] ?? 'muted';
    case 'outcome':
      return { [ImportOutcome.Created]: 'ok', [ImportOutcome.Updated]: 'info', [ImportOutcome.Skipped]: 'muted', [ImportOutcome.Recorded]: 'ok', [ImportOutcome.Invalid]: 'bad' }[value as ImportOutcome] ?? 'muted';
  }
}

/** Splits a camel/Pascal-case member name into words: UnderReview becomes "Under review". */
export function words(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/** A date input value (yyyy-MM-dd) to the ISO text the API reads, or null. */
export function toIsoDate(value: string | null | undefined): string | null {
  return value ? `${value}T00:00:00` : null;
}

export function toDateInput(value: string | null | undefined): string {
  return value ? value.substring(0, 10) : '';
}

export function shortId(id: string | null | undefined): string {
  return id ? id.substring(0, 8) : '';
}

/** A file size for people: 532 B, 4.2 KB, 12 MB. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) { return `${bytes} B`; }
  const units = ['KB', 'MB', 'GB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value >= 10 || Number.isInteger(value) ? Math.round(value) : value.toFixed(1)} ${units[unit]}`;
}
