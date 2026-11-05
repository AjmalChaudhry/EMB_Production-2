// ===============================
// module-template.service.ts
// ===============================

import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  ModuleTemplateRowViewModel,
  ModuleTemplateDetailViewModel,
  ModuleTemplateDto,
  ApiResponseBase,
  ApiResponseData,
  ApiResponsePaged
} from '../shared/models/module-template.dto';

@Injectable({
  providedIn: 'root'
})
export class ModuleTemplateService {

  // Must match controller prefix
  private baseUrl = '/emb-products/module-templates';

  constructor(private http: HttpClient) { }

  // ============================================
  // LIST
  // ============================================
  getAll(
    search: string = '',
    activeFilter: string = '',
    showDeleted: string = '',
    page: number = 1,
    pageSize: number = 10
  ): Observable<ApiResponsePaged<ModuleTemplateRowViewModel>> {

    const params = new HttpParams({
      fromObject: {
        search,
        activeFilter,
        showDeleted,
        page: page.toString(),
        pageSize: pageSize.toString()
      }
    });

    return this.http.get<ApiResponsePaged<ModuleTemplateRowViewModel>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  // ============================================
  // DETAIL
  // ============================================
  getDetail(id: number): Observable<ApiResponseData<ModuleTemplateDetailViewModel>> {
    return this.http.get<ApiResponseData<ModuleTemplateDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  // ============================================
  // CREATE
  // ============================================
  create(dto: ModuleTemplateDto): Observable<ApiResponseData<number>> {
    return this.http.post<ApiResponseData<number>>(
      `${this.baseUrl}/create`,
      dto
    );
  }

  // ============================================
  // UPDATE
  // ============================================
  update(id: number, dto: ModuleTemplateDto): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(
      `${this.baseUrl}/update/${id}`,
      dto
    );
  }

  // ============================================
  // DELETE
  // ============================================
  delete(id: number): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(
      `${this.baseUrl}/delete/${id}`,
      {}
    );
  }

  // ============================================
  // TOGGLE ACTIVE
  // ============================================
  toggleActive(id: number): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(
      `${this.baseUrl}/toggle-active/${id}`,
      {}
    );
  }

  // ============================================
  // OPTIONAL: SERVER EXPORT ENDPOINTS
  // ============================================
  exportExcelUrl(): string {
    return `${this.baseUrl}/export-excel`;
  }

  exportPdfUrl(): string {
    return `${this.baseUrl}/export-pdf`;
  }

  exportPrintUrl(): string {
    return `${this.baseUrl}/print`;
  }

}
