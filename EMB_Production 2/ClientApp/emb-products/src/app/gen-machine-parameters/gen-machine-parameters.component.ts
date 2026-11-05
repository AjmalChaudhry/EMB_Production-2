import { Component, OnInit, ElementRef, ViewChild } from '@angular/core';
import { CommonModule, NgClass } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

import * as bootstrap from 'bootstrap';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import {ApiPagedResponse,ApiResponse} from '../shared/models/api-response';
import {MachineParameterDto,MachineParameterRowViewModel,
        MachineParameterDetailViewModel,} from '../shared/models/machine-parameter.dto';
import { MachineParametersService } from '../services/machine-parameters.service';

@Component({
  selector: 'app-gen-machine-parameters',
  standalone: true,
  templateUrl: './gen-machine-parameters.component.html',
  styleUrls: ['./gen-machine-parameters.component.css'],
  imports: [CommonModule, FormsModule, NgClass]
})
export class GenMachineParametersComponent implements OnInit {

  rows: MachineParameterRowViewModel[] = [];
  totalItems = 0;

  // Filters
  searchText = '';
  activeFilter = '';
  machineFilter: number | null = null;

  // Pagination
  page = 1;
  pageSize = 10;
  pages: number[] = [];

  // Dropdown data
  machineList: { id: number; code?: string; name?: string }[] = [];
  
  headToHeadOptions: string[] = [
    'Single',
    'Head to Head',
    'Flat',
    'Cap Frame'
  ];

  frameHeightOptions: number[] = [300, 350, 400, 450, 500];

  // Phase R.5 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctMachines: 0,
    byHeadToHead: [] as Array<{ headToHead: string; count: number }>,
    byMachine: [] as Array<{ machine: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // Form & Detail
  formModel: MachineParameterDto = this.emptyModel();
  modalMode: 'create' | 'edit' = 'create';
  detailModel: MachineParameterDetailViewModel | null = null;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  deleteId: number | null = null;

  constructor(
    private svc: MachineParametersService,
    private http: HttpClient
  ) { }

  ngOnInit(): void {
    this.loadMachinesDropdown();
    this.loadRows();
  }

  emptyModel(): MachineParameterDto {
    return {
      Id: 0,
      MachineId: null,
      HeadToHead: '',
      FrameHeight: null,
      Active: true
    };
  }

  // ===================== DROPDOWNS =====================
  loadMachinesDropdown() {
    this.http.get<any>('/emb-products/dropdowns/machines')
      .subscribe({
        next: res => {
          const data = res?.data ?? [];
          this.machineList = data.map((m: any) => ({
            id: m.id,
            code: m.code,
            name: m.name || m.code
          }));
        },
        error: err => console.error('Failed to load machines dropdown', err)
      });
  }

  // ===================== LIST =====================
  loadRows() {
    this.svc
      .list(this.searchText, this.activeFilter, this.page, this.pageSize, this.machineFilter)

      .subscribe({
        next: (res: any) => {
          this.rows = res.data?.items || [];
          this.totalItems = res.data?.totalItems || 0;
          const sm = res.data?.summary ?? null;
          if (sm) {
            this.summary = {
              count: Number(sm.count ?? 0),
              activeCount: Number(sm.activeCount ?? 0),
              inactiveCount: Number(sm.inactiveCount ?? 0),
              distinctMachines: Number(sm.distinctMachines ?? 0),
              byHeadToHead: Array.isArray(sm.byHeadToHead) ? sm.byHeadToHead : [],
              byMachine: Array.isArray(sm.byMachine) ? sm.byMachine : []
            };
          }
          this.setupPagination();
        },
        error: err => {
          console.error('Failed to load machine parameters', err);
          this.rows = [];
          this.totalItems = 0;
          this.setupPagination();
        }
      });
  }

  refresh() {
    this.searchText = '';
    this.activeFilter = '';
    this.machineFilter = null;
    this.page = 1;
    this.loadRows();
  }

  // Phase R.5 - filter handlers + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadRows(); }
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
        console.error('MachineParameter profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  setupPagination() {
    const totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.pages = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  onPageChange(p: number) {
    if (p < 1 || p > this.pages.length) return;
    this.page = p;
    this.loadRows();
  }

  // ===================== CREATE / EDIT =====================
  openCreate() {
    this.modalMode = 'create';
    this.formModel = this.emptyModel();
    new bootstrap.Modal(this.formModal.nativeElement).show();
  }

  openEdit(id: number) {
    this.modalMode = 'edit';

    this.svc.getDetail(id).subscribe({
      next: (res: ApiResponse) => {
        if (res.success && res.data) {
          const d = res.data as MachineParameterDetailViewModel;

          this.formModel = {
            Id: d.Id,
            MachineId: d.MachineId,
            HeadToHead: d.HeadToHead ?? '',
            FrameHeight: d.FrameHeight,
            Active: d.Active
          };

          new bootstrap.Modal(this.formModal.nativeElement).show();
        } else {
          alert(res.message || 'Failed to load detail');
        }
      },
      error: (err) => console.error("Failed to load detail", err)
    });
  }



  saveForm() {
    if (this.modalMode === 'create') {
      this.svc.create(this.formModel).subscribe(res => {
        alert(res.message || 'Created');
        bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
        this.loadRows();
      });
    } else {
      this.svc.update(this.formModel).subscribe(res => {
        alert(res.message || 'Updated');
        bootstrap.Modal.getInstance(this.formModal.nativeElement)?.hide();
        this.loadRows();
      });
    }
  }

  // ===================== DETAIL =====================
  openDetail(id: number) {
    this.svc.getDetail(id).subscribe({
      next: (res: ApiResponse) => {
        if (res.success && res.data) {
          this.detailModel = res.data as MachineParameterDetailViewModel;
          new bootstrap.Modal(this.detailModal.nativeElement).show();
        } else {
          alert(res.message || 'Failed to load detail');
        }
      },
      error: (err) => console.error("Failed to load detail", err)
    });
  }


  // ===================== DELETE =====================
 

  openDelete(id: number) {
    this.deleteId = id;
    new bootstrap.Modal(this.deleteModal.nativeElement).show();
  }

  confirmDelete() {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe(res => {
      alert(res.message);
      bootstrap.Modal.getInstance(this.deleteModal.nativeElement)?.hide();
      this.loadRows();
    });

    this.deleteId = null;
  }


  // ===================== EXPORTS =====================
  private prepareExportRows() {
    return this.rows.map(r => ({
      Machine: r.MachineCode ?? '',
      HeadToHead: r.HeadToHead ?? '',
      FrameHeight: r.FrameHeight ?? '',
      Status: r.Active ? 'Active' : 'Inactive'
    }));
  }

  exportExcel() {
    const data = this.prepareExportRows();
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'MachineParameters');
    XLSX.writeFile(wb, 'MachineParameters.xlsx');
  }

  exportPDF() {
    const doc = new jsPDF();
    doc.setFontSize(14);
    doc.text('Machine Parameters', 10, 10);

    autoTable(doc, {
      startY: 16,
      head: [['Machine', 'HeadToHead', 'FrameHeight', 'Status']],
      body: this.prepareExportRows().map(r => Object.values(r))
    });

    doc.save('MachineParameters.pdf');
  }

  printTable() {
    const printContents = document.querySelector('.table-responsive')?.innerHTML;
    if (!printContents) return;

    const win = window.open('', '', 'width=900,height=650');
    win?.document.write(`
      <html>
        <head>
          <title>Print Machine Parameters</title>
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
    this.loadRows();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} machine parameter(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} machine parameter(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} machine parameter(s)? (soft-delete)`)) return;
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
