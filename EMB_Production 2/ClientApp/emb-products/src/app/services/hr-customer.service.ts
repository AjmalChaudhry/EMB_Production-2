import { map } from 'rxjs/operators';
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiPagedResponse, ApiResponseData } from '../shared/models/api-response';
import { CustomerListViewModel, CustomerDetailViewModel } from '../shared/models/customer.dto';



@Injectable({ providedIn: 'root' })
export class HrCustomerService {

  private baseUrl = '/emb-products/customers';

  constructor(private http: HttpClient) { }

  getAll(
    search: string,
    page: number,
    pageSize: number,
    active: string
  ): Observable<ApiPagedResponse<CustomerListViewModel>> {
    return this.http.get<ApiPagedResponse<CustomerListViewModel>>(
      `${this.baseUrl}/list`,
      {
        params: {
          search,
          page,
          pageSize,
          active
        }
      }
    );
  }

  getDetail(id: number): Observable<CustomerDetailViewModel> {
    return this.http
      .get<ApiResponseData<any>>(`${this.baseUrl}/detail/${id}`)
      .pipe(
        map(res => {
          const d = res.data;

          return {
            id: d.id,
            customerId: d.customerId,
            name: d.name,

            ntnNumber: d.ntnNumber,
            strnNumber: d.strnNumber,
            customerAddress: d.customerAddress,

            customerCity: d.customerCity,
            customerCountry: d.customerCountry,
            customerEmail: d.customerEmail,
            customerEmail2: d.customerEmail2,

            customerPhone: d.customerPhone,
            customerPhone2: d.customerPhone2,
            customerMobile: d.customerMobile,
            customerFax: d.customerFax,

            customerCreditLimitCap: d.customerCreditLimitCap,
            customerInvoicePeriod: d.customerInvoicePeriod,

            branchId: d.branchId,
            branchName: d.branchName,

            active: d.active,
            deleted: d.deleted,

            createdBy: d.createdBy,
            createdDateFormatted: d.createdDateFormatted,

            updatedBy: d.updatedBy,
            updatedDateFormatted: d.updatedDateFormatted,

            recordTimeStamp: d.recordTimeStamp
          } as CustomerDetailViewModel;
        })
      );
  }




  // ---------- CREATE ----------
  create(fd: FormData): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/create`,
      fd
    );
  }

  // ---------- UPDATE ----------
  update(id: number, fd: FormData): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(
      `${this.baseUrl}/update/${id}`,
      fd
    );
  }

  delete(id: number): Observable<ApiResponseData<any>> {
    return this.http.post<ApiResponseData<any>>(`${this.baseUrl}/delete/${id}`, {});
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
