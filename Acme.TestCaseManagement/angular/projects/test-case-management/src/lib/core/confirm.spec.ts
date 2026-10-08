import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ConfirmHostComponent, ConfirmService } from './confirm';

describe('ConfirmService without a dialog on screen', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('falls back to the browser boxes, so that a question is still asked', () => {
    vi.stubGlobal('confirm', () => true);
    vi.stubGlobal('prompt', () => '  BUG-1  ');
    const service = TestBed.inject(ConfirmService);
    const answers: unknown[] = [];
    service.ask({ message: 'Sure?' }).subscribe(a => answers.push(a));
    service.askText({ message: 'Key?' }).subscribe(a => answers.push(a));
    expect(answers).toEqual([true, 'BUG-1']);
  });
});

describe('The question dialog', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<ConfirmHostComponent>>;
  let service: ConfirmService;
  const dom = () => fixture.nativeElement as HTMLElement;
  const click = (selector: string) => { dom().querySelector<HTMLElement>(selector)!.click(); fixture.detectChanges(); };

  beforeEach(() => {
    TestBed.resetTestingModule();
    service = TestBed.inject(ConfirmService);
    fixture = TestBed.createComponent(ConfirmHostComponent);
    fixture.detectChanges();
  });

  it('shows the message and the name of the action, and answers true on the confirming button', () => {
    const answers: boolean[] = [];
    service.ask({ message: 'Delete the plan "Sprint 1"?', confirmText: 'Delete', danger: true }).subscribe(a => answers.push(a));
    fixture.detectChanges();

    expect(dom().querySelector('[data-test=confirm-dialog]')?.textContent).toContain('Delete the plan "Sprint 1"?');
    const ok = dom().querySelector<HTMLElement>('[data-test=confirm-ok]')!;
    expect(ok.textContent?.trim()).toBe('Delete');
    expect(ok.classList).toContain('danger-solid');

    click('[data-test=confirm-ok]');
    expect(answers).toEqual([true]);
    expect(dom().querySelector('[role=dialog]')).toBeNull();
  });

  it('answers false on Cancel, on Escape and on the backdrop', () => {
    const answers: boolean[] = [];
    const ask = () => { service.ask({ message: 'Sure?' }).subscribe(a => answers.push(a)); fixture.detectChanges(); };

    ask();
    click('[data-test=confirm-cancel]');
    ask();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    ask();
    const backdrop = dom().querySelector<HTMLElement>('.backdrop')!;
    backdrop.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    backdrop.click();
    fixture.detectChanges();

    expect(answers).toEqual([false, false, false]);
  });

  it('asks for a text: Add stays off until something is typed, and the typed text is trimmed', () => {
    const answers: (string | null)[] = [];
    service.askText({ message: 'Issue key', confirmText: 'Add' }).subscribe(a => answers.push(a));
    fixture.detectChanges();

    const add = dom().querySelector<HTMLButtonElement>('[data-test=confirm-ok]')!;
    expect(add.disabled).toBe(true);

    return fixture.whenStable().then(() => {
      const input = dom().querySelector<HTMLInputElement>('#prompt-text')!;
      input.value = '  SHOP-7 ';
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(add.disabled).toBe(false);

      dom().querySelector<HTMLFormElement>('#prompt-form')!.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
      expect(answers).toEqual(['SHOP-7']);
    });
  });

  it('cancels the first question when a second one comes before it is answered', () => {
    const first: boolean[] = [];
    const second: boolean[] = [];
    service.ask({ message: 'one' }).subscribe(a => first.push(a));
    service.ask({ message: 'two' }).subscribe(a => second.push(a));
    fixture.detectChanges();
    expect(first).toEqual([false]);
    expect(dom().textContent).toContain('two');

    click('[data-test=confirm-ok]');
    expect(second).toEqual([true]);
  });

  it('does not leave a question waiting when the page goes away', () => {
    const answers: unknown[] = [];
    service.ask({ message: 'Sure?' }).subscribe({ next: a => answers.push(a), complete: () => answers.push('done') });
    fixture.detectChanges();

    fixture.destroy();

    expect(answers).toEqual([null, 'done']);
  });
});
