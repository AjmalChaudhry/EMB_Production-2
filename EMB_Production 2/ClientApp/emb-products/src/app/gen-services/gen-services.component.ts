import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as bootstrap from 'bootstrap';

import {
  ServiceDto,
  ServiceRowViewModel,
  ServiceDetailViewModel
} from '../shared/models/gen-services.dto';

import { GenServicesService } from '../services/gen-services.service';

interface DropdownResponse<T> {
  data: T[];
}

interface DropdownItem {
  id: number;
  name?: string;
  code?: string;
}

@Component({
  selector: 'app-gen-services',
  standalone: true,
  templateUrl: './gen-services.component.html',
  styleUrls: ['./gen-services.component.css'],
  imports: [CommonModule, FormsModule]
})
export class GenServicesComponent implements OnInit {

  // -------------------------------------------------
  // TABLE DATA
  // -------------------------------------------------
  rows: ServiceRowViewModel[] = [];
  totalItems = 0;

  page = 1;
  pageSize = 10;
  pages: number[] = [];

  // -------------------------------------------------
  // FILTERS
  // -------------------------------------------------
  searchText = '';
  activeFilter = '';
  branchFilter: number | null = null;
  serviceTypeFilter: number | null = null;

  // -------------------------------------------------
  // DROPDOWN LISTS
  // -------------------------------------------------
  serviceTypeList: DropdownItem[] = [];
  branchList: DropdownItem[] = [];
  machineList: DropdownItem[] = [];

  // Phase R.7 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctTypes: 0,
    distinctMachines: 0,
    distinctBranches: 0,
    avgRate: 0,
    avgMax: 0,
    avgMin: 0,
    byBranch: [] as Array<{ branch: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // -------------------------------------------------
  // FORM / MODALS
  // -------------------------------------------------
  formModel: ServiceDto = this.getEmptyModel();
  modalMode: 'create' | 'edit' = 'create';

  detailModel: ServiceDetailViewModel | null = null;
  deleteId: number | null = null;

  isSaving = false;
  isLoading = false;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(private svc: GenServicesService) { }

  ngOnInit(): void {
    this.loadServiceTypes();
    this.loadBranches();
    this.loadMachines();
    this.loadList();
  }

  // -------------------------------------------------
  // EMPTY FORM MODEL
  // -------------------------------------------------
  getEmptyModel(): ServiceDto {
    return {
      Id: 0,
      ServiceTypeId: null,
      ServiceName: '',
      ServiceRateCharge: null,
      MachineDevice: null,
      MaxRange: '',
      MaxCharges: null,
      MinCharges: null,
      Field1: null,
      Field2: null,
      BranchId: null,
      Active: true
    };
  }

  // -------------------------------------------------
  // PAGINATION
  // -------------------------------------------------
  setupPagination(): void {
    const totalPages = Math.ceil(this.totalItems / this.pageSize);
    this.pages = totalPages > 0
      ? Array.from({ length: totalPages }, (_, i) => i + 1)
      : [];
  }

  goToPage(p: number): void {
    if (p < 1) return;

    const totalPages = Math.ceil(this.totalItems / this.pageSize);
    if (p > totalPages) return;

    this.page = p;
    this.loadList();
  }

  // -------------------------------------------------
  // DROPDOWNS
  // -------------------------------------------------
  loadServiceTypes(): void {
    this.svc.getServiceTypes().subscribe({
      next: (r: DropdownResponse<DropdownItem>) => {
        this.serviceTypeList = r?.data || [];
      },
      error: err => {
        console.error('Service types load failed', err);
      }
    });
  }

  loadBranches(): void {
    this.svc.getBranches().subscribe({
      next: (r: DropdownResponse<DropdownItem>) => {
        this.branchList = r?.data || [];
      },
      error: err => {
        console.error('Branch load failed', err);
      }
    });
  }

  loadMachines(): void {
    this.svc.getMachines().subscribe({
      next: (r: DropdownResponse<DropdownItem>) => {
        this.machineList = r?.data || [];
      },
      error: err => {
        console.error('Machine load failed', err);
      }
    });
  }

  // -------------------------------------------------
  // LIST
  // -------------------------------------------------
  loadList(): void {
    this.isLoading = true;

    this.svc.list({
      page: this.page,
      pageSize: this.pageSize,
      search: this.searchText || '',
      activeFilter: this.activeFilter || '',
      branchFilter: this.branchFilter,
      serviceTypeFilter: this.serviceTypeFilter
    }).subscribe({
      next: (res: any) => {
        this.rows = res?.data?.items || [];
        this.totalItems = res?.data?.totalItems || 0;
        const sm = res?.data?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctTypes: Number(sm.distinctTypes ?? 0),
            distinctMachines: Number(sm.distinctMachines ?? 0),
            distinctBranches: Number(sm.distinctBranches ?? 0),
            avgRate: Number(sm.avgRate ?? 0),
            avgMax: Number(sm.avgMax ?? 0),
            avgMin: Number(sm.avgMin ?? 0),
            byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : []
          };
        }
        this.setupPagination();
        this.isLoading = false;
      },
      error: err => {
        console.error('Load list failed', err);
        this.rows = [];
        this.totalItems = 0;
        this.setupPagination();
        this.isLoading = false;
      }
    });
  }

  onSearch(): void {
    this.page = 1;
    this.loadList();
  }

  onResetFilters(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.branchFilter = null;
    this.serviceTypeFilter = null;
    this.page = 1;
    this.loadList();
  }

  // Phase R.7 - profile drawer
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.svc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('Service profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  // -------------------------------------------------
  // CREATE
  // -------------------------------------------------
  openCreate(): void {
    this.modalMode = 'create';
    this.formModel = this.getEmptyModel();
    this.openFormModal();
  }

  saveCreate(): void {
    if (!this.validateForm()) return;

    this.isSaving = true;

    this.svc.create(this.formModel).subscribe({
      next: (res) => {
        this.isSaving = false;

        if (res?.success) {
          alert(res.message || 'Service created successfully');
          this.closeFormModal();
          this.loadList();
        } else {
          alert(res?.message || 'Unable to save service');
        }
      },
      error: (err) => {
        this.isSaving = false;
        console.error('Create failed', err);
        alert('Error creating service');
      }
    });
  }

  // -------------------------------------------------
  // EDIT
  // -------------------------------------------------
  openEdit(id: number): void {
    this.modalMode = 'edit';

    this.svc.detail(id).subscribe({
      next: (r: any) => {
        this.formModel = {
          Id: r?.data?.Id ?? 0,
          ServiceTypeId: r?.data?.ServiceTypeId ?? null,
          ServiceName: r?.data?.ServiceName ?? '',
          ServiceRateCharge: r?.data?.ServiceRateCharge ?? null,
          MachineDevice: r?.data?.MachineDevice ?? null,
          MaxRange: r?.data?.MaxRange ?? '',
          MaxCharges: r?.data?.MaxCharges ?? null,
          MinCharges: r?.data?.MinCharges ?? null,
          Field1: r?.data?.Field1 ?? null,
          Field2: r?.data?.Field2 ?? null,
          BranchId: r?.data?.BranchId ?? null,
          Active: r?.data?.Active ?? true
        };

        this.openFormModal();
      },
      error: err => {
        console.error('Load detail failed', err);
        alert('Unable to load service detail');
      }
    });
  }

  saveEdit(): void {
    if (!this.validateForm()) return;

    this.isSaving = true;

    this.svc.update(this.formModel).subscribe({
      next: (res) => {
        this.isSaving = false;

        if (res?.success) {
          alert(res.message || 'Service updated successfully');
          this.closeFormModal();
          this.loadList();
        } else {
          alert(res?.message || 'Unable to update service');
        }
      },
      error: (err) => {
        this.isSaving = false;
        console.error('Update failed', err);
        alert('Error updating service');
      }
    });
  }

  // -------------------------------------------------
  // DETAIL
  // -------------------------------------------------
  openDetail(id: number): void {
    this.svc.detail(id).subscribe({
      next: (r: any) => {
        this.detailModel = r?.data || null;
        this.openDetailModal();
      },
      error: err => {
        console.error('Detail failed', err);
        alert('Unable to load detail');
      }
    });
  }

  // -------------------------------------------------
  // DELETE
  // -------------------------------------------------
  openDelete(id: number): void {
    this.deleteId = id;
    this.openDeleteModal();
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe({
      next: (res: any) => {
        alert(res?.message || 'Deleted successfully');
        this.closeDeleteModal();
        this.deleteId = null;
        this.loadList();
      },
      error: err => {
        console.error('Delete failed', err);
        alert('Delete failed');
      }
    });
  }

  // -------------------------------------------------
  // MODAL HELPERS
  // -------------------------------------------------
  openFormModal(): void {
    const modal = bootstrap.Modal.getOrCreateInstance(this.formModal.nativeElement);
    modal.show();
  }

  closeFormModal(): void {
    const modal = bootstrap.Modal.getInstance(this.formModal.nativeElement);
    modal?.hide();
  }

  openDetailModal(): void {
    const modal = bootstrap.Modal.getOrCreateInstance(this.detailModal.nativeElement);
    modal.show();
  }

  closeDetailModal(): void {
    const modal = bootstrap.Modal.getInstance(this.detailModal.nativeElement);
    modal?.hide();
  }

  openDeleteModal(): void {
    const modal = bootstrap.Modal.getOrCreateInstance(this.deleteModal.nativeElement);
    modal.show();
  }

  closeDeleteModal(): void {
    const modal = bootstrap.Modal.getInstance(this.deleteModal.nativeElement);
    modal?.hide();
  }

  // -------------------------------------------------
  // VALIDATION
  // -------------------------------------------------
  validateForm(): boolean {
    if (!this.formModel.ServiceTypeId) {
      alert('Service Type is required.');
      return false;
    }

    if (!this.formModel.ServiceName || !this.formModel.ServiceName.trim()) {
      alert('Service Name is required.');
      return false;
    }

    if (this.formModel.ServiceRateCharge == null || this.formModel.ServiceRateCharge < 0) {
      alert('Rate Charge is required.');
      return false;
    }

    if (!this.formModel.BranchId) {
      alert('Branch is required.');
      return false;
    }

    return true;
  }

  // -------------------------------------------------
  // UTIL FUNCTIONS
  // -------------------------------------------------
  getMachineName(id: number | null | undefined): string {
    if (!id) return '-';

    const item = this.machineList.find(m => m.id === id);
    return item?.code || item?.name || '-';
  }

  formatDate(raw: string | null | undefined): string {
    if (!raw) return '-';

    try {
      if (raw.startsWith('/Date(')) {
        const ms = parseInt(raw.replace('/Date(', '').replace(')/', ''), 10);
        if (!isNaN(ms)) {
          return new Date(ms).toLocaleString();
        }
      }

      const dt = new Date(raw);
      if (!isNaN(dt.getTime())) {
        return dt.toLocaleString();
      }

      return raw;
    } catch {
      return raw;
    }
  }

  // -------------------------------------------------
  // EXPORT CSV
  // -------------------------------------------------
  exportToCsv(): void {
    if (!this.rows.length) {
      alert('No data to export');
      return;
    }

    const header = `"ServiceName","Type","Rate","Machine","Branch","Status"`;

    const body = this.rows.map(s =>
      `"${s.ServiceName ?? ''}",` +
      `"${s.ServiceTypeName ?? ''}",` +
      `"${s.ServiceRateCharge ?? ''}",` +
      `"${this.getMachineName(s.MachineDevice)}",` +
      `"${s.BranchName ?? ''}",` +
      `"${s.Active ? 'Active' : 'Inactive'}"`
    );

    const csv = [header, ...body].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = 'services.csv';
    a.click();

    URL.revokeObjectURL(url);
  }

  // -------------------------------------------------
  // PRINT
  // -------------------------------------------------
  printTable(): void {
    const table = document.querySelector('table');

    if (!table) {
      alert('No table to print');
      return;
    }

    const printWindow = window.open('', '', 'width=900,height=700');
    if (!printWindow) return;

    printWindow.document.write(`
      <html>
        <head>
          <title>Services</title>
          <link rel="stylesheet" href="/Content/bootstrap.min.css">
          <style>
            body { padding: 20px; font-family: Arial, sans-serif; }
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #ccc; padding: 8px; font-size: 12px; }
            th { background: #f5f5f5; }
          </style>
        </head>
        <body>
          <h3>Services</h3>
          ${table.outerHTML}
        </body>
      </html>
    `);

    printWindow.document.close();
    printWindow.focus();
    printWindow.print();
  }

  exportToPdf(): void {
    this.printTable();
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
    return this.rows.length > 0 && this.rows.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.rows.forEach((r: any) => this.selectedIds.delete(r.id));
    else this.rows.forEach((r: any) => this.selectedIds.add(r.id));
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
    this.loadList();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} service(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} service(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} service(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
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
    this.svc.getAuditLog(id).subscribe({
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
