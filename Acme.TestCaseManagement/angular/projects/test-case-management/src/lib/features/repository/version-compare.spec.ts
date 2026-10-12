import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TestCaseVersion, TestStep } from '../../proxy/dtos';
import { VersionCompareComponent } from './version-compare';

const step = (id: string, order: number, action: string): TestStep => ({ id, stepOrder: order, action, expectedResult: 'ok', testData: null });

const version = (n: number, title: string, steps: TestStep[], extra: Partial<TestCaseVersion> = {}): TestCaseVersion => ({
  id: `v${n}`, testCaseId: 't', versionNumber: n, title, preconditions: null, postconditions: null, changeSummary: null,
  steps, creationTime: '2026-10-01T00:00:00Z', ...extra,
});

describe('the comparison of two versions', () => {
  beforeEach(() => TestBed.resetTestingModule());

  const open = (versions: TestCaseVersion[]) => {
    const fixture = TestBed.createComponent(VersionCompareComponent);
    fixture.componentRef.setInput('versions', versions);
    fixture.detectChanges();
    return fixture;
  };
  const root = (fixture: ReturnType<typeof open>) => fixture.nativeElement as HTMLElement;

  it('says that two versions are needed when there are fewer', () => {
    expect(root(open([version(1, 'Login', [])])).querySelector('[data-test=compare-need-two]')).not.toBeNull();
  });

  it('compares the newest version with the one before it, showing the words and the steps that changed', () => {
    const fixture = open([
      version(2, 'Login with 2FA', [step('a', 1, 'Open the page'), step('b', 2, 'Press Sign in')], { changeSummary: 'Added 2FA', priority: 3 }),
      version(1, 'Login', [step('a', 1, 'Open the page')], { priority: 1 }),
    ]);

    const html = root(fixture);
    expect(html.querySelector('[data-test=compare-summary]')?.textContent).toContain('Added 2FA');
    const fields = html.querySelector('[data-test=compare-fields]')!;
    expect(fields.querySelector('.diff-ins')?.textContent).toContain('with');
    expect(fields.textContent).toContain('Priority');
    expect([...html.querySelectorAll('[data-test=compare-steps] tbody tr')].map(r => r.getAttribute('data-status'))).toEqual(['same', 'added']);
  });

  it('says when the two versions have the same content, and asks for two different ones when the same is chosen twice', () => {
    const fixture = open([version(2, 'Login', [step('a', 1, 'Open')]), version(1, 'Login', [step('a', 1, 'Open')])]);
    expect(root(fixture).querySelector('[data-test=compare-identical]')).not.toBeNull();

    const component = fixture.componentInstance as unknown as { fromNumber: { set(n: number): void } };
    component.fromNumber.set(2);
    fixture.detectChanges();
    expect(root(fixture).querySelector('[data-test=compare-same-version]')).not.toBeNull();
  });
});
