import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, Permissions } from '../../core/auth';
import { TCM_BASE_PATH } from '../../core/host';
import { TranslatePipe } from '../../core/i18n/i18n';
import { ProjectContext } from '../../core/project-context';
import { Project } from '../../proxy/dtos';

/**
 * The project the user works in, at the top of every page. Choosing another one builds the page again for it (the shell does that when the
 * context says the project changed). It loads the list of projects, so the pages below are built once the answer is known.
 */
@Component({
  selector: 'app-project-bar',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <div class="project-bar" data-test="project-bar">
      <label for="tcm-project">{{ 'project.label' | t }}</label>
      <select id="tcm-project" name="project" [ngModel]="context.currentId()" (ngModelChange)="context.select($event)" [disabled]="!context.projects().length">
        @for (project of context.projects(); track project.id) {
          <option [ngValue]="project.id">{{ label(project) }}</option>
        }
      </select>
      <span class="grow"></span>
      @if (auth.can(perm.Projects.Manage)) {
        <a class="btn sm" [routerLink]="base + '/projects'" data-test="manage-projects">{{ 'project.manage' | t }}</a>
      }
    </div>
  `,
})
export class ProjectBarComponent implements OnInit {
  protected readonly context = inject(ProjectContext);
  protected readonly auth = inject(AuthService);
  protected readonly base = inject(TCM_BASE_PATH);
  protected readonly perm = Permissions;

  /** The key and the name; the default project has no key worth showing, so it is shown by its name only. */
  protected label(project: Project): string {
    return project.key === 'DEFAULT' ? project.name : `${project.key} - ${project.name}`;
  }

  ngOnInit(): void {
    if (!this.context.ready()) { this.context.load(); }
  }
}
