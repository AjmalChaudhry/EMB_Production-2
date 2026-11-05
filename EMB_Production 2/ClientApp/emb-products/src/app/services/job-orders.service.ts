import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  ApiResponse,
  ApiResponseList,
  ApiResponseData,
  ApiPagedResponse
} from '../shared/models/api-response';

import {
  JobOrderRowViewModel,
  JobOrderDetailViewModel,
  JobOrderDto,
  JobOrderItemViewModel,
  JobOrderType
} from '../shared/models/job-order.dto';

@Injectable({ providedIn: 'root' })
export class JobOrdersService {

  // IMPORTANT: this is your existing working controller route
  private baseUrl = '/emb-products/joborders';

  constructor(private http: HttpClient) { }

  getAll(
    search: string = '',
    activeFilter: string = '',
    page: number = 1,
    pageSize: number = 20,
    extra: {
      fromDate?: string | null;
      toDate?: string | null;
      customerId?: number | null;
      branchId?: number | null;
      posted?: string | null;
      orderType?: string | null;
      priority?: string | null;
      status?: string | null;
    } = {}
  ): Observable<ApiPagedResponse<JobOrderRowViewModel> & { summary?: any }> {

    let params = new HttpParams()
      .set('search', search ?? '')
      .set('activeFilter', activeFilter ?? '')
      .set('page', String(page))
      .set('pageSize', String(pageSize));

    if (extra.fromDate)   params = params.set('fromDate', extra.fromDate);
    if (extra.toDate)     params = params.set('toDate', extra.toDate);
    if (extra.customerId) params = params.set('customerId', String(extra.customerId));
    if (extra.branchId)   params = params.set('branchId', String(extra.branchId));
    if (extra.posted)     params = params.set('posted', extra.posted);
    if (extra.orderType)  params = params.set('orderType', extra.orderType);
    if (extra.priority)   params = params.set('priority', extra.priority);
    if (extra.status)     params = params.set('status', extra.status);

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map((res: any) => {
        const raw = res?.data ?? res?.Data ?? {};

        const rawItems = Array.isArray(raw?.items)
          ? raw.items
          : Array.isArray(raw?.Items)
            ? raw.Items
            : Array.isArray(raw)
              ? raw
              : [];

        const items = rawItems.map((x: any) => this.mapRow(x));

        const totalItems = Number(raw?.totalItems ?? raw?.TotalItems ?? items.length);
        const currentPage = Number(raw?.page ?? raw?.Page ?? page);
        const currentPageSize = Number(raw?.pageSize ?? raw?.PageSize ?? pageSize);
        const totalPages = Number(
          raw?.totalPages ??
          raw?.TotalPages ??
          Math.ceil(totalItems / Math.max(currentPageSize, 1))
        );

        return {
          success: res?.success ?? res?.Success ?? true,
          message: res?.message ?? res?.Message ?? '',
          data: {
            items,
            page: currentPage,
            pageSize: currentPageSize,
            totalItems,
            totalPages
          },
          summary: raw?.summary ?? raw?.Summary ?? null
        } as ApiPagedResponse<JobOrderRowViewModel> & { summary?: any };
      })
    );
  }

  list(
    search: string = '',
    activeFilter: string = '',
    page: number = 1,
    pageSize: number = 20
  ): Observable<ApiPagedResponse<JobOrderRowViewModel>> {
    return this.getAll(search, activeFilter, page, pageSize);
  }

  getDetail(id: number): Observable<ApiResponseData<JobOrderDetailViewModel>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => {
        const d = res?.data ?? res?.Data ?? {};
        return {
          success: res?.success ?? res?.Success ?? true,
          message: res?.message ?? res?.Message ?? '',
          data: this.mapDetail(d)
        } as ApiResponseData<JobOrderDetailViewModel>;
      })
    );
  }

  // Phase M.1 - Job Order 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  create(model: JobOrderDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, this.toServerDto(model));
  }

  update(model: JobOrderDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, this.toServerDto(model));
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  post(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/post/${id}`, {});
  }

  reversePost(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reverse-post/${id}`, {});
  }

  syncComponents(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/sync-components/${id}`, {});
  }

  // ============================================================
  //  Sprint-3 bulk actions. Each returns a per-id success/failure
  //  report so the UI can show "5 posted, 2 failed (because …)".
  // ============================================================
  bulkPost(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-post`, { ids });
  }

  bulkDelete(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-delete`, { ids });
  }

  bulkChangePriority(ids: number[], priority: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-change-priority`, { ids, priority });
  }

  // Sprint-3 clone — backend deep-copies header + components and returns the
  // new id/code so the caller can immediately open the edit modal for fine-
  // tuning (priority, qty, due date, status).
  clone(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/clone/${id}`, {});
  }

  // Sprint-4 audit timeline — returns newest-first events for this JO
  // (Created / Updated / Posted / Reversed / Deleted / Cloned /
  // ComponentsSynced / PriorityChanged) with resolved user name.
  getAuditLog(id: number): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(`${this.baseUrl}/audit-log/${id}`);
  }

  // Sprint-4 workflow: mark a JO as Closed (terminal stage). Backend rejects
  // if data doesn't back the move (not yet fully Dispatched).
  markClosed(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/mark-closed/${id}`, {});
  }

  getCustomers(): Observable<any[]> {
    return this.http.get<any>(`${this.baseUrl}/customers`).pipe(
      map((res: any) => this.extractArray(res))
    );
  }

  getProducts(): Observable<any[]> {
    return this.http.get<any>('/emb-products/dropdowns/products').pipe(
      map((res: any) => this.extractArray(res))
    );
  }

  getDesigns(): Observable<any[]> {
    return this.http.get<any>('/emb-products/dropdowns/designs').pipe(
      map((res: any) => this.extractArray(res))
    );
  }

  getDesignsByProduct(productId: number): Observable<any[]> {
    return this.http.get<any>('/emb-products/dropdowns/designs-by-product', {
      params: new HttpParams().set('productId', String(productId))
    }).pipe(
      map((res: any) => this.extractArray(res))
    );
  }

  getBranches(): Observable<any[]> {
    return this.http.get<any>('/emb-products/dropdowns/branches').pipe(
      map((res: any) => this.extractArray(res))
    );
  }

  getDesignDetail(designId: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/design-detail/${designId}`).pipe(
      map((res: any) => res?.data ?? res?.Data ?? res)
    );
  }

  getNextCode(): Observable<ApiResponseData<any>> {
    return this.http.get<ApiResponseData<any>>(`${this.baseUrl}/next-code`);
  }

  getDdl(): Observable<ApiResponseList<any>> {
    return this.http.get<ApiResponseList<any>>(`${this.baseUrl}/ddl`);
  }

  getComponentsPreview(designId: number, orderQty: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/component-preview`, {
      params: new HttpParams()
        .set('designId', String(designId))
        .set('orderQty', String(orderQty))
    });
  }

  exportToExcel(id?: number, search?: string, activeFilter?: string): void {
    let params = new HttpParams();

    if (id && id > 0) params = params.set('id', String(id));
    if (search) params = params.set('search', search);
    if (activeFilter) params = params.set('activeFilter', activeFilter);

    const url = params.toString()
      ? `${this.baseUrl}/exporttoexcel?${params.toString()}`
      : `${this.baseUrl}/exporttoexcel`;

    window.open(url, '_blank');
  }

  exportToPdf(id?: number, search?: string, activeFilter?: string): void {
    let params = new HttpParams();

    if (id && id > 0) params = params.set('id', String(id));
    if (search) params = params.set('search', search);
    if (activeFilter) params = params.set('activeFilter', activeFilter);

    const url = params.toString()
      ? `${this.baseUrl}/exporttopdf?${params.toString()}`
      : `${this.baseUrl}/exporttopdf`;

    window.open(url, '_blank');
  }

  private mapRow(x: any): JobOrderRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      jobOrderCode: x?.jobOrderCode ?? x?.JobOrderCode ?? '',

      orderType: this.mapOrderType(x?.orderType ?? x?.OrderType),
      isCompanyStockOrder: Boolean(x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder ?? false),

      customerId: this.toNullableNumber(x?.customerId ?? x?.CustomerId),
      customerName: x?.customerName ?? x?.CustomerName ?? null,
      customerCode: x?.customerCode ?? x?.CustomerCode ?? null,

      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      designName: x?.designName ?? x?.DesignName ?? null,

      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      productName: x?.productName ?? x?.ProductName ?? null,
      productCode: x?.productCode ?? x?.ProductCode ?? null,

      branchId: this.toNullableNumber(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? null,

      orderQty: Number(x?.orderQty ?? x?.OrderQty ?? 0),
      remainingQty: Number(
        x?.remainingQty ??
        x?.RemainingQty ??
        x?.orderQty ??
        x?.OrderQty ??
        0
      ),

      // Sprint-1 progress fields (sum of posted DailyProduction.AcceptedQty
      // / OrderQty, capped at 100). Defaults to 0 so the bar renders empty
      // for JOs with no production yet rather than NaN.
      producedQty: Number(x?.producedQty ?? x?.ProducedQty ?? 0),
      completionPercent: Number(x?.completionPercent ?? x?.CompletionPercent ?? 0),

      // Sprint-2 quick-jump counts (number of linked WOs / Plans / Dispatches).
      woCount: Number(x?.woCount ?? x?.WoCount ?? 0),
      productionPlanCount: Number(x?.productionPlanCount ?? x?.ProductionPlanCount ?? 0),
      dispatchCount: Number(x?.dispatchCount ?? x?.DispatchCount ?? 0),

      orderDate: x?.orderDate ?? x?.OrderDate ?? null,
      dueDate: x?.dueDate ?? x?.DueDate ?? null,
      // ISO yyyy-MM-dd strings (added Sprint-2). The raw orderDate/dueDate
      // arrive as the legacy /Date(ms)/ which Angular can't parse — these
      // text fields are the canonical display source.
      orderDateText: x?.orderDateText ?? x?.OrderDateText ?? null,
      dueDateText:   x?.dueDateText   ?? x?.DueDateText   ?? null,

      priority: x?.priority ?? x?.Priority ?? null,
      status: x?.status ?? x?.Status ?? null,

      isPosted: Boolean(x?.isPosted ?? x?.IsPosted ?? false),
      postedBy: this.toNullableNumber(x?.postedBy ?? x?.PostedBy),
      postedDate: x?.postedDate ?? x?.PostedDate ?? null,
      postedDateText: x?.postedDateText ?? x?.PostedDateText ?? null,

      active: Boolean(x?.active ?? x?.Active ?? true),
      deleted: Boolean(x?.deleted ?? x?.Deleted ?? false),

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null,

      updatedBy: this.toNullableNumber(x?.updatedBy ?? x?.UpdatedBy),
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null,
      updatedDateText: x?.updatedDateText ?? x?.UpdatedDateText ?? null
    };
  }

  private mapDetail(d: any): JobOrderDetailViewModel {
    const rawItems = Array.isArray(d?.items)
      ? d.items
      : Array.isArray(d?.Items)
        ? d.Items
        : [];

    return {
      id: Number(d?.id ?? d?.Id ?? 0),
      jobOrderCode: d?.jobOrderCode ?? d?.JobOrderCode ?? '',

      orderType: this.mapOrderType(d?.orderType ?? d?.OrderType),
      isCompanyStockOrder: Boolean(d?.isCompanyStockOrder ?? d?.IsCompanyStockOrder ?? false),

      customerId: this.toNullableNumber(d?.customerId ?? d?.CustomerId),
      customerName: d?.customerName ?? d?.CustomerName ?? null,
      customerCode: d?.customerCode ?? d?.CustomerCode ?? null,

      designId: this.toNullableNumber(d?.designId ?? d?.DesignId),
      designName: d?.designName ?? d?.DesignName ?? null,

      productId: this.toNullableNumber(d?.productId ?? d?.ProductId),
      productName: d?.productName ?? d?.ProductName ?? null,
      productCode: d?.productCode ?? d?.ProductCode ?? null,

      branchId: this.toNullableNumber(d?.branchId ?? d?.BranchId),
      branchName: d?.branchName ?? d?.BranchName ?? null,

      orderQty: Number(d?.orderQty ?? d?.OrderQty ?? 0),
      remainingQty: Number(
        d?.remainingQty ??
        d?.RemainingQty ??
        d?.orderQty ??
        d?.OrderQty ??
        0
      ),

      orderDate: d?.orderDate ?? d?.OrderDate ?? null,
      dueDate: d?.dueDate ?? d?.DueDate ?? null,

      priority: d?.priority ?? d?.Priority ?? null,
      status: d?.status ?? d?.Status ?? null,

      isPosted: Boolean(d?.isPosted ?? d?.IsPosted ?? false),
      postedBy: this.toNullableNumber(d?.postedBy ?? d?.PostedBy),
      postedDate: d?.postedDate ?? d?.PostedDate ?? null,
      postedDateText: d?.postedDateText ?? d?.PostedDateText ?? null,

      active: Boolean(d?.active ?? d?.Active ?? true),
      deleted: Boolean(d?.deleted ?? d?.Deleted ?? false),

      createdBy: this.toNullableNumber(d?.createdBy ?? d?.CreatedBy),
      createdByEmployeeId: this.toNullableNumber(d?.createdByEmployeeId ?? d?.CreatedByEmployeeId),
      createdDate: d?.createdDate ?? d?.CreatedDate ?? null,
      createdDateText: d?.createdDateText ?? d?.CreatedDateText ?? null,

      updatedBy: this.toNullableNumber(d?.updatedBy ?? d?.UpdatedBy),
      updatedByEmployeeId: this.toNullableNumber(d?.updatedByEmployeeId ?? d?.UpdatedByEmployeeId),
      updatedDate: d?.updatedDate ?? d?.UpdatedDate ?? null,
      updatedDateText: d?.updatedDateText ?? d?.UpdatedDateText ?? null,

      items: rawItems.map((x: any) => this.mapDetailItem(x))
    };
  }

  private mapDetailItem(x: any): JobOrderItemViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      componentId: this.toNullableNumber(x?.componentId ?? x?.ComponentId),
      componentCode: x?.componentCode ?? x?.ComponentCode ?? null,
      componentName: x?.componentName ?? x?.ComponentName ?? null,
      reqRepeats: this.toNullableNumber(x?.reqRepeats ?? x?.ReqRepeats),
      reqHead: this.toNullableNumber(x?.reqHead ?? x?.ReqHead),
      panni: this.toNullableNumber(x?.panni ?? x?.Panni),
      solving: this.toNullableNumber(x?.solving ?? x?.Solving)
    };
  }

  private toServerDto(model: JobOrderDto): any {
    return {
      Id: model.id ?? 0,
      JobOrderCode: model.jobOrderCode ?? '',

      OrderType: model.orderType,
      IsCompanyStockOrder: model.isCompanyStockOrder,

      CustomerId: model.isCompanyStockOrder ? null : model.customerId,

      ProductId: model.productId,
      DesignId: model.designId,
      BranchId: model.branchId,

      OrderQty: model.orderQty,

      OrderDate: model.orderDate ?? null,
      DueDate: model.dueDate ?? null,

      Priority: model.priority ?? 'Normal',
      Status: model.status ?? 'Draft',
      Active: model.active
    };
  }

  private extractArray(res: any): any[] {
    const data = res?.data ?? res?.Data ?? res;
    return Array.isArray(data) ? data : [];
  }

  private mapOrderType(value: any): JobOrderType {
    const v = String(value ?? '').trim();
    return v === 'CompanyStock' ? 'CompanyStock' : 'Customer';
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }
}
