import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { TCM_BASE_PATH } from '../../core/host';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { AddDefect, DefectLink, TestExecution, TestRun, TestRunItem } from '../../proxy/dtos';
import { AttachmentOwnerType, RunStatus, SeverityLevel, TestResultStatus, TransferFormat, enumOptions } from '../../proxy/enums';
import { TestRunService } from '../../proxy/services';
import { TransferService, saveFile } from '../../proxy/transfer';
import { AttachmentsComponent } from '../attachments/attachments';
import { ImportDialogComponent } from '../transfer/import-dialog';
import { CasePickerComponent } from './case-picker';

interface ExecuteForm {
  item: TestRunItem; status: TestResultStatus; actualResult: string; durationSeconds: number; defects: AddDefect[];
}

@Component({
  selector: 'app-run-detail',
  imports: [FormsModule, RouterLink, FormatDatePipe, TranslatePipe, ModalComponent, CasePickerComponent, ImportDialogComponent, AttachmentsComponent],
  templateUrl: './run-detail.html',
})
export class RunDetailComponent {
  protected readonly base = inject(TCM_BASE_PATH);
  private readonly service = inject(TestRunService);
  private readonly toast = inject(ToastService);
  private readonly transfer = inject(TransferService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  /** Bound from the route parameter :id. */
  /** The run in the address. Read from the route, not bound as an input, so that the host does not have to enable input binding. */
  readonly id = toSignal(inject(ActivatedRoute).paramMap.pipe(map(params => params.get('id') ?? '')), { initialValue: '' });

  protected readonly run = signal<TestRun | null>(null);
  /** True when the run in the address could not be loaded (not found, or not allowed). */
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  /** Counts the loads: /runs/A to /runs/B reuses this component, and a late answer for A must not show under B. */
  private loadRequest = 0;
  protected readonly executeForm = signal<ExecuteForm | null>(null);
  protected readonly history = signal<{ item: TestRunItem; attempts: TestExecution[] } | null>(null);
  protected readonly addForm = signal<{ testCaseIds: string[] } | null>(null);
  protected readonly importOpen = signal(false);
  protected readonly formats = TransferFormat;

  protected readonly resultEnum = TestResultStatus;
  protected readonly executionOwner = AttachmentOwnerType.TestExecution;
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
    const request = ++this.loadRequest;
    if (this.run()?.id !== id) {
      // Another run is being opened: do not show the old one meanwhile.
      this.run.set(null);
      this.loadFailed.set(false);
    }
    this.service.get(id).subscribe({
      next: run => {
        if (request !== this.loadRequest) { return; }
        this.loadFailed.set(false);
        this.run.set(run);
      },
      error: () => {
        if (request !== this.loadRequest) { return; }
        // A refresh that fails keeps the run that is on screen; only a run that never loaded shows the error.
        if (this.run()?.id !== id) { this.loadFailed.set(true); }
      },
    });
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
    if (!form || !run || this.busy()) { return; }
    this.busy.set(true);
    const failed = form.status === TestResultStatus.Failed;
    this.service.execute(run.id, form.item.id, {
      status: form.status,
      actualResult: form.actualResult || null,
      durationSeconds: form.durationSeconds || 0,
      defects: failed
        ? form.defects.filter(d => d.issueKey.trim()).map(d => ({ ...d, issueUrl: d.issueUrl || null, severity: d.severity === null ? null : Number(d.severity) }))
        : [],
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('run.recorded', { code: form.item.testCaseCode, result: this.label(TestResultStatus, form.status) }));
        this.executeForm.set(null);
        this.reload();
      },
      error: () => this.busy.set(false),
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
