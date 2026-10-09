import { Injectable, computed, inject, signal } from '@angular/core';
import { Project } from '../proxy/dtos';
import { ProjectService } from '../proxy/projects';

const STORAGE_KEY = 'tcm.project';

function stored(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

/**
 * The project the user is working in. Every page shows what belongs to it, and what the user creates goes into it. The services of the
 * module read it, so a page does not pass it around; the choice is kept in the browser for the next visit.
 *
 * Nothing chosen (the list is not loaded yet, or the user may not read projects) means no project is sent, and the server answers for every
 * project, as it always did.
 */
@Injectable({ providedIn: 'root' })
export class ProjectContext {
  private readonly service = inject(ProjectService);

  readonly projects = signal<Project[]>([]);
  readonly currentId = signal<string | null>(stored());
  /** False until the first answer about the projects, so a page is not built (and its data asked for) twice. */
  readonly ready = signal(false);
  /** Goes up when the project changes: the shell builds the page again then. */
  readonly version = signal(0);

  readonly current = computed(() => this.projects().find(p => p.id === this.currentId()) ?? null);

  /** Asks for the projects. The one chosen before is kept when it is still there; otherwise the first one is chosen. */
  load(): void {
    this.service.list().subscribe({
      next: projects => { this.setProjects(projects); this.ready.set(true); },
      error: () => this.ready.set(true),
    });
  }

  setProjects(projects: Project[]): void {
    this.projects.set(projects);
    const current = this.currentId();
    if (current === null || !projects.some(p => p.id === current)) {
      this.choose(projects[0]?.id ?? null);
    }
  }

  select(id: string | null): void {
    if (id === this.currentId()) { return; }
    this.choose(id);
  }

  private choose(id: string | null): void {
    this.currentId.set(id);
    try {
      if (id) { localStorage.setItem(STORAGE_KEY, id); } else { localStorage.removeItem(STORAGE_KEY); }
    } catch { /* a private window: the choice lasts until the page is left */ }
    this.version.update(v => v + 1);
  }
}
