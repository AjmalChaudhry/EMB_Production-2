import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, ApiResponseData } from '../shared/models/api-response';
import {DispatchItemRowViewModel,DispatchItemDetailViewModel,DispatchStockLookupViewModel
} from '../shared/models/dispatch-item.dto';
import { DispatchItemDto } from '../shared/models/dispatch.dto';

export type SaleType = 'CUSTOMER_DEMAND' | 'DIRECT_SALE';

@Injectable({ providedIn: 'root' })
export class DispatchItemsService {
  private readonly baseUrl = '/emb-products/dispatch-items';

  constructor(private http: HttpClient) { }

  list(dispatchId: number): Observable<ApiResponseData<{
    dispatchIsPosted: boolean;
    dispatchIsReversed: boolean;
    saleType: SaleType;
    items: DispatchItemRowViewModel[];
  }>> {
    return this.http.get<ApiResponseData<{
      dispatchIsPosted: boolean;
      dispatchIsReversed: boolean;
      saleType: SaleType;
      items: DispatchItemRowViewModel[];
    }>>(`${this.baseUrl}/items/${dispatchId}`);
  }

  itemsDetail(dispatchId: number): Observable<ApiResponseData<DispatchItemRowViewModel[]>> {
    return this.http.get<ApiResponseData<DispatchItemRowViewModel[]>>(
      `${this.baseUrl}/items-detail/${dispatchId}`
    );
  }

  detail(id: number): Observable<ApiResponseData<DispatchItemDetailViewModel>> {
    return this.http.get<ApiResponseData<DispatchItemDetailViewModel>>(
      `${this.baseUrl}/detail/${id}`
    );
  }

  create(dto: DispatchItemDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(dto: DispatchItemDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  getAvailableProducts(
    saleType: SaleType = 'CUSTOMER_DEMAND',
    branchId: number | null = null,
    customerId: number | null = null,
    search = ''
  ): Observable<ApiResponseData<DispatchStockLookupViewModel[]>> {
    let params = new HttpParams()
      .set('saleType', saleType)
      .set('search', search || '');

    if (branchId != null && branchId > 0) {
      params = params.set('branchId', String(branchId));
    }

    if (customerId != null && customerId > 0) {
      params = params.set('customerId', String(customerId));
    }

    return this.http.get<ApiResponseData<DispatchStockLookupViewModel[]>>(
      `${this.baseUrl}/available-products`,
      { params }
    );
  }

  exportPdf(dispatchId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/export-pdf/${dispatchId}`, {
      responseType: 'blob'
    });
  }
}
