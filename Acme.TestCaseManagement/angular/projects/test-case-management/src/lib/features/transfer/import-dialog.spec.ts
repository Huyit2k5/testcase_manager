import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ImportReport } from '../../proxy/dtos';
import { ImportConflictMode } from '../../proxy/enums';
import { ImportDialogComponent } from './import-dialog';

const URL_IMPORT = '/api/test-case-management/test-cases/import';

const report = (patch: Partial<ImportReport>): ImportReport => ({
  dryRun: true, imported: false, total: 1, created: 1, updated: 0, skipped: 0, recorded: 0, invalid: 0,
  createdSuites: 0, fileErrors: [], ignoredColumns: [], items: [], ...patch,
});

describe('ImportDialogComponent', () => {
  let http: HttpTestingController;
  let fixture: ReturnType<typeof TestBed.createComponent<ImportDialogComponent>>;
  const read = <T>(name: string): T => (fixture.componentInstance as unknown as Record<string, () => T>)[name]();
  const call = (name: string, ...args: unknown[]) => (fixture.componentInstance as unknown as Record<string, (...a: unknown[]) => unknown>)[name](...args);

  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImportDialogComponent);
    fixture.componentRef.setInput('kind', 'cases');
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { file: { set(f: File): void } }).file.set(new File(['x'], 'cases.csv'));
  });

  it('discards a check that comes back after the options changed, so the import stays locked', () => {
    call('submit', true);
    const request = http.expectOne(URL_IMPORT);
    expect((request.request.body as FormData).get('OnExisting')).toBe(String(ImportConflictMode.Skip));

    (fixture.componentInstance as unknown as { onExisting: ImportConflictMode }).onExisting = ImportConflictMode.Update;
    call('reset');
    request.flush(report({}));

    expect(read<ImportReport | null>('report')).toBeNull();
    expect(read<boolean>('canImport')).toBe(false);
    expect(read<boolean>('busy')).toBe(false);
  });

  it('keeps a check whose options did not change', () => {
    call('submit', true);
    http.expectOne(URL_IMPORT).flush(report({}));
    expect(read<boolean>('canImport')).toBe(true);
  });

  it('disables the file and the option selects while a request is running', async () => {
    call('submit', true);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector<HTMLSelectElement>('#import-suite')?.disabled).toBe(true);
    expect(root.querySelector<HTMLSelectElement>('#import-existing')?.disabled).toBe(true);
    expect(root.querySelector<HTMLInputElement>('#import-file')?.disabled).toBe(true);
    http.expectOne(URL_IMPORT).flush(report({}));
  });
});
