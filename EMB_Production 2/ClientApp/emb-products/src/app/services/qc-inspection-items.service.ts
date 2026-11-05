import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';

import {
  ApiResponse,
  ApiResponseData,
  ApiPagedResponse
} from '../shared/models/api-response';

import {
  QCInspectionItemRowViewModel,
  QCInspectionItemDetailViewModel,
  QCInspectionItemForm
} from '../shared/models/qc-inspection-item.dto';

export interface QCInspectionItemSavePayload {
  id: number;
  qcInspectionId: number | null;
  workOrderItemId: number | null;
  productionId: number | null;
  compId: number | null;
  qty: number;
  passedQty: number;
  failedQty: number;
  reworkQty: number;
  result: string | null;
  remarks: string | null;
}

@Injectable({ providedIn: 'root' })
export class QCInspectionItemsService {
  private readonly baseUrl = '/emb-products/qc-inspection-items';

  constructor(private http: HttpClient) { }

  // ======================================================
  // LIST
  // ======================================================
  list(params?: {
    search?: string;
    activeFilter?: string;
    showDeleted?: boolean;
    page?: number;
    pageSize?: number;
    qcInspectionId?: number;
  }): Observable<ApiPagedResponse<QCInspectionItemRowViewModel>> {
    const httpParams = new HttpParams({
      fromObject: {
        search: params?.search ?? '',
        activeFilter: params?.activeFilter ?? '',
        showDeleted: String(params?.showDeleted ?? false),
        page: String(params?.page ?? 1),
        pageSize: String(params?.pageSize ?? 10),
        qcInspectionId: params?.qcInspectionId != null ? String(params.qcInspectionId) : ''
      }
    });

    return this.http.get<any>(`${this.baseUrl}/list`, { params: httpParams }).pipe(
      map((res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';

        const raw = res?.data ?? res?.Data ?? {};
        const rawItems = Array.isArray(raw?.items)
          ? raw.items
          : Array.isArray(raw?.Items)
            ? raw.Items
            : Array.isArray(raw)
              ? raw
              : [];

        const items: QCInspectionItemRowViewModel[] = rawItems.map((x: any) => this.mapRow(x));

        const totalItems = Number(raw?.totalItems ?? raw?.TotalItems ?? items.length);
        const page = Number(raw?.page ?? raw?.Page ?? params?.page ?? 1);
        const pageSize = Number(raw?.pageSize ?? raw?.PageSize ?? params?.pageSize ?? 10);
        const totalPages = Number(
          raw?.totalPages ??
          raw?.TotalPages ??
          Math.ceil(totalItems / Math.max(pageSize, 1))
        );

        return {
          success,
          message,
          data: {
            items,
            page,
            pageSize,
            totalItems,
            totalPages
          }
        } as ApiPagedResponse<QCInspectionItemRowViewModel>;
      })
    );
  }

  // ======================================================
  // DETAIL
  // ======================================================
  detail(id: number): Observable<ApiResponseData<QCInspectionItemDetailViewModel>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';
        const raw = res?.data ?? res?.Data ?? null;

        return {
          success,
          message,
          data: raw ? this.mapDetail(raw) : null
        } as ApiResponseData<QCInspectionItemDetailViewModel>;
      })
    );
  }

  // ======================================================
  // INLINE ITEM SAVE
  // Used by main QC screen Complete Items mode
  // ======================================================
  saveItem(model: QCInspectionItemSavePayload): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/item-save`, model);
  }

  // ======================================================
  // CREATE
  // ======================================================
  create(model: QCInspectionItemForm): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, model);
  }

  // ======================================================
  // UPDATE
  // ======================================================
  update(id: number, model: QCInspectionItemForm): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update/${id}`, model);
  }

  // ======================================================
  // DELETE
  // ======================================================
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  // ======================================================
  // TOGGLE ACTIVE
  // ======================================================
  toggleActive(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/toggle-active/${id}`, {});
  }

  // ======================================================
  // SMART AUTO-FILL BY QC
  // ======================================================
  getAutoFillByQc(qcInspectionId: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/autofill/by-qc/${qcInspectionId}`);
  }

  // ======================================================
  // QC INSPECTION DROPDOWN
  // ======================================================
  getQcInspectionDropdown(): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/dropdown/qc-inspections`);
  }

  // ======================================================
  // MAPPERS
  // ======================================================
  private mapRow(x: any): QCInspectionItemRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      qcInspectionId: this.toNullableNumber(x?.qcInspectionId ?? x?.QCInspectionId),
      qcInspectionNo: x?.qcInspectionNo ?? x?.QCInspectionNo ?? null,

      workOrderItemId: this.toNullableNumber(x?.workOrderItemId ?? x?.WorkOrderItemId),
      workOrderCode: x?.workOrderCode ?? x?.WorkOrderCode ?? null,

      productionId: this.toNullableNumber(x?.productionId ?? x?.ProductionId),
      productionNo: x?.productionNo ?? x?.ProductionNo ?? null,

      compId: this.toNullableNumber(x?.compId ?? x?.CompId),
      componentName: x?.componentName ?? x?.ComponentName ?? null,

      qty: Number(x?.qty ?? x?.Qty ?? 0),
      passedQty: Number(x?.passedQty ?? x?.PassedQty ?? 0),
      failedQty: Number(x?.failedQty ?? x?.FailedQty ?? 0),
      reworkQty: Number(x?.reworkQty ?? x?.ReworkQty ?? 0),

      result: x?.result ?? x?.Result ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      active: Boolean(x?.active ?? x?.Active ?? false),
      deleted: Boolean(x?.deleted ?? x?.Deleted ?? false),

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      createdByEmployeeId: x?.createdByEmployeeId ?? x?.CreatedByEmployeeId ?? null,
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null
    };
  }

  private mapDetail(x: any): QCInspectionItemDetailViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      qcInspectionId: this.toNullableNumber(x?.qcInspectionId ?? x?.QCInspectionId),
      qcInspectionNo: x?.qcInspectionNo ?? x?.QCInspectionNo ?? null,

      workOrderItemId: this.toNullableNumber(x?.workOrderItemId ?? x?.WorkOrderItemId),
      workOrderCode: x?.workOrderCode ?? x?.WorkOrderCode ?? null,

      productionId: this.toNullableNumber(x?.productionId ?? x?.ProductionId),
      productionNo: x?.productionNo ?? x?.ProductionNo ?? null,

      compId: this.toNullableNumber(x?.compId ?? x?.CompId),
      componentName: x?.componentName ?? x?.ComponentName ?? null,

      qty: Number(x?.qty ?? x?.Qty ?? 0),
      passedQty: Number(x?.passedQty ?? x?.PassedQty ?? 0),
      failedQty: Number(x?.failedQty ?? x?.FailedQty ?? 0),
      reworkQty: Number(x?.reworkQty ?? x?.ReworkQty ?? 0),

      result: x?.result ?? x?.Result ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      active: Boolean(x?.active ?? x?.Active ?? false),
      deleted: Boolean(x?.deleted ?? x?.Deleted ?? false),

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      createdByEmployeeId: x?.createdByEmployeeId ?? x?.CreatedByEmployeeId ?? null,
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null
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
