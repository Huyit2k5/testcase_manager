import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ModalComponent } from './modal';

@Component({
  imports: [ModalComponent],
  template: `
    <button id="opener" type="button">open</button>
    @if (outer()) {
      <app-modal title="Outer" (closed)="outerClosed.update(n => n + 1)">
        <input id="outer-a" /><input id="outer-b" />
        @if (inner()) {
          <app-modal title="Inner" (closed)="innerClosed.update(n => n + 1)"><input id="inner-a" /><input id="inner-b" /></app-modal>
        }
      </app-modal>
    }
  `,
})
class HostComponent {
  readonly outer = signal(false);
  readonly inner = signal(false);
  readonly outerClosed = signal(0);
  readonly innerClosed = signal(0);
}

describe('ModalComponent', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>;
  let host: HostComponent;
  const el = (selector: string) => fixture.nativeElement.querySelector(selector) as HTMLElement;
  const key = (name: string, init: KeyboardEventInit = {}) => {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...init });
    document.dispatchEvent(event);
    return event;
  };

  beforeEach(async () => {
    TestBed.resetTestingModule();
    fixture = TestBed.createComponent(HostComponent);
    document.body.appendChild(fixture.nativeElement);
    host = fixture.componentInstance;
    el('#opener').focus();
  });

  const open = async (outer = true, inner = false) => {
    host.outer.set(outer);
    host.inner.set(inner);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  it('does not close when the press began inside the dialog and the release is on the backdrop', async () => {
    await open();
    const backdrop = el('.backdrop');
    el('.dialog').dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(host.outerClosed()).toBe(0);
  });

  it('closes on a click that both pressed and released on the backdrop', async () => {
    await open();
    const backdrop = el('.backdrop');
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(host.outerClosed()).toBe(1);
  });

  it('closes on Escape, and only the topmost of nested dialogs', async () => {
    await open(true, true);
    key('Escape');
    expect(host.innerClosed()).toBe(1);
    expect(host.outerClosed()).toBe(0);

    host.inner.set(false);
    fixture.detectChanges();
    key('Escape');
    expect(host.outerClosed()).toBe(1);
  });

  it('moves focus into the dialog and gives it back to the previous element on close', async () => {
    await open();
    expect(el('.dialog').contains(document.activeElement)).toBe(true);
    host.outer.set(false);
    fixture.detectChanges();
    expect(document.activeElement).toBe(el('#opener'));
  });

  it('keeps Tab and Shift+Tab inside the dialog', async () => {
    await open();
    const last = Array.from(el('.dialog').querySelectorAll<HTMLElement>('button, input')).pop()!;
    last.focus();
    const forward = key('Tab');
    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(el('.icon-btn'));

    const backward = key('Tab', { shiftKey: true });
    expect(backward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(last);
  });

  it('keeps its aria attributes', async () => {
    await open();
    expect(el('.dialog').getAttribute('role')).toBe('dialog');
    expect(el('.dialog').getAttribute('aria-modal')).toBe('true');
    expect(el('.dialog').getAttribute('aria-label')).toBe('Outer');
  });
});
