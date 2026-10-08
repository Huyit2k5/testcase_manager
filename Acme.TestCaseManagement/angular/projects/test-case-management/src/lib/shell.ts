import { Component, ViewEncapsulation, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHostComponent } from './core/confirm';
import { TCM_FOLLOW_OS_THEME } from './core/host';

/**
 * Wraps every page of the module. The styles of the module are written under .tcm, so they cannot reach the host's
 * own pages, and they are loaded here, with the first page, so the host has no stylesheet to add.
 */
@Component({
  selector: 'tcm-shell',
  imports: [RouterOutlet, ConfirmHostComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './styles/tcm.scss',
  template: `<div class="tcm tcm-theme" [class.tcm-auto-dark]="followOsTheme"><router-outlet /><app-confirm-host /></div>`,
})
export class TcmShellComponent {
  protected readonly followOsTheme = inject(TCM_FOLLOW_OS_THEME);
}
