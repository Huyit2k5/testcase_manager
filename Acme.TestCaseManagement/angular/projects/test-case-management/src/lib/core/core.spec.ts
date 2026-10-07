import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { describeError } from './core';
import { I18nService } from './i18n/i18n';
import { TestCaseStatus, enumLabel, enumOptions } from '../proxy/enums';
import { words } from './ui';

describe('describeError', () => {
  let i18n: I18nService;

  beforeEach(() => {
    localStorage.clear();
    i18n = TestBed.inject(I18nService);
    i18n.use('en');
  });

  it('uses the message of the ABP error envelope', () => {
    const error = new HttpErrorResponse({ status: 403, error: { error: { code: 'X', message: 'Quality gate was not passed.' } } });
    expect(describeError(error, i18n)).toBe('Quality gate was not passed.');
  });

  it('lists validation errors with their members', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { error: { validationErrors: [{ message: 'The Name field is required.', members: ['Name'] }] } },
    });
    expect(describeError(error, i18n)).toBe('Name: The Name field is required.');
  });

  it('falls back to the status when the body is empty', () => {
    expect(describeError(new HttpErrorResponse({ status: 403 }), i18n)).toContain('permission');
    expect(describeError(new HttpErrorResponse({ status: 0 }), i18n)).toContain('Cannot reach the API');
    expect(describeError(new HttpErrorResponse({ status: 500 }), i18n)).toBe('Request failed (500).');
  });

  it('writes the fallback in the language of the user', () => {
    i18n.use('vi');
    expect(describeError(new HttpErrorResponse({ status: 403 }), i18n)).toBe('Bạn không có quyền thực hiện thao tác này.');
    expect(describeError(new HttpErrorResponse({ status: 500 }), i18n)).toBe('Yêu cầu thất bại (500).');
  });
});

describe('enums', () => {
  it('lists numeric members only', () => {
    expect(enumOptions(TestCaseStatus).map(o => o.label)).toEqual(['Draft', 'UnderReview', 'Approved', 'Deprecated']);
    expect(enumLabel(TestCaseStatus, 2)).toBe('Approved');
  });

  it('writes member names as words', () => {
    expect(words('UnderReview')).toBe('Under review');
  });
});
