import { Injectable, Provider, signal } from '@angular/core';
import { AuthService, TcmUser } from './auth';

/** For unit tests only: a signed-in user who holds exactly these permissions ('*' holds all of them). */
export function grant(...permissions: string[]): Provider {
  @Injectable()
  class GrantedAuth extends AuthService {
    readonly user = signal<TcmUser | null>({ userId: 'u1', userName: 'tester', roles: ['QA'] });
    readonly isAuthenticated = signal(true);
    can(permission: string): boolean { return permissions.includes('*') || permissions.includes(permission); }
  }
  return { provide: AuthService, useClass: GrantedAuth };
}
