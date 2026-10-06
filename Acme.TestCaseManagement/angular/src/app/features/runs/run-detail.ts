import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { AddDefect, DefectLink, TestExecution, TestRun, TestRunItem } from '../../proxy/dtos';
import { RunStatus, SeverityLevel, TestResultStatus, TransferFormat, enumOptions } from '../../proxy/enums';
import { TestRunService } from '../../proxy/services';
import { TransferService, saveFile } from '../../proxy/transfer';
import { ImportDialogComponent } from '../transfer/import-dialog';
import { CasePickerComponent } from './case-picker';

interface ExecuteForm {
  item: TestRunItem; status: TestResultStatus; actualResult: string; durationSeconds: number; defects: AddDefect[];
}

@Component({
  selector: 'app-run-detail',
  imports: [FormsModule, RouterLink, FormatDatePipe, TranslatePipe, ModalComponent, CasePickerComponent, ImportDialogComponent],
  templateUrl: './run-detail.html',
})
export class RunDetailComponent {
  private readonly service = inject(TestRunService);
  private readonly toast = inject(ToastService);
  private readonly transfer = inject(TransferService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  /** Bound from the route parameter :id. */
  readonly id = input.required<string>();

  protected readonly run = signal<TestRun | null>(null);
  protected readonly executeForm = signal<ExecuteForm | null>(null);
  protected readonly history = signal<{ item: TestRunItem; attempts: TestExecution[] } | null>(null);
  protected readonly addForm = signal<{ testCaseIds: string[] } | null>(null);
  protected readonly importOpen = signal(false);
  protected readonly formats = TransferFormat;

  protected readonly resultEnum = TestResultStatus;
  protected readonly severityEnum = SeverityLevel;
  protected readonly runEnum = RunStatus;
  protected readonly badgeOf = badge;
  /** Untested is not a valid result to record. */
  protected readonly resultOptions = enumOptions(TestResultStatus).filter(o => o.value !== TestResultStatus.Untested);
  protected readonly severityOptions = enumOptions(SeverityLevel);

  constructor() {
    effect(() => this.load(this.id()));
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected isOpen(run: TestRun): boolean { return run.status !== RunStatus.Completed; }

  private load(id: string): void {
    this.service.get(id).subscribe(run => this.run.set(run));
  }

  protected reload(): void {
    this.load(this.id());
  }

  protected excluded(run: TestRun): string[] { return run.items.map(i => i.testCaseId); }

  // ---- execute
  protected startExecute(item: TestRunItem): void {
    this.executeForm.set({ item, status: TestResultStatus.Passed, actualResult: '', durationSeconds: 0, defects: [] });
  }

  protected addDefectRow(form: ExecuteForm): void {
    form.defects.push({ externalSystem: 'Jira', issueKey: '', issueUrl: '', severity: null });
  }

  protected execute(): void {
    const form = this.executeForm();
    const run = this.run();
    if (!form || !run) { return; }
    const failed = form.status === TestResultStatus.Failed;
    this.service.execute(run.id, form.item.id, {
      status: form.status,
      actualResult: form.actualResult || null,
      durationSeconds: form.durationSeconds || 0,
      defects: failed
        ? form.defects.filter(d => d.issueKey.trim()).map(d => ({ ...d, issueUrl: d.issueUrl || null, severity: d.severity === null ? null : Number(d.severity) }))
        : [],
    }).subscribe(() => {
      this.toast.success(this.i18n.t('run.recorded', { code: form.item.testCaseCode, result: this.label(TestResultStatus, form.status) }));
      this.executeForm.set(null);
      this.reload();
    });
  }

  // ---- history and defects
  protected openHistory(item: TestRunItem): void {
    this.service.executions(this.id(), item.id).subscribe(attempts => this.history.set({ item, attempts }));
  }

  protected toggleResolved(attempt: TestExecution, defect: DefectLink): void {
    this.service.updateDefect(attempt.id, defect.id, defect.severity, !defect.isResolved).subscribe(() => {
      this.toast.success(this.i18n.t(defect.isResolved ? 'run.defectReopened' : 'run.defectResolved'));
      this.refreshHistory();
    });
  }

  protected linkDefect(attempt: TestExecution): void {
    const key = prompt(this.i18n.t('run.promptIssueKey'));
    if (!key?.trim()) { return; }
    this.service.addDefect(attempt.id, { externalSystem: 'Jira', issueKey: key.trim() }).subscribe(() => {
      this.toast.success(this.i18n.t('run.defectLinked'));
      this.refreshHistory();
    });
  }

  protected removeDefect(attempt: TestExecution, defect: DefectLink): void {
    if (!confirm(this.i18n.t('run.confirmRemoveLink', { key: defect.issueKey }))) { return; }
    this.service.removeDefect(attempt.id, defect.id).subscribe(() => this.refreshHistory());
  }

  private refreshHistory(): void {
    const current = this.history();
    if (current) {
      // Only refreshes a dialog that is still open: a user who closed it meanwhile must not see it come back.
      this.service.executions(this.id(), current.item.id).subscribe(attempts => {
        if (this.history()) { this.history.set({ item: current.item, attempts }); }
      });
    }
    this.reload();
  }

  // ---- import and export of the results
  protected exportResults(format: TransferFormat): void {
    this.transfer.exportResults(this.id(), format).subscribe(file => {
      saveFile(file);
      this.toast.success(this.i18n.t('transfer.exported', { name: file.fileName }));
    });
  }

  // ---- run level
  protected complete(): void {
    if (!confirm(this.i18n.t('run.confirmComplete'))) { return; }
    this.service.complete(this.id()).subscribe(run => {
      this.toast.success(this.i18n.t('run.completed'));
      this.run.set(run);
    });
  }

  protected addItems(): void {
    const form = this.addForm();
    if (!form?.testCaseIds.length) { return; }
    this.service.addItems(this.id(), form.testCaseIds).subscribe(run => {
      this.toast.success(this.i18n.t('run.casesAdded'));
      this.addForm.set(null);
      this.run.set(run);
    });
  }
}
