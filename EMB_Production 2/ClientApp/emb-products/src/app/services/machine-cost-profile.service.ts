// ================= IMPORTS =================
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { ApiResponse } from '../shared/models/api-response';
import { ApiPagedResponse } from '../shared/models/api-response';

import { MachineCostProfileDto } from '../shared/models/machine-cost-profile.dto';
import { MachineCostProfileDetail } from '../shared/models/machine-cost-profile.dto';
import { DropdownItem } from '../shared/models/api-response';


// ================= SERVICE =================
@Injectable({ providedIn: 'root' })
export class MachineCostProfileService {

  private baseUrl = '/emb-products/machine-cost-profile';

  constructor(private http: HttpClient) { }

  /* =====================================================
     LIST (WITH PAGINATION SUPPORT)
  ===================================================== */
  list(params: {
    page?: number;
    pageSize?: number;
    activeFilter?: string;
    search?: string;
    machineId?: number | null;
  }): Observable<ApiPagedResponse<MachineCostProfileDto>> {

    const obj: { [k: string]: string } = {
      page: params.page?.toString() ?? '1',
      pageSize: params.pageSize?.toString() ?? '10',
      activeFilter: params.activeFilter ?? '',
      search: params.search ?? ''
    };
    if (params.machineId && params.machineId > 0) obj['machineId'] = String(params.machineId);

    const httpParams = new HttpParams({ fromObject: obj });

    return this.http.get<ApiPagedResponse<MachineCostProfileDto>>(
      `${this.baseUrl}/list`,
      { params: httpParams }
    );
  }

  // Phase R.6 - 360 profile
  getProfile(id: number): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(`${this.baseUrl}/profile/${id}`);
  }

  /* =====================================================
     DETAIL
  ===================================================== */
  detail(id: number): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  /* =====================================================
     SAVE (CREATE / UPDATE)
  ===================================================== */
  save(payload: MachineCostProfileDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/save`,
      payload
    );
  }

  /* =====================================================
     DELETE (SOFT DELETE)
  ===================================================== */
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/delete/${id}`,
      {}
    );
  }

  /* =====================================================
     MACHINE DROPDOWN (Gen_Machines)
  ===================================================== */
  getMachineDropdown(): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(
      '/emb-products/dropdowns/machines'
    );
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
