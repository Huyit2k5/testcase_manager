import { HttpInterceptorFn } from '@angular/common/http';
import { Component, Injectable, Pipe, PipeTransform, effect, inject, signal } from '@angular/core';
import { formatDate, registerLocaleData } from '@angular/common';
import localeVi from '@angular/common/locales/vi';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import {
  AttachmentOwnerType, ExecutionType, FlakinessLevel, ImportConflictMode, ImportOutcome, PlanStatus, PriorityLevel, RequirementCoverageStatus, RunStatus, SeverityLevel,
  SignOffStatus, TestCaseStatus, TestKind, TestLayer, TestResultStatus, TransferFormat, enumLabel,
} from '../../proxy/enums';
import { words } from '../ui';
import { en } from './en';
import { vi } from './vi';

registerLocaleData(localeVi, 'vi');

export type Lang = 'en' | 'vi';
export type MessageParams = Record<string, string | number | null | undefined>;

/** The languages of the app: the code is also what is sent as Accept-Language. */
export const LANGUAGES: readonly { code: Lang; label: string }[] = [
  { code: 'en', label: 'English' },
  { code: 'vi', label: 'Tiếng Việt' },
];

const MESSAGES: Record<Lang, Record<string, string>> = { en, vi };
const STORAGE_KEY = 'tcm.lang';

/** TypeScript enum object to the name used in the dictionaries (enum.<Name>.<Member>). */
const ENUM_NAMES = new Map<object, string>([
  [PriorityLevel, 'PriorityLevel'], [SeverityLevel, 'SeverityLevel'], [TestCaseStatus, 'TestCaseStatus'],
  [TestResultStatus, 'TestResultStatus'], [ExecutionType, 'ExecutionType'], [TestKind, 'TestKind'], [TestLayer, 'TestLayer'],
  [PlanStatus, 'PlanStatus'], [RunStatus, 'RunStatus'], [RequirementCoverageStatus, 'RequirementCoverageStatus'],
  [SignOffStatus, 'SignOffStatus'], [TransferFormat, 'TransferFormat'], [ImportConflictMode, 'ImportConflictMode'], [ImportOutcome, 'ImportOutcome'],
  [FlakinessLevel, 'FlakinessLevel'], [AttachmentOwnerType, 'AttachmentOwnerType'],
]);

function isLang(value: unknown): value is Lang {
  return value === 'en' || value === 'vi';
}

/** The remembered choice, else the browser language (Vietnamese when it starts with vi), else English. */
function initialLanguage(): Lang {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (isLang(stored)) { return stored; }
  } catch { /* storage may be unavailable */ }
  return typeof navigator !== 'undefined' && navigator.language?.toLowerCase().startsWith('vi') ? 'vi' : 'en';
}

/**
 * Runtime translation. Reading lang() inside a template or computed registers the dependency, so switching the
 * language re-renders the screen without a reload.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  readonly lang = signal<Lang>(initialLanguage());

  constructor() {
    this.applyToDocument(this.lang());
  }

  use(lang: Lang): void {
    this.lang.set(lang);
    this.applyToDocument(lang);
    try { localStorage.setItem(STORAGE_KEY, lang); } catch { /* storage may be unavailable */ }
  }

  /** The text of a key with {placeholders} filled in; an unknown key is returned as it is, so a gap stays visible. */
  t(key: string, params?: MessageParams): string {
    const text = MESSAGES[this.lang()][key] ?? en[key as keyof typeof en] ?? key;
    return params ? text.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? '')) : text;
  }

  /** The name of an enum member in the current language (UnderReview becomes "Under review" / "Chờ duyệt"). */
  enumText(type: object, value: number | null | undefined): string {
    if (value == null) { return ''; }
    const member = enumLabel(type, value);
    const name = ENUM_NAMES.get(type);
    const key = name ? `enum.${name}.${member}` : '';
    return key && key in MESSAGES[this.lang()] ? this.t(key) : words(member);
  }

  /** A date in the current language, with an Angular date format such as 'short' or 'mediumDate'. */
  date(value: string | Date | null | undefined, format: string): string {
    return value ? formatDate(value, format, this.lang() === 'vi' ? 'vi' : 'en-US') : '';
  }

  private applyToDocument(lang: Lang): void {
    if (typeof document !== 'undefined') { document.documentElement.lang = lang; }
  }
}

/** {{ 'key' | t }} or {{ 'key' | t: { name: value } }}. Impure so that it follows the language signal. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: string, params?: MessageParams): string { return this.i18n.t(key, params); }
}

/** {{ value | fdate: 'short' }}, like the date pipe but in the language of the app. */
@Pipe({ name: 'fdate', pure: false })
export class FormatDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: string | Date | null | undefined, format = 'mediumDate'): string { return this.i18n.date(value, format); }
}

/** Tells the API which language to answer in: error messages of the module follow Accept-Language. */
export const languageInterceptor: HttpInterceptorFn = (request, next) => {
  const i18n = inject(I18nService);
  return next(request.url.startsWith('/api/') ? request.clone({ setHeaders: { 'Accept-Language': i18n.lang() } }) : request);
};

/** Route titles are dictionary keys; the browser title is kept in step with the language. */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly i18n = inject(I18nService);
  private readonly key = signal<string | undefined>(undefined);

  constructor() {
    super();
    effect(() => {
      const key = this.key();
      const name = this.i18n.t('app.name');
      this.title.setTitle(key ? `${this.i18n.t(key)} - ${name}` : name);
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.key.set(this.buildTitle(snapshot));
  }
}

/** The English / Tiếng Việt buttons: the whole screen follows at once and the choice is remembered. */
@Component({
  selector: 'app-language-switch',
  template: `
    <div class="lang" role="group" [attr.aria-label]="i18n.t('lang.label')">
      @for (l of languages; track l.code) {
        <button type="button" [class.active]="i18n.lang() === l.code" [attr.aria-pressed]="i18n.lang() === l.code"
                [attr.lang]="l.code" (click)="i18n.use(l.code)">{{ l.label }}</button>
      }
    </div>
  `,
  styles: `
    .lang { display: inline-flex; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; }
    button { border: 0; background: var(--surface); color: var(--text); padding: 4px 10px; font-weight: 600; font-size: .8rem; cursor: pointer; }
    button + button { border-left: 1px solid var(--border); }
    button:hover { background: var(--surface-2); }
    button.active { background: var(--primary); color: var(--primary-contrast); }
  `,
})
export class LanguageSwitchComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly languages = LANGUAGES;
}
