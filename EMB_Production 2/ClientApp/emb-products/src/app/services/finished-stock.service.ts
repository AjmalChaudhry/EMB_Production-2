import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { FinishedStockRowDto, FinishedStockDetailDto } from '../shared/models/finished-stock.dto';

@Injectable({ providedIn: 'root' })
export class FinishedStockService {

  // ✔ must match RoutePrefix
  private baseUrl = '/emb-products/finished-stocks';

  constructor(private http: HttpClient) { }

  // =====================================================
  // LIST
  // =====================================================
  getAll(search: string = '') {
    return this.http.get<{
      success: boolean;
      message: string;
      data: FinishedStockRowDto[];
    }>(`${this.baseUrl}?search=${encodeURIComponent(search)}`);
  }

  // =====================================================
  // DETAIL
  // =====================================================
  getDetail(id: number) {
    return this.http.get<{
      success: boolean;
      message: string;
      data: FinishedStockDetailDto;
    }>(`${this.baseUrl}/detail/${id}`);
  }

  // =====================================================
  // RESERVE
  // =====================================================
  reserve(id: number, qty: number) {
    return this.http.post<{
      success: boolean;
      message: string;
    }>(`${this.baseUrl}/reserve`, {
      id,
      qty
    });
  }

  // =====================================================
  // RELEASE
  // =====================================================
  release(id: number) {
    return this.http.post<{
      success: boolean;
      message: string;
    }>(`${this.baseUrl}/release`, {
      id
    });
  }

  // =====================================================
  // ADJUST (ADMIN)
  // =====================================================
  adjust(id: number, qty: number, reason: string) {
    return this.http.post<{
      success: boolean;
      message: string;
    }>(`${this.baseUrl}/adjust`, {
      id,
      qty,
      reason
    });
  }
}
