import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PoRow, PoDetail, PoSummary, PoUpdatePayload } from '../shared/models/purchase-order.dto';

interface ApiEnvelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class PurchaseOrderService {
  private base = '/emb-products/purchase-order';

  constructor(private http: HttpClient) {}

  list(params?: {
    status?: string; branchId?: number; supplierId?: number;
    search?: string; showDeleted?: string;
    // Sprint-1: date range filter on OrderDate.
    fromDate?: string; toDate?: string;
  }): Observable<ApiEnvelope<{ items: PoRow[]; summary: PoSummary }>> {
    const qs = new URLSearchParams();
    if (params?.status) qs.set('status', params.status);
    if (params?.branchId) qs.set('branchId', String(params.branchId));
    if (params?.supplierId) qs.set('supplierId', String(params.supplierId));
    if (params?.search) qs.set('search', params.search);
    if (params?.showDeleted) qs.set('showDeleted', params.showDeleted);
    if (params?.fromDate) qs.set('fromDate', params.fromDate);
    if (params?.toDate)   qs.set('toDate',   params.toDate);
    return this.http.get<ApiEnvelope<{ items: PoRow[]; summary: PoSummary }>>(
      `${this.base}/list${qs.toString() ? '?' + qs.toString() : ''}`);
  }

  detail(id: number): Observable<ApiEnvelope<PoDetail>> {
    return this.http.get<ApiEnvelope<PoDetail>>(`${this.base}/detail/${id}`);
  }

  update(payload: PoUpdatePayload): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/update`, payload);
  }

  send(id: number):   Observable<ApiEnvelope<unknown>> { return this.http.post<ApiEnvelope<unknown>>(`${this.base}/send/${id}`,   {}); }
  close(id: number):  Observable<ApiEnvelope<unknown>> { return this.http.post<ApiEnvelope<unknown>>(`${this.base}/close/${id}`,  {}); }
  cancel(id: number, reason: string): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/cancel/${id}`, { reason });
  }
  syncReceipts(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/sync-receipts/${id}`, {});
  }

  openItems(id: number): Observable<ApiEnvelope<any[]>> {
    return this.http.get<ApiEnvelope<any[]>>(`${this.base}/open-items/${id}`);
  }
  openList(): Observable<ApiEnvelope<any[]>> {
    return this.http.get<ApiEnvelope<any[]>>(`${this.base}/open-list`);
  }

  /** Sprint-2: append-only audit timeline for the detail drawer. */
  getAuditLog(id: number): Observable<ApiEnvelope<PoAuditEntry[]>> {
    return this.http.get<ApiEnvelope<PoAuditEntry[]>>(`${this.base}/audit-log/${id}`);
  }

  /** Sprint-3 bulk actions. Each returns per-id success/failure report
   *  so the UI can show "3 sent, 1 failed (reason: …)". */
  bulkSend(ids: number[]): Observable<PoBulkResponse> {
    return this.http.post<PoBulkResponse>(`${this.base}/bulk-send`, { ids });
  }
  bulkClose(ids: number[]): Observable<PoBulkResponse> {
    return this.http.post<PoBulkResponse>(`${this.base}/bulk-close`, { ids });
  }
  bulkCancel(ids: number[], reason: string): Observable<PoBulkResponse> {
    return this.http.post<PoBulkResponse>(`${this.base}/bulk-cancel`, { ids, reason });
  }
  bulkDelete(ids: number[]): Observable<PoBulkResponse> {
    return this.http.post<PoBulkResponse>(`${this.base}/bulk-delete`, { ids });
  }

  /** Soft-delete a single PO. Backend refuses anything that isn't DRAFT
   *  or CANCELLED — Sent / Received / Closed must be cancelled first. */
  delete(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/delete/${id}`, {});
  }

  /** Sprint-3: deep-copy a PO into a fresh DRAFT. If overrides are passed,
   *  the buyer's preview edits (qty / rate / dates / terms) are applied on
   *  the new record — saves the round-trip of "clone then edit". */
  clone(id: number, overrides?: PoCloneOverride): Observable<ApiEnvelope<{
    id: number; poCode: string; sourceId: number; sourceCode: string; itemsCloned: number;
  }>> {
    return this.http.post<ApiEnvelope<{
      id: number; poCode: string; sourceId: number; sourceCode: string; itemsCloned: number;
    }>>(`${this.base}/clone/${id}`, overrides ?? {});
  }
}

/** Optional per-line + header overrides for the Clone endpoint. Sent
 *  only when the buyer edits the clone preview before saving. */
export interface PoCloneOverride {
  expectedDeliveryDate?: string | null;
  paymentTerms?: string;
  deliveryTerms?: string;
  remarks?: string;
  items?: Array<{
    rawItemId: number;
    orderedQty?: number;
    rate?: number;
    remarks?: string;
  }>;
}

/** Sprint-3: standard bulk-action envelope shared by send/close/cancel. */
export interface PoBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}

/** Sprint-2: shape of one audit-log entry. */
export interface PoAuditEntry {
  id: number;
  poId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}
