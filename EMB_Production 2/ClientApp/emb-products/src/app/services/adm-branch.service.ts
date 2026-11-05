import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  BranchListViewModel,
  BranchDetailViewModel,
  BranchForm
} from '../models/branch.dto';

import {
  ApiResponseData,
  ApiPagedResponse
} from '../shared/models/api-response';

@Injectable({ providedIn: 'root' })
export class BranchService {

  // ✔ safer than string concat everywhere
  private readonly baseUrl = `${window.location.origin}/emb-products/branches`;

  constructor(private http: HttpClient) { }

  // ===============================
  // LIST
  // ===============================
  getAll(
    search = '',
    page = 1,
    pageSize = 10
  ): Observable<ApiPagedResponse<BranchListViewModel>> {

    const params = new HttpParams()
      .set('search', search ?? '')
      .set('page', String(page))
      .set('pageSize', String(pageSize));

    return this.http.get<ApiPagedResponse<BranchListViewModel>>(
      `${this.baseUrl}/list`,
      { params, withCredentials: true }
    );
  }

  // ===============================
  // DETAIL
  // ===============================
  getById(id: number): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(
      `${this.baseUrl}/detail/${id}`,
      { withCredentials: true }
    );
  }


  // ===============================
  // COMPANY DROPDOWN
  // ===============================
  getCompanies(): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(
      `${this.baseUrl}/companies`,
      { withCredentials: true }
    );
  }

  // ===============================
  // CREATE
  // ===============================
  createBranch(form: BranchForm): Observable<ApiResponseData<number>> {
    return this.http.post<ApiResponseData<number>>(
      `${this.baseUrl}/create`,
      form,
      { withCredentials: true }
    );
  }

  // ===============================
  // UPDATE
  // ===============================
  updateBranch(
    id: number,
    form: BranchForm
  ): Observable<ApiResponseData<boolean>> {
    return this.http.post<ApiResponseData<boolean>>(
      `${this.baseUrl}/update/${id}`,
      form,
      { withCredentials: true }
    );
  }

  // ===============================
  // DELETE (MVC-safe POST)
  // ===============================
  deleteBranch(branchId: number): Observable<ApiResponseData<boolean>> {
    return this.http.post<ApiResponseData<boolean>>(
      `${this.baseUrl}/delete`,
      { id: branchId },
      { withCredentials: true }
    );
  }

}
