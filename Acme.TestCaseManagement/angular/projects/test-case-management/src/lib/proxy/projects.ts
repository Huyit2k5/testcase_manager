import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { apiRoot } from '../core/host';
import { Project, SaveProject } from './dtos';

@Injectable({ providedIn: 'root' })
export class ProjectService {
  private readonly http = inject(HttpClient);
  private readonly root = apiRoot();

  /** Archived projects are left out unless asked for. */
  list(includeArchived = false): Observable<Project[]> {
    let params = new HttpParams();
    if (includeArchived) { params = params.set('IncludeArchived', 'true'); }
    return this.http.get<Project[]>(`${this.root}/projects`, { params });
  }
  create(input: SaveProject & { key: string }): Observable<Project> { return this.http.post<Project>(`${this.root}/projects`, input); }
  /** The key does not change. */
  update(id: string, input: SaveProject): Observable<Project> { return this.http.put<Project>(`${this.root}/projects/${id}`, input); }
  archive(id: string): Observable<Project> { return this.http.post<Project>(`${this.root}/projects/${id}/archive`, null); }
  restore(id: string): Observable<Project> { return this.http.post<Project>(`${this.root}/projects/${id}/restore`, null); }
  delete(id: string): Observable<void> { return this.http.delete<void>(`${this.root}/projects/${id}`); }
}
