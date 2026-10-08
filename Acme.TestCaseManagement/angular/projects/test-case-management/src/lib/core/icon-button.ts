import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A small icon on a button, for the actions of a table row (edit, delete). The button's own markup gives it a name for people who do not see
 * the icon (`aria-label`) and a tooltip (`title`): <button type="button" tcmIcon="edit" [attr.aria-label]="..." [attr.title]="..."></button>.
 */
@Component({
  selector: 'button[tcmIcon]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'icon-action', '[class.danger]': "tcmIcon() === 'trash'" },
  template: `
    <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
      @switch (tcmIcon()) {
        @case ('edit') {
          <path d="M12 20h9" />
          <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z" />
        }
        @case ('trash') {
          <path d="M3 6h18" />
          <path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2" />
          <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
          <path d="M10 11v6M14 11v6" />
        }
      }
    </svg>
  `,
})
export class IconButtonComponent {
  readonly tcmIcon = input.required<'edit' | 'trash'>();
}
