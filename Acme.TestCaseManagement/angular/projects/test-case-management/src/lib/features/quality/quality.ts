import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge, shortId } from '../../core/ui';
import { QualityGate, QualityGateEvaluation, SignOffReport, TestPlan } from '../../proxy/dtos';
import { SeverityLevel, SignOffStatus, TestResultStatus } from '../../proxy/enums';
import { QualityGateService, SignOffService, TestPlanService } from '../../proxy/services';

interface GateForm { id: string | null; name: string; description: string; minPassRate: number; requiredApprovals: number; isDefault: boolean }
/** `planId` and `gateId` are what was evaluated when the sign-off was started, so that they cannot drift with the selects. */
interface ApprovalForm { report: SignOffReport | null; role: string; comment: string; planId: string; gateId: string }

@Component({
  selector: 'app-quality',
  imports: [FormsModule, FormatDatePipe, TranslatePipe, ModalComponent],
  templateUrl: './quality.html',
})
export class QualityComponent implements OnInit {
  private readonly gateService = inject(QualityGateService);
  private readonly signOffService = inject(SignOffService);
  private readonly planService = inject(TestPlanService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly gates = signal<QualityGate[]>([]);
  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly reports = signal<SignOffReport[]>([]);
  protected readonly evaluation = signal<QualityGateEvaluation | null>(null);
  /** The plan and gate the result on screen was computed for; the selects may point elsewhere by now. */
  protected readonly evaluatedPlanId = signal('');
  protected readonly evaluatedGateId = signal('');
  protected readonly approvalBusy = signal(false);
  protected readonly gateBusy = signal(false);
  /** Counts the evaluations; a change of the selects counts too, so that an answer for an old choice is dropped. */
  private evaluationRequest = 0;
  protected readonly gateForm = signal<GateForm | null>(null);
  protected readonly approvalForm = signal<ApprovalForm | null>(null);
  protected readonly selectedReport = signal<SignOffReport | null>(null);

  protected planId = '';
  protected gateId = '';

  protected readonly severityEnum = SeverityLevel;
  protected readonly resultEnum = TestResultStatus;
  protected readonly statusEnum = SignOffStatus;
  protected readonly badgeOf = badge;
  protected readonly shortId = shortId;

  protected readonly canEvaluate = computed(() => this.plans().length > 0);

  ngOnInit(): void {
    // The plan list and the sign-off list have their own permissions; without them the part stays empty instead of failing.
    if (this.auth.can(Permissions.TestPlans.Default)) {
      this.planService.list().subscribe(r => {
        this.plans.set(r.items);
        this.planId = r.items.find(p => p.status === 1)?.id ?? r.items[0]?.id ?? '';
      });
    }
    this.reloadGates();
    this.reloadReports();
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected planName(id: string | null): string { return this.plans().find(p => p.id === id)?.name ?? shortId(id); }

  private reloadGates(): void { this.gateService.list().subscribe(g => this.gates.set(g)); }

  private reloadReports(): void {
    if (!this.auth.can(Permissions.SignOff.Default)) { return; }
    this.signOffService.list().subscribe(r => this.reports.set(r.items));
  }

  // ---- evaluation
  protected evaluate(): void {
    if (!this.planId) { return; }
    this.evaluateFor(this.planId, this.gateId);
  }

  private evaluateFor(planId: string, gateId: string): void {
    const request = ++this.evaluationRequest;
    this.gateService.evaluate({ testPlanId: planId, qualityGateId: gateId || null }).subscribe(e => {
      if (request !== this.evaluationRequest) { return; }
      this.evaluatedPlanId.set(planId);
      this.evaluatedGateId.set(gateId);
      this.evaluation.set(e);
    });
  }

  /** A result belongs to the plan and gate it was computed for: another choice makes it stale, so it goes. */
  protected selectionChanged(): void {
    this.evaluationRequest++;
    this.evaluation.set(null);
  }

  protected startSignOff(): void {
    if (!this.evaluation()) { return; }
    this.approvalForm.set({ report: null, role: this.auth.roleLabel(), comment: '', planId: this.evaluatedPlanId(), gateId: this.evaluatedGateId() });
  }

  protected submitApproval(): void {
    const form = this.approvalForm();
    if (!form || this.approvalBusy()) { return; }
    this.approvalBusy.set(true);
    const request = form.report
      ? this.signOffService.approve(form.report.id, form.role || null, form.comment || null)
      : this.signOffService.start({ testPlanId: form.planId, qualityGateId: form.gateId || null, approverRole: form.role || null, comment: form.comment || null });
    request.subscribe({
      next: report => {
        this.approvalBusy.set(false);
        this.toast.success(this.i18n.t(report.status === SignOffStatus.Approved ? 'qg.approvedAll' : 'qg.approvalRecorded'));
        this.approvalForm.set(null);
        this.selectedReport.set(null);
        this.reloadReports();
        // The result on screen is refreshed for the plan and gate it belongs to, not for whatever the selects show now.
        if (this.evaluation()) { this.evaluateFor(this.evaluatedPlanId(), this.evaluatedGateId()); }
      },
      error: () => this.approvalBusy.set(false),
    });
  }

  protected approve(report: SignOffReport): void {
    this.approvalForm.set({ report, role: this.auth.roleLabel(), comment: '', planId: '', gateId: '' });
  }

  protected alreadyApproved(report: SignOffReport): boolean {
    return report.approvals.some(a => a.approverUserId === this.auth.user()?.userId);
  }

  // ---- gates
  protected newGate(): void {
    this.gateForm.set({ id: null, name: '', description: '', minPassRate: 95, requiredApprovals: 2, isDefault: this.gates().length === 0 });
  }

  protected editGate(gate: QualityGate): void {
    this.gateForm.set({
      id: gate.id, name: gate.name, description: gate.description ?? '', minPassRate: gate.minPassRate,
      requiredApprovals: gate.requiredApprovals, isDefault: gate.isDefault,
    });
  }

  protected saveGate(): void {
    const form = this.gateForm();
    if (!form || this.gateBusy()) { return; }
    this.gateBusy.set(true);
    const body = {
      name: form.name, description: form.description || null, minPassRate: Number(form.minPassRate),
      requiredApprovals: Number(form.requiredApprovals), isDefault: form.isDefault,
    };
    const request = form.id ? this.gateService.update(form.id, body) : this.gateService.create(body);
    request.subscribe({
      next: () => {
        this.gateBusy.set(false);
        this.toast.success(this.i18n.t('qg.gateSaved'));
        this.gateForm.set(null);
        this.reloadGates();
      },
      error: () => this.gateBusy.set(false),
    });
  }

  protected deleteGate(gate: QualityGate): void {
    if (!confirm(this.i18n.t('qg.confirmDeleteGate', { name: gate.name }))) { return; }
    this.gateService.delete(gate.id).subscribe(() => {
      this.toast.success(this.i18n.t('qg.gateDeleted'));
      this.reloadGates();
    });
  }
}
