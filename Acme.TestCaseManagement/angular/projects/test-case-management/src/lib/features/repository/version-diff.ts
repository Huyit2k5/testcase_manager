import { TestCaseVersion, TestStep } from '../../proxy/dtos';

/** A piece of a text that is shown as it is, as added, or as removed. */
export interface Segment { text: string; kind: 'same' | 'added' | 'removed'; }

/** A field of the test case that differs between the two versions. */
export interface FieldChange {
  field: 'title' | 'preconditions' | 'postconditions' | 'priority' | 'severity' | 'kind' | 'layer' | 'executionType';
  /** A text field shows the words that changed; a classification shows the two values. */
  type: 'text' | 'enum';
  before: string | number | null;
  after: string | number | null;
}

export type StepStatus = 'same' | 'added' | 'removed' | 'changed' | 'moved';

export interface StepDiff {
  status: StepStatus;
  before: TestStep | null;
  after: TestStep | null;
  /** Which parts of a changed step differ. */
  changed: ('action' | 'expectedResult' | 'testData')[];
}

export interface VersionDiff {
  fields: FieldChange[];
  steps: StepDiff[];
  /** Nothing differs: the same fields, the same steps in the same order. */
  identical: boolean;
}

const TEXT_FIELDS = ['title', 'preconditions', 'postconditions'] as const;
const ENUM_FIELDS = ['priority', 'severity', 'kind', 'layer', 'executionType'] as const;

const clean = (text: string | null | undefined): string => (text ?? '').trim();

/**
 * The words of the new text against the old one: what stays, what was added, what was removed (longest common subsequence of the
 * words, with the spaces kept so that the pieces can be put back together).
 */
export function wordDiff(before: string, after: string): Segment[] {
  const a = before.match(/\s+|\S+/g) ?? [];
  const b = after.match(/\s+|\S+/g) ?? [];
  const lengths: number[][] = Array.from({ length: a.length + 1 }, () => new Array<number>(b.length + 1).fill(0));
  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      lengths[i][j] = a[i] === b[j] ? lengths[i + 1][j + 1] + 1 : Math.max(lengths[i + 1][j], lengths[i][j + 1]);
    }
  }

  const segments: Segment[] = [];
  const push = (text: string, kind: Segment['kind']) => {
    const last = segments[segments.length - 1];
    if (last && last.kind === kind) { last.text += text; } else { segments.push({ text, kind }); }
  };
  let i = 0;
  let j = 0;
  while (i < a.length && j < b.length) {
    if (a[i] === b[j]) { push(a[i], 'same'); i++; j++; }
    else if (lengths[i + 1][j] >= lengths[i][j + 1]) { push(a[i], 'removed'); i++; }
    else { push(b[j], 'added'); j++; }
  }
  while (i < a.length) { push(a[i++], 'removed'); }
  while (j < b.length) { push(b[j++], 'added'); }
  return segments;
}

/** The indexes of a longest increasing subsequence: the steps that kept their relative order. */
function longestIncreasing(values: number[]): Set<number> {
  const best: number[] = new Array(values.length).fill(1);
  const previous: number[] = new Array(values.length).fill(-1);
  let end = -1;
  for (let i = 0; i < values.length; i++) {
    for (let j = 0; j < i; j++) {
      if (values[j] < values[i] && best[j] + 1 > best[i]) { best[i] = best[j] + 1; previous[i] = j; }
    }
    if (end < 0 || best[i] > best[end]) { end = i; }
  }
  const kept = new Set<number>();
  for (let i = end; i >= 0; i = previous[i]) { kept.add(i); }
  return kept;
}

function stepParts(a: TestStep, b: TestStep): StepDiff['changed'] {
  const changed: StepDiff['changed'] = [];
  if (clean(a.action) !== clean(b.action)) { changed.push('action'); }
  if (clean(a.expectedResult) !== clean(b.expectedResult)) { changed.push('expectedResult'); }
  if (clean(a.testData) !== clean(b.testData)) { changed.push('testData'); }
  return changed;
}

/**
 * What changed from version <paramref name="from"/> to <paramref name="to"/>. Steps are matched by the id they keep from one
 * version to the next; a step that was inserted, removed, edited, or only moved is told apart. A classification (priority and
 * so on) is compared only when both versions have it: an older version published before it was kept has none.
 */
export function diffVersions(from: TestCaseVersion, to: TestCaseVersion): VersionDiff {
  const fields: FieldChange[] = [];
  for (const field of TEXT_FIELDS) {
    const before = clean(from[field]);
    const after = clean(to[field]);
    if (before !== after) { fields.push({ field, type: 'text', before, after }); }
  }
  for (const field of ENUM_FIELDS) {
    const before = from[field];
    const after = to[field];
    if (before != null && after != null && before !== after) { fields.push({ field, type: 'enum', before, after }); }
  }

  const fromSteps = [...from.steps].sort((x, y) => (x.stepOrder ?? 0) - (y.stepOrder ?? 0));
  const toSteps = [...to.steps].sort((x, y) => (x.stepOrder ?? 0) - (y.stepOrder ?? 0));
  const fromIndex = new Map<string, number>();
  fromSteps.forEach((s, i) => { if (s.id) { fromIndex.set(s.id, i); } });

  // For every step of the new version, the step of the old one it is (null: it is new).
  const matches = toSteps.map(s => (s.id && fromIndex.has(s.id) ? fromIndex.get(s.id)! : null));
  const matchedFrom = new Set(matches.filter((m): m is number => m !== null));

  // Among the steps that exist in both, the ones that are out of order are the ones that moved.
  const common = matches.map((m, i) => ({ m, i })).filter((x): x is { m: number; i: number } => x.m !== null);
  const inOrder = longestIncreasing(common.map(x => x.m));
  const moved = new Set(common.filter((_, k) => !inOrder.has(k)).map(x => x.i));

  const steps: StepDiff[] = toSteps.map((after, i) => {
    const m = matches[i];
    if (m === null) { return { status: 'added', before: null, after, changed: [] }; }
    const before = fromSteps[m];
    const changed = stepParts(before, after);
    if (changed.length) { return { status: 'changed', before, after, changed }; }
    return { status: moved.has(i) ? 'moved' : 'same', before, after, changed: [] };
  });

  // A removed step is shown after the step that came before it in the old version.
  fromSteps.forEach((before, index) => {
    if (matchedFrom.has(index)) { return; }
    let at = 0;
    steps.forEach((s, k) => { if (s.before && fromSteps.indexOf(s.before) < index) { at = k + 1; } });
    steps.splice(at, 0, { status: 'removed', before, after: null, changed: [] });
  });

  return { fields, steps, identical: fields.length === 0 && steps.every(s => s.status === 'same') };
}
