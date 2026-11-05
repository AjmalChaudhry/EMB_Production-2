import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  MachineDto,
  MachineRowViewModel,
  MachineDetailViewModel
} from '../shared/models/machine.dto';

import {
  ApiPagedResponse,
  ApiResponse
} from '../shared/models/api-response';

@Injectable({ providedIn: 'root' })
export class MachinesService {
  private baseUrl = '/emb-products/machines';

  constructor(private http: HttpClient) { }

  list(filters: {
    search: string,
    active: string,
    brandId: number | null,
    serviceId: number | null,
    branchId: number | null,
    page: number,
    pageSize: number
  }) {
    const params = new URLSearchParams();

    params.append("search", filters.search || "");
    params.append("activeFilter", filters.active || "");
    params.append("brandFilter", filters.brandId?.toString() || "");
    params.append("serviceFilter", filters.serviceId?.toString() || "");
    params.append("branchFilter", filters.branchId?.toString() || "");
    params.append("page", filters.page.toString());
    params.append("pageSize", filters.pageSize.toString());

    return this.http.get<ApiPagedResponse<MachineRowViewModel>>(
      `/emb-products/machines/list?${params.toString()}`
    );
  }


  getDetail(id: number): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(`${this.baseUrl}/detail/${id}`);
  }

  create(dto: MachineDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(dto: MachineDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete`, { id });
  }

  // Phase R.4 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  // Phase R.4 - auto code
  getNextCode(): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(`${this.baseUrl}/next-code`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — audit timeline for the detail drawer.
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
