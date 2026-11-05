import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {ServiceDto} from '../shared/models/gen-services.dto';

@Injectable({
  providedIn: 'root'
})
export class GenServicesService {
  private baseUrl = '/emb-products/services';

  constructor(private http: HttpClient) { }

  list(filters: any): Observable<any> {
    let params = new HttpParams()
      .set('page', filters.page ?? 1)
      .set('pageSize', filters.pageSize ?? 10)
      .set('search', filters.search ?? '')
      .set('activeFilter', filters.activeFilter ?? '')
      .set('branchFilter', filters.branchFilter ?? '')
      .set('serviceTypeFilter', filters.serviceTypeFilter ?? '');

    return this.http.get<any>(`${this.baseUrl}/list`, { params });
  }

  detail(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`);
  }

  create(dto: ServiceDto): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/create`, dto);
  }

  update(dto: ServiceDto): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/delete`, { id });
  }

  getServiceTypes(): Observable<any> {
    return this.http.get<any>('/emb-products/dropdowns/service-types');
  }

  getBranches(): Observable<any> {
    return this.http.get<any>('/emb-products/dropdowns/branches');
  }

  getMachines(): Observable<any> {
    return this.http.get<any>('/emb-products/dropdowns/machines');
  }

  // Phase R.7 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — audit timeline.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk activate / deactivate / delete.
  // ───────────────────────────────────────────────────────────────
  bulkActivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-activate`, { ids });
  }
  bulkDeactivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-deactivate`, { ids });
  }
  bulkDelete(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-delete`, { ids });
  }
}
