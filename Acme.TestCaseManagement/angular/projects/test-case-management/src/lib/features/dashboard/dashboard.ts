import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { Dashboard, FlakyTestList, TestPlan } from '../../proxy/dtos';
import { FlakinessLevel } from '../../proxy/enums';
import { DashboardService, FlakyTestService, TestPlanService } from '../../proxy/services';
import { CHART, burnDownChart, velocityChart } from './chart';

@Component({
  selector: 'app-dashboard',
  imports: [FormsModule, FormatDatePipe, TranslatePipe],
  templateUrl: './dashboard.html',
})
export class DashboardComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly flakyService = inject(FlakyTestService);
  private readonly planService = inject(TestPlanService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly flaky = signal<FlakyTestList | null>(null);
  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly loading = signal(true);
  /** Counts the loads, so that a slow answer to an old plan or period cannot replace the current one. */
  private request = 0;
  /** The plan list and the flaky card are secondary calls with their own permissions; without them the card is hidden, not an error. */
  protected readonly canSeePlans = this.auth.can(Permissions.TestPlans.Default);
  protected readonly canSeeFlaky = this.auth.can(Permissions.TestCases.Default);
  protected planId = '';
  protected days = 14;
  protected clearRecovered = false;

  protected readonly chart = CHART;
  protected readonly levelEnum = FlakinessLevel;
  protected readonly dayOptions = [7, 14, 30, 60, 90];

  protected readonly burn = computed(() => {
    const d = this.dashboard();
    return d ? burnDownChart(d.burnDown.points, iso => this.shortDate(iso)) : null;
  });
  protected readonly velocity = computed(() => {
    const d = this.dashboard();
    return d ? velocityChart(d.velocity.points, iso => this.shortDate(iso)) : null;
  });

  ngOnInit(): void {
    if (this.canSeePlans) { this.planService.list().subscribe(r => this.plans.set(r.items)); }
    this.reload();
  }

  protected reload(): void {
    const request = ++this.request;
    this.loading.set(true);
    this.dashboardService.get({ testPlanId: this.planId || null, days: Number(this.days) }).subscribe({
      next: d => {
        if (request !== this.request) { return; }
        this.dashboard.set(d);
        this.loading.set(false);
      },
      error: () => { if (request === this.request) { this.loading.set(false); } },
    });
    this.reloadFlaky();
  }

  private reloadFlaky(): void {
    if (!this.canSeeFlaky) { return; }
    this.flakyService.list({ minimumLevel: FlakinessLevel.Watch, maxResultCount: 50 }).subscribe(f => this.flaky.set(f));
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  /** "Mar 5" in the language of the screen, from the date part of an ISO text. */
  protected shortDate(iso: string): string { return this.i18n.date(iso, this.i18n.lang() === 'vi' ? 'd/M' : 'MMM d'); }

  protected percent(value: number | null): string { return value === null ? this.i18n.t('common.na') : `${value}%`; }

  protected levelClass(level: number): string {
    return level === FlakinessLevel.Flaky ? 'bad' : level === FlakinessLevel.Watch ? 'warn' : 'muted';
  }

  protected scoreWidth(score: number): string { return `${Math.round(score * 100)}%`; }

  protected apply(): void {
    this.flakyService.apply(this.clearRecovered).subscribe(result => {
      this.toast.success(this.i18n.t('dash.applied', { flagged: result.flagged, cleared: result.cleared }));
      this.reloadFlaky();
    });
  }

  protected trendText(trend: number | null): string {
    if (trend === null) { return ''; }
    return `${trend > 0 ? '+' : ''}${trend}%`;
  }
}
