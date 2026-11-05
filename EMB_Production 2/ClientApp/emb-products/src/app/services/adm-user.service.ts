import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

import {
  ApiResponseData,
  ApiPagedResponse
} from '../shared/models/api-response';

import {
  UserRowViewModel,
  UserDetailViewModel
} from '../shared/models/user.dto';

@Injectable({ providedIn: 'root' })
export class UsersService {

  private baseUrl = '/emb-products/users';

  constructor(private http: HttpClient) { }

  // ============ LIST (Phase R: filters + summary) ============
  getAll(opts: {
    search?: string;
    page?: number;
    pageSize?: number;
    roleId?: number | null;
    branchId?: number | null;
    companyId?: number | null;
    designationId?: number | null;
    activeFilter?: string;     // 'active' | 'inactive' | ''
    dashboardFilter?: string;  // 'yes' | 'no' | ''
  } = {}) {
    const params: any = {
      search: opts.search ?? '',
      page: opts.page ?? 1,
      pageSize: opts.pageSize ?? 10
    };
    if (opts.roleId)         params.roleId = String(opts.roleId);
    if (opts.branchId)       params.branchId = String(opts.branchId);
    if (opts.companyId)      params.companyId = String(opts.companyId);
    if (opts.designationId)  params.designationId = String(opts.designationId);
    if (opts.activeFilter)   params.activeFilter = opts.activeFilter;
    if (opts.dashboardFilter) params.dashboardFilter = opts.dashboardFilter;

    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map(res => {
        const items = res?.data?.items ?? [];

        const mapped: UserRowViewModel[] = items.map((u: any) => ({
          Id: u.Id,
          EmployeeId: u.EmployeeId,
          UserName: u.UserName,

          FirstName: u.FirstName,
          LastName: u.LastName,
          FullName: u.FullName ?? `${u.FirstName ?? ''} ${u.LastName ?? ''}`.trim(),

          EmailAddress: u.EmailAddress,
          MobilePrimary: u.MobilePrimary,
          Landline: u.Landline,

          Password: undefined, // never expose in list

          BranchId: u.BranchId,
          BranchName: u.BranchName,

          RoleId: u.RoleId,
          RoleName: u.RoleName,

          CompanyId: u.CompanyId,
          CompanyName: u.CompanyName,

          DesignationId: u.DesignationId,
          DesignationName: u.DesignationName,

          Active: u.Active,
          DashboardAccess: u.DashboardAccess,
          Approvel: u.Approvel,

          FacialRecognition: u.FacialRecognition,

          CreatedBy: u.CreatedBy,
          CreatedDate: u.CreatedDate,
          UpdatedBy: u.UpdatedBy,
          UpdatedDate: u.UpdatedDate,

          // computed labels
          ActiveLabel: u.Active ? 'Active' : 'Inactive',
          ApprovelLabel: u.Approvel === 'true' ? 'Approved' : 'Pending'
        }));

        return {
          success: true,
          data: {
            items: mapped,
            totalItems: res?.data?.totalItems ?? mapped.length,
            summary: res?.data?.summary ?? null
          }
        };
      })
    );
  }

  // ============ 360 PROFILE ============
  getProfile(id: number) {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }


  // ============ DETAIL ============
  getDetail(id: number) {
    return this.http.get<ApiResponseData<UserDetailViewModel>>(`${this.baseUrl}/detail/${id}`);
  }

  // ============ CREATE ============
  create(formData: FormData) {
    return this.http.post<ApiResponseData<UserDetailViewModel>>(`${this.baseUrl}/create`, formData);
  }

  // ============ UPDATE ============
  update(id: number, formData: FormData) {
    return this.http.post<ApiResponseData<UserDetailViewModel>>(`${this.baseUrl}/update/${id}`, formData);
  }

  // ============ DELETE ============
  delete(id: number) {
    return this.http.post<ApiResponseData<null>>(`${this.baseUrl}/delete/${id}`, {});
  }

  /** ---------------- Dropdowns ---------------- */

  getBranches() {
    return this.http.get<any>(`${this.baseUrl}/branches`).pipe(
      map(res => {
        if (res?.data && Array.isArray(res.data)) return res.data;
        if (Array.isArray(res)) return res;
        return [];
      })
    );
  }

  getRoles() {
    return this.http.get<any>(`${this.baseUrl}/roles`).pipe(
      map(res => {
        if (res?.data && Array.isArray(res.data)) return res.data;
        if (Array.isArray(res)) return res;
        return [];
      })
    );
  }

  getCompanies() {
    return this.http.get<any>(`${this.baseUrl}/companies`).pipe(
      map(res => {
        if (res?.data && Array.isArray(res.data)) return res.data;
        if (Array.isArray(res)) return res;
        return [];
      })
    );
  }

  getDesignations() {
    return this.http.get<any>(`${this.baseUrl}/designations`).pipe(
      map(res => {
        if (res?.data && Array.isArray(res.data)) return res.data;
        if (Array.isArray(res)) return res;
        return [];
      })
    );
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
