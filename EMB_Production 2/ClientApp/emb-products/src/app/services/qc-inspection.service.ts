import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { ApiResponse, ApiResponseData } from '../shared/models/api-response';
import {QcInspectionRowDto,QcInspectionDetailDto,QcInspectionCreateDto,QcInspectionUpdateDto,
  QcInspectionReverseDto,QcInspectionCancelPendingDto,QcInspectionItemRowDto,QcInspectionContinueDto,
  QcInspectionListDataDto} from '../shared/models/qc-inspection.dto';

@Injectable({ providedIn: 'root' })
export class QcInspectionService {
  private readonly baseUrl = '/emb-products/qc-inspection';

  constructor(private http: HttpClient) { }

  list(params: any): Observable<ApiResponseData<QcInspectionListDataDto> & { rawSummary?: any }> {
    let httpParams = new HttpParams({
      fromObject: {
        search: params?.search ?? '',
        status: params?.status ?? '',
        showDeleted: String(params?.showDeleted ?? false),
        page: String(params?.page ?? 1),
        pageSize: String(params?.pageSize ?? 10)
      }
    });

    // Phase N.0 - additional filters
    if (params?.fromDate)       httpParams = httpParams.set('fromDate', params.fromDate);
    if (params?.toDate)         httpParams = httpParams.set('toDate', params.toDate);
    if (params?.workOrderId)    httpParams = httpParams.set('workOrderId', String(params.workOrderId));
    if (params?.productId)      httpParams = httpParams.set('productId', String(params.productId));
    if (params?.branchId)       httpParams = httpParams.set('branchId', String(params.branchId));
    if (params?.posted)         httpParams = httpParams.set('posted', params.posted);
    if (params?.inspectionType) httpParams = httpParams.set('inspectionType', params.inspectionType);
    if (params?.sourceType)     httpParams = httpParams.set('sourceType', params.sourceType);

    return this.http.get<any>(`${this.baseUrl}/list`, { params: httpParams }).pipe(
      map((res: any) => {
        const raw = res?.data ?? res?.Data ?? {};
        const rawItems = Array.isArray(raw?.items)
          ? raw.items
          : Array.isArray(raw?.Items)
            ? raw.Items
            : [];

        const summaryRaw = raw?.summary ?? raw?.Summary ?? {};

        return {
          success: res?.success ?? res?.Success ?? false,
          message: res?.message ?? res?.Message ?? '',
          data: {
            items: rawItems.map((x: any) => this.mapRow(x)),
            page: Number(raw?.page ?? raw?.Page ?? 1),
            pageSize: Number(raw?.pageSize ?? raw?.PageSize ?? 10),
            totalItems: Number(raw?.totalItems ?? raw?.TotalItems ?? 0),
            totalPages: Number(raw?.totalPages ?? raw?.TotalPages ?? 1),
            summary: {
              batchQty: Number(summaryRaw?.batchQty ?? summaryRaw?.BatchQty ?? 0),
              inspectedQty: Number(summaryRaw?.inspectedQty ?? summaryRaw?.InspectedQty ?? 0),
              passedQty: Number(summaryRaw?.passedQty ?? summaryRaw?.PassedQty ?? 0),
              failedQty: Number(summaryRaw?.failedQty ?? summaryRaw?.FailedQty ?? 0),
              reworkQty: Number(summaryRaw?.reworkQty ?? summaryRaw?.ReworkQty ?? 0)
            }
          },
          rawSummary: summaryRaw
        };
      })
    );
  }

  // Phase N.1 - QC 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  detail(id: number): Observable<ApiResponseData<QcInspectionDetailDto>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data || res?.Data ? this.mapDetail(res?.data ?? res?.Data) : null
      }))
    );
  }

  create(model: QcInspectionCreateDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, model);
  }

  update(model: QcInspectionUpdateDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, model);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  post(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/post/${id}`, {});
  }

  continue(id: number, model: QcInspectionContinueDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/continue/${id}`, model);
  }

  reverse(id: number, model: QcInspectionReverseDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reverse/${id}`, {
      reason: model?.reason ?? ''
    });
  }

  cancelPending(id: number, model?: QcInspectionCancelPendingDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/cancel-pending/${id}`, {
      reason: model?.reason ?? ''
    });
  }

  reinspect(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reinspect/${id}`, {});
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-1 — Process QC quick action. Empty body = "accept the
  //  backend's auto-filled defaults" (Inspected = Batch, Pass = all,
  //  Fail = Rework = 0). Pass overrides to apply the buyer's preview
  //  edits in the same round-trip. Set andPost=true to chain Process
  //  and Post into one call.
  // ───────────────────────────────────────────────────────────────
  processQc(id: number, overrides?: any): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/process/${id}`, overrides ?? {});
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<ApiResponse> {
    return this.http.get<ApiResponse>(`${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk actions. Each returns the per-id failure
  //  envelope so the toolbar can render "n succeeded, m failed".
  // ───────────────────────────────────────────────────────────────
  bulkProcess(ids: number[], andPost: boolean): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-process`, { ids, andPost });
  }
  bulkPost(ids: number[]): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-post`, { ids });
  }
  bulkReverse(ids: number[], reason: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-reverse`, { ids, reason });
  }
  bulkCancel(ids: number[], reason: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/bulk-cancel`, { ids, reason });
  }

  private mapRow(x: any): QcInspectionRowDto {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      qcNo: x?.qcNo ?? x?.QCNo ?? '',
      workOrderId: this.toNullableNumber(x?.workOrderId ?? x?.WorkOrderId),
      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      workOrderItemId: this.toNullableNumber(x?.workOrderItemId ?? x?.WorkOrderItemId),

      workOrderLabel: x?.workOrderLabel ?? x?.WorkOrderLabel ?? '',
      productName: x?.productName ?? x?.ProductName ?? '',
      designName: x?.designName ?? x?.DesignName ?? '',

      batchQty: Number(x?.batchQty ?? x?.BatchQty ?? 0),
      inspectedQty: Number(x?.inspectedQty ?? x?.InspectedQty ?? 0),
      passedQty: Number(x?.passedQty ?? x?.PassedQty ?? 0),
      failedQty: Number(x?.failedQty ?? x?.FailedQty ?? 0),
      reworkQty: Number(x?.reworkQty ?? x?.ReworkQty ?? 0),

      status: x?.status ?? x?.Status ?? '',
      sourceType: x?.sourceType ?? x?.SourceType ?? '',

      inspectionDate: x?.inspectionDate ?? x?.InspectionDate ?? null,
      inspectionType: x?.inspectionType ?? x?.InspectionType ?? null,

      isPosted: Boolean(x?.isPosted ?? x?.IsPosted ?? false),
      isReversed: Boolean(x?.isReversed ?? x?.IsReversed ?? false),

      itemsPending: Number(x?.itemsPending ?? x?.ItemsPending ?? 0),
      itemsCompleted: Number(x?.itemsCompleted ?? x?.ItemsCompleted ?? 0),
      itemsStatusText: x?.itemsStatusText ?? x?.ItemsStatusText ?? null,
      inspectionProgressText: x?.inspectionProgressText ?? x?.InspectionProgressText ?? null,

      canReinspectRework: Boolean(x?.canReinspectRework ?? x?.CanReinspectRework ?? false),
      hasReworkReference: Boolean(x?.hasReworkReference ?? x?.HasReworkReference ?? false),

      // Sprint-1: Process QC quick-action gate + recent badge.
      canProcess: (x?.canProcess ?? x?.CanProcess ?? false) === true,
      isRecent:   (x?.isRecent   ?? x?.IsRecent   ?? false) === true,

      canEdit: Boolean(x?.canEdit ?? x?.CanEdit ?? false),
      canDelete: Boolean(x?.canDelete ?? x?.CanDelete ?? false),
      canPost: Boolean(x?.canPost ?? x?.CanPost ?? false),
      canReverse: Boolean(x?.canReverse ?? x?.CanReverse ?? false),

      isExpanded: Boolean(x?.isExpanded ?? false),
      isEditingItems: Boolean(x?.isEditingItems ?? false),
      itemsLoaded: Boolean(x?.itemsLoaded ?? false),

      items: []
    };
  }

  private mapDetail(x: any): QcInspectionDetailDto {
    const rawItems = Array.isArray(x?.items)
      ? x.items
      : Array.isArray(x?.Items)
        ? x.Items
        : [];

    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      qcNo: x?.qcNo ?? x?.QCNo ?? null,

      workOrderId: this.toNullableNumber(x?.workOrderId ?? x?.WorkOrderId),
      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      workOrderItemId: this.toNullableNumber(x?.workOrderItemId ?? x?.WorkOrderItemId),
      productionId: this.toNullableNumber(x?.productionId ?? x?.ProductionId),

      workOrderLabel: x?.workOrderLabel ?? x?.WorkOrderLabel ?? null,
      designName: x?.designName ?? x?.DesignName ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      batchQty: Number(x?.batchQty ?? x?.BatchQty ?? 0),
      inspectedQty: Number(x?.inspectedQty ?? x?.InspectedQty ?? 0),
      passedQty: Number(x?.passedQty ?? x?.PassedQty ?? 0),
      failedQty: Number(x?.failedQty ?? x?.FailedQty ?? 0),
      reworkQty: Number(x?.reworkQty ?? x?.ReworkQty ?? 0),

      issuesFound: x?.issuesFound ?? x?.IssuesFound ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,
      status: x?.status ?? x?.Status ?? null,

      inspectorId: this.toNullableNumber(x?.inspectorId ?? x?.InspectorId),
      inspectorName: x?.inspectorName ?? x?.InspectorName ?? null,

      inspectionType: x?.inspectionType ?? x?.InspectionType ?? null,
      sourceType: x?.sourceType ?? x?.SourceType ?? null,

      isPosted: Boolean(x?.isPosted ?? x?.IsPosted ?? false),
      isReversed: Boolean(x?.isReversed ?? x?.IsReversed ?? false),
      postedDate: x?.postedDate ?? x?.PostedDate ?? null,

      inspectionDate: x?.inspectionDate ?? x?.InspectionDate ?? null,
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null,

      itemsPending: Number(x?.itemsPending ?? x?.ItemsPending ?? 0),
      itemsCompleted: Number(x?.itemsCompleted ?? x?.ItemsCompleted ?? 0),
      itemsStatusText: x?.itemsStatusText ?? x?.ItemsStatusText ?? null,
      inspectionProgressText: x?.inspectionProgressText ?? x?.InspectionProgressText ?? null,

      canEdit: Boolean(x?.canEdit ?? x?.CanEdit ?? false),
      canDelete: Boolean(x?.canDelete ?? x?.CanDelete ?? false),
      canPost: Boolean(x?.canPost ?? x?.CanPost ?? false),
      canReverse: Boolean(x?.canReverse ?? x?.CanReverse ?? false),
      canCompleteItems: Boolean(x?.canCompleteItems ?? x?.CanCompleteItems ?? false),
      canContinueQc: Boolean(x?.canContinueQc ?? x?.CanContinueQc ?? false),
      canFinalize: Boolean(x?.canFinalize ?? x?.CanFinalize ?? false),
      canReinspect: Boolean(x?.canReinspect ?? x?.CanReinspect ?? false),

      items: rawItems.map((i: any) => this.mapItemRow(i))
    };
  }

  private mapItemRow(x: any): QcInspectionItemRowDto {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      qcInspectionId: Number(x?.qcInspectionId ?? x?.QCInspectionId ?? 0),
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
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null,

      isNew: Boolean(x?.isNew ?? x?.IsNew ?? false)
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
