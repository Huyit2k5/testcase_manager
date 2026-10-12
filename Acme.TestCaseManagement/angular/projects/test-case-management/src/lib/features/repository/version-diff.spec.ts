import { describe, expect, it } from 'vitest';
import { TestCaseVersion, TestStep } from '../../proxy/dtos';
import { diffVersions, wordDiff } from './version-diff';

const step = (id: string, order: number, action: string, expected = 'ok', data: string | null = null): TestStep =>
  ({ id, stepOrder: order, action, expectedResult: expected, testData: data });

const version = (n: number, steps: TestStep[], extra: Partial<TestCaseVersion> = {}): TestCaseVersion => ({
  id: `v${n}`, testCaseId: 't', versionNumber: n, title: 'Login', preconditions: null, postconditions: null, changeSummary: null,
  steps, creationTime: '2026-10-01T00:00:00Z', ...extra,
});

describe('wordDiff', () => {
  it('keeps the words that stay and marks the ones that were added and removed', () => {
    const segments = wordDiff('Open the login page', 'Open the new login screen');

    expect(segments.map(s => `${s.kind}:${s.text}`)).toEqual(['same:Open the ', 'added:new ', 'same:login ', 'removed:page', 'added:screen']);
    // Put back together, the segments give both texts.
    expect(segments.filter(s => s.kind !== 'added').map(s => s.text).join('')).toBe('Open the login page');
    expect(segments.filter(s => s.kind !== 'removed').map(s => s.text).join('')).toBe('Open the new login screen');
  });

  it('is one piece when nothing changed, and handles an empty side', () => {
    expect(wordDiff('same text', 'same text')).toEqual([{ text: 'same text', kind: 'same' }]);
    expect(wordDiff('', 'new')).toEqual([{ text: 'new', kind: 'added' }]);
    expect(wordDiff('old', '')).toEqual([{ text: 'old', kind: 'removed' }]);
  });
});

describe('diffVersions', () => {
  const a = step('a', 1, 'Open the page');
  const b = step('b', 2, 'Enter the credentials');
  const c = step('c', 3, 'Press Sign in');

  it('says nothing differs for two versions with the same content', () => {
    const diff = diffVersions(version(1, [a, b, c]), version(2, [a, b, c]));

    expect(diff.identical).toBe(true);
    expect(diff.fields).toEqual([]);
    expect(diff.steps.map(s => s.status)).toEqual(['same', 'same', 'same']);
  });

  it('lists the text fields that changed, and a classification only when both versions have it', () => {
    const from = version(1, [a], { title: 'Login', priority: 1, severity: undefined });
    const to = version(2, [a], { title: 'Login with 2FA', preconditions: 'An account', priority: 3, severity: 2 });

    const diff = diffVersions(from, to);

    expect(diff.fields.map(f => f.field)).toEqual(['title', 'preconditions', 'priority']);   // severity: the old version did not keep it
    expect(diff.fields.find(f => f.field === 'priority')).toMatchObject({ type: 'enum', before: 1, after: 3 });
    expect(diff.identical).toBe(false);
  });

  it('tells an added, a removed and an edited step apart, keeping the order of the new version', () => {
    const edited = step('b', 2, 'Enter the credentials and the code');
    const added = step('d', 3, 'Check the dashboard');

    const diff = diffVersions(version(1, [a, b, c]), version(2, [a, edited, added]));

    expect(diff.steps.map(s => s.status)).toEqual(['same', 'changed', 'removed', 'added']);   // the removed step stays where it was
    expect(diff.steps[1].changed).toEqual(['action']);
    expect(diff.steps[2].before?.id).toBe('c');
    expect(diff.steps[3].after?.id).toBe('d');
  });

  it('puts a removed step after the one that came before it', () => {
    const diff = diffVersions(version(1, [a, b, c]), version(2, [a, c]));

    expect(diff.steps.map(s => `${s.status}:${(s.before ?? s.after)!.id}`)).toEqual(['same:a', 'removed:b', 'same:c']);
  });

  it('does not call the steps after an inserted one moved, but does call a step that changed place', () => {
    const inserted = diffVersions(version(1, [a, b]), version(2, [step('x', 1, 'New first'), step('a', 2, a.action), step('b', 3, b.action)]));
    expect(inserted.steps.map(s => s.status)).toEqual(['added', 'same', 'same']);

    const reordered = diffVersions(version(1, [a, b, c]), version(2, [step('c', 1, c.action), step('a', 2, a.action), step('b', 3, b.action)]));
    expect(reordered.steps.map(s => s.status)).toEqual(['moved', 'same', 'same']);
    expect(reordered.identical).toBe(false);
  });

  it('compares the expected result and the test data too', () => {
    const diff = diffVersions(version(1, [step('a', 1, 'Do', 'Done', 'x=1')]), version(2, [step('a', 1, 'Do', 'Done!', 'x=2')]));

    expect(diff.steps[0].changed).toEqual(['expectedResult', 'testData']);
  });
});
