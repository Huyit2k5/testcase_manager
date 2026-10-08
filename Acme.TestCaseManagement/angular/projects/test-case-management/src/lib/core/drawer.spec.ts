import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ModalComponent } from './modal';

@Component({
  imports: [ModalComponent],
  template: `@if (open()) { <app-modal title="Detail" [side]="side()" (closed)="open.set(false)"><p>body</p></app-modal> }`,
})
class HostComponent {
  readonly open = signal(true);
  readonly side = signal(true);
}

describe('The drawer', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>;
  const dom = () => fixture.nativeElement as HTMLElement;
  const drawer = () => dom().querySelector<HTMLElement>('.dialog.side');
  const separator = () => dom().querySelector<HTMLElement>('[role=separator]')!;

  beforeEach(() => {
    try { localStorage.clear(); } catch { /* ignore */ }
    TestBed.resetTestingModule();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('is a dialog from the right edge, with a handle to resize it, and stays a plain dialog without side', () => {
    expect(drawer()).not.toBeNull();
    expect(separator().getAttribute('aria-label')).toBeTruthy();

    fixture.componentInstance.side.set(false);
    fixture.detectChanges();
    expect(drawer()).toBeNull();
    expect(dom().querySelector('[role=separator]')).toBeNull();
  });

  it('is widened by the left arrow and narrowed by the right arrow, never below its minimum, and remembers the width', () => {
    const before = Number(separator().getAttribute('aria-valuenow'));
    separator().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    const wider = Number(separator().getAttribute('aria-valuenow'));
    expect(wider).toBe(Math.min(before + 32, Number(separator().getAttribute('aria-valuemax'))));
    expect(localStorage.getItem('tcm.drawerWidth')).toBe(String(wider));

    for (let i = 0; i < 60; i++) { separator().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true })); fixture.detectChanges(); }
    expect(Number(separator().getAttribute('aria-valuenow'))).toBe(Number(separator().getAttribute('aria-valuemin')));
  });

  it('uses the width of the last drawer for the next one', () => {
    separator().dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));
    fixture.detectChanges();
    const chosen = separator().getAttribute('aria-valuenow');

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    expect(separator().getAttribute('aria-valuenow')).toBe(chosen);
  });

  it('closes on Escape, like a dialog', () => {
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(drawer()).toBeNull();
  });
});
