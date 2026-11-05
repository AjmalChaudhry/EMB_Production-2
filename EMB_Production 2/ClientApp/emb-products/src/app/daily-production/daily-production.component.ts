import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DropdownItem } from '../shared/models/api-response';
import { DailyProductionService } from '../services/daily-production.service';
import {DailyProductionRowViewModel,DailyProductionInputDto,DailyProductionDetailViewModel,
WorkOrderItemDropdownVm,ReworkTaskVm } from '../shared/models/daily-production.dto';

declare var bootstrap: any;

@Component({
  selector: 'app-daily-production',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './daily-production.component.html',
  styleUrls: ['./daily-production.component.css']
})
export class DailyProductionComponent implements OnInit {

  items: DailyProductionRowViewModel[] = [];
  loading = false;

  // Phase Q.0 filters
  filterFromDate = '';
  filterToDate = '';
  filterWorkOrderId: number | null = null;
  filterMachineId: number | null = null;
  filterOperatorId: number | null = null;
  filterShift = '';
  filterPosted = '';
  filterSentToQc = '';
  filterStatus = '';

  summary = {
    count: 0,
    postedCount: 0,
    draftCount: 0,
    reversedCount: 0,
    sentToQcCount: 0,
    qcDoneCount: 0,
    plannedTotal: 0,
    actualTotal: 0,
    acceptedTotal: 0,
    rejectedTotal: 0,
    stitchesTotal: 0,
    yieldPct: 0,
    qcPassPct: 0,
    // Phase S.1 - machine cost rollup
    totalProductionCost: 0,
    costPerAcceptedPc: 0,
    totalProductiveHours: 0,
    totalStopHours: 0,
    rowsWithCostProfile: 0,
    rowsMissingCostProfile: 0,
    byStatus: [] as Array<{ status: string; count: number }>,
    byShift:  [] as Array<{ shift: string; count: number }>
  };

  // Phase Q.1 profile drawer
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  WORKFLOW STEPPER + AUDIT TIMELINE
  //  workflowStages comes from the detail payload (canonical pipeline).
  //  auditLog is loaded lazily after the detail drawer opens.
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['Draft', 'PostedToWIP', 'SentToQC', 'Completed', 'Reversed'];
  workflowStageIndex = 0;
  auditLog: any[] = [];
  auditLogLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions toolbar.
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  modalMode: 'create' | 'edit' = 'create';
  selectedId: number | null = null;

  detailModel: DailyProductionDetailViewModel | null = null;
  deleteModel: DailyProductionRowViewModel | null = null;
  reverseModel: DailyProductionRowViewModel | null = null;
  reverseReason = '';

  @ViewChild('dpModal') dpModal!: ElementRef;
  @ViewChild('dpDetailModal') dpDetailModal!: ElementRef;
  @ViewChild('dpDeleteModal') dpDeleteModal!: ElementRef;
  @ViewChild('dpReverseModal') dpReverseModal!: ElementRef;

  currentModal: any = null;

  formModel: DailyProductionInputDto = this.createEmptyForm();

  workOrderItems: WorkOrderItemDropdownVm[] = [];
  selectedWorkOrderItem: WorkOrderItemDropdownVm | null = null;

  // Rework loop — the operator can pick a QC task that's awaiting
  // rework. Picking one auto-fills the WO Item + ActualQty and tags
  // the entry so PostToWip routes the parent QC back to QC.
  reworkTasks: ReworkTaskVm[] = [];
  selectedReworkTaskId: number | null = null;
  entryMode: 'production' | 'rework' = 'production';

  // UI-only fields for the Production Date + time-only inputs.
  // One row = one shift on one day. These are composed into
  // formModel.startTime / formModel.endTime when saving.
  uiProductionDate = '';
  uiStartTime = '';
  uiEndTime = '';

  machines: DropdownItem[] = [];
  operators: DropdownItem[] = [];

  constructor(private dpSvc: DailyProductionService) { }

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadData();
  }

  private createEmptyForm(): DailyProductionInputDto {
    return {
      workOrderItemId: null,
      machineId: null,
      operatorId: null,
      shift: null,
      startTime: null,
      endTime: null,
      stitches: null,
      actualQty: null,
      rejectedQty: 0,
      remarks: '',
      sourceQcInspectionId: null
    };
  }

  private normalizeForm(): DailyProductionInputDto {
    return {
      ...this.formModel,
      workOrderItemId: this.formModel.workOrderItemId != null ? Number(this.formModel.workOrderItemId) : null,
      machineId: this.formModel.machineId != null ? Number(this.formModel.machineId) : null,
      operatorId: this.formModel.operatorId != null ? Number(this.formModel.operatorId) : null,
      actualQty: this.formModel.actualQty != null ? Number(this.formModel.actualQty) : null,
      rejectedQty: this.formModel.rejectedQty != null ? Number(this.formModel.rejectedQty) : 0,
      stitches: this.formModel.stitches != null ? Number(this.formModel.stitches) : null,
      remarks: (this.formModel.remarks || '').trim()
    };
  }

  loadDropdowns(): void {
    this.dpSvc.getWorkOrderItemDropdown().subscribe({
      next: res => this.workOrderItems = res || [],
      error: () => this.workOrderItems = []
    });

    this.dpSvc.getMachineDropdown().subscribe({
      next: res => this.machines = res || [],
      error: () => this.machines = []
    });

    this.dpSvc.getOperatorDropdown().subscribe({
      next: res => this.operators = res || [],
      error: () => this.operators = []
    });

    this.dpSvc.getReworkTasks().subscribe({
      next: res => this.reworkTasks = res || [],
      error: () => this.reworkTasks = []
    });
  }

  onEntryModeChange(mode: 'production' | 'rework'): void {
    this.entryMode = mode;

    this.formModel.workOrderItemId = null;
    this.formModel.sourceQcInspectionId = null;
    this.formModel.actualQty = null;
    this.formModel.rejectedQty = 0;
    this.formModel.stitches = null;
    this.selectedReworkTaskId = null;
    this.selectedWorkOrderItem = null;
  }

  onReworkTaskChange(): void {
    const id = this.selectedReworkTaskId != null ? Number(this.selectedReworkTaskId) : null;
    const task = (this.reworkTasks || []).find(t => Number(t.qcInspectionId) === Number(id)) || null;

    if (!task) {
      this.formModel.sourceQcInspectionId = null;
      this.formModel.workOrderItemId = null;
      this.formModel.actualQty = null;
      return;
    }

    this.formModel.sourceQcInspectionId = task.qcInspectionId;
    this.formModel.workOrderItemId = task.workOrderItemId;
    this.formModel.actualQty = Number(task.reworkQty || 0);
    this.formModel.rejectedQty = 0;

    if (task.machineId && !this.formModel.machineId) {
      this.formModel.machineId = Number(task.machineId);
    }

    // Build/find a placeholder dropdown entry so the form's WO Item label
    // shows the right text. We piggyback on the existing WO Item list.
    // The QC row carries the resolved stitchesPerPiece for the component;
    // pulling it through here is what makes auto-fill fire on rework.
    if (task.workOrderItemId) {
      const existing = (this.workOrderItems || []).find(
        x => Number(x.id) === Number(task.workOrderItemId)
      );

      if (existing) {
        // Refresh stitchesPerPiece from the rework task — the QC-side
        // resolution may have a more authoritative count.
        if (Number(task.stitchesPerPiece || 0) > 0) {
          existing.stitchesPerPiece = Number(task.stitchesPerPiece);
        }
      } else {
        this.workOrderItems = [
          {
            id: Number(task.workOrderItemId),
            name: task.name,
            workOrderLabel: task.workOrderLabel,
            productName: task.productName,
            designName: task.designName,
            componentName: '',
            plannedQty: 0,
            completedQty: 0,
            remainingQty: Number(task.reworkQty || 0),
            isCompleted: false,
            machineId: task.machineId,
            stitchesPerPiece: Number(task.stitchesPerPiece || 0)
          } as WorkOrderItemDropdownVm,
          ...(this.workOrderItems || [])
        ];
      }
      this.onWorkOrderItemChange();
    }
  }

  loadData(): void {
    this.loading = true;

    this.dpSvc.getList({
      fromDate: this.filterFromDate || null,
      toDate: this.filterToDate || null,
      workOrderIdFilter: this.filterWorkOrderId,
      machineIdFilter: this.filterMachineId,
      operatorIdFilter: this.filterOperatorId,
      shift: this.filterShift || null,
      posted: this.filterPosted || null,
      sentToQc: this.filterSentToQc || null,
      status: this.filterStatus || null
    }).subscribe({
      next: (res: any) => {
        const list = Array.isArray(res?.items) ? res.items : [];
        this.items = list.map((x: any) => ({
          ...x,
          isPosted: x.isPosted === true,
          isReversed: x.isReversed === true,
          isSentToQC: x.isSentToQC === true,
          hasQc: x.hasQc === true,
          qcCompleted: x.qcCompleted === true,
          canEdit: x.canEdit === true,
          canDelete: x.canDelete === true,
          canPost: x.canPost === true,
          canReverse: x.canReverse === true
        }));

        const sm = res?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            postedCount: Number(sm.postedCount ?? 0),
            draftCount: Number(sm.draftCount ?? 0),
            reversedCount: Number(sm.reversedCount ?? 0),
            sentToQcCount: Number(sm.sentToQcCount ?? 0),
            qcDoneCount: Number(sm.qcDoneCount ?? 0),
            plannedTotal: Number(sm.plannedTotal ?? 0),
            actualTotal: Number(sm.actualTotal ?? 0),
            acceptedTotal: Number(sm.acceptedTotal ?? 0),
            rejectedTotal: Number(sm.rejectedTotal ?? 0),
            stitchesTotal: Number(sm.stitchesTotal ?? 0),
            yieldPct: Number(sm.yieldPct ?? 0),
            qcPassPct: Number(sm.qcPassPct ?? 0),
            totalProductionCost: Number(sm.totalProductionCost ?? 0),
            costPerAcceptedPc: Number(sm.costPerAcceptedPc ?? 0),
            totalProductiveHours: Number(sm.totalProductiveHours ?? 0),
            totalStopHours: Number(sm.totalStopHours ?? 0),
            rowsWithCostProfile: Number(sm.rowsWithCostProfile ?? 0),
            rowsMissingCostProfile: Number(sm.rowsMissingCostProfile ?? 0),
            byStatus: Array.isArray(sm.byStatus) ? sm.byStatus : [],
            byShift:  Array.isArray(sm.byShift)  ? sm.byShift  : []
          };
        }
      },
      error: () => this.items = [],
      complete: () => this.loading = false
    });
  }

  // Phase Q.0 - filter helpers
  onFiltersChange(): void { this.loadData(); }
  clearFilters(): void {
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterWorkOrderId = null;
    this.filterMachineId = null;
    this.filterOperatorId = null;
    this.filterShift = '';
    this.filterPosted = '';
    this.filterSentToQc = '';
    this.filterStatus = '';
    this.loadData();
  }
  productionStatusPill(s: string): { cssClass: string; icon: string } {
    const u = (s ?? '').toString().trim().toUpperCase();
    if (u === 'SENTTOQC' || u === 'SENT TO QC') return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-clipboard-check' };
    if (u === 'POSTEDTOWIP' || u === 'POSTED TO WIP' || u === 'COMPLETED' || u === 'DONE' || u === 'POSTED') return { cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'REWORK' || u === 'REWORKPOSTED') return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-arrow-repeat' };
    if (u === 'INPROGRESS' || u.includes('PROGRESS') || u === 'WIP') return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    if (u === 'REVERSED' || u === 'CANCELLED' || u === 'CANCELED') return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-arrow-counterclockwise' };
    if (u === 'PENDING' || u === 'DRAFT' || !u) return { cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  CLICKABLE BADGES
  //  Deep-link helpers for the WO / Product / Design / Machine /
  //  Operator badges in the list row. Each navigates to its target
  //  module with a search-query pre-filter so the operator lands
  //  directly on the right record.
  // ──────────────────────────────────────────────────────────────────
  gotoWorkOrder(row: any): void {
    if (!row?.workOrderId) return;
    location.href = `/Content/emb-products/work-order?search=${encodeURIComponent(row.workOrderLabel || '')}`;
  }
  gotoProduct(row: any): void {
    if (!row?.productId) return;
    location.href = `/Content/emb-products/products?id=${row.productId}`;
  }
  gotoDesign(row: any): void {
    if (!row?.designId) return;
    location.href = `/Content/emb-products/designs?id=${row.designId}`;
  }
  gotoMachine(row: any): void {
    if (!row?.machineId) return;
    location.href = `/Content/emb-products/machines?id=${row.machineId}`;
  }
  gotoOperator(row: any): void {
    if (!row?.operatorId) return;
    location.href = `/Content/emb-products/users?id=${row.operatorId}`;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  QC chip helper
  // ──────────────────────────────────────────────────────────────────
  qcChip(row: any): { label: string; cssClass: string; icon: string } {
    if (row?.qcCompleted) return { label: 'QC Posted',   cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (row?.hasQc)       return { label: 'QC Pending',  cssClass: 'bg-info-subtle text-info-emphasis border border-info',         icon: 'bi-clipboard-check' };
    if (row?.isSentToQC)  return { label: 'Sent to QC',  cssClass: 'bg-info-subtle text-info-emphasis border border-info',         icon: 'bi-clipboard-data' };
    return { label: '—', cssClass: 'bg-light text-muted border', icon: '' };
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  STEPPER state — same shape as the rest of the chain.
  //  REVERSED sits at index 4; when active it lights up red and the
  //  POSTED_TO_WIP dot greys out.
  // ──────────────────────────────────────────────────────────────────
  stepperState(stageIdx: number): { done: boolean; current: boolean; cancelled: boolean } {
    const idx = this.workflowStageIndex;
    const isReversed = idx === 4;
    if (stageIdx === 4)
      return { done: false, current: isReversed, cancelled: true };
    if (isReversed)
      return { done: stageIdx <= 1, current: false, cancelled: false };
    return { done: stageIdx < idx, current: stageIdx === idx, cancelled: false };
  }
  stageLabel(stage: string): string {
    const m: { [k: string]: string } = {
      Draft: 'Draft',
      PostedToWIP: 'Posted to WIP',
      SentToQC: 'Sent to QC',
      Completed: 'Completed',
      Reversed: 'Reversed'
    };
    return m[stage] || stage;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG LOADER — best-effort, empty on error.
  // ──────────────────────────────────────────────────────────────────
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.dpSvc.getAuditLog(id).subscribe({
      next: (res: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(res?.data) ? res.data : [];
      },
      error: () => {
        this.auditLogLoading = false;
        this.auditLog = [];
      }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')                                 return 'bg-primary';
    if (a === 'EDITED')                                  return 'bg-warning text-dark';
    if (a === 'POSTED' || a === 'SENT_TO_QC')            return 'bg-success';
    if (a === 'REVERSED' || a === 'DELETED')             return 'bg-danger';
    return 'bg-secondary';
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION
  // ──────────────────────────────────────────────────────────────────
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.items.length > 0
      && this.items.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) {
      this.items.forEach((r: any) => this.selectedIds.delete(r.id));
    } else {
      this.items.forEach((r: any) => this.selectedIds.add(r.id));
    }
  }
  clearSelection(): void { this.selectedIds.clear(); }

  private afterBulk(res: any, verb: string): void {
    const d = res?.data ?? {};
    const ok = Number(d.successCount || 0);
    const failed = Number(d.failureCount || 0);
    let msg = `${ok} ${verb}, ${failed} failed.`;
    if (failed > 0 && Array.isArray(d.failures)) {
      msg += '\n\nFailures:\n' + d.failures
        .slice(0, 10)
        .map((f: any) => `  • #${f.id} ${f.code}: ${f.reason}`)
        .join('\n');
      if (d.failures.length > 10) msg += `\n  …and ${d.failures.length - 10} more.`;
    }
    alert(msg);
    this.clearSelection();
    this.loadData();
  }

  bulkPost(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} production row(s) to WIP? Posted rows cannot be edited.`)) return;
    this.bulkBusy = true;
    this.dpSvc.bulkPost(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'posted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk post failed.'); }
    });
  }
  bulkSendToQc(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Send ${ids.length} posted row(s) to QC? A QC batch will be created per work order item.`)) return;
    this.bulkBusy = true;
    this.dpSvc.bulkSendToQc(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'sent to QC'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk send-to-QC failed.'); }
    });
  }
  bulkReverse(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const reason = prompt(`Reverse ${ids.length} posted row(s)? Enter a short reason (optional):`, '');
    if (reason === null) return;   // user hit Cancel
    this.bulkBusy = true;
    this.dpSvc.bulkReverse(ids, reason || '').subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'reversed'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk reverse failed.'); }
    });
  }
  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} draft row(s)? Posted/sent-to-QC rows are skipped.`)) return;
    this.bulkBusy = true;
    this.dpSvc.bulkDelete(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  KPI deep-link filters — clicking a chip applies the
  //  matching filter and reloads.
  // ──────────────────────────────────────────────────────────────────
  filterByKpi(kind: 'sentToQc' | 'posted' | 'draft' | 'reversed'): void {
    if (kind === 'sentToQc')  { this.filterSentToQc = 'YES'; this.filterPosted = ''; }
    if (kind === 'posted')    { this.filterPosted = 'POSTED';   this.filterSentToQc = ''; }
    if (kind === 'draft')     { this.filterPosted = 'DRAFT';    this.filterSentToQc = ''; }
    if (kind === 'reversed')  { this.filterPosted = 'REVERSED'; this.filterSentToQc = ''; }
    this.loadData();
  }

  // Phase Q.1 - profile drawer
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) {
      alert('Production id missing on this row.');
      return;
    }
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.dpSvc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          alert(res?.message || 'Failed to load profile.');
          this.showProfile = false;
          return;
        }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('DailyProduction profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
  }

  canSelectWorkOrderItem(item: WorkOrderItemDropdownVm): boolean {
    if (!item) return false;

    const isCompleted = Boolean(item.isCompleted ?? false);
    const remainingQty = Number(item.remainingQty ?? 0);

    return !isCompleted && remainingQty > 0;
  }

  getSelectableWorkOrderItems(): WorkOrderItemDropdownVm[] {
    const selectedWorkOrderItemId = Number(this.formModel.workOrderItemId ?? 0);

    if (this.modalMode === 'edit' && selectedWorkOrderItemId > 0) {
      return (this.workOrderItems || []).filter(x =>
        this.canSelectWorkOrderItem(x) || Number(x.id) === selectedWorkOrderItemId
      );
    }

    return (this.workOrderItems || []).filter(x => this.canSelectWorkOrderItem(x));
  }

  onWorkOrderItemChange(): void {
    const selectedId = this.formModel.workOrderItemId != null
      ? Number(this.formModel.workOrderItemId)
      : null;

    const selected = (this.workOrderItems || []).find(x => Number(x.id) === Number(selectedId)) || null;
    this.selectedWorkOrderItem = selected;

    if (!selected) return;

    if (!this.formModel.machineId && selected.machineId) {
      this.formModel.machineId = Number(selected.machineId);
    }

    if (!this.formModel.actualQty || Number(this.formModel.actualQty) <= 0) {
      const remaining = Number(selected.remainingQty ?? 0);
      this.formModel.actualQty = remaining > 0 ? remaining : null;
    }

    this.recalcStitches();
  }

  /** Auto-compute Stitches = stitchesPerPiece × actualQty. Called on
   *  WO Item change and on Actual Qty change. User can still override
   *  the value manually; the next change to either input will recompute. */
  recalcStitches(): void {
    const sel = this.selectedWorkOrderItem;
    if (!sel) return;

    const perPiece = Number(sel.stitchesPerPiece ?? 0);
    const qty = Number(this.formModel.actualQty ?? 0);

    if (perPiece > 0 && qty > 0) {
      this.formModel.stitches = perPiece * qty;
    }
  }

  onActualQtyChange(): void {
    this.recalcStitches();
  }

  getStitchesPerPiece(): number {
    return Number(this.selectedWorkOrderItem?.stitchesPerPiece ?? 0);
  }

  save(): void {
    const payload = this.normalizeForm();

    if (!payload.workOrderItemId) return alert('Work Order Item is required.');
    if (!payload.machineId) return alert('Machine is required.');
    if (!payload.operatorId) return alert('Operator is required.');
    if (!payload.actualQty || payload.actualQty <= 0) return alert('Actual quantity must be greater than zero.');
    if ((payload.rejectedQty ?? 0) < 0) return alert('Rejected quantity cannot be negative.');
    if ((payload.rejectedQty ?? 0) > payload.actualQty) return alert('Rejected quantity cannot exceed actual quantity.');

    // Compose date + time inputs into ISO start/end. Either both supplied or both empty.
    const hasAnyTime = !!(this.uiProductionDate || this.uiStartTime || this.uiEndTime);
    if (hasAnyTime) {
      if (!this.uiProductionDate) return alert('Production Date is required.');
      if (!this.uiStartTime) return alert('Start Time is required.');
      if (!this.uiEndTime) return alert('End Time is required.');
      if (this.getWorkingHours() <= 0) return alert('End Time must be after Start Time.');

      payload.startTime = this.composeStartIso();
      payload.endTime = this.composeEndIso();
    } else {
      payload.startTime = null;
      payload.endTime = null;
    }

    const selected = (this.workOrderItems || []).find(x =>
      Number(x.id) === Number(payload.workOrderItemId)
    );

    if (!selected) return alert('Invalid Work Order Item.');

    const accepted = Number(payload.actualQty || 0) - Number(payload.rejectedQty || 0);

    if (accepted <= 0) return alert('Accepted quantity must be greater than zero.');

    if (this.modalMode === 'create' && accepted > Number(selected.remainingQty || 0)) {
      return alert(`Accepted quantity exceeds remaining quantity (${selected.remainingQty}).`);
    }

    if (this.modalMode === 'edit' && (!this.selectedId || this.selectedId <= 0)) {
      return alert('Selected production record is missing.');
    }

    const request$ = this.modalMode === 'create'
      ? this.dpSvc.create(payload)
      : this.dpSvc.update(this.selectedId!, payload);

    request$.subscribe({
      next: res => {
        if (res.success) {
          this.closeModal();
          this.loadDropdowns();
          this.loadData();
        } else {
          alert(res.message || 'Operation failed.');
        }
      },
      error: () => alert('Operation failed.')
    });
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.selectedId = null;
    this.formModel = this.createEmptyForm();
    this.selectedWorkOrderItem = null;
    this.entryMode = 'production';
    this.selectedReworkTaskId = null;

    this.uiProductionDate = this.toLocalDateString(new Date());
    this.uiStartTime = '';
    this.uiEndTime = '';

    // Refresh rework tasks each time the form opens — operators expect
    // the list to be current with QC's latest postings.
    this.dpSvc.getReworkTasks().subscribe({
      next: res => this.reworkTasks = res || [],
      error: () => this.reworkTasks = []
    });

    this.prefillCurrentOperator();
    this.openModal(this.dpModal);
  }

  private buildFallbackWorkOrderItem(detail: DailyProductionDetailViewModel): WorkOrderItemDropdownVm {
    return {
      id: Number(detail.workOrderItemId ?? 0),
      name: detail.workOrderLabel ?? '',
      workOrderLabel: detail.workOrderLabel ?? '',
      productName: detail.productName ?? '',
      designName: detail.designName ?? '',
      componentName: '',
      plannedQty: Number(detail.plannedQty ?? detail.actualQty ?? 0),
      completedQty: Number(detail.acceptedQty ?? 0),
      remainingQty: 0,
      isCompleted: true,
      machineId: detail.machineId != null ? Number(detail.machineId) : null,
      stitchesPerPiece: 0
    };
  }

  openEdit(row: DailyProductionRowViewModel): void {
    this.dpSvc.getDetail(row.id).subscribe({
      next: detail => {
        this.modalMode = 'edit';
        this.selectedId = Number(detail.id ?? 0) || null;

        const selectedWorkOrderItemId = Number(detail.workOrderItemId ?? 0) || null;

        this.formModel = {
          workOrderItemId: selectedWorkOrderItemId,
          machineId: Number(detail.machineId ?? 0) || null,
          operatorId: Number(detail.operatorId ?? 0) || null,
          shift: detail.shift ?? null,
          startTime: detail.startTime ?? null,
          endTime: detail.endTime ?? null,
          stitches: detail.stitches != null ? Number(detail.stitches) : null,
          actualQty: detail.actualQty != null ? Number(detail.actualQty) : null,
          rejectedQty: detail.rejectedQty != null ? Number(detail.rejectedQty) : 0,
          remarks: detail.remarks ?? ''
        };

        // Split persisted Start/End datetime back into the UI's date + time parts.
        // The production date is anchored to StartTime; end time is just the H:MM —
        // night-shift crossing is recomputed from the time values.
        const startDate = detail.startTime ? new Date(detail.startTime) : null;
        const endDate = detail.endTime ? new Date(detail.endTime) : null;

        if (startDate && !isNaN(startDate.getTime())) {
          this.uiProductionDate = this.toLocalDateString(startDate);
          this.uiStartTime = `${String(startDate.getHours()).padStart(2, '0')}:${String(startDate.getMinutes()).padStart(2, '0')}`;
        } else {
          this.uiProductionDate = this.toLocalDateString(new Date());
          this.uiStartTime = '';
        }

        if (endDate && !isNaN(endDate.getTime())) {
          this.uiEndTime = `${String(endDate.getHours()).padStart(2, '0')}:${String(endDate.getMinutes()).padStart(2, '0')}`;
        } else {
          this.uiEndTime = '';
        }

        const exists = (this.workOrderItems || []).some(
          x => Number(x.id) === Number(selectedWorkOrderItemId)
        );

        if (!exists && selectedWorkOrderItemId) {
          this.workOrderItems = [
            this.buildFallbackWorkOrderItem(detail),
            ...(this.workOrderItems || [])
          ];
        }

        this.onWorkOrderItemChange();
        this.openModal(this.dpModal);
      },
      error: () => alert('Failed to load edit data.')
    });
  }

  openDetail(row: DailyProductionRowViewModel): void {
    this.detailModel = null;

    this.dpSvc.getDetail(row.id).subscribe({
      next: res => {
        this.detailModel = res;
        // Sprint-2: snapshot stepper state from the detail payload and
        // kick off a lazy fetch of the audit timeline so the drawer
        // renders the workflow + history side-by-side.
        const stages = (res as any)?.workflowStages;
        const idx    = (res as any)?.workflowStageIndex;
        if (Array.isArray(stages) && stages.length) this.workflowStages = stages;
        if (typeof idx === 'number') this.workflowStageIndex = idx;
        this.loadAuditLog(row.id);
        this.openModal(this.dpDetailModal);
      },
      error: () => alert('Failed to load detail.')
    });
  }

  confirmDelete(row: DailyProductionRowViewModel): void {
    if (!row.canDelete) return alert('This record cannot be deleted.');

    this.deleteModel = row;
    this.openModal(this.dpDeleteModal);
  }

  deleteConfirmed(): void {
    if (!this.deleteModel) return;

    this.dpSvc.delete(this.deleteModel.id).subscribe({
      next: res => {
        if (res.success) {
          this.closeModal();
          this.loadDropdowns();
          this.loadData();
        } else {
          alert(res.message || 'Delete failed.');
        }
      },
      error: () => alert('Delete failed.')
    });
  }

  canPostToWip(row: DailyProductionRowViewModel): boolean {
    return !!row && row.canPost === true;
  }

  canSendToQc(row: DailyProductionRowViewModel): boolean {
    return !!row &&
      row.isPosted === true &&
      row.isReversed !== true &&
      row.isSentToQC !== true &&
      row.hasQc !== true &&
      row.qcCompleted !== true &&
      Number(row.acceptedQty ?? 0) > 0 &&
      !this.isReworkRow(row);
  }

  /** Rework entries don't create new QC inspections — they re-open the
   *  original QC via the parent's status flip in PostToWip. */
  isReworkRow(row: DailyProductionRowViewModel): boolean {
    const status = String(row?.status ?? '').toLowerCase();
    return status === 'rework' || status === 'reworkposted';
  }

  postToWip(row: DailyProductionRowViewModel): void {
    if (!this.canPostToWip(row)) return;
    if (!confirm('Post production to WIP?')) return;

    this.dpSvc.postToWip(row.id).subscribe({
      next: res => {
        if (res.success) {
          this.loadDropdowns();
          this.loadData();
        } else {
          alert(res.message || 'Post to WIP failed.');
        }
      },
      error: () => alert('Post to WIP failed.')
    });
  }

  sendToQc(row: DailyProductionRowViewModel): void {
    if (!this.canSendToQc(row)) return;
    if (!confirm('Send this production to QC?')) return;

    this.dpSvc.sendToQc(row.id).subscribe({
      next: res => {
        if (res.success) {
          this.loadDropdowns();
          this.loadData();
        } else {
          alert(res.message || 'Send to QC failed.');
        }
      },
      error: () => alert('Send to QC failed.')
    });
  }

  openReverse(row: DailyProductionRowViewModel): void {
    if (!row.canReverse) return alert('This production record cannot be reversed.');

    this.reverseModel = row;
    this.reverseReason = '';
    this.openModal(this.dpReverseModal);
  }

  reverseConfirmed(): void {
    if (!this.reverseModel) return;

    const reason = this.reverseReason.trim();
    if (!reason) return alert('Reverse reason is required.');

    this.dpSvc.reverse(this.reverseModel.id, reason).subscribe({
      next: res => {
        if (res.success) {
          this.closeModal();
          this.loadDropdowns();
          this.loadData();
        } else {
          alert(res.message || 'Reverse failed.');
        }
      },
      error: () => alert('Reverse failed.')
    });
  }

  openModal(ref: ElementRef): void {
    if (!ref?.nativeElement) return;

    if (this.currentModal) {
      this.currentModal.hide();
      this.currentModal = null;
    }

    this.currentModal = new bootstrap.Modal(ref.nativeElement);
    this.currentModal.show();
  }

  closeModal(): void {
    if (this.currentModal) {
      this.currentModal.hide();
      this.currentModal = null;
    }

    this.selectedId = null;
    this.formModel = this.createEmptyForm();
    this.selectedWorkOrderItem = null;
    this.detailModel = null;
    this.deleteModel = null;
    this.reverseModel = null;
    this.reverseReason = '';
    this.entryMode = 'production';
    this.selectedReworkTaskId = null;

    this.uiProductionDate = '';
    this.uiStartTime = '';
    this.uiEndTime = '';
  }

  prefillCurrentOperator(): void {
    this.dpSvc.getCurrentOperator().subscribe({
      next: res => {
        if (res?.success && res.data?.id) {
          this.formModel.operatorId = Number(res.data.id);
        }
      }
    });
  }

  get acceptedQty(): number {
    const actual = Number(this.formModel.actualQty ?? 0);
    const rejected = Number(this.formModel.rejectedQty ?? 0);
    return Math.max(actual - rejected, 0);
  }

  getProgressPercent(): number {
    if (!this.selectedWorkOrderItem?.plannedQty) return 0;

    const planned = Number(this.selectedWorkOrderItem.plannedQty ?? 0);
    const completed = Number(this.selectedWorkOrderItem.completedQty ?? 0);

    if (planned <= 0) return 0;

    const pct = (completed / planned) * 100;
    return Math.max(0, Math.min(100, pct));
  }

  private parseTimeToMinutes(t: string | null | undefined): number | null {
    if (!t) return null;

    const parts = t.split(':');
    if (parts.length < 2) return null;

    const h = parseInt(parts[0], 10);
    const m = parseInt(parts[1], 10);

    if (isNaN(h) || isNaN(m)) return null;
    return h * 60 + m;
  }

  private toLocalDateString(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }

  /** True when End Time is earlier in the day than Start Time (night shift). */
  isNightShiftCross(): boolean {
    const s = this.parseTimeToMinutes(this.uiStartTime);
    const e = this.parseTimeToMinutes(this.uiEndTime);
    if (s == null || e == null) return false;
    return e <= s;
  }

  /** Returns YYYY-MM-DD for End Time — same as production date, or +1 day for night shift. */
  private resolveEndDate(): string {
    if (!this.uiProductionDate) return '';
    if (!this.isNightShiftCross()) return this.uiProductionDate;

    const d = new Date(this.uiProductionDate + 'T00:00:00');
    if (isNaN(d.getTime())) return this.uiProductionDate;

    d.setDate(d.getDate() + 1);
    return this.toLocalDateString(d);
  }

  private composeStartIso(): string | null {
    if (!this.uiProductionDate || !this.uiStartTime) return null;
    return `${this.uiProductionDate}T${this.uiStartTime}:00`;
  }

  private composeEndIso(): string | null {
    if (!this.uiProductionDate || !this.uiEndTime) return null;
    const endDate = this.resolveEndDate();
    if (!endDate) return null;
    return `${endDate}T${this.uiEndTime}:00`;
  }

  getWorkingHours(): number {
    const start = this.parseTimeToMinutes(this.uiStartTime);
    const end = this.parseTimeToMinutes(this.uiEndTime);

    if (start == null || end == null) return 0;

    let diffMin = end - start;
    if (diffMin <= 0) diffMin += 24 * 60;   // night shift crossing midnight

    return +(diffMin / 60).toFixed(2);
  }

  detectShift(): string {
    const m = this.parseTimeToMinutes(this.uiStartTime);
    if (m == null) return '';

    const hour = Math.floor(m / 60);

    if (hour >= 6 && hour < 14) return 'A';
    if (hour >= 14 && hour < 22) return 'B';
    return 'C';
  }

  onTimeChange(): void {
    this.formModel.shift = this.detectShift();
  }

  getProductionPerHour(): number {
    const hours = this.getWorkingHours();
    if (hours <= 0) return 0;

    const actualQty = Number(this.formModel.actualQty ?? 0);
    return +(actualQty / hours).toFixed(2);
  }

  getStatusClass(status: string | null, isReversed?: boolean | null): string {
    if (isReversed === true) return 'state-reversed';

    switch (status) {
      case 'PostedToWIP':
      case 'WIP':
        return 'state-wip';

      case 'SentToQC':
      case 'InProgress':
        return 'state-qc-pending';

      case 'Reversed':
        return 'state-reversed';

      case 'Production Complete':
      case 'Completed':
        return 'state-done';

      case 'Draft':
        return 'state-muted';

      default:
        return 'state-muted';
    }
  }

  exportExcel(): void {
    this.dpSvc.exportExcel().subscribe(blob => this.download(blob, 'DailyProduction.xlsx'));
  }

  exportPdf(): void {
    this.dpSvc.exportPdf().subscribe(blob => this.download(blob, 'DailyProduction.pdf'));
  }

  openPrint(): void {
    window.open(this.dpSvc.printView(), '_blank');
  }

  private download(blob: Blob, name: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');

    a.href = url;
    a.download = name;
    a.click();

    URL.revokeObjectURL(url);
  }
}
