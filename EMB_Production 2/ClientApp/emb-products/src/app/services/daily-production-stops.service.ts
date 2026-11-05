import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  DailyProductionStopRowViewModel,
  DailyProductionStopDetailViewModel,
  DailyProductionStopInputDto,
  DowntimeReasonStatVm
} from '../shared/models/daily-production-stops.dto';

import { ApiResponse, ApiResponseData, ApiPagedResponse } from '../shared/models/api-response';
import { DropdownItem } from '../shared/models/api-response';

@Injectable({ providedIn: 'root' })
export class DailyProductionStopsService {
  private baseUrl = '/emb-products/daily-production-stops';

  constructor(private http: HttpClient) { }

  // Dropdown production list (from DropdownsController)
  getProductionDropdown(): Observable<{ data: DropdownItem[] }> {
    return this.http.get<{ data: DropdownItem[] }>('/emb-products/dropdowns/daily-productions');
  }

  getPaged(productionId: number | null, search: string, page: number, pageSize: number)
    : Observable<ApiPagedResponse<DailyProductionStopRowViewModel>> {

    let params = new HttpParams()
      .set('page', String(page))
      .set('pageSize', String(pageSize))
      .set('search', search || '');

    if (productionId && productionId > 0) {
      params = params.set('productionId', String(productionId));
    }

    return this.http.get<ApiPagedResponse<DailyProductionStopRowViewModel>>(`${this.baseUrl}/list`, { params });
  }

  getDetail(id: number): Observable<ApiResponseData<DailyProductionStopDetailViewModel>> {
    return this.http.get<ApiResponseData<DailyProductionStopDetailViewModel>>(`${this.baseUrl}/detail/${id}`);
  }

  create(dto: DailyProductionStopInputDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/create`, dto);
  }

  update(id: number, dto: DailyProductionStopInputDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/update/${id}`, dto);
  }

  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/delete/${id}`, {});
  }

  analytics(productionId: number | null): Observable<ApiResponseData<{ totalMinutes: number; reasons: DowntimeReasonStatVm[] }>> {
    let params = new HttpParams();
    if (productionId && productionId > 0) params = params.set('productionId', String(productionId));
    return this.http.get<ApiResponseData<{ totalMinutes: number; reasons: DowntimeReasonStatVm[] }>>(`${this.baseUrl}/analytics`, { params });
  }
  
  exportExcel(productionId: number | null = null, search: string = ''): Observable<Blob> {
    let params = new HttpParams();
    if (productionId && productionId > 0) params = params.set('productionId', String(productionId));
    if (search) params = params.set('search', search);
    return this.http.get(
      `${this.baseUrl}/export-excel`,
      { responseType: 'blob', params }
    );
  }

  
  exportPdf(): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/export-pdf`,
      { responseType: 'blob' }
    );
  }

  
  printView(): string {
    return `${this.baseUrl}/print`;
  }
}

