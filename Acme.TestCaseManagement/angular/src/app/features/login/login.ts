import { Component, inject, input, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth';
import { describeError } from '../../core/core';
import { I18nService, LanguageSwitchComponent, TranslatePipe } from '../../core/i18n/i18n';

@Component({
  selector: 'app-login',
  imports: [FormsModule, TranslatePipe, LanguageSwitchComponent],
  template: `
    <div class="login-wrap">
      <div class="lang-corner"><app-language-switch /></div>
      <form class="card login" (ngSubmit)="submit()">
        <h1>{{ 'app.name' | t }}</h1>
        <p class="muted">{{ 'login.subtitle' | t }}</p>

        @if (error()) { <div class="alert bad" role="alert">{{ error() }}</div> }

        <div class="field">
          <label for="login-user">{{ 'login.userName' | t }}</label>
          <input id="login-user" name="userName" autocomplete="username" required [(ngModel)]="userName" />
        </div>
        <div class="field">
          <label for="login-password">{{ 'login.password' | t }}</label>
          <input id="login-password" name="password" type="password" autocomplete="current-password" required [(ngModel)]="password" />
        </div>
        <button type="submit" class="btn primary" style="width: 100%" [disabled]="busy()">{{ (busy() ? 'login.busy' : 'login.submit') | t }}</button>

        <p class="muted hint">{{ 'login.demoAccounts' | t }} <span class="mono">qa.lead</span>, <span class="mono">product.owner</span>, <span class="mono">tester</span>.</p>
      </form>
    </div>
  `,
  styles: `
    .login-wrap { min-height: 100vh; display: grid; place-items: center; padding: 16px; position: relative; }
    .lang-corner { position: absolute; top: 16px; right: 16px; }
    .login { width: min(400px, 100%); }
    h1 { margin-bottom: 4px; }
    .hint { font-size: .8rem; margin: 16px 0 0; }
  `,
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  /** Bound from the query string: where to go after signing in. */
  readonly returnUrl = input<string>();

  protected userName = '';
  protected password = '';
  protected readonly busy = signal(false);
  /** Kept as a state, not as text, so that it follows the language when the user switches it. */
  private readonly failure = signal<HttpErrorResponse | null>(null);
  protected error(): string {
    const failure = this.failure();
    if (!failure) { return ''; }
    // The API answers every refused login the same way; the text shown is the one of the chosen language.
    return failure.status === 401 ? this.i18n.t('login.invalid') : describeError(failure, this.i18n);
  }

  protected submit(): void {
    this.busy.set(true);
    this.failure.set(null);
    this.auth.login(this.userName.trim(), this.password).subscribe({
      next: async () => {
        await this.auth.loadPermissions();
        this.busy.set(false);
        const target = this.returnUrl();
        await this.router.navigateByUrl(target && target.startsWith('/') ? target : '/repository');
      },
      error: error => {
        this.busy.set(false);
        this.failure.set(error);
      },
    });
  }
}
