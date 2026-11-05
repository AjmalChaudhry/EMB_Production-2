import {Component,OnInit,AfterViewInit,ElementRef,ViewChild} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { GenComponentMaterialService } from '../services/component-material.service';
import {ComponentMaterialRowViewModel,ComponentMaterialDetailViewModel,
        ComponentMaterialForm,DropdownItem} from '../shared/models/component-material.dto';

declare const bootstrap: any;

@Component({
  selector: 'app-component-materials',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './component-materials.html',
  styleUrls: ['./component-materials.css']
})
export class ComponentMaterialsComponent implements OnInit, AfterViewInit {

  loading = false;

  rows: ComponentMaterialRowViewModel[] = [];
  detailData: ComponentMaterialDetailViewModel | null = null;
  selectedRow: ComponentMaterialRowViewModel | null = null;

  page = 1;
  pageSize = 10;
  totalCount = 0;
  totalPages = 1;

  search = '';
  activeFilter = '';
  showDeleted = false;
  compFilter: number | null = null;
  rawItemFilter: number | null = null;
  unitFilter: number | null = null;

  // Phase R.2 summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctComponents: 0,
    distinctRawItems: 0,
    totalQty: 0,
    byUnit: [] as Array<{ unit: string; count: number; totalQty: number }>
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  components: DropdownItem[] = [];
  rawItems: DropdownItem[] = [];
  units: DropdownItem[] = [];

  formData: ComponentMaterialForm = this.emptyForm();
  isEdit = false;
  deleteId = 0;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  private formModalInstance: any;
  private detailModalInstance: any;
  private deleteModalInstance: any;

  constructor(private svc: GenComponentMaterialService) { }

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

  private emptyForm(): ComponentMaterialForm {
    return {
      id: 0,
      compId: 0,
      rawItemId: 0,
      qtyPerUnit: 0,
      unitId: null,
      remarks: '',
      active: true
    };
  }

  resetForm(): void {
    this.formData = this.emptyForm();
    this.isEdit = false;
  }

  loadDropdowns(): void {
    this.svc.ddlComponents().subscribe({
      next: (res: any) => {
        const items = res?.data ?? res?.Data ?? [];
        this.components = items.map((x: any) => ({
          id: x.id ?? x.Id ?? 0,
          code: x.code ?? x.Code ?? '',
          name: x.name ?? x.Name ?? ''
        }));
        console.log('components:', this.components);
      },
      error: (err) => {
        console.error('ddlComponents error:', err);
        this.components = [];
      }
    });

    this.svc.ddlRawItems().subscribe({
      next: (res: any) => {
        const items = res?.data ?? res?.Data ?? [];
        this.rawItems = items.map((x: any) => ({
          id: x.id ?? x.Id ?? 0,
          code: x.code ?? x.Code ?? '',
          name: x.name ?? x.Name ?? ''
        }));
        console.log('rawItems:', this.rawItems);
      },
      error: (err) => {
        console.error('ddlRawItems error:', err);
        this.rawItems = [];
      }
    });

    this.svc.ddlUnits().subscribe({
      next: (res: any) => {
        const items = res?.data ?? res?.Data ?? [];
        this.units = items.map((x: any) => ({
          id: x.id ?? x.Id ?? 0,
          code: x.code ?? x.Code ?? '',
          name: x.name ?? x.Name ?? ''
        }));
        console.log('units:', this.units);
      },
      error: (err) => {
        console.error('ddlUnits error:', err);
        this.units = [];
      }
    });
  }

  loadList(): void {
    this.loading = true;

    this.svc.list(
      this.page,
      this.pageSize,
      this.search,
      this.activeFilter,
      this.showDeleted,
      this.compFilter
    ).subscribe({
      next: (res: any) => {
        this.loading = false;

        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          this.rows = [];
          this.totalCount = 0;
          this.totalPages = 1;
          alert(res?.message ?? res?.Message ?? 'Failed to load component materials.');
          return;
        }

        const d = res?.data ?? res?.Data ?? {};

        const items = d?.items ?? d?.Items ?? [];

        this.rows = items.map((x: any) => ({
          id: x.id ?? x.Id ?? 0,

          compId: x.compId ?? x.CompId ?? 0,
          componentCode: x.componentCode ?? x.ComponentCode ?? '',
          componentName: x.componentName ?? x.ComponentName ?? '',

          rawItemId: x.rawItemId ?? x.RawItemId ?? 0,
          rawItemName: x.rawItemName ?? x.RawItemName ?? '',

          qtyPerUnit: x.qtyPerUnit ?? x.QtyPerUnit ?? 0,
          unitId: x.unitId ?? x.UnitId ?? null,
          unitCode: x.unitCode ?? x.UnitCode ?? '',
          unitName: x.unitName ?? x.UnitName ?? '',

          remarks: x.remarks ?? x.Remarks ?? '',
          active: x.active ?? x.Active ?? false,
          deleted: x.deleted ?? x.Deleted ?? false,
          // Sprint-1 — 24-hour "NEW" badge flag.
          isRecent: (x.isRecent ?? x.IsRecent) === true
        }));

        this.page = d?.page ?? d?.Page ?? 1;
        this.pageSize = d?.pageSize ?? d?.PageSize ?? this.pageSize;
        this.totalCount = d?.totalCount ?? d?.TotalCount ?? 0;
        this.totalPages = d?.totalPages ?? d?.TotalPages ?? 1;

        const sm = d?.summary ?? d?.Summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? sm.Count ?? 0),
            activeCount: Number(sm.activeCount ?? sm.ActiveCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? sm.InactiveCount ?? 0),
            distinctComponents: Number(sm.distinctComponents ?? sm.DistinctComponents ?? 0),
            distinctRawItems: Number(sm.distinctRawItems ?? sm.DistinctRawItems ?? 0),
            totalQty: Number(sm.totalQty ?? sm.TotalQty ?? 0),
            byUnit: Array.isArray(sm.byUnit ?? sm.ByUnit)
              ? (sm.byUnit ?? sm.ByUnit).map((b: any) => ({
                  unit: b.unit ?? b.Unit ?? '',
                  count: Number(b.count ?? b.Count ?? 0),
                  totalQty: Number(b.qty ?? b.Qty ?? 0)
                }))
              : []
          };
        }
      },
      error: (err) => {
        this.loading = false;
        console.error(err);
        alert('Unexpected error while loading component materials.');
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

  // Phase R.2 - filter helpers + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadList(); }
  clearFiltersR(): void {
    this.search = '';
    this.activeFilter = '';
    this.compFilter = null;
    this.rawItemFilter = null;
    this.unitFilter = null;
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
        // Sprint-2: lazy-load the audit timeline alongside the profile.
        this.loadAuditLog(numericId);
      },
      error: err => {
        this.profileLoading = false;
        console.error('ComponentMaterial profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; this.auditLog = []; }

  changePage(p: number): void {
    if (p < 1 || p > this.totalPages || p === this.page) return;
    this.page = p;
    this.loadList();
  }

  get pages(): number[] {
    const arr: number[] = [];
    for (let i = 1; i <= this.totalPages; i++) arr.push(i);
    return arr;
  }

  openCreate(): void {
    this.resetForm();
    this.formModalInstance?.show();
  }

  openEdit(id: number): void {
    this.isEdit = true;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load detail.');
          return;
        }

        const d: any = res.data;

        this.formData = {
          id: d.id ?? d.Id ?? 0,
          compId: d.compId ?? d.CompId ?? 0,
          rawItemId: d.rawItemId ?? d.RawItemId ?? 0,
          qtyPerUnit: d.qtyPerUnit ?? d.QtyPerUnit ?? 0,
          unitId: d.unitId ?? d.UnitId ?? null,
          remarks: d.remarks ?? d.Remarks ?? '',
          active: d.active ?? d.Active ?? true
        };

        this.formModalInstance?.show();
      },
      error: (err) => {
        console.error(err);
        alert('Failed to load detail.');
      }
    });
  }

  save(): void {
    if (!this.formData.compId || this.formData.compId <= 0) {
      alert('Component is required.');
      return;
    }

    if (!this.formData.rawItemId || this.formData.rawItemId <= 0) {
      alert('Raw item is required.');
      return;
    }

    if (!this.formData.qtyPerUnit || this.formData.qtyPerUnit <= 0) {
      alert('Qty per unit must be greater than zero.');
      return;
    }

    this.svc.save(this.formData).subscribe({
      next: (res: any) => {
        if (!res?.success) {
          alert(res?.message || 'Save failed.');
          return;
        }

        this.formModalInstance?.hide();
        this.resetForm();
        this.loadList();
      },
      error: (err) => {
        console.error(err);
        alert('Unexpected save error.');
      }
    });
  }

  openDetail(id: number): void {
    this.detailData = null;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load detail.');
          return;
        }

        const d: any = res.data;

        this.detailData = {
          id: d.id ?? d.Id ?? 0,
          compId: d.compId ?? d.CompId ?? 0,
          componentCode: d.componentCode ?? d.ComponentCode ?? '',
          componentName: d.componentName ?? d.ComponentName ?? '',
          rawItemId: d.rawItemId ?? d.RawItemId ?? 0,
          rawItemName: d.rawItemName ?? d.RawItemName ?? '',
          qtyPerUnit: d.qtyPerUnit ?? d.QtyPerUnit ?? 0,
          unitId: d.unitId ?? d.UnitId ?? null,
          unitCode: d.unitCode ?? d.UnitCode ?? '',
          unitName: d.unitName ?? d.UnitName ?? '',
          remarks: d.remarks ?? d.Remarks ?? '',
          active: d.active ?? d.Active ?? true,
          deleted: d.deleted ?? d.Deleted ?? false,
          createdByEmployeeId: d.createdByEmployeeId ?? d.CreatedByEmployeeId ?? null,
          updatedByEmployeeId: d.updatedByEmployeeId ?? d.UpdatedByEmployeeId ?? null,
          createdDateText: d.createdDateText ?? d.CreatedDateText ?? '',
          updatedDateText: d.updatedDateText ?? d.UpdatedDateText ?? ''
        };

        this.detailModalInstance?.show();
      },
      error: (err) => {
        console.error(err);
        alert('Unexpected detail error.');
      }
    });
  }

  confirmDelete(row: ComponentMaterialRowViewModel): void {
    this.selectedRow = row;
    this.deleteId = row.id;
    this.deleteModalInstance?.show();
  }

  doDelete(): void {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe({
      next: (res: any) => {
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
        console.error(err);
        alert('Unexpected delete error.');
      }
    });
  }

  closeForm(): void {
    this.formModalInstance?.hide();
    this.resetForm();
  }

  closeDetail(): void {
    this.detailModalInstance?.hide();
    this.detailData = null;
  }

  closeDelete(): void {
    this.deleteModalInstance?.hide();
    this.deleteId = 0;
    this.selectedRow = null;
  }

  trackById(_: number, item: ComponentMaterialRowViewModel): number {
    return item.id;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions toolbar
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
    if (!confirm(`Activate ${ids.length} component material(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} component material(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} component material(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  Clickable badges
  // ──────────────────────────────────────────────────────────────────
  gotoComponent(row: any): void {
    if (!row?.compId) return;
    location.href = '/Content/emb-products/components?id=' + row.compId;
  }
  gotoRawItem(row: any): void {
    if (!row?.rawItemId) return;
    location.href = '/Content/emb-products/raw-items?id=' + row.rawItemId;
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
    if (a === 'CREATED')                     return 'bg-primary';
    if (a === 'EDITED')                      return 'bg-warning text-dark';
    if (a === 'ACTIVATED')                   return 'bg-success';
    if (a === 'DEACTIVATED')                 return 'bg-secondary';
    if (a === 'DELETED')                     return 'bg-danger';
    return 'bg-light text-dark';
  }
}
