import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { apiRoot } from '../core/host';
import { Observable, map } from 'rxjs';
import { ImportReport, TestCaseExportRequest } from './dtos';
import { ImportConflictMode, TransferFormat } from './enums';

export interface DownloadedFile { blob: Blob; fileName: string }

/** Takes the file name out of a Content-Disposition header (the UTF-8 form first), or returns null. */
export function fileNameOf(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const encoded = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (encoded?.[1]) {
    try { return decodeURIComponent(encoded[1].trim()); } catch { /* fall through to the plain form */ }
  }
  const plain = /filename\s*=\s*("([^"]+)"|[^;]+)/.exec(header);
  return (plain?.[2] ?? plain?.[1])?.trim() || null;
}

/** Excel and CSV import and export of test cases and of the results of a run. */
@Injectable({ providedIn: 'root' })
export class TransferService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  exportTestCases(request: TestCaseExportRequest): Observable<DownloadedFile> {
    let params = new HttpParams().set('Format', String(request.format));
    if (request.filter) { params = params.set('Filter', request.filter); }
    if (request.suiteId) { params = params.set('SuiteId', request.suiteId); }
    if (request.includeDescendantSuites !== undefined) { params = params.set('IncludeDescendantSuites', String(request.includeDescendantSuites)); }
    if (request.status !== null && request.status !== undefined) { params = params.set('Status', String(request.status)); }
    if (request.priority !== null && request.priority !== undefined) { params = params.set('Priority', String(request.priority)); }
    for (const tag of request.tags ?? []) { params = params.append('Tags', tag); }
    if (request.hasAutomationId !== null && request.hasAutomationId !== undefined) { params = params.set('HasAutomationId', String(request.hasAutomationId)); }
    return this.download(`${this.root}/test-cases/export`, params, request.format);
  }

  exportResults(runId: string, format: TransferFormat): Observable<DownloadedFile> {
    return this.download(`${this.root}/runs/${runId}/results/export`, new HttpParams().set('format', String(format)), format);
  }

  importTestCases(
    file: File, options: { defaultSuiteId?: string | null; onExisting: ImportConflictMode; dryRun: boolean },
  ): Observable<ImportReport> {
    const form = new FormData();
    form.append('File', file, file.name);
    if (options.defaultSuiteId) { form.append('DefaultSuiteId', options.defaultSuiteId); }
    form.append('OnExisting', String(options.onExisting));
    form.append('DryRun', String(options.dryRun));
    return this.http.post<ImportReport>(`${this.root}/test-cases/import`, form);
  }

  importResults(runId: string, file: File, dryRun: boolean): Observable<ImportReport> {
    const form = new FormData();
    form.append('File', file, file.name);
    form.append('DryRun', String(dryRun));
    return this.http.post<ImportReport>(`${this.root}/runs/${runId}/results/import`, form);
  }

  private download(url: string, params: HttpParams, format: TransferFormat): Observable<DownloadedFile> {
    return this.http.get(url, { params, responseType: 'blob', observe: 'response' }).pipe(
      map(response => ({
        blob: response.body as Blob,
        fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? (format === TransferFormat.Xlsx ? 'export.xlsx' : 'export.csv'),
      })),
    );
  }
}

/** Hands a downloaded file to the browser's download. */
export function saveFile(file: DownloadedFile): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // The download has started by the time the click returns; the address can go.
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** The header row and one example row of each import file, for the "download a template" button. */
export const TEMPLATES = {
  cases: {
    fileName: 'test-cases-template.csv',
    text: 'Suite,Code,Title,Description,Preconditions,Postconditions,Priority,Severity,Kind,Layer,ExecutionType,AutomationId,Flaky,Tags,Action,ExpectedResult,TestData\r\n'
      + 'Payments/Cards,PAY-001,Pay by card,,,,High,Critical,Functional,Acceptance,Manual,,false,smoke; payments,Open the checkout page,The page is shown,\r\n'
      + 'Payments/Cards,PAY-001,Pay by card,,,,High,Critical,Functional,Acceptance,Manual,,false,smoke; payments,Pay with a valid card,The order is confirmed,4111 1111 1111 1111\r\n',
  },
  results: {
    fileName: 'results-template.csv',
    text: 'Code,Result,ActualResult,DurationSeconds,Defects\r\n'
      + 'PAY-001,Passed,,30,\r\n'
      + 'PAY-002,Failed,The card was declined,45,Jira:BUG-88\r\n',
  },
} as const;

export function templateFile(kind: keyof typeof TEMPLATES): DownloadedFile {
  const template = TEMPLATES[kind];
  // The byte order mark is what lets Excel show the file as UTF-8.
  return { blob: new Blob(['﻿', template.text], { type: 'text/csv;charset=utf-8' }), fileName: template.fileName };
}
