import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {ApiPagedResponse,ApiResponseData,DropdownItem} from '../shared/models/api-response';
import {StockRowViewModel,StockDetailViewModel,StockDashboardViewModel
        } from '../shared/models/gen-stocks.dto';

@Injectable({ providedIn: 'root' })
export class GenStocksService {
  private readonly baseUrl = '/emb-products/gen-stocks';

  constructor(private http: HttpClient) { }

  list(
    search: string = '',
    page: number = 1,
    pageSize: number = 20,
    branchId: number | null = null,
    extra: {
      stockStatusFilter?: string | null;
      productIdFilter?: number | null;
    } = {}
  ): Observable<ApiPagedResponse<StockRowViewModel> & { dashboard?: any }> {
    let params = new HttpParams({
      fromObject: {
        search: search ?? '',
        page: String(page ?? 1),
        pageSize: String(pageSize ?? 20),
        branchId: branchId != null ? String(branchId) : ''
      }
    });

    if (extra.stockStatusFilter) params = params.set('stockStatusFilter', extra.stockStatusFilter);
    if (extra.productIdFilter)   params = params.set('productIdFilter', String(extra.productIdFilter));

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map((res: any) => {
        const rawData = res?.data ?? res?.Data ?? {};
        const rawItems = Array.isArray(rawData?.items)
          ? rawData.items
          : Array.isArray(rawData?.Items)
            ? rawData.Items
            : [];

        const items = rawItems.map((x: any) => this.mapRow(x));
        const totalItems = Number(rawData?.totalItems ?? rawData?.TotalItems ?? items.length);
        const resolvedPageSize = Number(rawData?.pageSize ?? rawData?.PageSize ?? pageSize ?? 20);

        return {
          success: res?.success ?? res?.Success ?? false,
          message: res?.message ?? res?.Message ?? '',
          data: {
            items,
            page: Number(rawData?.page ?? rawData?.Page ?? page ?? 1),
            pageSize: resolvedPageSize,
            totalItems,
            totalPages: Number(
              rawData?.totalPages ??
              rawData?.TotalPages ??
              Math.max(1, Math.ceil(totalItems / Math.max(resolvedPageSize, 1)))
            )
          },
          dashboard: rawData?.dashboard ?? rawData?.Dashboard ?? null
        } as ApiPagedResponse<StockRowViewModel> & { dashboard?: any };
      })
    );
  }

  // Phase P.1 - per-product full ledger
  itemHistory(productId: number, branchId?: number | null): Observable<any> {
    let p = new HttpParams();
    if (branchId != null && Number(branchId) > 0) {
      p = p.set('branchId', String(branchId));
    }
    return this.http.get<any>(`${this.baseUrl}/item-history/${productId}`, { params: p });
  }

  detail(id: number): Observable<ApiResponseData<StockDetailViewModel>> {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`).pipe(
      map((res: any) => ({
        success: res?.success ?? res?.Success ?? false,
        message: res?.message ?? res?.Message ?? '',
        data: res?.data || res?.Data ? this.mapDetail(res?.data ?? res?.Data) : null
      }))
    );
  }

  getDashboard(
    search: string = '',
    branchId: number | null = null
  ): Observable<ApiResponseData<StockDashboardViewModel>> {
    const params = new HttpParams({
      fromObject: {
        search: search ?? '',
        branchId: branchId != null ? String(branchId) : ''
      }
    });

    return this.http.get<any>(`${this.baseUrl}/dashboard-summary`, { params }).pipe(
      map((res: any) => {
        const raw = res?.data ?? res?.Data ?? {};

        return {
          success: res?.success ?? res?.Success ?? false,
          message: res?.message ?? res?.Message ?? '',
          data: {
            customerReservedStock: Number(raw?.customerReservedStock ?? raw?.CustomerReservedStock ?? 0),
            freeCompanyStock: Number(raw?.freeCompanyStock ?? raw?.FreeCompanyStock ?? 0),
            totalFinishedStock: Number(raw?.totalFinishedStock ?? raw?.TotalFinishedStock ?? 0),
            totalProducts: Number(raw?.totalProducts ?? raw?.TotalProducts ?? 0),
            totalBranches: Number(raw?.totalBranches ?? raw?.TotalBranches ?? 0)
          }
        } as ApiResponseData<StockDashboardViewModel>;
      })
    );
  }

  getBranches(): Observable<ApiResponseData<DropdownItem[]>> {
    return this.http.get<any>(`${this.baseUrl}/branches`).pipe(
      map((res: any) => {
        const raw = res?.data ?? res?.Data ?? [];
        const items: DropdownItem[] = Array.isArray(raw)
          ? raw.map((x: any) => this.mapDropdown(x))
          : [];

        return {
          success: res?.success ?? res?.Success ?? false,
          message: res?.message ?? res?.Message ?? '',
          data: items
        };
      })
    );
  }

  exportExcel(search: string = '', branchId: number | null = null): Observable<Blob> {
    const params = new HttpParams({
      fromObject: {
        search: search ?? '',
        branchId: branchId != null ? String(branchId) : ''
      }
    });

    return this.http.get(`${this.baseUrl}/export-excel`, {
      params,
      responseType: 'blob'
    });
  }

  exportPdf(search: string = '', branchId: number | null = null): Observable<Blob> {
    const params = new HttpParams({
      fromObject: {
        search: search ?? '',
        branchId: branchId != null ? String(branchId) : ''
      }
    });

    return this.http.get(`${this.baseUrl}/export-pdf`, {
      params,
      responseType: 'blob'
    });
  }

  printUrl(search: string = '', branchId: number | null = null): string {
    const params = new URLSearchParams();

    if (search) params.set('search', search);
    if (branchId != null) params.set('branchId', String(branchId));

    const query = params.toString();
    return query ? `${this.baseUrl}/print?${query}` : `${this.baseUrl}/print`;
  }

  private mapDropdown(x: any): DropdownItem {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      name:
        x?.name ??
        x?.Name ??
        x?.text ??
        x?.Text ??
        x?.label ??
        x?.Label ??
        ''
    };
  }

  private mapRow(x: any): StockRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),

      prodId: this.toNullableNumber(x?.prodId ?? x?.ProdId ?? x?.productId ?? x?.ProductId),
      productName: x?.productName ?? x?.ProductName ?? null,

      itemType: x?.itemType ?? x?.ItemType ?? null,
      itemId: this.toNullableNumber(x?.itemId ?? x?.ItemId),
      itemName: x?.itemName ?? x?.ItemName ?? null,

      branchId: this.toNullableNumber(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? null,

      warehouseId: this.toNullableNumber(x?.warehouseId ?? x?.WarehouseId),
      warehouseName: x?.warehouseName ?? x?.WarehouseName ?? null,

      stockStage: x?.stockStage ?? x?.StockStage ?? null,
      transactionType: x?.transactionType ?? x?.TransactionType ?? null,

      openingQty: Number(x?.openingQty ?? x?.OpeningQty ?? 0),
      inQty: Number(x?.inQty ?? x?.InQty ?? 0),
      outQty: Number(x?.outQty ?? x?.OutQty ?? 0),
      qty: Number(x?.qty ?? x?.Qty ?? 0),
      currentQty: Number(x?.currentQty ?? x?.CurrentQty ?? 0),

      customerReservedStock: Number(x?.customerReservedStock ?? x?.CustomerReservedStock ?? 0),
      freeCompanyStock: Number(x?.freeCompanyStock ?? x?.FreeCompanyStock ?? 0),
      totalFinishedStock: Number(x?.totalFinishedStock ?? x?.TotalFinishedStock ?? x?.currentQty ?? x?.CurrentQty ?? 0),

      unitCost: Number(x?.unitCost ?? x?.UnitCost ?? 0),
      totalFinishedCost: Number(x?.totalFinishedCost ?? x?.TotalFinishedCost ?? x?.totalCost ?? x?.TotalCost ?? 0),
      totalCost: Number(x?.totalCost ?? x?.TotalCost ?? 0),

      reservedValue: Number(x?.reservedValue ?? x?.ReservedValue ?? 0),
      freeStockValue: Number(x?.freeStockValue ?? x?.FreeStockValue ?? 0),

      weightedAvgCost: Number(x?.weightedAvgCost ?? x?.WeightedAvgCost ?? 0),

      sellingPrice: Number(x?.sellingPrice ?? x?.SellingPrice ?? 0),
      expectedRevenue: Number(x?.expectedRevenue ?? x?.ExpectedRevenue ?? 0),
      estimatedProfit: Number(x?.estimatedProfit ?? x?.EstimatedProfit ?? 0),
      profitMarginPercent: Number(x?.profitMarginPercent ?? x?.ProfitMarginPercent ?? 0),

      stockStatus: x?.stockStatus ?? x?.StockStatus ?? null,
      stockValueStatus: x?.stockValueStatus ?? x?.StockValueStatus ?? null,
      stockValueClass: x?.stockValueClass ?? x?.StockValueClass ?? null,

      qcPostCount: Number(x?.qcPostCount ?? x?.QcPostCount ?? 0),
      dispatchCount: Number(x?.dispatchCount ?? x?.DispatchCount ?? 0),
      reverseCount: Number(x?.reverseCount ?? x?.ReverseCount ?? 0),
      movementCount: Number(x?.movementCount ?? x?.MovementCount ?? 0),
      
      rate: this.toNullableDecimal(x?.rate ?? x?.Rate),
      amount: this.toNullableDecimal(x?.amount ?? x?.Amount),
      lastKnownRate: this.toNullableDecimal(x?.lastKnownRate ?? x?.LastKnownRate),

      unit: x?.unit ?? x?.Unit ?? null,

      referenceType: x?.referenceType ?? x?.ReferenceType ?? null,
      referenceId: this.toNullableNumber(x?.referenceId ?? x?.ReferenceId),
      referenceNo: x?.referenceNo ?? x?.ReferenceNo ?? null,

      customerId: this.toNullableNumber(x?.customerId ?? x?.CustomerId),
      customerCode: x?.customerCode ?? x?.CustomerCode ?? null,
      customerName: x?.customerName ?? x?.CustomerName ?? null,

      stockDescription: x?.stockDescription ?? x?.StockDescription ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      active: Boolean(x?.active ?? x?.Active ?? false),
      deleted: Boolean(x?.deleted ?? x?.Deleted ?? false),

      createdBy: this.toNullableNumber(x?.createdBy ?? x?.CreatedBy),
      createdByName: x?.createdByName ?? x?.CreatedByName ?? null,
      updatedBy: this.toNullableNumber(x?.updatedBy ?? x?.UpdatedBy),

      recordTimeStamp: this.parseDotNetDate(x?.recordTimeStamp ?? x?.RecordTimeStamp),
      createdDate: this.parseDotNetDate(x?.createdDate ?? x?.CreatedDate),
      updatedDate: this.parseDotNetDate(x?.updatedDate ?? x?.UpdatedDate)
    };
  }

  private mapDetail(x: any): StockDetailViewModel {
    const base = this.mapRow(x) as StockDetailViewModel;

    const rawResv: any[] = Array.isArray(x?.customerReservations)
      ? x.customerReservations
      : Array.isArray(x?.CustomerReservations)
        ? x.CustomerReservations
        : [];

    base.customerReservations = rawResv.map(r => ({
      customerId:    Number(r?.customerId    ?? r?.CustomerId    ?? 0),
      customerCode:  r?.customerCode  ?? r?.CustomerCode  ?? null,
      customerName:  r?.customerName  ?? r?.CustomerName  ?? null,
      jobOrders:     r?.jobOrders     ?? r?.JobOrders     ?? null,
      reservedQty:   Number(r?.reservedQty   ?? r?.ReservedQty   ?? 0),
      dispatchedQty: Number(r?.dispatchedQty ?? r?.DispatchedQty ?? 0),
      remainingQty:  Number(r?.remainingQty  ?? r?.RemainingQty  ?? 0)
    }));

    base.lastMovementDate    = this.parseDotNetDate(x?.lastMovementDate ?? x?.LastMovementDate);
    base.firstMovementDate   = this.parseDotNetDate(x?.firstMovementDate ?? x?.FirstMovementDate);
    base.pendingDispatchCount = Number(x?.pendingDispatchCount ?? x?.PendingDispatchCount ?? 0);
    base.pendingDispatchQty   = Number(x?.pendingDispatchQty   ?? x?.PendingDispatchQty   ?? 0);

    return base;
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') return null;
    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }

  private toNullableDecimal(value: any): number | null {
    if (value === null || value === undefined || value === '') return null;
    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }

  private parseDotNetDate(value: any): string | null {
    if (!value) return null;

    if (value instanceof Date) {
      return this.formatDate(value);
    }

    const str = String(value).trim();
    const match = /\/Date\((\d+)\)\//.exec(str);

    if (match?.[1]) {
      return this.formatDate(new Date(Number(match[1])));
    }

    const normalDate = new Date(str);
    if (!isNaN(normalDate.getTime())) {
      return this.formatDate(normalDate);
    }

    return str;
  }

  private formatDate(d: Date): string {
    const day = String(d.getDate()).padStart(2, '0');
    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const month = monthNames[d.getMonth()];
    const year = d.getFullYear();

    let hours = d.getHours();
    const minutes = String(d.getMinutes()).padStart(2, '0');
    const ampm = hours >= 12 ? 'PM' : 'AM';

    hours = hours % 12 || 12;

    return `${day}-${month}-${year} ${hours}:${minutes} ${ampm}`;
  }
}
