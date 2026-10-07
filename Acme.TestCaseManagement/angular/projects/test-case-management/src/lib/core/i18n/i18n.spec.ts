import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import * as enums from '../../proxy/enums';
import { en } from './en';
import { I18nService, languageInterceptor } from './i18n';
import { vi } from './vi';

const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map(m => m[1]).sort();

describe('dictionaries', () => {
  it('have the same keys in both languages', () => {
    expect(Object.keys(vi).sort()).toEqual(Object.keys(en).sort());
  });

  it('use the same {placeholders} in both languages', () => {
    for (const key of Object.keys(en) as (keyof typeof en)[]) {
      expect(placeholders(vi[key]), key).toEqual(placeholders(en[key]));
    }
  });

  it('have no empty text', () => {
    for (const [key, text] of [...Object.entries(en), ...Object.entries(vi)]) {
      expect(text.trim(), key).not.toBe('');
    }
  });

  it('name every member of every enum', () => {
    // The transition tables of the enums module are records of arrays, not enums, so only numeric enums are looked at.
    const numericEnums = Object.entries(enums).filter(
      ([, value]) => typeof value === 'object' && !Array.isArray(value) && Object.values(value as object).some(v => typeof v === 'number'),
    ) as [string, object][];
    expect(numericEnums.length).toBeGreaterThanOrEqual(11);
    for (const [name, type] of numericEnums) {
      for (const member of enums.enumOptions(type)) {
        const key = `enum.${name}.${member.label}`;
        expect(key in en, key).toBe(true);
        expect(key in vi, key).toBe(true);
      }
    }
  });
});

describe('I18nService', () => {
  let i18n: I18nService;

  beforeEach(() => {
    localStorage.clear();
    i18n = TestBed.inject(I18nService);
    i18n.use('en');
  });

  it('translates and fills in placeholders', () => {
    expect(i18n.t('repo.page', { page: 3 })).toBe('Page 3');
    i18n.use('vi');
    expect(i18n.t('repo.page', { page: 3 })).toBe('Trang 3');
  });

  it('returns an unknown key as it is, so that a gap stays visible', () => {
    expect(i18n.t('no.such.key')).toBe('no.such.key');
  });

  it('names enum members in the current language', () => {
    expect(i18n.enumText(enums.TestCaseStatus, enums.TestCaseStatus.UnderReview)).toBe('Under review');
    i18n.use('vi');
    expect(i18n.enumText(enums.TestCaseStatus, enums.TestCaseStatus.UnderReview)).toBe('Chờ duyệt');
    expect(i18n.enumText(enums.TestResultStatus, enums.TestResultStatus.Failed)).toBe('Không đạt');
    expect(i18n.enumText(enums.TestCaseStatus, null)).toBe('');
  });

  it('remembers the language and sets it on the document', () => {
    i18n.use('vi');
    expect(localStorage.getItem('tcm.lang')).toBe('vi');
    expect(document.documentElement.lang).toBe('vi');
  });

  it('formats dates in the current language', () => {
    expect(i18n.date('2026-03-05T10:30:00', 'mediumDate')).toBe('Mar 5, 2026');
    i18n.use('vi');
    expect(i18n.date('2026-03-05T10:30:00', 'mediumDate')).toContain('5');
    expect(i18n.date('2026-03-05T10:30:00', 'mediumDate')).not.toContain('Mar');
    expect(i18n.date(null, 'short')).toBe('');
  });
});

describe('languageInterceptor', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([languageInterceptor])), provideHttpClientTesting()],
    });
  });

  it('asks the API to answer in the language of the user', () => {
    const http = TestBed.inject(HttpClient);
    const controller = TestBed.inject(HttpTestingController);
    const i18n = TestBed.inject(I18nService);

    i18n.use('vi');
    http.get('/api/test-case-management/suites').subscribe();
    expect(controller.expectOne('/api/test-case-management/suites').request.headers.get('Accept-Language')).toBe('vi');

    i18n.use('en');
    http.get('/api/test-case-management/suites').subscribe();
    expect(controller.expectOne('/api/test-case-management/suites').request.headers.get('Accept-Language')).toBe('en');
  });

  it('leaves other requests alone', () => {
    TestBed.inject(HttpClient).get('/assets/x.json').subscribe();
    expect(TestBed.inject(HttpTestingController).expectOne('/assets/x.json').request.headers.has('Accept-Language')).toBe(false);
  });
});
