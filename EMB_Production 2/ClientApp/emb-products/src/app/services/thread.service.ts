// src/app/services/thread.service.ts
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {ThreadRowViewModel,ThreadDetailViewModel,ThreadFormDto } from '../shared/models/thread.dto';
import {ApiResponse,ApiResponseData,ApiResponseList, DropdownItem} from '../shared/models/api-response';

// ============================================================
// LOOKUP INTERFACES
// ============================================================

export interface BranchLookup {
  id: number;
  name: string;
}

// ⭐ Matches your Gen_Suppliers dropdown controller exactly
export interface SupplierLookup {
  id: number;
  supplierName: string;
}

@Injectable({ providedIn: 'root' })
export class ThreadService {

  private baseUrl = '/emb-products/threads';
  private dropdownUrl = '/emb-products/dropdowns';

  constructor(private http: HttpClient) { }

  // ============================================================
  // LIST
  // Backend returns:
  // { success, data: ThreadRowViewModel[], total }
  // ============================================================
  getAll(
    search: string = '',
    page: number = 1,
    pageSize: number = 10,
    activeFilter: string = '',
    branchId: string = '',
    supplierId: string = ''
  ): Observable<ApiResponseList<ThreadRowViewModel>> {

    let params = new HttpParams()
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (search) params = params.set('search', search);
    if (activeFilter) params = params.set('activeFilter', activeFilter);
    if (branchId) params = params.set('branchId', branchId);
    if (supplierId) params = params.set('supplierId', supplierId);

    return this.http.get<ApiResponseList<ThreadRowViewModel>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  // ============================================================
  // DETAIL
  // ============================================================
  getDetail(id: number): Observable<ApiResponseData<ThreadDetailViewModel>> {
    return this.http.get<ApiResponseData<ThreadDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  // ============================================================
  // CREATE
  // ============================================================
  create(dto: ThreadFormDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/create`,
      dto
    );
  }

  // ============================================================
  // UPDATE
  // ============================================================
  update(dto: ThreadFormDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/update`,
      dto
    );
  }

  // ============================================================
  // DELETE
  // ============================================================
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/delete/${id}`,
      {}
    );
  }

  // ============================================================
  // ⭐ CENTRAL DROPDOWNS
  // ============================================================

  // Branch dropdown (central)
  getBranchesDropdown(): Observable<{ data: BranchLookup[] }> {
    return this.http.get<{ data: BranchLookup[] }>(
      `${this.dropdownUrl}/branches`
    );
  }

  getSuppliersDropdown(): Observable<{ success: boolean; data: DropdownItem[] }> {
    return this.http.get<{ success: boolean; data: DropdownItem[] }>(
      '/emb-products/dropdowns/suppliers'
    );
  }



}
