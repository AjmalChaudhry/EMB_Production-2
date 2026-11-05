import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {DailyProductionRowViewModel,DailyProductionInputDto,DailyProductionDetailViewModel,
WorkOrderItemDropdownVm,DropdownItem,ReworkTaskVm} from '../shared/models/daily-production.dto';

export interface ApiResponse<T = any> {
  success: boolean;
  message: string;
  data: T;
}

@Injectable({ providedIn: 'root' })
export class DailyProductionService {
  private baseUrl = '/emb-products/daily-production';

  constructor(private http: HttpClient) { }

  getList(opts: {
    fromDate?: string | null;
    toDate?: string | null;
    workOrderIdFilter?: number | null;
    machineIdFilter?: number | null;
    operatorIdFilter?: number | null;
    shift?: string | null;
    posted?: string | null;
    sentToQc?: string | null;
    status?: string | null;
  } = {}): Observable<{ items: DailyProductionRowViewModel[]; summary: any }> {
    let params = new HttpParams();
    if (opts.fromDate)          params = params.set('fromDate', opts.fromDate);
    if (opts.toDate)            params = params.set('toDate', opts.toDate);
    if (opts.workOrderIdFilter) params = params.set('workOrderIdFilter', String(opts.workOrderIdFilter));
    if (opts.machineIdFilter)   params = params.set('machineIdFilter', String(opts.machineIdFilter));
    if (opts.operatorIdFilter)  params = params.set('operatorIdFilter', String(opts.operatorIdFilter));
    if (opts.shift)             params = params.set('shift', opts.shift);
    if (opts.posted)            params = params.set('posted', opts.posted);
    if (opts.sentToQc)          params = params.set('sentToQc', opts.sentToQc);
    if (opts.status)            params = params.set('status', opts.status);

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res ?? {};
        const itemsRaw = Array.isArray(raw)
          ? raw
          : (Array.isArray(raw?.items) ? raw.items : (Array.isArray(raw?.Items) ? raw.Items : []));
        return {
          items: itemsRaw.map((x: any) => this.mapRow(x)),
          summary: raw?.summary ?? raw?.Summary ?? null
        };
      })
    );
  }

  // Phase Q.1 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  getDetail(id: number): Observable<DailyProductionDetailViewModel> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res;
        return this.mapDetail(raw);
      })
    );
  }

  create(payload: DailyProductionInputDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, payload);
  }

  update(id: number, payload: DailyProductionInputDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update/${id}`, payload);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  postToWip(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/post-to-wip/${id}`,
      {}
    );
  }

  sendToQc(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/send-to-qc/${id}`, {});
  }

  reverse(id: number, reason: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/reverse/${id}`, { reason });
  }

  getWorkOrderItemDropdown(): Observable<WorkOrderItemDropdownVm[]> {
    return this.http.get<any>(`/emb-products/dropdowns/work-order-items`).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res ?? [];
        return Array.isArray(raw) ? raw.map((x: any) => this.mapWorkOrderItem(x)) : [];
      })
    );
  }

  getReworkTasks(): Observable<ReworkTaskVm[]> {
    return this.http.get<any>(`/emb-products/dropdowns/rework-tasks`).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res ?? [];
        return Array.isArray(raw)
          ? raw.map((x: any) => ({
              qcInspectionId: Number(x?.qcInspectionId ?? x?.QcInspectionId ?? 0),
              qcNo: String(x?.qcNo ?? x?.QcNo ?? ''),
              workOrderId: x?.workOrderId ?? x?.WorkOrderId ?? null,
              workOrderLabel: String(x?.workOrderLabel ?? x?.WorkOrderLabel ?? ''),
              workOrderItemId: x?.workOrderItemId ?? x?.WorkOrderItemId ?? null,
              productId: x?.productId ?? x?.ProductId ?? null,
              productName: String(x?.productName ?? x?.ProductName ?? ''),
              designId: x?.designId ?? x?.DesignId ?? null,
              designName: String(x?.designName ?? x?.DesignName ?? ''),
              reworkQty: Number(x?.reworkQty ?? x?.ReworkQty ?? 0),
              machineId: x?.machineId ?? x?.MachineId ?? null,
              stitchesPerPiece: Number(x?.stitchesPerPiece ?? x?.StitchesPerPiece ?? 0),
              name: String(x?.name ?? x?.Name ?? '')
            }))
          : [];
      })
    );
  }

  getMachineDropdown(): Observable<DropdownItem[]> {
    return this.http.get<any>(`/emb-products/dropdowns/machines`).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res ?? [];
        return Array.isArray(raw) ? raw.map((x: any) => this.mapDropdownItem(x)) : [];
      })
    );
  }

  getOperatorDropdown(): Observable<DropdownItem[]> {
    return this.http.get<any>(`/emb-products/dropdowns/operators`).pipe(
      map(res => {
        const raw = res?.data ?? res?.Data ?? res ?? [];
        return Array.isArray(raw) ? raw.map((x: any) => this.mapDropdownItem(x)) : [];
      })
    );
  }

  getCurrentOperator(): Observable<ApiResponse<{ id: number; name: string }>> {
    return this.http.get<ApiResponse<{ id: number; name: string }>>(
      `${this.baseUrl}/current-operator`
    );
  }

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

  printView(): string {
    return `${this.baseUrl}/print`;
  }

  private mapDropdownItem(x: any): DropdownItem {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      name: String(x?.name ?? x?.Name ?? x?.text ?? x?.Text ?? '')
    };
  }

  private mapWorkOrderItem(x: any): WorkOrderItemDropdownVm {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      name:
        x?.name ??
        x?.Name ??
        x?.workOrderLabel ??
        x?.WorkOrderLabel ??
        '',

      workOrderLabel: x?.workOrderLabel ?? x?.WorkOrderLabel ?? '',
      productName: x?.productName ?? x?.ProductName ?? '',
      designName: x?.designName ?? x?.DesignName ?? '',
      componentName: x?.componentName ?? x?.ComponentName ?? '',

      plannedQty: Number(x?.plannedQty ?? x?.PlannedQty ?? 0),
      completedQty: Number(x?.completedQty ?? x?.CompletedQty ?? 0),
      remainingQty: Number(x?.remainingQty ?? x?.RemainingQty ?? 0),
      isCompleted: Boolean(x?.isCompleted ?? x?.IsCompleted ?? false),

      machineId:
        x?.machineId != null
          ? Number(x.machineId)
          : x?.MachineId != null
            ? Number(x.MachineId)
            : null,

      stitchesPerPiece:
        x?.stitchesPerPiece != null
          ? Number(x.stitchesPerPiece)
          : x?.StitchesPerPiece != null
            ? Number(x.StitchesPerPiece)
            : 0
    };
  }

  private mapRow(x: any): DailyProductionRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      workOrderItemId: x?.workOrderItemId ?? x?.WorkOrderItemId ?? null,
      // Sprint-1: parent WO header id, used by the clickable WO badge.
      workOrderId: x?.workOrderId ?? x?.WorkOrderId ?? null,
      workOrderLabel: x?.workOrderLabel ?? x?.WorkOrderLabel ?? null,

      productId: x?.productId ?? x?.ProductId ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      designId: x?.designId ?? x?.DesignId ?? null,
      designName: x?.designName ?? x?.DesignName ?? null,

      machineId: x?.machineId ?? x?.MachineId ?? null,
      machineName: x?.machineName ?? x?.MachineName ?? null,

      operatorId: x?.operatorId ?? x?.OperatorId ?? null,
      operatorCode: x?.operatorCode ?? x?.OperatorCode ?? null,
      operatorName: x?.operatorName ?? x?.OperatorName ?? null,
      operatorLabel: x?.operatorLabel ?? x?.OperatorLabel ?? null,

      shift: x?.shift ?? x?.Shift ?? null,
      startTime: x?.startTime ?? x?.StartTime ?? null,
      endTime: x?.endTime ?? x?.EndTime ?? null,

      workingHours: Number(x?.workingHours ?? x?.WorkingHours ?? 0),
      productionPerHour: Number(x?.productionPerHour ?? x?.ProductionPerHour ?? 0),

      stitches: x?.stitches ?? x?.Stitches ?? null,

      plannedQty: Number(x?.plannedQty ?? x?.PlannedQty ?? 0),
      actualQty: x?.actualQty ?? x?.ActualQty ?? null,
      rejectedQty: x?.rejectedQty ?? x?.RejectedQty ?? null,
      acceptedQty: x?.acceptedQty ?? x?.AcceptedQty ?? null,

      status: x?.status ?? x?.Status ?? null,

      isPosted: (x?.isPosted ?? x?.IsPosted ?? false) === true,
      isSentToQC: (x?.isSentToQC ?? x?.IsSentToQC ?? false) === true,
      hasQc: (x?.hasQc ?? x?.HasQc ?? false) === true,
      qcCompleted: (x?.qcCompleted ?? x?.QcCompleted ?? false) === true,
      isReversed: (x?.isReversed ?? x?.IsReversed ?? false) === true,

      canEdit: (x?.canEdit ?? x?.CanEdit ?? false) === true,
      canDelete: (x?.canDelete ?? x?.CanDelete ?? false) === true,
      canPost: (x?.canPost ?? x?.CanPost ?? false) === true,
      canReverse: (x?.canReverse ?? x?.CanReverse ?? false) === true
    };
  }

  private mapDetail(x: any): DailyProductionDetailViewModel {
    const operatorCode = x?.operatorCode ?? x?.OperatorCode ?? null;
    const operatorName = x?.operatorName ?? x?.OperatorName ?? null;

    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      workOrderItemId: x?.workOrderItemId ?? x?.WorkOrderItemId ?? null,
      workOrderLabel: x?.workOrderLabel ?? x?.WorkOrderLabel ?? null,

      productId: x?.productId ?? x?.ProductId ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      designId: x?.designId ?? x?.DesignId ?? null,
      designName: x?.designName ?? x?.DesignName ?? null,

      machineId: x?.machineId ?? x?.MachineId ?? null,
      machineName: x?.machineName ?? x?.MachineName ?? null,

      operatorId: x?.operatorId ?? x?.OperatorId ?? null,
      operatorCode,
      operatorName,
      operatorLabel: operatorCode ? `${operatorCode} - ${operatorName ?? ''}` : operatorName,

      shift: x?.shift ?? x?.Shift ?? null,
      startTime: x?.startTime ?? x?.StartTime ?? null,
      endTime: x?.endTime ?? x?.EndTime ?? null,

      workingHours: Number(x?.workingHours ?? x?.WorkingHours ?? 0),
      productionPerHour: Number(x?.productionPerHour ?? x?.ProductionPerHour ?? 0),

      stitches: x?.stitches ?? x?.Stitches ?? null,

      plannedQty: Number(x?.plannedQty ?? x?.PlannedQty ?? 0),
      actualQty: x?.actualQty ?? x?.ActualQty ?? null,
      rejectedQty: x?.rejectedQty ?? x?.RejectedQty ?? null,
      acceptedQty: x?.acceptedQty ?? x?.AcceptedQty ?? null,

      status: x?.status ?? x?.Status ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      isPosted: (x?.isPosted ?? x?.IsPosted ?? false) === true,
      isSentToQC: (x?.isSentToQC ?? x?.IsSentToQC ?? false) === true,
      hasQc: (x?.hasQc ?? x?.HasQc ?? false) === true,
      qcCompleted: (x?.qcCompleted ?? x?.QcCompleted ?? false) === true,
      isReversed: (x?.isReversed ?? x?.IsReversed ?? false) === true,

      qcId: x?.qcId ?? x?.QcId ?? null,
      sentToQCDate: x?.sentToQCDate ?? x?.SentToQCDate ?? null,

      recordTimeStamp: x?.recordTimeStamp ?? x?.RecordTimeStamp ?? null,

      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      createdByEmployeeId: x?.createdByEmployeeId ?? x?.CreatedByEmployeeId ?? null,

      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null,
      updatedByEmployeeId: x?.updatedByEmployeeId ?? x?.UpdatedByEmployeeId ?? null,

      canEdit: (x?.canEdit ?? x?.CanEdit ?? false) === true,
      canDelete: (x?.canDelete ?? x?.CanDelete ?? false) === true,
      canPost: (x?.canPost ?? x?.CanPost ?? false) === true,
      canReverse: (x?.canReverse ?? x?.CanReverse ?? false) === true,

      // Sprint-1: parent WO id for the clickable header pill.
      workOrderId: x?.workOrderId ?? x?.WorkOrderId ?? null,

      // Sprint-2: workflow stepper data — the row VM only needs the
      // pill; the detail drawer renders the full pipeline.
      workflowStages: x?.workflowStages ?? x?.WorkflowStages ?? undefined,
      workflowStageIndex: x?.workflowStageIndex ?? x?.WorkflowStageIndex ?? 0
    };
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — append-only audit timeline for the detail drawer.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<ApiResponse<any[]>> {
    return this.http.get<ApiResponse<any[]>>(`${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk actions. Each returns the per-id failure
  //  envelope so the toolbar can render "n succeeded, m failed".
  // ───────────────────────────────────────────────────────────────
  bulkPost(ids: number[]): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.baseUrl}/bulk-post`, { ids });
  }
  bulkSendToQc(ids: number[]): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.baseUrl}/bulk-send-to-qc`, { ids });
  }
  bulkReverse(ids: number[], reason: string): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.baseUrl}/bulk-reverse`, { ids, reason });
  }
  bulkDelete(ids: number[]): Observable<ApiResponse<any>> {
    return this.http.post<ApiResponse<any>>(`${this.baseUrl}/bulk-delete`, { ids });
  }
}
