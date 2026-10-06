import { Component, input, output } from '@angular/core';
import { TranslatePipe } from './i18n/i18n';

/** A dialog: the content goes between the tags, footer buttons into the element with slot="footer". */
@Component({
  selector: 'app-modal',
  imports: [TranslatePipe],
  template: `
    <div class="backdrop" (click)="closed.emit()">
      <section class="dialog" [class.wide]="wide()" role="dialog" aria-modal="true" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
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
    .backdrop { position: fixed; inset: 0; background: rgba(15, 23, 42, .45); display: grid; place-items: center; z-index: 50; padding: 16px; }
    .dialog { background: var(--surface); color: var(--text); border-radius: 12px; width: min(560px, 100%); max-height: 90vh; display: flex; flex-direction: column; box-shadow: var(--shadow-lg); }
    .dialog.wide { width: min(900px, 100%); }
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
  readonly closed = output<void>();
}
