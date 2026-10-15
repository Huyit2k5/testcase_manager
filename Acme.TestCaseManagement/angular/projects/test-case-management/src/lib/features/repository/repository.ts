import { ProjectSwitchComponent } from '../projects/project-switch';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { ToastService } from '../../core/core';
import { badge } from '../../core/ui';
import { ApproveTestCasesResult, TagSummary, TestCase, TestSuiteTree } from '../../proxy/dtos';
import { PriorityLevel, SeverityLevel, TestCaseStatus, TransferFormat, enumOptions } from '../../proxy/enums';
import { TestCaseService, TestSuiteService } from '../../proxy/services';
import { TransferService, saveFile } from '../../proxy/transfer';
import { ImportDialogComponent } from '../transfer/import-dialog';
import { RowMenuComponent, RowMenuItem } from '../../core/row-menu';
import { SuiteNodeComponent } from './suite-node';
import { SuiteOption, flattenSuites } from './suite-options';
import { TestCaseDetailComponent } from './test-case-detail';
import { TestCaseFormComponent } from './test-case-form';
import { ConfirmService } from '../../core/confirm';
import { TCM_FEATURES } from '../../core/host';


const PAGE_SIZE = 20;

@Component({
  selector: 'app-repository',
  imports: [ProjectSwitchComponent, FormsModule, TranslatePipe, ModalComponent, ImportDialogComponent, SuiteNodeComponent, TestCaseDetailComponent, TestCaseFormComponent, RowMenuComponent],
  templateUrl: './repository.html',
})
export class RepositoryComponent implements OnInit {
  private readonly suiteService = inject(TestSuiteService);
  private readonly caseService = inject(TestCaseService);
  private readonly toast = inject(ToastService);
  private readonly transfer = inject(TransferService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly features = inject(TCM_FEATURES);
  protected readonly perm = Permissions;

  protected readonly tree = signal<TestSuiteTree[]>([]);
  protected readonly suites = signal<SuiteOption[]>([]);
  protected readonly selectedSuite = signal<string | null>(null);
  protected readonly cases = signal<TestCase[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly loading = signal(false);
  protected readonly suiteBusy = signal(false);
  /** Counts the list requests, so that a slow answer to an old filter cannot replace the answer to the current one. */
  private casesRequest = 0;

  protected search = '';
  protected priority: PriorityLevel | '' = '';
  protected status: TestCaseStatus | '' = '';
  protected tag = '';
  protected automation: '' | 'linked' | 'unlinked' = '';
  protected readonly tagOptions = signal<TagSummary[]>([]);

  protected readonly priorityOptions = enumOptions(PriorityLevel);
  protected readonly statusOptions = enumOptions(TestCaseStatus);
  protected readonly priorityEnum = PriorityLevel;
  protected readonly severityEnum = SeverityLevel;
  protected readonly statusEnum = TestCaseStatus;
  protected readonly badgeOf = badge;
  protected readonly pageSize = PAGE_SIZE;

  protected readonly detail = signal<TestCase | null>(null);
  protected readonly form = signal<{ existing: TestCase | null } | null>(null);
  protected readonly suiteDialog = signal<{ name: string; description: string } | null>(null);
  protected readonly importOpen = signal(false);
  protected readonly emptySelection: ReadonlySet<string> = new Set();

  /** The test cases ticked in the list, for approving together. */
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly approving = signal(false);
  /** The test cases that could not be approved together, with the reason, shown after the others were approved. */
  protected readonly approveReport = signal<ApproveTestCasesResult | null>(null);
  protected readonly formats = TransferFormat;

  ngOnInit(): void {
    this.loadTree();
    this.loadCases();
    this.loadTags();
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  // ---- approving together
  /** A draft or one waiting for review can be approved; an approved or a deprecated one cannot. */
  protected approvable(testCase: TestCase): boolean {
    return testCase.status === TestCaseStatus.Draft || testCase.status === TestCaseStatus.UnderReview;
  }

  protected isSelected(id: string): boolean { return this.selected().has(id); }

  protected toggleSelected(id: string): void {
    this.selected.update(current => {
      const next = new Set(current);
      if (!next.delete(id)) { next.add(id); }
      return next;
    });
  }

  protected allSelected(): boolean {
    const rows = this.cases().filter(tc => this.approvable(tc));
    return rows.length > 0 && rows.every(tc => this.selected().has(tc.id));
  }

  protected toggleAll(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.cases().filter(tc => this.approvable(tc)).map(tc => tc.id)));
  }

  protected approveSelected(): void {
    const ids = [...this.selected()];
    if (!ids.length || this.approving()) { return; }
    this.confirmer.askText({
      title: this.i18n.t('repo.approveManyTitle'), message: this.i18n.t('repo.approveManyMessage', { n: ids.length }),
      placeholder: this.i18n.t('detail.changeNoteHint'), confirmText: this.i18n.t('detail.approve'), optional: true,
    }).subscribe(note => {
      if (note === null) { return; }
      this.approving.set(true);
      this.caseService.approveMany(ids, note).subscribe({
        next: result => {
          this.approving.set(false);
          this.selected.set(new Set());
          if (result.approved) { this.toast.success(this.i18n.t('repo.approvedMany', { n: result.approved })); }
          if (result.failed.length) { this.approveReport.set(result); }
          this.refresh();
        },
        error: () => this.approving.set(false),
      });
    });
  }

  protected selectSuite(id: string | null): void {
    this.selectedSuite.set(id);
    this.page.set(0);
    this.loadCases();
  }

  protected applyFilters(): void {
    this.page.set(0);
    this.loadCases();
  }

  protected goto(delta: number): void {
    this.page.update(p => p + delta);
    this.loadCases();
  }

  protected selectedSuiteName(): string {
    return this.suites().find(s => s.id === this.selectedSuite())?.label.trim() ?? this.i18n.t('repo.allSuites');
  }

  private loadTree(): void {
    // A person without the permission to read suites gets no tree, not an error on every visit.
    if (!this.auth.can(Permissions.TestSuites.Default)) { return; }
    this.suiteService.tree().subscribe(tree => {
      this.tree.set(tree);
      this.suites.set(flattenSuites(tree));
    });
  }

  private hasAutomationId(): boolean | null {
    return this.automation === 'linked' ? true : this.automation === 'unlinked' ? false : null;
  }

  protected loadTagsList(): void { this.loadTags(); }

  private loadTags(): void {
    this.caseService.tags().subscribe(tags => {
      this.tagOptions.set(tags);
      // A tag that no test case has any more cannot stay selected; the list that was loaded with it is loaded again without it.
      if (this.tag && !tags.some(t => t.name.toLowerCase() === this.tag.toLowerCase())) {
        this.tag = '';
        this.page.set(0);
        this.loadCases();
      }
    });
  }

  protected loadCases(): void {
    const request = ++this.casesRequest;
    this.loading.set(true);
    this.caseService.list({
      filter: this.search,
      suiteId: this.selectedSuite(),
      includeDescendantSuites: true,
      priority: this.priority === '' ? null : this.priority,
      status: this.status === '' ? null : this.status,
      tags: this.tag ? [this.tag] : [],
      hasAutomationId: this.hasAutomationId(),
      skipCount: this.page() * PAGE_SIZE,
      maxResultCount: PAGE_SIZE,
      sorting: 'code',
    }).subscribe({
      next: result => {
        if (request !== this.casesRequest) { return; }
        const lastPage = Math.max(0, Math.ceil(result.totalCount / PAGE_SIZE) - 1);
        if (this.page() > lastPage) {
          // After a delete or a deprecate the page we were on may be gone.
          this.page.set(lastPage);
          this.loadCases();
          return;
        }
        this.cases.set(result.items);
        // Keep the ticks only for rows that are still on the page.
        const onPage = new Set((result.items ?? []).map(tc => tc.id));
        this.selected.update(current => new Set([...current].filter(id => onPage.has(id))));
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: () => { if (request === this.casesRequest) { this.loading.set(false); } },
    });
  }

  // ---- import and export
  /** Downloads what the filters show, from every page, and not just the page that is on screen. */
  /** Export (what the filters show) and import: one menu instead of three buttons. */
  protected transferItems(): RowMenuItem[] {
    const items: RowMenuItem[] = [{ value: 0, text: this.i18n.t('transfer.exportExcel') }, { value: 1, text: this.i18n.t('transfer.exportCsv') }];
    if (this.auth.can(Permissions.TestCases.Create)) { items.push({ value: 2, text: this.i18n.t('transfer.import') }); }
    return items;
  }

  protected onTransfer(choice: number): void {
    if (choice === 0) { this.exportCases(TransferFormat.Xlsx); }
    else if (choice === 1) { this.exportCases(TransferFormat.Csv); }
    else { this.importOpen.set(true); }
  }

  protected exportCases(format: TransferFormat): void {
    this.transfer.exportTestCases({
      format,
      filter: this.search,
      suiteId: this.selectedSuite(),
      includeDescendantSuites: true,
      status: this.status === '' ? null : this.status,
      priority: this.priority === '' ? null : this.priority,
      tags: this.tag ? [this.tag] : [],
      hasAutomationId: this.hasAutomationId(),
    }).subscribe(file => {
      saveFile(file);
      this.toast.success(this.i18n.t('transfer.exported', { name: file.fileName }));
    });
  }

  protected onImported(): void {
    this.refresh();
  }

  // ---- suites
  protected openSuiteDialog(): void {
    this.suiteDialog.set({ name: '', description: '' });
  }

  protected createSuite(): void {
    const dialog = this.suiteDialog();
    // Enter in the form submits even when the button is disabled, so the guard is here and not only on the button.
    if (!dialog?.name.trim() || this.suiteBusy()) {
      return;
    }
    this.suiteBusy.set(true);
    this.suiteService.create({ name: dialog.name.trim(), description: dialog.description || null, parentId: this.selectedSuite() }).subscribe({
      next: () => {
        this.suiteBusy.set(false);
        this.toast.success(this.i18n.t('repo.suiteCreated'));
        this.suiteDialog.set(null);
        this.loadTree();
      },
      error: () => this.suiteBusy.set(false),
    });
  }

  protected deleteSuite(): void {
    const id = this.selectedSuite();
    if (!id) { return; }
    this.confirmer.ask({ message: this.i18n.t('repo.confirmDeleteSuite', { name: this.selectedSuiteName() }), confirmText: this.i18n.t('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.suiteService.delete(id).subscribe(() => {
        this.toast.success(this.i18n.t('repo.suiteDeleted'));
        this.selectSuite(null);
        this.loadTree();
      });
    });
  }

  // ---- test cases
  protected open(testCase: TestCase): void {
    this.caseService.get(testCase.id).subscribe(full => this.detail.set(full));
  }

  /** No search, priority, status or tag is asked for: an empty list is then empty, not filtered. */
  protected noFilters(): boolean { return !this.search && this.priority === '' && this.status === '' && !this.tag; }

  protected newCase(): void {
    this.form.set({ existing: null });
  }

  protected editCase(testCase: TestCase): void {
    this.detail.set(null);
    this.form.set({ existing: testCase });
  }

  protected onSaved(): void {
    this.form.set(null);
    this.refresh();
  }

  /** A copy was made: it is shown, and it is in the list. */
  protected onCopied(copy: TestCase): void {
    this.detail.set(copy);
    this.refresh();
  }

  protected onChanged(): void {
    this.detail.set(null);
    this.refresh();
  }

  private refresh(): void {
    this.loadTree();
    this.loadCases();
    this.loadTags();
  }
}
