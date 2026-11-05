import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  PrRow, PrDetail, PrSummary, PrSavePayload,
  SupplierDropdown, RawItemDropdown, ShortfallSuggestion
} from '../shared/models/purchase-requisition.dto';

interface ApiEnvelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class PurchaseRequisitionService {
  private base = '/emb-products/purchase-requisition';

  constructor(private http: HttpClient) {}

  list(params?: {
    status?: string; branchId?: number; supplierId?: number;
    search?: string; showDeleted?: string;
    // Sprint-3 filters
    sourceType?: string; fromDate?: string; toDate?: string;
  }): Observable<ApiEnvelope<{ items: PrRow[]; summary: PrSummary }>> {
    const qs = new URLSearchParams();
    if (params?.status) qs.set('status', params.status);
    if (params?.branchId) qs.set('branchId', String(params.branchId));
    if (params?.supplierId) qs.set('supplierId', String(params.supplierId));
    if (params?.search) qs.set('search', params.search);
    if (params?.showDeleted) qs.set('showDeleted', params.showDeleted);
    if (params?.sourceType) qs.set('sourceType', params.sourceType);
    if (params?.fromDate)   qs.set('fromDate',   params.fromDate);
    if (params?.toDate)     qs.set('toDate',     params.toDate);
    return this.http.get<ApiEnvelope<{ items: PrRow[]; summary: PrSummary }>>(
      `${this.base}/list${qs.toString() ? '?' + qs.toString() : ''}`);
  }

  detail(id: number): Observable<ApiEnvelope<PrDetail>> {
    return this.http.get<ApiEnvelope<PrDetail>>(`${this.base}/detail/${id}`);
  }
  nextCode(): Observable<ApiEnvelope<{ code: string }>> {
    return this.http.get<ApiEnvelope<{ code: string }>>(`${this.base}/next-code`);
  }
  suppliers(): Observable<ApiEnvelope<SupplierDropdown[]>> {
    return this.http.get<ApiEnvelope<SupplierDropdown[]>>(`${this.base}/suppliers-dropdown`);
  }
  rawItems(): Observable<ApiEnvelope<RawItemDropdown[]>> {
    return this.http.get<ApiEnvelope<RawItemDropdown[]>>(`${this.base}/raw-items-dropdown`);
  }

  // Returns raw items whose current store balance is below Gen_RawItems.ReorderLevel.
  // Used to auto-fill the New PR modal with shortage rows the buyer can edit.
  shortfallSuggestions(): Observable<ApiEnvelope<{ count: number; items: ShortfallSuggestion[] }>> {
    return this.http.get<ApiEnvelope<{ count: number; items: ShortfallSuggestion[] }>>(
      `${this.base}/shortfall-suggestions`);
  }

  create(payload: PrSavePayload): Observable<ApiEnvelope<{ id: number; prCode: string }>> {
    return this.http.post<ApiEnvelope<{ id: number; prCode: string }>>(`${this.base}/create`, payload);
  }
  update(payload: PrSavePayload): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/update`, payload);
  }
  submit(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/submit/${id}`, {});
  }
  approve(id: number): Observable<ApiEnvelope<{ poId: number; poCode: string; itemCount: number; totalAmount: number }>> {
    return this.http.post<ApiEnvelope<{ poId: number; poCode: string; itemCount: number; totalAmount: number }>>(`${this.base}/approve/${id}`, {});
  }
  reject(id: number, reason: string): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/reject/${id}`, { reason });
  }
  remove(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/delete/${id}`, {});
  }

  // Change supplier on a PR. If the PR has converted to a PO and the PO is
  // still safe to modify (no receipts), the PO supplier is updated atomically
  // in the same call. See ChangeSupplier on the controller for the full
  // safety rules.
  changeSupplier(id: number, supplierId: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(
      `${this.base}/change-supplier/${id}`, { supplierId });
  }

  /** Sprint-2: append-only audit timeline for the detail drawer. Newest
   *  entry first. Empty for PRs created before the table existed. */
  getAuditLog(id: number): Observable<ApiEnvelope<PrAuditEntry[]>> {
    return this.http.get<ApiEnvelope<PrAuditEntry[]>>(`${this.base}/audit-log/${id}`);
  }

  /** Sprint-3 bulk actions. Each returns a per-id success/failure report
   *  so the UI can show "3 approved, 2 failed (reason: …)". */
  bulkApprove(ids: number[]): Observable<PrBulkResponse> {
    return this.http.post<PrBulkResponse>(`${this.base}/bulk-approve`, { ids });
  }
  bulkReject(ids: number[], reason: string): Observable<PrBulkResponse> {
    return this.http.post<PrBulkResponse>(`${this.base}/bulk-reject`, { ids, reason });
  }
  bulkDelete(ids: number[]): Observable<PrBulkResponse> {
    return this.http.post<PrBulkResponse>(`${this.base}/bulk-delete`, { ids });
  }

  /** Sprint-3: clone a PR into a fresh DRAFT with same supplier + items. */
  clone(id: number): Observable<ApiEnvelope<{
    id: number; prCode: string; sourceId: number; sourceCode: string; itemsCloned: number;
  }>> {
    return this.http.post<ApiEnvelope<{
      id: number; prCode: string; sourceId: number; sourceCode: string; itemsCloned: number;
    }>>(`${this.base}/clone/${id}`, {});
  }
}

/** Sprint-3: standard bulk-action envelope shared by approve/reject/delete. */
export interface PrBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}

/** Sprint-2: shape of one audit-log entry. Matches the controller projection. */
export interface PrAuditEntry {
  id: number;
  prId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}
