import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm';
import { ToastService } from '../../core/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { IconButtonComponent } from '../../core/icon-button';
import { ModalComponent } from '../../core/modal';
import { ProjectContext } from '../../core/project-context';
import { AuthService, Permissions } from '../../core/auth';
import { UserNames } from '../../core/users';
import { Project } from '../../proxy/dtos';
import { ProjectService } from '../../proxy/projects';

interface AccessForm { project: Project; isRestricted: boolean; userIds: string[]; pick: string }
interface ProjectForm { id: string | null; key: string; name: string; description: string }

/** The page that manages the projects: who works on what is chosen in the bar above; here they are made, renamed, archived and removed. */
@Component({
  selector: 'app-projects',
  imports: [FormsModule, TranslatePipe, ModalComponent, IconButtonComponent],
  template: `
    <div class="page">
      <div class="page-head">
        <h1>{{ 'project.title' | t }}</h1>
        <button type="button" class="btn primary" (click)="newProject()" data-test="new-project">{{ 'project.new' | t }}</button>
      </div>
      <p class="muted">{{ 'project.intro' | t }}</p>

      <div class="card">
        <label class="row"><input type="checkbox" name="archived" [ngModel]="showArchived" (ngModelChange)="toggleArchived($event)" /> {{ 'project.showArchived' | t }}</label>
      </div>

      <div class="card flush">
        <table>
          <thead>
            <tr>
              <th>{{ 'project.colKey' | t }}</th><th>{{ 'common.name' | t }}</th><th>{{ 'project.colContents' | t }}</th><th></th>
            </tr>
          </thead>
          <tbody>
            @for (project of projects(); track project.id) {
              <tr [class.muted]="project.isArchived" data-test="project-row">
                <td><span class="mono">{{ project.key }}</span></td>
                <td>
                  {{ project.name }}
                  @if (project.isArchived) { <span class="badge">{{ 'project.archived' | t }}</span> }
                  @if (project.isRestricted) { <span class="badge warn" data-test="restricted-badge">{{ 'project.restricted' | t }} · {{ 'project.membersCount' | t: { n: project.memberCount } }}</span> }
                  @if (project.description) { <div class="muted">{{ project.description }}</div> }
                </td>
                <td class="muted">
                  {{ 'project.contents' | t: { suites: project.suiteCount, cases: project.testCaseCount, plans: project.planCount, requirements: project.requirementCount, runs: project.runCount } }}
                </td>
                <td class="right nowrap">
                  <button type="button" tcmIcon="edit" [attr.aria-label]="'common.edit' | t" [attr.title]="'common.edit' | t" (click)="edit(project)"></button>
                  @if (project.isArchived) {
                    <button type="button" class="btn sm" (click)="restore(project)">{{ 'project.restore' | t }}</button>
                  } @else {
                    <button type="button" class="btn sm" (click)="archive(project)">{{ 'project.archive' | t }}</button>
                  }
                  @if (isEmpty(project)) {
                    <button type="button" tcmIcon="trash" [attr.aria-label]="'common.delete' | t" [attr.title]="'common.delete' | t" (click)="remove(project)"></button>
                  }
                  @if (auth.can(perm.Projects.Manage)) { <button type="button" class="btn sm" data-test="project-access" (click)="openAccess(project)">{{ 'project.access' | t }}</button> }
                </td>
              </tr>
            }
            @if (!projects().length) { <tr><td colspan="4" class="muted">{{ 'common.none' | t }}</td></tr> }
          </tbody>
        </table>
      </div>
    </div>

    @if (access(); as a) {
      <app-modal [title]="'project.accessTitle' | t: { name: a.project.name }" (closed)="access.set(null)">
        <label class="row" style="margin-bottom: 8px">
          <input type="checkbox" name="restricted" data-test="restrict" [checked]="a.isRestricted" (change)="a.isRestricted = !a.isRestricted" />
          {{ 'project.restrictedLabel' | t }}
        </label>
        <p class="muted">{{ 'project.restrictedHelp' | t }}</p>

        <h3>{{ 'project.members' | t }}</h3>
        @if (a.userIds.length) {
          <ul class="member-list" data-test="members">
            @for (id of a.userIds; track id) {
              <li><span>{{ names.nameOf(id) ?? id }}</span> <button type="button" class="btn sm" (click)="removeMember(a, id)" [attr.aria-label]="'common.remove' | t">{{ 'common.remove' | t }}</button></li>
            }
          </ul>
        } @else {
          <p class="muted">{{ 'project.noMembers' | t }}</p>
        }

        @if (names.available()) {
          <div class="row">
            <select name="member-pick" data-test="member-pick" [(ngModel)]="a.pick" [attr.aria-label]="'project.pickUser' | t" style="width: auto; min-width: 240px">
              <option value="">{{ 'project.pickUser' | t }}</option>
              @for (u of candidates(a); track u.id) { <option [value]="u.id">{{ u.displayName }}</option> }
            </select>
            <button type="button" class="btn" data-test="member-add" [disabled]="!a.pick" (click)="addMember(a)">{{ 'common.add' | t }}</button>
          </div>
        } @else {
          <p class="muted">{{ 'project.noDirectory' | t }}</p>
        }
        <ng-container slot="footer">
          <button type="button" class="btn" (click)="access.set(null)">{{ 'common.cancel' | t }}</button>
          <button type="button" class="btn primary" data-test="access-save" [disabled]="busy()" (click)="saveAccess()">{{ 'common.save' | t }}</button>
        </ng-container>
      </app-modal>
    }

    @if (form(); as f) {
      <app-modal [title]="(f.id ? 'project.editTitle' : 'project.new') | t" [narrow]="true" (closed)="form.set(null)">
        <form id="project-form" (ngSubmit)="save()">
          <div class="field">
            <label for="project-key">{{ 'project.key' | t }}</label>
            <input id="project-key" name="key" [(ngModel)]="f.key" [readonly]="!!f.id" maxlength="10" required autocomplete="off" style="text-transform: uppercase" />
            <small class="muted">{{ (f.id ? 'project.keyFixed' : 'project.keyHint') | t }}</small>
          </div>
          <div class="field">
            <label for="project-name">{{ 'common.name' | t }}</label>
            <input id="project-name" name="name" [(ngModel)]="f.name" required maxlength="128" />
          </div>
          <div class="field">
            <label for="project-description">{{ 'common.description' | t }}</label>
            <textarea id="project-description" name="description" [(ngModel)]="f.description" rows="3" maxlength="2000"></textarea>
          </div>
        </form>
        <ng-container slot="footer">
          <button type="button" class="btn" (click)="form.set(null)">{{ 'common.cancel' | t }}</button>
          <button type="submit" form="project-form" class="btn primary" [disabled]="busy()">{{ 'common.save' | t }}</button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class ProjectsComponent implements OnInit {
  private readonly service = inject(ProjectService);
  private readonly context = inject(ProjectContext);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);

  protected readonly auth = inject(AuthService);
  protected readonly names = inject(UserNames);
  protected readonly perm = Permissions;

  protected readonly access = signal<AccessForm | null>(null);
  protected readonly projects = signal<Project[]>([]);
  protected readonly form = signal<ProjectForm | null>(null);
  protected readonly busy = signal(false);
  protected showArchived = false;

  ngOnInit(): void { this.load(); }

  // ---- who may see a project
  protected openAccess(project: Project): void {
    this.names.load();
    this.service.getAccess(project.id).subscribe(current => this.access.set({ project, isRestricted: current.isRestricted, userIds: [...current.userIds], pick: '' }));
  }

  /** The users of the host who are not members yet. */
  protected candidates(form: AccessForm) {
    return this.names.users().filter(u => !form.userIds.includes(u.id));
  }

  protected addMember(form: AccessForm): void {
    if (form.pick && !form.userIds.includes(form.pick)) { form.userIds = [...form.userIds, form.pick]; }
    form.pick = '';
  }

  protected removeMember(form: AccessForm, id: string): void { form.userIds = form.userIds.filter(x => x !== id); }

  protected saveAccess(): void {
    const form = this.access();
    if (!form || this.busy()) { return; }
    this.busy.set(true);
    this.service.updateAccess(form.project.id, { isRestricted: form.isRestricted, userIds: form.userIds }).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('project.accessSaved'));
        this.access.set(null);
        this.changed(null);
      },
      error: () => this.busy.set(false),
    });
  }

  protected toggleArchived(value: boolean): void {
    this.showArchived = value;
    this.load();
  }

  protected isEmpty(project: Project): boolean {
    return project.suiteCount + project.planCount + project.requirementCount + project.runCount === 0;
  }

  protected newProject(): void { this.form.set({ id: null, key: '', name: '', description: '' }); }

  protected edit(project: Project): void {
    this.form.set({ id: project.id, key: project.key, name: project.name, description: project.description ?? '' });
  }

  protected save(): void {
    const form = this.form();
    if (!form || this.busy()) { return; }
    this.busy.set(true);
    const body = { name: form.name, description: form.description || null };
    const request = form.id ? this.service.update(form.id, body) : this.service.create({ ...body, key: form.key.trim().toUpperCase() });
    request.subscribe({
      next: saved => {
        this.busy.set(false);
        this.toast.success(this.i18n.t('project.saved'));
        this.form.set(null);
        this.changed(form.id === null ? saved.id : null);
      },
      error: () => this.busy.set(false),
    });
  }

  protected archive(project: Project): void {
    this.confirmer.ask({ message: this.i18n.t('project.confirmArchive', { name: project.name }), confirmText: this.i18n.t('project.archive') }).subscribe(ok => {
      if (!ok) { return; }
      this.service.archive(project.id).subscribe(() => { this.toast.success(this.i18n.t('project.archivedDone')); this.changed(null); });
    });
  }

  protected restore(project: Project): void {
    this.service.restore(project.id).subscribe(() => { this.toast.success(this.i18n.t('project.restoredDone')); this.changed(null); });
  }

  protected remove(project: Project): void {
    this.confirmer.ask({ message: this.i18n.t('project.confirmDelete', { name: project.name }), confirmText: this.i18n.t('common.delete'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.service.delete(project.id).subscribe(() => { this.toast.success(this.i18n.t('project.deleted')); this.changed(null); });
    });
  }

  private load(): void {
    this.service.list(this.showArchived).subscribe(projects => this.projects.set(projects));
  }

  /** The list on this page and the one in the bar above are both up to date; a project that was just made becomes the one in use. */
  private changed(chooseId: string | null): void {
    this.load();
    this.service.list().subscribe(projects => {
      this.context.setProjects(projects);
      if (chooseId) { this.context.select(chooseId); }
    });
  }
}
