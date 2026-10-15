import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TestCase } from '../../proxy/dtos';
import { TestCaseFormComponent } from './test-case-form';

const existing = { id: 'tc1', suiteId: 's1', code: 'EINV-001', title: 'Login', description: null, preconditions: null, postconditions: null,
  priority: 1, severity: 1, status: 0, executionType: 0, kind: 0, layer: 0, automationId: null, isFlaky: false, currentVersion: 0, tags: [],
  creationTime: '2026-01-01T00:00:00Z', lastModificationTime: null, steps: [] } as unknown as TestCase;

describe('the layout of the form of a test case', () => {
  const open = (testCase: TestCase | null) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'S' }]);
    fixture.componentRef.setInput('existing', testCase);
    fixture.detectChanges();
    http.expectOne('/api/test-case-management/test-cases/tags').flush([]);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  beforeEach(() => localStorage.clear());

  it('puts the steps before the rest of the fields, and keeps the rest folded for a new test case', () => {
    const element = open(null);
    const more = element.querySelector<HTMLDetailsElement>('[data-test=more-options]')!;
    const steps = element.querySelector('h3')!;

    expect(more.open).toBe(false);
    expect(steps.compareDocumentPosition(more) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(more.querySelector('#tc-priority')).not.toBeNull();
    expect(more.querySelector('#tc-title')).toBeNull();
  });

  it('has the rest open when a test case is edited, because the person came to change something', () => {
    expect(open(existing).querySelector<HTMLDetailsElement>('[data-test=more-options]')!.open).toBe(true);
  });
});
