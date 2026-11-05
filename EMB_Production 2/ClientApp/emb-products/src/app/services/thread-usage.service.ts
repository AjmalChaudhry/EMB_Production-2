// thread-usage.service.ts
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ThreadUsageDetailDto, ThreadUsageRowViewModel,ThreadUsageFormDto} from '../shared/models/thread-usage.dto';
import { map } from 'rxjs/operators';
import { ApiPagedResponse, ApiResponse, ApiResponseData, DropdownItem} from '../shared/models/api-response';


@Injectable({ providedIn: 'root' })

export class ThreadUsageService {

  private baseUrl = '/emb-products/thread-usage';
  private dropdownUrl = '/emb-products/dropdowns';

  constructor(private http: HttpClient) { }

  // =========================
  // LIST
  // =========================
  list(search: string, page: number, pageSize: number): Observable<any> {
    return this.http.get<ApiPagedResponse<ThreadUsageRowViewModel>>(
      `${this.baseUrl}/list`,
      { params: { search, page, pageSize } }
    );
  }


  // =========================
  // DETAIL
  // =========================
  detail(id: number): Observable<ApiResponseData<ThreadUsageDetailDto>> {
    return this.http.get<ApiResponseData<ThreadUsageDetailDto>>(
      `${this.baseUrl}/detail/${id}`
    );
  }


  // =========================
  // CREATE
  // =========================
  create(payload: ThreadUsageFormDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/create`,
      payload
    );
  }

  // =========================
  // UPDATE
  // =========================
  update(payload: ThreadUsageFormDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/update`,
      payload
    );
  }

  // =========================
  // DELETE
  // =========================
  delete(id: number): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/delete/${id}`,
      {}
    );
  }

  // =========================
  // DROPDOWNS
  // =========================
  getBranches(): Observable<DropdownItem[]> {
    return this.http.get<any>(`${this.dropdownUrl}/branches`).pipe(
      map(res => Array.isArray(res) ? res : (res?.data ?? []))
    );
  }

  getComponents(): Observable<DropdownItem[]> {
    return this.http.get<any>(`${this.dropdownUrl}/components`).pipe(
      map(res => Array.isArray(res) ? res : (res?.data ?? []))
    );
  }

  getThreads(): Observable<DropdownItem[]> {
    return this.http.get<any>(`${this.dropdownUrl}/threads`).pipe(
      map(res => Array.isArray(res) ? res : (res?.data ?? []))
    );
  }

  getNextCode(): Observable<ApiResponseData<{ code: number }>> {
    return this.http.get<ApiResponseData<{ code: number }>>(
      `${this.baseUrl}/next-code`
    );
  }
}
