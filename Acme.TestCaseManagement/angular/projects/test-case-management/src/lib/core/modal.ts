import { Component, DestroyRef, ElementRef, afterNextRender, inject, input, output, signal, viewChild } from '@angular/core';
import { TranslatePipe } from './i18n/i18n';

/** A dialog, or with side a drawer that slides in from the right edge: the content goes between the tags, footer buttons into the element with slot="footer". */
/** The open dialogs, oldest first: only the last one answers to Escape and Tab. */
const openModals: object[] = [];

const DRAWER_KEY = 'tcm.drawerWidth';
const DRAWER_MIN = 440;
const DRAWER_STEP = 32;

/** How wide the drawers are, for all of them: one choice of the user, kept for the next visit (half the window until they choose). */
const drawerWidth = signal(initialDrawerWidth());

function initialDrawerWidth(): number {
  try {
    const saved = Number(localStorage.getItem(DRAWER_KEY));
    if (Number.isFinite(saved) && saved >= DRAWER_MIN) { return saved; }
  } catch { /* storage may be blocked: the default will do */ }
  return Math.round(Math.min(760, typeof window === 'undefined' ? 760 : window.innerWidth / 2));
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type=hidden]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

@Component({
  selector: 'app-modal',
  imports: [TranslatePipe],
  template: `
    <div class="backdrop" [class.side]="side()" (mousedown)="pressed($event)" (click)="backdropClick($event)">
      <section #dialog class="dialog" [class.wide]="wide()" [class.narrow]="narrow()" [class.side]="side()" [style.width.px]="side() ? sideWidth() : null" role="dialog" aria-modal="true" tabindex="-1" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
        @if (side()) {
          <div class="resizer" role="separator" aria-orientation="vertical" tabindex="0" [attr.aria-label]="'drawer.resize' | t" [attr.aria-valuenow]="sideWidth()" [attr.aria-valuemin]="minWidth" [attr.aria-valuemax]="maxWidth()"
            (pointerdown)="startResize($event)" (pointermove)="resize($event)" (pointerup)="endResize($event)" (pointercancel)="endResize($event)" (keydown)="resizeKey($event)"></div>
        }
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
    /* The drawer: full height at the right edge, as wide as the user has pulled it (and never wider than the window). */
    .backdrop.side { place-items: stretch end; padding: 0; }
    .dialog.side { position: relative; height: 100vh; max-height: 100vh; border-radius: 12px 0 0 12px; max-width: 100vw; animation: slide-in .22s ease-out; }
    .dialog.side .body { flex: 1; }
    @keyframes slide-in { from { transform: translateX(48px); opacity: .4; } to { transform: none; opacity: 1; } }
    @media (prefers-reduced-motion: reduce) { .dialog.side { animation: none; } }
    @media (max-width: 640px) { .dialog.side { width: 100vw !important; border-radius: 0; } .resizer { display: none; } }
    .resizer { position: absolute; left: -5px; top: 0; bottom: 0; width: 10px; cursor: col-resize; z-index: 1; touch-action: none; }
    .resizer::after { content: ''; position: absolute; left: 4px; top: 50%; width: 3px; height: 48px; margin-top: -24px; border-radius: 2px; background: var(--border); transition: background .15s; }
    .resizer:hover::after, .resizer:focus-visible::after, .resizer.dragging::after { background: var(--primary); }
    .resizer:focus-visible { outline: none; }
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
  /** A drawer from the right edge instead of a dialog in the middle: for the details of a record, with the list still in view behind it. */
  readonly side = input(false);

  protected readonly minWidth = DRAWER_MIN;
  /** The width the drawer has: the user's choice, kept inside the window. */
  protected readonly sideWidth = () => Math.max(DRAWER_MIN, Math.min(drawerWidth(), this.maxWidth()));
  protected maxWidth(): number { return Math.max(DRAWER_MIN, Math.round(window.innerWidth * 0.92)); }
  private resizing = false;
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

  protected startResize(event: PointerEvent): void {
    this.resizing = true;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    (event.currentTarget as HTMLElement).classList.add('dragging');
    event.preventDefault();
  }

  protected resize(event: PointerEvent): void {
    if (!this.resizing) { return; }
    drawerWidth.set(Math.max(DRAWER_MIN, Math.min(window.innerWidth - event.clientX, this.maxWidth())));
  }

  protected endResize(event: PointerEvent): void {
    if (!this.resizing) { return; }
    this.resizing = false;
    (event.currentTarget as HTMLElement).classList.remove('dragging');
    this.remember();
  }

  /** The arrow keys do what the mouse does: left widens the drawer, right narrows it. */
  protected resizeKey(event: KeyboardEvent): void {
    const change = event.key === 'ArrowLeft' ? DRAWER_STEP : event.key === 'ArrowRight' ? -DRAWER_STEP : 0;
    if (!change) { return; }
    event.preventDefault();
    event.stopPropagation();
    drawerWidth.set(Math.max(DRAWER_MIN, Math.min(this.sideWidth() + change, this.maxWidth())));
    this.remember();
  }

  private remember(): void {
    try { localStorage.setItem(DRAWER_KEY, String(drawerWidth())); } catch { /* not kept this time */ }
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
