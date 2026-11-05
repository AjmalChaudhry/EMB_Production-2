import {Component,OnInit,AfterViewInit,ViewChild,ElementRef} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GenBomService } from '../services/gen-bom.service';
import {BomRowViewModel,BomDetailViewModel,BomForm,DropdownItem} from '../shared/models/gen-bom.dto';

declare const bootstrap: any;

@Component({
  selector: 'app-gen-bom',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gen-bom.component.html',
  styleUrls: ['./gen-bom.component.css']
})
export class GenBomComponent implements OnInit, AfterViewInit {

  /* =========================
     STATE
  ========================= */
  loading = false;

  rows: BomRowViewModel[] = [];
  selectedRow: BomRowViewModel | null = null;

  page = 1;
  pageSize = 10;
  totalCount = 0;
  totalPages = 1;

  search = '';
  activeFilter = '';
  showDeleted = false;

  // Phase R.3 - filter + summary + profile
  filterDesignId: number | null = null;
  filterCompId: number | null = null;

  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctDesigns: 0,
    distinctComponents: 0,
    totalRepeats: 0,
    totalHead: 0,
    totalPanni: 0,
    totalSolving: 0,
    byDesign: [] as Array<{ design: string; count: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  designs: DropdownItem[] = [];
  components: DropdownItem[] = [];

  formData: BomForm = this.emptyForm();
  detailData: BomDetailViewModel | null = null;

  deleteId = 0;
  isEdit = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT TIMELINE — lazy-loaded after detail / profile.
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  CLONE-FROM-DESIGN modal state
  //  Lets the designer copy a whole design's BOM (e.g. last season)
  //  to a new design and tweak the diff instead of re-entering 20+
  //  components from scratch.
  // ──────────────────────────────────────────────────────────────────
  showCloneModal = false;
  cloneBusy = false;
  cloneForm: { sourceDesignId: number | null; targetDesignId: number | null } = {
    sourceDesignId: null,
    targetDesignId: null
  };

  /* =========================
     MODALS
  ========================= */
  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  private formModalInstance: any;
  private detailModalInstance: any;
  private deleteModalInstance: any;

  constructor(private svc: GenBomService) { }

  /* =========================
     LIFECYCLE
  ========================= */
  ngOnInit(): void {
    this.loadDropdowns();
    this.loadList();
  }

  ngAfterViewInit(): void {
    if (this.formModal?.nativeElement) {
      this.formModalInstance = new bootstrap.Modal(this.formModal.nativeElement);
    }

    if (this.detailModal?.nativeElement) {
      this.detailModalInstance = new bootstrap.Modal(this.detailModal.nativeElement);
    }

    if (this.deleteModal?.nativeElement) {
      this.deleteModalInstance = new bootstrap.Modal(this.deleteModal.nativeElement);
    }
  }

  /* =========================
     FORM DEFAULTS
  ========================= */
  private emptyForm(): BomForm {
    return {
      id: 0,
      designId: 0,
      compId: 0,
      reqRepeats: 0,
      reqHead: 0,
      panni: 0,
      solving: 0,
      remarks: '',
      active: true
    };
  }

  resetForm(): void {
    this.formData = this.emptyForm();
    this.isEdit = false;
  }

  /* =========================
     LIST
  ========================= */
  loadList(): void {
    this.loading = true;

    this.svc.list(
      this.page,
      this.pageSize,
      this.search?.trim() || '',
      this.activeFilter,
      this.showDeleted,
      this.filterDesignId,
      this.filterCompId
    ).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success) {
          alert(res?.message || 'Failed to load BOM list.');
          this.rows = [];
          this.totalCount = 0;
          this.totalPages = 1;
          return;
        }

        const data = res?.data || {};

        this.rows = data.items ?? [];
        this.page = data.page ?? 1;
        this.pageSize = data.pageSize ?? this.pageSize;

        this.totalCount =
          data.totalCount ??
          data.totalItems ??
          data.total ??
          0;

        this.totalPages =
          data.totalPages ??
          Math.max(1, Math.ceil(this.totalCount / (this.pageSize || 1)));

        const sm = data.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctDesigns: Number(sm.distinctDesigns ?? 0),
            distinctComponents: Number(sm.distinctComponents ?? 0),
            totalRepeats: Number(sm.totalRepeats ?? 0),
            totalHead: Number(sm.totalHead ?? 0),
            totalPanni: Number(sm.totalPanni ?? 0),
            totalSolving: Number(sm.totalSolving ?? 0),
            byDesign: Array.isArray(sm.byDesign) ? sm.byDesign : []
          };
        }
      },
      error: (err) => {
        this.loading = false;
        console.error('BOM list error:', err);
        this.rows = [];
        this.totalCount = 0;
        this.totalPages = 1;
        alert('Unexpected error while loading BOM list.');
      }
    });
  }

  onSearch(): void {
    this.page = 1;
    this.loadList();
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadList();
  }

  // Phase R.3 - filter helpers + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadList(); }
  clearFiltersR(): void {
    this.search = '';
    this.activeFilter = '';
    this.filterDesignId = null;
    this.filterCompId = null;
    this.showDeleted = false;
    this.page = 1;
    this.loadList();
  }
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
        console.error('BOM profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  changePage(p: number): void {
    if (p < 1 || p > this.totalPages || p === this.page) return;
    this.page = p;
    this.loadList();
  }

  get pages(): number[] {
    const arr: number[] = [];
    for (let i = 1; i <= this.totalPages; i++) {
      arr.push(i);
    }
    return arr;
  }

  trackById(_: number, item: BomRowViewModel): number {
    return item.id;
  }

  /* =========================
     DROPDOWNS
  ========================= */
  loadDropdowns(): void {
    this.svc.ddlDesigns().subscribe({
      next: (res) => {
        this.designs = res?.success && res?.data ? res.data : [];
      },
      error: (err) => {
        console.error('Design dropdown error:', err);
        this.designs = [];
      }
    });

    this.svc.ddlComponents().subscribe({
      next: (res) => {
        this.components = res?.success && res?.data ? res.data : [];
      },
      error: (err) => {
        console.error('Component dropdown error:', err);
        this.components = [];
      }
    });
  }

  /* =========================
     CREATE / EDIT
  ========================= */
  openCreate(): void {
    this.resetForm();
    this.isEdit = false;
    this.formModalInstance?.show();
  }

  openEdit(id: number): void {
    if (!id || id <= 0) {
      alert('Invalid BOM selected.');
      return;
    }

    this.isEdit = true;
    this.loading = true;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        this.loading = false;

        const ok = res?.success ?? res?.Success ?? false;
        const d: any = res?.data ?? res?.Data ?? null;

        if (!ok || !d) {
          alert(res?.message || res?.Message || 'Failed to load BOM detail.');
          return;
        }

        this.formData = {
          id: Number(d.id ?? d.Id ?? 0),
          designId: Number(d.designId ?? d.DesignId ?? 0),
          compId: Number(d.compId ?? d.CompId ?? 0),
          reqRepeats: Number(d.reqRepeats ?? d.ReqRepeats ?? 0),
          reqHead: Number(d.reqHead ?? d.ReqHead ?? 0),
          panni: Number(d.panni ?? d.Panni ?? 0),
          solving: Number(d.solving ?? d.Solving ?? 0),
          remarks: d.remarks ?? d.Remarks ?? '',
          active: (d.active ?? d.Active) ?? true
        };

        this.formModalInstance?.show();
      },
      error: (err) => {
        this.loading = false;
        console.error('BOM detail load error:', err);
        alert('Failed to load BOM detail.');
      }
    });
  }

  save(): void {
    if (!this.formData.designId || this.formData.designId <= 0) {
      alert('Design is required.');
      return;
    }

    if (!this.formData.compId || this.formData.compId <= 0) {
      alert('Component is required.');
      return;
    }

    this.loading = true;

    this.svc.save(this.formData).subscribe({
      next: (res) => {
        this.loading = false;

        if (!res?.success) {
          alert(res?.message || 'Save failed.');
          return;
        }

        this.formModalInstance?.hide();
        this.resetForm();
        this.loadList();
      },
      error: (err) => {
        this.loading = false;
        console.error('BOM save error:', err);
        alert('Unexpected save error.');
      }
    });
  }

  cancelForm(): void {
    this.formModalInstance?.hide();
    this.resetForm();
  }

  closeForm(): void {
    this.formModalInstance?.hide();
    this.resetForm();
  }

  /* =========================
     DETAIL
  ========================= */
  openDetail(id: number): void {
    if (!id || id <= 0) {
      alert('Invalid BOM selected.');
      return;
    }

    this.loading = true;
    this.detailData = null;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load BOM detail.');
          return;
        }

        const d: any = res.data;

        this.detailData = {
          id: d.id ?? d.Id ?? 0,

          designId: d.designId ?? d.DesignId ?? 0,
          designName: d.designName ?? d.DesignName ?? '',
          designCode: d.designCode ?? d.DesignCode ?? '',

          compId: d.compId ?? d.CompId ?? 0,
          componentName: d.componentName ?? d.ComponentName ?? '',
          componentCode: d.componentCode ?? d.ComponentCode ?? '',

          reqRepeats: d.reqRepeats ?? d.ReqRepeats ?? 0,
          reqHead: d.reqHead ?? d.ReqHead ?? 0,
          panni: d.panni ?? d.Panni ?? 0,
          solving: d.solving ?? d.Solving ?? 0,

          remarks: d.remarks ?? d.Remarks ?? '',
          active: d.active ?? d.Active ?? true,
          deleted: d.deleted ?? d.Deleted ?? false,

          createdByEmployeeId: d.createdByEmployeeId ?? d.CreatedByEmployeeId ?? null,
          updatedByEmployeeId: d.updatedByEmployeeId ?? d.UpdatedByEmployeeId ?? null,
          createdDateText: d.createdDateText ?? d.CreatedDateText ?? '',
          updatedDateText: d.updatedDateText ?? d.UpdatedDateText ?? '',

          materials: d.materials ?? d.Materials ?? [],

          materialCost: d.materialCost ?? d.MaterialCost ?? 0,
          laborCost: d.laborCost ?? d.LaborCost ?? 0,
          machineCost: d.machineCost ?? d.MachineCost ?? 0,
          totalCost: d.totalCost ?? d.TotalCost ?? 0
        };

        this.detailModalInstance?.show();
      },
      error: (err) => {
        this.loading = false;
        console.error('BOM detail error:', err);
        alert('Unexpected error while loading BOM detail.');
      }
    });
  }

  closeDetail(): void {
    this.detailModalInstance?.hide();
    this.detailData = null;
  }

  /* =========================
     DELETE
  ========================= */
  confirmDelete(row: BomRowViewModel): void {
    if (!row?.id) {
      alert('Invalid BOM selected.');
      return;
    }

    this.deleteId = row.id;
    this.selectedRow = row;
    this.deleteModalInstance?.show();
  }

  doDelete(): void {
    if (!this.deleteId || this.deleteId <= 0) return;

    this.loading = true;

    this.svc.delete(this.deleteId).subscribe({
      next: (res) => {
        this.loading = false;

        if (!res?.success) {
          alert(res?.message || 'Delete failed.');
          return;
        }

        this.deleteModalInstance?.hide();
        this.deleteId = 0;
        this.selectedRow = null;
        this.loadList();
      },
      error: (err) => {
        this.loading = false;
        console.error('BOM delete error:', err);
        alert('Unexpected delete error.');
      }
    });
  }

  closeDelete(): void {
    this.deleteModalInstance?.hide();
    this.deleteId = 0;
    this.selectedRow = null;
  }

  /* =========================
     HELPERS
  ========================= */
  getSelectedComponent(): string {
    const item = this.components.find(x => x.id === this.formData.compId);
    if (!item) return '';
    return `${item.code || ''}${item.code && item.name ? ' - ' : ''}${item.name || ''}`;
  }

  /* =========================
     EXPORTS
  ========================= */
  exportExcel(): void {
    this.svc.exportExcel(this.search, this.activeFilter, this.showDeleted);
  }

  exportPdf(): void {
    this.svc.exportPdf(this.search, this.activeFilter, this.showDeleted);
  }

  print(): void {
    this.svc.print(this.search, this.activeFilter, this.showDeleted);
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  CLICKABLE BADGES
  //  Deep-link from the BOM row to the upstream Design / Component
  //  master with the id pre-selected.
  // ──────────────────────────────────────────────────────────────────
  gotoDesign(row: any): void {
    if (!row?.designId) return;
    location.href = '/Content/emb-products/designs?id=' + row.designId;
  }
  gotoComponent(row: any): void {
    if (!row?.compId) return;
    location.href = '/Content/emb-products/components?id=' + row.compId;
  }

  /** Sprint-1: a summary chip per design lets the designer filter to
   *  "show me all components for DES-2026-010" in a single click. */
  filterByDesignChip(designCode: string): void {
    const match = this.designs.find((d: any) =>
      d?.code === designCode || d?.text?.startsWith(designCode));
    if (!match) return;
    this.filterDesignId = (match as any).id ?? null;
    this.loadList();
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG loader (best-effort).
  // ──────────────────────────────────────────────────────────────────
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.svc.getAuditLog(id).subscribe({
      next: (res: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(res?.data) ? res.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')                                       return 'bg-primary';
    if (a === 'EDITED')                                        return 'bg-warning text-dark';
    if (a === 'ACTIVATED')                                     return 'bg-success';
    if (a === 'DEACTIVATED')                                   return 'bg-secondary';
    if (a === 'DELETED')                                       return 'bg-danger';
    if (a === 'CLONED')                                        return 'bg-info';
    return 'bg-light text-dark';
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions
  // ──────────────────────────────────────────────────────────────────
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    const rs = (this.rows ?? []);
    return rs.length > 0 && rs.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    const rs = (this.rows ?? []);
    if (this.allVisibleSelected()) rs.forEach((r: any) => this.selectedIds.delete(r.id));
    else rs.forEach((r: any) => this.selectedIds.add(r.id));
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
    if (!confirm(`Activate ${ids.length} BOM row(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} BOM row(s)? They'll be hidden from production but not deleted.`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} BOM row(s)? This is a soft-delete; the rows can be re-created later.`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  CLONE-FROM-DESIGN
  //  Copies every live BOM row from sourceDesignId → targetDesignId.
  //  Components that already exist on the target are skipped and
  //  reported in the failure summary so the call is idempotent.
  // ──────────────────────────────────────────────────────────────────
  openCloneFromDesign(): void {
    this.cloneForm = { sourceDesignId: null, targetDesignId: null };
    this.showCloneModal = true;
  }
  closeCloneModal(): void {
    this.showCloneModal = false;
  }
  doClone(): void {
    if (!this.cloneForm.sourceDesignId || !this.cloneForm.targetDesignId) {
      return alert('Pick both a source and a target design.');
    }
    if (this.cloneForm.sourceDesignId === this.cloneForm.targetDesignId) {
      return alert('Source and target must be different designs.');
    }
    this.cloneBusy = true;
    this.svc.cloneFromDesign(this.cloneForm.sourceDesignId, this.cloneForm.targetDesignId).subscribe({
      next: (r: any) => {
        this.cloneBusy = false;
        if (!r?.success) return alert(r?.message || 'Clone failed.');
        const d = r?.data ?? {};
        let msg = `${d.cloned || 0} row(s) cloned, ${d.skipped || 0} skipped.`;
        if (Array.isArray(d.failures) && d.failures.length) {
          msg += '\n\nSkipped:\n' + d.failures.slice(0, 10)
            .map((f: any) => `  • ${f.code}: ${f.reason}`).join('\n');
          if (d.failures.length > 10) msg += `\n  …and ${d.failures.length - 10} more.`;
        }
        alert(msg);
        this.closeCloneModal();
        this.loadList();
      },
      error: (err: any) => {
        this.cloneBusy = false;
        alert(err?.error?.message || err?.message || 'Clone failed.');
      }
    });
  }
}
