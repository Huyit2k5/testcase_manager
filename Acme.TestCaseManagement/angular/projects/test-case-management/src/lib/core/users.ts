import { Injectable, computed, inject, signal } from '@angular/core';
import { catchError, of } from 'rxjs';
import { TCM_USER_DIRECTORY, TcmDirectoryUser } from './host';

/** The users of the host, read once from its directory: who a test can be assigned to, and the names to show. */
@Injectable({ providedIn: 'root' })
export class UserNames {
  private readonly directory = inject(TCM_USER_DIRECTORY);
  private requested = false;

  private readonly all = signal<TcmDirectoryUser[]>([]);
  /** The directory has answered (even with nobody). */
  readonly loaded = signal(false);
  readonly users = this.all.asReadonly();
  /** False when the host has no directory, or it refused: the pages then do not offer an assignment. */
  readonly available = computed(() => this.all().length > 0);

  /** Starts reading the directory the first time it is called; a failure (for example no permission to list users) is the same as no directory. */
  load(): void {
    if (this.requested) { return; }
    this.requested = true;
    this.directory.list().pipe(catchError(() => of([] as TcmDirectoryUser[]))).subscribe(users => {
      this.all.set([...users].sort((a, b) => a.displayName.localeCompare(b.displayName)));
      this.loaded.set(true);
    });
  }

  /** The name to show for a user id; null when the user is unknown. */
  nameOf(id: string | null): string | null {
    return id ? this.all().find(u => u.id === id)?.displayName ?? null : null;
  }
}
