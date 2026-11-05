import { Component, OnInit, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { finalize } from 'rxjs/operators';

import { MachineCostProfileService } from '../services/machine-cost-profile.service';
import { DropdownDataService } from '../shared/services/dropdown-data.service';

import { MachineCostProfileDto } from '../shared/models/machine-cost-profile.dto';
import { DropdownItem, ApiPagedResponse } from '../shared/models/api-response';

declare var bootstrap: any;

@Component({
  selector: 'app-machine-cost-profile',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './machine-cost-profile.html',
  styleUrls: ['./machine-cost-profile.css']
})
export class MachineCostProfileComponent implements OnInit {

  constructor(
    private svc: MachineCostProfileService,
    private dropdownSvc: DropdownDataService
  ) { }

  // ===============================
  // DATA
  // ===============================

  rows: MachineCostProfileDto[] = [];
  machines: DropdownItem[] = [];
  detailData: MachineCostProfileDto | null = null;

  load(): void { this.page = 1; this.loadData(); }
  loading = false;

  // ===============================
  // PAGINATION
  // ===============================

  page = 1;
  pageSize = 10;
  totalCount = 0;
  totalPages = 1;

  search = '';
  activeFilter = '';
  machineFilter: number | null = null;

  // Phase R.6 - summary + profile
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    distinctMachines: 0,
    avgHourly: 0,
    avgOperator: 0,
    avgPower: 0,
    avgStitch: 0,
    avgElectricityUnit: 0,
    avgTotalHourly: 0
  };
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ===============================
  // FORM
  // ===============================

  form: any = this.buildForm();
  editingId: number | null = null;

  // ===============================
  // MODALS
  // ===============================

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;

  private formModalInstance: any;
  private deleteModalInstance: any;
  private detailModalInstance: any;

  deleteId: number | null = null;
  

  saving = false;

  // ===============================
  // INIT
  // ===============================

  ngOnInit(): void {
    this.loadMachines();
    this.loadData();
  }

  // ===============================
  // DROPDOWN
  // ===============================

  loadMachines(): void {
    this.dropdownSvc.getMachines().subscribe({
      next: list => {
        this.machines = list ?? [];
      },
      error: err => {
        console.error('Machine dropdown failed', err);
        this.machines = [];
      }
    });
  }

  // ===============================
  // LOAD LIST
  // ===============================

  loadData(): void {

    this.loading = true;

    this.svc.list({
      page: this.page,
      pageSize: this.pageSize,
      search: this.search,
      activeFilter: this.activeFilter,
      machineId: this.machineFilter
    }).subscribe({

      next: res => {

        if (!res?.success) {
          this.loading = false;
          return;
        }

        this.rows = res.data?.items ?? [];
        this.totalCount = res.data?.totalItems ?? 0;

        const sm: any = (res.data as any)?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            distinctMachines: Number(sm.distinctMachines ?? 0),
            avgHourly: Number(sm.avgHourly ?? 0),
            avgOperator: Number(sm.avgOperator ?? 0),
            avgPower: Number(sm.avgPower ?? 0),
            avgStitch: Number(sm.avgStitch ?? 0),
            avgElectricityUnit: Number(sm.avgElectricityUnit ?? 0),
            avgTotalHourly: Number(sm.avgTotalHourly ?? 0)
          };
        }

        this.totalPages = Math.max(
          1,
          Math.ceil(this.totalCount / this.pageSize)
        );

        this.loading = false;
      },

      error: err => {
        console.error(err);
        this.loading = false;
      }

    });
  }

  // Phase R.6 - filter + profile drawer
  onFiltersChange(): void { this.page = 1; this.loadData(); }
  clearFiltersR(): void {
    this.search = '';
    this.activeFilter = '';
    this.machineFilter = null;
    this.page = 1;
    this.loadData();
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
        console.error('CostProfile profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }
  // ===============================
  // PAGINATION
  // ===============================

  changePage(p: number): void {

    if (p < 1 || p > this.totalPages) return;

    this.page = p;
    this.loadData();
  }

  // ===============================
  // FORM BUILD
  // ===============================

  buildForm() {
    return {
      Id: 0,

      MachineId: 0,

      HourlyRate: 0,
      PowerKW: 0,
      StitchPerMinute: 0,

      OperatorHourlyRate: 0,
      ElectricityUnitRate: 0,

      EffectiveFrom: null,

      Active: true
    };
  }

  // ===============================
  // OPEN CREATE
  // ===============================

  openForm(): void {

    // reset editing mode
    this.editingId = null;

    // reset form
    this.form = this.buildForm();
    this.form.Id = 0;

    // create modal only once
    if (!this.formModalInstance) {
      this.formModalInstance = new bootstrap.Modal(this.formModal.nativeElement);
    }

    this.formModalInstance.show();
  }

  closeForm(): void {

    if (this.formModalInstance) {
      this.formModalInstance.hide();
    }

  }

  // ===============================
  // EDIT
  // ===============================

  edit(row: MachineCostProfileDto): void {

    this.editingId = row.Id;

    this.form = {
      Id: row.Id,

      MachineId: row.MachineId,

      HourlyRate: row.HourlyRate ?? 0,
      PowerKW: row.PowerKW ?? 0,
      StitchPerMinute: row.StitchPerMinute ?? 0,

      OperatorHourlyRate: row.OperatorHourlyRate ?? 0,
      ElectricityUnitRate: row.ElectricityUnitRate ?? 0,

      EffectiveFrom: row.EffectiveFromText,

      Active: row.Active ?? true
    };

    if (!this.formModalInstance) {
      this.formModalInstance = new bootstrap.Modal(this.formModal.nativeElement);
    }

    this.formModalInstance.show();
  }

  // ===============================
  // SAVE
  // ===============================

  save(): void {

    if (this.saving) return;

    this.saving = true;

    const payload: any = { ...this.form };
    
    delete payload.id;

    this.svc.save(payload)
      .pipe(finalize(() => this.saving = false))
      .subscribe({

        next: res => {

          if (!res?.success) return;

          this.closeForm();
          this.loadData();

        },

        error: err => console.error(err)

      });

  }

  openDetail(id: number): void {

    this.svc.detail(id).subscribe(res => {

      if (!res?.success) return;

      this.detailData = res.data;

      this.detailModalInstance =
        new bootstrap.Modal(this.detailModal.nativeElement);

      this.detailModalInstance.show();
    });

  }

  closeDetail(): void {
    this.detailModalInstance?.hide();
  }

  // ===============================
  // DELETE
  // ===============================

  confirmDelete(id: number): void {

    this.deleteId = id;

    this.deleteModalInstance =
      new bootstrap.Modal(this.deleteModal.nativeElement);

    this.deleteModalInstance.show();
  }

  closeDelete(): void {
    this.deleteModalInstance?.hide();
  }

  doDelete(): void {

    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe({

      next: res => {

        this.closeDelete();
        this.loadData();
      },

      error: err => console.error(err)

    });
  }

  get electricCostPerHour(): number {

    const power = this.form.PowerKW || 0;
    const rate = this.form.ElectricityUnitRate || 0;

    return power * rate;
  }

  get totalMachineRunningCost(): number {

    const machine = this.form.HourlyRate || 0;
    const operator = this.form.OperatorHourlyRate || 0;
    const electric = this.electricCostPerHour;

    return machine + operator + electric;
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
    this.loadData();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} cost profile(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} cost profile(s)?`)) return;
    this.bulkBusy = true;
    this.svc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} cost profile(s)? (soft-delete)`)) return;
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
