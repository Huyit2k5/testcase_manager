import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { TestCaseVersion } from '../../proxy/dtos';
import { ExecutionType, PriorityLevel, SeverityLevel, TestKind, TestLayer } from '../../proxy/enums';
import { FieldChange, Segment, diffVersions, wordDiff } from './version-diff';

const ENUMS: Record<string, object> = {
  priority: PriorityLevel, severity: SeverityLevel, kind: TestKind, layer: TestLayer, executionType: ExecutionType,
};

/** Two versions of a test case side by side: the fields and the steps that differ, with the words that changed marked. */
@Component({
  selector: 'app-version-compare',
  imports: [FormsModule, TranslatePipe],
  template: `
    @if (versions().length < 2) {
      <p class="muted" data-test="compare-need-two">{{ 'compare.needTwo' | t }}</p>
    } @else {
      <div class="row" style="margin-bottom: 8px">
        <label for="compare-from">{{ 'compare.from' | t }}</label>
        <select id="compare-from" name="compare-from" [ngModel]="fromNumber()" (ngModelChange)="fromChoice.set($event)" style="width: auto">
          @for (v of versions(); track v.id) { <option [ngValue]="v.versionNumber">v{{ v.versionNumber }}</option> }
        </select>
        <label for="compare-to">{{ 'compare.to' | t }}</label>
        <select id="compare-to" name="compare-to" [ngModel]="toNumber()" (ngModelChange)="toChoice.set($event)" style="width: auto">
          @for (v of versions(); track v.id) { <option [ngValue]="v.versionNumber">v{{ v.versionNumber }}</option> }
        </select>
      </div>

      @if (!from() || !to() || from() === to()) {
        <p class="muted" data-test="compare-same-version">{{ 'compare.pickTwo' | t }}</p>
      } @else if (diff(); as d) {
        @if (to()!.changeSummary) { <p class="muted" data-test="compare-summary">{{ 'compare.summary' | t: { text: to()!.changeSummary } }}</p> }
        @if (d.identical) {
          <p class="alert" data-test="compare-identical">{{ 'compare.identical' | t }}</p>
        } @else {
          @if (d.fields.length) {
            <table data-test="compare-fields">
              <thead><tr><th style="width: 160px">{{ 'compare.field' | t }}</th><th>{{ 'compare.change' | t }}</th></tr></thead>
              <tbody>
                @for (f of d.fields; track f.field) {
                  <tr>
                    <td>{{ ('field.' + f.field) | t }}</td>
                    <td>
                      @if (f.type === 'text') {
                        @for (s of words(f); track $index) { <span [class]="segmentClass(s)">{{ s.text }}</span> }
                      } @else {
                        <span class="diff-del">{{ enumLabel(f, f.before) }}</span> → <span class="diff-ins">{{ enumLabel(f, f.after) }}</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }

          <table data-test="compare-steps" style="margin-top: 10px">
            <thead><tr><th style="width: 90px">{{ 'compare.step' | t }}</th><th>{{ 'form.action' | t }}</th><th>{{ 'form.expected' | t }}</th><th>{{ 'form.testData' | t }}</th></tr></thead>
            <tbody>
              @for (s of d.steps; track $index) {
                <tr [class]="'diff-row ' + s.status" [attr.data-status]="s.status">
                  <td><span class="chip small">{{ ('compare.status.' + s.status) | t }}</span></td>
                  @if (s.status === 'added') {
                    <td><span class="diff-ins">{{ s.after!.action }}</span></td><td><span class="diff-ins">{{ s.after!.expectedResult }}</span></td><td>@if (s.after!.testData) { <span class="diff-ins">{{ s.after!.testData }}</span> }</td>
                  } @else if (s.status === 'removed') {
                    <td><span class="diff-del">{{ s.before!.action }}</span></td><td><span class="diff-del">{{ s.before!.expectedResult }}</span></td><td>@if (s.before!.testData) { <span class="diff-del">{{ s.before!.testData }}</span> }</td>
                  } @else if (s.status === 'changed') {
                    <td>@for (p of stepWords(s.before!.action, s.after!.action); track $index) { <span [class]="segmentClass(p)">{{ p.text }}</span> }</td>
                    <td>@for (p of stepWords(s.before!.expectedResult, s.after!.expectedResult); track $index) { <span [class]="segmentClass(p)">{{ p.text }}</span> }</td>
                    <td>@for (p of stepWords(s.before!.testData, s.after!.testData); track $index) { <span [class]="segmentClass(p)">{{ p.text }}</span> }</td>
                  } @else {
                    <td>{{ s.after!.action }}</td><td>{{ s.after!.expectedResult }}</td><td>{{ s.after!.testData }}</td>
                  }
                </tr>
              }
            </tbody>
          </table>
        }
      }
    }
  `,
})
export class VersionCompareComponent {
  private readonly i18n = inject(I18nService);

  /** The versions of the test case, newest first. */
  readonly versions = input.required<TestCaseVersion[]>();

  /** What the person chose; null until they choose, and then the newest version against the one before it. */
  protected readonly fromChoice = signal<number | null>(null);
  protected readonly toChoice = signal<number | null>(null);

  private readonly known = (n: number | null) => n !== null && this.versions().some(v => v.versionNumber === n);
  protected readonly fromNumber = computed(() => (this.known(this.fromChoice()) ? this.fromChoice() : this.versions()[1]?.versionNumber ?? null));
  protected readonly toNumber = computed(() => (this.known(this.toChoice()) ? this.toChoice() : this.versions()[0]?.versionNumber ?? null));

  protected readonly from = computed(() => this.versions().find(v => v.versionNumber === this.fromNumber()) ?? null);
  protected readonly to = computed(() => this.versions().find(v => v.versionNumber === this.toNumber()) ?? null);
  protected readonly diff = computed(() => (this.from() && this.to() ? diffVersions(this.from()!, this.to()!) : null));

  protected words(change: FieldChange): Segment[] { return wordDiff(String(change.before ?? ''), String(change.after ?? '')); }

  protected stepWords(before: string | null | undefined, after: string | null | undefined): Segment[] { return wordDiff((before ?? '').trim(), (after ?? '').trim()); }

  protected segmentClass(segment: Segment): string { return segment.kind === 'added' ? 'diff-ins' : segment.kind === 'removed' ? 'diff-del' : ''; }

  protected enumLabel(change: FieldChange, value: string | number | null): string {
    return typeof value === 'number' ? this.i18n.enumText(ENUMS[change.field], value) : '';
  }
}
