import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  PlanRow, PlanDetail, PlanSummary, PlanSavePayload, PlanItemSavePayload,
  EligibleJobOrder, MachineDropdownItem
} from '../shared/models/production-plan.dto';

interface ApiEnvelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class ProductionPlanService {
  private base = '/emb-products/production-plan';

  constructor(private http: HttpClient) {}

  list(params?: {
    status?: string; branchId?: number; year?: number; weekNo?: number;
    search?: string; showDeleted?: string;
    // Sprint-2 quick-jump from Job Orders → Related → Plan.
    jobOrderId?: number;
  }): Observable<ApiEnvelope<{ items: PlanRow[]; summary: PlanSummary }>> {
    const qs = new URLSearchParams();
    if (params?.status) qs.set('status', params.status);
    if (params?.branchId) qs.set('branchId', String(params.branchId));
    if (params?.year) qs.set('year', String(params.year));
    if (params?.weekNo) qs.set('weekNo', String(params.weekNo));
    if (params?.search) qs.set('search', params.search);
    if (params?.showDeleted) qs.set('showDeleted', params.showDeleted);
    if (params?.jobOrderId && params.jobOrderId > 0)
      qs.set('jobOrderId', String(params.jobOrderId));
    return this.http.get<ApiEnvelope<{ items: PlanRow[]; summary: PlanSummary }>>(
      `${this.base}/list${qs.toString() ? '?' + qs.toString() : ''}`);
  }

  detail(id: number): Observable<ApiEnvelope<PlanDetail>> {
    return this.http.get<ApiEnvelope<PlanDetail>>(`${this.base}/detail/${id}`);
  }

  nextCode(): Observable<ApiEnvelope<{ code: string }>> {
    return this.http.get<ApiEnvelope<{ code: string }>>(`${this.base}/next-code`);
  }

  eligibleJobOrders(): Observable<ApiEnvelope<EligibleJobOrder[]>> {
    return this.http.get<ApiEnvelope<EligibleJobOrder[]>>(`${this.base}/eligible-job-orders`);
  }

  machines(): Observable<ApiEnvelope<MachineDropdownItem[]>> {
    return this.http.get<ApiEnvelope<MachineDropdownItem[]>>(`${this.base}/machines-dropdown`);
  }

  create(payload: PlanSavePayload): Observable<ApiEnvelope<{ id: number; planCode: string }>> {
    return this.http.post<ApiEnvelope<{ id: number; planCode: string }>>(`${this.base}/create`, payload);
  }

  update(payload: PlanSavePayload): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/update`, payload);
  }

  addItem(payload: PlanItemSavePayload): Observable<ApiEnvelope<{ id: number }>> {
    return this.http.post<ApiEnvelope<{ id: number }>>(`${this.base}/add-item`, payload);
  }

  updateItem(payload: PlanItemSavePayload): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/update-item`, payload);
  }

  removeItem(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/remove-item/${id}`, {});
  }

  approve(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/approve/${id}`, {});
  }

  /** Undo an APPROVED plan: roll back generated WOs and put the plan
   *  back into DRAFT so it can be edited. Server-side blocks if any
   *  generated WO has already been posted. */
  reverse(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/reverse/${id}`, {});
  }

  close(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/close/${id}`, {});
  }

  remove(id: number): Observable<ApiEnvelope<unknown>> {
    return this.http.post<ApiEnvelope<unknown>>(`${this.base}/delete/${id}`, {});
  }

  /** Sprint-2: deep-copy a plan into a new DRAFT for next ISO week. Server
   *  shifts every item's Planned dates +7 days, resets Status/GeneratedWorkOrderId
   *  so the clone is unscheduled. Refuses if a plan already exists for the
   *  target (branch, year, week) so the unique constraint is enforced
   *  with a friendly message instead of a 500. */
  cloneNextWeek(id: number): Observable<ApiEnvelope<{
    id: number; planCode: string; sourceId: number; sourceCode: string;
    year: number; weekNo: number; itemsCloned: number;
  }>> {
    return this.http.post<ApiEnvelope<{
      id: number; planCode: string; sourceId: number; sourceCode: string;
      year: number; weekNo: number; itemsCloned: number;
    }>>(`${this.base}/clone-next-week/${id}`, {});
  }

  /** Sprint-3 bulk actions. Each returns a per-id success/failure report
   *  so the UI can show "3 approved, 2 failed (because capacity over-
   *  allocated …)". */
  bulkApprove(ids: number[]): Observable<ApiEnvelope<{
    successCount: number; failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  }>> {
    return this.http.post<ApiEnvelope<{
      successCount: number; failureCount: number;
      failures: Array<{ id: number; code: string; reason: string }>;
    }>>(`${this.base}/bulk-approve`, { ids });
  }

  bulkClose(ids: number[]): Observable<ApiEnvelope<{
    successCount: number; failureCount: number;
    failures: Array<{ id: number; code: string; reason: string }>;
  }>> {
    return this.http.post<ApiEnvelope<{
      successCount: number; failureCount: number;
      failures: Array<{ id: number; code: string; reason: string }>;
    }>>(`${this.base}/bulk-close`, { ids });
  }

  /** Sprint-4 audit timeline. Returns newest-first list of state-changing
   *  actions on the plan (Created, ItemAdded, Approved, Reversed, etc). */
  getAuditLog(id: number): Observable<ApiEnvelope<PlanAuditEntry[]>> {
    return this.http.get<ApiEnvelope<PlanAuditEntry[]>>(`${this.base}/audit-log/${id}`);
  }
}

/** Sprint-4: shape of one audit log entry. */
export interface PlanAuditEntry {
  id: number;
  planId: number;
  action: string;
  changedBy: number | null;
  changedByName: string | null;
  changedDate: string | null;
  changedDateText: string | null;
  oldValue: string | null;
  newValue: string | null;
  detail: string | null;
}
