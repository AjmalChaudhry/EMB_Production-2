import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { from, Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { CompanyListViewModel, CompanyDetailViewModel } from '../models/company.dto';
import { ApiResponseData } from '../shared/models/api-response';

export interface Company {
  companyID: number;
  companyName: string;
  logo?: string;
  address?: string;
  parentId?: number;
  nickName?: string;
  email?: string;
  licenseKey?: string;
  contactNumber?: string;
  ntn?: string;
  strn?: string;
  masterPassword?: string;
  cultureCode?: string;
  calendarCode?: string;
  theme?: string;
  amountFormat?: number;
  decimalsInAmount?: number;
  decimal_Points?: number;
  currencyDefault?: string;
  showCurrencyName?: boolean;
  stopLogin?: boolean;
  taxEnabled?: boolean;
  periodId?: number;
  language?: string;
  recordTimeStamp?: string;
  approval?: string;
  operatingUserID?: number;
  deleted?: boolean;
  active?: boolean;
  createdBy?: number;
  createdDate?: string;
  updatedBy?: number;
  updatedDate?: string;
  allowExtraP?: boolean;
  allowRM?: boolean;
}

@Injectable({ providedIn: 'root' })
export class CompanyService {
  private baseUrl = 'https://localhost:44310/emb-products/companies';

  constructor(private http: HttpClient) { }

  // ─────────────── LIST PAGED ───────────────
  listPaged(
    search: string = '',
    page: number = 1,
    pageSize: number = 10,
    showDeleted: boolean = false ): Observable<{ data: CompanyListViewModel[]; total: number }> {
    const params = new HttpParams()
      .set('search', search)
      .set('page', page.toString())
      .set('pageSize', pageSize.toString())
      .set('showDeleted', showDeleted.toString());

    return this.http.get<{ data: any[]; total: number }>(`${this.baseUrl}/listPaged`, { params }).pipe(
      map(res => ({
        data: (res?.data || []).map((x: any): CompanyListViewModel => ({
          companyID: x.CompanyID ?? '',
          companyName: x.CompanyName ?? '',
          nickName: x.NickName ?? '',
          email: x.Email ?? '',
          contactNumber: x.ContactNumber ?? '',
          address: x.Address ?? '',
          approval: x.Approval ?? 'Pending',
          active: x.Active,
          deleted: x.Deleted,
          logoPath: x.LogoPath ? this.resolveLogoPath(x.LogoPath) : null
        })),
        total: res?.total ?? 0
      })),
      catchError(err => {
        console.error('CompanyService.listPaged error:', err);
        return of({ data: [], total: 0 });
      })
    );
  }


  resolveLogoPath(fileName: string): string {
    if (!fileName) return '';
    if (fileName.startsWith('http') || fileName.startsWith('/Content/CompanyLogo')) return fileName;
    return `/Content/CompanyLogo/${fileName}`;
  }

  // ──────────────── GET DETAIL ────────────────
  getDetail(id: number): Observable<CompanyDetailViewModel> {
    return this.http.get<{ success: boolean; data: any }>(`${this.baseUrl}/detail/${id}`).pipe(
      map(res => {
        const c = res.data;
        if (!c) return {} as CompanyDetailViewModel;

        // Helper: convert .NET JSON date format to JS date string
        const parseDate = (d: string | null) =>
          d ? new Date(parseInt(d.replace(/[^0-9]/g, ''), 10)).toLocaleString() : null;

        return {
          companyID: c.CompanyID,
          companyName: c.CompanyName,
          nickName: c.NickName,
          contactNumber: c.ContactNumber,
          email: c.Email,
          address: c.Address,
          approval: c.Approval === true || c.Approval === "true", // ensure boolean
          active: c.Active,
          deleted: c.Deleted,
          recordTime: parseDate(c.RecordTimeStamp),
          logo: c.LogoPath ? this.resolveLogoPath(c.LogoPath) : null,

          // Detailed fields
          allowExtraP: c.AllowExtraP,
          taxEnabled: c.TaxEnabled,
          stopLogin: c.StopLogin,
          showCurrencyName: c.ShowCurrencyName,
          operatingUserID: c.OperatingUserID,
          licenseKey: c.LicenseKey,
          ntn: c.NTN,
          strn: c.STRN,
          masterPassword: c.MasterPassword,
          cultureCode: c.CultureCode,
          calendarCode: c.CalendarCode,
          theme: c.Theme,
          periodId: c.PeriodId,
          currencyDefault: c.CurrencyDefault,
          decimalsInAmount: c.DecimalsInAmount,
          decimal_Points: c.Decimal_Points,
          language: c.Language,

          createdBy: c.CreatedBy,
          createdDate: parseDate(c.CreatedDate),
          updatedBy: c.UpdatedBy,
          updatedDate: parseDate(c.UpdatedDate),

          ownerName: c.OwnerName ?? null,
          logoFile: c.LogoPath ?? null,
        };
      }),
      catchError(err => {
        console.error('CompanyService.getDetail error:', err);
        return of({} as CompanyDetailViewModel);
      })
    );
  }


  // ─────────────── CREATE ───────────────
  createFormData(formData: FormData): Observable<ApiResponseData<CompanyDetailViewModel>> {
    return this.http.post<ApiResponseData<CompanyDetailViewModel>>(`${this.baseUrl}/create`, formData).pipe(
      map(res => {
        // Use type assertion to handle the response data safely
        if (res.data && !Array.isArray(res.data)) {
          const companyData = res.data as CompanyDetailViewModel;
          if (companyData.logoFile) {
            companyData.logo = this.resolveLogoPath(companyData.logoFile);
          }
        }
        return res;
      }),
      catchError(err => {
        console.error('CompanyService.createFormData error:', err);
        return of({ success: false, message: 'Error creating company.' } as ApiResponseData<CompanyDetailViewModel>);
      })
    );
  }

  // ─────────────── UPDATE ───────────────
  updateFormData(companyID: number, formData: FormData): Observable<ApiResponseData<CompanyDetailViewModel>> {
    return this.http.post<ApiResponseData<CompanyDetailViewModel>>(`${this.baseUrl}/update/${companyID}`, formData).pipe(
      map(res => {
        // Use type assertion and array check to handle the response safely
        if (res.data && !Array.isArray(res.data)) {
          const companyData = res.data as CompanyDetailViewModel;
          if (companyData.logoFile) {
            companyData.logo = this.resolveLogoPath(companyData.logoFile);
          }
        }
        return res;
      }),
      catchError(err => {
        console.error('CompanyService.updateFormData error:', err);
        return of({ success: false, message: 'Error updating company.' } as ApiResponseData<CompanyDetailViewModel>);
      })
    );
  }

  // ─────────────── DELETE ───────────────
  delete(companyID: number): Observable<any> {
    return this.http.delete(`${this.baseUrl}/api/delete/${companyID}`).pipe(
      catchError(err => {
        console.error('CompanyService.delete error:', err);
        return of({ success: false, message: 'Failed to delete company.' });
      })
    );
  }

  // ─────────────── UPDATE APPROVAL ───────────────
  updateApproval(companyID: number, approval: string): Observable<any> {
    return this.http.put<any>(`${this.baseUrl}/approval/${companyID}`, { approval }).pipe(
      catchError(err => {
        console.error('CompanyService.updateApproval error:', err);
        return of({ success: false, message: 'Failed to update approval.' });
      })
    );
  }
 
  // ─────────────── EXPORT / PRINT UTILITIES ───────────────
  exportExcel(companies: Company[]) {
    const ws: XLSX.WorkSheet = XLSX.utils.json_to_sheet(companies);
    const wb: XLSX.WorkBook = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Companies');
    XLSX.writeFile(wb, 'Companies.xlsx');
  }

  exportPdf(companies: Company[]) {
    const doc = new jsPDF();
    autoTable(doc, {
      head: [['ID', 'Name', 'Email', 'Contact', 'Status']],
      body: companies.map(c => [
        c.companyID,
        c.companyName,
        c.email || '-',
        c.contactNumber || '-',
        c.active ? 'Active' : 'Inactive',
      ]),
    });
    doc.save('Companies.pdf');
  }

  printTable(htmlContent: string) {
    const win = window.open('', '_blank');
    if (win) {
      win.document.write(htmlContent);
      win.document.close();
      win.print();
    }
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
