import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import { ApiPagedResponse, ApiResponseData } from '../shared/models/api-response';
import {
  CategoryListViewModel,
  CategoryDetailViewModel,
  CategoryLookups
} from '../shared/models/category.dto';

@Injectable({ providedIn: 'root' })
export class GenCategoryService {

  private baseUrl = '/Gen_Catagories';

  constructor(private http: HttpClient) { }

  // ============================================================
  // LOOKUPS
  // ============================================================
  getLookups(): Observable<ApiResponseData<CategoryLookups>> {
    return this.http.get<ApiResponseData<CategoryLookups>>(
      `${this.baseUrl}/lookups`,
      { withCredentials: true }
    );
  }

  // ============================================================
  // LIST
  // ============================================================
  getAll(
    search: string = '',
    page: number = 1,
    pageSize: number = 10,
    active: string = '',
    brandId: number | null = null,
    branchId: number | null = null
  ): Observable<ApiPagedResponse<CategoryListViewModel>> {

    let params = new HttpParams()
      .set('search', search ?? '')
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (active) {
      params = params.set('active', active);
    }
    if (brandId && brandId > 0) params = params.set('brandId', String(brandId));
    if (branchId && branchId > 0) params = params.set('branchId', String(branchId));

    return this.http.get<ApiPagedResponse<CategoryListViewModel>>(
      `${this.baseUrl}/list`,
      {
        params,
        withCredentials: true
      }
    );
  }

  // Phase R.12 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(
      `${this.baseUrl}/profile/${id}`,
      { withCredentials: true }
    );
  }

  // ============================================================
  // NEXT CODE
  // ============================================================
  getNextCode(): Observable<ApiResponseData<{ code: string }>> {
    return this.http.get<ApiResponseData<{ code: string }>>(
      `${this.baseUrl}/next-code`,
      { withCredentials: true }
    );
  }

  // ============================================================
  // DETAIL
  // ============================================================
  getDetail(id: number): Observable<ApiResponseData<CategoryDetailViewModel>> {
    return this.http.get<ApiResponseData<CategoryDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`,
      { withCredentials: true }
    );
  }

  // ============================================================
  // CREATE
  // ============================================================
  create(fd: FormData): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/create`,
      fd,
      { withCredentials: true }
    );
  }

  // ============================================================
  // UPDATE
  // ============================================================
  update(id: number, fd: FormData): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/update/${id}`,
      fd,
      { withCredentials: true }
    );
  }

  // ============================================================
  // DELETE
  // ============================================================
  delete(id: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/delete/${id}`,
      {},
      { withCredentials: true }
    );
  }
}
