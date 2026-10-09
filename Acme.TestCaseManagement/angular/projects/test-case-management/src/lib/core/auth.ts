import { Injectable, Signal, computed, signal } from '@angular/core';

export interface TcmUser { userId: string; userName: string; roles: string[] }

/** The permission names of the module, as ABP defines them (TestCaseManagementPermissions). */
export const Permissions = {
  TestCases: { Default: 'TestCaseManagement.TestCases', Create: 'TestCaseManagement.TestCases.Create', Update: 'TestCaseManagement.TestCases.Update', Delete: 'TestCaseManagement.TestCases.Delete', Approve: 'TestCaseManagement.TestCases.Approve', SuggestSteps: 'TestCaseManagement.TestCases.SuggestSteps' },
  Projects: { Default: 'TestCaseManagement.Projects', Manage: 'TestCaseManagement.Projects.Manage' },
  TestSuites: { Default: 'TestCaseManagement.TestSuites', Manage: 'TestCaseManagement.TestSuites.Manage' },
  TestPlans: { Default: 'TestCaseManagement.TestPlans', Manage: 'TestCaseManagement.TestPlans.Manage' },
  TestRuns: { Default: 'TestCaseManagement.TestRuns', Execute: 'TestCaseManagement.TestRuns.Execute' },
  Requirements: { Default: 'TestCaseManagement.Requirements', Manage: 'TestCaseManagement.Requirements.Manage' },
  QualityGates: { Default: 'TestCaseManagement.QualityGates', Manage: 'TestCaseManagement.QualityGates.Manage' },
  SignOff: { Default: 'TestCaseManagement.SignOff', Approve: 'TestCaseManagement.SignOff.Approve' },
  SharedSteps: { Default: 'TestCaseManagement.SharedSteps', Manage: 'TestCaseManagement.SharedSteps.Manage' },
  ApiKeys: { Default: 'TestCaseManagement.ApiKeys', Manage: 'TestCaseManagement.ApiKeys.Manage' },
} as const;

/**
 * Who is signed in and what that person may do. The module only reads this: signing in and out belongs to the host,
 * which provides its own implementation (the standalone app, or an adapter over the ABP services). An implementation
 * needs its own @Injectable(), or Angular builds the signed-out default below instead of it.
 */
@Injectable({ providedIn: 'root', useFactory: () => new SignedOutAuthService() })
export abstract class AuthService {
  abstract readonly user: Signal<TcmUser | null>;
  abstract readonly isAuthenticated: Signal<boolean>;
  abstract can(permission: string): boolean;

  /** What to show as the user's role: the first role, or the user name when there is none. */
  readonly roleLabel = computed(() => this.user()?.roles[0] ?? this.user()?.userName ?? '');
}

/** What the module uses when the host provides nothing: nobody is signed in and nothing is allowed. */
class SignedOutAuthService extends AuthService {
  readonly user = signal<TcmUser | null>(null);
  readonly isAuthenticated = signal(false);
  can(): boolean { return false; }
}
