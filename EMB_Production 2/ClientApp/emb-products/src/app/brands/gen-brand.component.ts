import { Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Modal } from 'bootstrap';
import { DropdownDataService } from '../shared/services/dropdown-data.service';
import { GenBrandService } from '../services/gen-brand.service';
import { BrandListViewModel, BrandDetailViewModel } from '../shared/models/brand.dto';
import { ApiDataService } from '../shared/services/api-data.service';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-brand',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, HttpClientModule],
  templateUrl: './gen-brand.component.html',
  styleUrls: ['./gen-brand.component.css']
})
export class GenBrandComponent implements OnInit {

  @ViewChild('brandFormModal') brandFormModal!: ElementRef;
  @ViewChild('brandDetailModal') brandDetailModal!: ElementRef;

  private formModal!: Modal;
  private detailModal!: Modal;

  // ========================= DATA =========================
  brands: BrandListViewModel[] = [];
  branches: any[] = [];

  selectedBrand: BrandDetailViewModel | null = null;
  editingBrand: BrandListViewModel | null = null;

  // ========================= FORM =========================
  brandForm!: FormGroup;

  // ========================= FILTERS & PAGINATION =========================
  searchText = '';
  activeFilter = '';
  branchFilter: number | null = null;
  page = 1;
  pageSize = 10;
  totalItems = 0;
  paginationArray: number[] = [];
  loading = false;

  // Phase R.11 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctBranches: 0,
    byBranch: [] as Array<{ branch: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ========================= SERVICES =========================
  private fb = inject(FormBuilder);
  private brandSvc = inject(GenBrandService);
  private dropdowns = inject(DropdownDataService);
  private apiData = inject(ApiDataService);

  // ================================================================
  // INITIALIZATION
  // ================================================================
  ngOnInit(): void {
    this.initForm();
    this.loadDropdownsAndBrands();
  }

  private initForm(): void {
    this.brandForm = this.fb.group({
      code: ['', Validators.required],
      brandName: ['', Validators.required],
      description: [''],
      branchId: [null, Validators.required],
      active: [true]
    });
  }

  // ================================================================
  // LOAD DROPDOWNS + LIST
  // ================================================================
  loadDropdownsAndBrands(): void {
    forkJoin({
      branches: this.brandSvc.getBranches(),
      brands: this.brandSvc.getAll(this.searchText, this.page, this.pageSize, this.activeFilter, this.branchFilter)
    }).subscribe(({ branches, brands }) => {
      this.branches = branches ?? [];
      this.brands = brands?.data?.items ?? [];
      this.totalItems = brands?.data?.totalItems ?? 0;
      const sm = brands?.data?.summary ?? null;
      if (sm) {
        this.summary = {
          count: Number(sm.count ?? 0),
          activeCount: Number(sm.activeCount ?? 0),
          inactiveCount: Number(sm.inactiveCount ?? 0),
          distinctBranches: Number(sm.distinctBranches ?? 0),
          byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : []
        };
      }
      this.setupPagination();
    });
  }

  // Phase R.11 - filter helpers + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadDropdownsAndBrands(); }
  clearFiltersR(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.branchFilter = null;
    this.page = 1;
    this.loadDropdownsAndBrands();
  }
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.brandSvc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('Brand profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }



  // ================================================================
  // PAGINATION
  // ================================================================
  private setupPagination(): void {
    const totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.paginationArray = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  onPageChange(pg: number): void {
    if (pg < 1 || pg === this.page) return;
    this.page = pg;
    this.loadDropdownsAndBrands();
  }

  ngAfterViewInit(): void {
    this.formModal = new Modal(this.brandFormModal.nativeElement);
    this.detailModal = new Modal(this.brandDetailModal.nativeElement);
  }

  // ================================================================
  // MODAL HANDLING
  // ================================================================
  openCreate(): void {
    this.editingBrand = null;
    this.brandForm.reset({ active: true });
    new bootstrap.Modal(document.getElementById('brandFormModal')).show();
  }

  // FIXED — Ensure branchId is filled in edit form
  openEdit(brand: BrandListViewModel): void {
    this.editingBrand = brand;

    this.brandForm.patchValue({
      code: brand.code,
      brandName: brand.brandName,
      description: brand.description,
      branchId: brand.branchId,
      active: brand.active
    });

    new bootstrap.Modal(document.getElementById('brandFormModal')).show();
  }
  closeFormModal(): void {
    this.formModal.hide();
  }


  // ================================================================
  // CREATE / UPDATE BRAND
  // ================================================================
  saveBrand(): void {
    if (this.brandForm.invalid || this.loading) return;

    const form = this.brandForm.value;
    this.loading = true;

    const formData = new FormData();
    Object.keys(form).forEach(key => formData.append(key, form[key] ?? ''));

    const request$ = this.editingBrand
      ? this.brandSvc.update(this.editingBrand.id, formData)
      : this.brandSvc.create(formData);

    request$.subscribe({
      next: res => {
        this.loading = false;
        alert(res.message || 'Saved.');
        this.loadDropdownsAndBrands();
        bootstrap.Modal.getInstance(document.getElementById('brandFormModal'))?.hide();
      },
      error: err => {
        this.loading = false;
        console.error(err);
        alert('❌ Error saving brand.');
      }
    });
  }

  // ================================================================
  // DELETE BRAND
  // ================================================================
  deleteTarget: BrandListViewModel | null = null;

  confirmDelete(brand: BrandListViewModel): void {
    this.deleteTarget = brand;
    new bootstrap.Modal(document.getElementById('deleteBrandModal')).show();
  }

  deleteBrand(): void {
    if (!this.deleteTarget) return;

    this.brandSvc.delete(this.deleteTarget.id).subscribe({
      next: res => {
        alert(res.message || 'Deleted.');
        this.loadDropdownsAndBrands();
        bootstrap.Modal.getInstance(document.getElementById('deleteBrandModal'))?.hide();
      },
      error: () => alert('❌ Error deleting brand.')
    });
  }

  // ================================================================
  // DETAILS MODAL
  // ================================================================
  openDetail(id: number): void {
    this.brandSvc.getDetail(id).subscribe({
      next: res => {
        if (!res.success || !res.data) {
          alert('Brand not found.');
          return;
        }

        const b = res.data;

        this.selectedBrand = {
          ...b,

          Code: b.Code ?? '-',
          BrandName: b.BrandName ?? '-',
          Description: b.Description ?? '-',
          BranchName: b.BranchName ?? '-',

          Active: b.Active ?? false,
          Deleted: b.Deleted ?? false,

          CreatedByName: b.CreatedByName ?? '-',
          CreatedDateFormatted: b.CreatedDateFormatted ?? '-',

          UpdatedByName: b.UpdatedByName ?? '-',
          UpdatedDateFormatted: b.UpdatedDateFormatted ?? '-',

          // ⭐ THE FIX
          RecordTimeStamp: b.RecordTimeStampFormatted ?? '-'
        };

        new bootstrap.Modal(this.brandDetailModal.nativeElement).show();
      },
      error: err => {
        console.error('❌ Detail load error', err);
        alert('Failed to load brand details.');
      }
    });
  }


  closeDetail(): void {
    bootstrap.Modal.getInstance(this.brandDetailModal.nativeElement)?.hide();
    this.selectedBrand = null;
  }

  // ================================================================
  // EXPORTS
  // ================================================================
  exportBrandsToExcel(): void {
    const cols = [
      { header: 'Code', field: 'code' },
      { header: 'Brand Name', field: 'brandName' },
      { header: 'Branch', field: 'branchName' },
      { header: 'Active', field: 'active' }
    ];

    this.apiData.exportToExcel(this.brands, 'BrandsList', cols);
  }

  exportBrandsToPDF(): void {
    const cols = [
      { header: 'Code', field: 'code' },
      { header: 'Brand Name', field: 'brandName' },
      { header: 'Branch', field: 'branchName' },
      { header: 'Active', field: 'active' }
    ];

    this.apiData.exportToPDF(this.brands, 'BrandsList', cols);
  }

  printBrands(): void {
    const cols = [
      { header: 'Code', field: 'code' },
      { header: 'Brand Name', field: 'brandName' },
      { header: 'Branch', field: 'branchName' },
      { header: 'Active', field: 'active' }
    ];

    this.apiData.printTable(this.brands, cols, 'Brands List');
  }
}
