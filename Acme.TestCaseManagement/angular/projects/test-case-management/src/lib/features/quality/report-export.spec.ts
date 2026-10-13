import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { grant } from '../../core/auth-testing';
import { SignOffReport } from '../../proxy/dtos';
import { QualityComponent } from './quality';

const ROOT = '/api/test-case-management';

const report = { id: 'r1', title: 'Nghiệm thu Sprint 24', status: 1, approvals: [], requiredApprovals: 2, minPassRate: 95, qualityGateName: 'Release', integrityVerified: true, creationTime: '2026-10-13T10:00:00Z', snapshotHash: 'a'.repeat(64) } as unknown as SignOffReport;

describe('the report of a sign-off as a file', () => {
  let http: HttpTestingController;
  let blobs: Blob[];
  let downloads: string[];

  const open = () => {
    blobs = [];
    downloads = [];
    localStorage.clear();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: (blob: Blob) => { blobs.push(blob); return 'blob:report'; }, revokeObjectURL: () => undefined }));
    // The page makes its download by clicking a link; the click is what is recorded.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { downloads.push(this.download); });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*')] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(QualityComponent);
    fixture.detectChanges();
    http.match(() => true).forEach(request => {
      if (request.request.url === `${ROOT}/sign-off`) { request.flush({ totalCount: 1, items: [report] }); }
      else if (request.request.url.endsWith('/plans')) { request.flush({ totalCount: 0, items: [] }); }
      else { request.flush([]); }
    });
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => localStorage.clear());
  afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

  it('asks for the PDF of that report, and hands the file to the browser under the name the server gave', () => {
    const fixture = open();

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-test=export-pdf]')!.click();

    const request = http.expectOne(r => r.url === `${ROOT}/sign-off/r1/export`);
    expect(request.request.params.get('format')).toBe('0');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['%PDF-'], { type: 'application/pdf' }), { headers: { 'Content-Disposition': 'attachment; filename="signoff-sprint-24-20261013.pdf"' } });

    expect(blobs.length).toBe(1);
    expect(downloads).toEqual(['signoff-sprint-24-20261013.pdf']);
  });

  it('asks for the workbook with the other format, and both are offered in the detail of a report too', () => {
    const fixture = open();
    const root = fixture.nativeElement as HTMLElement;

    root.querySelector<HTMLButtonElement>('[data-test=export-xlsx]')!.click();
    const request = http.expectOne(r => r.url === `${ROOT}/sign-off/r1/export`);
    expect(request.request.params.get('format')).toBe('1');
    request.flush(new Blob(['x']));
    expect(downloads).toEqual(['Nghiệm thu Sprint 24.xlsx']);   // no name from the server (another origin hides the header): the title names it

    root.querySelector<HTMLAnchorElement>('td a')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));   // the click method of links is replaced above
    fixture.detectChanges();
    expect(root.querySelector('[data-test=export-pdf-detail]')).not.toBeNull();
    expect(root.querySelector('[data-test=export-xlsx-detail]')).not.toBeNull();
  });
});
