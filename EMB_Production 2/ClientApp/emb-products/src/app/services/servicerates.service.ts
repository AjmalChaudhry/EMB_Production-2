import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ServiceRateDto,
  ServiceRateRowViewModel,
  ServiceRateDetailViewModel,
  ApiResponse,
  ApiResponseList,
  ApiResponseData
} from '../shared/models/service-rate.dto';

@Injectable({ providedIn: 'root' })
export class ServiceRatesService {
  private baseUrl = '/emb-products/service-rates';

  constructor(private http: HttpClient) { }

  list(search = '', activeFilter = '', branchFilter: number | null = null) {
    return this.http.get<ApiResponseList<ServiceRateRowViewModel>>(
      `${this.baseUrl}/list?search=${search}&activeFilter=${activeFilter}&branchFilter=${branchFilter ?? ''}`
    );
  }

  detail(id: number) {
    return this.http.get<ApiResponseData<ServiceRateDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  create(dto: ServiceRateDto) {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(dto: ServiceRateDto) {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number) {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete`, { id });
  }

  // DROPDOWNS
  loadCustomers() {
    return this.http.get<any>('/emb-products/dropdowns/customers');
  }
  loadServiceTypes() {
    return this.http.get<{ data: { id: number; name: string }[] }>(
      '/emb-products/dropdowns/service-types'
    );
  }

  loadBranches() {
    return this.http.get<any>('/emb-products/dropdowns/branches');
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
