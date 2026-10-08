import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService, Permissions } from '../../core/auth';
import { ToastService } from '../../core/core';
import { TCM_API_URL } from '../../core/host';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { ModalComponent } from '../../core/modal';
import { ApiKey, ApiKeyCreated } from '../../proxy/dtos';
import { ApiKeyService } from '../../proxy/services';
import { ConfirmService } from '../../core/confirm';

const ENDPOINT = '/api/test-case-management/automation/results';

/** The body of a result request, as the pipeline sends it. Kept here so the page and its tests share one example. */
export const SAMPLE_BODY = {
  run: { title: 'CI build #123', environment: 'staging', buildVersion: '1.4.2' },
  completeRun: true,
  results: [
    { automationId: 'e2e.login.valid', status: 'Passed', durationSeconds: 12 },
    { automationId: 'e2e.checkout.card', status: 'Failed', actualResult: 'The card was declined', durationSeconds: 30 },
  ],
};

export function curlExample(origin: string, key: string): string {
  return [
    `curl -X POST ${origin}${ENDPOINT} \\`,
    `  -H "X-Api-Key: ${key}" \\`,
    `  -H "Idempotency-Key: build-123" \\`,
    `  -H "Content-Type: application/json" \\`,
    `  -d '${JSON.stringify(SAMPLE_BODY)}'`,
  ].join('\n');
}

export function githubActionsExample(origin: string): string {
  return [
    '- name: Publish results to Test Case Management',
    '  if: always()',
    '  run: |',
    `    curl --fail-with-body -X POST ${origin}${ENDPOINT} \\`,
    '      -H "X-Api-Key: ${{ secrets.TCM_API_KEY }}" \\',
    '      -H "Idempotency-Key: ${{ github.run_id }}-${{ github.run_attempt }}" \\',
    '      -H "Content-Type: application/json" \\',
    '      -d @results.json',
  ].join('\n');
}

@Component({
  selector: 'app-automation',
  imports: [FormsModule, FormatDatePipe, TranslatePipe, ModalComponent],
  templateUrl: './automation.html',
})
export class AutomationComponent implements OnInit {
  private readonly service = inject(ApiKeyService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  private readonly confirmer = inject(ConfirmService);
  protected readonly auth = inject(AuthService);
  protected readonly perm = Permissions;

  protected readonly keys = signal<ApiKey[]>([]);
  protected readonly loading = signal(true);
  protected readonly form = signal<{ name: string; expiresOn: string } | null>(null);
  /** The key just created; its secret is on this screen and nowhere else, and is gone once the dialog is closed. */
  protected readonly created = signal<ApiKeyCreated | null>(null);
  protected readonly copied = signal(false);
  protected readonly saving = signal(false);

  protected readonly origin = inject(TCM_API_URL) || (typeof location === 'undefined' ? '' : location.origin);
  protected readonly githubExample = githubActionsExample(this.origin);
  protected readonly activeCount = computed(() => this.keys().filter(k => k.isActive).length);

  protected curl(key: string): string { return curlExample(this.origin, key); }

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    this.service.list().subscribe({
      next: keys => { this.keys.set(keys); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  protected state(key: ApiKey): 'active' | 'revoked' | 'expired' {
    return key.revokedAt ? 'revoked' : key.isActive ? 'active' : 'expired';
  }

  protected newKey(): void { this.form.set({ name: '', expiresOn: '' }); }

  protected save(): void {
    const form = this.form();
    // Enter submits the form even while the button is disabled, and a second key would be a second secret.
    if (!form || !form.name.trim() || this.saving()) { return; }
    // The date is the end of that day where the user is, sent as an instant: the list shows it in local time too, so the day stays the same.
    const expiresAt = form.expiresOn ? new Date(`${form.expiresOn}T23:59:59`).toISOString() : null;
    this.saving.set(true);
    this.service.create({ name: form.name.trim(), expiresAt }).subscribe({
      next: key => {
        this.saving.set(false);
        this.form.set(null);
        this.copied.set(false);
        this.created.set(key);
        this.reload();
      },
      error: () => this.saving.set(false),
    });
  }

  protected async copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
    } catch {
      // Clipboard access can be refused; the text stays selectable on the screen.
      this.toast.info(this.i18n.t('auto.copyFailed'));
    }
  }

  protected closeCreated(): void { this.created.set(null); }

  protected revoke(key: ApiKey): void {
    this.confirmer.ask({ message: this.i18n.t('auto.confirmRevoke', { name: key.name }), confirmText: this.i18n.t('auto.revoke'), danger: true }).subscribe(ok => {
      if (!ok) { return; }
      this.service.revoke(key.id).subscribe(() => {
        this.toast.success(this.i18n.t('auto.revoked'));
        this.reload();
      });
    });
  }
}
