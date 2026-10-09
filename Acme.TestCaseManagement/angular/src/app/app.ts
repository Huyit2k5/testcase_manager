import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { LanguageSwitchComponent, TCM_FEATURES, ToastService, TranslatePipe, tcmMenu } from 'test-case-management';
import { LocalAuthService } from './core/local-auth';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe, LanguageSwitchComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly toasts = inject(ToastService);
  protected readonly auth = inject(LocalAuthService);

  protected readonly tabs = tcmMenu(inject(TCM_FEATURES)).map(item => ({ path: `/${item.path}`, label: item.label, permission: item.permission }));

  /** The tabs the signed-in user may open; a tab without a permission is open to everyone who is signed in. */
  protected readonly visibleTabs = computed(() => this.tabs.filter(tab => !tab.permission || this.auth.can(tab.permission)));

  protected logout(): void {
    this.auth.logout();
  }
}
