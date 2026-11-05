import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { CompanyService } from '../services/adm-company.service';
import { CompanyForm, CompanyListViewModel, CompanyDetailViewModel } from '../models/company.dto';
import { Observable } from 'rxjs';
import { ApiResponse, ApiPagedResponse, ApiResponseList, ApiResponseData }
  from 'src/app/shared/models/api-response';


@Component({
  selector: 'app-adm-company',
  templateUrl: './adm-company.component.html',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, HttpClientModule],
  providers: [DatePipe]
})
export class AdmCompanyComponent implements OnInit {
  companies: CompanyListViewModel[] = [];
  selectedCompany: CompanyDetailViewModel | null = null;
  editingCompany: CompanyDetailViewModel | null = null;
  form!: FormGroup;
  selectedFile: File | null = null;
  logoPreview: string | null = null;

  search = '';
  page = 1;
  pageSize = 10;
  total = 0;
  totalPages = 0;
  paginationArray: number[] = [];
  showDeleted = false;

  @ViewChild('companyFormModal') companyFormModal!: ElementRef;
  @ViewChild('companyDetailModal') companyDetailModal!: ElementRef;
  @ViewChild('companyDeleteModal') companyDeleteModal!: ElementRef;

  constructor(private fb: FormBuilder, private companyService: CompanyService) { }

  ngOnInit(): void {
    this.buildForm();
    this.loadCompanies();
  }

  /** Build Reactive Form */
  buildForm() {
    this.form = this.fb.group({
      companyID: [null],
      companyName: ['', Validators.required],
      nickName: [''],
      address: [''],
      email: ['', Validators.email],
      contactNumber: [''],
      approval: ['Pending'],
      active: [true],
      allowExtraP: [false],
      deleted: [false],
      ownerName: [''],
      licenseKey: [''],
      ntn: [''],
      strn: [''],
      masterPassword: [''],
      cultureCode: ['en-PK'],
      calendarCode: ['Gregorian'],
      theme: ['light'],
      currencyDefault: ['PKR'],
      decimalsInAmount: [2],
      decimal_Points: [2],
      periodId: [null],
      showCurrencyName: [true],
      taxEnabled: [false],
      stopLogin: [false],
      logoFile: [null]
    });
  }

/** Load Companies with logo path resolved */
  loadCompanies() {
    this.companyService.listPaged(this.search, this.page, this.pageSize, this.showDeleted).subscribe({
      next: (res: any) => {
        this.companies = res.data || [];
        this.total = res.total || 0;
        this.totalPages = Math.ceil(this.total / this.pageSize);
        this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
      },
      error: (err) => {
        console.error('Error loading companies:', err);
        this.companies = [];
        this.total = 0;
      }
    });
  }

  resolveLogoPath(fileName: string): string {
    if (!fileName) return '';
    if (fileName.startsWith('http') || fileName.startsWith('/Content/CompanyLogo')) return fileName;
    return `/Content/CompanyLogo/${fileName}`;
  }


  /** Pagination / Search */
  onSearch() { this.page = 1; this.loadCompanies(); }
  clearSearch() { this.search = ''; this.page = 1; this.loadCompanies(); }
  onPageChange(pg: number) { if (pg < 1 || pg > this.totalPages) return; this.page = pg; this.loadCompanies(); }

  /** Toggle Show Deleted */
  toggleShowDeleted() {
    this.showDeleted = !this.showDeleted;
    this.page = 1;
    this.loadCompanies();
  }

  /** Modal Handling */
  openFormModal(company?: CompanyListViewModel) {
    if (company) {
      this.editingCompany = {
        // Base info
        companyID: company.companyID,
        companyName: company.companyName,
        nickName: company.nickName ?? '',
        email: company.email ?? '',
        contactNumber: company.contactNumber ?? '',
        address: company.address ?? '',
        active: company.active ?? true,
        deleted: company.deleted ?? false,
        approval: (company as any).approval ?? 'Pending',
        recordTime: company.recordTimeStamp ?? null,
        logo: null,
        ownerName: (company as any).ownerName ?? '',

        // Detailed fields
        licenseKey: (company as any).licenseKey ?? '',
        ntn: (company as any).ntn ?? '',
        strn: (company as any).strn ?? '',
        masterPassword: '',
        cultureCode: 'en-PK',
        calendarCode: 'Gregorian',
        theme: 'light',
        currencyDefault: 'PKR',
        decimalsInAmount: 2,
        decimal_Points: 2,
        periodId: null,
        showCurrencyName: true,
        taxEnabled: false,
        stopLogin: false,
        allowExtraP: false,
        operatingUserID: (company as any).operatingUserID ?? null,
        logoFile: '',

        // ✅ Audit fields
        createdBy: (company as any).createdBy ?? '',
        createdDate: (company as any).createdDate ?? null,
        updatedBy: (company as any).updatedBy ?? '',
        updatedDate: (company as any).updatedDate ?? null,

        // Optional fields for TS safety
        language: (company as any).language ?? null
      };

      // Patch form
      if (this.editingCompany) {
        this.form.patchValue(this.editingCompany);
      }

      // Logo preview
      this.logoPreview = company.logoPath ?? null;
    } else {
      // New company
      this.editingCompany = null;
      this.form.reset({
        active: true,
        approval: 'Pending',
        theme: 'light',
        deleted: false
      });
      this.logoPreview = null;
    }

    // Show modal
    (window as any).bootstrap.Modal.getOrCreateInstance(this.companyFormModal.nativeElement).show();
  }



  closeFormModal() {
    this.form.reset({
      active: true,
      approval: 'Pending',
      theme: 'light',
      cultureCode: 'en-PK',
      calendarCode: 'Gregorian',
      currencyDefault: 'PKR',
      decimalsInAmount: 2,
      decimal_Points: 2,
      showCurrencyName: true,
      taxEnabled: false,
      stopLogin: false
    });
    this.logoPreview = null;
    (window as any).bootstrap.Modal.getInstance(this.companyFormModal.nativeElement)?.hide();
  }

  /** Detail Modal */
  openDetail(company: CompanyListViewModel) {
    this.companyService.getDetail(company.companyID).subscribe({
      next: (res: CompanyDetailViewModel) => {
        this.selectedCompany = res;
        // Show modal
        (window as any).bootstrap.Modal.getOrCreateInstance(
          this.companyDetailModal.nativeElement
        ).show();
      },
      error: (err) => {
        console.error('Error loading company details:', err);
        alert('Failed to load company details.');
      }
    });
  }

  closeDetail() {
    this.selectedCompany = null;
    (window as any).bootstrap.Modal.getInstance(
      this.companyDetailModal.nativeElement
    )?.hide();
  }


  /** Delete Modal */
  confirmDelete(company: CompanyListViewModel) {
    this.selectedCompany = company as unknown as CompanyDetailViewModel;
    (window as any).bootstrap.Modal.getOrCreateInstance(this.companyDeleteModal.nativeElement).show();
  }

  // Close modal
  closeDeleteModal() {
    (window as any).bootstrap.Modal.getInstance(this.companyDeleteModal.nativeElement)?.hide();
  }

  // Execute delete
  deleteCompany(): void {
    if (!this.selectedCompany || !this.selectedCompany.companyID) return;

    const confirmDelete = confirm(`Are you sure you want to delete "${this.selectedCompany.companyName}"?`);
    if (!confirmDelete) return;

    this.companyService.delete(this.selectedCompany.companyID).subscribe({
      next: (res: any) => {
        if (res?.success) {
          alert(res.message || 'Company deleted successfully.');
          this.closeDeleteModal();

          // ✅ Remove from displayed list safely
          this.companies = this.companies.filter(c => c.companyID !== this.selectedCompany?.companyID);
          this.selectedCompany = null;

          // ✅ Update total and pagination safely
          this.total = Math.max(0, this.total - 1);
          this.totalPages = Math.ceil(this.total / this.pageSize);
          this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);

          // ✅ If the current page becomes empty (e.g., last item deleted)
          if (this.companies.length === 0 && this.page > 1) {
            this.page--;
            this.loadCompanies(); // reload previous page
          }
        } else {
          alert(res?.message || 'Failed to delete company.');
        }
      },
      error: (err) => {
        console.error('Delete error:', err);
        alert('Error deleting company. Please try again.');
      }
    });
  }


  /** File Change / Preview */
  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;
    this.selectedFile = input.files[0];

    const reader = new FileReader();
    reader.onload = () => this.logoPreview = reader.result as string;
    reader.readAsDataURL(this.selectedFile);
  }
  
  /** ───── Save (Create / Update) ───── */

  saveCompany(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const companyForm: CompanyForm = this.form.value;
    const formData = new FormData();

    // Append form fields safely
    Object.entries(companyForm).forEach(([key, val]) => {
      if (val !== null && val !== undefined) {
        // Handle file uploads
        if (val instanceof File || val instanceof Blob) {
          formData.append(key, val);
        } else {
          formData.append(key, `${val}`);
        }
      }
    });

    // Ensure companyID for updates
    if (this.editingCompany?.companyID) {
      formData.append('companyID', this.editingCompany.companyID.toString());
    }

    // Append selected logo file only when changed
    if (this.selectedFile) {
      formData.append('logoFile', this.selectedFile, this.selectedFile.name);
    }

    // Type fix: ApiResponse instead of ApiResponseBase
    const request$: Observable<ApiResponse> = this.editingCompany
      ? this.companyService.updateFormData(this.editingCompany.companyID!, formData)
      : this.companyService.createFormData(formData);

    request$.subscribe({
      next: (res) => {
        if (res?.success) {
          alert(res.message || 'Company saved successfully.');
          this.closeFormModal();
          this.loadCompanies();
        } else {
          alert(res?.message || 'Failed to save company.');
        }
      },
      error: (err) => {
        console.error('Error saving company:', err);
        alert('Error saving company.');
      }
    });
  }



  formatLogoPath(logo: string): string {
    if (!logo) return '';
    if (logo.startsWith('http') || logo.startsWith('data:image')) return logo;
    const fileName = logo.split('\\').pop()?.split('/').pop() ?? '';
    return `/Content/CompanyLogo/${fileName}`;
  }
    
  /** Update Approval */
  changeApproval(company: CompanyListViewModel, status: string) {
    this.companyService.updateApproval(company.companyID, status).subscribe({
      next: () => { alert('Approval updated successfully.'); this.loadCompanies(); },
      error: (err) => { console.error(err); alert('Error updating approval.'); }
    });
  }

  /** Export / Print */
  exportExcel() {
    this.companyService.exportExcel(this.companies as any); // ✅ type relax
  }
  exportPdf() {
    this.companyService.exportPdf(this.companies as any); // ✅ type relax
  }
  printTable() {
    const htmlContent = document.querySelector('.table-responsive')?.innerHTML || '';
    this.companyService.printTable(htmlContent);
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.companies.length > 0 && this.companies.every((r: any) => this.selectedIds.has(r.companyID));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.companies.forEach((r: any) => this.selectedIds.delete(r.companyID));
    else this.companies.forEach((r: any) => this.selectedIds.add(r.companyID));
  }
  clearSelection(): void { this.selectedIds.clear(); }

  private afterBulk(res: any, verb: string): void {
    const d = res?.data ?? {};
    const ok = Number(d.successCount || 0);
    const failed = Number(d.failureCount || 0);
    let msg = `${ok} ${verb}, ${failed} failed.`;
    if (failed > 0 && Array.isArray(d.failures)) {
      msg += '\n\nFailures:\n' + d.failures.slice(0, 10)
        .map((f: any) => `  • #${f.id} ${f.code}: ${f.reason}`).join('\n');
      if (d.failures.length > 10) msg += `\n  …and ${d.failures.length - 10} more.`;
    }
    alert(msg);
    this.clearSelection();
    this.loadCompanies();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} company(ies)?`)) return;
    this.bulkBusy = true;
    this.companyService.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} company(ies)?`)) return;
    this.bulkBusy = true;
    this.companyService.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} company(ies)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.companyService.bulkDelete(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  Audit log loader
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.companyService.getAuditLog(id).subscribe({
      next: (r: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(r?.data) ? r.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')       return 'bg-primary';
    if (a === 'EDITED')        return 'bg-warning text-dark';
    if (a === 'ACTIVATED')     return 'bg-success';
    if (a === 'DEACTIVATED')   return 'bg-secondary';
    if (a === 'DELETED')       return 'bg-danger';
    return 'bg-light text-dark';
  }
}
