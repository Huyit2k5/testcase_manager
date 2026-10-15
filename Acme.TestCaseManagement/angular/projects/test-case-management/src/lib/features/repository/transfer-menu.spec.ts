import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { RepositoryComponent } from './repository';

describe('the export and import choices of the repository page', () => {
  const create = (...permissions: string[]) => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant(...permissions)] });
    return TestBed.createComponent(RepositoryComponent).componentInstance as unknown as {
      transferItems(): { value: number; text: string }[]; onTransfer(choice: number): void; importOpen: () => boolean; noFilters(): boolean; search: string; status: number | '';
    };
  };

  beforeEach(() => localStorage.clear());

  it('offers Excel, CSV and import to someone who may create test cases', () => {
    expect(create('*').transferItems().map(i => i.text)).toEqual(['Export Excel', 'Export CSV', 'Import']);
  });

  it('offers only the two exports to someone who may not', () => {
    expect(create('TestCaseManagement.TestCases').transferItems().map(i => i.text)).toEqual(['Export Excel', 'Export CSV']);
  });

  it('opens the import dialog on the third choice', () => {
    const page = create('*');
    expect(page.importOpen()).toBe(false);
    page.onTransfer(2);
    expect(page.importOpen()).toBe(true);
  });

  it('knows an empty list from a filtered one', () => {
    const page = create('*');
    expect(page.noFilters()).toBe(true);
    page.search = 'login';
    expect(page.noFilters()).toBe(false);
    page.search = '';
    page.status = 2;
    expect(page.noFilters()).toBe(false);
  });
});
