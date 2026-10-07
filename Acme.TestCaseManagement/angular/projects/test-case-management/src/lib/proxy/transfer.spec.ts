import { HttpClient, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ToastService, errorInterceptor } from '../core/core';
import { I18nService } from '../core/i18n/i18n';
import { ImportConflictMode, PriorityLevel, TestCaseStatus, TransferFormat } from './enums';
import { TEMPLATES, TransferService, fileNameOf, templateFile } from './transfer';

describe('fileNameOf', () => {
  it('reads the plain and the quoted form', () => {
    expect(fileNameOf('attachment; filename=test-cases.csv')).toBe('test-cases.csv');
    expect(fileNameOf('attachment; filename="test cases.xlsx"; size=10')).toBe('test cases.xlsx');
  });

  it('prefers the UTF-8 form, which carries any name', () => {
    expect(fileNameOf("attachment; filename=\"x.csv\"; filename*=UTF-8''%C4%90%E1%BB%83.csv")).toBe('Để.csv');
  });

  it('gives null when there is no name', () => {
    expect(fileNameOf(null)).toBeNull();
    expect(fileNameOf('inline')).toBeNull();
  });
});

describe('TransferService', () => {
  let service: TransferService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(TransferService);
    http = TestBed.inject(HttpTestingController);
  });

  it('asks for the test cases the filters show, in the chosen format, and names the file from the response', () => {
    let name = '';
    service.exportTestCases({
      format: TransferFormat.Csv, filter: 'pay', suiteId: 's1', includeDescendantSuites: true,
      status: TestCaseStatus.Approved, priority: PriorityLevel.Urgent,
    }).subscribe(file => (name = file.fileName));

    const request = http.expectOne(r => r.url === '/api/test-case-management/test-cases/export');
    expect(request.request.responseType).toBe('blob');
    expect(request.request.params.get('Format')).toBe('0');
    expect(request.request.params.get('Filter')).toBe('pay');
    expect(request.request.params.get('SuiteId')).toBe('s1');
    expect(request.request.params.get('IncludeDescendantSuites')).toBe('true');
    expect(request.request.params.get('Status')).toBe('2');
    expect(request.request.params.get('Priority')).toBe('3');

    request.flush(new Blob(['x']), { headers: new HttpHeaders({ 'Content-Disposition': 'attachment; filename=test-cases-20261006.csv' }) });
    expect(name).toBe('test-cases-20261006.csv');
  });

  it('leaves out the filters that are not set and falls back to a name by format', () => {
    let name = '';
    service.exportTestCases({ format: TransferFormat.Xlsx, status: null, priority: null }).subscribe(file => (name = file.fileName));

    const request = http.expectOne(r => r.url === '/api/test-case-management/test-cases/export');
    expect(request.request.params.keys().sort()).toEqual(['Format']);
    request.flush(new Blob(['x']));
    expect(name).toBe('export.xlsx');
  });

  it('exports the results of a run', () => {
    service.exportResults('run-1', TransferFormat.Xlsx).subscribe();

    const request = http.expectOne(r => r.url === '/api/test-case-management/runs/run-1/results/export');
    expect(request.request.params.get('format')).toBe('1');
    request.flush(new Blob(['x']));
  });

  it('uploads test cases as multipart form data with the options', () => {
    const file = new File(['Code\nTC-1\n'], 'cases.csv', { type: 'text/csv' });
    service.importTestCases(file, { defaultSuiteId: 'suite-1', onExisting: ImportConflictMode.Update, dryRun: true }).subscribe();

    const request = http.expectOne('/api/test-case-management/test-cases/import');
    const form = request.request.body as FormData;
    expect((form.get('File') as File).name).toBe('cases.csv');
    expect(form.get('DefaultSuiteId')).toBe('suite-1');
    expect(form.get('OnExisting')).toBe('1');
    expect(form.get('DryRun')).toBe('true');
    request.flush({});
  });

  it('does not send a default suite that was not chosen, and uploads results into the run', () => {
    const file = new File(['Code,Result\nTC-1,Passed\n'], 'r.csv');
    service.importTestCases(file, { defaultSuiteId: null, onExisting: ImportConflictMode.Skip, dryRun: false }).subscribe();
    const cases = http.expectOne('/api/test-case-management/test-cases/import');
    expect((cases.request.body as FormData).has('DefaultSuiteId')).toBe(false);
    expect((cases.request.body as FormData).get('DryRun')).toBe('false');
    cases.flush({});

    service.importResults('run-9', file, true).subscribe();
    const results = http.expectOne('/api/test-case-management/runs/run-9/results/import');
    expect((results.request.body as FormData).get('DryRun')).toBe('true');
    results.flush({});
  });

  it('offers a template that names the columns the import reads', async () => {
    expect(TEMPLATES.cases.text.split('\r\n')[0]).toContain('Code,Title');
    expect(TEMPLATES.results.text.split('\r\n')[0]).toBe('Code,Result,ActualResult,DurationSeconds,Defects');

    const file = templateFile('results');
    expect(file.fileName).toBe('results-template.csv');
    // The three bytes of the byte order mark come in front of the text.
    expect(file.blob.size).toBe(new TextEncoder().encode(TEMPLATES.results.text).length + 3);
  });
});

describe('errorInterceptor and downloads', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting()],
    });
    TestBed.inject(I18nService).use('en');
  });

  it('shows the message of the API when a download fails, although the body arrives as a blob', async () => {
    const toasts = TestBed.inject(ToastService);
    const body = JSON.stringify({ error: { code: 'X', message: 'The export would have 30000 test cases.' } });

    let failed = false;
    TestBed.inject(HttpClient).get('/api/x/export', { responseType: 'blob' }).subscribe({ error: () => (failed = true) });
    TestBed.inject(HttpTestingController).expectOne('/api/x/export').flush(new Blob([body]), { status: 403, statusText: 'Forbidden' });

    await vi.waitFor(() => expect(failed).toBe(true));
    expect(toasts.toasts().map(t => t.text)).toEqual(['The export would have 30000 test cases.']);
  });

  it('falls back to the status when the blob is not JSON', async () => {
    const toasts = TestBed.inject(ToastService);

    TestBed.inject(HttpClient).get('/api/x/export', { responseType: 'blob' }).subscribe({ error: () => undefined });
    TestBed.inject(HttpTestingController).expectOne('/api/x/export').flush(new Blob(['<html>nope</html>']), { status: 403, statusText: 'Forbidden' });

    await vi.waitFor(() => expect(toasts.toasts().length).toBe(1));
    expect(toasts.toasts()[0].text).toBe('You do not have the permission for this action.');
  });
});
