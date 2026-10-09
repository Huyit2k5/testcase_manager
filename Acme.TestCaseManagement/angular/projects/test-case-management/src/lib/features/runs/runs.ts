import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { TCM_BASE_PATH } from '../../core/host';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge, toDateInput, toIsoDate } from '../../core/ui';
import { TestPlan, TestRun } from '../../proxy/dtos';
import { PLAN_TRANSITIONS, PlanStatus, RunStatus, enumOptions } from '../../proxy/enums';
import { TestPlanService, TestRunService } from '../../proxy/services';
import { CasePickerComponent } from './case-picker';
import { IconButtonComponent } from '../../core/icon-button';
import { RowMenuComponent, RowMenuItem } from '../../core/row-menu';
import { ConfirmService } from '../../core/confirm';

function storedView(): 'plan' | 'list' {
  try { return localStorage.getItem('tcm.runsView') === 'list' ? 'list' : 'plan'; } catch { return 'plan'; }
}

interface PlanForm { id: string | null; name: string; description: string; milestoneId: string; startDate: string; endDate: string }
interface RunForm { testPlanId: string; title: string; environment: string; testCaseIds: string[] }

@Component({
  selector: 'app-runs',
  imports: [NgTemplateOutlet, FormsModule, RouterLink, FormatDatePipe, TranslatePipe, ModalComponent, CasePickerComponent, IconButtonComponent, RowMenuComponent],
  templateUrl: './runs.html',
})
export class RunsComponent implements OnInit {
  private readonly planService = inject(TestPlanService);
  private readonly runService = inject(TestRunService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  private readonly router = inject(Router);
  protected readonly base = inject(TCM_BASE_PATH);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  /** Every plan of the project: the names of the plans of the runs, the choices of a new run, and the table of plans (filtered and paged here). */
  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly runs = signal<TestRun[]>([]);
  protected readonly runTotal = signal(0);
  protected readonly runPage = signal(0);
  protected readonly runPageSize = 15;
  /** The filters of the list of runs, which the server applies. */
  protected runSearch = '';
  protected runPlan = '';
  protected runStatus: RunStatus | '' = '';
  protected runEnvironment = '';
  /** Counts the requests for runs, so that a slow answer to an old filter cannot replace the current one. */
  private runRequest = 0;

  /** The runs are shown under their plans (a group for each, opened on request) or as one list that can be filtered and paged. */
  protected readonly view = signal<'plan' | 'list'>(storedView());
  /** The groups that are open: a plan id, or 'none' for the runs without a plan. */
  protected readonly open = signal<ReadonlySet<string>>(new Set());
  protected readonly groupRuns = signal<Record<string, { runs: TestRun[]; total: number }>>({});
  protected readonly noPlanTotal = signal(0);
  /** The most runs a group shows; more of them are in the list view. */
  protected readonly groupSize = 100;
  private opened = false;

  protected readonly planPage = signal(0);
  protected readonly planPageSize = 10;
  protected readonly planSearch = signal('');
  protected readonly planStatus = signal<PlanStatus | ''>('');
  protected readonly filteredPlans = computed(() => {
    const text = this.planSearch().trim().toLowerCase();
    const status = this.planStatus();
    return this.plans().filter(p => (status === '' || p.status === status) && (!text || p.name.toLowerCase().includes(text)));
  });
  protected readonly pagedPlans = computed(() => this.filteredPlans().slice(this.planPage() * this.planPageSize, (this.planPage() + 1) * this.planPageSize));
  protected readonly runStatusOptions = enumOptions(RunStatus);
  protected readonly planStatusOptions = enumOptions(PlanStatus);
  protected readonly planForm = signal<PlanForm | null>(null);
  protected readonly runForm = signal<RunForm | null>(null);
  protected readonly busy = signal(false);

  protected readonly badgeOf = badge;
  protected readonly planEnum = PlanStatus;
  protected readonly runEnum = RunStatus;

  ngOnInit(): void {
    this.reload();
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected planName(id: string | null): string {
    return this.plans().find(p => p.id === id)?.name ?? '-';
  }

  protected transitions(plan: TestPlan): PlanStatus[] { return PLAN_TRANSITIONS[plan.status]; }

  protected reload(): void {
    this.planService.list({ maxResultCount: 1000 }).subscribe(r => {
      this.plans.set(r.items);
      // The first time, the first plan shows its runs, so the page does not open as a column of closed groups.
      if (!this.opened && this.view() === 'plan' && this.pagedPlans().length) {
        this.opened = true;
        const first = this.pagedPlans()[0].id;
        this.open.set(new Set([first]));
        this.loadGroup(first);
      }
      // Plans that were deleted or filtered away leave the page behind the last one: go back to the last page.
      const last = Math.max(0, Math.ceil(this.filteredPlans().length / this.planPageSize) - 1);
      if (this.planPage() > last) { this.planPage.set(last); }
    });
    if (this.view() === 'list') { this.loadRuns(); } else { this.loadGroups(); }
  }

  // ---- the view by plan
  protected setView(view: 'plan' | 'list'): void {
    if (view === this.view()) { return; }
    this.view.set(view);
    try { localStorage.setItem('tcm.runsView', view); } catch { /* the choice lasts until the page is left */ }
    if (view === 'list') { this.loadRuns(); } else { this.loadGroups(); }
  }

  protected isOpen(key: string): boolean { return this.open().has(key); }

  protected toggleGroup(key: string): void {
    const next = new Set(this.open());
    if (next.has(key)) { next.delete(key); } else { next.add(key); this.loadGroup(key); }
    this.open.set(next);
  }

  protected groupOf(key: string): { runs: TestRun[]; total: number } { return this.groupRuns()[key] ?? { runs: [], total: 0 }; }

  /** The runs of every group that is open, and how many runs have no plan (to know whether that group is there at all). */
  private loadGroups(): void {
    this.loadGroup('none');
    for (const key of this.open()) { if (key !== 'none') { this.loadGroup(key); } }
  }

  private loadGroup(key: string): void {
    const request = key === 'none' ? { noPlan: true, maxResultCount: this.groupSize } : { testPlanId: key, maxResultCount: this.groupSize };
    this.runService.list(request).subscribe(result => {
      this.groupRuns.update(groups => ({ ...groups, [key]: { runs: result.items, total: result.totalCount } }));
      if (key === 'none') { this.noPlanTotal.set(result.totalCount); }
    });
  }

  protected loadRuns(): void {
    const request = ++this.runRequest;
    this.runService.list({
      filter: this.runSearch, testPlanId: this.runPlan || null, status: this.runStatus === '' ? null : this.runStatus, environment: this.runEnvironment,
      skipCount: this.runPage() * this.runPageSize, maxResultCount: this.runPageSize,
    }).subscribe(result => {
      if (request !== this.runRequest) { return; }
      // The last page is gone when runs were removed or the filter narrowed: show the one before it.
      if (!result.items.length && result.totalCount > 0 && this.runPage() > 0) {
        this.runPage.set(Math.ceil(result.totalCount / this.runPageSize) - 1);
        this.loadRuns();
        return;
      }
      this.runs.set(result.items);
      this.runTotal.set(result.totalCount);
    });
  }

  /** A filter changed: the list starts again from its first page. */
  protected filterRuns(): void {
    this.runPage.set(0);
    this.loadRuns();
  }

  protected gotoRuns(step: number): void {
    this.runPage.update(p => Math.max(0, p + step));
    this.loadRuns();
  }

  protected runRange(): { from: number; to: number } {
    const from = this.runTotal() === 0 ? 0 : this.runPage() * this.runPageSize + 1;
    return { from, to: Math.min(this.runTotal(), (this.runPage() + 1) * this.runPageSize) };
  }

  protected readonly min = Math.min;

  protected setPlanSearch(text: string): void { this.planSearch.set(text); this.planPage.set(0); }
  protected setPlanStatus(status: PlanStatus | ''): void { this.planStatus.set(status); this.planPage.set(0); }
  protected gotoPlans(step: number): void { this.planPage.update(p => Math.max(0, p + step)); }

  // ---- plans
  protected newPlan(): void {
    this.planForm.set({ id: null, name: '', description: '', milestoneId: '', startDate: '', endDate: '' });
  }

  protected editPlan(plan: TestPlan): void {
    this.planForm.set({
      id: plan.id, name: plan.name, description: plan.description ?? '', milestoneId: plan.milestoneId ?? '',
      startDate: toDateInput(plan.startDate), endDate: toDateInput(plan.endDate),
    });
  }

  protected savePlan(): void {
    const form = this.planForm();
    if (!form || this.busy()) { return; }
    this.busy.set(true);
    const body = {
      name: form.name, description: form.description || null, milestoneId: form.milestoneId || null,
      startDate: toIsoDate(form.startDate), endDate: toIsoDate(form.endDate),
    };
    const request = form.id ? this.planService.update(form.id, body) : this.planService.create(body);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('runs.planSaved'));
        this.planForm.set(null);
        this.reload();
      },
      error: () => this.busy.set(false),
    });
  }

  /** What the menu of a plan offers: its next states, each with the colour of its badge. */
  protected nextStates(plan: TestPlan): RowMenuItem[] {
    return this.transitions(plan).map(target => ({ value: target, text: this.label(PlanStatus, target), badge: badge('plan', target) }));
  }

  protected movePlan(plan: TestPlan, target: PlanStatus): void {
    this.planService.changeStatus(plan.id, target).subscribe(() => {
      this.toast.success(this.i18n.t('runs.planNow', { status: this.label(PlanStatus, target) }));
      this.reload();
    });
  }

  protected deletePlan(plan: TestPlan): void {
    this.confirmer.ask({ message: this.i18n.t('runs.confirmDeletePlan', { name: plan.name }), confirmText: this.i18n.t('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.planService.delete(plan.id).subscribe(() => {
        this.toast.success(this.i18n.t('runs.planDeleted'));
        this.reload();
      });
    });
  }

  // ---- runs
  protected newRun(): void {
    const active = this.plans().find(p => p.status === PlanStatus.Active) ?? this.plans()[0];
    this.runForm.set({ testPlanId: active?.id ?? '', title: '', environment: 'Staging', testCaseIds: [] });
  }

  protected createRun(): void {
    const form = this.runForm();
    if (!form || this.busy()) { return; }
    this.busy.set(true);
    this.runService.create({
      testPlanId: form.testPlanId || null, title: form.title, environment: form.environment, testCaseIds: form.testCaseIds,
    }).subscribe({
      next: run => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('runs.runCreated'));
        this.runForm.set(null);
        void this.router.navigate([this.base + '/runs', run.id]);
      },
      error: () => this.busy.set(false),
    });
  }
}
