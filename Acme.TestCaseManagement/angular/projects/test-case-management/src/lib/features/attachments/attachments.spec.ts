import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { formatSize } from '../../core/ui';
import { Attachment } from '../../proxy/dtos';
import { AttachmentsComponent } from './attachments';

const URL_LIST = '/api/test-case-management/attachments';

const file = (patch: Partial<Attachment>): Attachment => ({
  id: 'a1', ownerType: 0, ownerId: 'owner-1', fileName: 'shot.png', contentType: 'image/png', size: 2048, sha256: 'abc',
  description: null, creationTime: '2026-03-01T10:00:00Z', creatorId: null, ...patch,
});

describe('formatSize', () => {
  it('writes sizes for people', () => {
    expect([0, 532, 1023, 1024, 1536, 4300, 10 * 1024, 5 * 1024 * 1024, 25 * 1024 * 1024, 1024 ** 3].map(formatSize))
      .toEqual(['0 B', '532 B', '1023 B', '1 KB', '1.5 KB', '4.2 KB', '10 KB', '5 MB', '25 MB', '1 GB']);
  });
});

describe('AttachmentsComponent', () => {
  let http: HttpTestingController;
  let component: AttachmentsComponent;
  const call = <T>(name: string, ...args: unknown[]) => (component as unknown as Record<string, (...a: unknown[]) => T>)[name](...args);
  const read = <T>(name: string) => (component as unknown as Record<string, () => T>)[name]();

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AttachmentsComponent);
    fixture.componentRef.setInput('ownerType', 0);
    fixture.componentRef.setInput('ownerId', 'owner-1');
    fixture.componentRef.setInput('canWrite', true);
    component = fixture.componentInstance;
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: () => 'blob:thumb', revokeObjectURL: () => undefined }));
  });

  afterEach(() => vi.unstubAllGlobals());

  const start = (files: Attachment[]) => {
    component.ngOnInit();
    const list = http.expectOne(r => r.url === URL_LIST);
    expect(list.request.params.get('OwnerType')).toBe('0');
    expect(list.request.params.getAll('OwnerIds')).toEqual(['owner-1']);
    list.flush(files);
  };

  it('lists the files of its owner and fetches a thumbnail for images only', () => {
    start([file({ id: 'img' }), file({ id: 'log', fileName: 'run.log', contentType: 'text/plain' })]);

    http.expectOne('/api/test-case-management/attachments/img/content').flush(new Blob(['x']));
    http.expectNone('/api/test-case-management/attachments/log/content');

    expect(read<Attachment[]>('files').length).toBe(2);
    expect(read<Record<string, string>>('thumbs')['img']).toBe('blob:thumb');
    expect(call<string>('extension', file({ fileName: 'run.log' }))).toBe('LOG');
    expect(call<string>('extension', file({ fileName: 'archive.tar.gz' }))).toBe('GZ');
    expect(call<string>('extension', file({ fileName: 'README' }))).toBe('');
  });

  it('uploads the files one after the other, goes on when one is refused, and then reloads', () => {
    start([]);
    const first = new File(['a'], 'a.png', { type: 'image/png' });
    const second = new File(['b'], 'b.exe');
    const third = new File(['c'], 'c.txt');

    call('uploadAll', [first, second, third]);
    expect(read<boolean>('busy')).toBe(true);

    const one = http.expectOne(URL_LIST);
    expect(one.request.method).toBe('POST');
    const form = one.request.body as FormData;
    expect(form.get('OwnerType')).toBe('0');
    expect(form.get('OwnerId')).toBe('owner-1');
    expect((form.get('File') as File).name).toBe('a.png');
    one.flush(file({}));

    http.expectOne(URL_LIST).flush({ error: { message: 'no' } }, { status: 403, statusText: 'Forbidden' });
    const last = http.expectOne(URL_LIST);
    expect(((last.request.body as FormData).get('File') as File).name).toBe('c.txt');
    last.flush(file({ id: 'a3', fileName: 'c.txt', contentType: 'text/plain' }));

    expect(read<boolean>('busy')).toBe(false);
    http.expectOne(r => r.method === 'GET' && r.url === URL_LIST).flush([]);
  });

  it('ignores a second drop while it is still uploading', () => {
    start([]);
    call('uploadAll', [new File(['a'], 'a.txt')]);
    call('uploadAll', [new File(['b'], 'b.txt')]);

    expect(http.match(URL_LIST).length).toBe(1);
  });

  it('names a pasted screenshot after the time and leaves a pasted text alone', () => {
    start([]);
    const image = new File(['x'], 'image.png', { type: 'image/png' });
    const event = (files: File[]) => ({ clipboardData: { files }, preventDefault: vi.fn() }) as unknown as ClipboardEvent;

    const text = event([]);
    call('paste', text);
    expect(text.preventDefault).not.toHaveBeenCalled();
    http.expectNone(URL_LIST);

    const pasted = event([image]);
    call('paste', pasted);
    expect(pasted.preventDefault).toHaveBeenCalled();
    const request = http.expectOne(URL_LIST);
    expect(((request.request.body as FormData).get('File') as File).name).toMatch(/^screenshot-\d{14}\.png$/);
    request.flush(file({}));
    http.expectOne(r => r.method === 'GET').flush([]);
  });

  it('deletes after a confirmation only, and reloads', () => {
    start([file({ id: 'gone', contentType: 'text/plain', fileName: 'old.txt' })]);

    vi.stubGlobal('confirm', () => false);
    call('remove', file({ id: 'gone' }));
    http.expectNone('/api/test-case-management/attachments/gone');

    vi.stubGlobal('confirm', () => true);
    call('remove', file({ id: 'gone' }));
    const request = http.expectOne('/api/test-case-management/attachments/gone');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    http.expectOne(r => r.method === 'GET' && r.url === URL_LIST).flush([]);
  });

  it('creates no object URL for a thumbnail that arrives after the panel is gone, and revokes the ones it made', () => {
    const created: string[] = [];
    const revoked: string[] = [];
    vi.stubGlobal('URL', Object.assign(URL, {
      createObjectURL: () => { const url = `blob:${created.length}`; created.push(url); return url; },
      revokeObjectURL: (url: string) => { revoked.push(url); },
    }));
    const fixture = TestBed.createComponent(AttachmentsComponent);
    fixture.componentRef.setInput('ownerType', 0);
    fixture.componentRef.setInput('ownerId', 'owner-1');
    fixture.componentInstance.ngOnInit();
    http.expectOne(r => r.url === URL_LIST).flush([file({ id: 'fast' }), file({ id: 'slow' })]);
    http.expectOne('/api/test-case-management/attachments/fast/content').flush(new Blob(['x']));
    const slow = http.expectOne('/api/test-case-management/attachments/slow/content');

    fixture.destroy();

    expect(slow.cancelled).toBe(true);
    expect(created.length).toBe(1);
    expect(revoked).toEqual(created);
  });

  type Preview = { file: Attachment; kind: string; url: string; text: string; cut: boolean } | null;

  it('shows an image in a dialog from the file it already downloaded for the thumbnail, without fetching it again', () => {
    start([file({ id: 'img' })]);
    http.expectOne('/api/test-case-management/attachments/img/content').flush(new Blob(['x']));

    call('open', file({ id: 'img' }));

    http.expectNone('/api/test-case-management/attachments/img/content');
    expect(read<Preview>('preview')).toMatchObject({ kind: 'image', url: 'blob:thumb' });
    call('closePreview');
    expect(read<Preview>('preview')).toBeNull();
  });

  it('shows the text of a log, and says when only the beginning is shown', () => {
    const log = file({ id: 'log', fileName: 'console.log', contentType: 'application/octet-stream' });
    start([log]);
    call('open', log);
    http.expectOne('/api/test-case-management/attachments/log/content').flush(new Blob(['line 1 line 2']));

    return vi.waitFor(() => expect(read<Preview>('preview')).toMatchObject({ kind: 'text', text: 'line 1 line 2', cut: false }));
  });

  it('does not show a file of another type: it offers the download instead of an empty dialog', () => {
    const zip = file({ id: 'zip', fileName: 'dump.zip', contentType: 'application/zip' });
    start([zip]);
    call('open', zip);
    // The download is asked for; nothing opens.
    http.expectOne('/api/test-case-management/attachments/zip/content');
    expect(read<Preview>('preview')).toBeNull();
  });

  it('ignores a file that arrives after the dialog was closed', () => {
    const log = file({ id: 'late', fileName: 'late.txt', contentType: 'text/plain' });
    start([log]);
    call('open', log);
    call('closePreview');
    http.expectOne('/api/test-case-management/attachments/late/content').flush(new Blob(['late']));

    return new Promise<void>(resolve => setTimeout(() => { expect(read<Preview>('preview')).toBeNull(); resolve(); }, 20));
  });
});
