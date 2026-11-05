import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ComponentRowViewModel,
  ComponentDetailViewModel,
  ComponentCreateUpdateModel
} from '../shared/models/component.dto';

import { ComponentsService } from '../services/components.service';
import { DropdownDataService } from '../shared/services/dropdown-data.service';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-components',
  standalone: true,
  templateUrl: './gen-components.component.html',
  styleUrls: ['./gen-components.component.css'],
  imports: [CommonModule, FormsModule]
})
export class GenComponentsComponent implements OnInit {

  // ===================== DATA =====================
  components: ComponentRowViewModel[] = [];

  designs: any[] = [];
  branches: any[] = [];

  // ===================== FILTERS =====================
  searchText = '';
  activeFilter = '';
  selectedDesignId: number | null = null;
  filterBranchId: number | null = null;

  // Phase R.1 summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    byBranch: [] as Array<{ branch: string; count: number }>,
    byDesign: [] as Array<{ design: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ===================== PAGINATION =====================
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  // ===================== FORM =====================
  modalMode: 'create' | 'edit' | 'detail' = 'create';
  formComponent: ComponentCreateUpdateModel = this.createEmpty();
  selectedComponent: ComponentDetailViewModel | null = null;

  deleteId: number | null = null;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(
    private compSvc: ComponentsService,
    private dropdownSvc: DropdownDataService
  ) { }

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadComponents();
  }

  // ===================== FACTORY =====================
  private createEmpty(): ComponentCreateUpdateModel {
    return {
      id: null,

      name: '',

      designId: null,      // ✅ THIS is what backend needs

      headToHead: null,
      stitch: null,
      tilla: null,
      seqA: null,
      seqB: null,

      branchId: null,
      active: true
    };
  }


  // ===================== DROPDOWNS =====================
  loadDropdowns(): void {

    this.compSvc.getDesigns().subscribe({
      next: list => this.designs = Array.isArray(list) ? list : [],
      error: () => this.designs = []
    });

    this.dropdownSvc.getBranches().subscribe({
      next: list => this.branches = Array.isArray(list) ? list : [],
      error: () => this.branches = []
    });
  }

  // ===================== LIST =====================
  loadComponents(): void {
    this.compSvc.getAll(
      this.searchText,
      this.page,
      this.pageSize,
      this.activeFilter,
      this.selectedDesignId,
      this.filterBranchId
    ).subscribe({
      next: (res: any) => {
        if (!res?.success) {
          this.components = [];
          this.totalItems = 0;
        } else {
          this.components = res.data ?? [];
          this.totalItems = res.total ?? this.components.length;
          const sm = res.summary ?? null;
          if (sm) {
            this.summary = {
              count: Number(sm.count ?? 0),
              activeCount: Number(sm.activeCount ?? 0),
              inactiveCount: Number(sm.inactiveCount ?? 0),
              byBranch: Array.isArray(sm.byBranch) ? sm.byBranch : [],
              byDesign: Array.isArray(sm.byDesign) ? sm.byDesign : []
            };
          }
        }
        this.setupPagination();
      },
      error: () => {
        this.components = [];
        this.totalItems = 0;
        this.setupPagination();
      }
    });
  }

  // Phase R.1 - filter + profile
  onFiltersChange(): void { this.page = 1; this.loadComponents(); }
  clearFiltersR(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.selectedDesignId = null;
    this.filterBranchId = null;
    this.page = 1;
    this.loadComponents();
  }
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.compSvc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('Components profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  setupPagination(): void {
    const pages = Math.ceil(this.totalItems / this.pageSize);
    this.pages = Array.from({ length: pages }, (_, i) => i + 1);
  }

  onPageChange(p: number): void {
    if (p < 1 || p === this.page) return;
    this.page = p;
    this.loadComponents();
  }

  onSearch(): void {
    this.page = 1;
    this.loadComponents();
  }

  onActiveFilterChange(val: string): void {
    this.activeFilter = val;
    this.page = 1;
    this.loadComponents();
  }

  onDesignFilterChange(): void {
    this.page = 1;
    this.loadComponents();
  }

  refresh(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.selectedDesignId = null;
    this.page = 1;
    this.loadComponents();
  }

  // ===================== MODALS =====================
  private openModal(ref: ElementRef): void {
    new bootstrap.Modal(ref.nativeElement).show();
  }

  private closeModal(ref: ElementRef): void {
    bootstrap.Modal.getInstance(ref.nativeElement)?.hide();
  }

  // ===================== CREATE =====================
  openCreate(): void {
    this.modalMode = 'create';
    this.formComponent = this.createEmpty();
    this.openModal(this.formModal);
  }

  saveCreate(): void {

    const payload = {
      name: this.formComponent.name,

      designId: this.formComponent.designId,   

      headToHead: this.formComponent.headToHead ?? 0,
      stitch: this.formComponent.stitch ?? 0,
      tilla: this.formComponent.tilla ?? 0,
      seqA: this.formComponent.seqA ?? 0,
      seqB: this.formComponent.seqB ?? 0,

      branchId: this.formComponent.branchId,
      active: this.formComponent.active
    };
    console.log('FINAL PAYLOAD 👉', payload);

    this.compSvc.create(payload).subscribe({
      next: res => {
        if (res.success) {
          alert('Component created successfully');
          this.closeModal(this.formModal);
          this.loadComponents();
        } else {
          alert(res.message);
        }
      },
      error: err => console.error('Create failed', err)
    });
  }

  // ===================== EDIT =====================
  openEdit(id: number): void {
    this.modalMode = 'edit';

    this.compSvc.getDetail(id).subscribe({
      next: res => {
        if (!res?.success) return;

        const d = res.data;

        this.formComponent = {
          id: d.id,
          name: d.name ?? '',
          designId: d.designId ?? null,   // ✅ FIXED

          headToHead: d.headToHead ?? 0,
          stitch: d.stitch ?? 0,
          tilla: d.tilla ?? 0,
          seqA: d.seqA ?? 0,
          seqB: d.seqB ?? 0,

          branchId: d.branchId ?? null,
          active: d.active ?? true
        };

        this.openModal(this.formModal);   // 🔑 MISSING LINE
      },
      error: err => console.error('Detail load failed', err)
    });
  }


  saveEdit(): void {
    if (!this.formComponent.id) return;

    const payload = {
      id: this.formComponent.id,

      name: this.formComponent.name,
      designId: this.formComponent.designId,

      headToHead: this.formComponent.headToHead ?? 0,
      stitch: this.formComponent.stitch ?? 0,
      tilla: this.formComponent.tilla ?? 0,
      seqA: this.formComponent.seqA ?? 0,
      seqB: this.formComponent.seqB ?? 0,

      branchId: this.formComponent.branchId,
      active: this.formComponent.active
    };


    this.compSvc.update(payload).subscribe({
      next: res => {
        alert(res?.message || 'Updated');
        this.closeModal(this.formModal);
        this.loadComponents();
      },
      error: err => console.error('Update failed', err)
    });
  }

  // ===================== DETAIL =====================
  openDetail(id: number): void {
    this.modalMode = 'detail';

    this.compSvc.getDetail(id).subscribe(res => {
      if (!res?.success || !res.data) return;

      this.selectedComponent = {
        ...res.data,

        // ✅ FIX DATE FORMAT
        createdDate: this.parseMvcDate(res.data.createdDate),
        updatedDate: this.parseMvcDate(res.data.updatedDate)
      };

      this.openModal(this.formModal);
    });
  }

  // ===================== DELETE =====================
  openDelete(id: number): void {
    this.deleteId = id;
    this.openModal(this.deleteModal);
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.compSvc.delete(this.deleteId).subscribe({
      next: res => {
        alert(res?.message || 'Deleted');
        this.closeModal(this.deleteModal);
        this.loadComponents();
      }
    });
  }

  private parseMvcDate(value: any): Date | null {
    if (!value) return null;

    if (typeof value === 'string' && value.startsWith('/Date(')) {
      const ticks = parseInt(value.replace(/\/Date\((\d+)\)\//, '$1'), 10);
      return new Date(ticks);
    }

    return new Date(value);
  }

  formatComponentCode(code: number | null): string {
    if (!code) return '-';
    return 'CMP-' + code.toString().padStart(4, '0');
  }


  // ===================== EXPORT =====================
  // Both Excel and PDF now hit the backend's real file endpoints:
  //   GET /emb-products/components/export-excel  → .xlsx via EPPlus
  //   GET /emb-products/components/export-pdf    → .pdf  via iTextSharp
  // The previous client-side HTML hacks produced fake .xls files and
  // browser print dialogs instead of real downloads.

  private buildExportQuery(): string {
    const params = new URLSearchParams();
    if (this.searchText)   params.set('search', this.searchText);
    if (this.activeFilter) params.set('activeFilter', this.activeFilter);
    const q = params.toString();
    return q ? '?' + q : '';
  }

  exportExcel(): void {
    if (!this.components.length) return alert('No data');
    window.open('/emb-products/components/export-excel' + this.buildExportQuery(), '_blank');
  }

  exportPdf(): void {
    if (!this.components.length) return alert('No data');
    window.open('/emb-products/components/export-pdf' + this.buildExportQuery(), '_blank');
  }

  printTable(): void {
    // Real browser print dialog. Builds a self-contained HTML page from the
    // current component list, opens it in a new tab, and triggers print on
    // load. This is what "Print" should do — not a PDF download.
    if (!this.components.length) return alert('No data');

    const rows = this.components.map(c => `
      <tr>
        <td>${this.escapeHtml(c.code ?? '')}</td>
        <td>${this.escapeHtml(c.name ?? '')}</td>
        <td>${this.escapeHtml(c.designDisplay ?? '')}</td>
        <td>${this.escapeHtml(c.stitch ?? '')}</td>
        <td>${this.escapeHtml(c.tilla ?? '')}</td>
        <td>${this.escapeHtml(c.seqA ?? '')}</td>
        <td>${this.escapeHtml(c.seqB ?? '')}</td>
        <td>${this.escapeHtml(c.branchName ?? '')}</td>
        <td>${c.active ? 'Active' : 'Inactive'}</td>
      </tr>`).join('');

    const html = `<!doctype html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>Components — Print</title>
        <style>
          body { font-family: "Segoe UI", Arial, sans-serif; font-size: 11pt; color: #212529; margin: 14mm; }
          h2 { text-align: center; margin: 0 0 12px; }
          table { width: 100%; border-collapse: collapse; }
          thead { background: #0d6efd; color: #fff; }
          th, td { border: 1px solid #ccc; padding: 5px 7px; text-align: left; vertical-align: top; font-size: 10pt; }
          tbody tr:nth-child(even) { background: #f6f8fb; }
          .footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
        </style>
      </head>
      <body>
        <h2>Components &mdash; List</h2>
        <table>
          <thead>
            <tr>
              <th>Code</th><th>Name</th><th>Design</th>
              <th>Stitch</th><th>Tilla</th><th>Seq A</th><th>Seq B</th>
              <th>Branch</th><th>Status</th>
            </tr>
          </thead>
          <tbody>${rows}</tbody>
        </table>
        <div class="footer">Generated: ${new Date().toLocaleString()} | Rows: ${this.components.length}</div>
        <script>
          window.addEventListener('load', function () {
            setTimeout(function () { window.print(); }, 100);
          });
        </script>
      </body>
      </html>`;

    const win = window.open('', '_blank');
    if (!win) return alert('Pop-up blocked. Please allow pop-ups for this site.');
    win.document.open();
    win.document.write(html);
    win.document.close();
  }

  private escapeHtml(value: any): string {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.components.length > 0
      && this.components.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.components.forEach((r: any) => this.selectedIds.delete(r.id));
    else this.components.forEach((r: any) => this.selectedIds.add(r.id));
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
    this.loadComponents();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} component(s)?`)) return;
    this.bulkBusy = true;
    this.compSvc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} component(s)? They won't appear in design BOMs while inactive.`)) return;
    this.bulkBusy = true;
    this.compSvc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} component(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.compSvc.bulkDelete(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  Clickable Design badge → /designs?id=
  // ──────────────────────────────────────────────────────────────────
  gotoDesign(row: any): void {
    if (!row?.designId) return;
    location.href = '/Content/emb-products/designs?id=' + row.designId;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  Audit log loader
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.compSvc.getAuditLog(id).subscribe({
      next: (r: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(r?.data) ? r.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')                     return 'bg-primary';
    if (a === 'EDITED')                      return 'bg-warning text-dark';
    if (a === 'ACTIVATED')                   return 'bg-success';
    if (a === 'DEACTIVATED')                 return 'bg-secondary';
    if (a === 'DELETED')                     return 'bg-danger';
    return 'bg-light text-dark';
  }
}
