import { Component, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '../../core/i18n/i18n';

/** What the server allows: 50 characters a tag, 20 tags, no comma or semicolon (they separate tags in files and here). */
export const TAG_MAX_LENGTH = 50;
export const TAG_MAX_COUNT = 20;

/** A tag as the server keeps it: the white space cleaned. Empty when there is nothing left. */
export function cleanTag(text: string): string {
  return text.split(/\s+/).filter(part => part.length > 0).join(' ');
}

/**
 * Adds a tag to a list: nothing for an empty tag or one that is there already (ignoring case), and the list is returned as it
 * is when it is full. The text of a tag is cut at the longest the server accepts.
 */
export function addTag(tags: string[], text: string): string[] {
  const tag = cleanTag(text).slice(0, TAG_MAX_LENGTH).trim();
  if (!tag || tags.length >= TAG_MAX_COUNT || tags.some(t => t.toLowerCase() === tag.toLowerCase())) {
    return tags;
  }
  return [...tags, tag];
}

/** Chips with a field: Enter, comma or semicolon adds the tag, Backspace in an empty field takes the last one away. */
@Component({
  selector: 'app-tag-input',
  imports: [FormsModule, TranslatePipe],
  template: `
    <div class="tag-input" [class.readonly]="readonly()">
      @for (tag of tags(); track tag) {
        <span class="chip">
          {{ tag }}
          @if (!readonly()) {
            <button type="button" [attr.aria-label]="'tags.remove' | t: { tag }" (click)="remove(tag)">&times;</button>
          }
        </span>
      }
      @if (!readonly()) {
        <input
          [attr.list]="listId" [(ngModel)]="draft" name="tag-draft" [maxlength]="maxLength" autocomplete="off"
          [placeholder]="(tags().length ? 'tags.addMore' : 'tags.add') | t"
          [attr.aria-label]="'tags.title' | t"
          (keydown)="key($event)" (blur)="commit()" />
        <datalist [id]="listId">
          @for (s of suggestions(); track s) { <option [value]="s"></option> }
        </datalist>
      }
      @if (readonly() && tags().length === 0) { <span class="muted">-</span> }
    </div>
  `,
})
export class TagInputComponent {
  private static next = 0;

  readonly tags = model<string[]>([]);
  readonly suggestions = input<string[]>([]);
  readonly readonly = input(false);
  /** Emitted when the person changed the list (not when the parent sets it). */
  readonly edited = output<string[]>();

  protected readonly listId = `tag-suggestions-${TagInputComponent.next++}`;
  protected readonly maxLength = TAG_MAX_LENGTH;
  protected draft = '';
  protected readonly full = signal(false);

  protected key(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
      event.preventDefault();
      this.commit();
    } else if (event.key === 'Backspace' && this.draft === '' && this.tags().length > 0) {
      this.set(this.tags().slice(0, -1));
    }
  }

  protected commit(): void {
    const text = this.draft;
    this.draft = '';
    if (!text.trim()) { return; }
    // A pasted "a; b, c" is three tags.
    this.set(text.split(/[,;]/).reduce((all, part) => addTag(all, part), this.tags()));
  }

  protected remove(tag: string): void {
    this.set(this.tags().filter(t => t !== tag));
  }

  private set(next: string[]): void {
    if (next.length === this.tags().length && next.every((t, i) => t === this.tags()[i])) { return; }
    this.tags.set(next);
    this.edited.emit(next);
  }
}
