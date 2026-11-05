import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, DatePipe, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import * as bootstrap from 'bootstrap';

import { HttpClient } from '@angular/common/http';
import { MachinesService } from '../services/machines.service';

import {
  MachineDto,
  MachineRowViewModel,
  MachineDetailViewModel
} from '../shared/models/machine.dto';

import {
  ApiPagedResponse,
  ApiResponse
} from '../shared/models/api-response';


@Component({
  selector: 'app-gen-machines',
  standalone: true,
  templateUrl: './gen-machines.component.html',
  styleUrls: ['./gen-machines.component.css'],
  imports: [CommonModule, FormsModule, NgClass, DatePipe]
})
export class GenMachinesComponent implements OnInit {

  rows: MachineRowViewModel[] = [];
  totalItems = 0;

  searchText = '';
  activeFilter = '';

  // ⭐ ADD FILTERS
  brandFilter: number | null = null;
  serviceFilter: number | null = null;
  branchFilter: number | null = null;

  // dropdowns
  brandList: any[] = [];
  serviceList: any[] = [];
  branchList: any[] = [];

  // Phase R.4 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctBrands: 0,
    distinctServices: 0,
    distinctBranches: 0,
    totalHeads: 0,
    totalNeedles: 0,
    serviceDueSoon: 0,
    serviceOverdue: 0,
    byBrand: [] as Array<{ brand: string; count: number }>,
    byBranch: [] as Array<{ branch: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // Pagination
  page = 1;
  pageSize = 10;
  pages: number[] = [];
  totalPages = 1;

  detailModel: MachineDetailViewModel | null = null;

  formModel: MachineDto = this.emptyMachine();
  modalMode: 'create' | 'edit' = 'create';

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  deleteId: number | null = null;

  constructor(private machSvc: MachinesService, private http: HttpClient) { }

  ngOnInit(): void {
    this.loadBrands();
    this.loadServices();
    this.loadBranches();
    this.loadMachines();
  }

  // =====================================================
  // RESET + RELOAD
  // =====================================================
  refresh() {
    this.searchText = '';
    this.activeFilter = '';
    this.page = 1;

    this.brandFilter = null;
    this.serviceFilter = null;
    this.branchFilter = null;

    this.loadMachines();
  }


  // =====================================================
  // EMPTY MODEL
  // =====================================================
  emptyMachine(): MachineDto {
    return {
      Id: 0,
      Code: '',
      BrandId: null,
      ServiceId: null,
      HeadtoHead: '',
      FrameHeight: '',
      HeadsNum: null,
      NeedlesNum: null,
      SerialNumber: '',
      CallingNumber: '',
      PurchaseDate: '',
      Warranty: null,
      ServiceEnd: '',
      ServiceNext: '',
      BranchId: null,
      Active: true
    };
  }

  // =====================================================
  // LOAD LIST (NOW WITH ALL FILTERS)
  // =====================================================
  loadMachines() {
    this.machSvc
      .list({
        search: this.searchText,
        active: this.activeFilter,
        brandId: this.brandFilter,
        serviceId: this.serviceFilter,
        branchId: this.branchFilter,
        page: this.page,
        pageSize: this.pageSize
      })
      .subscribe(res => {
        this.rows = res.data?.items || [];
        this.totalItems = res.data?.totalItems || 0;
        const sm: any = (res.data as any)?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctBrands: Number(sm.distinctBrands ?? 0),
            distinctServices: Number(sm.distinctServices ?? 0),
            distinctBranches: Number(sm.distinctBranches ?? 0),
            totalHeads: Number(sm.totalHeads ?? 0),
            totalNeedles: Number(sm.totalNeedles ?? 0),
            serviceDueSoon: Number(sm.serviceDueSoon ?? 0),
            serviceOverdue: Number(sm.serviceOverdue ?? 0),
            byBrand: Array.isArray(sm.byBrand) ? sm.byBrand : [],
            byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : []
          };
        }
        this.setupPagination();
      });
  }

  // Phase R.4 - profile drawer
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.machSvc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('Machine profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }


  // =====================================================
  // DROPDOWNS
  // =====================================================

  private unwrapDropdown(res: any): any[] {
    if (!res) return [];
    if (Array.isArray(res)) return res;        // raw array
    if (res.data && Array.isArray(res.data)) return res.data;
    if (res.Data && Array.isArray(res.Data)) return res.Data;
    return [];
  }


  private mapDropdown(list: any[]): any[] {
    if (!Array.isArray(list)) return [];

    return list.map(item => ({
      id: item.id ?? item.Id ?? null,
      name: item.name ?? item.Name ?? item.display ?? ""
    }));
  }


  loadBrands() {
    this.http.get<any>('/emb-products/dropdowns/brands')
      .subscribe({
        next: (res: any) => {
          const raw = res?.data || res; // support both {data} and raw array
          this.brandList = this.mapDropdown(raw);
        },
        error: (err: any) => console.error("Failed to load brands", err)
      });
  }


  loadServices() {
    this.http.get<any>('/emb-products/dropdowns/services')
      .subscribe({
        next: (res: any) => {
          const list = this.unwrapDropdown(res);
          this.serviceList = list.map(x => ({
            id: x.id ?? x.Id,
            name: x.name ?? x.Name
          }));
        },
        error: (err: any) => console.error("Failed to load services", err)
      });
  }





  loadBranches() {
    this.http.get<any>('/emb-products/dropdowns/branches')
      .subscribe({
        next: (res: any) => {
          const raw = res?.data || res;
          this.branchList = this.mapDropdown(raw);
        },
        error: (err: any) => console.error("Failed to load branches", err)
      });
  }


  // =====================================================
  // PAGINATION
  // =====================================================
  setupPagination() {
    this.totalPages = Math.ceil(this.totalItems / this.pageSize);
    this.pages = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  onPageChange(p: number) {
    if (p < 1 || p > this.totalPages) return;
    this.page = p;
    this.loadMachines();
  }

  // =====================================================
  // CREATE
  // =====================================================
  openCreate() {
    this.modalMode = 'create';
    this.formModel = this.emptyMachine();
    // Phase R.4 - auto-generate machine code
    this.machSvc.getNextCode().subscribe({
      next: (res: any) => {
        const code = res?.data?.code ?? res?.Data?.code ?? '';
        if (code) this.formModel.Code = code;
      },
      error: err => console.error('Failed to fetch next machine code', err)
    });
    new bootstrap.Modal(this.formModal.nativeElement).show();
  }

  saveCreate() {
    this.machSvc.create(this.formModel).subscribe(res => {
      alert(res.message);
      bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
      this.loadMachines();
    });
  }

  // =====================================================
  // EDIT
  // =====================================================
  openEdit(id: number) {
    this.modalMode = 'edit';
    this.machSvc.getDetail(id).subscribe(res => {
      this.formModel = res.data;
      new bootstrap.Modal(this.formModal.nativeElement).show();
    });
  }

  saveEdit() {
    this.machSvc.update(this.formModel).subscribe(res => {
      alert(res.message);
      bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
      this.loadMachines();
    });
  }

  // =====================================================
  // DETAIL
  // =====================================================
  openDetail(id: number) {
    this.machSvc.getDetail(id).subscribe(res => {
      this.detailModel = res.data;
      new bootstrap.Modal(this.detailModal.nativeElement).show();
    });
  }

  // =====================================================
  // DELETE
  // =====================================================
  openDelete(id: number) {
    this.deleteId = id;
    new bootstrap.Modal(this.deleteModal.nativeElement).show();
  }

  confirmDelete() {
    if (!this.deleteId) return;
    this.machSvc.delete(this.deleteId).subscribe(res => {
      alert(res.message);
      bootstrap.Modal.getInstance(this.deleteModal.nativeElement)?.hide();
      this.loadMachines();
    });
    this.deleteId = null;
  }


  // =====================================================
  // EXPORTS
  // =====================================================
  prepareExportRows() {
    return this.rows.map(m => ({
      Code: m.Code || '',
      Brand: m.BrandName || '',
      Service: m.ServiceName || '',
      Heads: m.HeadsNum ?? '',
      Needles: m.NeedlesNum ?? '',
      Serial: m.SerialNumber || '',
      Branch: m.BranchName || '',
      Status: m.Active ? 'Active' : 'Inactive'
    }));
  }

  exportExcel() {
    const ws = XLSX.utils.json_to_sheet(this.prepareExportRows());
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Machines');
    XLSX.writeFile(wb, 'Machines.xlsx');
  }

  exportPDF() {
    const doc = new jsPDF('l', 'mm', 'a4');
    doc.setFontSize(14);
    doc.text('Machines List', 10, 10);

    autoTable(doc, {
      startY: 15,
      head: [[
        'Code', 'Brand', 'Service', 'Heads', 'Needles', 'Serial', 'Branch', 'Status'
      ]],
      body: this.prepareExportRows().map(r => Object.values(r)),
    });

    doc.save('Machines.pdf');
  }

  printTable() {
    const printContents = document.querySelector('.table-responsive')?.innerHTML;
    if (!printContents) return;

    const win = window.open('', '', 'width=900,height=650');
    win?.document.write(`
      <html>
      <head>
        <title>Print Machines</title>
        <style>
          table { width: 100%; border-collapse: collapse; font-size: 13px; }
          th, td { border: 1px solid #ccc; padding: 6px; }
          th { background: #f8f9fa; }
        </style>
      </head>
      <body>${printContents}</body>
      </html>
    `);

    win?.document.close();
    win?.print();
  }
  getBrandName(id: number | null): string {
    if (!id) return 'Brand';
    const b = this.brandList.find(x => x.id === id);
    return b ? b.name : 'Brand';
  }

  getServiceName(id: number | null): string {
    if (!id) return 'Service';
    const s = this.serviceList.find(x => x.id === id);
    return s ? s.name : 'Service';
  }

  getBranchName(id: number | null): string {
    if (!id) return 'Branch';
    const br = this.branchList.find(x => x.id === id);
    return br ? br.name : 'Branch';
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
    this.loadMachines();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} machine(s)?`)) return;
    this.bulkBusy = true;
    this.machSvc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} machine(s)?`)) return;
    this.bulkBusy = true;
    this.machSvc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} machine(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.machSvc.bulkDelete(ids).subscribe({
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
    this.machSvc.getAuditLog(id).subscribe({
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
