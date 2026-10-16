import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink, convertToParamMap } from '@angular/router';
import { map, of } from 'rxjs';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { TCM_BASE_PATH } from '../../core/host';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge, pct } from '../../core/ui';
import { UserNames } from '../../core/users';
import { RowMenuComponent, RowMenuItem } from '../../core/row-menu';
import { AddDefect, DefectLink, TestExecution, TestRun, TestRunItem, TestStep } from '../../proxy/dtos';
import { AttachmentOwnerType, RunStatus, SeverityLevel, TestResultStatus, TransferFormat, enumOptions } from '../../proxy/enums';
import { TestCaseService, TestRunService } from '../../proxy/services';
import { TransferService, saveFile } from '../../proxy/transfer';
import { AttachmentsComponent } from '../attachments/attachments';
import { ImportDialogComponent } from '../transfer/import-dialog';
import { CasePickerComponent } from './case-picker';
import { ConfirmService } from '../../core/confirm';

interface ExecuteForm {
  item: TestRunItem; status: TestResultStatus; actualResult: string; durationSeconds: number; defects: AddDefect[];
}

/** One step of the version being run, with the result the tester gives it (null: the step is not run, so no result is recorded). */
interface StepRow { id: string; order: number; action: string; expectedResult: string; testData: string | null; status: TestResultStatus | null; actual: string }

@Component({
  selector: 'app-run-detail',
  imports: [RowMenuComponent, FormsModule, RouterLink, FormatDatePipe, TranslatePipe, ModalComponent, CasePickerComponent, ImportDialogComponent, AttachmentsComponent],
  templateUrl: './run-detail.html',
})
export class RunDetailComponent {
  protected readonly base = inject(TCM_BASE_PATH);
  private readonly service = inject(TestRunService);
  private readonly testCaseService = inject(TestCaseService);
  private readonly toast = inject(ToastService);
  private readonly transfer = inject(TransferService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly users = inject(UserNames);
  protected readonly perm = Permissions;

  /** Bound from the route parameter :id. */
  /** The run in the address. Read from the route, not bound as an input, so that the host does not have to enable input binding. */
  private readonly route = inject(ActivatedRoute);
  readonly id = toSignal(this.route.paramMap.pipe(map(params => params.get('id') ?? '')), { initialValue: '' });
  /** The item that "My work" asks to open for execution (?item=). The run page opens its form once, when the run has loaded. */
  private readonly requestedItem = toSignal((this.route.queryParamMap ?? of(convertToParamMap({}))).pipe(map(params => params.get('item'))), { initialValue: null });
  private openedItem: string | null = null;

  protected readonly pct = pct;
  protected readonly run = signal<TestRun | null>(null);
  /** True when the run in the address could not be loaded (not found, or not allowed). */
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  /** Counts the loads: /runs/A to /runs/B reuses this component, and a late answer for A must not show under B. */
  private loadRequest = 0;
  protected readonly executeForm = signal<ExecuteForm | null>(null);
  /** The steps of the version in the execute form; null while they load or when they could not be loaded. */
  protected readonly stepRows = signal<StepRow[] | null>(null);
  /** The steps of the version in the history dialog, by id, to show what each result is about. */
  protected readonly historySteps = signal<Map<string, TestStep>>(new Map());
  protected readonly history = signal<{ item: TestRunItem; attempts: TestExecution[] } | null>(null);
  protected readonly addForm = signal<{ testCaseIds: string[]; assignedUserId: string } | null>(null);
  /** Which items to list: 'all', 'me', 'none' (nobody assigned) or the id of a user. */
  protected readonly testerFilter = signal('all');
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

  /** The ids of the items ticked for a bulk assignment. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  /** Who the ticked items go to; '' is nobody (clears the assignment). */
  protected bulkUser = '';

  /** The items that the tester filter lets through. */
  protected readonly visibleItems = computed(() => {
    const items = this.run()?.items ?? [];
    const filter = this.testerFilter();
    const me = this.auth.user()?.userId;
    return items.filter(i => filter === 'all' || (filter === 'me' ? !!me && i.assignedUserId === me : filter === 'none' ? !i.assignedUserId : i.assignedUserId === filter));
  });

  /** The ticked items that are shown: one the filter hides is not assigned by surprise. */
  protected readonly selectedItems = computed(() => this.visibleItems().filter(i => this.selected().has(i.id)));
  protected readonly allSelected = computed(() => this.visibleItems().length > 0 && this.selectedItems().length === this.visibleItems().length);

  /** For each person with work in the run: how many of their items are done and how many there are. */
  protected readonly workload = computed(() => {
    const byUser = new Map<string, { id: string; done: number; total: number }>();
    for (const item of this.run()?.items ?? []) {
      if (!item.assignedUserId) { continue; }
      const entry = byUser.get(item.assignedUserId) ?? { id: item.assignedUserId, done: 0, total: 0 };
      entry.total++;
      if (item.currentStatus !== TestResultStatus.Untested) { entry.done++; }
      byUser.set(item.assignedUserId, entry);
    }
    return [...byUser.values()].map(e => { const name = this.users.nameOf(e.id); return { ...e, name: name ?? this.i18n.t('run.unknownUser'), known: name !== null }; }).sort((a, b) => a.name.localeCompare(b.name));
  });

  constructor() {
    this.users.load();
    effect(() => this.load(this.id()));
  }

  /** The assignment can be changed by who manages plans, on a run that is not completed, when the host can list its users. */
  protected canAssign(run: TestRun): boolean { return this.isOpen(run) && this.auth.can(Permissions.TestPlans.Manage) && this.users.available(); }

  protected testerName(item: TestRunItem): string {
    return item.assignedUserId ? this.users.nameOf(item.assignedUserId) ?? this.i18n.t('run.unknownUser') : '';
  }

  protected assign(item: TestRunItem, userId: string): void {
    const run = this.run();
    if (!run || this.busy()) { return; }
    this.busy.set(true);
    this.service.assignTester(run.id, item.id, userId || null).subscribe({
      next: updated => {
        this.busy.set(false);
        if (this.run()?.id === updated.id) { this.run.set(updated); }
        const key = userId ? 'run.assigned' : 'run.unassignedDone';
        this.toast.success(this.i18n.t(key, { code: item.testCaseCode ?? '', name: this.users.nameOf(userId) ?? '' }));
      },
      // The select shows the new choice; put it back to what the server has.
      error: () => { this.busy.set(false); this.run.update(r => (r ? { ...r } : r)); },
    });
  }

  protected toggleItem(item: TestRunItem): void {
    this.selected.update(set => { const next = new Set(set); if (!next.delete(item.id)) { next.add(item.id); } return next; });
  }

  protected toggleAllItems(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.visibleItems().map(i => i.id)));
  }

  protected clearSelection(): void { this.selected.set(new Set()); }

  protected assignSelected(): void {
    const run = this.run();
    const items = this.selectedItems();
    if (!run || !items.length || this.busy()) { return; }
    const userId = this.bulkUser;
    this.busy.set(true);
    this.service.assignTesters(run.id, items.map(i => i.id), userId || null).subscribe({
      next: updated => {
        this.busy.set(false);
        if (this.run()?.id === updated.id) { this.run.set(updated); this.selected.set(new Set()); }
        const key = userId ? 'run.assignedMany' : 'run.unassignedMany';
        this.toast.success(this.i18n.t(key, { count: items.length, name: this.users.nameOf(userId) ?? '' }));
      },
      error: () => this.busy.set(false),
    });
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected isOpen(run: TestRun): boolean { return run.status !== RunStatus.Completed; }

  private load(id: string): void {
    const request = ++this.loadRequest;
    if (this.run()?.id !== id) {
      // Another run is being opened: do not show the old one meanwhile.
      this.run.set(null);
      this.loadFailed.set(false);
      this.selected.set(new Set());
    }
    this.service.get(id).subscribe({
      next: run => {
        if (request !== this.loadRequest) { return; }
        this.loadFailed.set(false);
        this.run.set(run);
        this.openRequestedItem(run);
      },
      error: () => {
        if (request !== this.loadRequest) { return; }
        // A refresh that fails keeps the run that is on screen; only a run that never loaded shows the error.
        if (this.run()?.id !== id) { this.loadFailed.set(true); }
      },
    });
  }

  private openRequestedItem(run: TestRun): void {
    const wanted = this.requestedItem();
    if (!wanted || wanted === this.openedItem) { return; }
    this.openedItem = wanted;
    const item = run.items.find(i => i.id === wanted);
    if (item && this.isOpen(run) && this.auth.can(Permissions.TestRuns.Execute)) { this.startExecute(item); }
  }

  protected reload(): void {
    this.load(this.id());
  }

  protected excluded(run: TestRun): string[] { return run.items.map(i => i.testCaseId); }

  // ---- execute
  protected startExecute(item: TestRunItem): void {
    this.executeForm.set({ item, status: TestResultStatus.Passed, actualResult: '', durationSeconds: 0, defects: [] });
    this.stepRows.set(null);
    this.testCaseService.version(item.testCaseId, item.versionNumber).subscribe({
      // The answer is for the item that was asked: a tester who has moved on to another one must not get these steps.
      next: version => {
        if (this.executeForm()?.item.id !== item.id) { return; }
        this.stepRows.set([...version.steps].sort((a, b) => (a.stepOrder ?? 0) - (b.stepOrder ?? 0))
          .map(s => ({ id: s.id as string, order: s.stepOrder ?? 0, action: s.action, expectedResult: s.expectedResult, testData: s.testData ?? null, status: null, actual: '' })));
      },
      error: () => undefined,
    });
  }

  /** All the steps passed: the attempt passes too. */
  protected passAllSteps(form: ExecuteForm): void {
    for (const row of this.stepRows() ?? []) { row.status = TestResultStatus.Passed; }
    form.status = TestResultStatus.Passed;
  }

  /** A step that failed or was blocked settles the attempt: it cannot pass (the server refuses it), so the result follows the step. */
  protected stepChanged(form: ExecuteForm): void {
    const rows = this.stepRows() ?? [];
    if (form.status !== TestResultStatus.Passed) { return; }
    if (rows.some(r => r.status === TestResultStatus.Failed)) { form.status = TestResultStatus.Failed; }
    else if (rows.some(r => r.status === TestResultStatus.Blocked)) { form.status = TestResultStatus.Blocked; }
  }

  protected stepsAgainstPass(form: ExecuteForm): boolean {
    return form.status === TestResultStatus.Passed && (this.stepRows() ?? []).some(r => r.status === TestResultStatus.Failed || r.status === TestResultStatus.Blocked);
  }

  /** The text of a step that a result was recorded for, from the version; a note when the version does not have it. */
  protected stepText(stepId: string): string {
    const step = this.historySteps().get(stepId);
    return step ? step.action : this.i18n.t('run.stepGone');
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
      stepResults: (this.stepRows() ?? []).filter(r => r.status !== null).map(r => ({ stepId: r.id, status: r.status as TestResultStatus, actualResult: r.actual.trim() || null })),
      defects: failed
        ? form.defects.filter(d => d.issueKey.trim()).map(d => ({ ...d, issueUrl: d.issueUrl || null, severity: d.severity === null ? null : Number(d.severity) }))
        : [],
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('run.recorded', { code: form.item.testCaseCode, result: this.label(TestResultStatus, form.status) }));
        this.executeForm.set(null);
        this.stepRows.set(null);
        this.reload();
      },
      error: () => this.busy.set(false),
    });
  }

  // ---- history and defects
  protected openHistory(item: TestRunItem): void {
    this.historySteps.set(new Map());
    this.service.executions(this.id(), item.id).subscribe(attempts => {
      this.history.set({ item, attempts });
      if (attempts.some(a => a.stepResults?.length)) {
        this.testCaseService.version(item.testCaseId, item.versionNumber).subscribe(version => {
          this.historySteps.set(new Map(version.steps.map(s => [s.id as string, s])));
        });
      }
    });
  }

  protected toggleResolved(attempt: TestExecution, defect: DefectLink): void {
    this.service.updateDefect(attempt.id, defect.id, defect.severity, !defect.isResolved).subscribe(() => {
      this.toast.success(this.i18n.t(defect.isResolved ? 'run.defectReopened' : 'run.defectResolved'));
      this.refreshHistory();
    });
  }

  protected linkDefect(attempt: TestExecution): void {
    this.confirmer.askText({ title: this.i18n.t('run.linkDefectTitle'), message: this.i18n.t('run.promptIssueKey'), placeholder: 'BUG-42', confirmText: this.i18n.t('common.add') }).subscribe(key => {
      if (!key) { return; }
      this.service.addDefect(attempt.id, { externalSystem: 'Jira', issueKey: key }).subscribe(() => {
        this.toast.success(this.i18n.t('run.defectLinked'));
        this.refreshHistory();
      });
    });
  }

  protected removeDefect(attempt: TestExecution, defect: DefectLink): void {
    this.confirmer.ask({ message: this.i18n.t('run.confirmRemoveLink', { key: defect.issueKey }), confirmText: this.i18n.t('common.remove'), danger: true }).subscribe(ok => {
      if (ok) { this.service.removeDefect(attempt.id, defect.id).subscribe(() => this.refreshHistory()); }
    });
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
  protected transferItems(run: TestRun): RowMenuItem[] {
    const items: RowMenuItem[] = [{ value: 0, text: this.i18n.t('transfer.exportExcel') }, { value: 1, text: this.i18n.t('transfer.exportCsv') }];
    if (this.isOpen(run) && this.auth.can(Permissions.TestRuns.Execute)) { items.push({ value: 2, text: this.i18n.t('transfer.importResults') }); }
    return items;
  }

  protected onTransfer(choice: number): void {
    if (choice === 0) { this.exportResults(TransferFormat.Xlsx); }
    else if (choice === 1) { this.exportResults(TransferFormat.Csv); }
    else { this.importOpen.set(true); }
  }

  protected exportResults(format: TransferFormat): void {
    this.transfer.exportResults(this.id(), format).subscribe(file => {
      saveFile(file);
      this.toast.success(this.i18n.t('transfer.exported', { name: file.fileName }));
    });
  }

  // ---- run level
  protected complete(): void {
    this.confirmer.ask({ message: this.i18n.t('run.confirmComplete'), confirmText: this.i18n.t('run.complete') }).subscribe(ok => {
      if (!ok) { return; }
      this.service.complete(this.id()).subscribe(run => {
        this.toast.success(this.i18n.t('run.completed'));
        this.run.set(run);
      });
    });
  }

  protected addItems(): void {
    const form = this.addForm();
    if (!form?.testCaseIds.length) { return; }
    this.service.addItems(this.id(), form.testCaseIds, form.assignedUserId || null).subscribe(run => {
      this.toast.success(this.i18n.t('run.casesAdded'));
      this.addForm.set(null);
      this.run.set(run);
    });
  }
}
