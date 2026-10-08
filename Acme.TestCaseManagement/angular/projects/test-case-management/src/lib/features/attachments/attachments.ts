import { Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ModalComponent } from '../../core/modal';
import { ToastService } from '../../core/core';
import { FormatDatePipe, I18nService, TranslatePipe } from '../../core/i18n/i18n';
import { formatSize } from '../../core/ui';
import { Attachment } from '../../proxy/dtos';
import { AttachmentService } from '../../proxy/services';
import { saveFile } from '../../proxy/transfer';

/** How much of a text file the dialog shows. */
const PREVIEW_CHARS = 200_000;

/** The files of one test case or one execution attempt: upload (button, drop or paste), list, download, delete. */
@Component({
  selector: 'app-attachments',
  imports: [FormatDatePipe, TranslatePipe, ModalComponent],
  template: `
    <div class="attachments" [attr.data-test]="'attachments-' + ownerId()">
      @if (canWrite()) {
        <div
          class="dropzone" [class.over]="over()" tabindex="0" role="button" [attr.aria-label]="'att.drop' | t"
          (dragover)="dragOver($event)" (dragleave)="over.set(false)" (drop)="drop($event)" (paste)="paste($event)"
          (keydown.enter)="picker.click()" (click)="picker.click()">
          {{ (busy() ? 'att.uploading' : 'att.drop') | t }}
          <input #picker type="file" multiple hidden data-test="attachment-input" (click)="$event.stopPropagation()" (change)="picked($event)" />
        </div>
      }

      @if (files().length) {
        <ul class="files">
          @for (file of files(); track file.id) {
            <li>
              @if (thumbs()[file.id]; as url) {
                <button type="button" class="thumb-button" (click)="open(file)" [attr.aria-label]="'att.view' | t: { name: file.fileName }">
                  <img class="thumb" [src]="url" [alt]="file.fileName" />
                </button>
              } @else {
                <span class="thumb kind">{{ extension(file) }}</span>
              }
              <div class="info">
                <button type="button" class="link" (click)="open(file)">{{ file.fileName }}</button>
                <div class="muted">
                  {{ size(file.size) }} · {{ file.creationTime | fdate: 'short' }}@if (file.description) { · {{ file.description }} }
                </div>
              </div>
              @if (canWrite()) {
                <button type="button" class="btn sm danger" (click)="remove(file)">{{ 'common.delete' | t }}</button>
              }
            </li>
          }
        </ul>
      } @else if (!canWrite()) {
        <p class="muted">{{ 'att.none' | t }}</p>
      }
    </div>

    @if (preview(); as view) {
      <app-modal [title]="view.file.fileName" [wide]="true" (closed)="closePreview()">
        <div class="preview" data-test="attachment-preview">
          @if (view.kind === 'image') {
            <img [src]="view.url" [alt]="view.file.fileName" />
          } @else if (view.kind === 'text') {
            <pre>{{ view.text }}</pre>
            @if (view.cut) { <p class="muted">{{ 'att.cut' | t }}</p> }
          } @else {
            <p class="muted">{{ 'att.noPreview' | t }}</p>
          }
        </div>
        <ng-container slot="footer">
          <button type="button" class="btn" (click)="download(view.file)">{{ 'att.download' | t }}</button>
          <button type="button" class="btn primary" (click)="closePreview()">{{ 'common.close' | t }}</button>
        </ng-container>
      </app-modal>
    }
  `,
})
export class AttachmentsComponent implements OnInit {
  private readonly service = inject(AttachmentService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  readonly ownerType = input.required<number>();
  readonly ownerId = input.required<string>();
  readonly canWrite = input(false);

  protected readonly files = signal<Attachment[]>([]);
  /** Object URLs of the images, by attachment id; revoked when the panel goes away. */
  protected readonly thumbs = signal<Record<string, string>>({});
  protected readonly over = signal(false);
  /** The file shown in the dialog: images and text are shown, any other type only offers the download. */
  protected readonly preview = signal<{ file: Attachment; kind: 'image' | 'text' | 'other'; url: string; text: string; cut: boolean } | null>(null);
  private previewRequest = 0;
  protected readonly busy = signal(false);
  protected readonly size = formatSize;
  private readonly destroyRef = inject(DestroyRef);
  /** Thumbnails being downloaded, so that a second reload does not fetch (and create a URL for) the same image twice. */
  private readonly loading = new Set<string>();

  constructor() {
    this.destroyRef.onDestroy(() => Object.values(this.thumbs()).forEach(url => URL.revokeObjectURL(url)));
  }

  ngOnInit(): void { this.reload(); }

  private reload(): void {
    this.service.list(this.ownerType(), [this.ownerId()]).subscribe(files => {
      this.files.set(files);
      files.filter(f => f.contentType.startsWith('image/') && !this.thumbs()[f.id]).forEach(f => this.loadThumb(f));
    });
  }

  private loadThumb(file: Attachment): void {
    if (this.loading.has(file.id)) { return; }
    this.loading.add(file.id);
    // Cancelled with the component: an answer that arrives later must not create an object URL nobody will revoke.
    this.service.content(file.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => {
        this.loading.delete(file.id);
        const previous = this.thumbs()[file.id];
        if (previous) { URL.revokeObjectURL(previous); }
        this.thumbs.update(all => ({ ...all, [file.id]: URL.createObjectURL(blob) }));
      },
      error: () => this.loading.delete(file.id),   // a missing image is shown as its extension
    });
  }

  /** What the dialog can show of a file: an image, or text (a log, JSON, CSV...). Anything else is downloaded. */
  private kindOf(file: Attachment): 'image' | 'text' | 'other' {
    if (file.contentType.startsWith('image/') && file.contentType !== 'image/svg+xml') { return 'image'; }
    const isText = file.contentType.startsWith('text/') || /json|xml|csv|yaml|log/.test(file.contentType)
      || /\.(log|txt|json|csv|xml|md|yml|yaml)$/i.test(file.fileName);
    return isText ? 'text' : 'other';
  }

  protected open(file: Attachment): void {
    const kind = this.kindOf(file);
    if (kind === 'other') { this.download(file); return; }
    const request = ++this.previewRequest;
    const done = (view: { kind: 'image' | 'text'; url: string; text: string; cut: boolean }): void => {
      if (request === this.previewRequest) { this.preview.set({ file, ...view }); }
    };
    if (kind === 'image') {
      // The thumbnail's object URL is the file itself, already downloaded.
      const url = this.thumbs()[file.id];
      if (url) { done({ kind, url, text: '', cut: false }); return; }
    }
    this.service.content(file.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async blob => {
      if (kind === 'image') {
        const url = URL.createObjectURL(blob);
        this.thumbs.update(all => ({ ...all, [file.id]: url }));
        done({ kind, url, text: '', cut: false });
      } else {
        const text = await blob.text();
        done({ kind, url: '', text: text.slice(0, PREVIEW_CHARS), cut: text.length > PREVIEW_CHARS });
      }
    });
  }

  protected closePreview(): void {
    this.previewRequest++;
    this.preview.set(null);
  }

  protected extension(file: Attachment): string {
    const dot = file.fileName.lastIndexOf('.');
    return dot < 0 ? '' : file.fileName.slice(dot + 1, dot + 5).toUpperCase();
  }

  protected dragOver(event: DragEvent): void {
    if (!this.canWrite()) { return; }
    event.preventDefault();
    this.over.set(true);
  }

  protected drop(event: DragEvent): void {
    event.preventDefault();
    this.over.set(false);
    this.uploadAll(Array.from(event.dataTransfer?.files ?? []));
  }

  /** A screenshot on the clipboard becomes a file named after the time; copied files and text are left alone. */
  protected paste(event: ClipboardEvent): void {
    const files = Array.from(event.clipboardData?.files ?? []);
    if (files.length === 0) { return; }
    event.preventDefault();
    this.uploadAll(files.map(f => (f.name && f.name !== 'image.png' ? f : new File([f], this.pastedName(f), { type: f.type }))));
  }

  private pastedName(file: File): string {
    const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14);
    const extension = file.type.split('/')[1]?.replace('jpeg', 'jpg') || 'png';
    return `screenshot-${stamp}.${extension}`;
  }

  protected picked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadAll(Array.from(input.files ?? []));
    input.value = '';
  }

  /** One file after the other, so that a refused file is named in its own message and the rest still go up. */
  private uploadAll(files: File[]): void {
    if (files.length === 0 || this.busy()) { return; }
    this.busy.set(true);
    const next = (index: number): void => {
      if (index >= files.length) {
        this.busy.set(false);
        this.reload();
        return;
      }
      this.service.upload(this.ownerType(), this.ownerId(), files[index]).subscribe({
        next: () => next(index + 1),
        error: () => next(index + 1),   // the error interceptor has shown why
      });
    };
    next(0);
  }

  protected download(file: Attachment): void {
    this.service.content(file.id).subscribe(blob => saveFile({ blob, fileName: file.fileName }));
  }

  protected remove(file: Attachment): void {
    if (!confirm(this.i18n.t('att.confirmDelete', { name: file.fileName }))) { return; }
    this.service.remove(file.id).subscribe(() => {
      const url = this.thumbs()[file.id];
      if (url) { URL.revokeObjectURL(url); }
      this.thumbs.update(all => { const { [file.id]: _gone, ...rest } = all; return rest; });
      this.toast.success(this.i18n.t('att.deleted'));
      this.reload();
    });
  }
}
