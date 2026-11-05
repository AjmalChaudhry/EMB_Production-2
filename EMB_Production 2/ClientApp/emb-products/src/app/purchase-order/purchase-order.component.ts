import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import * as bootstrap from 'bootstrap';

import { PurchaseOrderService, PoAuditEntry } from '../services/purchase-order.service';
import {
  PoRow, PoItemRow, GrnSummaryRow, PoSummary, PoLineUpdatePayload
} from '../shared/models/purchase-order.dto';

interface EditableLine {
  id: number;
  rawItemName: string;
  unitCode: string;
  orderedQty: number;
  rate: number;
  receivedQty: number;
  remarks: string;
}

@Component({
  selector: 'app-purchase-order',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './purchase-order.component.html',
  styleUrls: ['./purchase-order.component.css']
})
export class PurchaseOrderComponent implements OnInit, AfterViewInit {
  rows: PoRow[] = [];
  summary: PoSummary = {
    count: 0, draftCount: 0, sentCount: 0, partialCount: 0,
    fullyReceivedCount: 0, closedCount: 0, cancelledCount: 0,
    totalValue: 0, receivedValue: 0
  };
  loading = false; saving = false;

  // Filters
  statusFilter = '';
  searchText = '';
  // Sprint-1: supplier + date range filters.
  supplierFilter: number | null = null;
  fromDateFilter = '';
  toDateFilter = '';

  // Sprint-1: supplier dropdown options (loaded once on init).
  suppliers: { id: number; name: string }[] = [];

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-2  WORKFLOW STAGE + AUDIT TIMELINE STATE
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['DRAFT', 'SENT', 'PARTIALLY_RECEIVED', 'FULLY_RECEIVED', 'CLOSED', 'CANCELLED'];
  workflowStageIndex = 0;
  auditLog: PoAuditEntry[] = [];
  auditLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK SELECTION STATE
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  // ──────────────────────────────────────────────────────────────────
  //  CLONE PREVIEW STATE
  //  When set, the editor modal is showing an unsaved clone preview.
  //  cloneSourceId = the source PO id (passed to /clone on Save).
  //  cloneSourceCode = the source PO code (shown in the header banner).
  //  Save Clone → POST /clone/{cloneSourceId} with overrides; on success
  //  the modal closes and the list reloads with the new row.
  // ──────────────────────────────────────────────────────────────────
  cloneSourceId: number | null = null;
  cloneSourceCode = '';
  cloningSaving = false;

  // Detail
  current: PoRow | null = null;
  items: EditableLine[] = [];
  grns: GrnSummaryRow[] = [];

  // Cancel modal
  cancelReason = '';

  @ViewChild('editorModal') editorModal?: ElementRef;
  @ViewChild('cancelModal') cancelModal?: ElementRef;
  private editorModalInstance?: bootstrap.Modal;
  private cancelModalInstance?: bootstrap.Modal;

  constructor(
    private svc: PurchaseOrderService,
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient
  ) {}

  ngOnInit(): void {
    // Sprint-1: also honour ?search= and ?status= from the URL so deep
    // links from PR module (Generated PO chip) land here pre-filtered.
    const qp0 = this.route.snapshot.queryParamMap;
    const search0 = qp0.get('search'); if (search0) this.searchText = search0.trim();
    const status0 = qp0.get('status'); if (status0) this.statusFilter = status0.trim();

    this.loadSuppliers();
    this.loadList();

    // Auto-open if redirected from PR Approve flow
    this.route.queryParamMap.subscribe(qp => {
      const id = Number(qp.get('id') || 0);
      if (id > 0) setTimeout(() => this.openDetail(id), 300);
    });
  }

  /** Sprint-1: load supplier dropdown once on init. Re-uses the PR
   *  module's endpoint (only active suppliers, name + id). */
  private loadSuppliers(): void {
    this.http.get<any>('/emb-products/purchase-requisition/suppliers-dropdown').subscribe(r => {
      if (r?.success) this.suppliers = r.data || [];
    });
  }

  ngAfterViewInit(): void {
    if (this.editorModal) this.editorModalInstance = new bootstrap.Modal(this.editorModal.nativeElement);
    if (this.cancelModal) this.cancelModalInstance = new bootstrap.Modal(this.cancelModal.nativeElement);
  }

  loadList(): void {
    this.loading = true;
    // Sprint-3: any list reload clears the selection so bulk actions
    // never silently operate on rows the user can't see.
    this.selectedIds.clear();
    this.svc.list({
      status: this.statusFilter,
      supplierId: this.supplierFilter || undefined,
      search: this.searchText,
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
    this.statusFilter = ''; this.searchText = '';
    this.supplierFilter = null;
    this.fromDateFilter = ''; this.toDateFilter = '';
    this.loadList();
  }

  openDetail(id: number): void {
    this.svc.detail(id).subscribe(r => {
      if (!r?.success || !r.data) return;
      this.applyDetail(r.data.header, r.data.items, r.data.grns);

      // Sprint-2: capture workflow stage from the same detail response.
      const anyData: any = r.data as any;
      this.workflowStages = Array.isArray(anyData.workflowStages) && anyData.workflowStages.length
        ? anyData.workflowStages
        : ['DRAFT', 'SENT', 'PARTIALLY_RECEIVED', 'FULLY_RECEIVED', 'CLOSED', 'CANCELLED'];
      this.workflowStageIndex = Number(anyData.workflowStageIndex ?? 0);

      this.editorModalInstance?.show();
    });
    // Sprint-2: audit timeline loads in parallel.
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

  /** Sprint-2: action → Bootstrap chip class + icon + label. */
  auditPill(action: string): { cls: string; icon: string; label: string } {
    switch ((action || '').trim()) {
      case 'Updated':         return { cls: 'bg-info',                icon: 'bi-pencil-square',     label: 'Updated'         };
      case 'Sent':            return { cls: 'bg-primary',             icon: 'bi-send-fill',         label: 'Sent'            };
      case 'ReceiptsSynced':  return { cls: 'bg-success',             icon: 'bi-box-seam',          label: 'Receipts updated'};
      case 'Closed':          return { cls: 'bg-success',             icon: 'bi-archive-fill',      label: 'Closed'          };
      case 'Cancelled':       return { cls: 'bg-danger',              icon: 'bi-x-circle',          label: 'Cancelled'       };
      default:                return { cls: 'bg-secondary',           icon: 'bi-clock-history',     label: action || '—'     };
    }
  }

  /** Stepper helper — past=green, current=blue+bold, future=grey.
   *  Cancelled is the terminal red sibling (index 5). */
  stepperState(stageIndex: number): { dotCls: string; lineCls: string; textCls: string } {
    if (this.workflowStageIndex === 5) {
      if (stageIndex < 1)               return { dotCls: 'bg-success border-success text-white', lineCls: 'bg-success', textCls: 'text-success' };
      if (stageIndex === 5)             return { dotCls: 'bg-danger border-danger text-white',   lineCls: 'bg-danger',  textCls: 'text-danger fw-bold' };
      return { dotCls: 'bg-light border text-muted', lineCls: 'bg-light', textCls: 'text-muted' };
    }
    if (stageIndex < this.workflowStageIndex)
      return { dotCls: 'bg-success border-success text-white', lineCls: 'bg-success', textCls: 'text-success' };
    if (stageIndex === this.workflowStageIndex)
      return { dotCls: 'bg-primary border-primary text-white', lineCls: 'bg-light',   textCls: 'text-primary fw-bold' };
    return { dotCls: 'bg-light border text-muted', lineCls: 'bg-light', textCls: 'text-muted' };
  }

  /** PARTIALLY_RECEIVED → "Partial", FULLY_RECEIVED → "Received". */
  stageLabel(stage: string): string {
    if (!stage) return '';
    if (stage === 'PARTIALLY_RECEIVED') return 'Partial';
    if (stage === 'FULLY_RECEIVED')     return 'Received';
    return stage.split('_')
      .map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase())
      .join(' ');
  }

  private applyDetail(header: PoRow, items: PoItemRow[], grns: GrnSummaryRow[]): void {
    this.current = header;
    this.items = (items || []).map(i => ({
      id: i.id,
      rawItemName: i.rawItemName,
      unitCode: i.unitCode || '',
      orderedQty: Number(i.orderedQty || 0),
      rate: Number(i.rate || 0),
      receivedQty: Number(i.receivedQty || 0),
      remarks: i.remarks || ''
    }));
    this.grns = grns || [];
  }

  refresh(): void {
    if (!this.current?.id) return;
    this.svc.detail(this.current.id).subscribe(r => {
      if (r?.success && r.data) this.applyDetail(r.data.header, r.data.items, r.data.grns);
    });
  }

  closeEditor(): void {
    // Drop clone-preview state so reopening a different row doesn't flash
    // the previous source's data.
    this.cloneSourceId = null;
    this.cloneSourceCode = '';
    this.editorModalInstance?.hide();
  }

  saveChanges(): void {
    if (!this.current?.id) return;
    if (this.current.status !== 'DRAFT') return alert('Only DRAFT POs can be edited.');

    const payload = {
      id: this.current.id,
      expectedDeliveryDate: this.current.expectedDeliveryDate || null,
      paymentTerms: this.current.paymentTerms || '',
      deliveryTerms: this.current.deliveryTerms || '',
      remarks: this.current.remarks || '',
      items: this.items.map(i => ({
        id: i.id,
        orderedQty: Number(i.orderedQty),
        rate: Number(i.rate),
        remarks: i.remarks
      } as PoLineUpdatePayload))
    };

    this.saving = true;
    this.svc.update(payload).subscribe({
      next: r => {
        this.saving = false;
        if (!r?.success) return alert(r?.message || 'Save failed.');
        this.refresh(); this.loadList();
      },
      error: e => {
        this.saving = false;
        alert(e?.error?.message || e?.message || 'Save failed.');
      }
    });
  }

  send(): void {
    if (!this.current?.id) return;
    if (!confirm('Send PO to supplier?')) return;
    this.svc.send(this.current.id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Send failed.');
      this.refresh(); this.loadList();
    });
  }

  close(): void {
    if (!this.current?.id) return;
    if (!confirm('Mark PO as Closed?')) return;
    this.svc.close(this.current.id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Close failed.');
      this.refresh(); this.loadList();
    });
  }

  openCancel(): void { this.cancelReason = ''; this.cancelModalInstance?.show(); }
  confirmCancel(): void {
    if (!this.current?.id) return;
    if (!this.cancelReason.trim()) return alert('Cancellation reason is required.');
    this.svc.cancel(this.current.id, this.cancelReason.trim()).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Cancel failed.');
      this.cancelModalInstance?.hide();
      this.refresh(); this.loadList();
    });
  }

  goToReceiveGrn(): void {
    if (!this.current?.id) return;
    // Hand off to the existing Purchases (GRN) screen with the PO pre-selected.
    this.router.navigate(['/purchase'], { queryParams: { poId: this.current.id } });
  }

  badgeClass(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'DRAFT':              return 'bg-secondary';
      case 'SENT':               return 'bg-primary';
      case 'PARTIALLY_RECEIVED': return 'bg-warning text-dark';
      case 'FULLY_RECEIVED':     return 'bg-info';
      case 'CLOSED':             return 'bg-success';
      case 'CANCELLED':          return 'bg-danger';
      default: return 'bg-light text-dark';
    }
  }

  // ────────────────────────────────────────────────────────────────────
  //  SPRINT-1  ROW HELPERS — short status label, progress bar colour,
  //  overdue badge, deep-links to PR and Supplier.
  // ────────────────────────────────────────────────────────────────────

  /** Shorter label for the chip — collapses the verbose values so the
   *  badge stays narrow and the column doesn't wrap. */
  statusLabel(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'PARTIALLY_RECEIVED': return 'Partial';
      case 'FULLY_RECEIVED':     return 'Received';
      default: return (status || '').replace(/_/g, ' ');
    }
  }

  /** Progress bar colour by threshold (same scheme as JO/WO). */
  progressBarClass(percent: number | null | undefined): string {
    const p = Math.max(0, Math.min(100, Number(percent ?? 0)));
    if (p >= 100) return 'bg-success';
    if (p >= 75)  return 'bg-info';
    if (p >= 25)  return 'bg-warning';
    return 'bg-danger';
  }

  /** Overdue / due-soon / on-track badge for the Expected delivery cell.
   *  Returns null when no expected date is set or the PO is closed. */
  dueBadge(row: any): { label: string; cssClass: string; icon: string } | null {
    if (!row?.expectedDeliveryDate) return null;
    // Backend already tagged overdueDays when expected < today AND status is
    // still open (DRAFT / SENT / PARTIALLY_RECEIVED). Use that directly for
    // overdue; compute due-soon ourselves for the look-ahead window.
    const overdueDays = Number(row?.overdueDays || 0);
    if (overdueDays > 0) {
      return { label: `Overdue ${overdueDays}d`, cssClass: 'bg-danger text-white', icon: 'bi-exclamation-triangle-fill' };
    }

    const due = new Date(row.expectedDeliveryDate);
    if (isNaN(due.getTime())) return null;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    due.setHours(0, 0, 0, 0);
    const diffDays = Math.round((due.getTime() - today.getTime()) / 86_400_000);

    // Suppress the badge entirely on closed/cancelled/fully-received rows
    // — the date is historical, not actionable.
    const s = (row?.status || '').toUpperCase();
    if (s === 'FULLY_RECEIVED' || s === 'CLOSED' || s === 'CANCELLED') return null;

    if (diffDays === 0)  return { label: 'Due today',          cssClass: 'bg-warning text-dark',  icon: 'bi-exclamation-circle-fill' };
    if (diffDays <= 3)   return { label: `Due in ${diffDays}d`, cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-clock-history' };
    if (diffDays <= 7)   return { label: `In ${diffDays}d`,    cssClass: 'bg-info-subtle text-info-emphasis border', icon: 'bi-calendar-event' };
    return null;
  }

  /** Deep-link to the source PR — opens the PR list pre-filtered by code,
   *  same pattern WO / Plan rows use. */
  gotoPr(row: any): void {
    if (!row?.prCode) return;
    this.router.navigate(['/purchase-requisition'], { queryParams: { search: row.prCode } });
  }

  /** Deep-link to the supplier — opens the suppliers admin page filtered
   *  by name. The Gen_Suppliers list page reads `?search=`. */
  gotoSupplier(row: any): void {
    if (!row?.supplierName) return;
    this.router.navigate(['/gen-suppliers'], { queryParams: { search: row.supplierName } });
  }

  outstanding(i: EditableLine): number {
    return Math.max(0, Number(i.orderedQty || 0) - Number(i.receivedQty || 0));
  }

  receivedPercent(): number {
    if (!this.current || !this.current.totalAmount) return 0;
    return Math.round((this.current.receivedAmount / this.current.totalAmount) * 100);
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

  bulkSend(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Send ${ids.length} PO(s) to supplier? Each must be DRAFT and have an Expected Delivery Date set.`)) return;
    this.bulkBusy = true;
    this.svc.bulkSend(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'sent'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk send failed.'); }
    });
  }

  bulkClose(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Close ${ids.length} PO(s)? Drafts and already-closed/cancelled rows will be skipped.`)) return;
    this.bulkBusy = true;
    this.svc.bulkClose(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'closed'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk close failed.'); }
    });
  }

  bulkCancel(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const reason = prompt(`Cancel ${ids.length} PO(s)? Enter a reason (required, applied to all):`, '');
    if (!reason || !reason.trim()) return;
    this.bulkBusy = true;
    this.svc.bulkCancel(ids, reason.trim()).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'cancelled'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk cancel failed.'); }
    });
  }

  /** Shared post-bulk handler — same pattern as other modules. */
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

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} PO(s)? Only DRAFT and CANCELLED rows will be removed — Sent / Received / Closed will be skipped with a reason.`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'deleted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk delete failed.'); }
    });
  }

  /** Can this row be deleted? DRAFT and CANCELLED only — same rule as
   *  the backend gate, but evaluated upfront so the button is greyed out
   *  on rows where it would refuse. */
  canDelete(row: any): boolean {
    const s = (row?.status || '').toUpperCase();
    return s === 'DRAFT' || s === 'CANCELLED';
  }

  /** Soft-delete a single PO. Confirms and reloads. */
  deletePO(row: PoRow): void {
    if (!row?.id) return;
    if (!this.canDelete(row)) {
      alert('Only DRAFT or CANCELLED POs can be deleted. Cancel the PO first if it has been sent.');
      return;
    }
    if (!confirm(`Delete ${row.poCode}? This soft-deletes the PO (the audit log is kept).`)) return;
    this.svc.delete(row.id).subscribe({
      next: (res: any) => {
        if (!res?.success) { alert(res?.message || 'Delete failed.'); return; }
        this.loadList();
      },
      error: (err: any) => alert(err?.error?.message || err?.message || 'Delete failed.')
    });
  }

  /** Clone preview: open the editor with source data pre-filled but DO NOT
   *  call the backend yet. The new PO row only appears in the list after
   *  the buyer clicks "Save Clone" in the footer. Lets the operator tweak
   *  qty / rate / dates / terms before committing. */
  cloneP(row: PoRow): void {
    if (!row?.id) return;
    this.svc.detail(row.id).subscribe(r => {
      if (!r?.success || !r.data) {
        alert('Failed to load source PO for clone preview.');
        return;
      }
      // Stage a synthetic header derived from the source. id = 0 marks
      // this as not-yet-persisted; cloneSourceId carries the real id so
      // saveClone() knows which row to clone.
      const src = r.data.header;
      this.cloneSourceId   = src.id;
      this.cloneSourceCode = src.poCode;

      this.current = {
        ...src,
        id: 0,
        poCode: '(new clone of ' + src.poCode + ')',
        prId: null, prCode: null,
        status: 'DRAFT',
        receivedAmount: 0,
        sentBy: null, sentDate: null,
        closedDate: null, cancelledDate: null, cancellationReason: null,
        // Auto-Date the new PO to today; expected date stays unset until
        // the buyer fills it in.
        orderDate: new Date().toISOString().substring(0, 10),
        expectedDeliveryDate: null,
        remarks: 'Cloned from ' + src.poCode
      } as PoRow;

      this.items = (r.data.items || []).map(i => ({
        id: i.id,
        rawItemName: i.rawItemName,
        unitCode: i.unitCode || '',
        orderedQty: Number(i.orderedQty || 0),
        rate: Number(i.rate || 0),
        receivedQty: 0,
        remarks: i.remarks || ''
      }));
      this.grns = []; // no GRNs on an unsaved clone

      // Stepper sits at Draft for the preview.
      this.workflowStages = ['DRAFT', 'SENT', 'PARTIALLY_RECEIVED', 'FULLY_RECEIVED', 'CLOSED', 'CANCELLED'];
      this.workflowStageIndex = 0;
      this.auditLog = []; // no audit yet — record doesn't exist

      this.editorModalInstance?.show();
    });
  }

  /** Commit the clone preview — POST /clone/{sourceId} with the user's
   *  overrides (items + dates + terms). On success, the list reloads and
   *  the new draft appears. If the buyer closes without saving, nothing
   *  is persisted. */
  saveClone(): void {
    if (!this.cloneSourceId) return;
    this.cloningSaving = true;

    // Resolve each source item's RawItemId via the previously-loaded
    // source detail. Since we only kept the EditableLine shape (no
    // rawItemId), re-fetch the source items just to build the override
    // payload keyed by RawItemId. Cheap — same call already cached.
    this.svc.detail(this.cloneSourceId).subscribe(r => {
      const srcItems = r?.data?.items ?? [];
      const overrides = {
        expectedDeliveryDate: this.current?.expectedDeliveryDate || null,
        paymentTerms: this.current?.paymentTerms || '',
        deliveryTerms: this.current?.deliveryTerms || '',
        remarks: this.current?.remarks || '',
        items: this.items.map((edited, idx) => {
          const src = srcItems[idx];
          return {
            rawItemId: src?.rawItemId ?? 0,
            orderedQty: Number(edited.orderedQty),
            rate: Number(edited.rate),
            remarks: edited.remarks || ''
          };
        }).filter(x => x.rawItemId > 0)
      };

      this.svc.clone(this.cloneSourceId!, overrides).subscribe({
        next: (res: any) => {
          this.cloningSaving = false;
          if (!res?.success) {
            alert(res?.message || 'Clone save failed.');
            return;
          }
          const d = res?.data ?? {};
          alert(`Cloned to ${d.poCode} (${d.itemsCloned} line(s)).`);
          this.cloneSourceId = null;
          this.cloneSourceCode = '';
          this.editorModalInstance?.hide();
          this.loadList();
        },
        error: (err: any) => {
          this.cloningSaving = false;
          alert(err?.error?.message || err?.message || 'Clone save failed.');
        }
      });
    });
  }

  /** True when the editor modal is currently showing a clone preview. */
  isCloning(): boolean { return this.cloneSourceId !== null; }
}
