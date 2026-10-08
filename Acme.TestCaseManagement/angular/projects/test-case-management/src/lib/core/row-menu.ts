import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, HostListener, inject, input, output, signal } from '@angular/core';

export interface RowMenuItem { value: number; text: string; badge?: string }

/**
 * A "..." button for a table row that opens a small menu of choices (the next states of a plan). The menu is placed with fixed
 * coordinates, so that the table's own clipping (a card with rounded corners) does not cut it; it closes on a choice, on Escape,
 * on a click elsewhere and when the page scrolls or is resized.
 */
@Component({
  selector: 'app-row-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button #trigger type="button" class="icon-action" aria-haspopup="menu" [attr.aria-expanded]="open()" [attr.aria-label]="label()" [attr.title]="label()" (click)="toggle(trigger)">
      <svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor" aria-hidden="true" focusable="false">
        <circle cx="5" cy="12" r="1.8" /><circle cx="12" cy="12" r="1.8" /><circle cx="19" cy="12" r="1.8" />
      </svg>
    </button>
    @if (open()) {
      <div class="row-menu" role="menu" [attr.aria-label]="label()" [style.top.px]="above() ? null : edge()" [style.bottom.px]="above() ? edge() : null" [style.right.px]="right()">
        <div class="row-menu-title">{{ heading() }}</div>
        @for (item of items(); track item.value) {
          <button type="button" role="menuitem" class="row-menu-item" (click)="choose(item)">
            @if (item.badge) { <span class="dot" [class]="item.badge"></span> }
            {{ item.text }}
          </button>
        }
      </div>
    }
  `,
})
export class RowMenuComponent {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** What the button is called for people who do not see it ("Change status"). */
  readonly label = input.required<string>();
  readonly heading = input('');
  readonly items = input.required<RowMenuItem[]>();
  readonly chosen = output<number>();

  private readonly destroyRef = inject(DestroyRef);
  protected readonly open = signal(false);
  /** Scrolling of any container (the page content of a theme is usually not the window) moves the button away from a fixed menu. */
  private readonly onScroll = () => this.setOpen(false);
  /** The distance of the menu from the top of the window, or from its bottom when it opens upwards. */
  protected readonly edge = signal(0);
  protected readonly above = signal(false);
  protected readonly right = signal(0);

  constructor() {
    this.destroyRef.onDestroy(() => document.removeEventListener('scroll', this.onScroll, true));
  }

  private setOpen(value: boolean): void {
    if (this.open() === value) { return; }
    this.open.set(value);
    if (value) { document.addEventListener('scroll', this.onScroll, true); }
    else { document.removeEventListener('scroll', this.onScroll, true); }
  }

  protected toggle(trigger: HTMLElement): void {
    if (this.open()) { this.setOpen(false); return; }
    const box = trigger.getBoundingClientRect();
    // About the height of the menu: its title, a row per choice, and the padding.
    const height = 44 + this.items().length * 38;
    const upwards = box.bottom + 4 + height > window.innerHeight - 8 && box.top - 4 - height > 8;
    this.above.set(upwards);
    this.edge.set(upwards ? window.innerHeight - box.top + 4 : box.bottom + 4);
    this.right.set(Math.max(8, window.innerWidth - box.right));
    this.setOpen(true);
  }

  protected choose(item: RowMenuItem): void {
    this.setOpen(false);
    this.chosen.emit(item.value);
  }

  @HostListener('document:click', ['$event'])
  protected outside(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) { this.setOpen(false); }
  }

  @HostListener('document:keydown.escape')
  protected escape(): void { this.setOpen(false); }

  @HostListener('window:resize')
  protected resized(): void { this.setOpen(false); }
}
