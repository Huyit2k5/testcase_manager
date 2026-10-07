import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { ModalComponent } from '../../core/modal';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { TestCase, TestCaseDefect, TestCaseVersion } from '../../proxy/dtos';
import {
  ExecutionType, PriorityLevel, SeverityLevel, TEST_CASE_TRANSITIONS, TestCaseStatus, TestKind, TestLayer,
} from '../../proxy/enums';
import { AttachmentOwnerType } from '../../proxy/enums';
import { TestCaseService } from '../../proxy/services';
import { AttachmentsComponent } from '../attachments/attachments';

@Component({
  selector: 'app-test-case-detail',
  imports: [ModalComponent, FormatDatePipe, TranslatePipe, AttachmentsComponent],
  template: `
    <app-modal [title]="testCase().code + ' - ' + testCase().title" [wide]="true" (closed)="closed.emit()">
      <div class="row" style="margin-bottom:12px">
        <span class="badge" [class]="badgeOf('case', testCase().status)">{{ label(statuses, testCase().status) }}</span>
        <span class="badge" [class]="badgeOf('priority', testCase().priority)">{{ 'detail.priorityBadge' | t: { value: label(priorities, testCase().priority) } }}</span>
        <span class="badge" [class]="badgeOf('severity', testCase().severity)">{{ 'detail.severityBadge' | t: { value: label(severities, testCase().severity) } }}</span>
        <span class="muted">{{ label(kinds, testCase().kind) }} / {{ label(layers, testCase().layer) }} / {{ label(executions, testCase().executionType) }}</span>
        <span class="muted">{{ 'detail.version' | t: { n: testCase().currentVersion } }}</span>
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
            <tr><td>{{ step.stepOrder }}</td><td>{{ step.action }}</td><td>{{ step.expectedResult }}</td><td>{{ step.testData }}</td></tr>
          }
        </tbody>
      </table>

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
export class TestCaseDetailComponent implements OnInit {
  private readonly service = inject(TestCaseService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  readonly testCase = input.required<TestCase>();
  readonly edit = output<TestCase>();
  readonly changed = output<void>();
  readonly closed = output<void>();

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

  ngOnInit(): void {
    const id = this.testCase().id;
    this.service.versions(id).subscribe(v => this.versions.set(v));
    this.service.defects(id).subscribe(d => this.defects.set(d));
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

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

  protected changeStatus(target: TestCaseStatus): void {
    this.service.changeStatus(this.testCase().id, target).subscribe(() => {
      this.toast.success(this.i18n.t('detail.statusNow', { status: this.label(TestCaseStatus, target) }));
      this.changed.emit();
    });
  }

  protected remove(): void {
    if (!confirm(this.i18n.t('detail.confirmDelete', { code: this.testCase().code }))) {
      return;
    }
    this.service.delete(this.testCase().id).subscribe(() => {
      this.toast.success(this.i18n.t('detail.deleted'));
      this.changed.emit();
    });
  }
}
