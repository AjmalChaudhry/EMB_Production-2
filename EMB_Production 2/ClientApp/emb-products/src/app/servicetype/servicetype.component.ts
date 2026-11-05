import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { ServiceTypeService } from '../services/servicetype.service';

import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {ServiceTypeRowViewModel,ServiceTypeDetailViewModel,ServiceTypeDto,ServiceTypeListResponse,
        ServiceTypeDetailResponse} from '../shared/models/service-type.dto';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

declare var bootstrap: any;

@Component({
  selector: 'app-service-types',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule
  ],
  templateUrl: './servicetype.component.html',
  styleUrls: ['./servicetype.component.css']
})


export class ServiceTypeComponent implements OnInit {

  
  // Filters
  searchText = '';
  activeFilter = '';
  branchFilter: number | null = null;

 
  // Paging
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  // Data
  rows: ServiceTypeRowViewModel[] = [];

  // Phase R.8 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctBranches: 0,
    totalServicesUnderTypes: 0,
    byBranch: [] as Array<{ branch: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // Form / modal
  modalMode: 'create' | 'edit' | 'detail' = 'create';

  formModel: ServiceTypeDetailViewModel = this.createEmptyFormModel();

  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  detailModel: any = {};
  selectedId: number | null = null;

  constructor(
    private svc: ServiceTypeService
  ) { }

  ngOnInit(): void {
    this.loadBranches();
    this.loadList();
  }

  branchList: { value: number; text: string }[] = [];

  loadBranches(): void {
    this.svc.getBranchList().subscribe({
      next: (res) => {

        // The REAL fix (convert your API format to dropdown format)
        this.branchList = (res.data || []).map((b: any) => ({
          value: b.id,
          text: b.name
        }));

        console.log("Converted branchList:", this.branchList);
      },
      error: err => {
        console.error("Failed to load branches", err);
      }
    });
  }



  // ==========================
  // Helpers
  // ==========================
  private createEmptyFormModel(): ServiceTypeDetailViewModel {
    return {
      Id: 0,
      Service_Type: '',
      Service_TypeDescription: '',
      BranchId: null,
      BranchName: null,
      Active: true,
      Deleted: false,
      CreatedBy: null,
      CreatedDate: null,
      UpdatedBy: null,
      UpdatedDate: null
    };
  }

  private setupPagination(): void {
    const totalPages = Math.ceil((this.totalItems || 0) / this.pageSize);
    this.pages = [];
    for (let i = 1; i <= totalPages; i++) {
      this.pages.push(i);
    }
  }

  // ==========================
  // Load List
  // ==========================
  loadList(): void {
    this.svc.getList(
      this.searchText,
      this.page,
      this.pageSize,
      this.branchFilter,
      this.activeFilter
    ).subscribe({
      next: (res: any) => {
        this.rows = res.data ?? [];
        this.totalItems = res.total ?? 0;
        const sm = res?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctBranches: Number(sm.distinctBranches ?? 0),
            totalServicesUnderTypes: Number(sm.totalServicesUnderTypes ?? 0),
            byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : []
          };
        }
        this.setupPagination();
      },
      error: err => {
        console.error('Failed to load service types', err);
        this.rows = [];
        this.totalItems = 0;
        this.setupPagination();
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
    this.page = 1;
    this.loadList();
  }

  onPageChange(p: number): void {
    if (p < 1 || p === this.page) { return; }
    this.page = p;
    this.loadList();
  }

  // Phase R.8 - profile drawer
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
        console.error('ServiceType profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  // ==========================
  // Modal open/create/edit
  // ==========================
  openCreate(): void {
    this.modalMode = 'create';
    this.formModel = this.createEmptyFormModel();
    const modal = new bootstrap.Modal(this.formModal.nativeElement);
    modal.show();
  }

  openEdit(id: number): void {
    this.modalMode = 'edit';
    this.formModel = this.createEmptyFormModel();

    this.svc.getDetail(id).subscribe({
      next: (res: ServiceTypeDetailResponse) => {
        if (res.success && res.data) {
          this.formModel = {
            ...this.createEmptyFormModel(),
            ...res.data
          };
          const modal = new bootstrap.Modal(this.formModal.nativeElement);
          modal.show();
        } else {
          alert(res.message ?? 'Failed to load record.');
        }
      },
      error: err => {
        console.error('Failed to load detail', err);
        alert('Error loading record.');
      }
    });
  }

  // ==========================
  // Save (create/update)
  // ==========================
  save(): void {
    // Map detail VM → DTO
    const payload: ServiceTypeDto = {
      Id: this.formModel.Id || 0,
      Service_Type: this.formModel.Service_Type,
      Service_TypeDescription: this.formModel.Service_TypeDescription || '',
      BranchId: this.formModel.BranchId ?? null,
      Active: this.formModel.Active ?? true
    };

    if (!payload.Service_Type || !payload.Service_Type.trim()) {
      alert('Service Type is required.');
      return;
    }

    const req$ = this.modalMode === 'create'
      ? this.svc.create(payload)
      : this.svc.update(payload);

    req$.subscribe({
      next: res => {
        alert(res.message || (this.modalMode === 'create'
          ? 'Service Type created.'
          : 'Service Type updated.'));

        this.closeModal();
        this.loadList();
      },
      error: err => {
        console.error('Save failed', err);
        alert('Failed to save record.');
      }
    });
  }

  closeModal(): void {
    const modalEl = this.formModal?.nativeElement;
    if (!modalEl) { return; }
    const modalInstance = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
    modalInstance.hide();
  }

  openDetail(id: number): void {
    this.svc.getDetail(id).subscribe({
      next: (res) => {
        if (res.success && res.data) {
          this.detailModel = res.data;

          setTimeout(() => {
            const modal = new bootstrap.Modal(this.detailModal.nativeElement);
            modal.show();
          }, 10);
        }
      },
      error: err => console.error(err)
    });
  }





  // ==========================
  // Delete
  // ==========================
  openDelete(id: number): void {
    this.selectedId = id;

    const modal = new bootstrap.Modal(this.deleteModal.nativeElement);
    modal.show();
  }


  confirmDelete(): void {
    if (!this.selectedId) return;

    this.svc.delete(this.selectedId).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadList();
      },
      error: () => {
        this.closeDeleteModal();
      }
    });
  }


  closeDeleteModal(): void {
    const modal = bootstrap.Modal.getInstance(this.deleteModal.nativeElement);
    if (modal) modal.hide();
  }

  formatDate(d: any): string {
    if (!d) return '-';
    const ts = parseInt(d.replace(/\D/g, ''), 10);
    return new Date(ts).toLocaleString('en-GB'); // DD/MM/YYYY HH:mm
  }



  // ==========================
  // EXPORT: CSV / Excel
  // ==========================
  exportToCsv(filename: string = 'service-types.csv'): void {
    if (!this.rows || this.rows.length === 0) {
      alert('No data to export.');
      return;
    }

    const header = ['Service Type', 'Description', 'Branch', 'Status'];

    const csvRows = this.rows.map(r => {
      const status = r.Active ? 'Active' : 'Inactive';
      return [
        r.Service_Type ?? '',
        r.Service_TypeDescription ?? '',
        r.BranchName ?? '',
        status
      ];
    });

    const lines = [
      header.join(','),
      ...csvRows.map(row =>
        row
          .map(v => `"${(v ?? '').toString().replace(/"/g, '""')}"`)
          .join(',')
      )
    ];

    const csvContent = lines.join('\r\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });

    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.href = url;
    link.setAttribute('download', filename);
    link.click();
    URL.revokeObjectURL(url);
  }

  // ==========================
  // EXPORT: PDF
  // ==========================
  exportToPdf(): void {
    if (!this.rows || this.rows.length === 0) {
      alert('No data to export.');
      return;
    }

    const doc = new jsPDF();

    // Title
    doc.setFontSize(14);
    doc.text('Service Types', 14, 15);

    autoTable(doc, {
      startY: 20,
      head: [['Service Type', 'Description', 'Branch', 'Status']],
      body: this.rows.map(r => [
        r.Service_Type ?? '',
        r.Service_TypeDescription ?? '',
        r.BranchName ?? '',
        r.Active ? 'Active' : 'Inactive'
      ]),
      styles: { fontSize: 10 }
    });

    doc.save('service-types.pdf');
  }


  // ==========================
  // PRINT
  // ==========================
  printTable(): void {
    if (!this.rows || this.rows.length === 0) {
      alert('No data to print.');
      return;
    }

    let html = `
      <html>
      <head>
        <title>Service Types</title>
        <style>
          table { border-collapse: collapse; width: 100%; font-family: Arial, sans-serif; font-size: 12px; }
          th, td { border: 1px solid #333; padding: 4px 8px; text-align: left; }
          th { background: #f0f0f0; }
          h3 { font-family: Arial, sans-serif; }
        </style>
      </head>
      <body>
        <h3>Service Types</h3>
        <table>
          <thead>
            <tr>
              <th>Service Type</th>
              <th>Description</th>
              <th>Branch</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
    `;

    for (const r of this.rows) {
      html += `
        <tr>
          <td>${r.Service_Type ?? ''}</td>
          <td>${r.Service_TypeDescription ?? ''}</td>
          <td>${r.BranchName ?? ''}</td>
          <td>${r.Active ? 'Active' : 'Inactive'}</td>
        </tr>
      `;
    }

    html += `
          </tbody>
        </table>
      </body>
      </html>
    `;

    const printWin = window.open('', '_blank', 'width=900,height=700');
    if (!printWin) { return; }
    printWin.document.open();
    printWin.document.write(html);
    printWin.document.close();
    printWin.focus();
    printWin.print();
    printWin.close();
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
    if (!confirm(`Activate ${ids.length} service type(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} service type(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} service type(s)? (soft-delete)`)) return;
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
