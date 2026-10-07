import { Component, input, output } from '@angular/core';
import { TestSuiteTree } from '../../proxy/dtos';

/** One suite of the library tree, with its children rendered recursively. */
@Component({
  selector: 'app-suite-node',
  template: `
    <li>
      <button type="button" class="node" [class.selected]="node().id === selectedId()" (click)="selected.emit(node().id)">
        <span class="name">{{ node().name }}</span>
        <span class="count">{{ node().testCaseCount }}</span>
      </button>
      @if (node().children.length) {
        <ul>
          @for (child of node().children; track child.id) {
            <app-suite-node [node]="child" [selectedId]="selectedId()" (selected)="selected.emit($event)" />
          }
        </ul>
      }
    </li>
  `,
  styles: `
    :host { display: contents; }
    ul { list-style: none; margin: 0; padding-left: 14px; border-left: 1px solid var(--border); margin-left: 8px; }
    .node { width: 100%; display: flex; justify-content: space-between; gap: 8px; text-align: left; border: 0; background: transparent; color: var(--text); padding: 6px 8px; border-radius: 6px; }
    .node:hover { background: var(--surface-2); }
    .node.selected { background: var(--info-bg); color: var(--info); font-weight: 700; }
    .count { color: var(--muted); font-size: .8rem; }
  `,
})
export class SuiteNodeComponent {
  readonly node = input.required<TestSuiteTree>();
  readonly selectedId = input<string | null>(null);
  readonly selected = output<string>();
}
