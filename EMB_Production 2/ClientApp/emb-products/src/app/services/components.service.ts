import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  ComponentRowViewModel,
  ComponentDetailViewModel,
  ComponentCreateUpdateModel
} from '../shared/models/component.dto';

@Injectable({
  providedIn: 'root'
})
export class ComponentsService {

  private baseUrl = '/emb-products/components';

  constructor(private http: HttpClient) { }

  // ===================== GET ALL =====================
  getAll(
    search: string,
    page: number,
    pageSize: number,
    activeFilter: string,
    designId: number | null,
    branchId: number | null = null
  ) {
    let params = new HttpParams()
      .set('search', search ?? '')
      .set('page', page.toString())
      .set('pageSize', pageSize.toString())
      .set('activeFilter', activeFilter ?? '');

    if (designId !== null && designId > 0) {
      params = params.set('designId', designId.toString());
    }
    if (branchId !== null && branchId > 0) {
      params = params.set('branchId', branchId.toString());
    }

    return this.http.get<any>(this.baseUrl, { params });
  }

  // Phase R.1 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  // ===================== DETAIL (FIXED) =====================
  getDetail(id: number) {
    return this.http.post<any>(
      `${this.baseUrl}/detail`,
      { id }
    );
  }

  // ===================== CREATE =====================
  create(model: any) {
    return this.http.post<any>(
      `${this.baseUrl}/create`,
      model
    );
  }

  // ===================== UPDATE =====================
  update(model: any) {
    return this.http.post<any>(
      `${this.baseUrl}/update`,
      model
    );
  }

  // ===================== DELETE =====================
  delete(id: number) {
    return this.http.post<any>(
      `${this.baseUrl}/delete`,
      { id }
    );
  }

  // ===================== DESIGNS =====================
  getDesigns() {
    return this.http
      .get<any>('/emb-products/dropdowns/designs-for-components')
      .pipe(
        map(res => Array.isArray(res?.data) ? res.data : [])
      );
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number) {
    return this.http.get<any>(`/emb-products/components/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk activate / deactivate / delete.
  // ───────────────────────────────────────────────────────────────
  bulkActivate(ids: number[]) {
    return this.http.post<any>('/emb-products/components/bulk-activate', { ids });
  }
  bulkDeactivate(ids: number[]) {
    return this.http.post<any>('/emb-products/components/bulk-deactivate', { ids });
  }
  bulkDelete(ids: number[]) {
    return this.http.post<any>('/emb-products/components/bulk-delete', { ids });
  }
}
