import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  BomAuditSummaryResponse, BomAuditDetailResponse
} from '../shared/models/bom-audit.dto';

interface Envelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class BomAuditService {
  private readonly base = '/emb-products/bom-audit';

  constructor(private http: HttpClient) {}

  summary(params: {
    fromDate?: string | null;
    toDate?: string | null;
    designId?: number | null;
    branchId?: number | null;
    tolerancePct?: number;     // 0..1 (default 0.20 server-side)
  } = {}): Observable<Envelope<BomAuditSummaryResponse>> {
    let p = new HttpParams();
    if (params.fromDate)     p = p.set('fromDate', params.fromDate);
    if (params.toDate)       p = p.set('toDate', params.toDate);
    if (params.designId)     p = p.set('designId', String(params.designId));
    if (params.branchId)     p = p.set('branchId', String(params.branchId));
    if (params.tolerancePct !== undefined && params.tolerancePct !== null) {
      p = p.set('tolerancePct', String(params.tolerancePct));
    }
    return this.http.get<Envelope<BomAuditSummaryResponse>>(`${this.base}/summary`, { params: p });
  }

  detail(designId: number, params: {
    fromDate?: string | null;
    toDate?: string | null;
    branchId?: number | null;
    tolerancePct?: number;
  } = {}): Observable<Envelope<BomAuditDetailResponse>> {
    let p = new HttpParams();
    if (params.fromDate)     p = p.set('fromDate', params.fromDate);
    if (params.toDate)       p = p.set('toDate', params.toDate);
    if (params.branchId)     p = p.set('branchId', String(params.branchId));
    if (params.tolerancePct !== undefined && params.tolerancePct !== null) {
      p = p.set('tolerancePct', String(params.tolerancePct));
    }
    return this.http.get<Envelope<BomAuditDetailResponse>>(`${this.base}/detail/${designId}`, { params: p });
  }
}
