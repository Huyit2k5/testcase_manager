import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService, Permissions } from '../../core/auth';
import { TCM_BASE_PATH } from '../../core/host';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { ProjectContext } from '../../core/project-context';
import { badge } from '../../core/ui';
import { MyWork } from '../../proxy/dtos';
import { TestResultStatus } from '../../proxy/enums';
import { TestRunService } from '../../proxy/services';

@Component({
  selector: 'app-my-work',
  imports: [FormsModule, RouterLink, TranslatePipe],
  templateUrl: './my-work.html',
})
export class MyWorkComponent implements OnInit {
  private readonly service = inject(TestRunService);
  private readonly i18n = inject(I18nService);
  private readonly projects = inject(ProjectContext);
  protected readonly base = inject(TCM_BASE_PATH);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly work = signal<MyWork | null>(null);
  protected readonly failed = signal(false);
  protected includeDone = false;
  /** Counts the requests, so that a slow answer to an old choice cannot replace the current one. */
  private request = 0;

  protected readonly badgeOf = badge;
  protected readonly untested = TestResultStatus.Untested;

  /** The project of an item is named only when the caller has more than one, otherwise it would say the same on every row. */
  protected readonly showProject = computed(() => this.projects.projects().length > 1);

  ngOnInit(): void {
    this.reload();
  }

  protected reload(): void {
    const request = ++this.request;
    this.service.myWork(this.includeDone).subscribe({
      next: work => { if (request === this.request) { this.failed.set(false); this.work.set(work); } },
      error: () => { if (request === this.request) { this.failed.set(true); } },
    });
  }

  protected projectName(id: string): string {
    return this.projects.projects().find(p => p.id === id)?.name ?? '';
  }

  protected label(value: number): string { return this.i18n.enumText(TestResultStatus, value); }
}
