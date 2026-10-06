import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth';
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
    { path: '/repository', label: 'nav.repository' },
    { path: '/runs', label: 'nav.runs' },
    { path: '/traceability', label: 'nav.traceability' },
    { path: '/quality', label: 'nav.quality' },
  ];

  protected logout(): void {
    this.auth.logout();
  }
}
