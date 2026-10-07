import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { ModalComponent } from '../../core/modal';
import { badge } from '../../core/ui';
import { SaveSharedStepGroup, SharedStepGroup, SharedStepGroupSummary, SharedStepUsage } from '../../proxy/dtos';
import { TestCaseStatus } from '../../proxy/enums';
import { SharedStepGroupService } from '../../proxy/services';

interface EditForm { id: string | null; name: string; description: string; steps: { id?: string | null; action: string; expectedResult: string; testData: string }[] }
interface UsageDialog { group: SharedStepGroupSummary; items: SharedStepUsage[]; selected: Set<string> }

/** The library of reusable groups of steps: write a group once, see who uses it, and bring the test cases that are behind up to date. */
@Component({
  selector: 'app-shared-steps',
  imports: [FormsModule, TranslatePipe, ModalComponent],
  templateUrl: './shared-steps.html',
})
export class SharedStepsComponent implements OnInit {
  private readonly service = inject(SharedStepGroupService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly groups = signal<SharedStepGroupSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly form = signal<EditForm | null>(null);
  protected readonly usage = signal<UsageDialog | null>(null);
  protected search = '';
  protected readonly statusEnum = TestCaseStatus;
  protected readonly badgeOf = badge;

  ngOnInit(): void { this.reload(); }

  protected reload(): void {
    this.loading.set(true);
    this.service.list(this.search).subscribe({
      next: groups => { this.groups.set(groups); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  protected label(type: object, value: number): string { return this.i18n.enumText(type, value); }

  // ---- edit
  protected newGroup(): void {
    this.form.set({ id: null, name: '', description: '', steps: [{ action: '', expectedResult: '', testData: '' }] });
  }

  protected edit(summary: SharedStepGroupSummary): void {
    this.service.get(summary.id).subscribe(group => this.form.set(toForm(group)));
  }

  protected addStep(form: EditForm): void { form.steps.push({ action: '', expectedResult: '', testData: '' }); }

  protected removeStep(form: EditForm, index: number): void { form.steps.splice(index, 1); }

  protected move(form: EditForm, index: number, delta: number): void {
    const target = index + delta;
    [form.steps[index], form.steps[target]] = [form.steps[target], form.steps[index]];
  }

  protected save(): void {
    const form = this.form();
    if (!form) { return; }
    if (form.steps.length === 0) {
      this.toast.error(this.i18n.t('shared.needStep'));
      return;
    }
    const body: SaveSharedStepGroup = {
      name: form.name,
      description: form.description || null,
      steps: form.steps.map(s => ({ id: s.id ?? null, action: s.action, expectedResult: s.expectedResult, testData: s.testData || null })),
    };
    const request = form.id ? this.service.update(form.id, body) : this.service.create(body);
    request.subscribe(saved => {
      this.toast.success(this.i18n.t(form.id ? 'shared.updated' : 'shared.created', { revision: saved.revision }));
      this.form.set(null);
      this.reload();
    });
  }

  protected remove(group: SharedStepGroupSummary): void {
    if (!confirm(this.i18n.t('shared.confirmDelete', { name: group.name }))) { return; }
    this.service.remove(group.id).subscribe(() => {
      this.toast.success(this.i18n.t('shared.deleted'));
      this.reload();
    });
  }

  // ---- usage
  protected openUsage(group: SharedStepGroupSummary): void {
    this.service.usage(group.id).subscribe(items => {
      this.usage.set({ group, items, selected: new Set(items.filter(i => i.isOutdated).map(i => i.testCaseId)) });
    });
  }

  protected behind(dialog: UsageDialog): number { return dialog.items.filter(i => i.isOutdated).length; }

  protected toggle(dialog: UsageDialog, id: string): void {
    if (dialog.selected.has(id)) { dialog.selected.delete(id); } else { dialog.selected.add(id); }
  }

  protected updateSelected(): void {
    const dialog = this.usage();
    if (!dialog || dialog.selected.size === 0) { return; }
    this.service.updateTestCases(dialog.group.id, [...dialog.selected]).subscribe(result => {
      this.toast.success(this.i18n.t('shared.bulkDone', { count: result.updated, versions: result.newVersions }));
      this.usage.set(null);
      this.reload();
    });
  }
}

export function toForm(group: SharedStepGroup): EditForm {
  return {
    id: group.id,
    name: group.name,
    description: group.description ?? '',
    steps: group.steps.map(s => ({ id: s.id, action: s.action, expectedResult: s.expectedResult, testData: s.testData ?? '' })),
  };
}
