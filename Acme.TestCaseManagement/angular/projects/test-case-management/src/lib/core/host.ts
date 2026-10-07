import { InjectionToken, Signal, inject } from '@angular/core';

/**
 * What the module needs from the application it runs in. The standalone app of this repository and an ABP
 * application each provide these; nothing in the module knows which one it is.
 */

/** The origin of the API, with no trailing slash: empty when the API is served from the same origin as the page. */
export const TCM_API_URL = new InjectionToken<string>('TCM_API_URL', { providedIn: 'root', factory: () => '' });

/** Where the pages of the module are mounted in the router, with no trailing slash: empty at the root. */
export const TCM_BASE_PATH = new InjectionToken<string>('TCM_BASE_PATH', { providedIn: 'root', factory: () => '' });

/** The language of the host when the host owns the language switch; without it the module keeps its own choice. */
export const TCM_LANGUAGE = new InjectionToken<Signal<string>>('TCM_LANGUAGE');

/** Shows a message in the host's own notification area; without it the module shows its own toasts. */
export interface TcmNotifier {
  success(text: string): void;
  info(text: string): void;
  error(text: string): void;
}
export const TCM_NOTIFIER = new InjectionToken<TcmNotifier>('TCM_NOTIFIER');

/** The root of the API of the module, for the services that call it. Call it while a service is being built. */
export function apiRoot(): string {
  return `${inject(TCM_API_URL)}/api/test-case-management`;
}

/** True when the pages should switch to the dark colours with the operating system (the standalone app does; an ABP theme owns its colours). */
export const TCM_FOLLOW_OS_THEME = new InjectionToken<boolean>('TCM_FOLLOW_OS_THEME', { providedIn: 'root', factory: () => false });
