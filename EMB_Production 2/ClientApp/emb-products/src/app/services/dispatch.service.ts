import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {ApiResponse,ApiResponseData,ApiPagedResponse,DropdownItem} from '../shared/models/api-response';
import {DispatchRowViewModel,DispatchDetailViewModel,DispatchDto,DispatchStockRow,
  DispatchItemViewModel,SaleType} from '../shared/models/dispatch.dto';

@Injectable({ providedIn: 'root' })
export class DispatchService {
  private readonly baseUrl = '/emb-products/dispatch';

  constructor(private http: HttpClient) { }

  list(
    search = '',
    activeFilter = '',
    showDeleted = '',
    page = 1,
    pageSize = 10,
    extra: {
      fromDate?: string | null;
      toDate?: string | null;
      customerId?: number | null;
      jobOrderId?: number | null;
      branchId?: number | null;
      posted?: string | null;
      dispatchType?: string | null;
      saleType?: string | null;
    } = {}
  ): Observable<ApiPagedResponse<DispatchRowViewModel>> {
    let params = new HttpParams()
      .set('search', search || '')
      .set('activeFilter', activeFilter || '')
      .set('showDeleted', showDeleted || '')
      .set('page', String(page))
      .set('pageSize', String(pageSize));

    if (extra.fromDate)     params = params.set('fromDate', extra.fromDate);
    if (extra.toDate)       params = params.set('toDate', extra.toDate);
    if (extra.customerId)   params = params.set('customerId', String(extra.customerId));
    if (extra.jobOrderId)   params = params.set('jobOrderId', String(extra.jobOrderId));
    if (extra.branchId)     params = params.set('branchId', String(extra.branchId));
    if (extra.posted)       params = params.set('posted', extra.posted);
    if (extra.dispatchType) params = params.set('dispatchType', extra.dispatchType);
    if (extra.saleType)     params = params.set('saleType', extra.saleType);

    return this.http.get<ApiPagedResponse<DispatchRowViewModel>>(
      `${this.baseUrl}/list`,
      { params }
    );
  }

  // Phase O.1 - Dispatch 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  detail(id: number): Observable<ApiResponseData<DispatchDetailViewModel>> {
    return this.http.get<ApiResponseData<DispatchDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  getById(id: number): Observable<ApiResponseData<DispatchDetailViewModel>> {
    return this.detail(id);
  }

  create(dto: DispatchDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(id: number, dto: DispatchDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, {
      ...dto,
      id
    });
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  post(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/post/${id}`, {});
  }

  reverse(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reverse/${id}`, {});
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(`${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk actions. Each returns { successCount,
  //  failureCount, failures } so the toolbar can render a per-id
  //  failure summary.
  // ───────────────────────────────────────────────────────────────
  bulkPost(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-post`, { ids });
  }
  bulkReverse(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-reverse`, { ids });
  }
  bulkDelete(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-delete`, { ids });
  }

  getLedger(dispatchId: number): Observable<ApiResponseData<any[]>> {
    return this.http.get<ApiResponseData<any[]>>(
      `${this.baseUrl}/ledger/${dispatchId}`
    );
  }

  getDispatchItems(dispatchId: number): Observable<ApiResponseData<DispatchItemViewModel[]>> {
    return this.http.get<ApiResponseData<DispatchItemViewModel[]>>(
      `${this.baseUrl}/items/${dispatchId}`
    );
  }

  getAvailableProducts(
    branchId: number | null = null,
    search = '',
    customerId: number | null = null,
    saleType: SaleType = 'CUSTOMER_DEMAND',
    jobOrderId: number | null = null,
    excludeDispatchId: number | null = null
  ): Observable<ApiResponseData<DispatchStockRow[]>> {

    let params = new HttpParams()
      .set('search', search || '')
      .set('saleType', saleType || 'CUSTOMER_DEMAND');

    if (branchId && branchId > 0) {
      params = params.set('branchId', String(branchId));
    }

    if (customerId && customerId > 0) {
      params = params.set('customerId', String(customerId));
    }

    if (jobOrderId && jobOrderId > 0) {
      params = params.set('jobOrderId', String(jobOrderId));
    }

    if (excludeDispatchId && excludeDispatchId > 0) {
      params = params.set('excludeDispatchId', String(excludeDispatchId));
    }

    return this.http.get<ApiResponseData<DispatchStockRow[]>>(
      '/emb-products/dispatch-items/available-products',
      { params }
    );
  }

  getCustomers(branchId: number | null = null): Observable<ApiResponseData<DropdownItem[]>> {
    let params = new HttpParams();

    if (branchId && branchId > 0) {
      params = params.set('branchId', String(branchId));
    }
   
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/dispatch-customers',
      { params }
    );
  }

  getCustomerJobOrders(customerId: number, branchId?: number | null): Observable<any> {
    let params = new HttpParams()
      .set('customerId', String(customerId));

    if (branchId && branchId > 0) {
      params = params.set('branchId', String(branchId));
    }

    return this.http.get<any>(
      `/emb-products/dispatch-items/customer-job-orders`,
      { params }
    );
  }

  getBranches(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<ApiResponseData<DropdownItem[]>>(
      '/emb-products/dropdowns/branches'
    );
  }
}
