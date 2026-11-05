import { Component, OnInit, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule, DecimalPipe, CurrencyPipe } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { FormsModule } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { finalize, catchError } from 'rxjs/operators';

import { GenCategoryService } from '../services/gen-category.service';
import {
  CategoryListViewModel,
  CategoryDetailViewModel
} from '../shared/models/category.dto';
import { ApiDataService, ExportColumn } from '../shared/services/api-data.service';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-category',
  standalone: true,
  templateUrl: './gen-category.component.html',
  styleUrls: ['./gen-category.component.css'],
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    HttpClientModule,
    DecimalPipe,
    CurrencyPipe
  ]
})
export class GenCategoryComponent implements OnInit {

  @ViewChild('categoryDetailModal', { static: true }) categoryDetailModal!: ElementRef;
  @ViewChild('categoryFormModal', { static: true }) categoryFormModal!: ElementRef;
  @ViewChild('deleteCategoryModal', { static: true }) deleteCategoryModal!: ElementRef;

  // ================= DATA =================
  categories: CategoryListViewModel[] = [];
  branches: { Id: number; Name: string }[] = [];
  brands: { Id: number; Name: string; Code?: string }[] = [];

  selectedCategory: CategoryDetailViewModel | null = null;
  editingCategory: CategoryListViewModel | null = null;
  deleteTarget: CategoryListViewModel | null = null;

  // ================= FORM =================
  categoryForm!: FormGroup;

  // ================= FILTER / PAGING =================
  searchText = '';
  activeFilter = '';
  brandFilter: number | null = null;
  branchFilter: number | null = null;

  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 0;
  paginationArray: number[] = [];

  loading = false;

  // Phase R.12 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctBrands: 0,
    distinctBranches: 0,
    totalValue: 0,
    avgPrice: 0,
    maxPrice: 0,
    minPrice: 0,
    byBrand: [] as Array<{ brand: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ================= SERVICES =================
  private fb = inject(FormBuilder);
  private categorySvc = inject(GenCategoryService);
  private apiData = inject(ApiDataService);

  // ====================================================
  // INIT
  // ====================================================
  ngOnInit(): void {
    this.initForm();
    this.loadLookupsAndCategories();
  }

  // ====================================================
  // FORM
  // ====================================================
  private initForm(): void {
    this.categoryForm = this.fb.group({
      id: [null],
      name: ['', Validators.required],
      code: [{ value: 'Auto-generated', disabled: true }],
      description: [''],

      brandId: [null, Validators.required],
      branchId: [null, Validators.required],

      quantity: [''],
      length: [''],
      weight: [''],
      price: [null],

      active: [true]
    });
  }

  // ====================================================
  // LOAD LOOKUPS + LIST
  // ====================================================
  loadLookupsAndCategories(): void {
    this.loading = true;

    forkJoin({
      lookups: this.categorySvc.getLookups().pipe(
        catchError(() =>
          of({ success: false, data: { branches: [], brands: [] } })
        )
      ),

      pageData: this.categorySvc
        .getAll(this.searchText, this.page, this.pageSize, this.activeFilter, this.brandFilter, this.branchFilter)
        .pipe(
          catchError(() =>
            of({ success: false, data: { items: [], totalItems: 0 } })
          )
        )
    })
      .pipe(finalize(() => (this.loading = false)))
      .subscribe(({ lookups, pageData }) => {
        const lookupData: any = lookups?.data ?? {};
        this.branches = Array.isArray(lookupData.branches) ? lookupData.branches : [];
        this.brands = Array.isArray(lookupData.brands)
          ? lookupData.brands
          : Array.isArray(lookupData.brandList)
            ? lookupData.brandList
            : [];

        const paged: any = pageData?.data ?? { items: [], totalItems: 0 };

        this.categories = Array.isArray(paged.items) ? paged.items : [];
        this.totalItems = Number(paged.totalItems ?? 0);

        const sm = paged.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctBrands: Number(sm.distinctBrands ?? 0),
            distinctBranches: Number(sm.distinctBranches ?? 0),
            totalValue: Number(sm.totalValue ?? 0),
            avgPrice: Number(sm.avgPrice ?? 0),
            maxPrice: Number(sm.maxPrice ?? 0),
            minPrice: Number(sm.minPrice ?? 0),
            byBrand: Array.isArray(sm.byBrand) ? sm.byBrand : []
          };
        }

        this.setupPagination();
      });
  }

  // Phase R.12 - filter helpers + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadLookupsAndCategories(); }
  clearFiltersR(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.brandFilter = null;
    this.branchFilter = null;
    this.page = 1;
    this.loadLookupsAndCategories();
  }
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.categorySvc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('Category profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  // ====================================================
  // PAGINATION
  // ====================================================
  private setupPagination(): void {
    this.totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  onPageChange(pg: number): void {
    if (pg < 1 || pg > this.totalPages || pg === this.page) return;
    this.page = pg;
    this.loadLookupsAndCategories();
  }

  // ====================================================
  // SEARCH / FILTER
  // ====================================================
  onSearch(): void {
    this.page = 1;
    this.loadLookupsAndCategories();
  }

  refresh(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.page = 1;
    this.loadLookupsAndCategories();
  }

  onActiveFilterChange(): void {
    this.page = 1;
    this.loadLookupsAndCategories();
  }

  // ====================================================
  // MODALS (BOOTSTRAP)
  // ====================================================
  private getModal(ref: ElementRef): any {
    let modal = bootstrap.Modal.getInstance(ref.nativeElement);
    if (!modal) {
      modal = new bootstrap.Modal(ref.nativeElement, {
        backdrop: 'static',
        keyboard: false
      });
    }
    return modal;
  }

  private showModal(ref: ElementRef): void {
    this.getModal(ref).show();
  }

  private hideModal(ref: ElementRef): void {
    const modal = bootstrap.Modal.getInstance(ref.nativeElement);
    if (modal) {
      modal.hide();
    }
  }

  closeFormModal(): void {
    this.hideModal(this.categoryFormModal);
  }

  closeDeleteModal(): void {
    this.hideModal(this.deleteCategoryModal);
  }

  closeDetail(): void {
    this.hideModal(this.categoryDetailModal);
    this.selectedCategory = null;
  }

  // ====================================================
  // CREATE / EDIT
  // ====================================================
  openCreate(): void {
    this.editingCategory = null;

    this.categoryForm.reset({
      id: null,
      name: '',
      code: 'Auto-generated',
      description: '',
      brandId: null,
      branchId: null,
      quantity: '',
      length: '',
      weight: '',
      price: null,
      active: true
    });

    this.categorySvc.getNextCode().subscribe({
      next: (res: any) => {
        const code =
          res?.data?.code ??
          res?.data?.Code ??
          'Auto-generated';

        this.categoryForm.patchValue({ code });
        this.showModal(this.categoryFormModal);
      },
      error: () => {
        this.categoryForm.patchValue({ code: 'Auto-generated' });
        this.showModal(this.categoryFormModal);
      }
    });
  }

  openEdit(cat: CategoryListViewModel): void {
    this.editingCategory = cat;

    this.categorySvc.getDetail(cat.id).subscribe({
      next: (res: any) => {
        const d = res?.data;

        if (!res?.success || !d) {
          alert('Failed to load category detail.');
          return;
        }

        this.categoryForm.reset({
          id: d?.id ?? cat.id ?? null,
          name: d?.name ?? cat.name ?? '',
          code: d?.code ?? cat.code ?? '',
          description: d?.description ?? cat.description ?? '',
          brandId: d?.brandId ?? cat.brandId ?? null,
          branchId: d?.branchId ?? cat.branchId ?? null,
          quantity: d?.quantity ?? cat.quantity ?? '',
          length: d?.length ?? cat.length ?? '',
          weight: d?.weight ?? cat.weight ?? '',
          price: d?.price ?? cat.price ?? null,
          active: d?.active ?? cat.active ?? true
        });

        this.showModal(this.categoryFormModal);
      },
      error: (err) => {
        console.error('Edit detail load failed:', err);
        alert('Failed to load category for edit.');
      }
    });
  }

  saveCategory(): void {
    if (this.categoryForm.invalid || this.loading) {
      this.categoryForm.markAllAsTouched();
      return;
    }

    this.loading = true;

    const raw = this.categoryForm.getRawValue();
    const fd = new FormData();

    fd.append('name', raw.name ?? '');
    fd.append('description', raw.description ?? '');
    fd.append('brandId', raw.brandId ?? '');
    fd.append('branchId', raw.branchId ?? '');
    fd.append('quantity', raw.quantity ?? '');
    fd.append('length', raw.length ?? '');
    fd.append('weight', raw.weight ?? '');
    fd.append('price', raw.price ?? '');
    fd.append('active', raw.active ? 'true' : 'false');

    if (this.editingCategory) {
      fd.append('code', raw.code ?? '');
    }

    const obs$ = this.editingCategory
      ? this.categorySvc.update(raw.id || this.editingCategory.id, fd)
      : this.categorySvc.create(fd);

    obs$.pipe(
      finalize(() => (this.loading = false))
    ).subscribe({
      next: (res: any) => {
        if (res?.success ?? res?.Success) {
          alert(res?.message ?? res?.Message ?? 'Category saved.');
          this.closeFormModal();
          this.loadLookupsAndCategories();
        } else {
          alert(res?.message ?? res?.Message ?? 'Save failed.');
        }
      },
      error: (err) => {
        console.error('Save category error:', err);
        alert('Error saving category.');
      }
    });
  }

  // ====================================================
  // DELETE
  // ====================================================
  confirmDelete(c: CategoryListViewModel): void {
    this.deleteTarget = c;
    this.showModal(this.deleteCategoryModal);
  }

  deleteCategory(): void {
    if (!this.deleteTarget) return;

    this.categorySvc.delete(this.deleteTarget.id).subscribe({
      next: (res: any) => {
        if (res?.success) {
          alert(res.message || 'Category deleted.');
          this.closeDeleteModal();
          this.deleteTarget = null;
          this.loadLookupsAndCategories();
        } else {
          alert(res?.message || 'Delete failed.');
        }
      },
      error: () => alert('Error deleting category.')
    });
  }

  // ====================================================
  // DETAIL
  // ====================================================
  openDetail(id: number): void {
    this.categorySvc.getDetail(id).subscribe({
      next: (res: any) => {
        if (!res?.success || !res?.data) return;
        this.selectedCategory = res.data;
        this.showModal(this.categoryDetailModal);
      },
      error: () => alert('Failed to load category detail.')
    });
  }

  // ====================================================
  // EXPORTS
  // ====================================================
  exportCategoriesToExcel(): void {
    this.apiData.exportToExcel(this.categories, 'CategoriesList', this.exportCols());
  }

  exportCategoriesToPDF(): void {
    this.apiData.exportToPDF(this.categories, 'CategoriesList', this.exportCols());
  }

  printCategories(): void {
    this.apiData.printTable(this.categories, this.exportCols(), 'Categories List');
  }

  private exportCols(): ExportColumn[] {
    return [
      { header: 'Code', field: 'code' },
      { header: 'Name', field: 'name' },
      { header: 'Brand', field: 'brandName' },
      { header: 'Branch', field: 'branchName' },
      { header: 'Qty', field: 'quantity' },
      { header: 'Length', field: 'length' },
      { header: 'Weight', field: 'weight' },
      { header: 'Price', field: 'price' },
      { header: 'Status', field: 'active' }
    ];
  }
}
