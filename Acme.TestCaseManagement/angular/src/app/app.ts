import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService, Permissions } from './core/auth';
import { ToastService } from './core/core';
import { LanguageSwitchComponent, TranslatePipe } from './core/i18n/i18n';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LanguageSwitchComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly toasts = inject(ToastService);
  protected readonly auth = inject(AuthService);

  protected readonly tabs = [
    { path: '/dashboard', label: 'nav.dashboard', permission: Permissions.TestRuns.Default },
    { path: '/repository', label: 'nav.repository' },
    { path: '/shared-steps', label: 'nav.sharedSteps', permission: Permissions.SharedSteps.Default },
    { path: '/runs', label: 'nav.runs' },
    { path: '/traceability', label: 'nav.traceability' },
    { path: '/quality', label: 'nav.quality' },
    { path: '/automation', label: 'nav.automation', permission: Permissions.ApiKeys.Default },
  ];

  /** The tabs the signed-in user may open; a tab without a permission is open to everyone who is signed in. */
  protected readonly visibleTabs = computed(() => this.tabs.filter(tab => !tab.permission || this.auth.can(tab.permission)));

  protected logout(): void {
    this.auth.logout();
  }
}
