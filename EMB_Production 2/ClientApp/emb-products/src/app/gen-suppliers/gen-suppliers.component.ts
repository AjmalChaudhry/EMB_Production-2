import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

import {
  SupplierRowViewModel,
  SupplierDetailViewModel,
  SupplierDto
} from '../shared/models/supplier.dto';

import { SupplierService } from '../services/supplier.service';
import { ApiDataService, ExportColumn } from '../shared/services/api-data.service';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-suppliers',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './gen-suppliers.component.html',
  styleUrls: ['./gen-suppliers.component.css']
})
export class GenSuppliersComponent implements OnInit {

  // ======================================================
  // DATA
  // ======================================================
  suppliers: SupplierRowViewModel[] = [];
  selectedSupplier: SupplierDetailViewModel | null = null;

  branchList: { id: number; name: string }[] = [];

  // Filters
  searchText = '';
  activeFilter = '';
  branchFilter = '';

  // Pagination
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  isLoading = false;

  // Modal mode
  modalMode: 'create' | 'edit' = 'create';

  // Supplier Form Model
  formSupplier: SupplierDto = this.createEmptySupplier();

  // ======================================================
  // MODAL HANDLES
  // ======================================================
  @ViewChild('supplierFormModal', { static: false }) supplierFormModal!: ElementRef<HTMLDivElement>;
  @ViewChild('supplierDetailModal', { static: false }) supplierDetailModal!: ElementRef<HTMLDivElement>;
  @ViewChild('deleteModal', { static: false }) deleteModal!: ElementRef<HTMLDivElement>;

  deleteId: number | null = null;

  constructor(
    private supplierSvc: SupplierService,
    private apiData: ApiDataService
  ) { }

  /** Columns shared by Excel / PDF / Print exports. The `field`
   *  values match keys on SupplierRowViewModel exactly. */
  exportColumns: ExportColumn[] = [
    { header: 'Name',    field: 'SupplierName' },
    { header: 'Contact', field: 'Contact'      },
    { header: 'Phone',   field: 'Phone'        },
    { header: 'Email',   field: 'Email'        },
    { header: 'Address', field: 'Address'      },
    { header: 'Branch',  field: 'BranchName'   },
    { header: 'Status',  field: 'StatusText'   }
  ];

  /** Augment rows with a friendly StatusText for export. The grid uses
   *  a coloured badge; exports need a plain string. */
  private exportRows(): any[] {
    return (this.suppliers || []).map(s => ({
      ...s,
      StatusText: s.Active ? 'Active' : 'Inactive'
    }));
  }

  // ======================================================
  // INIT
  // ======================================================
  ngOnInit(): void {
    this.loadBranches();
    this.loadSuppliers();
  }

  // ======================================================
  // HELPER: Empty Supplier DTO
  // ======================================================
  createEmptySupplier(): SupplierDto {
    return {
      id: 0,
      supplierName: '',
      contact: '',
      phone: '',
      email: '',
      address: '',
      branchId: null,
      active: true
    };
  }

  // ======================================================
  // PAGINATION
  // ======================================================
  setupPagination(): void {
    const totalPages = Math.ceil(this.totalItems / this.pageSize) || 1;
    this.pages = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  // ======================================================
  // BRANCH DROPDOWN
  // ======================================================
  loadBranches(): void {
    this.supplierSvc.getBranchesDropdown().subscribe({
      next: res => {
        this.branchList = res?.data ?? [];
      },
      error: () => {
        this.branchList = [];
      }
    });
  }

  // ======================================================
  // LOAD SUPPLIERS
  // ======================================================
  loadSuppliers(): void {
    this.isLoading = true;

    this.supplierSvc.getAll(
      this.searchText,
      this.page,
      this.pageSize,
      this.activeFilter,
      this.branchFilter
    ).subscribe({
      next: res => {
        this.isLoading = false;

        // List result ONLY contains "data" (array)
        this.suppliers = Array.isArray(res.data) ? res.data : [];

        // No totalItems / total exists → calculate manually
        this.totalItems = this.suppliers.length;

        this.setupPagination();
      },
      error: () => {
        this.isLoading = false;
        this.suppliers = [];
        this.totalItems = 0;
        this.setupPagination();
      }
    });
  }


  // ======================================================
  // FILTER ACTIONS
  // ======================================================
  onSearch(): void { this.page = 1; this.loadSuppliers(); }
  onActiveFilterChange(): void { this.page = 1; this.loadSuppliers(); }
  onBranchFilterChange(): void { this.page = 1; this.loadSuppliers(); }

  changePage(p: number): void {
    if (p < 1 || p > this.pages.length) return;
    this.page = p;
    this.loadSuppliers();
  }

  // ======================================================
  // CREATE
  // ======================================================
  openCreate(): void {
    this.modalMode = 'create';
    this.formSupplier = this.createEmptySupplier();

    setTimeout(() => this.openModal(this.supplierFormModal), 100);
  }

  saveCreate(): void {
    if (!this.formSupplier.supplierName.trim()) {
      alert("Supplier name is required.");
      return;
    }

    this.supplierSvc.create(this.formSupplier).subscribe({
      next: res => {
        alert(res.message || "Supplier created.");
        if (res.success) {
          this.closeModal(this.supplierFormModal);
          this.loadSuppliers();
        }
      }
    });
  }

  // ======================================================
  // EDIT
  // ======================================================
  openEdit(id: number): void {
    this.modalMode = 'edit';

    this.supplierSvc.getDetail(id).subscribe({
      next: res => {
        if (res.success && res.data) {
          const d = res.data;

          this.formSupplier = {
            id: d.Id,
            supplierName: d.SupplierName,
            contact: d.Contact,
            phone: d.Phone,
            email: d.Email,
            address: d.Address,
            branchId: d.BranchId,
            active: d.Active
          };

          setTimeout(() => this.openModal(this.supplierFormModal), 120);
        }
      }
    });
  }

  saveEdit(): void {
    if (!this.formSupplier.supplierName.trim()) {
      alert("Supplier name is required.");
      return;
    }

    this.supplierSvc.update(this.formSupplier).subscribe({
      next: res => {
        alert(res.message || "Supplier updated.");
        if (res.success) {
          this.closeModal(this.supplierFormModal);
          this.loadSuppliers();
        }
      }
    });
  }

  // ======================================================
  // DETAIL
  // ======================================================
  openDetail(id: number): void {
    this.supplierSvc.getDetail(id).subscribe({
      next: res => {
        if (res.success && res.data) {
          this.selectedSupplier = res.data;
          this.openModal(this.supplierDetailModal);
        }
      }
    });
  }

  closeDetailModal(): void {
    this.closeModal(this.supplierDetailModal);
  }

  // ======================================================
  // DELETE
  // ======================================================
  openDeleteModal(id: number): void {
    this.deleteId = id;
    this.openModal(this.deleteModal);
  }

  closeDeleteModal(): void {
    this.closeModal(this.deleteModal);
  }

  confirmDeleteConfirmed(): void {
    if (!this.deleteId) return;

    this.supplierSvc.delete(this.deleteId).subscribe({
      next: res => {
        alert(res.message || "Supplier deleted.");

        if (res.success) {
          this.closeDeleteModal();
          this.loadSuppliers();
        }
      }
    });
  }

  // ======================================================
  // MODAL CORE HANDLERS
  // ======================================================
  openModal(el: ElementRef | HTMLDivElement): void {
    const element = el instanceof ElementRef ? el.nativeElement : el;
    new bootstrap.Modal(element).show();
  }

  closeModal(el: ElementRef | HTMLDivElement): void {
    const element = el instanceof ElementRef ? el.nativeElement : el;
    const modal = bootstrap.Modal.getInstance(element);
    if (modal) modal.hide();
  }

  // ======================================================
  // EXPORTS
  // ======================================================
  exportToExcel(): void {
    this.apiData.exportToExcel(this.exportRows(), 'Suppliers', this.exportColumns);
  }
  exportToPdf(): void {
    this.apiData.exportToPDF(this.exportRows(), 'Suppliers', this.exportColumns);
  }
  printTable(): void {
    this.apiData.printTable(this.exportRows(), this.exportColumns, 'Suppliers');
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
    return this.suppliers.length > 0 && this.suppliers.every((r: any) => this.selectedIds.has(r.Id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.suppliers.forEach((r: any) => this.selectedIds.delete(r.Id));
    else this.suppliers.forEach((r: any) => this.selectedIds.add(r.Id));
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
    this.loadSuppliers();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} supplier(s)?`)) return;
    this.bulkBusy = true;
    this.supplierSvc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} supplier(s)?`)) return;
    this.bulkBusy = true;
    this.supplierSvc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} supplier(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.supplierSvc.bulkDelete(ids).subscribe({
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
    this.supplierSvc.getAuditLog(id).subscribe({
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
