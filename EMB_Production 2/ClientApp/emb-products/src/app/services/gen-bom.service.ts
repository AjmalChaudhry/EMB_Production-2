import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  ApiPagedResponse,
  ApiResponseData,
  ApiResponseBase
} from '../shared/models/api-response';

import {
  BomRowViewModel,
  BomDetailViewModel,
  BomForm,
  DropdownItem
} from '../shared/models/gen-bom.dto';

@Injectable({ providedIn: 'root' })
export class GenBomService {

  private base = '/emb-products/gen-bom';

  constructor(private http: HttpClient) { }

  // ======================================================
  // LIST
  // ======================================================
  list(
    page: number,
    pageSize: number,
    search: string,
    activeFilter: string,
    showDeleted: boolean,
    designId?: number | null,
    compId?: number | null
  ): Observable<ApiPagedResponse<BomRowViewModel>> {

    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize))
      .set('search', search || '')
      .set('activeFilter', activeFilter || '')
      .set('showDeleted', showDeleted ? '1' : '');

    if (designId && designId > 0) params = params.set('designId', String(designId));
    if (compId && compId > 0) params = params.set('compId', String(compId));

    return this.http.get<ApiPagedResponse<BomRowViewModel>>(
      `${this.base}/list`,
      { params }
    );
  }

  // Phase R.3 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.base}/profile/${id}`);
  }

  // ======================================================
  // DETAIL
  // ======================================================
  detail(id: number): Observable<ApiResponseData<BomDetailViewModel>> {
    return this.http.get<ApiResponseData<BomDetailViewModel>>(
      `${this.base}/detail/${id}`
    );
  }

  // ======================================================
  // SAVE
  // ======================================================
  save(payload: BomForm): Observable<ApiResponseData<{ id: number }>> {
    return this.http.post<ApiResponseData<{ id: number }>>(
      `${this.base}/save`,
      payload
    );
  }

  // ======================================================
  // DELETE
  // ======================================================
  delete(id: number): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(
      `${this.base}/delete/${id}`,
      {}
    );
  }

  // ======================================================
  // EXPORTS
  // ======================================================
  exportExcel(search: string, activeFilter: string, showDeleted: boolean): void {
    window.open(
      this.buildExportUrl(`${this.base}/export-excel`, search, activeFilter, showDeleted),
      '_blank'
    );
  }

  exportPdf(search: string, activeFilter: string, showDeleted: boolean): void {
    window.open(
      this.buildExportUrl(`${this.base}/export-pdf`, search, activeFilter, showDeleted),
      '_blank'
    );
  }

  print(search: string, activeFilter: string, showDeleted: boolean): void {
    window.open(
      this.buildExportUrl(`${this.base}/print`, search, activeFilter, showDeleted),
      '_blank'
    );
  }

  private buildExportUrl(
    url: string,
    search: string,
    activeFilter: string,
    showDeleted: boolean
  ): string {
    const params = new URLSearchParams();

    if (search) params.append('search', search);
    if (activeFilter) params.append('activeFilter', activeFilter);
    if (showDeleted) params.append('showDeleted', '1');

    const query = params.toString();
    return query
      ? `${window.location.origin}${url}?${query}`
      : `${window.location.origin}${url}`;
  }

  // ======================================================
  // DROPDOWNS
  // ======================================================
  ddlDesigns(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `/emb-products/dropdowns/designs`
    );
  }

  ddlComponents(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.base}/components`
    );
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(`${this.base}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk activate / deactivate / delete.
  // ───────────────────────────────────────────────────────────────
  bulkActivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.base}/bulk-activate`, { ids });
  }
  bulkDeactivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.base}/bulk-deactivate`, { ids });
  }
  bulkDelete(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.base}/bulk-delete`, { ids });
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — clone all live BOM rows from one design to another.
  //  Skips rows whose component already exists on the target so the
  //  call can be re-run safely after a partial first pass.
  // ───────────────────────────────────────────────────────────────
  cloneFromDesign(sourceDesignId: number, targetDesignId: number): Observable<any> {
    return this.http.post<any>(`${this.base}/clone-from-design`, {
      sourceDesignId, targetDesignId
    });
  }
}
