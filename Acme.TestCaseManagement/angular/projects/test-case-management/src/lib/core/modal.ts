import { Component, DestroyRef, ElementRef, afterNextRender, inject, input, output, viewChild } from '@angular/core';
import { TranslatePipe } from './i18n/i18n';

/** A dialog: the content goes between the tags, footer buttons into the element with slot="footer". */
/** The open dialogs, oldest first: only the last one answers to Escape and Tab. */
const openModals: object[] = [];

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

@Component({
  selector: 'app-modal',
  imports: [TranslatePipe],
  template: `
    <div class="backdrop" (mousedown)="pressed($event)" (click)="backdropClick($event)">
      <section #dialog class="dialog" [class.wide]="wide()" [class.narrow]="narrow()" role="dialog" aria-modal="true" tabindex="-1" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
        <header>
          <h2>{{ title() }}</h2>
          <button type="button" class="icon-btn" [attr.aria-label]="'common.close' | t" (click)="closed.emit()">&times;</button>
        </header>
        <div class="body"><ng-content /></div>
        <footer><ng-content select="[slot=footer]" /></footer>
      </section>
    </div>
  `,
  styles: `
    .backdrop { position: fixed; inset: 0; background: rgba(15, 23, 42, .45); display: grid; place-items: center; z-index: 1055; padding: 16px; }
    .dialog { background: var(--surface); color: var(--text); border-radius: 12px; width: min(560px, 100%); max-height: 90vh; display: flex; flex-direction: column; box-shadow: var(--shadow-lg); }
    .dialog:focus { outline: none; }
    .dialog.wide { width: min(900px, 100%); }
    .dialog.narrow { width: min(440px, 100%); }
    header { display: flex; align-items: center; justify-content: space-between; padding: 14px 20px; border-bottom: 1px solid var(--border); }
    h2 { margin: 0; font-size: 1.05rem; }
    .body { padding: 20px; overflow: auto; }
    footer { padding: 12px 20px; border-top: 1px solid var(--border); display: flex; gap: 8px; justify-content: flex-end; }
    footer:empty { display: none; }
  `,
})
export class ModalComponent {
  readonly title = input.required<string>();
  readonly wide = input(false);
  /** A small dialog, for a question. */
  readonly narrow = input(false);
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLElement>>('dialog');
  private readonly previouslyFocused = typeof document === 'undefined' ? null : document.activeElement as HTMLElement | null;
  /** True when the current mouse press began on the backdrop itself, not inside the dialog. */
  private pressedOnBackdrop = false;
  private readonly onKeydown = (event: KeyboardEvent): void => this.keydown(event);

  constructor() {
    openModals.push(this);
    document.addEventListener('keydown', this.onKeydown);
    afterNextRender(() => {
      // Keyboard users start inside the dialog; the first field if there is one, else the dialog itself.
      const root = this.dialog().nativeElement;
      (root.querySelector('.body')?.querySelector<HTMLElement>(FOCUSABLE) ?? root).focus();
    });
    inject(DestroyRef).onDestroy(() => {
      document.removeEventListener('keydown', this.onKeydown);
      openModals.splice(openModals.indexOf(this), 1);
      // Back to where the user was, unless that element is gone.
      if (this.previouslyFocused?.isConnected) { this.previouslyFocused.focus(); }
    });
  }

  protected pressed(event: MouseEvent): void {
    this.pressedOnBackdrop = event.target === event.currentTarget;
  }

  /** A drag that starts in the dialog (selecting text) and ends on the backdrop must not close it and lose what was typed. */
  protected backdropClick(event: MouseEvent): void {
    const close = this.pressedOnBackdrop && event.target === event.currentTarget;
    this.pressedOnBackdrop = false;
    if (close) { this.closed.emit(); }
  }

  private keydown(event: KeyboardEvent): void {
    if (openModals[openModals.length - 1] !== this) { return; }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closed.emit();
      return;
    }
    if (event.key !== 'Tab') { return; }
    const root = this.dialog().nativeElement;
    const items = Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE));
    if (items.length === 0) {
      event.preventDefault();
      root.focus();
      return;
    }
    const first = items[0];
    const last = items[items.length - 1];
    const active = document.activeElement;
    if (!root.contains(active) || active === root) {
      event.preventDefault();
      (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }
}
