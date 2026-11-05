import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  MachineParameterDto,
  MachineParameterDetailViewModel
} from '../shared/models/machine-parameter.dto';

@Injectable({ providedIn: 'root' })
export class MachineParametersService {
  private baseUrl = '/emb-products/machine-parameters';

  constructor(private http: HttpClient) { }

  list(search: string, active: string, page: number, pageSize: number, machineId: number | null = null)
: Observable<any> {
    let params = new HttpParams()
      .set('search', search)
      .set('activeFilter', active)
      .set('page', page)
      .set('pageSize', pageSize);
    if (machineId && machineId > 0) params = params.set('machineId', String(machineId));

    return this.http.get<any>(`${this.baseUrl}/list`, { params });
  }

  getDetail(id: number) {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`);
  }

  // Phase R.5 - 360 profile
  getProfile(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }

  create(dto: MachineParameterDto) {
    return this.http.post<any>(`${this.baseUrl}/create`, dto);
  }

  update(dto: MachineParameterDto) {
    return this.http.post<any>(`${this.baseUrl}/update`, dto);
  }

  delete(id: number) {
    return this.http.post<any>(`${this.baseUrl}/delete/${id}`, {});
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-2 — audit timeline.
  // ───────────────────────────────────────────────────────────────
  getAuditLog(id: number): Observable<any> {
    return this.http.get<any>(`${this.baseUrl}/audit-log/${id}`);
  }

  // ───────────────────────────────────────────────────────────────
  //  Sprint-3 — bulk activate / deactivate / delete.
  // ───────────────────────────────────────────────────────────────
  bulkActivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-activate`, { ids });
  }
  bulkDeactivate(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-deactivate`, { ids });
  }
  bulkDelete(ids: number[]): Observable<any> {
    return this.http.post<any>(`${this.baseUrl}/bulk-delete`, { ids });
  }
}
