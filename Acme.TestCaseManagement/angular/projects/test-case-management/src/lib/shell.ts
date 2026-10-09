import { Component, ViewEncapsulation, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHostComponent } from './core/confirm';
import { TCM_FOLLOW_OS_THEME } from './core/host';
import { ProjectContext } from './core/project-context';
import { ProjectBarComponent } from './features/projects/project-bar';

/**
 * Wraps every page of the module. The styles of the module are written under .tcm, so they cannot reach the host's
 * own pages, and they are loaded here, with the first page, so the host has no stylesheet to add.
 */
@Component({
  selector: 'tcm-shell',
  imports: [RouterOutlet, ConfirmHostComponent, ProjectBarComponent],
  encapsulation: ViewEncapsulation.None,
  styleUrl: './styles/tcm.scss',
  // The page is built once the projects are known, and again when the project changes: a page asks for its data when it is built, so it
  // then asks for the data of the new project. (A new outlet shows the route that is already active.)
  template: `
    <div class="tcm tcm-theme" [class.tcm-auto-dark]="followOsTheme">
      <app-project-bar />
      @if (projects.ready()) {
        @for (version of [projects.version()]; track version) { <router-outlet /> }
      }
      <app-confirm-host />
    </div>
  `,
})
export class TcmShellComponent {
  protected readonly followOsTheme = inject(TCM_FOLLOW_OS_THEME);
  protected readonly projects = inject(ProjectContext);
}
