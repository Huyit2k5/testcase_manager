import { Component, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, Subject, of } from 'rxjs';
import { ModalComponent } from './modal';
import { I18nService, TranslatePipe } from './i18n/i18n';

export interface ConfirmOptions {
  message: string;
  /** Defaults to "Please confirm". */
  title?: string;
  /** What the confirming button says; defaults to "OK". Name the action ("Delete"), not "Yes". */
  confirmText?: string;
  /** Marks a destructive action: a red button and a warning icon. */
  danger?: boolean;
}

export interface PromptOptions {
  /** What is asked, shown above the field. */
  message: string;
  title?: string;
  confirmText?: string;
  placeholder?: string;
  /** The answer may be empty: the button works without text, and gives an empty string (cancelling still gives null). */
  optional?: boolean;
}

interface Pending {
  kind: 'confirm' | 'prompt';
  options: ConfirmOptions & PromptOptions;
  answer: Subject<boolean | string | null>;
}

/**
 * Asks the user a question in a dialog of the module's own (the pages show it through the shell), instead of the browser's
 * confirm() and prompt(). Where no shell is showing (a unit test, a host that mounts a page on its own), it falls back to the
 * browser's boxes, so a question is never left unasked.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private mounted = 0;

  /** The question on screen; the shell's host component shows it. */
  readonly pending = signal<Pending | null>(null);

  /** Emits once: true when the user confirmed, false when they cancelled (Cancel, Escape, the backdrop or the close button). */
  ask(options: ConfirmOptions): Observable<boolean> {
    if (this.mounted === 0) { return of(confirm(options.message)); }
    return this.open('confirm', options) as Observable<boolean>;
  }

  /** Emits once: the text the user typed (not empty), or null when they cancelled or left it empty; with `optional`, empty text is '' and only cancelling is null. */
  askText(options: PromptOptions): Observable<string | null> {
    if (this.mounted === 0) {
      const typed = prompt(options.message);
      return of(typed === null ? null : (typed.trim() || (options.optional ? '' : null)));
    }
    return this.open('prompt', options) as Observable<string | null>;
  }

  private open(kind: Pending['kind'], options: ConfirmOptions & PromptOptions): Observable<boolean | string | null> {
    // A second question while one is open (a double click) cancels the first, so that nothing waits for ever.
    this.settle(kind === 'confirm' ? false : null);
    const answer = new Subject<boolean | string | null>();
    this.pending.set({ kind, options, answer });
    return answer.asObservable();
  }

  /** Called by the host component: closes the question with its answer. */
  settle(value: boolean | string | null): void {
    const current = this.pending();
    if (!current) { return; }
    this.pending.set(null);
    current.answer.next(value);
    current.answer.complete();
  }

  /** The shell announces that it shows questions, for as long as it lives. */
  mount(): () => void {
    this.mounted++;
    return () => { this.mounted--; };
  }
}

/** Shows the question of the ConfirmService. One lives in the shell that wraps the pages of the module. */
@Component({
  selector: 'app-confirm-host',
  imports: [FormsModule, ModalComponent, TranslatePipe],
  template: `
    @if (service.pending(); as q) {
      <app-modal [title]="q.options.title ?? ('confirm.title' | t)" [narrow]="true" (closed)="cancel(q.kind)">
        @if (q.kind === 'confirm') {
          <div class="confirm-body" data-test="confirm-dialog">
            <span class="confirm-icon" [class.danger]="q.options.danger" aria-hidden="true">
              @if (q.options.danger) {
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z" /><path d="M12 9v4M12 17h.01" /></svg>
              } @else {
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3M12 17h.01" /></svg>
              }
            </span>
            <p>{{ q.options.message }}</p>
          </div>
        } @else {
          <form id="prompt-form" data-test="prompt-dialog" (ngSubmit)="submitText()">
            <div class="field">
              <label for="prompt-text">{{ q.options.message }}</label>
              <input id="prompt-text" name="text" [(ngModel)]="text" [placeholder]="q.options.placeholder ?? ''" autocomplete="off" />
            </div>
          </form>
        }
        <ng-container slot="footer">
          <button type="button" class="btn" data-test="confirm-cancel" (click)="cancel(q.kind)">{{ 'common.cancel' | t }}</button>
          @if (q.kind === 'confirm') {
            <button type="button" class="btn" [class.primary]="!q.options.danger" [class.danger-solid]="q.options.danger" data-test="confirm-ok" (click)="service.settle(true)">{{ q.options.confirmText ?? ('confirm.ok' | t) }}</button>
          } @else {
            <button type="submit" form="prompt-form" class="btn primary" [disabled]="!q.options.optional && !text.trim()" data-test="confirm-ok">{{ q.options.confirmText ?? ('confirm.ok' | t) }}</button>
          }
        </ng-container>
      </app-modal>
    }
  `,
})
export class ConfirmHostComponent {
  protected readonly service = inject(ConfirmService);
  private readonly i18n = inject(I18nService);
  protected text = '';

  constructor() {
    const unmount = this.service.mount();
    inject(DestroyRef).onDestroy(() => {
      unmount();
      this.service.settle(null);   // a page that goes away with a question open must not leave it waiting
    });
  }

  protected cancel(kind: 'confirm' | 'prompt'): void {
    this.service.settle(kind === 'confirm' ? false : null);
    this.text = '';
  }

  protected submitText(): void {
    const value = this.text.trim();
    this.text = '';
    this.service.settle(value || (this.service.pending()?.options.optional ? '' : null));
  }
}
