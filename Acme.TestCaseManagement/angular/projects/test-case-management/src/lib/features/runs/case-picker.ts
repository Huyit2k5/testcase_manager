import { Component, OnInit, inject, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { badge } from '../../core/ui';
import { TestCase } from '../../proxy/dtos';
import { PriorityLevel, TestCaseStatus } from '../../proxy/enums';
import { TestCaseService } from '../../proxy/services';

/** A checklist of approved test cases; only approved ones can be scheduled in a run. */
@Component({
  selector: 'app-case-picker',
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="row" style="margin-bottom: 8px">
      <input class="grow" name="picker-search" [(ngModel)]="search" (ngModelChange)="load()" [placeholder]="'picker.search' | t" [attr.aria-label]="'picker.search' | t" />
      <button type="button" class="btn sm" (click)="selectAll()">{{ 'picker.selectAll' | t }}</button>
      <button type="button" class="btn sm" (click)="selectedIds.set([])">{{ 'picker.clear' | t }}</button>
    </div>
    <div style="max-height: 280px; overflow: auto; border: 1px solid var(--border); border-radius: 8px">
      <table>
        <tbody>
          @for (tc of cases(); track tc.id) {
            <tr>
              <td style="width: 32px"><input type="checkbox" [name]="'pick' + tc.id" [checked]="isSelected(tc.id)" (change)="toggle(tc.id)" [attr.aria-label]="'picker.select' | t: { code: tc.code }" /></td>
              <td class="mono nowrap">{{ tc.code }}</td>
              <td>{{ tc.title }}</td>
              <td><span class="badge" [class]="badgeOf('priority', tc.priority)">{{ priorityName(tc.priority) }}</span></td>
            </tr>
          }
        </tbody>
      </table>
      @if (!cases().length) { <div class="empty">{{ 'picker.none' | t }}</div> }
    </div>
    <p class="muted" style="margin: 6px 0 0">{{ 'picker.selected' | t: { n: selectedIds().length } }}</p>
  `,
})
export class CasePickerComponent implements OnInit {
  private readonly service = inject(TestCaseService);
  private readonly i18n = inject(I18nService);

  /** Test cases that must not be offered, for example the ones already in the run. */
  readonly exclude = input<string[]>([]);
  /** Scheduling needs approved test cases; linking a requirement accepts any status. */
  readonly onlyApproved = input(true);
  readonly selectedIds = model<string[]>([]);

  protected readonly cases = signal<TestCase[]>([]);
  protected search = '';
  protected readonly badgeOf = badge;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.service.list({ filter: this.search, status: this.onlyApproved() ? TestCaseStatus.Approved : null, maxResultCount: 200, sorting: 'code' }).subscribe(result => {
      const excluded = new Set(this.exclude());
      this.cases.set(result.items.filter(tc => !excluded.has(tc.id)));
    });
  }

  protected priorityName(value: PriorityLevel): string { return this.i18n.enumText(PriorityLevel, value); }

  protected isSelected(id: string): boolean { return this.selectedIds().includes(id); }

  protected toggle(id: string): void {
    this.selectedIds.update(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]));
  }

  protected selectAll(): void {
    this.selectedIds.update(ids => [...new Set([...ids, ...this.cases().map(tc => tc.id)])]);
  }
}
