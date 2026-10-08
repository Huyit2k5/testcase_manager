import { Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { ModalComponent } from '../../core/modal';
import { StepSuggestionStatus, SuggestedStep } from '../../proxy/dtos';
import { StepSuggestionService } from '../../proxy/services';

const MIN_LENGTH = 10;

interface Proposal { step: SuggestedStep; picked: boolean }

/**
 * The "Suggest steps with AI" button of the test case form and its dialog. The button shows only when the host has an AI model
 * configured and the user may ask; the proposals are only that: the user picks the ones they want and they are added to the form,
 * which saves nothing until the test case is saved.
 */
@Component({
  selector: 'app-step-suggestions',
  imports: [FormsModule, TranslatePipe, ModalComponent],
  template: `
    @if (status()?.enabled) {
      <button type="button" class="btn sm" data-test="suggest-open" (click)="show()">{{ 'suggest.button' | t }}</button>
    }
    @if (open()) {
      <app-modal [title]="'suggest.title' | t" [wide]="true" (closed)="close()">
        <p class="muted">{{ 'suggest.intro' | t }}</p>
        <p class="muted">{{ 'suggest.privacy' | t }}</p>
        <div class="field">
          <label for="sg-req">{{ 'suggest.requirement' | t }}</label>
          <textarea id="sg-req" rows="5" [attr.maxlength]="limit()" [(ngModel)]="requirement" [ngModelOptions]="{ standalone: true }" data-test="suggest-requirement"></textarea>
          <div class="muted">{{ 'suggest.chars' | t: { n: requirement.length, max: limit() } }}@if (requirement.trim().length < min) { · {{ 'suggest.tooShort' | t }} }</div>
        </div>
        <div class="row">
          <div class="field">
            <label for="sg-count">{{ 'suggest.count' | t }}</label>
            <select id="sg-count" [(ngModel)]="count" [ngModelOptions]="{ standalone: true }">
              @for (n of counts(); track n) { <option [ngValue]="n">{{ n }}</option> }
            </select>
          </div>
          <button type="button" class="btn primary" data-test="suggest-generate" [disabled]="busy() || requirement.trim().length < min" (click)="generate()">
            {{ (busy() ? 'suggest.generating' : 'suggest.generate') | t }}
          </button>
        </div>

        @if (proposals(); as list) {
          <h3>{{ 'suggest.results' | t }}</h3>
          <table>
            <thead><tr><th style="width:36px"></th><th>{{ 'form.action' | t }}</th><th>{{ 'form.expected' | t }}</th><th>{{ 'form.testData' | t }}</th></tr></thead>
            <tbody>
              @for (p of list; track $index; let i = $index) {
                <tr>
                  <td><input type="checkbox" [(ngModel)]="p.picked" [ngModelOptions]="{ standalone: true }" [attr.aria-label]="'suggest.pick' | t: { n: i + 1 }" /></td>
                  <td>{{ p.step.action }}</td>
                  <td>{{ p.step.expectedResult }}</td>
                  <td>{{ p.step.testData }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
        <ng-container slot="footer">
          <button type="button" class="btn" (click)="close()">{{ 'common.close' | t }}</button>
          @if (proposals()) {
            <button type="button" class="btn primary" data-test="suggest-add" [disabled]="pickedCount() === 0" (click)="add()">
              {{ 'suggest.addSelected' | t: { count: pickedCount() } }}
            </button>
          }
        </ng-container>
      </app-modal>
    }
  `,
})
export class StepSuggestionsComponent implements OnInit {
  private readonly service = inject(StepSuggestionService);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly destroyRef = inject(DestroyRef);
  /** Counts the requests and the openings of the dialog: an answer that comes after either has moved on is dropped. */
  private run = 0;

  /** What the form knows about the test case, to start the requirement from. */
  readonly title = input('');
  readonly description = input<string | null | undefined>(null);
  /** The steps the user chose; the form adds them. */
  readonly chosen = output<SuggestedStep[]>();

  protected readonly min = MIN_LENGTH;
  protected readonly status = signal<StepSuggestionStatus | null>(null);
  protected readonly open = signal(false);
  protected readonly busy = signal(false);
  protected readonly proposals = signal<Proposal[] | null>(null);
  protected readonly limit = computed(() => this.status()?.maxRequirementLength ?? 4000);
  protected readonly counts = computed(() => {
    const max = this.status()?.maxSteps ?? 20;
    return [3, 5, 8, 12, 20].filter(n => n <= max);
  });
  protected readonly pickedCount = computed(() => this.proposals()?.filter(p => p.picked).length ?? 0);

  protected requirement = '';
  protected count = 8;

  ngOnInit(): void {
    // Only a person who may ask is told whether a model exists (the status needs the same permission).
    if (!this.auth.can(Permissions.TestCases.SuggestSteps)) { return; }
    this.service.status().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: status => { this.status.set(status); this.count = status.defaultSteps; }, error: () => undefined });
  }

  protected show(): void {
    if (!this.requirement.trim()) {
      // The title first: it says what is tested, and it is what a cut at the limit must not lose.
      this.requirement = [this.title().trim(), this.description()?.trim()].filter(Boolean).join('\n').slice(0, this.limit());
    }
    this.run++;
    this.busy.set(false);
    this.proposals.set(null);
    this.open.set(true);
  }

  protected close(): void {
    this.run++;
    this.busy.set(false);
    this.open.set(false);
  }

  protected generate(): void {
    const run = ++this.run;
    // Proposals of an earlier request must not stay pickable for a requirement that has changed since, or when this one fails.
    this.proposals.set(null);
    this.busy.set(true);
    this.service.suggest({
      requirementText: this.requirement.trim(),
      title: this.title().trim() || null,
      maxSteps: this.count,
      language: this.i18n.lang(),
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        if (run !== this.run) { return; }
        this.busy.set(false);
        this.proposals.set(result.steps.map(step => ({ step, picked: true })));
      },
      error: () => { if (run === this.run) { this.busy.set(false); } },
    });
  }

  protected add(): void {
    const steps = (this.proposals() ?? []).filter(p => p.picked).map(p => p.step);
    if (steps.length === 0) { return; }
    this.chosen.emit(steps);
    this.toast.success(this.i18n.t('suggest.added', { count: steps.length }));
    this.close();
  }
}
