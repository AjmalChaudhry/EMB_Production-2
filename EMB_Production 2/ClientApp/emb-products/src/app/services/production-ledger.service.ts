import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ProductionQcLedgerRowDto } from '../shared/models/production-ledger.dto';

@Injectable({ providedIn: 'root' })
export class ProductionLedgerService {
  private baseUrl = '/emb-products/production-ledger';

  constructor(private http: HttpClient) { }

  byProduction(productionId: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/by-production/${productionId}`);
  }

  recent(top: number = 50): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/recent?top=${top}`);
  }
}
