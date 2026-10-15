import { ProjectSwitchComponent } from '../projects/project-switch';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { UserNames } from '../../core/users';
import { pct } from '../../core/ui';
import { Dashboard, FlakyTestList, SprintTrend, TestPlan, TesterWorkload, Workload } from '../../proxy/dtos';
import { FlakinessLevel } from '../../proxy/enums';
import { DashboardService, FlakyTestService, TestPlanService } from '../../proxy/services';
import { CHART, burnDownChart, trendChart, velocityChart } from './chart';

@Component({
  selector: 'app-dashboard',
  imports: [ProjectSwitchComponent, FormsModule, FormatDatePipe, TranslatePipe],
  templateUrl: './dashboard.html',
})
export class DashboardComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly flakyService = inject(FlakyTestService);
  private readonly planService = inject(TestPlanService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly users = inject(UserNames);
  protected readonly perm = Permissions;

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly flaky = signal<FlakyTestList | null>(null);
  protected readonly trend = signal<SprintTrend | null>(null);
  protected readonly workload = signal<Workload | null>(null);
  protected readonly plans = signal<TestPlan[]>([]);
  protected readonly loading = signal(true);
  /** Counts the loads, so that a slow answer to an old plan or period cannot replace the current one. */
  private request = 0;
  /** The plan list and the flaky card are secondary calls with their own permissions; without them the card is hidden, not an error. */
  protected readonly canSeePlans = this.auth.can(Permissions.TestPlans.Default);
  protected readonly canSeeFlaky = this.auth.can(Permissions.TestCases.Default);
  /** The workload names people, so it is for those who assign the work. */
  protected readonly canSeeWorkload = this.auth.can(Permissions.TestPlans.Manage);
  protected sprints = 8;
  protected readonly sprintOptions = [4, 8, 12, 20];
  private trendRequest = 0;
  private workloadRequest = 0;
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

  protected readonly trendLine = computed(() => {
    const trend = this.trend();
    return trend && trend.points.length > 0 ? trendChart(trend.points) : null;
  });

  ngOnInit(): void {
    if (this.canSeeWorkload) { this.users.load(); }
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
    this.reloadTrend();
    this.reloadWorkload();
  }

  protected reloadTrend(): void {
    const request = ++this.trendRequest;
    this.dashboardService.trend(Number(this.sprints)).subscribe(trend => { if (request === this.trendRequest) { this.trend.set(trend); } });
  }

  private reloadWorkload(): void {
    if (!this.canSeeWorkload) { return; }
    const request = ++this.workloadRequest;
    this.dashboardService.workload({ testPlanId: this.planId || null, days: Number(this.days) })
      .subscribe(workload => { if (request === this.workloadRequest) { this.workload.set(workload); } });
  }

  /** The name of a tester; attempts with no known creator are those of a pipeline, and an id the directory does not know is said so. */
  protected testerName(row: TesterWorkload): string {
    if (!row.userId) { return this.i18n.t('dash.pipeline'); }
    return this.users.nameOf(row.userId) ?? this.i18n.t('run.unknownUser');
  }

  /** "1h 05m", "12m" or "40s". */
  protected duration(seconds: number): string {
    if (seconds <= 0) { return '-'; }
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    if (hours > 0) { return `${hours}h ${String(minutes).padStart(2, '0')}m`; }
    return minutes > 0 ? `${minutes}m` : `${seconds}s`;
  }

  /** "+4.5 pts" or "-3 pts"; empty when there is no change to tell. */
  protected points(change: number | null): string {
    return change === null ? '' : `${change > 0 ? '+' : ''}${change} ${this.i18n.t('dash.pts')}`;
  }

  /** Better is up for a rate and down for defects per 100 tests. */
  protected changeClass(change: number | null, lowerIsBetter = false): string {
    if (change === null || change === 0) { return 'muted'; }
    return (change > 0) !== lowerIsBetter ? 'good' : 'low';
  }

  private reloadFlaky(): void {
    if (!this.canSeeFlaky) { return; }
    this.flakyService.list({ minimumLevel: FlakinessLevel.Watch, maxResultCount: 50 }).subscribe(f => this.flaky.set(f));
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  /** "Mar 5" in the language of the screen, from the date part of an ISO text. */
  protected shortDate(iso: string): string { return this.i18n.date(iso, this.i18n.lang() === 'vi' ? 'd/M' : 'MMM d'); }

  /** The pace and the day the rest would be done: the tooltip of the burn-down line, not a paragraph. */
  protected paceText(b: { itemsPerDay: number; projectedFinish: string | null }): string {
    const pace = this.i18n.t('dash.pace', { pace: b.itemsPerDay });
    return b.projectedFinish ? `${pace} ${this.i18n.t('dash.projected', { date: this.i18n.date(b.projectedFinish, 'mediumDate') })}` : pace;
  }

  protected velocityText(v: { totalAttempts: number; averagePerDay: number }, days: number): string {
    return this.i18n.t('dash.velocityLine', { total: v.totalAttempts, avg: v.averagePerDay, days });
  }

  /** What the table of the trend leaves out of a row (the defects and their density), as the tooltip of the row. */
  protected sprintTip(p: { defects: number; openDefects: number; defectsPer100Executed: number | null }): string {
    return this.i18n.t('dash.sprintTip', {
      defects: p.defects, open: p.openDefects, per: p.defectsPer100Executed === null ? this.i18n.t('common.na') : p.defectsPer100Executed,
    });
  }

  /** What the table of the workload leaves out of a row (blocked or skipped, the tests run, the days worked), as the tooltip of the row. */
  protected testerTip(t: { blocked: number; skipped: number; itemsExecuted: number; activeDays: number }): string {
    return this.i18n.t('dash.testerTip', { other: t.blocked + t.skipped, items: t.itemsExecuted, days: t.activeDays });
  }

  protected percent(value: number | null): string { return value === null ? this.i18n.t('common.na') : pct(value); }

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
