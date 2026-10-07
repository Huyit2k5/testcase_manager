import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { catchError, from, map, of, switchMap, throwError } from 'rxjs';
import { TCM_NOTIFIER } from './host';
import { I18nService } from './i18n/i18n';

// ---- Toasts -------------------------------------------------------------------------------------------------------

export interface Toast { id: number; kind: 'success' | 'error' | 'info'; text: string }

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private nextId = 1;
  /** The host's own notification area, when it has one: the message goes there and no toast of the module is shown. */
  private readonly notifier = inject(TCM_NOTIFIER, { optional: true });

  success(text: string): void { this.notifier ? this.notifier.success(text) : this.push('success', text, 3500); }
  info(text: string): void { this.notifier ? this.notifier.info(text) : this.push('info', text, 3500); }
  error(text: string): void { this.notifier ? this.notifier.error(text) : this.push('error', text, 8000); }

  dismiss(id: number): void { this.toasts.update(list => list.filter(t => t.id !== id)); }

  private push(kind: Toast['kind'], text: string, ms: number): void {
    const id = this.nextId++;
    this.toasts.update(list => [...list, { id, kind, text }]);
    setTimeout(() => this.dismiss(id), ms);
  }
}

// ---- Errors -------------------------------------------------------------------------------------------------------

/** Reads the ABP error envelope: { error: { code, message, validationErrors: [{ message, members }] } }. */
export function describeError(error: HttpErrorResponse, i18n: I18nService): string {
  const info = error.error?.error;
  if (info?.validationErrors?.length) {
    return info.validationErrors
      .map((v: { message: string; members?: string[] }) => (v.members?.length ? `${v.members.join(', ')}: ${v.message}` : v.message))
      .join('\n');
  }
  if (info?.message) {
    return info.message;
  }
  switch (error.status) {
    case 0: return i18n.t('error.network');
    case 401: return i18n.t('error.unauthorized');
    case 403: return i18n.t('error.forbidden');
    case 404: return i18n.t('error.notFound');
    default: return i18n.t('error.other', { status: error.status });
  }
}

export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);
  const i18n = inject(I18nService);
  return next(request).pipe(
    catchError((error: HttpErrorResponse) =>
      readBody(error).pipe(
        switchMap(readable => {
          // A 401 is answered by the auth interceptor (sign out) or by the login form itself.
          if (readable.status !== 401) {
            toast.error(describeError(readable, i18n));
          }
          return throwError(() => readable);
        }),
      ),
    ),
  );
};

/**
 * A failed download arrives with its error body as a blob. Reads it as JSON, so that the message of the API (which
 * says, for example, that an export is too large) is what the user sees.
 */
export function readBody(error: HttpErrorResponse) {
  if (!(error.error instanceof Blob)) {
    return of(error);
  }
  return from(error.error.text()).pipe(
    map(text => {
      let body: unknown = null;
      try { body = JSON.parse(text); } catch { /* not JSON: the status alone is described */ }
      return new HttpErrorResponse({ error: body, headers: error.headers, status: error.status, statusText: error.statusText, url: error.url ?? undefined });
    }),
  );
}
