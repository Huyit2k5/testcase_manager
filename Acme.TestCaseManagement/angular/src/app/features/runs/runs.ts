import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge, toDateInput, toIsoDate } from '../../core/ui';
import { TestPlan, TestRun } from '../../proxy/dtos';
import { PLAN_TRANSITIONS, PlanStatus, RunStatus } from '../../proxy/enums';
import { TestPlanService, TestRunService } from '../../proxy/services';
import { CasePickerComponent } from './case-picker';

interface PlanForm { id: string | null; name: string; description: string; milestoneId: string; startDate: string; endDate: string }
interface RunForm { testPlanId: string; title: string; environment: string; testCaseIds: string[] }

@Component({
  selector: 'app-runs',
  imports: [FormsModule, RouterLink, FormatDatePipe, TranslatePipe, ModalComponent, CasePickerComponent],
  templateUrl: './runs.html',
})
export class RunsComponent implements OnInit {
  private readonly planService = inject(TestPlanService);
  private readonly runService = inject(TestRunService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly runs = signal<TestRun[]>([]);
  protected readonly planForm = signal<PlanForm | null>(null);
  protected readonly runForm = signal<RunForm | null>(null);

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
    this.planService.list().subscribe(r => this.plans.set(r.items));
    this.runService.list().subscribe(r => this.runs.set(r.items));
  }

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
    if (!form) { return; }
    const body = {
      name: form.name, description: form.description || null, milestoneId: form.milestoneId || null,
      startDate: toIsoDate(form.startDate), endDate: toIsoDate(form.endDate),
    };
    const request = form.id ? this.planService.update(form.id, body) : this.planService.create(body);
    request.subscribe(() => {
      this.toast.success(this.i18n.t('runs.planSaved'));
      this.planForm.set(null);
      this.reload();
    });
  }

  protected movePlan(plan: TestPlan, target: PlanStatus): void {
    this.planService.changeStatus(plan.id, target).subscribe(() => {
      this.toast.success(this.i18n.t('runs.planNow', { status: this.label(PlanStatus, target) }));
      this.reload();
    });
  }

  protected deletePlan(plan: TestPlan): void {
    if (!confirm(this.i18n.t('runs.confirmDeletePlan', { name: plan.name }))) { return; }
    this.planService.delete(plan.id).subscribe(() => {
      this.toast.success(this.i18n.t('runs.planDeleted'));
      this.reload();
    });
  }

  // ---- runs
  protected newRun(): void {
    const active = this.plans().find(p => p.status === PlanStatus.Active) ?? this.plans()[0];
    this.runForm.set({ testPlanId: active?.id ?? '', title: '', environment: 'Staging', testCaseIds: [] });
  }

  protected createRun(): void {
    const form = this.runForm();
    if (!form) { return; }
    this.runService.create({
      testPlanId: form.testPlanId || null, title: form.title, environment: form.environment, testCaseIds: form.testCaseIds,
    }).subscribe(run => {
      this.toast.success(this.i18n.t('runs.runCreated'));
      this.runForm.set(null);
      void this.router.navigate(['/runs', run.id]);
    });
  }
}
