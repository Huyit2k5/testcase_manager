import { Permissions } from './core/auth';

export interface TcmMenuItem {
  /** The path under the base path of the module, with no leading slash. */
  path: string;
  /** The key of the label in the dictionary of the module (en.ts / vi.ts). */
  label: string;
  /** The key of the label in the ABP localization resource of the module (Localization/TestCaseManagement/*.json). */
  abpName: string;
  /** The permission that opens the page; a page without one is open to everyone who is signed in. */
  permission?: string;
  /** The permission an ABP application needs to show the entry in its sidebar (stricter than `permission`: every page has one). */
  policy: string;
  /** The order in the menu. */
  order: number;
}

/** The pages of the module, in menu order. The standalone app builds its tabs from this, an ABP application its sidebar. */
export const TCM_MENU: readonly TcmMenuItem[] = [
  { path: 'dashboard', label: 'nav.dashboard', abpName: 'TestCaseManagement::Menu:Dashboard', permission: Permissions.TestRuns.Default, policy: Permissions.TestRuns.Default, order: 1 },
  { path: 'repository', label: 'nav.repository', abpName: 'TestCaseManagement::Menu:Repository', permission: Permissions.TestCases.Default, policy: Permissions.TestCases.Default, order: 2 },
  { path: 'shared-steps', label: 'nav.sharedSteps', abpName: 'TestCaseManagement::Menu:SharedSteps', permission: Permissions.SharedSteps.Default, policy: Permissions.SharedSteps.Default, order: 3 },
  { path: 'runs', label: 'nav.runs', abpName: 'TestCaseManagement::Menu:Runs', permission: Permissions.TestPlans.Default, policy: Permissions.TestPlans.Default, order: 4 },
  { path: 'traceability', label: 'nav.traceability', abpName: 'TestCaseManagement::Menu:Traceability', permission: Permissions.Requirements.Default, policy: Permissions.Requirements.Default, order: 5 },
  { path: 'quality', label: 'nav.quality', abpName: 'TestCaseManagement::Menu:Quality', permission: Permissions.QualityGates.Default, policy: Permissions.QualityGates.Default, order: 6 },
  { path: 'automation', label: 'nav.automation', abpName: 'TestCaseManagement::Menu:Automation', permission: Permissions.ApiKeys.Default, policy: Permissions.ApiKeys.Default, order: 7 },
];
