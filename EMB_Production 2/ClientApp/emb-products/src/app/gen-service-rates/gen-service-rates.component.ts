// --------------------------------------------------------
// EXPORT LIBRARIES
// --------------------------------------------------------
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import * as bootstrap from 'bootstrap';
import {ApiResponse, ApiPagedResponse } from '../shared/models/api-response';
import {ServiceRateDto,ServiceRateRowViewModel,ServiceRateDetailViewModel } from '../shared/models/service-rate.dto';

interface DropdownItem {
  id: number;
  name: string;
  code?: string;
  display?: string;
}

@Component({
  selector: 'app-gen-service-rates',
  standalone: true,
  templateUrl: './gen-service-rates.component.html',
  styleUrls: ['./gen-service-rates.component.css'],
  imports: [CommonModule, FormsModule, NgClass, DatePipe]
})

export class GenServiceRatesComponent implements OnInit {

  rows: ServiceRateRowViewModel[] = [];
  totalItems = 0;

  page = 1;
  pageSize = 10;

  searchText = '';
  activeFilter = '';

  productFilter: number | null = null;
  serviceFilter: number | null = null;
  branchFilter: number | null = null;

  // ================= DROPDOWNS =================
  productList: DropdownItem[] = [];
  serviceList: { id: number; name: string }[] = [];
  branchList: DropdownItem[] = [];

  // Phase R.9 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctProducts: 0,
    distinctServices: 0,
    distinctBranches: 0,
    byService: [] as Array<{ service: string; count: number }>,
    byBranch: [] as Array<{ branch: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ================= FORM =================
  formModel: ServiceRateDto = this.getEmptyModel();
  modalMode: 'create' | 'edit' = 'create';

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  detailModel: ServiceRateDetailViewModel | null = null;
  deleteId: number | null = null;

  constructor(private http: HttpClient) { }

  ngOnInit(): void {
    this.loadProducts();
    this.loadServiceTypes();
    this.loadBranches();
    this.loadList();
  }

  // ==========================================
  // EMPTY MODEL
  // ==========================================
  private getEmptyModel(): ServiceRateDto {
    return {
      Id: 0,
      ProductId: null,
      ServiceId: null,
      ChargeFactor: '',
      Remarks: '',
      BranchId: null,
      Active: true
    };
  }

  refresh() {
    this.searchText = '';
    this.activeFilter = '';
    this.productFilter = null;
    this.serviceFilter = null;
    this.branchFilter = null;
    this.page = 1;
    this.loadProducts();
    this.loadServiceTypes();
    this.loadBranches();
    this.loadList();
  }

  openCreateRefresh() {
    this.loadProducts();
  }

  // ==========================================
  // DROPDOWNS
  // ==========================================

  loadProducts() {
    this.http.get<{ data: any[] }>('/emb-products/dropdowns/products')
      .subscribe({
        next: res => {
          const data = res?.data ?? [];
          this.productList = data.map(x => ({
            id: x.id,
            name: x.name,
            code: x.code,
            display: x.code ? `${x.code} - ${x.name}` : x.name
          }));
        },
        error: err => {
          console.error('Failed to load products', err);
          this.productList = [];
        }
      });
  }

  loadServiceTypes() {
    this.http.get<{ data: { id: number; name: string }[] }>(
      '/emb-products/dropdowns/service-types'
    )
      .subscribe({
        next: res => {
          this.serviceList = res?.data ?? [];
        },
        error: err => {
          console.error('Failed to load service types', err);
          this.serviceList = [];
        }
      });
  }


  loadBranches() {
    this.http.get<any>('/emb-products/dropdowns/branches')
      .subscribe({
        next: res => this.branchList = res.data || []
      });
  }

  // =====================================
  // LIST LOADING
  // =====================================
  loadList() {
    const url =
      `/emb-products/service-rates/list?page=${this.page}&pageSize=${this.pageSize}` +
      `&search=${encodeURIComponent(this.searchText || '')}` +
      `&activeFilter=${this.activeFilter || ''}` +
      `&productFilter=${this.productFilter ?? ''}` +
      `&serviceFilter=${this.serviceFilter ?? ''}` +
      `&branchFilter=${this.branchFilter ?? ''}`;

    this.http.get<any>(url)
      .subscribe({
        next: res => {
          this.rows = res.data?.items || [];
          this.totalItems = res.data?.totalItems || 0;
          const sm = res.data?.summary ?? null;
          if (sm) {
            this.summary = {
              count: Number(sm.count ?? 0),
              activeCount: Number(sm.activeCount ?? 0),
              inactiveCount: Number(sm.inactiveCount ?? 0),
              distinctProducts: Number(sm.distinctProducts ?? 0),
              distinctServices: Number(sm.distinctServices ?? 0),
              distinctBranches: Number(sm.distinctBranches ?? 0),
              byService: Array.isArray(sm.byService) ? sm.byService : [],
              byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : []
            };
          }
          this.setupPagination();
        }
      });
  }

  // Phase R.9 - profile drawer
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.http.get<any>(`/emb-products/service-rates/profile/${numericId}`).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('ServiceRate profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  // =====================================
  // PAGINATION SUPPORT
  // =====================================
  pages: number[] = [];
  totalPages = 1;

  setupPagination(): void {
    this.totalPages = Math.ceil(this.totalItems / this.pageSize);

    this.pages = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  onPageChange(p: number): void {
    if (p < 1 || p > this.totalPages) return;

    this.page = p;
    this.loadList();
  }


  onSearch() {
    this.page = 1;
    this.loadList();
  }

  // =====================================
  // CREATE
  // =====================================
  openCreate() {
    this.modalMode = 'create';
    this.formModel = this.getEmptyModel();
    this.loadProducts();
    new bootstrap.Modal(this.formModal.nativeElement).show();
  }

  saveCreate() {
    this.http
      .post<ApiResponse>('/emb-products/service-rates/create', this.formModel)
      .subscribe({
        next: res => {
          alert(res.message || "Created Successfully");
          bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
          this.loadList();
        }
      });
  }

  // =====================================
  // EDIT
  // =====================================
  openEdit(id: number) {
    this.modalMode = 'edit';

    this.http.get<ApiResponse>(`/emb-products/service-rates/detail/${id}`)
      .subscribe({
        next: r => {
          this.formModel = r.data;
          new bootstrap.Modal(this.formModal.nativeElement).show();
        }
      });
  }

  saveEdit() {
    this.http.post<ApiResponse>('/emb-products/service-rates/update', this.formModel)
      .subscribe({
        next: res => {
          alert(res.message || "Updated");
          bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
          this.loadList();
        }
      });
  }

  // =====================================
  // DETAIL
  // =====================================
  openDetail(id: number) {
    this.http.get<ApiResponse>(`/emb-products/service-rates/detail/${id}`)
      .subscribe({
        next: r => {
          this.detailModel = r.data;
          new bootstrap.Modal(this.detailModal.nativeElement).show();
        }
      });
  }

  // =====================================
  // DELETE
  // =====================================
  openDelete(id: number) {
    this.deleteId = id;
    new bootstrap.Modal(this.deleteModal.nativeElement).show();
  }

  confirmDelete() {
    if (!this.deleteId) return;

    this.http.post<ApiResponse>('/emb-products/service-rates/delete', { id: this.deleteId })
      .subscribe({
        next: res => {
          alert(res.message || "Deleted");
          bootstrap.Modal.getInstance(this.deleteModal.nativeElement)?.hide();
          this.loadList();
        }
      });

    this.deleteId = null;
  }

  // =====================================
  // EXPORT UTIL
  // =====================================
  prepareExportRows() {
    return this.rows.map(r => ({
      ProductCode: r.ProductCode || '-',
      ProductName: r.ProductName || '-',
      Service: r.ServiceName || '-',
      ChargeFactor: r.ChargeFactor || '-',
      Remarks: r.Remarks || '-',
      Branch: r.BranchName || '-',
      Status: r.Active ? 'Active' : 'Inactive'
    }));
  }

  // =====================================
  // EXPORT — EXCEL
  // =====================================
  exportExcel() {
    const exportData = this.prepareExportRows();
    const ws = XLSX.utils.json_to_sheet(exportData);
    const wb = XLSX.utils.book_new();

    XLSX.utils.book_append_sheet(wb, ws, 'ServiceRates');
    XLSX.writeFile(wb, 'ServiceRates.xlsx');
  }

  // =====================================
  // EXPORT — PDF
  // =====================================
  exportPDF() {
    const doc = new jsPDF('l', 'mm', 'a4');

    doc.setFontSize(14);
    doc.text('Service Rates List', 10, 10);

    autoTable(doc, {
      startY: 15,
      head: [[
        'Product Code', 'Product Name', 'Service', 'Charge Factor',
        'Remarks', 'Branch', 'Status'
      ]],
      body: this.prepareExportRows().map(r => Object.values(r)),
    });

    doc.save('ServiceRates.pdf');
  }

  // =====================================
  // EXPORT — PRINT
  // =====================================
  printTable() {
    const printContents = document.querySelector('.table-responsive')?.innerHTML;

    if (!printContents) return;

    const win = window.open('', '', 'width=900,height=650');
    win?.document.write(`
      <html>
        <head>
          <title>Print Service Rates</title>
          <style>
            table { width:100%; border-collapse:collapse; }
            th, td { border:1px solid #ccc; padding:6px; font-size:12px; }
            th { background:#f8f9fa; }
          </style>
        </head>
        <body>${printContents}</body>
      </html>
    `);

    win?.document.close();
    win?.print();
  }

  // =====================================
  // LOOKUPS
  // =====================================
  getProductName(id: number | null) {
    return this.productList.find(x => x.id === id)?.name || "-";
  }

  getServiceName(id: number | null) {
    return this.serviceList.find(x => x.id === id)?.name || "-";
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions (uses raw http since the
  //  component doesn't go through a typed service).
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
    if (!confirm(`Activate ${ids.length} rate(s)?`)) return;
    this.bulkBusy = true;
    this.http.post<any>('/emb-products/service-rates/bulk-activate', { ids }).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} rate(s)?`)) return;
    this.bulkBusy = true;
    this.http.post<any>('/emb-products/service-rates/bulk-deactivate', { ids }).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} rate(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.http.post<any>('/emb-products/service-rates/bulk-delete', { ids }).subscribe({
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
    this.http.get<any>('/emb-products/service-rates/audit-log/' + id).subscribe({
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
