import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TestCaseService } from '../../proxy/services';
import { TAG_MAX_COUNT, TAG_MAX_LENGTH, TagInputComponent, addTag, cleanTag } from './tag-input';

describe('cleanTag', () => {
  it('trims the ends and makes the white space inside one space', () => {
    expect(cleanTag('  Payments   API\t')).toBe('Payments API');
    expect(cleanTag('   ')).toBe('');
  });
});

describe('addTag', () => {
  it('adds a tag, but not an empty one or one that is there (ignoring case)', () => {
    expect(addTag([], ' smoke ')).toEqual(['smoke']);
    expect(addTag(['smoke'], 'SMOKE')).toEqual(['smoke']);
    expect(addTag(['smoke'], '   ')).toEqual(['smoke']);
    expect(addTag(['smoke'], 'api')).toEqual(['smoke', 'api']);
  });

  it('cuts a tag at the longest the server accepts and stops at the most tags', () => {
    expect(addTag([], 'x'.repeat(80))[0].length).toBe(TAG_MAX_LENGTH);
    const full = Array.from({ length: TAG_MAX_COUNT }, (_, i) => `t${i}`);
    expect(addTag(full, 'one more')).toBe(full);
  });
});

describe('TagInputComponent', () => {
  let component: TagInputComponent;
  let emitted: string[][];
  const call = (name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => void>)[name](...args);
  const set = (name: string, value: unknown) => ((component as unknown as Record<string, unknown>)[name] = value);
  const key = (k: string) => ({ key: k, preventDefault: () => undefined }) as unknown as KeyboardEvent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    component = TestBed.createComponent(TagInputComponent).componentInstance;
    emitted = [];
    component.edited.subscribe(tags => emitted.push(tags));
  });

  it('adds on Enter, comma and semicolon, and a pasted list becomes several tags', () => {
    set('draft', 'smoke');
    call('key', key('Enter'));
    set('draft', 'api');
    call('key', key(','));
    set('draft', 'ui; payments, Smoke');
    call('commit');

    expect(component.tags()).toEqual(['smoke', 'api', 'ui', 'payments']);
    expect(emitted.length).toBe(3);
  });

  it('takes the last tag away on Backspace in an empty field only', () => {
    component.tags.set(['a', 'b']);
    set('draft', 'x');
    call('key', key('Backspace'));
    expect(component.tags()).toEqual(['a', 'b']);

    set('draft', '');
    call('key', key('Backspace'));
    expect(component.tags()).toEqual(['a']);
    expect(emitted).toEqual([['a']]);
  });

  it('removes one tag and says nothing when nothing changed', () => {
    component.tags.set(['a', 'b']);
    call('remove', 'a');
    expect(component.tags()).toEqual(['b']);

    set('draft', '  ');
    call('commit');
    set('draft', 'B');
    call('commit');
    expect(emitted.length).toBe(1);
  });
});

describe('the list request', () => {
  it('sends the tags as a repeated parameter and the automation filter as a boolean', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);

    TestBed.inject(TestCaseService).list({ tags: ['smoke', 'Payments API'], hasAutomationId: false, skipCount: 0, maxResultCount: 20 }).subscribe();

    const request = http.expectOne(r => r.url === '/api/test-case-management/test-cases');
    expect(request.request.params.getAll('Tags')).toBeNull();
    expect(request.request.params.getAll('tags')).toEqual(['smoke', 'Payments API']);
    expect(request.request.params.get('hasAutomationId')).toBe('false');
    request.flush({ items: [], totalCount: 0 });
  });
});
