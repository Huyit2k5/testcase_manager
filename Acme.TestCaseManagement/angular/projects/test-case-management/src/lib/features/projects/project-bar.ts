import { Component, OnInit, inject, input } from '@angular/core';
import { ProjectContext } from '../../core/project-context';
import { ProjectSwitchComponent } from './project-switch';

/**
 * Loads the projects, so that the pages below are built once the answer is known. It also shows the choice of the project on its own line
 * (the standalone use); the shell asks for `bare`, because the pages carry the choice next to their titles.
 */
@Component({
  selector: 'app-project-bar',
  imports: [ProjectSwitchComponent],
  template: `
    @if (!bare()) {
      <div class="project-bar" data-test="project-bar"><app-project-switch /></div>
    }
  `,
})
export class ProjectBarComponent implements OnInit {
  protected readonly context = inject(ProjectContext);

  /** Only load the projects; show nothing. */
  readonly bare = input(false);

  ngOnInit(): void {
    if (!this.context.ready()) { this.context.load(); }
  }
}
