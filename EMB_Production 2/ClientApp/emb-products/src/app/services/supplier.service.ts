// src/app/services/supplier.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  SupplierRowViewModel,
  SupplierDetailViewModel,
  SupplierDto,
  SupplierProfileViewModel
} from '../shared/models/supplier.dto';

import {
  ApiResponse,
  ApiResponseData,
  ApiResponseList
} from '../shared/models/api-response';

export interface BranchLookup {
  id: number;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class SupplierService {

  private baseUrl = '/emb-products/suppliers';
  private dropdownUrl = '/emb-products/dropdowns';

  constructor(private http: HttpClient) { }

  // ------------------------------------------------------------
  // LIST
  // ------------------------------------------------------------
  getAll(
    search: string = '',
    page: number = 1,
    pageSize: number = 10,
    activeFilter: string = '',
    branchId: string = ''
  ): Observable<ApiResponseList<SupplierRowViewModel>> {

    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize);

    if (search) params = params.set('search', search);
    if (activeFilter) params = params.set('activeFilter', activeFilter);
    if (branchId) params = params.set('branchId', branchId);

    return this.http.get<ApiResponseList<SupplierRowViewModel>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  // ------------------------------------------------------------
  // DETAIL
  // ------------------------------------------------------------
  getDetail(id: number): Observable<ApiResponseData<SupplierDetailViewModel>> {
    return this.http.get<ApiResponseData<SupplierDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  // ------------------------------------------------------------
  // CREATE
  // ------------------------------------------------------------
  create(dto: SupplierDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  // ------------------------------------------------------------
  // UPDATE
  // ------------------------------------------------------------
  update(dto: SupplierDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, dto);
  }

  // ------------------------------------------------------------
  // DELETE
  // ------------------------------------------------------------
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  // ------------------------------------------------------------
  // BRANCH DROPDOWN
  // ------------------------------------------------------------
  getBranchesDropdown(): Observable<{ data: BranchLookup[] }> {
    return this.http.get<{ data: BranchLookup[] }>(
      `${this.dropdownUrl}/branches`
    );
  }

  // ------------------------------------------------------------
  // PHASE H.0 - SUPPLIER 360 PROFILE
  // ------------------------------------------------------------
  getProfile(id: number): Observable<ApiResponseData<SupplierProfileViewModel>> {
    return this.http.get<ApiResponseData<SupplierProfileViewModel>>(
      `${this.baseUrl}/profile/${id}`
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
