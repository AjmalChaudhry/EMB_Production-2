import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, ApiResponseData } from '../shared/models/api-response';
import { PurchaseDto, PoPrefillPayload } from '../shared/models/purchase.dto';
import {PurchaseRowViewModel,PurchaseDetailViewModel
} from '../shared/models/purchase-row.viewdto';
import { DropdownItem } from '../shared/models/api-response';

@Injectable({ providedIn: 'root' })
export class PurchaseService {
  private baseUrl = '/emb-products/purchase';

  constructor(private http: HttpClient) { }

  list(opts: {
    fromDate?: string | null;
    toDate?: string | null;
    supplierId?: number | null;
    status?: string | null;
    itemSearch?: string | null;
    invoiceSearch?: string | null;
  } = {}): Observable<ApiResponseData<{ rows: PurchaseRowViewModel[]; summary: any }>> {
    let params = new HttpParams();
    if (opts.fromDate)      params = params.set('fromDate', opts.fromDate);
    if (opts.toDate)        params = params.set('toDate', opts.toDate);
    if (opts.supplierId)    params = params.set('supplierId', String(opts.supplierId));
    if (opts.status)        params = params.set('status', opts.status);
    if (opts.itemSearch)    params = params.set('itemSearch', opts.itemSearch);
    if (opts.invoiceSearch) params = params.set('invoiceSearch', opts.invoiceSearch);

    return this.http.get<ApiResponseData<{ rows: PurchaseRowViewModel[]; summary: any }>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  detail(id: number): Observable<ApiResponseData<PurchaseDetailViewModel>> {
    return this.http.get<ApiResponseData<PurchaseDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  create(dto: PurchaseDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(dto: PurchaseDto): Observable<ApiResponse> {
    if (!dto.id) {
      throw new Error('Purchase id is required for update.');
    }

    return this.http.post<ApiResponse>(
      `${this.baseUrl}/update/${dto.id}`,
      dto
    );
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  previewPost(id: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(
      `${this.baseUrl}/preview-post/${id}`
    );
  }

  postPurchase(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/post/${id}`, {});
  }

  exportExcel(opts: {
    fromDate?: string | null;
    toDate?: string | null;
    supplierId?: number | null;
    status?: string | null;
    itemSearch?: string | null;
    invoiceSearch?: string | null;
  } = {}): Observable<Blob> {
    let params = new HttpParams();
    if (opts.fromDate)      params = params.set('fromDate', opts.fromDate);
    if (opts.toDate)        params = params.set('toDate', opts.toDate);
    if (opts.supplierId)    params = params.set('supplierId', String(opts.supplierId));
    if (opts.status)        params = params.set('status', opts.status);
    if (opts.itemSearch)    params = params.set('itemSearch', opts.itemSearch);
    if (opts.invoiceSearch) params = params.set('invoiceSearch', opts.invoiceSearch);

    return this.http.get(`${this.baseUrl}/export/excel`, {
      responseType: 'blob',
      params
    });
  }

  exportPdf(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export/pdf`, {
      responseType: 'blob'
    });
  }

  getBranches(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/branches'
    );
  }

  getSuppliers(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/suppliers'
    );
  }

  getRawItems(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/raw-items'
    );
  }

  getThreads(): Observable<ApiResponseData<Array<{
    id: number; code: string; name: string; color: string;
    unit: string; unitCost: number; text: string;
  }>>> {
    return this.http.get<ApiResponseData<any>>(`${this.baseUrl}/dropdowns/threads`);
  }

  getFabrics(): Observable<ApiResponseData<Array<{
    id: number; code: string; name: string; text: string;
  }>>> {
    return this.http.get<ApiResponseData<any>>(`${this.baseUrl}/dropdowns/fabrics`);
  }

  reversePurchase(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reverse/${id}`, {});
  }

  // Returns supplier/branch/items pre-populated from an open PO for
  // the New Purchase (GRN) modal. Outstanding qty per line.
  prefillFromPo(poId: number): Observable<ApiResponseData<PoPrefillPayload>> {
    return this.http.get<ApiResponseData<PoPrefillPayload>>(
      `${this.baseUrl}/prefill-from-po/${poId}`);
  }

  // Roll up PO ReceivedQty per line and update PO header status after
  // GRN save. Fire-and-forget from the UI; the PO modal refreshes on
  // its own load.
  syncPoReceipts(poId: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `/emb-products/purchase-order/sync-receipts/${poId}`, {});
  }

  // Returns every PO currently SENT or PARTIALLY_RECEIVED — used by
  // the Purchase list's "Receive against PO" dropdown so the buyer
  // can start a pre-filled GRN without leaving the Purchase screen.
  openPos(): Observable<ApiResponseData<Array<{
    id: number;
    poCode: string;
    supplierId: number;
    supplierName: string;
    orderDate: string;
    status: string;
    totalAmount: number;
    receivedAmount: number;
    outstanding: number;
  }>>> {
    return this.http.get<ApiResponseData<any>>(
      `/emb-products/purchase-order/open-list`);
  }

  // Purchase returns — material-only, no debit-note flow
  getReturnInfo(id: number): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(`${this.baseUrl}/return-info/${id}`);
  }

  submitReturn(id: number, payload: {
    reason: string | null;
    lines: Array<{ purchaseItemId: number; returnQty: number; remarks: string | null }>;
  }): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/return/${id}`, payload);
  }

  getReturnHistory(id: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(`${this.baseUrl}/returns/${id}`);
  }

  // Phase F.2 - clone a purchase into a new draft
  duplicate(id: number): Observable<ApiResponseData<{ id: number; invoiceNo: string; itemCount: number }>> {
    return this.http.post<ApiResponseData<{ id: number; invoiceNo: string; itemCount: number }>>(
      `${this.baseUrl}/duplicate/${id}`,
      {}
    );
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<ApiResponseData<PurchaseAuditEntry[]>> {
    return this.http.get<ApiResponseData<PurchaseAuditEntry[]>>(
      `${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk actions. Each returns the per-id failure
  //  envelope so the toolbar can render "n succeeded, m failed".
  // ───────────────────────────────────────────────────────────────
  bulkPost(ids: number[]): Observable<PurchaseBulkResponse> {
    return this.http.post<PurchaseBulkResponse>(`${this.baseUrl}/bulk-post`, { ids });
  }
  bulkReverse(ids: number[]): Observable<PurchaseBulkResponse> {
    return this.http.post<PurchaseBulkResponse>(`${this.baseUrl}/bulk-reverse`, { ids });
  }
  bulkDelete(ids: number[]): Observable<PurchaseBulkResponse> {
    return this.http.post<PurchaseBulkResponse>(`${this.baseUrl}/bulk-delete`, { ids });
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — preview-then-save Clone. Pass `overrides` to apply
  //  the buyer's preview edits (qty / rate / invoice / remarks) at
  //  commit time so we don't round-trip Clone-then-Update.
  // ───────────────────────────────────────────────────────────────
  clone(id: number, overrides?: PurchaseCloneOverride): Observable<ApiResponseData<{
    id: number; sourceId: number; invoiceNo: string; itemsCloned: number;
  }>> {
    return this.http.post<ApiResponseData<{
      id: number; sourceId: number; invoiceNo: string; itemsCloned: number;
    }>>(`${this.baseUrl}/clone/${id}`, overrides ?? {});
  }

  // Phase F.3 - analytics strip data
  analytics(opts: {
    fromDate?: string | null;
    toDate?: string | null;
    supplierId?: number | null;
  } = {}): Observable<ApiResponseData<{
    topSuppliers: Array<{ supplierId: number; supplierName: string; purchaseCount: number; totalAmount: number }>;
    topItems: Array<{ itemName: string; itemType: string; totalQty: number; totalAmount: number }>;
    monthlyTrend: Array<{ month: string; count: number; amount: number }>;
  }>> {
    let params = new HttpParams();
    if (opts.fromDate)   params = params.set('fromDate', opts.fromDate);
    if (opts.toDate)     params = params.set('toDate', opts.toDate);
    if (opts.supplierId) params = params.set('supplierId', String(opts.supplierId));
    return this.http.get<any>(`${this.baseUrl}/analytics`, { params });
  }
}

// ─────────────────────────────────────────────────────────────────
//  Sprint-2 — one entry in the audit-log timeline.
// ─────────────────────────────────────────────────────────────────
export interface PurchaseAuditEntry {
  id: number;
  purchaseId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}

// ─────────────────────────────────────────────────────────────────
//  Sprint-3 — standard bulk-action envelope.
// ─────────────────────────────────────────────────────────────────
export interface PurchaseBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}

// ─────────────────────────────────────────────────────────────────
//  Sprint-3 — preview-edit overrides sent with Clone.
//  `items[].sourceItemId` keys back to PurchaseItems.Id on the
//  source row so the backend can match per line.
// ─────────────────────────────────────────────────────────────────
export interface PurchaseCloneOverride {
  purchaseDate?: string | null;
  invoiceNo?: string;
  remarks?: string;
  items?: Array<{
    sourceItemId: number;
    qty?: number;
    rate?: number;
    remarks?: string;
  }>;
}
