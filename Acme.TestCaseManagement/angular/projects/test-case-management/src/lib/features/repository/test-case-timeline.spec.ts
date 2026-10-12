import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TCM_USER_DIRECTORY } from '../../core/host';
import { TestCaseHistory } from '../../proxy/dtos';
import { TestCaseHistoryType } from '../../proxy/enums';
import { of } from 'rxjs';
import { TestCaseTimelineComponent } from './test-case-timeline';

const line = (type: TestCaseHistoryType, extra: Partial<TestCaseHistory> = {}): TestCaseHistory => ({
  id: 'x', testCaseId: 't', type, fromValue: null, toValue: null, versionNumber: null, note: null,
  creationTime: '2026-10-01T09:00:00Z', creatorId: null, ...extra,
});

describe('the history of a test case', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: TCM_USER_DIRECTORY, useValue: { list: () => of([{ id: 'u1', userName: 'lead', displayName: 'Hoang Anh' }]) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  const open = (lines: TestCaseHistory[]) => {
    const fixture = TestBed.createComponent(TestCaseTimelineComponent);
    fixture.componentRef.setInput('testCaseId', 't');
    fixture.detectChanges();
    http.expectOne('/api/test-case-management/test-cases/t/history').flush(lines);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement);
  };
  const texts = (root: HTMLElement) => [...root.querySelectorAll('[data-test=timeline-line] .timeline-what')].map(e => e.textContent?.trim());

  it('tells each kind of line in words, with the fields of an edit, the statuses and the version', () => {
    const root = open([
      line(TestCaseHistoryType.VersionPublished, { versionNumber: 2, note: 'Added 2FA' }),
      line(TestCaseHistoryType.StatusChanged, { fromValue: 'Approved', toValue: 'UnderReview', note: 'Edited after approval' }),
      line(TestCaseHistoryType.Edited, { note: 'Title, Steps' }),
      line(TestCaseHistoryType.TagsChanged, { note: 'api, smoke' }),
      line(TestCaseHistoryType.SharedStepsChanged, { note: 'Inserted: Log in' }),
      line(TestCaseHistoryType.Created),
    ]);

    expect(texts(root)).toEqual([
      'Version v2 published: Added 2FA',
      'Status: Approved → Under review (edited after approval)',
      'Edited: Title, Steps',
      'Tags: api, smoke',
      'Shared steps inserted: Log in',
      'Test case created',
    ]);
  });

  it('names the person who did it when the host knows them, and says nothing when it does not', () => {
    const root = open([line(TestCaseHistoryType.Created, { creatorId: 'u1' }), line(TestCaseHistoryType.Edited, { note: 'Title', creatorId: 'nobody' })]);

    const rows = [...root.querySelectorAll('[data-test=timeline-line]')];
    expect(rows[0].textContent).toContain('by Hoang Anh');
    expect(rows[1].textContent).not.toContain('by ');
  });

  it('says so when there is nothing yet', () => {
    expect(open([]).textContent).toContain('Nothing has been recorded yet');
  });
});
