import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService, Permissions } from '../../core/auth';
import { TCM_BASE_PATH } from '../../core/host';
import { TranslatePipe } from '../../core/i18n/i18n';
import { ProjectContext } from '../../core/project-context';
import { Project } from '../../proxy/dtos';

/**
 * The project the user works in, a small choice next to the title of a page. Choosing another one builds the page again for it (the shell does
 * that when the context says the project changed). The projects themselves are loaded by the shell, through the project bar.
 */
@Component({
  selector: 'app-project-switch',
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="project-switch" data-test="project-switch">
      <select id="tcm-project" name="project" [attr.aria-label]="'project.label' | t" [attr.title]="'project.label' | t" [ngModel]="context.currentId()" (ngModelChange)="context.select($event)" [disabled]="!context.projects().length">
        @for (project of context.projects(); track project.id) {
          <option [ngValue]="project.id">{{ label(project) }}</option>
        }
      </select>
      @if (auth.can(perm.Projects.Manage)) {
        <button type="button" class="icon-action" (click)="router.navigateByUrl(base + '/projects')" data-test="manage-projects" [attr.aria-label]="'project.manage' | t" [attr.title]="'project.manage' | t">
          <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
            <circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
          </svg>
        </button>
      }
    </div>
  `,
})
export class ProjectSwitchComponent {
  protected readonly context = inject(ProjectContext);
  protected readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  protected readonly base = inject(TCM_BASE_PATH);
  protected readonly perm = Permissions;

  /** The key and the name; the default project has no key worth showing, so it is shown by its name only. */
  protected label(project: Project): string {
    return project.key === 'DEFAULT' ? project.name : `${project.key} - ${project.name}`;
  }
}
