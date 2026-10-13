import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { fieldList } from '../../core/field-names';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { RtmMatrix, RtmRow, RtmTestCase, TestPlan } from '../../proxy/dtos';
import { PriorityLevel, RequirementCoverageStatus, SeverityLevel, TestResultStatus, enumOptions } from '../../proxy/enums';
import { RequirementService, RtmService, TestPlanService } from '../../proxy/services';
import { CasePickerComponent } from '../runs/case-picker';
import { IconButtonComponent } from '../../core/icon-button';
import { ConfirmService } from '../../core/confirm';

interface RequirementForm {
  id: string | null; code: string; title: string; description: string; acceptanceCriteria: string;
  priority: PriorityLevel; milestoneId: string;
  /** The test cases linked to it that count; they are to be checked when its title, description or criteria change. */
  linkedTests: number;
}

@Component({
  selector: 'app-traceability',
  imports: [FormsModule, TranslatePipe, ModalComponent, CasePickerComponent, IconButtonComponent],
  templateUrl: './traceability.html',
})
export class TraceabilityComponent implements OnInit {
  private readonly rtmService = inject(RtmService);
  private readonly requirementService = inject(RequirementService);
  private readonly planService = inject(TestPlanService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly matrix = signal<RtmMatrix | null>(null);
  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly form = signal<RequirementForm | null>(null);
  protected readonly linkForm = signal<{ row: RtmRow; testCaseIds: string[] } | null>(null);

  protected readonly busy = signal(false);
  /** Counts the matrix requests, so that a slow answer to an old filter cannot replace the current one. */
  private matrixRequest = 0;

  protected search = '';
  protected planId = '';
  protected environment = '';
  protected status: RequirementCoverageStatus | '' = '';
  protected onlySuspect = false;
  /** Confirming that a test case still matches a changed requirement is looking after the test case. */
  protected readonly canReview = this.auth.can(Permissions.TestCases.Update);

  protected readonly coverageEnum = RequirementCoverageStatus;
  protected readonly resultEnum = TestResultStatus;
  protected readonly severityEnum = SeverityLevel;
  protected readonly priorityEnum = PriorityLevel;
  protected readonly priorityOptions = enumOptions(PriorityLevel);
  protected readonly statusOptions = enumOptions(RequirementCoverageStatus);
  protected readonly badgeOf = badge;

  ngOnInit(): void {
    // Plans are only a filter here and need their own permission; without it the filter stays empty.
    if (this.auth.can(Permissions.TestPlans.Default)) { this.planService.list().subscribe(r => this.plans.set(r.items)); }
    this.load();
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected load(): void {
    const request = ++this.matrixRequest;
    this.rtmService.matrix({
      filter: this.search,
      testPlanId: this.planId || null,
      environment: this.environment,
      status: this.status === '' ? null : this.status,
      onlySuspect: this.onlySuspect ? true : null,
    }).subscribe(m => { if (request === this.matrixRequest) { this.matrix.set(m); } });
  }

  protected newRequirement(): void {
    this.form.set({ id: null, code: '', title: '', description: '', acceptanceCriteria: '', priority: PriorityLevel.Medium, milestoneId: '', linkedTests: 0 });
  }

  protected editRequirement(row: RtmRow): void {
    const linkedTests = row.testCases.filter(t => t.countsTowardCoverage).length;
    this.requirementService.list({ filter: row.code }).subscribe(result => {
      const requirement = result.items.find(r => r.id === row.requirementId);
      if (!requirement) { return; }
      this.form.set({
        id: requirement.id, code: requirement.code, title: requirement.title, description: requirement.description ?? '',
        acceptanceCriteria: requirement.acceptanceCriteria ?? '', priority: requirement.priority, milestoneId: requirement.milestoneId ?? '', linkedTests,
      });
    });
  }

  protected saveRequirement(): void {
    const form = this.form();
    if (!form || this.busy()) { return; }
    this.busy.set(true);
    const body = {
      code: form.code, title: form.title, description: form.description || null,
      acceptanceCriteria: form.acceptanceCriteria || null, priority: form.priority, milestoneId: form.milestoneId || null,
    };
    const request = form.id ? this.requirementService.update(form.id, body) : this.requirementService.create(body);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('rtm.saved'));
        this.form.set(null);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }

  protected deleteRequirement(row: RtmRow): void {
    this.confirmer.ask({ message: this.i18n.t('rtm.confirmDelete', { code: row.code }), confirmText: this.i18n.t('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.requirementService.delete(row.requirementId).subscribe(() => {
        this.toast.success(this.i18n.t('rtm.deleted'));
        this.load();
      });
    });
  }

  protected openLink(row: RtmRow): void {
    this.linkForm.set({ row, testCaseIds: [] });
  }

  protected link(): void {
    const form = this.linkForm();
    if (!form?.testCaseIds.length || this.busy()) { return; }
    this.busy.set(true);
    this.requirementService.link(form.row.requirementId, form.testCaseIds).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('rtm.linked'));
        this.linkForm.set(null);
        this.load();
      },
      error: () => this.busy.set(false),
    });
  }

  protected unlink(row: RtmRow, testCaseId: string): void {
    this.requirementService.unlink(row.requirementId, testCaseId).subscribe(() => this.load());
  }

  protected linkedIds(row: RtmRow): string[] { return row.testCases.map(t => t.testCaseId); }

  /** "Title, Acceptance criteria since Mar 5": what changed and when, for the hint on the mark. */
  protected suspectHint(tc: RtmTestCase): string {
    return this.i18n.t('rtm.checkTitle', { fields: fieldList(this.i18n, tc.suspectReason), date: tc.suspectSince ? this.i18n.date(tc.suspectSince, 'mediumDate') : '' });
  }

  protected reviewLink(row: RtmRow, tc: RtmTestCase): void {
    this.requirementService.reviewLink(row.requirementId, tc.testCaseId).subscribe(() => {
      this.toast.success(this.i18n.t('rtm.reviewed', { code: tc.code }));
      this.load();
    });
  }

  protected reviewAll(row: RtmRow): void {
    this.requirementService.reviewAll(row.requirementId).subscribe(result => {
      this.toast.success(this.i18n.t('rtm.reviewedAll', { n: result.reviewed }));
      this.load();
    });
  }
}
