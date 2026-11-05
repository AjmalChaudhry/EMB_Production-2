import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import * as bootstrap from 'bootstrap';

import { PurchaseRequisitionService, PrAuditEntry } from '../services/purchase-requisition.service';
import {
  PrRow, PrItemRow, PrSavePayload, PrSummary,
  SupplierDropdown, RawItemDropdown, PR_SOURCE_TYPES
} from '../shared/models/purchase-requisition.dto';

interface EditableItem {
  id: number;
  rawItemId: number | null;
  rawItemName: string;
  unitId: number | null;
  unitCode: string;
  qty: number;
  estimatedRate: number | null;
  remarks: string;
}

@Component({
  selector: 'app-purchase-requisition',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './purchase-requisition.component.html',
  styleUrls: ['./purchase-requisition.component.css']
})
export class PurchaseRequisitionComponent implements OnInit, AfterViewInit {
  rows: PrRow[] = [];
  summary: PrSummary = { count: 0, draftCount: 0, submittedCount: 0, approvedCount: 0, rejectedCount: 0 };
  loading = false; saving = false;

  // Filters
  statusFilter = '';
  supplierFilter: number | null = null;
  searchText = '';
  // Sprint-3 filters — source type + request date range.
  sourceTypeFilter = '';
  fromDateFilter = '';
  toDateFilter = '';

  // Dropdowns
  suppliers: SupplierDropdown[] = [];
  rawItems: RawItemDropdown[] = [];
  readonly sourceTypes = PR_SOURCE_TYPES;

  // Editor
  modalMode: 'create' | 'edit' = 'create';
  form: {
    id: number; prCode: string; supplierId: number | null;
    sourceType: string; remarks: string; status: string;
    items: EditableItem[];
  } = this.emptyForm();

  // Reject modal
  rejectId: number | null = null;
  rejectReason = '';

  // Auto-fill banner state. Set when /shortfall-suggestions returns
  // rows and we pre-populated the items grid. Banner is dismissed on
  // close of the modal or on switching to edit mode.
  shortfallBannerCount = 0;
  loadingSuggestions = false;

  @ViewChild('formModal')   formModal?: ElementRef;
  @ViewChild('rejectModal') rejectModal?: ElementRef;
  @ViewChild('supplierModal') supplierModal?: ElementRef;
  private formModalInstance?: bootstrap.Modal;
  private rejectModalInstance?: bootstrap.Modal;
  private supplierModalInstance?: bootstrap.Modal;

  // Change-supplier modal state
  supplierChangeId: number | null = null;
  supplierChangePrCode = '';
  supplierChangeNewId: number | null = null;
  supplierChangeSaving = false;

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-2  WORKFLOW STAGE + AUDIT TIMELINE STATE
  //  Loaded in parallel with the detail so both cards are populated by
  //  the time the user scrolls down inside the form modal.
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['DRAFT', 'SUBMITTED', 'APPROVED', 'CONVERTED_TO_PO', 'REJECTED'];
  workflowStageIndex = 0;
  auditLog: PrAuditEntry[] = [];
  auditLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK SELECTION STATE
  //  Multi-select using a Set<number> of selected PR ids. Selection
  //  persists within the page but resets on any filter / page change
  //  via the loadList() reset.
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  constructor(
    private svc: PurchaseRequisitionService,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadList();
  }

  ngAfterViewInit(): void {
    if (this.formModal)     this.formModalInstance     = new bootstrap.Modal(this.formModal.nativeElement);
    if (this.rejectModal)   this.rejectModalInstance   = new bootstrap.Modal(this.rejectModal.nativeElement);
    if (this.supplierModal) this.supplierModalInstance = new bootstrap.Modal(this.supplierModal.nativeElement);

    // Handle ?openId=N — triggered after WO Post and Plan Approve so the
    // auto-generated DRAFT PR opens directly in the Edit modal. The buyer
    // reviews supplier/items, then drives Submit + Approve.
    // Done in ngAfterViewInit because openEdit() depends on the modal
    // instance being constructed first.
    this.route.queryParams.subscribe(qp => {
      const id = Number(qp['openId']);
      if (id > 0) {
        // Small delay so the loadDropdowns() observables (suppliers,
        // raw-items) have time to land — openEdit needs them to render
        // the items grid with names + units.
        setTimeout(() => this.openEdit(id), 400);
      }
    });
  }

  private loadDropdowns(): void {
    this.svc.suppliers().subscribe(r => { if (r?.success) this.suppliers = r.data || []; });
    this.svc.rawItems().subscribe(r => { if (r?.success) this.rawItems = r.data || []; });
  }

  loadList(): void {
    this.loading = true;
    // Sprint-3: paging out the visible set resets the selection so a
    // bulk action never silently operates on rows the user can't see.
    this.selectedIds.clear();
    this.svc.list({
      status: this.statusFilter,
      supplierId: this.supplierFilter || undefined,
      search: this.searchText,
      sourceType: this.sourceTypeFilter || undefined,
      fromDate: this.fromDateFilter || undefined,
      toDate: this.toDateFilter || undefined
    }).subscribe({
      next: r => {
        this.loading = false;
        if (r?.success) {
          this.rows = r.data?.items || [];
          this.summary = r.data?.summary || this.summary;
        }
      },
      error: () => { this.loading = false; }
    });
  }

  applyFilters(): void { this.loadList(); }
  clearFilters(): void {
    this.statusFilter = ''; this.supplierFilter = null; this.searchText = '';
    this.sourceTypeFilter = ''; this.fromDateFilter = ''; this.toDateFilter = '';
    this.loadList();
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.form = this.emptyForm();
    this.shortfallBannerCount = 0;
    this.svc.nextCode().subscribe(r => {
      if (r?.success && r.data?.code) this.form.prCode = r.data.code;
    });

    // Auto-fill items grid with current store shortages. Each row
    // remains fully editable — the buyer can change qty, drop rows,
    // or add more via "Add Line". Source is tagged PLAN_SHORTFALL by
    // default to record that the PR originated from a system signal.
    this.loadingSuggestions = true;
    this.svc.shortfallSuggestions().subscribe({
      next: r => {
        this.loadingSuggestions = false;
        if (!r?.success || !r.data) return;
        const items = r.data.items || [];
        this.shortfallBannerCount = items.length;
        if (items.length === 0) return;
        this.form.sourceType = 'PLAN_SHORTFALL';
        this.form.items = items.map(s => ({
          id: 0,
          rawItemId: s.rawItemId,
          rawItemName: s.rawItemName,
          unitId: s.unitId,
          unitCode: s.unitCode || '',
          qty: Number(s.suggestedQty || 0),
          estimatedRate: s.estimatedRate,
          remarks: `Auto: current ${s.currentBalance} < reorder ${s.reorderLevel}`
        }));
      },
      error: () => { this.loadingSuggestions = false; }
    });

    this.formModalInstance?.show();
  }

  openEdit(id: number): void {
    this.shortfallBannerCount = 0;
    this.svc.detail(id).subscribe(r => {
      if (!r?.success || !r.data) return;
      const h = r.data.header;
      this.modalMode = 'edit';
      this.form = {
        id: h.id, prCode: h.prCode,
        supplierId: h.supplierId, sourceType: h.sourceType,
        remarks: h.remarks || '', status: h.status,
        items: (r.data.items || []).map(i => ({
          id: i.id,
          rawItemId: i.rawItemId,
          rawItemName: i.rawItemName,
          unitId: i.unitId,
          unitCode: i.unitCode || '',
          qty: Number(i.qty || 0),
          estimatedRate: i.estimatedRate,
          remarks: i.remarks || ''
        }))
      };

      // Sprint-2: capture workflow stage from the detail response.
      const anyData: any = r.data as any;
      this.workflowStages = Array.isArray(anyData.workflowStages) && anyData.workflowStages.length
        ? anyData.workflowStages
        : ['DRAFT', 'SUBMITTED', 'APPROVED', 'CONVERTED_TO_PO', 'REJECTED'];
      this.workflowStageIndex = Number(anyData.workflowStageIndex ?? 0);

      this.formModalInstance?.show();
    });
    // Sprint-2: audit timeline loads in parallel so the timeline card is
    // populated by the time the user scrolls.
    this.loadAuditLog(id);
  }

  private loadAuditLog(id: number): void {
    this.auditLoading = true;
    this.auditLog = [];
    this.svc.getAuditLog(id).subscribe({
      next: r => {
        this.auditLoading = false;
        if (r?.success) this.auditLog = r.data || [];
      },
      error: () => { this.auditLoading = false; }
    });
  }

  /** Sprint-2: action → Bootstrap chip class + icon + label. Single
   *  switch keeps the look consistent and new action types can be added
   *  in one place. */
  auditPill(action: string): { cls: string; icon: string; label: string } {
    switch ((action || '').trim()) {
      case 'Created':         return { cls: 'bg-secondary',           icon: 'bi-plus-circle',      label: 'Created'         };
      case 'Updated':         return { cls: 'bg-info',                icon: 'bi-pencil-square',    label: 'Updated'         };
      case 'Submitted':       return { cls: 'bg-warning text-dark',   icon: 'bi-send',             label: 'Submitted'       };
      case 'Approved':        return { cls: 'bg-primary',             icon: 'bi-check2-circle',    label: 'Approved + PO'   };
      case 'Rejected':        return { cls: 'bg-danger',              icon: 'bi-x-circle',         label: 'Rejected'        };
      case 'Deleted':         return { cls: 'bg-danger',              icon: 'bi-trash',            label: 'Deleted'         };
      case 'SupplierChanged': return { cls: 'bg-light text-dark border', icon: 'bi-truck',         label: 'Supplier changed'};
      default:                return { cls: 'bg-secondary',           icon: 'bi-clock-history',    label: action || '—'     };
    }
  }

  /** Stepper helper — colour each marker by position relative to the
   *  current stage. Past = filled green, current = filled primary,
   *  future = grey. Rejected is shown as a separate red chip — its
   *  index is 4 in the canonical list (terminal sibling of Converted). */
  stepperState(stageIndex: number): { dotCls: string; lineCls: string; textCls: string } {
    // When a PR is rejected (index 4), all forward steps after Submitted
    // (>= 2) are greyed out — they never happened.
    if (this.workflowStageIndex === 4) {
      if (stageIndex < 2)              return { dotCls: 'bg-success border-success text-white', lineCls: 'bg-success', textCls: 'text-success' };
      if (stageIndex === 4)            return { dotCls: 'bg-danger border-danger text-white',   lineCls: 'bg-danger',  textCls: 'text-danger fw-bold' };
      return { dotCls: 'bg-light border text-muted', lineCls: 'bg-light', textCls: 'text-muted' };
    }
    if (stageIndex < this.workflowStageIndex)
      return { dotCls: 'bg-success border-success text-white', lineCls: 'bg-success', textCls: 'text-success' };
    if (stageIndex === this.workflowStageIndex)
      return { dotCls: 'bg-primary border-primary text-white', lineCls: 'bg-light',   textCls: 'text-primary fw-bold' };
    return { dotCls: 'bg-light border text-muted', lineCls: 'bg-light', textCls: 'text-muted' };
  }

  /** Human-friendly stage label (CONVERTED_TO_PO → "Converted to PO", etc.). */
  stageLabel(stage: string): string {
    if (!stage) return '';
    if (stage === 'CONVERTED_TO_PO') return 'Converted';
    if (stage === 'SUBMITTED')       return 'Pending';
    return stage.split('_')
      .map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase())
      .join(' ');
  }

  closeForm(): void {
    this.shortfallBannerCount = 0;
    this.formModalInstance?.hide();
    // Sprint-2: drop audit state so reopening a different PR doesn't
    // flash stale entries.
    this.auditLog = [];
    this.workflowStageIndex = 0;
  }

  addItem(): void {
    this.form.items.push({
      id: 0, rawItemId: null, rawItemName: '',
      unitId: null, unitCode: '', qty: 0,
      estimatedRate: null, remarks: ''
    });
  }
  removeItem(idx: number): void { this.form.items.splice(idx, 1); }

  onRawPick(row: EditableItem): void {
    const r = this.rawItems.find(x => x.id === Number(row.rawItemId));
    if (r) { row.rawItemName = r.name; row.unitId = r.unitId; }
  }

  save(): void {
    if (!this.form.items.length) return alert('Add at least one item.');

    const payload: PrSavePayload = {
      id: this.form.id || undefined,
      prCode: this.form.prCode,
      supplierId: this.form.supplierId || null,
      sourceType: this.form.sourceType || 'AD_HOC',
      remarks: this.form.remarks,
      items: this.form.items
        .filter(i => i.rawItemId && i.qty > 0)
        .map(i => ({
          id: i.id || undefined,
          rawItemId: i.rawItemId as number,
          unitId: i.unitId,
          qty: Number(i.qty),
          estimatedRate: i.estimatedRate != null ? Number(i.estimatedRate) : null,
          remarks: i.remarks
        }))
    };

    if (!payload.items.length) return alert('Each item needs a Raw Item and a positive Qty.');

    this.saving = true;
    const obs = this.modalMode === 'create' ? this.svc.create(payload) : this.svc.update(payload);
    obs.subscribe({
      next: r => {
        this.saving = false;
        if (!r?.success) return alert(r?.message || 'Save failed.');
        this.formModalInstance?.hide();
        this.loadList();
      },
      error: e => {
        this.saving = false;
        alert(e?.error?.message || e?.message || 'Save failed.');
      }
    });
  }

  submitFor(id: number): void {
    if (!confirm('Submit this PR for approval?')) return;
    this.svc.submit(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Submit failed.');
      this.loadList();
    });
  }

  approve(id: number): void {
    if (!confirm('Approve PR and auto-generate a Purchase Order? Supplier must be set and every item must have an Estimated Rate.')) return;
    this.svc.approve(id).subscribe({
      next: r => {
        if (!r?.success) return alert(r?.message || 'Approve failed.');
        const d = r.data;
        if (confirm(`Approved. ${d?.poCode || 'PO'} created (value ${d?.totalAmount}). Open it now?`)) {
          this.router.navigate(['/purchase-order'], { queryParams: { id: d?.poId } });
        }
        this.loadList();
      },
      error: e => alert(e?.error?.message || e?.message || 'Approve failed.')
    });
  }

  openReject(id: number): void {
    this.rejectId = id; this.rejectReason = '';
    this.rejectModalInstance?.show();
  }
  confirmReject(): void {
    if (!this.rejectId) return;
    if (!this.rejectReason.trim()) return alert('Rejection reason is required.');
    this.svc.reject(this.rejectId, this.rejectReason.trim()).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Reject failed.');
      this.rejectModalInstance?.hide();
      this.loadList();
    });
  }

  /** Open the change-supplier modal. Pre-selects the current supplier. */
  openChangeSupplier(row: PrRow): void {
    this.supplierChangeId      = row.id;
    this.supplierChangePrCode  = row.prCode;
    this.supplierChangeNewId   = row.supplierId ?? null;
    this.supplierChangeSaving  = false;
    this.supplierModalInstance?.show();
  }
  confirmChangeSupplier(): void {
    if (!this.supplierChangeId) return;
    if (!this.supplierChangeNewId) return alert('Pick a supplier.');
    this.supplierChangeSaving = true;
    this.svc.changeSupplier(this.supplierChangeId, this.supplierChangeNewId).subscribe({
      next: r => {
        this.supplierChangeSaving = false;
        if (!r?.success) return alert(r?.message || 'Failed to change supplier.');
        this.supplierModalInstance?.hide();
        this.loadList();
      },
      error: e => {
        this.supplierChangeSaving = false;
        alert(e?.error?.message || e?.message || 'Failed to change supplier.');
      }
    });
  }

  remove(id: number): void {
    if (!confirm('Soft-delete this PR?')) return;
    this.svc.remove(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Delete failed.');
      this.loadList();
    });
  }

  badgeClass(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'DRAFT':            return 'bg-secondary';
      case 'SUBMITTED':        return 'bg-warning text-dark';
      case 'APPROVED':         return 'bg-primary';
      case 'CONVERTED_TO_PO':  return 'bg-success';
      case 'REJECTED':         return 'bg-danger';
      default: return 'bg-light text-dark';
    }
  }

  // ────────────────────────────────────────────────────────────────────
  //  SPRINT-1  ROW HELPERS — short status label, source pill with icon,
  //  deep-link navigation to the source WO / Plan and the generated PO.
  // ────────────────────────────────────────────────────────────────────

  /** Shorter, friendlier label for the Status badge — collapses the verbose
   *  CONVERTED_TO_PO into "Converted" so the chip stays narrow. The PO code
   *  rides in its own chip alongside (see template). */
  statusLabel(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'CONVERTED_TO_PO': return 'Converted';
      case 'SUBMITTED':       return 'Pending';
      default: return (status || '').replace(/_/g, ' ');
    }
  }

  /** Source-type pill: short human label + icon + colour. Same lookup used
   *  by the clickable badge and tooltip. */
  sourcePill(sourceType: string): { label: string; icon: string; cls: string; clickable: boolean } {
    switch ((sourceType || '').toUpperCase()) {
      case 'WO_POST':
        return { label: 'WO Post',        icon: 'bi-list-task',     cls: 'bg-primary-subtle text-primary-emphasis border border-primary', clickable: true };
      case 'PLAN_APPROVE':
        return { label: 'Plan Approve',   icon: 'bi-calendar-week', cls: 'bg-info-subtle text-info-emphasis border border-info',          clickable: true };
      case 'PLAN_SHORTFALL':
        return { label: 'Plan Shortfall', icon: 'bi-exclamation-triangle', cls: 'bg-warning-subtle text-warning-emphasis border border-warning', clickable: true };
      case 'EMR_SHORTFALL':
        return { label: 'EMR',            icon: 'bi-clipboard-plus', cls: 'bg-warning-subtle text-warning-emphasis border border-warning', clickable: false };
      case 'AD_HOC':
      default:
        return { label: 'Manual',         icon: 'bi-pencil',         cls: 'bg-light text-secondary border',                                clickable: false };
    }
  }

  /** Deep-link to the source document — Work Order or Production Plan —
   *  using the same /work-orders?search=… and /production-plan?search=…
   *  pattern the other modules use. AD_HOC / EMR rows have no source link. */
  gotoSource(row: any): void {
    if (!row?.sourceType || !row?.sourceReferenceId) return;
    const t = (row.sourceType as string).toUpperCase();
    if (t === 'WO_POST') {
      this.router.navigate(['/work-orders'], { queryParams: { jobOrderId: row.sourceReferenceId } });
    } else if (t === 'PLAN_APPROVE' || t === 'PLAN_SHORTFALL') {
      // The source reference for PLAN_APPROVE is the Plan id. We can't
      // currently filter the plan list by id, so search by code falls back
      // to opening the list — better than nothing.
      this.router.navigate(['/production-plan']);
    }
  }

  /** Deep-link to the generated PO — opens the Purchase Order list with
   *  the PO code in the search box. */
  gotoPo(row: any): void {
    if (!row?.convertedToPoCode) return;
    this.router.navigate(['/purchase-order'], { queryParams: { search: row.convertedToPoCode } });
  }

  totalValue(items: EditableItem[] | PrItemRow[] | undefined): number {
    if (!items) return 0;
    let sum = 0;
    for (const i of (items as any[])) {
      sum += Number(i.qty || 0) * Number((i.estimatedRate ?? i.EstimatedRate) || 0);
    }
    return sum;
  }

  private emptyForm() {
    return {
      id: 0, prCode: '', supplierId: null as number | null,
      sourceType: 'AD_HOC', remarks: '', status: 'DRAFT',
      items: [] as EditableItem[]
    };
  }

  // ───────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK SELECTION + CLONE HANDLERS
  // ───────────────────────────────────────────────────────────────────

  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  selectedCount(): number { return this.selectedIds.size; }

  allOnPageSelected(): boolean {
    return this.rows.length > 0 && this.rows.every(r => this.selectedIds.has(r.id));
  }
  someOnPageSelected(): boolean {
    return !this.allOnPageSelected() && this.rows.some(r => this.selectedIds.has(r.id));
  }

  toggleRow(id: number, checked: boolean): void {
    if (checked) this.selectedIds.add(id);
    else this.selectedIds.delete(id);
  }
  toggleAllOnPage(checked: boolean): void {
    if (checked) this.rows.forEach(r => this.selectedIds.add(r.id));
    else this.rows.forEach(r => this.selectedIds.delete(r.id));
  }
  clearSelection(): void { this.selectedIds.clear(); }

  bulkApprove(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Approve ${ids.length} PR(s)? Each one will be converted to a Purchase Order. Non-Pending rows will be skipped.`)) return;
    this.bulkBusy = true;
    this.svc.bulkApprove(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'approved'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk approve failed.'); }
    });
  }

  bulkReject(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const reason = prompt(`Reject ${ids.length} PR(s)? Enter a reason (required):`, '');
    if (!reason || !reason.trim()) return;
    this.bulkBusy = true;
    this.svc.bulkReject(ids, reason.trim()).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'rejected'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk reject failed.'); }
    });
  }

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} PR(s)? PRs already converted to a PO will be skipped — cancel the PO instead.`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'deleted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk delete failed.'); }
    });
  }

  /** Shared post-bulk handler. Same pattern as Production Plan / Job
   *  Orders / Work Orders. Surfaces success/failure counts, lists up to
   *  10 failed rows with reasons, reloads the list, clears selection. */
  private afterBulk(res: any, verbLabel: string): void {
    const data = res?.data ?? {};
    const successCount = Number(data.successCount ?? 0);
    const failureCount = Number(data.failureCount ?? 0);
    const failures: Array<{ id: number; code: string; reason: string }> = data.failures ?? [];

    let msg = `${successCount} ${verbLabel}` + (failureCount ? `, ${failureCount} failed.` : '.');
    if (failureCount && failures.length) {
      msg += '\n\nFailed rows:\n' +
        failures.slice(0, 10).map(f => `  • ${f.code || ('#' + f.id)} — ${f.reason}`).join('\n');
      if (failures.length > 10) msg += `\n  …and ${failures.length - 10} more.`;
    }
    alert(msg);
    this.clearSelection();
    this.loadList();
  }

  /** Sprint-3: clone a single PR into a fresh DRAFT. Reloads list and
   *  opens the new draft in the editor. Refuses gracefully on failure. */
  /** Clone preview — open the form modal in create mode pre-filled with
   *  the source PR's supplier + items, but DO NOT persist. The new draft
   *  only appears in the list after the buyer clicks Save (which routes
   *  through the existing /create endpoint via the modalMode === 'create'
   *  branch in save()). Closing without saving abandons the preview. */
  cloneP(row: PrRow): void {
    if (!row?.id) return;
    this.svc.detail(row.id).subscribe(r => {
      if (!r?.success || !r.data) {
        alert('Failed to load source PR for clone preview.');
        return;
      }
      const src = r.data.header;
      const srcItems = r.data.items || [];

      // Stage a fresh form pre-filled with source data. modalMode='create'
      // so save() goes through svc.create() and creates a brand-new row.
      this.modalMode = 'create';
      this.shortfallBannerCount = 0;
      this.form = {
        id: 0,
        prCode: '(new clone of ' + src.prCode + ')',
        supplierId: src.supplierId,
        sourceType: 'AD_HOC',         // a clone is always treated as manual
        remarks: 'Cloned from ' + src.prCode
              + (src.remarks ? ' — ' + src.remarks : ''),
        status: 'DRAFT',
        items: srcItems.map(i => ({
          id: 0,                       // new line — no source-item id
          rawItemId: i.rawItemId,
          rawItemName: i.rawItemName,
          unitId: i.unitId,
          unitCode: i.unitCode || '',
          qty: Number(i.qty || 0),
          estimatedRate: i.estimatedRate,
          remarks: i.remarks || ''
        }))
      };
      // PR list deep-link buttons assume an existing record. In preview mode
      // we're not editing one, so clear any stale workflow / audit state.
      this.workflowStageIndex = 0;
      this.auditLog = [];

      this.formModalInstance?.show();
    });
  }
}
