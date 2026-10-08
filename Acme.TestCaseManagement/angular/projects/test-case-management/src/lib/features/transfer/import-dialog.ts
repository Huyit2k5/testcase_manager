import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { ToastService } from '../../core/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { ImportReport } from '../../proxy/dtos';
import { ImportConflictMode, ImportOutcome, enumOptions } from '../../proxy/enums';
import { TransferService, saveFile, templateFile } from '../../proxy/transfer';
import { SuiteOption } from '../repository/suite-options';

/** The report lists at most this many rows; the numbers above it count them all. */
const MAX_ROWS_SHOWN = 500;

/**
 * Imports test cases into the library, or results into a run. The file is checked first (a dry run on the server, which
 * writes nothing) and can only be imported once the check found no error, so what is imported is what was shown.
 */
@Component({
  selector: 'app-import-dialog',
  imports: [FormsModule, ModalComponent, TranslatePipe],
  template: `
    <app-modal [title]="(kind() === 'cases' ? 'import.casesTitle' : 'import.resultsTitle') | t" [wide]="true" (closed)="closed.emit()">
      <p class="muted">{{ (kind() === 'cases' ? 'import.casesHelp' : 'import.resultsHelp') | t }}</p>
      <p><button type="button" class="btn sm" (click)="downloadTemplate()">{{ 'import.template' | t }}</button></p>

      <div class="field">
        <label for="import-file">{{ 'import.file' | t }}</label>
        <input id="import-file" type="file" accept=".xlsx,.csv,text/csv" [disabled]="busy()" (change)="pick($event)" />
      </div>

      @if (kind() === 'cases') {
        <div class="form-grid">
          <div class="field">
            <label for="import-suite">{{ 'import.defaultSuite' | t }}</label>
            <select id="import-suite" name="suite" [disabled]="busy()" [(ngModel)]="defaultSuiteId" (ngModelChange)="reset()">
              <option value="">{{ 'import.noDefaultSuite' | t }}</option>
              @for (suite of suites(); track suite.id) { <option [value]="suite.id">{{ suite.label }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="import-existing">{{ 'import.onExisting' | t }}</label>
            <select id="import-existing" name="existing" [disabled]="busy()" [(ngModel)]="onExisting" (ngModelChange)="reset()">
              @for (o of conflictOptions; track o.value) { <option [ngValue]="o.value">{{ i18n.enumText(conflictEnum, o.value) }}</option> }
            </select>
          </div>
        </div>
      }

      @if (report(); as r) {
        @for (error of r.fileErrors; track $index) { <div class="alert bad" role="alert">{{ error }}</div> }

        @if (r.fileErrors.length === 0) {
          <div class="alert" [class]="r.invalid > 0 ? 'bad' : (r.imported ? 'ok' : 'warn')" role="status">
            {{ headline(r) | t }}
          </div>

          <div class="stats">
            <div class="stat"><b>{{ r.total }}</b><span>{{ 'import.total' | t }}</span></div>
            @if (kind() === 'cases') {
              <div class="stat"><b>{{ r.created }}</b><span>{{ i18n.enumText(outcomeEnum, outcomeEnum.Created) }}</span></div>
              <div class="stat"><b>{{ r.updated }}</b><span>{{ i18n.enumText(outcomeEnum, outcomeEnum.Updated) }}</span></div>
              <div class="stat"><b>{{ r.createdSuites }}</b><span>{{ 'import.newSuites' | t }}</span></div>
            } @else {
              <div class="stat"><b>{{ r.recorded }}</b><span>{{ i18n.enumText(outcomeEnum, outcomeEnum.Recorded) }}</span></div>
            }
            <div class="stat"><b>{{ r.skipped }}</b><span>{{ i18n.enumText(outcomeEnum, outcomeEnum.Skipped) }}</span></div>
            <div class="stat"><b [style.color]="r.invalid ? 'var(--bad)' : null">{{ r.invalid }}</b><span>{{ i18n.enumText(outcomeEnum, outcomeEnum.Invalid) }}</span></div>
          </div>

          @if (r.ignoredColumns.length) {
            <p class="muted">{{ 'import.ignored' | t: { columns: r.ignoredColumns.join(', ') } }}</p>
          }

          @if (r.items.length) {
            <div class="card flush" style="max-height: 280px; overflow: auto">
              <table>
                <thead><tr><th>{{ 'import.colRow' | t }}</th><th>{{ 'common.code' | t }}</th><th>{{ 'import.colOutcome' | t }}</th><th>{{ 'import.colNotes' | t }}</th></tr></thead>
                <tbody>
                  @for (item of shown(r); track item.row) {
                    <tr>
                      <td>{{ item.row }}</td>
                      <td class="mono">{{ item.code }}</td>
                      <td><span class="badge" [class]="badgeOf('outcome', item.outcome)">{{ i18n.enumText(outcomeEnum, item.outcome) }}</span></td>
                      <td>@for (message of item.messages; track $index) { <div>{{ message }}</div> }</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (r.items.length > maxShown) {
              <p class="muted">{{ 'import.more' | t: { n: r.items.length - maxShown } }}</p>
            }
          }
        }
      }

      <ng-container slot="footer">
        <button type="button" class="btn" (click)="closed.emit()">{{ (report()?.imported ? 'common.close' : 'common.cancel') | t }}</button>
        @if (!report()?.imported) {
          <button type="button" class="btn" [disabled]="!file() || busy()" (click)="submit(true)">{{ (busy() ? 'import.checking' : 'import.check') | t }}</button>
          <button type="button" class="btn primary" [disabled]="!canImport() || busy()" (click)="submit(false)">
            {{ (kind() === 'cases' ? 'import.run' : 'import.runResults') | t }}
          </button>
        }
      </ng-container>
    </app-modal>
  `,
})
export class ImportDialogComponent {
  private readonly transfer = inject(TransferService);
  private readonly toast = inject(ToastService);
  protected readonly i18n = inject(I18nService);

  readonly kind = input.required<'cases' | 'results'>();
  /** The run that results are imported into. */
  readonly runId = input<string | null>(null);
  readonly suites = input<SuiteOption[]>([]);

  /** Emitted after a real import, so that the screen can reload. */
  readonly imported = output<ImportReport>();
  readonly closed = output<void>();

  protected readonly file = signal<File | null>(null);
  protected readonly report = signal<ImportReport | null>(null);
  protected readonly busy = signal(false);
  /** Counts the changes of file and options; a check that started before the last change describes something else. */
  private choice = 0;
  protected defaultSuiteId = '';
  protected onExisting = ImportConflictMode.Skip;

  protected readonly conflictOptions = enumOptions(ImportConflictMode);
  protected readonly conflictEnum = ImportConflictMode;
  protected readonly outcomeEnum = ImportOutcome;
  protected readonly badgeOf = badge;
  protected readonly maxShown = MAX_ROWS_SHOWN;

  /** The import is allowed once the file has been checked, is free of errors and has something to do. */
  protected readonly canImport = computed(() => {
    const r = this.report();
    return !!r && r.dryRun && r.fileErrors.length === 0 && r.invalid === 0 && r.created + r.updated + r.recorded > 0;
  });

  protected pick(event: Event): void {
    this.file.set((event.target as HTMLInputElement).files?.item(0) ?? null);
    this.reset();
  }

  /** What was checked is no longer what would be imported: the file or an option changed. */
  protected reset(): void {
    this.choice++;
    this.report.set(null);
  }

  protected shown(report: ImportReport) {
    return report.items.slice(0, MAX_ROWS_SHOWN);
  }

  protected headline(report: ImportReport): string {
    if (report.invalid > 0) { return 'import.hasErrors'; }
    if (report.imported) { return 'import.did'; }
    return report.created + report.updated + report.recorded > 0 ? 'import.wouldDo' : 'import.nothing';
  }

  protected downloadTemplate(): void {
    saveFile(templateFile(this.kind()));
  }

  protected submit(dryRun: boolean): void {
    const file = this.file();
    if (!file) { return; }

    this.busy.set(true);
    const choice = this.choice;
    const request = this.kind() === 'cases'
      ? this.transfer.importTestCases(file, { defaultSuiteId: this.defaultSuiteId || null, onExisting: this.onExisting, dryRun })
      : this.transfer.importResults(this.runId()!, file, dryRun);

    request.subscribe({
      next: report => {
        this.busy.set(false);
        // A check of an earlier choice must not unlock the import of the current one; the import itself has already happened.
        if (dryRun && choice !== this.choice) { return; }
        this.report.set(report);
        if (report.imported) {
          this.toast.success(this.i18n.t(
            this.kind() === 'cases' ? 'import.casesDone' : 'import.resultsDone',
            { created: report.created, updated: report.updated, skipped: report.skipped, recorded: report.recorded },
          ));
          this.imported.emit(report);
        }
      },
      error: () => this.busy.set(false),
    });
  }
}
