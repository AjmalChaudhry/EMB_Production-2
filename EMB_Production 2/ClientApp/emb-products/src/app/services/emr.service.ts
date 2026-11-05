import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  EmrRow, EmrItemRow, EmrDetail, EmrSummary, EmrSavePayload,
  WorkOrderDropdownItem, RawItemDropdown, ThreadDropdown
} from '../shared/models/emr.dto';

interface ApiEnvelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class EmrService {
  private base = '/emb-products/extra-material-request';

  constructor(private http: HttpClient) {}

  list(params?: {
    status?: string; workOrderId?: number; branchId?: number;
    search?: string; showDeleted?: string;
  }): Observable<ApiEnvelope<{ items: EmrRow[]; summary: EmrSummary }>> {
    const qs = new URLSearchParams();
    if (params?.status) qs.set('status', params.status);
    if (params?.workOrderId) qs.set('workOrderId', String(params.workOrderId));
    if (params?.branchId) qs.set('branchId', String(params.branchId));
    if (params?.search) qs.set('search', params.search);
    if (params?.showDeleted) qs.set('showDeleted', params.showDeleted);
    const url = `${this.base}/list${qs.toString() ? '?' + qs.toString() : ''}`;
    return this.http.get<ApiEnvelope<{ items: EmrRow[]; summary: EmrSummary }>>(url);
  }

  detail(id: number): Observable<ApiEnvelope<EmrDetail>> {
    return this.http.get<ApiEnvelope<EmrDetail>>(`${this.base}/detail/${id}`);
  }

  nextCode(): Observable<ApiEnvelope<{ code: string }>> {
    return this.http.get<ApiEnvelope<{ code: string }>>(`${this.base}/next-code`);
  }

  create(payload: EmrSavePayload): Observable<ApiEnvelope<{ id: number; emrCode: string }>> {
    return this.http.post<ApiEnvelope<{ id: number; emrCode: string }>>(`${this.base}/create`, payload);
  }

  update(payload: EmrSavePayload): Observable<ApiEnvelope<{ id: number }>> {
    return this.http.post<ApiEnvelope<{ id: number }>>(`${this.base}/update`, payload);
  }

  submit(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/submit/${id}`, {});
  }

  approve(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/approve/${id}`, {});
  }

  reject(id: number, reason: string): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/reject/${id}`, { reason });
  }

  remove(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/delete/${id}`, {});
  }

  workOrders(): Observable<ApiEnvelope<WorkOrderDropdownItem[]>> {
    return this.http.get<ApiEnvelope<WorkOrderDropdownItem[]>>(`${this.base}/workorders-dropdown`);
  }

  rawItems(): Observable<ApiEnvelope<RawItemDropdown[]>> {
    return this.http.get<ApiEnvelope<RawItemDropdown[]>>(`${this.base}/raw-items-dropdown`);
  }

  threads(): Observable<ApiEnvelope<ThreadDropdown[]>> {
    return this.http.get<ApiEnvelope<ThreadDropdown[]>>(`${this.base}/threads-dropdown`);
  }

  profile(id: number): Observable<ApiEnvelope<any>> {
    return this.http.get<ApiEnvelope<any>>(`${this.base}/profile/${id}`);
  }

  // ============ Exports + Print (open as new tab, browser handles download / print) ============
  exportExcelUrl(params?: { status?: string; workOrderId?: number; search?: string; showDeleted?: string }): string {
    return this.buildUrl('/export-excel', params);
  }

  exportPdfUrl(params?: { status?: string; workOrderId?: number; search?: string; showDeleted?: string }): string {
    return this.buildUrl('/export-pdf', params);
  }

  private buildUrl(path: string, params?: any): string {
    const qs = new URLSearchParams();
    if (params?.status) qs.set('status', params.status);
    if (params?.workOrderId) qs.set('workOrderId', String(params.workOrderId));
    if (params?.search) qs.set('search', params.search);
    if (params?.showDeleted) qs.set('showDeleted', params.showDeleted);
    const q = qs.toString();
    return `${this.base}${path}${q ? '?' + q : ''}`;
  }
}
