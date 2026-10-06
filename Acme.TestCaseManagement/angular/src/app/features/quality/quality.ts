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
interface ApprovalForm { report: SignOffReport | null; role: string; comment: string }

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
    this.planService.list().subscribe(r => {
      this.plans.set(r.items);
      this.planId = r.items.find(p => p.status === 1)?.id ?? r.items[0]?.id ?? '';
    });
    this.reloadGates();
    this.reloadReports();
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  protected planName(id: string | null): string { return this.plans().find(p => p.id === id)?.name ?? shortId(id); }

  private reloadGates(): void { this.gateService.list().subscribe(g => this.gates.set(g)); }

  private reloadReports(): void { this.signOffService.list().subscribe(r => this.reports.set(r.items)); }

  // ---- evaluation
  protected evaluate(): void {
    if (!this.planId) { return; }
    this.gateService.evaluate({ testPlanId: this.planId, qualityGateId: this.gateId || null }).subscribe(e => this.evaluation.set(e));
  }

  protected startSignOff(): void {
    this.approvalForm.set({ report: null, role: this.auth.roleLabel(), comment: '' });
  }

  protected submitApproval(): void {
    const form = this.approvalForm();
    if (!form) { return; }
    const request = form.report
      ? this.signOffService.approve(form.report.id, form.role || null, form.comment || null)
      : this.signOffService.start({ testPlanId: this.planId, qualityGateId: this.gateId || null, approverRole: form.role || null, comment: form.comment || null });
    request.subscribe(report => {
      this.toast.success(this.i18n.t(report.status === SignOffStatus.Approved ? 'qg.approvedAll' : 'qg.approvalRecorded'));
      this.approvalForm.set(null);
      this.selectedReport.set(null);
      this.reloadReports();
      this.evaluate();
    });
  }

  protected approve(report: SignOffReport): void {
    this.approvalForm.set({ report, role: this.auth.roleLabel(), comment: '' });
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
    if (!form) { return; }
    const body = {
      name: form.name, description: form.description || null, minPassRate: Number(form.minPassRate),
      requiredApprovals: Number(form.requiredApprovals), isDefault: form.isDefault,
    };
    const request = form.id ? this.gateService.update(form.id, body) : this.gateService.create(body);
    request.subscribe(() => {
      this.toast.success(this.i18n.t('qg.gateSaved'));
      this.gateForm.set(null);
      this.reloadGates();
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
