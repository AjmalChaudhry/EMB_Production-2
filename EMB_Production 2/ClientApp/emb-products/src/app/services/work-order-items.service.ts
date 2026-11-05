import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map, of } from 'rxjs';
import {ApiResponseBase,ApiResponseData,ApiPagedResponse} from '../shared/models/api-response';
import {WorkOrderItemRowViewModel,WorkOrderItemDetailViewModel,
    WorkOrderItemDto} from '../shared/models/work-order-item.dto';

interface WorkOrderItemCostResult {
  costPerUnit: number;
  totalCost: number;
  sellingPricePerUnit: number;
  totalPrice: number;
  threadCost: number;
  machineCost: number;
  laborCost: number;
  electricityCost: number;
  otherCost: number;
}

interface DropdownItem {
  id: number;
  name: string;
}

@Injectable({
  providedIn: 'root'
})
export class WorkOrderItemsService {
  private readonly baseUrl = '/emb-products/work-order-items';

  constructor(private http: HttpClient) { }

  // =====================================================
  // LIST
  // =====================================================
  getAll(
    search: string = '',
    showDeleted: string = '',
    page: number = 1,
    pageSize: number = 10,
    extra: {
      workOrderId?: number | null;
      branchId?: number | null;
      fromDate?: string | null;
      toDate?: string | null;
      statusFilter?: string | null;
    } = {}
  ): Observable<ApiPagedResponse<WorkOrderItemRowViewModel> & { summary?: any }> {
    let params = new HttpParams({
      fromObject: {
        search: search ?? '',
        showDeleted: showDeleted ?? '',
        page: String(page),
        pageSize: String(pageSize)
      }
    });

    if (extra.workOrderId)  params = params.set('workOrderId', String(extra.workOrderId));
    if (extra.branchId)     params = params.set('branchId', String(extra.branchId));
    if (extra.fromDate)     params = params.set('fromDate', extra.fromDate);
    if (extra.toDate)       params = params.set('toDate', extra.toDate);
    if (extra.statusFilter) params = params.set('statusFilter', extra.statusFilter);

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
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

        const items: WorkOrderItemRowViewModel[] = rawItems.map((x: any) => this.mapRow(x));

        const totalItems = Number(
          raw?.totalItems ??
          raw?.TotalItems ??
          raw?.total ??
          raw?.Total ??
          items.length
        ) || items.length;

        const currentPage = Number(raw?.page ?? raw?.Page ?? page) || page;
        const currentPageSize = Number(raw?.pageSize ?? raw?.PageSize ?? pageSize) || pageSize;
        const totalPages = Number(
          raw?.totalPages ??
          raw?.TotalPages ??
          Math.max(1, Math.ceil(totalItems / currentPageSize))
        ) || 1;

        return {
          success,
          message,
          data: {
            items,
            totalItems,
            page: currentPage,
            pageSize: currentPageSize,
            totalPages
          },
          summary: raw?.summary ?? raw?.Summary ?? null
        };
      })
    );
  }

  get(id: number): Observable<ApiResponseData<any>> {
    return this.http.get<any>(`${this.baseUrl}/get/${id}`).pipe(
      map((res: any) => {
        return {
          success: Boolean(res?.success ?? res?.Success ?? false),
          message: String(res?.message ?? res?.Message ?? ''),
          data: res?.data ?? res?.Data ?? null
        };
      })
    );
  }

  // =====================================================
  // DETAIL
  // =====================================================
  getDetail(id: number): Observable<ApiResponseData<WorkOrderItemDetailViewModel | null>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';
        const raw = res?.data ?? res?.Data ?? null;

        return {
          success,
          message,
          data: success && raw ? this.mapDetail(raw) : null
        };
      })
    );
  }

  // =====================================================
  // CREATE
  // =====================================================
  create(dto: WorkOrderItemDto): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(`${this.baseUrl}/create`, dto);
  }

  // =====================================================
  // UPDATE
  // =====================================================
  update(id: number, dto: WorkOrderItemDto) {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/update/${id}`,
      dto
    );
  }

  // =====================================================
  // DELETE
  // =====================================================
  delete(id: number): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(`${this.baseUrl}/delete/${id}`, {});
  }

  // =====================================================
  // GENERATE / PREVIEW
  // =====================================================
  generateFromWorkOrder(workOrderId: number): Observable<ApiResponseBase> {
    return this.http.post<ApiResponseBase>(
      `${this.baseUrl}/generate-from-workorder/${workOrderId}`,
      {}
    );
  }
 
  // =====================================================
  // RECALCULATE COST
  // =====================================================
  recalculateCost(
    designId: number,
    quantity: number
  ): Observable<ApiResponseData<WorkOrderItemCostResult>> {
    const params = new HttpParams()
      .set('designId', String(designId))
      .set('quantity', String(quantity));

    return this.http.get<any>(`${this.baseUrl}/recalculate-cost`, { params }).pipe(
      map((res: any): ApiResponseData<WorkOrderItemCostResult> => {
        const success = Boolean(res?.success ?? res?.Success ?? false);
        const message = String(res?.message ?? res?.Message ?? '');
        const raw = res?.data ?? res?.Data ?? {};

        const costPerUnit = Number(raw?.costPerUnit ?? raw?.CostPerUnit ?? 0);
        const totalCost = Number(raw?.totalCost ?? raw?.TotalCost ?? 0);

        const sellingPricePerUnit = Number(
          raw?.sellingPricePerUnit ??
          raw?.SellingPricePerUnit ??
          raw?.unitPrice ??
          raw?.UnitPrice ??
          costPerUnit
        );

        const totalPrice = Number(
          raw?.totalPrice ??
          raw?.TotalPrice ??
          (quantity * sellingPricePerUnit)
        );

        return {
          success,
          message,
          data: {
            costPerUnit,
            totalCost,
            sellingPricePerUnit,
            totalPrice,
            threadCost: Number(raw?.threadCost ?? raw?.ThreadCost ?? 0),
            machineCost: Number(raw?.machineCost ?? raw?.MachineCost ?? 0),
            laborCost: Number(raw?.laborCost ?? raw?.LaborCost ?? 0),
            electricityCost: Number(raw?.electricityCost ?? raw?.ElectricityCost ?? 0),
            otherCost: Number(raw?.otherCost ?? raw?.OtherCost ?? 0)
          }
        };
      })
    );
  }

  // =====================================================
  // EXPORT URLS
  // =====================================================
  exportExcelUrl(): string {
    return `${this.baseUrl}/export-excel`;
  }

  exportPdfUrl(): string {
    return `${this.baseUrl}/export-pdf`;
  }

  exportPrintUrl(): string {
    return `${this.baseUrl}/print`;
  }

  // =====================================================
  // DROPDOWNS
  // =====================================================
  // =====================================================
  // DROPDOWNS
  // =====================================================
  getWorkOrders(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/dropdown/workorders').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getJobOrders(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/joborders/ddl').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getDesigns(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/dropdowns/designs').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getComponents(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/component-materials/ddl-components').pipe(
      map((res: any): DropdownItem[] => {
        const raw = res?.data ?? res?.Data ?? res ?? [];

        if (!Array.isArray(raw)) {
          return [];
        }

        return raw.map((x: any): DropdownItem => {
          const id = Number(x?.id ?? x?.Id ?? 0);
          const code = String(x?.code ?? x?.Code ?? '').trim();
          const name = String(x?.name ?? x?.Name ?? '').trim();
          const display = code ? `${code} - ${name}` : name;

          return {
            id: id,
            name: display
          };
        });
      })
    );
  }

  getBranches(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/dropdowns/branches').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getMachines(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/dropdowns/machines').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getProducts(): Observable<DropdownItem[]> {
    return this.http.get<any>('/emb-products/dropdowns/products').pipe(
      map((res: any) => this.mapDropdownArray(res))
    );
  }

  getComponentsByDesign(designId: number): Observable<DropdownItem[]> {
    if (!designId || designId <= 0) {
      return of<DropdownItem[]>([]);
    }

    return this.http.get<any>(`${this.baseUrl}/components-by-design/${designId}`).pipe(
      map((res: any): DropdownItem[] => {
        const raw = res?.data ?? res?.Data ?? res ?? [];

        if (!Array.isArray(raw)) {
          return [];
        }

        return raw.map((x: any): DropdownItem => {
          const id = Number(x?.id ?? x?.Id ?? 0);
          const code = String(x?.code ?? x?.Code ?? '').trim();
          const name = String(x?.name ?? x?.Name ?? '').trim();
          const display = String(
            x?.display ??
            x?.Display ??
            (code ? `${code} - ${name}` : name)
          ).trim();

          return {
            id: id,
            name: display
          };
        });
      })
    );
  }

  // =====================================================
  // MAPPERS
  // =====================================================
  private mapRow(x: any): WorkOrderItemRowViewModel {
    const quantity = Number(x?.quantity ?? x?.Quantity ?? 0);
    const unitPrice = this.toNullableNumber(x?.unitPrice ?? x?.UnitPrice);
    const totalPrice = this.toNullableNumber(
      x?.totalPrice ??
      x?.TotalPrice ??
      (quantity * (unitPrice ?? 0))
    );

    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      workOrderId: this.toNullableNumber(x?.workOrderId ?? x?.WorkOrderId),
      workOrderCode: x?.workOrderCode ?? x?.WorkOrderCode ?? '',

      jobOrderId: this.toNullableNumber(x?.jobOrderId ?? x?.JobOrderId),
      jobOrderCode: x?.jobOrderCode ?? x?.JobOrderCode ?? '',

      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      designCode: x?.designCode ?? x?.DesignCode ?? '',
      designName: x?.designName ?? x?.DesignName ?? '',

      compId: this.toNullableNumber(x?.compId ?? x?.CompId),
      componentCode: x?.componentCode ?? x?.ComponentCode ?? '',
      componentName: x?.componentName ?? x?.ComponentName ?? '',

      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      productName: x?.productName ?? x?.ProductName ?? '',

      quantity,
      unitPrice,
      totalPrice,

      branchId: this.toNullableNumber(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? '',

      machineId: this.toNullableNumber(x?.machineId ?? x?.MachineId),
      machineName: x?.machineName ?? x?.MachineName ?? '',

      status: x?.status ?? x?.Status ?? 'Pending',
      active: this.toNullableBoolean(x?.active ?? x?.Active),
      completedQty: Number(x?.completedQty ?? x?.CompletedQty ?? 0),
      rejectedQty: Number(x?.rejectedQty ?? x?.RejectedQty ?? 0),

      remarks: x?.remarks ?? x?.Remarks ?? '',
      deleted: this.toNullableBoolean(x?.deleted ?? x?.Deleted),

      createdDate: this.toNullableString(x?.createdDate ?? x?.CreatedDate),
      updatedDate: this.toNullableString(x?.updatedDate ?? x?.UpdatedDate),

      createdDateText: this.toNullableString(x?.createdDateText ?? x?.CreatedDateText),
      updatedDateText: this.toNullableString(x?.updatedDateText ?? x?.UpdatedDateText),

      threadCost: this.toNullableNumber(x?.threadCost ?? x?.ThreadCost),
      machineCost: this.toNullableNumber(x?.machineCost ?? x?.MachineCost),
      laborCost: this.toNullableNumber(x?.laborCost ?? x?.LaborCost),
      electricityCost: this.toNullableNumber(x?.electricityCost ?? x?.ElectricityCost),
      otherCost: this.toNullableNumber(x?.otherCost ?? x?.OtherCost),
      totalCost: this.toNullableNumber(x?.totalCost ?? x?.TotalCost),
      costPerUnit: this.toNullableNumber(x?.costPerUnit ?? x?.CostPerUnit)
    };
  }

  private mapDetail(x: any): WorkOrderItemDetailViewModel {
    const row = this.mapRow(x);

    return {
      ...row,
      workOrderLabel: this.toNullableString(x?.workOrderLabel ?? x?.WorkOrderLabel),

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      updatedBy: this.toNullableNumber(x?.updatedBy ?? x?.UpdatedBy),
      recordTimeStamp: this.toNullableString(x?.recordTimeStamp ?? x?.RecordTimeStamp),

      createdByEmployeeId: this.toNullableNumber(x?.createdByEmployeeId ?? x?.CreatedByEmployeeId),
      updatedByEmployeeId: this.toNullableNumber(x?.updatedByEmployeeId ?? x?.UpdatedByEmployeeId)
    };
  }
 
  private mapDropdownArray(res: any): DropdownItem[] {
    const raw = res?.data ?? res?.Data ?? [];
    if (!Array.isArray(raw)) {
      return [];
    }

    return raw.map((x: any) => ({
      id: Number(x?.id ?? x?.Id ?? 0),
      name: x?.name ?? x?.Name ?? x?.text ?? x?.Text ?? x?.label ?? x?.Label ?? ''
    }));
  }

  private toNullableNumber(value: any): number | null {
    if (value === undefined || value === null || value === '') {
      return null;
    }

    const num = Number(value);
    return Number.isNaN(num) ? null : num;
  }

  private toNullableBoolean(value: any): boolean | null {
    if (value === undefined || value === null || value === '') {
      return null;
    }

    if (typeof value === 'boolean') {
      return value;
    }

    if (typeof value === 'string') {
      const normalized = value.trim().toLowerCase();
      if (normalized === 'true' || normalized === '1') {
        return true;
      }
      if (normalized === 'false' || normalized === '0') {
        return false;
      }
    }

    if (typeof value === 'number') {
      return value !== 0;
    }

    return Boolean(value);
  }

  private toNullableString(value: any): string | null {
    if (value === undefined || value === null || value === '') {
      return null;
    }
    return String(value);
  }
}
