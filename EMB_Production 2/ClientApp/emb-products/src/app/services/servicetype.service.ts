import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ServiceTypeListResponse,
  ServiceTypeDetailResponse,
  ServiceTypeDto,
  ServiceTypeSaveResponse,
  ServiceTypeDeleteResponse
} from '../shared/models/service-type.dto';

@Injectable({ providedIn: 'root' })

export class ServiceTypeService {

  private readonly baseUrl = '/emb-products/service-types';

  constructor(private http: HttpClient) { }

  // LIST
  getList(
    search: string,
    page: number,
    pageSize: number,
    branchId?: number | null,
    activeFilter?: string
  ): Observable<ServiceTypeListResponse> {

    let params = new HttpParams()
      .set('search', search || '')
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (branchId != null) {
      params = params.set('branchId', branchId.toString());
    }

    if (activeFilter) {
      params = params.set('activeFilter', activeFilter);
    }

    return this.http.get<ServiceTypeListResponse>(`${this.baseUrl}/list`, { params });
  }

  // DETAIL
  getDetail(id: number): Observable<ServiceTypeDetailResponse> {
    return this.http.get<ServiceTypeDetailResponse>(`${this.baseUrl}/detail/${id}`);
  }

  // CREATE
  create(dto: ServiceTypeDto): Observable<ServiceTypeSaveResponse> {
    return this.http.post<ServiceTypeSaveResponse>(`${this.baseUrl}/create`, dto);
  }

  // UPDATE
  update(dto: ServiceTypeDto): Observable<ServiceTypeSaveResponse> {
    return this.http.post<ServiceTypeSaveResponse>(`${this.baseUrl}/update`, dto);
  }

  // DELETE
  delete(id: number): Observable<ServiceTypeDeleteResponse> {
    return this.http.post<ServiceTypeDeleteResponse>(`${this.baseUrl}/delete/${id}`, {});
  }
  getBranchList() {
    return this.http.get<any>('/emb-products/dropdowns/branches');
  }

  // Phase R.8 - 360 profile
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
