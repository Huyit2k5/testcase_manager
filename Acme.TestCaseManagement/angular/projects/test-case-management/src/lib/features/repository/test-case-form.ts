import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModalComponent } from '../../core/modal';
import { ToastService } from '../../core/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { SaveTestCase, SuggestedStep, TestCase, TestStep } from '../../proxy/dtos';
import { ExecutionType, PriorityLevel, SeverityLevel, TestKind, TestLayer, enumOptions } from '../../proxy/enums';
import { TestCaseService } from '../../proxy/services';
import { StepSuggestionsComponent } from '../suggestions/step-suggestions';
import { TagInputComponent } from '../tags/tag-input';
import { SuiteOption } from './suite-options';

@Component({
  selector: 'app-test-case-form',
  imports: [FormsModule, TranslatePipe, ModalComponent, TagInputComponent, StepSuggestionsComponent],
  template: `
    <app-modal [side]="true" [title]="existing() ? ('form.editCase' | t: { code: existing()!.code }) : ('form.newCase' | t)" [wide]="true" (closed)="closed.emit()">
      <form id="tc-form" (ngSubmit)="save()">
        <div class="form-grid">
          <div class="field">
            <label for="tc-suite">{{ 'form.suite' | t }}</label>
            <select id="tc-suite" name="suite" required [(ngModel)]="model.suiteId">
              @for (suite of suites(); track suite.id) {
                <option [value]="suite.id">{{ suite.label }}</option>
              }
            </select>
          </div>
          <div class="field">
            <label for="tc-code">{{ 'common.code' | t }}</label>
            <input id="tc-code" name="code" required maxlength="64" [(ngModel)]="model.code" [placeholder]="'form.codePlaceholder' | t" />
          </div>
        </div>
        <div class="field">
          <label for="tc-title">{{ 'common.title' | t }}</label>
          <input id="tc-title" name="title" required maxlength="256" [(ngModel)]="model.title" />
        </div>
        <div class="field">
          <label for="tc-desc">{{ 'common.description' | t }}</label>
          <textarea id="tc-desc" name="description" [(ngModel)]="model.description"></textarea>
        </div>
        <div class="form-grid">
          <div class="field">
            <label for="tc-pre">{{ 'form.preconditions' | t }}</label>
            <textarea id="tc-pre" name="preconditions" [(ngModel)]="model.preconditions"></textarea>
          </div>
          <div class="field">
            <label for="tc-post">{{ 'form.postconditions' | t }}</label>
            <textarea id="tc-post" name="postconditions" [(ngModel)]="model.postconditions"></textarea>
          </div>
        </div>
        <div class="form-grid">
          <div class="field">
            <label for="tc-priority">{{ 'common.priority' | t }}</label>
            <select id="tc-priority" name="priority" [(ngModel)]="model.priority">
              @for (o of priorities; track o.value) { <option [ngValue]="o.value">{{ label(priorityEnum, o.value) }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="tc-severity">{{ 'common.severity' | t }}</label>
            <select id="tc-severity" name="severity" [(ngModel)]="model.severity">
              @for (o of severities; track o.value) { <option [ngValue]="o.value">{{ label(severityEnum, o.value) }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="tc-kind">{{ 'form.kind' | t }}</label>
            <select id="tc-kind" name="kind" [(ngModel)]="model.kind">
              @for (o of kinds; track o.value) { <option [ngValue]="o.value">{{ label(kindEnum, o.value) }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="tc-layer">{{ 'form.layer' | t }}</label>
            <select id="tc-layer" name="layer" [(ngModel)]="model.layer">
              @for (o of layers; track o.value) { <option [ngValue]="o.value">{{ label(layerEnum, o.value) }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="tc-exec">{{ 'form.execution' | t }}</label>
            <select id="tc-exec" name="executionType" [(ngModel)]="model.executionType">
              @for (o of executionTypes; track o.value) { <option [ngValue]="o.value">{{ label(executionEnum, o.value) }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="tc-auto">{{ 'form.automationId' | t }}</label>
            <input id="tc-auto" name="automationId" [(ngModel)]="model.automationId" />
          </div>
        </div>
        <div class="field">
          <label class="check"><input type="checkbox" name="flaky" [(ngModel)]="model.isFlaky" /> {{ 'form.flaky' | t }}</label>
          <div class="field">
            <label>{{ 'tags.title' | t }}</label>
            <app-tag-input [(tags)]="tags" [suggestions]="suggestions()" />
          </div>
        </div>

        <h3>{{ 'common.steps' | t }}</h3>
        <table>
          <thead><tr><th style="width:36px">#</th><th>{{ 'form.action' | t }}</th><th>{{ 'form.expected' | t }}</th><th>{{ 'form.testData' | t }}</th><th style="width:96px"></th></tr></thead>
          <tbody>
            @for (step of model.steps; track $index; let i = $index) {
              <tr>
                <td>{{ i + 1 }}</td>
                <td>
                  @if (step.sharedStepGroupId) { <span class="chip small" [title]="'shared.linkedHint' | t">{{ 'shared.fromGroup' | t: { name: step.sharedStepGroupName ?? '' } }}</span> }
                  <textarea [name]="'action' + i" required [(ngModel)]="step.action" [readOnly]="!!step.sharedStepGroupId" [attr.aria-label]="'form.action' | t"></textarea>
                </td>
                <td><textarea [name]="'expected' + i" required [(ngModel)]="step.expectedResult" [readOnly]="!!step.sharedStepGroupId" [attr.aria-label]="'form.expected' | t"></textarea></td>
                <td><textarea [name]="'data' + i" [(ngModel)]="step.testData" [readOnly]="!!step.sharedStepGroupId" [attr.aria-label]="'form.testData' | t"></textarea></td>
                <td class="nowrap">
                  <button type="button" class="btn sm" [disabled]="i === 0" (click)="move(i, -1)" [attr.aria-label]="'form.moveUp' | t">&uarr;</button>
                  <button type="button" class="btn sm" [disabled]="i === model.steps.length - 1" (click)="move(i, 1)" [attr.aria-label]="'form.moveDown' | t">&darr;</button>
                  <button type="button" class="btn sm danger" (click)="removeStep(i)" [attr.aria-label]="'form.removeStep' | t">&times;</button>
                </td>
              </tr>
            }
          </tbody>
        </table>
        <div class="row" style="margin-top:8px">
          <button type="button" class="btn sm" (click)="addStep()">{{ 'form.addStep' | t }}</button>
          <app-step-suggestions [title]="model.title" [description]="model.description" (chosen)="addSuggested($event)" />
        </div>

        @if (existing() && existing()!.status === 2) {
          <div class="field" style="margin-top:12px">
            <label for="tc-summary">{{ 'form.changeSummary' | t }}</label>
            <input id="tc-summary" name="changeSummary" [(ngModel)]="model.changeSummary" />
          </div>
        }
      </form>
      <ng-container slot="footer">
        <button type="button" class="btn" (click)="closed.emit()">{{ 'common.cancel' | t }}</button>
        <button type="submit" form="tc-form" class="btn primary" [disabled]="saving()">{{ 'common.save' | t }}</button>
      </ng-container>
    </app-modal>
  `,
})
export class TestCaseFormComponent implements OnInit {
  private readonly service = inject(TestCaseService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  readonly suites = input.required<SuiteOption[]>();
  readonly defaultSuiteId = input<string | null>(null);
  /** The test case being edited, or null to create one. */
  readonly existing = input<TestCase | null>(null);
  readonly saved = output<TestCase>();
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly priorities = enumOptions(PriorityLevel);
  protected readonly priorityEnum = PriorityLevel;
  protected readonly severityEnum = SeverityLevel;
  protected readonly kindEnum = TestKind;
  protected readonly layerEnum = TestLayer;
  protected readonly executionEnum = ExecutionType;

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }
  protected readonly severities = enumOptions(SeverityLevel);
  protected readonly kinds = enumOptions(TestKind);
  protected readonly layers = enumOptions(TestLayer);
  protected readonly executionTypes = enumOptions(ExecutionType);

  protected model: SaveTestCase = {
    suiteId: '', code: '', title: '', description: '', preconditions: '', postconditions: '',
    priority: PriorityLevel.Medium, severity: SeverityLevel.Medium, executionType: ExecutionType.Manual,
    kind: TestKind.Functional, layer: TestLayer.Acceptance, automationId: '', isFlaky: false, steps: [], changeSummary: '',
  };

  protected tags: string[] = [];
  protected readonly suggestions = signal<string[]>([]);

  ngOnInit(): void {
    this.service.tags().subscribe(all => this.suggestions.set(all.map(t => t.name)));
    const existing = this.existing();
    if (existing) {
      this.tags = [...existing.tags];
      this.model = {
        suiteId: existing.suiteId, code: existing.code, title: existing.title, description: existing.description,
        preconditions: existing.preconditions, postconditions: existing.postconditions, priority: existing.priority,
        severity: existing.severity, executionType: existing.executionType, kind: existing.kind, layer: existing.layer,
        automationId: existing.automationId, isFlaky: existing.isFlaky, changeSummary: '',
        steps: existing.steps.map(s => ({
          id: s.id, action: s.action, expectedResult: s.expectedResult, testData: s.testData,
          sharedStepGroupId: s.sharedStepGroupId, sharedStepGroupName: s.sharedStepGroupName,
        })),
      };
    } else {
      this.model.suiteId = this.defaultSuiteId() ?? this.suites()[0]?.id ?? '';
      this.addStep();
    }
  }

  protected addStep(): void {
    this.model.steps.push({ action: '', expectedResult: '', testData: '' });
  }

  /** Adds the steps the user picked from the AI proposals; a step that is still blank (the one a new form starts with) makes room for them. */
  protected addSuggested(steps: SuggestedStep[]): void {
    const last = this.model.steps[this.model.steps.length - 1];
    if (last && !last.sharedStepGroupId && !last.action.trim() && !last.expectedResult.trim() && !last.testData?.trim()) {
      this.model.steps.pop();
    }
    for (const step of steps) {
      this.model.steps.push({ action: step.action, expectedResult: step.expectedResult, testData: step.testData ?? '' });
    }
  }

  protected removeStep(index: number): void {
    this.model.steps.splice(index, 1);
  }

  protected move(index: number, delta: number): void {
    const target = index + delta;
    const steps = this.model.steps;
    [steps[index], steps[target]] = [steps[target] as TestStep, steps[index] as TestStep];
  }

  protected save(): void {
    // Enter in a field submits the form even while the button is disabled, so the guard is here.
    if (this.saving()) { return; }
    if (this.model.steps.length === 0) {
      this.toast.error(this.i18n.t('form.needStep'));
      return;
    }
    const body: SaveTestCase = {
      ...this.model,
      description: this.model.description || null,
      preconditions: this.model.preconditions || null,
      postconditions: this.model.postconditions || null,
      automationId: this.model.automationId || null,
      tags: this.tags,
      changeSummary: this.model.changeSummary || null,
      steps: this.model.steps.map(s => ({ id: s.id ?? null, action: s.action, expectedResult: s.expectedResult, testData: s.testData || null })),
    };
    this.saving.set(true);
    const existing = this.existing();
    const request = existing ? this.service.update(existing.id, body) : this.service.create(body);
    request.subscribe({
      next: saved => {
        this.saving.set(false);
        this.toast.success(this.i18n.t(existing ? 'form.updated' : 'form.created'));
        this.saved.emit(saved);
      },
      error: () => this.saving.set(false),
    });
  }
}
