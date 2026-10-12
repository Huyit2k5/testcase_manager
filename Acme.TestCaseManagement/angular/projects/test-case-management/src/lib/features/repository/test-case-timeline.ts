import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { UserNames } from '../../core/users';
import { TestCaseHistory } from '../../proxy/dtos';
import { TestCaseHistoryType } from '../../proxy/enums';
import { TestCaseService } from '../../proxy/services';

/** What happened to a test case, newest first: edits with their fields, status changes, versions, tags. */
@Component({
  selector: 'app-test-case-timeline',
  imports: [FormatDatePipe, TranslatePipe],
  template: `
    @if (lines().length) {
      <ol class="timeline" data-test="timeline">
        @for (line of lines(); track $index) {
          <li data-test="timeline-line" [attr.data-type]="line.type">
            <span class="timeline-when muted nowrap">{{ line.creationTime | fdate: 'short' }}</span>
            <span class="timeline-what">{{ text(line) }}</span>
            @if (who(line); as name) { <span class="muted nowrap">{{ 'history.by' | t: { name } }}</span> }
          </li>
        }
      </ol>
    } @else if (loaded()) {
      <p class="muted">{{ 'history.empty' | t }}</p>
    }
  `,
})
export class TestCaseTimelineComponent {
  private readonly service = inject(TestCaseService);
  private readonly i18n = inject(I18nService);
  private readonly users = inject(UserNames);

  readonly testCaseId = input.required<string>();
  /** Changes when the test case changed, so that the lines are read again. */
  readonly version = input<unknown>(null);

  protected readonly lines = signal<TestCaseHistory[]>([]);
  protected readonly loaded = signal(false);

  constructor() {
    this.users.load();
    effect(() => {
      const id = this.testCaseId();
      this.version();
      untracked(() => this.service.history(id).subscribe({ next: lines => { this.lines.set(lines); this.loaded.set(true); }, error: () => this.loaded.set(true) }));
    });
  }

  protected who(line: TestCaseHistory): string | null { return this.users.nameOf(line.creatorId); }

  protected text(line: TestCaseHistory): string {
    const t = (key: string, params?: Record<string, string | number>) => this.i18n.t(key, params);
    switch (line.type) {
      case TestCaseHistoryType.Created: return t('history.created');
      case TestCaseHistoryType.Edited: return t('history.edited', { fields: this.fields(line.note) });
      case TestCaseHistoryType.StatusChanged: {
        const base = t('history.statusChanged', { from: this.status(line.fromValue), to: this.status(line.toValue) });
        return line.note === 'Edited after approval' ? `${base} (${t('history.afterApproval')})` : base;
      }
      case TestCaseHistoryType.VersionPublished: {
        const base = t('history.versionPublished', { n: line.versionNumber ?? 0 });
        return line.note ? `${base}: ${line.note}` : base;
      }
      case TestCaseHistoryType.TagsChanged: return line.note ? t('history.tags', { tags: line.note }) : t('history.tagsCleared');
      case TestCaseHistoryType.StepsReordered: return t('history.reordered');
      case TestCaseHistoryType.SharedStepsChanged: return this.shared(line.note);
      default: return line.note ?? '';
    }
  }

  private status(name: string | null): string {
    const key = name ? `enum.TestCaseStatus.${name}` : '';
    const text = key ? this.i18n.t(key) : '';
    return text && text !== key ? text : name ?? '';
  }

  /** The fields the server names ("Title, Steps") in the language of the person. */
  private fields(note: string | null): string {
    return (note ?? '').split(',').map(name => name.trim()).filter(Boolean).map(name => {
      const key = `field.${name.charAt(0).toLowerCase()}${name.slice(1)}`;
      const text = this.i18n.t(key);
      return text === key ? name : text;
    }).join(', ');
  }

  private shared(note: string | null): string {
    const t = (key: string, name?: string) => this.i18n.t(key, { name: name ?? '' });
    if (note?.startsWith('Inserted: ')) { return t('history.sharedInserted', note.slice('Inserted: '.length)); }
    if (note?.startsWith('Refreshed: ')) { return t('history.sharedRefreshed', note.slice('Refreshed: '.length)); }
    return t('history.sharedDetached');
  }
}
