import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { ApiResponse, ApiPagedResponse, ApiResponseData } from '../shared/models/api-response';
import {StoreRowViewModel,StoreDetailViewModel,StoreDashboardViewModel,StoreEntryDto,DropdownItem,
  WorkOrderMaterialRequirementViewModel,MaterialIssueDto} from '../shared/models/gen-store.dto';

@Injectable({ providedIn: 'root' })
export class GenStoreService {
  private baseUrl = '/emb-products/gen-store';
  private dropdownUrl = '/emb-products/dropdowns';

  constructor(private http: HttpClient) { }

  list(params: {
    search?: string;
    branchId?: number | null;
    workOrderId?: number | null;
    page?: number;
    pageSize?: number;
    activeFilter?: string;
    fromDate?: string | null;
    toDate?: string | null;
    referenceType?: string | null;
    stockStatus?: string | null;
  }): Observable<any> {

    let httpParams = new HttpParams()
      .set('search', params.search ?? '')
      .set('page', String(params.page ?? 1))
      .set('pageSize', String(params.pageSize ?? 20))
      .set('activeFilter', params.activeFilter ?? '');

    if (params.branchId != null && Number(params.branchId) > 0) {
      httpParams = httpParams.set('branchId', String(params.branchId));
    }

    if (params.workOrderId) {
      httpParams = httpParams.set('workOrderId', String(params.workOrderId));
    }

    if (params.fromDate)      httpParams = httpParams.set('fromDate', params.fromDate);
    if (params.toDate)        httpParams = httpParams.set('toDate', params.toDate);
    if (params.referenceType) httpParams = httpParams.set('referenceType', params.referenceType);
    if ((params as any).stockStatus) httpParams = httpParams.set('stockStatus', (params as any).stockStatus);

    return this.http.get<any>(`${this.baseUrl}/list`, { params: httpParams });
  }

  // Phase G.1 - update reorder/max thresholds for one item
  setLevels(itemId: number, payload: {
    reorderLevel?: number | null;
    maxLevel?: number | null;
    reorderQty?: number | null;
  }): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/set-levels/${itemId}`, payload).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  itemHistory(itemId: number, branchId?: number | null): Observable<any> {
    let p = new HttpParams();
    if (branchId != null && Number(branchId) > 0) {
      p = p.set('branchId', String(branchId));
    }
    return this.http.get<any>(`${this.baseUrl}/item-history/${itemId}`, { params: p });
  }

  detail(id: number): Observable<ApiResponseData<StoreDetailViewModel>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: this.mapDetail(res?.data ?? res?.Data ?? {})
      }))
    );
  }

  dashboardSummary(
    branchId?: number | null,
    search: string = ''
  ): Observable<ApiResponseData<StoreDashboardViewModel>> {
    const httpParams = new HttpParams({
      fromObject: {
        branchId: branchId != null ? String(branchId) : '',
        search: search ?? ''
      }
    });

    return this.http.get<any>(`${this.baseUrl}/dashboard-summary`, { params: httpParams }).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: this.mapDashboard(res?.data ?? res?.Data ?? {})
      }))
    );
  }

  openingIn(dto: StoreEntryDto): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/opening-in`, dto).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  issueOut(dto: StoreEntryDto): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/issue-out`, dto).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  // Phase G.2 - Branch transfer (paired IN/OUT)
  transfer(payload: {
    itemId: number;
    fromBranchId: number;
    toBranchId: number;
    qty: number;
    transactionDate?: string | null;
    rate?: number | null;
    remarks?: string | null;
  }): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/transfer`, payload).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  // Phase G.3 - Stock adjustment (single IN or OUT with reason)
  adjust(payload: {
    itemId: number;
    branchId: number;
    sign: 'IN' | 'OUT';
    qty: number;
    reason: string;
    transactionDate?: string | null;
    rate?: number | null;
    remarks?: string | null;
  }): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/adjustment`, payload).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  getRawItems(): Observable<DropdownItem[]> {
    return this.http.get<any>(`${this.dropdownUrl}/raw-items`).pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getBranches(): Observable<DropdownItem[]> {
    return this.http.get<any>(`${this.dropdownUrl}/branches`).pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getWorkOrders(branchId?: number | null): Observable<DropdownItem[]> {
    let params = new HttpParams();

    if (branchId != null && branchId > 0) {
      params = params.set('branchId', String(branchId));
    }

    return this.http
      .get<any>(`${this.baseUrl}/pending-workorders`, { params })
      .pipe(
        map((res: any) => this.mapDropdownArray(res))
      );
  }

  getWorkOrderMaterials(
    workOrderId: number,
    branchId?: number | null
  ): Observable<ApiResponseData<WorkOrderMaterialRequirementViewModel[]>> {
    let params = new HttpParams();

    if (branchId) {
      params = params.set('branchId', String(branchId));
    }

    return this.http.get<any>(
      `${this.baseUrl}/work-order-materials/${workOrderId}`,
      { params }
    ).pipe(
      map((res: any) => {
        const raw = res?.data ?? res?.Data ?? [];

        return {
          success: res?.success ?? res?.Success ?? false,
          message: res?.message ?? res?.Message ?? '',
          data: Array.isArray(raw)
            ? raw.map((x: any) => this.mapWorkOrderMaterial(x))
            : []
        };
      })
    );
  }

  issueAgainstWorkOrder(payload: MaterialIssueDto): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/issue-work-order`, payload).pipe(
      map((res: any) => this.mapApiResponse(res))
    );
  }

  getIssuedMaterialsByWorkOrder(workOrderId: number) {
    return this.http.get<any>(
      `${this.baseUrl}/issued-materials-by-workorder/${workOrderId}`
    );
  }

  getBalance(
    branchId?: number | null,
    search: string = '',
    type: string = ''
  ): Observable<ApiResponseData<any[]>> {
    let params = new HttpParams()
      .set('search', search ?? '')
      .set('type', type ?? '');

    if (branchId) {
      params = params.set('branchId', String(branchId));
    }

    return this.http.get<any>(`${this.baseUrl}/balance`, { params }).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data ?? res?.Data ?? []
      }))
    );
  }

  getAllWorkOrders(): Observable<any[]> {
    return this.http.get<any[]>(`/emb-products/dropdowns/workorders-all`);
  }

  private mapApiResponse(res: any): ApiResponse {
    return {
      success: res?.success ?? res?.Success ?? false,
      message: res?.message ?? res?.Message ?? '',
      data: res?.data ?? res?.Data ?? null
    };
  }

  private mapRow(x: any): StoreRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      itemId: Number(x?.itemId ?? x?.ItemId ?? 0),
      itemName: x?.itemName ?? x?.ItemName ?? '',

      branchId: this.toNullableNumber(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? '',

      warehouseId: this.toNullableNumber(x?.warehouseId ?? x?.WarehouseId),
      warehouseName: x?.warehouseName ?? x?.WarehouseName ?? '',

      transactionDate: x?.transactionDate ?? x?.TransactionDate ?? null,

      transactionType: x?.transactionType ?? x?.TransactionType ?? '',
      referenceType: x?.referenceType ?? x?.ReferenceType ?? '',
      referenceId: this.toNullableNumber(x?.referenceId ?? x?.ReferenceId),
      referenceNo: x?.referenceNo ?? x?.ReferenceNo ?? '',

      openingQty: Number(x?.openingQty ?? x?.OpeningQty ?? 0),
      inQty: Number(x?.inQty ?? x?.InQty ?? 0),
      outQty: Number(x?.outQty ?? x?.OutQty ?? 0),
      currentQty: Number(x?.currentQty ?? x?.CurrentQty ?? 0),

      unit: x?.unit ?? x?.Unit ?? '',
      rate: Number(x?.rate ?? x?.Rate ?? 0),
      amount: Number(x?.amount ?? x?.Amount ?? 0),

      remarks: x?.remarks ?? x?.Remarks ?? '',

      active: (x?.active ?? x?.Active) !== false,
      deleted: (x?.deleted ?? x?.Deleted) === true,

      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null
    };
  }

  private mapDetail(x: any): StoreDetailViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      itemId: Number(x?.itemId ?? x?.ItemId ?? 0),
      itemName: x?.itemName ?? x?.ItemName ?? '',

      branchId: this.toNullableNumber(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? '',

      warehouseId: this.toNullableNumber(x?.warehouseId ?? x?.WarehouseId),
      warehouseName: x?.warehouseName ?? x?.WarehouseName ?? '',

      transactionDate: x?.transactionDate ?? x?.TransactionDate ?? null,

      transactionType: x?.transactionType ?? x?.TransactionType ?? '',
      referenceType: x?.referenceType ?? x?.ReferenceType ?? '',
      referenceId: this.toNullableNumber(x?.referenceId ?? x?.ReferenceId),
      referenceNo: x?.referenceNo ?? x?.ReferenceNo ?? '',

      openingQty: Number(x?.openingQty ?? x?.OpeningQty ?? 0),
      inQty: Number(x?.inQty ?? x?.InQty ?? 0),
      outQty: Number(x?.outQty ?? x?.OutQty ?? 0),
      currentQty: Number(x?.currentQty ?? x?.CurrentQty ?? 0),

      unit: x?.unit ?? x?.Unit ?? '',
      rate: Number(x?.rate ?? x?.Rate ?? 0),
      amount: Number(x?.amount ?? x?.Amount ?? 0),

      remarks: x?.remarks ?? x?.Remarks ?? '',

      active: (x?.active ?? x?.Active) !== false,
      deleted: (x?.deleted ?? x?.Deleted) === true,

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,

      updatedBy: this.toNullableNumber(x?.updatedBy ?? x?.UpdatedBy),
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null
    };
  }

  private mapDashboard(x: any): StoreDashboardViewModel {
    return {
      openingQty: Number(
        x?.openingQty ??
        x?.OpeningQty ??
        x?.totalOpeningQty ??
        x?.TotalOpeningQty ??
        0
      ),

      totalIn: Number(
        x?.totalIn ??
        x?.TotalIn ??
        x?.totalInQty ??
        x?.TotalInQty ??
        0
      ),

      totalOut: Number(
        x?.totalOut ??
        x?.TotalOut ??
        x?.totalOutQty ??
        x?.TotalOutQty ??
        0
      ),

      currentBalance: Number(
        x?.currentBalance ??
        x?.CurrentBalance ??
        x?.totalCurrentQty ??
        x?.TotalCurrentQty ??
        0
      ),

      distinctItems: Number(
        x?.distinctItems ??
        x?.DistinctItems ??
        0
      ),

      branches: Number(
        x?.branches ??
        x?.Branches ??
        x?.distinctBranches ??
        x?.DistinctBranches ??
        0
      ),

      totalStockValue: Number(
        x?.totalStockValue ??
        x?.TotalStockValue ??
        0
      )
    };
  }

  private mapWorkOrderMaterial(x: any): WorkOrderMaterialRequirementViewModel {
    return {
      itemId: Number(x?.itemId ?? x?.ItemId ?? 0),
      itemType: x?.itemType ?? x?.ItemType ?? '',
      itemName: x?.itemName ?? x?.ItemName ?? '',

      requiredQty: Number(x?.requiredQty ?? x?.RequiredQty ?? 0),
      availableQty: Number(x?.availableQty ?? x?.AvailableQty ?? 0),
      alreadyIssuedQty: Number(x?.alreadyIssuedQty ?? x?.AlreadyIssuedQty ?? 0),
      pendingQty: Number(x?.pendingQty ?? x?.PendingQty ?? 0),

      issueQty: Number(x?.issueQty ?? x?.IssueQty ?? x?.pendingQty ?? x?.PendingQty ?? 0),

      department: x?.department ?? x?.Department ?? '',
      unit: x?.unit ?? x?.Unit ?? 'PCS',

      rate: Number(x?.rate ?? x?.Rate ?? 0),
      remarks: x?.remarks ?? x?.Remarks ?? ''
    };
  }

  private mapDropdownArray(res: any): DropdownItem[] {
    const raw = res?.data ?? res?.Data ?? res ?? [];
    const arr = Array.isArray(raw) ? raw : [];

    return arr.map((x: any) => ({
      id: Number(x?.id ?? x?.Id ?? 0),
      text:
        x?.text ??
        x?.Text ??
        x?.name ??
        x?.Name ??
        x?.label ??
        x?.Label ??
        x?.itemName ??
        x?.ItemName ??
        ''
    }));
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }

  // Reverse a Material Issue by its IssueCode (ReferenceNo on the OUT row).
  // The backend writes a compensating IN row and soft-deletes the OUT row.
  reverseMaterialIssue(issueCode: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `/emb-products/issue-materials/reverse/${encodeURIComponent(issueCode)}`,
      {}
    );
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  //  Returns one entry per state-changing event on a Gen_Store row.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(storeId: number): Observable<ApiResponseData<StoreAuditEntry[]>> {
    return this.http.get<ApiResponseData<StoreAuditEntry[]>>(
      `${this.baseUrl}/audit-log/${storeId}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk reverse. Each id should be a MATERIAL_ISSUE
  //  OUT row; the backend resolves its IssueCode and delegates to
  //  the per-issue Reverse endpoint. Per-id failures are reported
  //  in the standard envelope so the toolbar can render
  //  "n reversed, m failed (reason: …)".
  // ───────────────────────────────────────────────────────────────
  bulkReverse(ids: number[]): Observable<StoreBulkResponse> {
    return this.http.post<StoreBulkResponse>(
      `${this.baseUrl}/bulk-reverse`, { ids });
  }
}

// ─────────────────────────────────────────────────────────────────
//  Sprint-2 — one entry in the audit-log timeline.
// ─────────────────────────────────────────────────────────────────
export interface StoreAuditEntry {
  id: number;
  storeId: number;
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
//  Sprint-3 — bulk-action response envelope shared with the rest
//  of the modernized procurement chain (Purchase, PO, PR, etc.).
// ─────────────────────────────────────────────────────────────────
export interface StoreBulkResponse {
  success: boolean;
  message: string;
  data: {
    successCount: number;
    failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  };
}
