import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { AuthService, TCM_FOLLOW_OS_THEME, TranslatedTitleStrategy, errorInterceptor, languageInterceptor } from 'test-case-management';
import { routes } from './app.routes';
import { LocalAuthService, authInterceptor } from './core/local-auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useExisting: TranslatedTitleStrategy },
    { provide: AuthService, useExisting: LocalAuthService },
    { provide: TCM_FOLLOW_OS_THEME, useValue: true },
    provideAppInitializer(() => inject(LocalAuthService).restore()),
    provideHttpClient(withInterceptors([languageInterceptor, authInterceptor, errorInterceptor])),
  ],
};
