import { Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { SharedStepGroupSummary, TestCase, TestCaseDefect, TestCaseVersion } from '../../proxy/dtos';
import {
  ExecutionType, PriorityLevel, SeverityLevel, TEST_CASE_TRANSITIONS, TestCaseStatus, TestKind, TestLayer,
} from '../../proxy/enums';
import { AttachmentOwnerType } from '../../proxy/enums';
import { SharedStepGroupService, TestCaseService } from '../../proxy/services';
import { AttachmentsComponent } from '../attachments/attachments';
import { TagInputComponent } from '../tags/tag-input';
import { ConfirmService } from '../../core/confirm';

@Component({
  selector: 'app-test-case-detail',
  imports: [FormsModule, ModalComponent, FormatDatePipe, TranslatePipe, AttachmentsComponent, TagInputComponent],
  template: `
    <app-modal [side]="true" [title]="testCase().code + ' - ' + testCase().title" [wide]="true" (closed)="closed.emit()">
      <div class="row" style="margin-bottom:12px">
        <span class="badge" [class]="badgeOf('case', testCase().status)">{{ label(statuses, testCase().status) }}</span>
        <span class="badge" [class]="badgeOf('priority', testCase().priority)">{{ 'detail.priorityBadge' | t: { value: label(priorities, testCase().priority) } }}</span>
        <span class="badge" [class]="badgeOf('severity', testCase().severity)">{{ 'detail.severityBadge' | t: { value: label(severities, testCase().severity) } }}</span>
        <span class="muted">{{ label(kinds, testCase().kind) }} / {{ label(layers, testCase().layer) }} / {{ label(executions, testCase().executionType) }}</span>
        <span class="muted">{{ 'detail.version' | t: { n: testCase().currentVersion } }}</span>
      </div>

      @if (waitingForReview()) {
        <p class="alert warn" data-test="review-banner">{{ 'detail.waitingReview' | t: { n: testCase().currentVersion } }}</p>
      }

      <div class="field">
        <label>{{ 'tags.title' | t }}</label>
        <app-tag-input [tags]="tags()" (tagsChange)="tags.set($event)" (edited)="saveTags($event)" [suggestions]="suggestions()" [readonly]="!auth.can(perm.TestCases.Update)" />
      </div>

      @if (testCase().description) { <p>{{ testCase().description }}</p> }
      <div class="grid-2">
        <div><label>{{ 'form.preconditions' | t }}</label><p>{{ testCase().preconditions || '-' }}</p></div>
        <div><label>{{ 'form.postconditions' | t }}</label><p>{{ testCase().postconditions || '-' }}</p></div>
      </div>

      <h3>{{ 'common.steps' | t }}</h3>
      <table>
        <thead><tr><th style="width:36px">#</th><th>{{ 'form.action' | t }}</th><th>{{ 'form.expected' | t }}</th><th>{{ 'form.testData' | t }}</th></tr></thead>
        <tbody>
          @for (step of testCase().steps; track step.id) {
            <tr>
              <td>{{ step.stepOrder }}</td>
              <td>
                @if (step.sharedStepGroupId && step.sharedStepGroupName) { <span class="chip small">{{ 'shared.fromGroup' | t: { name: step.sharedStepGroupName } }}</span> }
                {{ step.action }}
              </td>
              <td>{{ step.expectedResult }}</td><td>{{ step.testData }}</td>
            </tr>
          }
        </tbody>
      </table>

      <h3>{{ 'shared.sectionTitle' | t }}</h3>
      @if (groupsUsed().length) {
        <table>
          <tbody>
            @for (g of groupsUsed(); track g.id) {
              <tr>
                <td>{{ g.name }}<div class="muted">{{ 'shared.copiedLine' | t: { count: g.count, revision: g.revision } }}</div></td>
                <td><span class="badge" [class]="g.outdated ? 'warn' : 'ok'">{{ (g.outdated ? 'shared.behind' : 'shared.upToDate') | t }}</span></td>
                <td class="right nowrap">
                  @if (auth.can(perm.TestCases.Update)) {
                    @if (g.outdated) { <button type="button" class="btn sm primary" (click)="refreshGroup(g.id)">{{ 'shared.update' | t }}</button> }
                    <button type="button" class="btn sm" (click)="detachGroup(g.id)">{{ 'shared.detach' | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">{{ 'shared.notUsed' | t }}</p>
      }
      @if (auth.can(perm.TestCases.Update) && auth.can(perm.SharedSteps.Default) && library().length) {
        <div class="row">
          <select [(ngModel)]="pick" name="pick" [attr.aria-label]="'shared.pick' | t" style="width: auto; min-width: 220px">
            <option value="">{{ 'shared.pickPlaceholder' | t }}</option>
            @for (g of library(); track g.id) { <option [value]="g.id">{{ g.name }} ({{ 'shared.stepsCount' | t: { n: g.stepCount } }})</option> }
          </select>
          <button type="button" class="btn" [disabled]="!pick" (click)="insertGroup()">{{ 'shared.insert' | t }}</button>
        </div>
      }

      <h3>{{ 'detail.history' | t }}</h3>
      @if (versions().length) {
        <table>
          <thead><tr><th>{{ 'common.version' | t }}</th><th>{{ 'detail.frozenTitle' | t }}</th><th>{{ 'common.steps' | t }}</th><th>{{ 'detail.change' | t }}</th><th>{{ 'detail.created' | t }}</th></tr></thead>
          <tbody>
            @for (v of versions(); track v.id) {
              <tr><td>v{{ v.versionNumber }}</td><td>{{ v.title }}</td><td>{{ v.steps.length }}</td><td>{{ v.changeSummary || '-' }}</td><td>{{ v.creationTime | fdate: 'short' }}</td></tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">{{ 'detail.noVersion' | t }}</p>
      }

      <h3>{{ 'att.title' | t }}</h3>
      <app-attachments [ownerType]="ownerType" [ownerId]="testCase().id" [canWrite]="auth.can(perm.TestCases.Update)" />

      <h3>{{ 'detail.defects' | t }}</h3>
      @if (defects().length) {
        <table>
          <thead><tr><th>{{ 'detail.issue' | t }}</th><th>{{ 'common.severity' | t }}</th><th>{{ 'detail.state' | t }}</th><th>{{ 'detail.run' | t }}</th><th>{{ 'detail.attempt' | t }}</th></tr></thead>
          <tbody>
            @for (d of defects(); track d.defectLinkId) {
              <tr>
                <td>@if (d.issueUrl) { <a [href]="d.issueUrl" target="_blank" rel="noopener">{{ d.externalSystem }} {{ d.issueKey }}</a> } @else { {{ d.externalSystem }} {{ d.issueKey }} }</td>
                <td><span class="badge" [class]="badgeOf('severity', d.severity)">{{ label(severities, d.severity) }}</span></td>
                <td><span class="badge" [class]="d.isResolved ? 'ok' : 'bad'">{{ (d.isResolved ? 'common.resolved' : 'common.open') | t }}</span></td>
                <td>{{ d.testRunTitle }} ({{ d.environment }})</td>
                <td>{{ 'detail.attemptOn' | t: { n: d.attemptNumber, version: d.versionNumber } }}</td>
              </tr>
            }
          </tbody>
        </table>
      } @else {
        <p class="muted">{{ 'common.none' | t }}</p>
      }

      <ng-container slot="footer">
        @for (target of transitions(); track target) {
          <button type="button" class="btn" (click)="changeStatus(target)">{{ actionLabel(target) }}</button>
        }
        @if (auth.can(perm.TestCases.Delete)) { <button type="button" class="btn danger" (click)="remove()">{{ 'common.delete' | t }}</button> }
        @if (auth.can(perm.TestCases.Update)) { <button type="button" class="btn primary" (click)="edit.emit(testCase())">{{ 'common.edit' | t }}</button> }
      </ng-container>
    </app-modal>
  `,
})
export class TestCaseDetailComponent {
  private readonly service = inject(TestCaseService);
  private readonly groupService = inject(SharedStepGroupService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  readonly testCase = input.required<TestCase>();
  readonly edit = output<TestCase>();
  readonly changed = output<void>();
  /** The test case after its shared steps were inserted, refreshed or detached; the dialog shows it. */
  readonly stepsChanged = output<TestCase>();
  /** The tags were saved; the list behind the dialog shows them. */
  readonly tagsSaved = output<void>();
  readonly closed = output<void>();

  protected readonly library = signal<SharedStepGroupSummary[]>([]);
  protected pick = '';
  /** The groups the steps of this test case were copied from, with how far behind each copy is. */
  protected readonly groupsUsed = computed(() => {
    const groups = new Map<string, { id: string; name: string; count: number; revision: number; outdated: boolean }>();
    for (const step of this.testCase().steps) {
      if (!step.sharedStepGroupId) { continue; }
      const known = groups.get(step.sharedStepGroupId);
      const revision = step.sharedStepRevision ?? 0;
      groups.set(step.sharedStepGroupId, {
        id: step.sharedStepGroupId,
        name: step.sharedStepGroupName ?? this.i18n.t('shared.unknownGroup'),
        count: (known?.count ?? 0) + 1,
        revision: Math.min(known?.revision ?? revision, revision),
        outdated: (known?.outdated ?? false) || !!step.sharedStepOutdated,
      });
    }
    return [...groups.values()];
  });
  protected readonly tags = signal<string[]>([]);
  protected readonly suggestions = signal<string[]>([]);
  protected readonly versions = signal<TestCaseVersion[]>([]);
  protected readonly defects = signal<TestCaseDefect[]>([]);

  protected readonly statuses = TestCaseStatus;
  protected readonly priorities = PriorityLevel;
  protected readonly severities = SeverityLevel;
  protected readonly kinds = TestKind;
  protected readonly layers = TestLayer;
  protected readonly executions = ExecutionType;
  protected readonly badgeOf = badge;
  protected readonly ownerType = AttachmentOwnerType.TestCase;

  constructor() {
    // The dialog shows a new test case when steps are inserted or refreshed (and a new version may exist), so it reloads then.
    effect(() => {
      const testCase = this.testCase();
      untracked(() => this.load(testCase));
    });
    if (!this.auth.can(Permissions.SharedSteps.Default)) { return; }
    this.groupService.list().subscribe({ next: groups => this.library.set(groups), error: () => undefined });
  }

  private load(testCase: TestCase): void {
    this.tags.set([...testCase.tags]);
    this.service.tags().subscribe(all => this.suggestions.set(all.map(t => t.name)));
    this.service.versions(testCase.id).subscribe(v => this.versions.set(v));
    this.service.defects(testCase.id).subscribe(d => this.defects.set(d));
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  /** Tags are labels: saved on their own, with no new version, and the dialog stays open. */
  protected saveTags(tags: string[]): void {
    this.service.setTags(this.testCase().id, tags).subscribe({
      next: saved => { this.tags.set(saved.tags); this.tagsSaved.emit(); },
      error: () => this.tags.set([...this.testCase().tags]),
    });
  }

  /** The moves the domain allows, minus those the permissions rule out (approving needs its own permission). */
  protected transitions(): TestCaseStatus[] {
    if (!this.auth.can(Permissions.TestCases.Update)) { return []; }
    return TEST_CASE_TRANSITIONS[this.testCase().status]
      .filter(target => target !== TestCaseStatus.Approved || this.auth.can(Permissions.TestCases.Approve));
  }

  protected actionLabel(target: TestCaseStatus): string {
    switch (target) {
      case TestCaseStatus.UnderReview: return this.i18n.t('detail.submitReview');
      case TestCaseStatus.Approved: return this.i18n.t('detail.approve');
      case TestCaseStatus.Deprecated: return this.i18n.t('detail.deprecate');
      default: return this.i18n.t('detail.backToDraft');
    }
  }

  // ---- shared steps
  /** An approved test case gets a new version from these, so the person is asked first; the action runs when there is nothing to ask or the answer is yes. */
  private withVersionConfirmed(action: () => void): void {
    if (this.testCase().status !== TestCaseStatus.Approved) { action(); return; }
    this.confirmer.ask({ message: this.i18n.t('shared.confirmVersion'), confirmText: this.i18n.t('confirm.continue') }).subscribe(ok => { if (ok) { action(); } });
  }

  protected insertGroup(): void {
    if (!this.pick) { return; }
    const pick = this.pick;
    this.withVersionConfirmed(() => this.service.insertSharedSteps(this.testCase().id, pick).subscribe(saved => {
      this.pick = '';
      this.toast.success(this.i18n.t('shared.inserted'));
      this.stepsChanged.emit(saved);
    }));
  }

  protected refreshGroup(groupId: string): void {
    this.withVersionConfirmed(() => this.service.refreshSharedSteps(this.testCase().id, groupId).subscribe(saved => {
      this.toast.success(this.i18n.t('shared.refreshed'));
      this.stepsChanged.emit(saved);
    }));
  }

  protected detachGroup(groupId: string): void {
    this.confirmer.ask({ message: this.i18n.t('shared.confirmDetach'), confirmText: this.i18n.t('shared.detach'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.service.detachSharedSteps(this.testCase().id, groupId).subscribe(saved => {
        this.toast.success(this.i18n.t('shared.detached'));
        this.stepsChanged.emit(saved);
      });
    });
  }

  /** An edit of an approved test case is waiting for its review: the runs use the last approved version meanwhile. */
  protected waitingForReview(): boolean {
    return this.testCase().status === TestCaseStatus.UnderReview && this.testCase().currentVersion > 0;
  }

  protected changeStatus(target: TestCaseStatus): void {
    // Approving a test case that was approved before makes its next version, so the person may say what changed (optional).
    if (target === TestCaseStatus.Approved && this.testCase().currentVersion > 0) {
      this.confirmer.askText({
        title: this.i18n.t('detail.approveAgainTitle'), message: this.i18n.t('detail.changeNote'), placeholder: this.i18n.t('detail.changeNoteHint'),
        confirmText: this.i18n.t('detail.approve'), optional: true,
      }).subscribe(note => { if (note !== null) { this.moveTo(target, note || null); } });
      return;
    }
    this.moveTo(target, null);
  }

  private moveTo(target: TestCaseStatus, changeSummary: string | null): void {
    this.service.changeStatus(this.testCase().id, target, changeSummary).subscribe(() => {
      this.toast.success(this.i18n.t('detail.statusNow', { status: this.label(TestCaseStatus, target) }));
      this.changed.emit();
    });
  }

  protected remove(): void {
    const testCase = this.testCase();
    this.confirmer.ask({ message: this.i18n.t('detail.confirmDelete', { code: testCase.code }), confirmText: this.i18n.t('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.service.delete(testCase.id).subscribe(() => {
        this.toast.success(this.i18n.t('detail.deleted'));
        this.changed.emit();
      });
    });
  }
}
