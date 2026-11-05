import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class GenBrandService {

  private baseUrl = '/emb-products/brands';
  private branchUrl = '/emb-products/dropdowns/branches';

  constructor(private http: HttpClient) { }

  // Existing methods…
  getAll(search: string, page: number, pageSize: number, activeFilter: string = '', branchId: number | null = null) {
    const params: any = { search, page, pageSize };
    if (activeFilter) params.activeFilter = activeFilter;
    if (branchId && branchId > 0) params.branchId = branchId;
    return this.http.get<any>(`${this.baseUrl}/list`, { params }).pipe(
      map(res => {
        const items = res?.data?.items ?? [];

        const mapped = items.map((x: any) => ({
          id: x.Id,
          code: x.Code,
          brandName: x.BrandName,
          description: x.Description,
          branchId: x.BranchId,
          branchName: x.BranchName,
          active: x.Active
        }));

        return {
          success: true,
          data: {
            items: mapped,
            summary: res?.data?.summary ?? null,
            totalItems: res?.data?.totalItems ?? mapped.length
          }
        };
      })
    );
  }

  // Phase R.11 - 360 profile
  getProfile(id: number) {
    return this.http.get<any>(`${this.baseUrl}/profile/${id}`);
  }



  getDetail(id: number) {
    return this.http.get<any>(`${this.baseUrl}/detail/${id}`);
  }

  create(formData: FormData) {
    return this.http.post<any>(`${this.baseUrl}/create`, formData);
  }

  update(id: number, formData: FormData) {
    return this.http.post<any>(`${this.baseUrl}/update/${id}`, formData);
  }

  delete(id: number) {
    return this.http.post<any>(`${this.baseUrl}/delete/${id}`, {});
  }

  // 🔥 NEW: DIRECT BRANCH DROPDOWN just for Brand module
  getBranches(): Observable<any[]> {
    return this.http.get<any>('/emb-products/dropdowns/branches').pipe(
      map(res => {
        return Array.isArray(res?.data) ? res.data : [];
      })
    );
  }



}
