import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { grant } from '../../core/auth-testing';
import { TCM_FEATURES } from '../../core/host';
import { RepositoryComponent } from './repository';
import { TestCaseFormComponent } from './test-case-form';

const ROOT = '/api/test-case-management';

describe('The automation part on the repository screens', () => {
  let http: HttpTestingController;

  function repository(extra: Provider[]) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*'), ...extra] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(RepositoryComponent);
    fixture.detectChanges();
    http.match(() => true).forEach(r => { if (!r.cancelled) { r.flush(r.request.url === `${ROOT}/test-cases` ? { items: [], totalCount: 0 } : []); } });
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function form(extra: Provider[]) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), grant('*'), ...extra] });
    const fixture = TestBed.createComponent(TestCaseFormComponent);
    fixture.componentRef.setInput('suites', [{ id: 's1', label: 'S' }]);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).match(() => true).forEach(r => r.flush([]));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => localStorage.clear());

  it('has no filter on the automation id and no field for it while the part is off, and has both when it is on', () => {
    const off = repository([]);
    expect(off.querySelector('select[name=automation]')).toBeNull();
    expect(off.querySelector('select[name=priority]')).not.toBeNull();

    const on = repository([{ provide: TCM_FEATURES, useValue: { automation: true } }]);
    expect(on.querySelector('select[name=automation]')).not.toBeNull();

    expect(form([]).querySelector('#tc-auto')).toBeNull();
    expect(form([{ provide: TCM_FEATURES, useValue: { automation: true } }]).querySelector('#tc-auto')).not.toBeNull();
  });
});
