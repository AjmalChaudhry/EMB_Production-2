import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';

import {
  ApiResponse,
  ApiPagedResponse,
  ApiResponseData
} from '../shared/models/api-response';

import {
  DesignCostingDto,
  DesignCostCalculationResult,
  DesignCostingDetail,
  DesignCostingListRow
} from '../shared/models/design-costing.dto';

@Injectable({ providedIn: 'root' })
export class DesignCostingService {
  private baseUrl = '/emb-products/design-costing';

  constructor(private http: HttpClient) { }

  /* =====================================================
     LIST
     Supports both:
     1) direct array in data
     2) server-paged object with items/total/page/pageSize
  ===================================================== */
  list(params: {
    page: number;
    pageSize: number;
    activeFilter?: string;
    search?: string;
    productId?: number | null;
    designId?: number | null;
    marginRange?: string | null;
  }): Observable<ApiPagedResponse<DesignCostingListRow> & { summary?: any }> {

    let httpParams = new HttpParams()
      .set('activeFilter', params.activeFilter ?? '')
      .set('search', params.search ?? '');
    if (params.productId)   httpParams = httpParams.set('productId', String(params.productId));
    if (params.designId)    httpParams = httpParams.set('designId', String(params.designId));
    if (params.marginRange) httpParams = httpParams.set('marginRange', params.marginRange);

    return this.http.get<any>(`${this.baseUrl}/list`, { params: httpParams }).pipe(
      map((res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';
        const rawData = res?.data ?? res?.Data ?? [];

        const page = Number(params.page || 1);
        const pageSize = Number(params.pageSize || 10);

        let allItems: DesignCostingListRow[] = [];

        if (Array.isArray(rawData)) {
          allItems = rawData.map((x: any) => this.mapListItem(x));
        } else {
          const rawItems =
            rawData?.items ??
            rawData?.Items ??
            rawData?.rows ??
            rawData?.Rows ??
            [];

          allItems = Array.isArray(rawItems)
            ? rawItems.map((x: any) => this.mapListItem(x))
            : [];
        }

        const totalItems = allItems.length;
        const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

        const pagedItems = allItems.slice(
          (page - 1) * pageSize,
          page * pageSize
        );

        return {
          success,
          message,
          data: {
            items: pagedItems,
            totalItems,
            page,
            pageSize,
            totalPages
          },
          summary: (rawData && !Array.isArray(rawData))
            ? (rawData.summary ?? rawData.Summary ?? null)
            : null
        } as ApiPagedResponse<DesignCostingListRow> & { summary?: any };
      })
    );
  }

  // Phase R.0 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  /* =====================================================
     DETAIL
  ===================================================== */
  detail(id: number): Observable<ApiResponseData<DesignCostingDetail>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';
        const raw = res?.data ?? res?.Data ?? null;

        return {
          success,
          message,
          data: raw ? this.mapDetailItem(raw) : (null as any)
        } as ApiResponseData<DesignCostingDetail>;
      })
    );
  }
  
  save(payload: DesignCostingDto): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/save`, payload).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        detail: res?.detail ?? res?.Detail ?? '',
        data: res?.data ?? res?.Data ?? null
      }))
    );
  }

  /* =====================================================
     DELETE
  ===================================================== */
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<any>(`${this.baseUrl}/delete/${id}`, {}).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data ?? res?.Data ?? null
      }))
    );
  }

  /* =====================================================
     RECALCULATE
     Backend action is [HttpPost]
  ===================================================== */
  recalculate(
    designId: number,
    batchQty: number,
    marginPercent: number,
    productId: number | null = null
  ): Observable<ApiResponseData<DesignCostCalculationResult>> {
    return this.http.post<any>(`${this.baseUrl}/recalculate`, {
      designId,
      batchQty,
      marginPercent,
      productId
    }).pipe(
      map((res: any): ApiResponseData<DesignCostCalculationResult> => {
        const d = res?.data ?? res?.Data ?? {};

        return {
          success: Boolean(res?.success ?? res?.Success ?? false),
          message: String(res?.message ?? res?.Message ?? ''),
          data: {
            threadCost: Number(d?.threadCost ?? d?.ThreadCost ?? 0),
            machineCost: Number(d?.machineCost ?? d?.MachineCost ?? 0),
            laborCost: Number(d?.laborCost ?? d?.LaborCost ?? 0),
            electricityCost: Number(d?.electricityCost ?? d?.ElectricityCost ?? 0),
            otherCost: Number(d?.otherCost ?? d?.OtherCost ?? 0),

            totalCost: Number(d?.totalCost ?? d?.TotalCost ?? 0),
            costPerUnit: Number(d?.costPerUnit ?? d?.CostPerUnit ?? 0),

            sellingPricePerUnit: Number(d?.sellingPricePerUnit ?? d?.SellingPricePerUnit ?? 0),
            totalSellingPrice: Number(
              d?.totalSellingPrice ??
              d?.TotalSellingPrice ??
              d?.totalPrice ??
              d?.TotalPrice ??
              0
            ),

            marginPercent: Number(d?.marginPercent ?? d?.MarginPercent ?? 0),

            threadMetersPerPiece: Number(d?.threadMetersPerPiece ?? d?.ThreadMetersPerPiece ?? 0),
            totalThreadMeters: Number(d?.totalThreadMeters ?? d?.TotalThreadMeters ?? 0),
            costPer1000Stitches: Number(d?.costPer1000Stitches ?? d?.CostPer1000Stitches ?? 0),

            productionMinutes: Number(d?.productionMinutes ?? d?.ProductionMinutes ?? 0),
            productionHours: Number(d?.productionHours ?? d?.ProductionHours ?? 0),
            piecesPerHour: Number(d?.piecesPerHour ?? d?.PiecesPerHour ?? 0),

            piecesPerShift: Number(d?.piecesPerShift ?? d?.PiecesPerShift ?? 0),
            dailyCapacity: Number(d?.dailyCapacity ?? d?.DailyCapacity ?? 0),
            productionDays: Number(d?.productionDays ?? d?.ProductionDays ?? 0)
          }
        };
      })
    );
  }

  /* =====================================================
     EXPORTS
  ===================================================== */
  exportExcel(params?: { activeFilter?: string; search?: string }): void {
    const query = this.buildQueryString(params);
    window.open(`${this.baseUrl}/export-excel${query}`, '_blank');
  }

  exportPdfList(params?: { activeFilter?: string; search?: string }): void {
    const query = this.buildQueryString(params);
    window.open(`${this.baseUrl}/export-pdf-list${query}`, '_blank');
  }

  exportPdf(id: number): void {
    window.open(`${this.baseUrl}/export-pdf/${id}`, '_blank');
  }

  printList(params?: { activeFilter?: string; search?: string }): void {
    const query = this.buildQueryString(params);
    window.open(`${this.baseUrl}/print-list${query}`, '_blank');
  }

  print(id: number): void {
    window.open(`${this.baseUrl}/print/${id}`, '_blank');
  }

  /* =====================================================
     DROPDOWN
  ===================================================== */
  getDesignDropdown(): Observable<ApiResponse> {
    return this.http.get<any>('/emb-products/dropdowns/designs').pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data ?? res?.Data ?? []
      }))
    );
  }

  /* =====================================================
     HELPERS
  ===================================================== */
  private buildQueryString(params?: { activeFilter?: string; search?: string }): string {
    const httpParams = new HttpParams()
      .set('activeFilter', params?.activeFilter ?? '')
      .set('search', params?.search ?? '');

    const query = httpParams.toString();
    return query ? `?${query}` : '';
  }

  private mapListItem(x: any): DesignCostingListRow {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      designName: x?.designName ?? x?.DesignName ?? '',

      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      productCode: x?.productCode ?? x?.ProductCode ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      batchQty: Number(x?.batchQty ?? x?.BatchQty ?? 0),
      marginPercent: this.toNullableNumber(x?.marginPercent ?? x?.MarginPercent),

      threadCost: this.toNullableNumber(x?.threadCost ?? x?.ThreadCost),
      machineCost: this.toNullableNumber(x?.machineCost ?? x?.MachineCost),
      laborCost: this.toNullableNumber(x?.laborCost ?? x?.LaborCost),
      electricityCost: this.toNullableNumber(x?.electricityCost ?? x?.ElectricityCost),
      otherCost: this.toNullableNumber(x?.otherCost ?? x?.OtherCost),

      totalCost: this.toNullableNumber(x?.totalCost ?? x?.TotalCost),
      costPerUnit: this.toNullableNumber(x?.costPerUnit ?? x?.CostPerUnit),

      threadPerUnit:         this.toNullableNumber(x?.threadPerUnit         ?? x?.ThreadPerUnit),
      stocksMaterialUnit:    this.toNullableNumber(x?.stocksMaterialUnit    ?? x?.StocksMaterialUnit),
      materialCostPerUnit:   this.toNullableNumber(x?.materialCostPerUnit   ?? x?.MaterialCostPerUnit),
      productionCostPerUnit: this.toNullableNumber(x?.productionCostPerUnit ?? x?.ProductionCostPerUnit),
      serviceCostPerUnit:    this.toNullableNumber(x?.serviceCostPerUnit    ?? x?.ServiceCostPerUnit),
      productCostPerUnit:    this.toNullableNumber(x?.productCostPerUnit    ?? x?.ProductCostPerUnit),
      totalProductCost:      this.toNullableNumber(x?.totalProductCost      ?? x?.TotalProductCost),

      sellingPricePerUnit: this.toNullableNumber(x?.sellingPricePerUnit ?? x?.SellingPricePerUnit),
      totalSellingPrice: this.toNullableNumber(
        x?.totalSellingPrice ?? x?.TotalSellingPrice ?? x?.totalPrice ?? x?.TotalPrice
      ),

      active: this.toNullableBoolean(x?.active ?? x?.Active),

      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null,
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null,
      updatedDateText: x?.updatedDateText ?? x?.UpdatedDateText ?? null
    };
  }

  private mapDetailItem(x: any): DesignCostingDetail {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      designId: this.toNullableNumber(x?.designId ?? x?.DesignId),
      designName: x?.designName ?? x?.DesignName ?? '',

      productId: this.toNullableNumber(x?.productId ?? x?.ProductId),
      productCode: x?.productCode ?? x?.ProductCode ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      batchQty: Number(x?.batchQty ?? x?.BatchQty ?? 0),
      marginPercent: this.toNullableNumber(x?.marginPercent ?? x?.MarginPercent),

      threadCost: this.toNullableNumber(x?.threadCost ?? x?.ThreadCost),
      machineCost: this.toNullableNumber(x?.machineCost ?? x?.MachineCost),
      laborCost: this.toNullableNumber(x?.laborCost ?? x?.LaborCost),
      electricityCost: this.toNullableNumber(x?.electricityCost ?? x?.ElectricityCost),
      otherCost: this.toNullableNumber(x?.otherCost ?? x?.OtherCost),

      totalCost: this.toNullableNumber(x?.totalCost ?? x?.TotalCost),
      costPerUnit: this.toNullableNumber(x?.costPerUnit ?? x?.CostPerUnit),

      threadPerUnit:         this.toNullableNumber(x?.threadPerUnit         ?? x?.ThreadPerUnit),
      stocksMaterialUnit:    this.toNullableNumber(x?.stocksMaterialUnit    ?? x?.StocksMaterialUnit),
      materialCostPerUnit:   this.toNullableNumber(x?.materialCostPerUnit   ?? x?.MaterialCostPerUnit),
      productionCostPerUnit: this.toNullableNumber(x?.productionCostPerUnit ?? x?.ProductionCostPerUnit),
      serviceCostPerUnit:    this.toNullableNumber(x?.serviceCostPerUnit    ?? x?.ServiceCostPerUnit),
      productCostPerUnit:    this.toNullableNumber(x?.productCostPerUnit    ?? x?.ProductCostPerUnit),
      totalProductCost:      this.toNullableNumber(x?.totalProductCost      ?? x?.TotalProductCost),

      sellingPricePerUnit: this.toNullableNumber(x?.sellingPricePerUnit ?? x?.SellingPricePerUnit),
      totalSellingPrice: this.toNullableNumber(
        x?.totalSellingPrice ?? x?.TotalSellingPrice ?? x?.totalPrice ?? x?.TotalPrice
      ),

      threadMetersPerPiece: this.toNullableNumber(x?.threadMetersPerPiece ?? x?.ThreadMetersPerPiece),
      totalThreadMeters: this.toNullableNumber(x?.totalThreadMeters ?? x?.TotalThreadMeters),
      costPer1000Stitches: this.toNullableNumber(x?.costPer1000Stitches ?? x?.CostPer1000Stitches),

      productionMinutes: this.toNullableNumber(x?.productionMinutes ?? x?.ProductionMinutes),
      productionHours: this.toNullableNumber(x?.productionHours ?? x?.ProductionHours),
      piecesPerHour: this.toNullableNumber(x?.piecesPerHour ?? x?.PiecesPerHour),

      piecesPerShift: this.toNullableNumber(x?.piecesPerShift ?? x?.PiecesPerShift),
      dailyCapacity: this.toNullableNumber(x?.dailyCapacity ?? x?.DailyCapacity),
      productionDays: this.toNullableNumber(x?.productionDays ?? x?.ProductionDays),

      active: this.toNullableBoolean(x?.active ?? x?.Active),

      createdByEmployeeId: this.toNullableNumber(x?.createdByEmployeeId ?? x?.CreatedByEmployeeId),
      updatedByEmployeeId: this.toNullableNumber(x?.updatedByEmployeeId ?? x?.UpdatedByEmployeeId),
      createdDate: x?.createdDate ?? x?.CreatedDate ?? null,
      updatedDate: x?.updatedDate ?? x?.UpdatedDate ?? null,
      createdDateText: x?.createdDateText ?? x?.CreatedDateText ?? null,
      updatedDateText: x?.updatedDateText ?? x?.UpdatedDateText ?? null
    };
  }

  private toNullableNumber(value: any): number | null {
    if (value === undefined || value === null || value === '') return null;
    const num = Number(value);
    return Number.isNaN(num) ? null : num;
  }

  private toNullableBoolean(value: any): boolean | null {
    if (value === undefined || value === null || value === '') return null;

    if (typeof value === 'boolean') return value;

    if (typeof value === 'string') {
      const v = value.trim().toLowerCase();
      if (v === 'true' || v === '1') return true;
      if (v === 'false' || v === '0') return false;
    }

    if (typeof value === 'number') {
      return value !== 0;
    }

    return Boolean(value);
  }
}
