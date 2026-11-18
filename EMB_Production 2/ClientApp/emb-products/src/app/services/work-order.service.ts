import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {ApiResponse,ApiResponseList,ApiResponseData} from '../shared/models/api-response';
import {WorkOrderDto,WorkOrderRowViewModel,WorkOrderDetailViewModel} from '../shared/models/work-order.dto';

@Injectable({
  providedIn: 'root'
})
export class WorkOrderService {

  private baseUrl = '/emb-products/work-orders';

  constructor(private http: HttpClient) { }

  // =====================================================
  // LIST
  // =====================================================
  getAll(
    search: string = '',
    status: string = '',
    page: number = 1,
    pageSize: number = 10,
    extra: {
      fromDate?: string | null;
      toDate?: string | null;
      customerId?: number | null;
      branchId?: number | null;
      posted?: string | null;
      orderType?: string | null;
      workOrderStatus?: string | null;
      jobOrderId?: number | null;
    } = {}
  ): Observable<ApiResponseList<WorkOrderRowViewModel> & { summary?: any }> {

    let params = new HttpParams()
      .set('search', search ?? '')
      .set('status', status ?? '')
      .set('page', page.toString())
      .set('pageSize', pageSize.toString());

    if (extra.fromDate)   params = params.set('fromDate', extra.fromDate);
    if (extra.toDate)     params = params.set('toDate', extra.toDate);
    if (extra.customerId) params = params.set('customerId', String(extra.customerId));
    if (extra.branchId)   params = params.set('branchId', String(extra.branchId));
    if (extra.posted)     params = params.set('posted', extra.posted);
    if (extra.orderType)  params = params.set('orderType', extra.orderType);
    if (extra.workOrderStatus) params = params.set('workOrderStatus', extra.workOrderStatus);
    if (extra.jobOrderId && extra.jobOrderId > 0) params = params.set('jobOrderId', String(extra.jobOrderId));

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map(res => {
        const rawItems = res?.data?.items || res?.data || [];

        const items = rawItems.map((x: any) => this.mapRow(x));

        return {
          success: res?.success === true,
          message: res?.message,
          data: {
            items,
            total: res?.data?.total ?? res?.data?.totalItems ?? 0,
            page: res?.data?.page ?? page,
            pageSize: res?.data?.pageSize ?? pageSize
          },
          summary: res?.data?.summary ?? res?.data?.Summary ?? null
        };
      })
    );
  }
  // =====================================================
  // PENDING FOR MATERIAL ISSUE
  // =====================================================
  // Work orders that still need materials issued against them (posted,
  // not completed, no MATERIAL_ISSUE row yet). Always fetched fresh —
  // deliberately not cached, since issuing materials against a WO should
  // immediately remove it from this list.
  getPendingForIssue(): Observable<Array<{ id: number; code: string; name: string }>> {
    return this.http.get<any>(`/emb-products/dropdowns/work-orders-pending-issue`).pipe(
      map(res => Array.isArray(res?.data) ? res.data : [])
    );
  }

  // =====================================================
  // DETAIL
  // =====================================================
  getDetail(id: number): Observable<ApiResponseData<WorkOrderDetailViewModel>> {

    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map(res => ({
        success: res?.success === true,
        message: res?.message,
        data: this.mapDetail(res?.data)
      }))
    );
  }

  // =====================================================
  // CREATE
  // =====================================================
  create(dto: WorkOrderDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  // =====================================================
  // UPDATE
  // =====================================================
  update(dto: WorkOrderDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/update/${dto.id}`,
      dto
    );
  }

  // =====================================================
  // DELETE
  // =====================================================
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/delete/${id}`,
      {}
    );
  }

  // =====================================================
  // DROPDOWNS
  // =====================================================

  getCustomers(): Observable<any[]> {
    return this.http.get<any>(`/emb-products/joborders/customers`).pipe(
      map((res: any) => {
        const data = res?.data ?? res?.Data ?? res;
        return Array.isArray(data) ? data : [];
      })
    );
  }

  getProducts() {
    return this.http.get<any>(`/emb-products/dropdowns/products`)
      .pipe(map(r => r?.data ?? r ?? []));
  }

  getJobOrders(): Observable<any[]> {
    return this.http
      .get<any>('/emb-products/work-orders/joborders-dropdown')
      .pipe(
        map((res: any) => {
          const raw = res?.data ?? res?.Data ?? [];
          return Array.isArray(raw) ? raw : [];
        })
      );
  }

  getJobOrderDetail(id: number): Observable<any> {
    return this.http.get<any>(`/emb-products/joborders/detail/${id}`);
  }

  getBranches() {
    return this.http.get<any>(`/emb-products/dropdowns/branches`)
      .pipe(map(r => r?.data ?? []));
  }

  getDesigns() {
    return this.http.get<any>(`/emb-products/dropdowns/designs`)
      .pipe(map(r => r?.data ?? []));
  }
 
  // =====================================================
  // NEXT CODE
  // =====================================================
  getNextCode(): Observable<string> {
    return this.http.get<any>(`${this.baseUrl}/next-code`).pipe(
      map(r => r?.data?.code ?? r?.code ?? '')
    );
  }

  // =====================================================
  // EXPORT
  // =====================================================
  exportExcel(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export-excel`, {
      responseType: 'blob'
    });
  }

  exportPdf(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export-pdf`, {
      responseType: 'blob'
    });
  }

  post(id: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/post/${id}`, {});
  }

  reversePost(id: number): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/reverse-post/${id}`, {});
  }

  // Phase L.1 - Work Order 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  // Sprint-2: audit log timeline (newest-first list of state transitions)
  getAuditLog(id: number): Observable<{ success: boolean; message: string; data: WorkOrderAuditEntry[] }> {
    return this.http.get<{ success: boolean; message: string; data: WorkOrderAuditEntry[] }>(
      `${this.baseUrl}/audit-log/${id}`);
  }

  // Sprint-2: cheap workflow-stage derivation (canonical stage list +
  // current index + the three downstream flags that drive it). Backend
  // computes from IsPosted + Status + downstream presence so the UI
  // never has to re-derive.
  getWorkflowStage(id: number): Observable<{ success: boolean; message: string; data: WorkOrderWorkflowStage }> {
    return this.http.get<{ success: boolean; message: string; data: WorkOrderWorkflowStage }>(
      `${this.baseUrl}/workflow-stage/${id}`);
  }

  /** Sprint-3 bulk actions. Each returns a per-id success/failure report
   *  so the UI can show "5 posted, 2 failed (reason: Already posted, …)". */
  bulkPost(ids: number[]): Observable<WoBulkResponse> {
    return this.http.post<WoBulkResponse>(`${this.baseUrl}/bulk-post`, { ids });
  }
  bulkReverse(ids: number[]): Observable<WoBulkResponse> {
    return this.http.post<WoBulkResponse>(`${this.baseUrl}/bulk-reverse`, { ids });
  }
  bulkDelete(ids: number[]): Observable<WoBulkResponse> {
    return this.http.post<WoBulkResponse>(`${this.baseUrl}/bulk-delete`, { ids });
  }

  /** Sprint-3: clone a WO into a new DRAFT with a fresh code. Qty is
   *  capped to the JO's remaining qty server-side. Refuses if the JO
   *  has no remaining qty (operator must create a new JO first). */
  clone(id: number): Observable<{ success: boolean; message: string; data: {
    id: number; workOrderCode: string; sourceId: number; sourceCode: string;
    orderQty: number; qtyCapped: boolean;
  } }> {
    return this.http.post<any>(`${this.baseUrl}/clone/${id}`, {});
  }

  private toDateInputValue(value: any): string | null {
    if (!value) return null;

    if (typeof value === 'string' && value.includes('/Date(')) {
      const timestamp = Number(value.replace('/Date(', '').replace(')/', ''));
      return new Date(timestamp).toISOString().substring(0, 10);
    }

    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value)) {
      return value.substring(0, 10);
    }

    const date = new Date(value);
    if (isNaN(date.getTime())) return null;

    return date.toISOString().substring(0, 10);
  }

  // =====================================================
  // 🔥 MAPPERS (CRITICAL)
  // =====================================================

  private mapRow(x: any): WorkOrderRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      workOrderCode: x?.workOrderCode ?? x?.WorkOrderCode ?? '',

      orderType: x?.orderType ?? x?.OrderType ?? 'Customer',
      isCompanyStockOrder: Boolean(x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder ?? false),

      jobOrderId: Number(x?.jobOrderId ?? x?.JobOrderId ?? 0) || null,
      jobOrderCode: x?.jobOrderCode ?? x?.JobOrderCode ?? null,

      customerId: (x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder)? null
        : (Number(x?.customerId ?? x?.CustomerId ?? 0) || null),
      customerName: (x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder)? 'Stock'
        : (x?.customerName ?? x?.CustomerName ?? null),

      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      productCode: x?.productCode ?? x?.ProductCode ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      designId: Number(x?.designId ?? x?.DesignId ?? 0) || null,
      designCode: x?.designCode ?? x?.DesignCode ?? null,
      designName: x?.designName ?? x?.DesignName ?? null,

      branchId: Number(x?.branchId ?? x?.BranchId ?? 0) || null,
      branchName: x?.branchName ?? x?.BranchName ?? null,

      orderQty: Number(x?.orderQty ?? x?.OrderQty ?? 0) || null,

      orderDate: this.toDateInputValue(
        x?.orderDate ??
        x?.OrderDate ??
        x?.orderDateText ??
        x?.OrderDateText
      ) || '',

      dueDate: this.toDateInputValue(
        x?.dueDate ??
        x?.DueDate ??
        x?.dueDateText ??
        x?.DueDateText
      ),

      referenceNo: x?.referenceNo ?? x?.ReferenceNo ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      status: x?.status ?? x?.Status ?? null,
      active: x?.active ?? x?.Active ?? true,
      deleted: x?.deleted ?? x?.Deleted ?? false,

      isPosted: x?.isPosted ?? x?.IsPosted ?? false,

      // ✅ FIX: ADD THESE
      postedBy: Number(x?.postedBy ?? x?.PostedBy ?? 0) || null,
      postedByEmployeeId: Number(x?.postedByEmployeeId ?? x?.PostedByEmployeeId ?? 0) || null,
      postedByName: x?.postedByName ?? x?.PostedByName ?? null,
      postedDate: this.toDateInputValue(
        x?.postedDate ??
        x?.PostedDate ??
        x?.postedDateText ??
        x?.PostedDateText
      ),

      recordTimeStamp: x?.recordTimeStamp ?? x?.RecordTimeStamp ?? null,
      createdDate: this.toDateInputValue(
        x?.createdDate ?? x?.CreatedDate ?? x?.createdDateText
      ),
      updatedDate: this.toDateInputValue(
        x?.updatedDate ?? x?.UpdatedDate ?? x?.updatedDateText
      ),

      // Sprint-1 enrichments — must be copied through here or the
      // VM fields never reach the component (the mapRow whitelist
      // trap that bit Job Orders).
      producedQty:         Number(x?.producedQty         ?? x?.ProducedQty         ?? 0),
      completionPercent:   Number(x?.completionPercent   ?? x?.CompletionPercent   ?? 0),
      planId:              this.toNullableNumber(x?.planId ?? x?.PlanId),
      planCode:            x?.planCode  ?? x?.PlanCode  ?? null,
      dailyProductionCount: Number(x?.dailyProductionCount ?? x?.DailyProductionCount ?? 0),
      qcCount:             Number(x?.qcCount             ?? x?.QcCount             ?? 0),
      dispatchCount:       Number(x?.dispatchCount       ?? x?.DispatchCount       ?? 0),
      orderDateText:       x?.orderDateText ?? x?.OrderDateText ?? null,
      dueDateText:         x?.dueDateText   ?? x?.DueDateText   ?? null
    };
  }

  private mapDetail(x: any): WorkOrderDetailViewModel {
    if (!x) return {} as any;

    return {
      ...this.mapRow(x),

      createdBy: x.createdBy ?? x.CreatedBy ?? null,
      updatedBy: x.updatedBy ?? x.UpdatedBy ?? null,

      createdByEmployeeId: x.createdByEmployeeId ?? x.CreatedByEmployeeId ?? null,
      updatedByEmployeeId: x.updatedByEmployeeId ?? x.UpdatedByEmployeeId ?? null
    };
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }
}

/** Sprint-2: one audit-log row shape. Matches the controller's
 *  AuditLog endpoint projection (camelCase). */
export interface WorkOrderAuditEntry {
  id: number;
  workOrderId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}

/** Sprint-2: workflow-stage response shape. */
export interface WorkOrderWorkflowStage {
  workflowStages: string[];
  workflowStageIndex: number;
  hasProduction: boolean;
  hasQc: boolean;
  hasDispatch: boolean;
}

/** Sprint-3: standard bulk-action envelope shared by post/reverse/delete. */
export interface WoBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}
