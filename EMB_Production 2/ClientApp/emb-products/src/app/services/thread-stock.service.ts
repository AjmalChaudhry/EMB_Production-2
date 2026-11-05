import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponseData } from '../shared/models/api-response';

export interface ThreadStockBalance {
  threadId: number;
  code: string;
  name: string;
  color: string;
  material: string;
  unit: string;
  unitCost: number;
  active: boolean;
  totalIn: number;
  totalOut: number;
  balance: number;
  lastRate: number;
  balanceValue: number;
}

export interface ThreadLedgerRow {
  id: number;
  date: string;
  dateText: string;
  transactionType: string;
  referenceType: string;
  referenceNo: string;
  referenceId: number | null;
  inQty: number;
  outQty: number;
  balance: number;
  unit: string;
  rate: number;
  amount: number;
  remarks: string;
}

export interface ThreadHistory {
  threadId: number;
  code: string;
  name: string;
  color: string;
  unit: string;
  branchId: number;
  totalIn: number;
  totalOut: number;
  balance: number;
  ledger: ThreadLedgerRow[];
}

@Injectable({ providedIn: 'root' })
export class ThreadStockService {
  private baseUrl = '/emb-products/thread-stock';

  constructor(private http: HttpClient) { }

  balance(branchId?: number, search = ''): Observable<ApiResponseData<ThreadStockBalance[]>> {
    let params = new HttpParams().set('search', search ?? '');
    if (branchId) params = params.set('branchId', String(branchId));
    return this.http.get<ApiResponseData<ThreadStockBalance[]>>(
      `${this.baseUrl}/balance`,
      { params }
    );
  }

  history(threadId: number, branchId?: number): Observable<ApiResponseData<ThreadHistory>> {
    let params = new HttpParams();
    if (branchId) params = params.set('branchId', String(branchId));
    return this.http.get<ApiResponseData<ThreadHistory>>(
      `${this.baseUrl}/history/${threadId}`,
      { params }
    );
  }
}
