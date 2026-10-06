import { TestSuiteTree } from '../../proxy/dtos';

export interface SuiteOption { id: string; label: string }

/** Flattens the suite forest for a select box, indenting by depth. */
export function flattenSuites(tree: TestSuiteTree[], depth = 0): SuiteOption[] {
  return tree.flatMap(node => [
    { id: node.id, label: `${'  '.repeat(depth)}${node.name}` },
    ...flattenSuites(node.children, depth + 1),
  ]);
}
