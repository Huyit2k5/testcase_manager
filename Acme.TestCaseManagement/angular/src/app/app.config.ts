import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { AuthService, authInterceptor } from './core/auth';
import { errorInterceptor } from './core/core';
import { TranslatedTitleStrategy, languageInterceptor } from './core/i18n/i18n';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withComponentInputBinding()),
    { provide: TitleStrategy, useExisting: TranslatedTitleStrategy },
    provideAppInitializer(() => inject(AuthService).restore()),
    provideHttpClient(withInterceptors([languageInterceptor, authInterceptor, errorInterceptor])),
  ],
};
