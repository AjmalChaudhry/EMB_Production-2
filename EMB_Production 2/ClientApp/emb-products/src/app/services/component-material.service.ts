import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {ApiResponse,ApiPagedResponse,ApiResponseData} from '../shared/models/api-response';
import {ComponentMaterialRowViewModel,ComponentMaterialDetailViewModel,
        ComponentMaterialForm,DropdownItem} from '../shared/models/component-material.dto';

@Injectable({ providedIn: 'root' })
export class GenComponentMaterialService {

  private base = '/emb-products/component-materials';

  constructor(private http: HttpClient) { }

  list(
    page: number,
    pageSize: number,
    search: string,
    activeFilter: string,
    showDeleted: boolean,
    compId?: number | null
  ): Observable<ApiPagedResponse<ComponentMaterialRowViewModel>> {
    let params = new HttpParams()
      .set('page', page)
      .set('pageSize', pageSize)
      .set('search', search || '')
      .set('activeFilter', activeFilter || '')
      .set('showDeleted', showDeleted);

    if (compId && compId > 0) {
      params = params.set('compId', compId);
    }

    return this.http.get<ApiPagedResponse<ComponentMaterialRowViewModel>>(
      `${this.base}/list`,
      { params }
    );
  }

  detail(id: number): Observable<ApiResponseData<ComponentMaterialDetailViewModel>> {
    return this.http.get<ApiResponseData<ComponentMaterialDetailViewModel>>(
      `${this.base}/detail/${id}`
    );
  }

  // Phase R.2 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.base}/profile/${id}`);
  }

  save(model: ComponentMaterialForm): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.base}/save`, model);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.base}/delete/${id}`, {});
  }

  ddlComponents(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.base}/ddl-components`
    );
  }

  ddlRawItems(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.base}/ddl-raw-items`
    );
  }

  ddlUnits(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      `${this.base}/ddl-units`
    );
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<any> {
    return this.http.get<any>(`${this.base}/audit-log/${id}`);
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
}
