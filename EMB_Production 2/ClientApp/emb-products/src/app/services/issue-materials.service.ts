import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, ApiResponseData } from '../shared/models/api-response';

export interface IssueSuggestion {
  threadId: number;
  code: string;
  name: string;
  color: string;
  unit: string;
  unitCost: number;
  perPiece: number;
  suggestedQty: number;
  currentBalance: number;
}

export interface IssueSuggestResponse {
  workOrderId: number;
  workOrderCode: string;
  designId: number;
  orderQty: number;
  branchId: number;
  suggestions: IssueSuggestion[];
}

export interface IssueItemDto {
  threadId: number;
  qty: number;
  rate?: number | null;
  remarks?: string | null;
}

export interface IssueDto {
  workOrderId: number;
  remarks?: string | null;
  items: IssueItemDto[];
}

export interface IssueListRow {
  issueCode: string;
  workOrderId: number | null;
  workOrderCode: string;
  itemCount: number;
  totalQty: number;
  totalAmount: number;
  issueDateText: string;
}

export interface IssueDetail {
  issueCode: string;
  workOrderId: number | null;
  workOrderCode: string | null;
  issueDateText: string;
  createdByEmployeeId: number | null;
  items: Array<{
    id: number;
    rawItemId: number;
    /** Always set — falls back to '-' if Gen_RawItems lookup failed. */
    rawItemName: string;
    threadId: number | null;
    /** Null for fabric/accessory rows (thread-only metadata). */
    threadCode: string | null;
    threadName: string | null;
    threadColor: string | null;
    unit: string;
    qty: number;
    rate: number;
    amount: number;
    remarks: string;
  }>;
  totalQty: number;
  totalAmount: number;
}

@Injectable({ providedIn: 'root' })
export class IssueMaterialsService {
  private baseUrl = '/emb-products/issue-materials';

  constructor(private http: HttpClient) { }

  suggest(workOrderId: number): Observable<ApiResponseData<IssueSuggestResponse>> {
    return this.http.get<ApiResponseData<IssueSuggestResponse>>(
      `${this.baseUrl}/suggest/${workOrderId}`
    );
  }

  save(dto: IssueDto): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(`${this.baseUrl}/save`, dto);
  }

  list(workOrderId?: number, top = 200): Observable<ApiResponseData<IssueListRow[]>> {
    let params = new HttpParams().set('top', String(top));
    if (workOrderId) params = params.set('workOrderId', String(workOrderId));
    return this.http.get<ApiResponseData<IssueListRow[]>>(`${this.baseUrl}/list`, { params });
  }

  detail(issueCode: string): Observable<ApiResponseData<IssueDetail>> {
    return this.http.get<ApiResponseData<IssueDetail>>(
      `${this.baseUrl}/detail/${encodeURIComponent(issueCode)}`
    );
  }

  reverse(issueCode: string): Observable<ApiResponse> {
    return this.http.post<ApiResponse>(
      `${this.baseUrl}/reverse/${encodeURIComponent(issueCode)}`,
      {}
    );
  }
}
