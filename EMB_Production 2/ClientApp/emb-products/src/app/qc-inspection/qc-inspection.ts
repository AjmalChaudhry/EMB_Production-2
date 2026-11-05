import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { QcInspectionService } from '../services/qc-inspection.service';
import {QcInspectionRowDto,QcInspectionUpdateDto,QcInspectionItemRowDto
} from '../shared/models/qc-inspection.dto';

@Component({
  standalone: true,
  selector: 'app-qc-inspection',
  imports: [CommonModule, FormsModule],
  templateUrl: './qc-inspection.html',
  styleUrls: ['./qc-inspection.css']
})
export class QcInspectionComponent implements OnInit {
  rows: QcInspectionRowDto[] = [];
  loading = false;

  summary = this.getEmptySummary();

  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 1;

  searchText = '';
  statusFilter = '';
  showDeleted = false;

  // Phase N.0 - additional filters
  filterFromDate = '';
  filterToDate = '';
  filterWorkOrderId: number | null = null;
  filterProductId: number | null = null;
  filterBranchId: number | null = null;
  filterPosted = '';
  filterInspectionType = '';
  filterSourceType = '';

  // Phase N.0 - beefed summary
  summaryExtra = {
    count: 0,
    postedCount: 0,
    draftCount: 0,
    byStatus: [] as Array<{ status: string; count: number }>,
    byInspectionType: [] as Array<{ type: string; count: number }>,
    bySourceType: [] as Array<{ source: string; count: number }>
  };

  // Phase N.1 - profile drawer
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  expandedRowIds = new Set<number>();
  itemLoadingIds = new Set<number>();

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  PROCESS QC quick-action modal
  //  Opens a focused modal with pre-filled defaults (Inspected = Batch,
  //  Pass = Inspected, Fail = 0, Rework = 0). Operator adjusts sliders
  //  and clicks "Process" or "Process & Post" — both fire through the
  //  same /process/{id} endpoint; the post-and-go variant chains into
  //  the existing /post/{id} flow on the backend.
  // ──────────────────────────────────────────────────────────────────
  showProcessModal = false;
  processBusy = false;
  processForm: {
    qcId: number | null;
    qcNo: string;
    batchQty: number;
    inspectedQty: number;
    passedQty: number;
    failedQty: number;
    reworkQty: number;
    issuesFound: string;
    remarks: string;
  } = {
    qcId: null, qcNo: '', batchQty: 0,
    inspectedQty: 0, passedQty: 0, failedQty: 0, reworkQty: 0,
    issuesFound: '', remarks: ''
  };

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  WORKFLOW STEPPER + AUDIT TIMELINE
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['Pending', 'InProgress', 'AwaitingRework', 'AwaitingReinspection', 'Completed', 'Reversed'];
  workflowStageIndex = 0;
  auditLog: any[] = [];
  auditLogLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions toolbar
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  showModal = false;
  selectedQc: QcInspectionRowDto | null = null;
  qcForm: QcInspectionUpdateDto = this.createEmptyForm();

  showReverseModal = false;
  reverseReason = '';
  modalAction: 'edit' | 'continue' = 'edit';

  constructor(
    private svc: QcInspectionService,
    private router: Router
  ) { }

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.loading = true;

    this.svc.list({
      search: this.searchText || '',
      status: this.statusFilter || '',
      showDeleted: this.showDeleted || false,
      page: this.page || 1,
      pageSize: this.pageSize || 10,
      fromDate: this.filterFromDate || null,
      toDate: this.filterToDate || null,
      workOrderId: this.filterWorkOrderId,
      productId: this.filterProductId,
      branchId: this.filterBranchId,
      posted: this.filterPosted || null,
      inspectionType: this.filterInspectionType || null,
      sourceType: this.filterSourceType || null
    }).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success || !res?.data) {
          this.rows = [];
          this.summary = this.getEmptySummary();
          this.totalItems = 0;
          this.totalPages = 1;
          return;
        }

        const data = res.data;

        this.rows = (data.items ?? []).map((row: any) => ({
          ...row,
          items: Array.isArray(row.items) ? row.items : [],
          itemsLoaded: !!row.itemsLoaded,
          isEditingItems: false
        }));

        this.summary = {
          batchQty: Number(data.summary?.batchQty ?? 0),
          inspectedQty: Number(data.summary?.inspectedQty ?? 0),
          passedQty: Number(data.summary?.passedQty ?? 0),
          failedQty: Number(data.summary?.failedQty ?? 0),
          reworkQty: Number(data.summary?.reworkQty ?? 0)
        };

        // Phase N.0 - unpack beefed summary
        const sm = res?.rawSummary ?? {};
        this.summaryExtra = {
          count: Number(sm.count ?? 0),
          postedCount: Number(sm.postedCount ?? 0),
          draftCount: Number(sm.draftCount ?? 0),
          byStatus: Array.isArray(sm.byStatus) ? sm.byStatus : [],
          byInspectionType: Array.isArray(sm.byInspectionType) ? sm.byInspectionType : [],
          bySourceType: Array.isArray(sm.bySourceType) ? sm.bySourceType : []
        };

        this.totalItems = Number(data.totalItems ?? 0);
        this.totalPages = Number(data.totalPages ?? 1);
        this.page = Number(data.page ?? this.page);
        this.pageSize = Number(data.pageSize ?? this.pageSize);
      },
      error: (err) => {
        console.error('QC Load Error:', err);
        this.loading = false;
        this.rows = [];
        this.summary = this.getEmptySummary();
        this.totalItems = 0;
        this.totalPages = 1;
      }
    });
  }

  onSearchChange(): void {
    this.page = 1;
    this.loadData();
  }

  pagedRows(): QcInspectionRowDto[] {
    return this.rows;
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages) return;
    this.page = p;
    this.loadData();
  }

  toggleRow(row: QcInspectionRowDto): void {
    if (this.expandedRowIds.has(row.id)) {
      this.expandedRowIds.delete(row.id);
      return;
    }

    this.expandedRowIds.add(row.id);

    if (!row.itemsLoaded) {
      this.loadRowItems(row);
    }
  }

  isExpanded(row: QcInspectionRowDto): boolean {
    return this.expandedRowIds.has(row.id);
  }

  loadRowItems(row: QcInspectionRowDto): void {
    if (!row?.id) return;

    this.itemLoadingIds.add(row.id);

    this.svc.detail(row.id).subscribe({
      next: (res) => {
        const data = res?.data;

        row.items = Array.isArray(data?.items) ? data.items : [];
        row.itemsLoaded = true;

        row.itemsPending = data?.itemsPending ?? row.itemsPending;
        row.itemsCompleted = data?.itemsCompleted ?? row.itemsCompleted;
        row.itemsStatusText = data?.itemsStatusText ?? row.itemsStatusText;
        row.inspectionProgressText = data?.inspectionProgressText ?? row.inspectionProgressText;
      },
      error: (err) => {
        console.error('Failed to load QC detail/items', err);
        row.items = [];
        row.itemsLoaded = true;
      },
      complete: () => {
        this.itemLoadingIds.delete(row.id);
      }
    });
  }

  isItemLoading(row: QcInspectionRowDto): boolean {
    return this.itemLoadingIds.has(row.id);
  }

  getStatusClass(r: QcInspectionRowDto): any {
    const status = (r.status || '').toLowerCase();

    return {
      'bg-secondary': status === 'pending',
      'bg-warning text-dark': status === 'inprogress',
      'bg-success': status === 'completed' || status === 'posted',
      'bg-danger': status === 'reversed' || status === 'cancelled'
    };
  }

  getStatusIcon(r: QcInspectionRowDto): string {
    const status = (r.status || '').toLowerCase();

    if (status === 'completed' || status === 'posted') return 'bi-check-circle';
    if (status === 'reversed') return 'bi-arrow-counterclockwise';
    if (status === 'cancelled') return 'bi-x-circle';
    if (status === 'inprogress') return 'bi-hourglass-split';
    if (status === 'awaitingrework') return 'bi-tools';
    if (status === 'awaitingreinspection') return 'bi-clipboard-check';

    return 'bi-clock';
  }

  getStatusText(r: QcInspectionRowDto): string {
    const status = (r.status || '').toLowerCase();
    if (status === 'awaitingrework') return 'Awaiting Rework';
    if (status === 'awaitingreinspection') return 'Awaiting Re-inspection';
    return r.status || 'Pending';
  }

  createEmptyForm(): QcInspectionUpdateDto {
    return {
      id: 0,
      inspectedQty: 0,
      passedQty: 0,
      failedQty: 0,
      reworkQty: 0,
      remarks: null,
      issuesFound: null,
      status: 'Pending',
      inspectorId: null,
      inspectionDate: null,
      inspectionType: null,
      sourceType: null
    };
  }

  openEdit(row: QcInspectionRowDto): void {
    if (!this.canEdit(row)) {
      alert('This QC record cannot be edited after posting/reversal.');
      return;
    }

    this.modalAction = 'edit';
    this.selectedQc = row;

    this.qcForm = {
      id: row.id,
      inspectedQty: Number(row.inspectedQty || 0),
      passedQty: Number(row.passedQty || 0),
      failedQty: Number(row.failedQty || 0),
      reworkQty: Number(row.reworkQty || 0),
      remarks: null,
      issuesFound: null,
      status: row.status || 'Pending',
      inspectorId: null,
      inspectionDate: row.inspectionDate || null,
      inspectionType: row.inspectionType || null,
      sourceType: row.sourceType || null
    };

    this.showModal = true;
  }

  closeEdit(): void {
    this.showModal = false;
    this.selectedQc = null;
    this.qcForm = this.createEmptyForm();
    this.modalAction = 'edit';
  }

  isSaveDisabled(): boolean {
    if (!this.selectedQc) return true;

    const inspected = Number(this.qcForm.inspectedQty || 0);
    const passed = Number(this.qcForm.passedQty || 0);
    const failed = Number(this.qcForm.failedQty || 0);
    const rework = Number(this.qcForm.reworkQty || 0);
    const batch = Number(this.selectedQc.batchQty || 0);

    if (inspected < 0 || passed < 0 || failed < 0 || rework < 0) return true;
    if (inspected > batch) return true;

    return inspected !== passed + failed + rework;
  }

  saveQc(): void {
    if (!this.selectedQc) {
      alert('No QC record selected.');
      return;
    }

    if (this.isSaveDisabled()) {
      alert('Please correct QC quantities before saving.');
      return;
    }

    const inspected = Number(this.qcForm.inspectedQty || 0);
    const passed = Number(this.qcForm.passedQty || 0);
    const failed = Number(this.qcForm.failedQty || 0);
    const rework = Number(this.qcForm.reworkQty || 0);

    if (this.modalAction === 'continue') {
      this.svc.continue(this.selectedQc.id, {
        inspectedQty: inspected,
        passedQty: passed,
        failedQty: failed,
        reworkQty: rework,
        remarks: this.qcForm.remarks,
        issuesFound: this.qcForm.issuesFound,
        inspectionDate: this.qcForm.inspectionDate
      }).subscribe({
        next: (res) => {
          alert(res?.message || 'QC updated.');
          if (res?.success) {
            this.closeEdit();
            this.loadData();
          }
        },
        error: (err) => {
          console.error('Continue QC error:', err);
          alert(err?.error?.message || 'Failed to continue QC.');
        }
      });

      return;
    }

    const updatePayload: QcInspectionUpdateDto = {
      id: this.qcForm.id,
      inspectedQty: inspected,
      passedQty: passed,
      failedQty: failed,
      reworkQty: rework,
      remarks: this.qcForm.remarks,
      issuesFound: this.qcForm.issuesFound,
      status: this.getAutoQcStatus(inspected, passed, failed, rework),
      inspectorId: this.qcForm.inspectorId,
      inspectionDate: this.qcForm.inspectionDate,
      inspectionType: this.qcForm.inspectionType,
      sourceType: this.qcForm.sourceType
    };

    this.svc.update(updatePayload).subscribe({
      next: (res) => {
        alert(res?.message || 'QC updated.');
        if (res?.success) {
          this.closeEdit();
          this.loadData();
        }
      },
      error: (err) => {
        console.error('Update QC error:', err);
        alert(err?.error?.message || 'Failed to update QC.');
      }
    });
  }

  openDetail(row: QcInspectionRowDto): void {
    if (!row?.id) return;

    this.expandedRowIds.add(row.id);

    if (!row.itemsLoaded) {
      this.loadRowItems(row);
    }

    row.isEditingItems = false;
  }

  openCompleteItems(row: QcInspectionRowDto): void {
    alert('Component-level QC item completion is disabled. Use Edit QC, then Post QC.');
  }

  postQc(row: QcInspectionRowDto): void {
    if (!this.canPost(row)) {
      alert('QC can be posted only when full batch is inspected and quantities match.');
      return;
    }

    if (!confirm(`Post QC ${row.qcNo || row.id}?`)) {
      return;
    }

    this.svc.post(row.id).subscribe({
      next: (res) => {
        alert(res?.message || 'QC posted.');
        if (res?.success) {
          this.loadData();
        }
      },
      error: (err) => {
        console.error(err);
        alert(err?.error?.message || 'Failed to post QC.');
      }
    });
  }

  cancelPendingQc(row: QcInspectionRowDto): void {
    if (!this.canDelete(row)) {
      alert('Only unposted QC can be cancelled.');
      return;
    }

    if (!confirm('Cancel QC?')) {
      return;
    }

    this.svc.cancelPending(row.id, { reason: 'Cancelled from UI' }).subscribe({
      next: (res) => {
        alert(res?.message || 'Cancelled.');
        if (res?.success) {
          this.loadData();
        }
      },
      error: (err) => {
        console.error(err);
        alert(err?.error?.message || 'Failed to cancel QC.');
      }
    });
  }

  openReverse(row: QcInspectionRowDto): void {
    if (!this.canReverse(row)) {
      alert('Only posted QC can be reversed.');
      return;
    }

    this.selectedQc = row;
    this.reverseReason = '';
    this.showReverseModal = true;
  }

  closeReverse(): void {
    this.showReverseModal = false;
    this.reverseReason = '';
    this.selectedQc = null;
  }

  confirmReverse(): void {
    if (!this.selectedQc) return;

    if (!this.reverseReason.trim()) {
      alert('Reason is required.');
      return;
    }

    this.svc.reverse(this.selectedQc.id, {
      reason: this.reverseReason.trim()
    }).subscribe({
      next: (res) => {
        alert(res?.message || 'Reversed.');
        if (res?.success) {
          this.closeReverse();
          this.loadData();
        }
      },
      error: (err) => {
        console.error(err);
        alert(err?.error?.message || 'Failed to reverse QC.');
      }
    });
  }

  reviewItems(row: QcInspectionRowDto): void {
    this.openDetail(row);
  }

  updateItemResult(item: QcInspectionItemRowDto): void {
    const qty = Number(item.qty || 0);
    const passed = Number(item.passedQty || 0);
    const failed = Number(item.failedQty || 0);
    const rework = Number(item.reworkQty || 0);

    if (qty <= 0) {
      item.result = 'Pending';
      return;
    }

    if (passed === qty) item.result = 'Pass';
    else if (failed === qty) item.result = 'Fail';
    else if (rework === qty) item.result = 'Rework';
    else item.result = 'Partial';
  }

  itemHasError(item: QcInspectionItemRowDto): boolean {
    const qty = Number(item.qty || 0);
    const passed = Number(item.passedQty || 0);
    const failed = Number(item.failedQty || 0);
    const rework = Number(item.reworkQty || 0);

    if (qty <= 0) return true;
    if (passed < 0 || failed < 0 || rework < 0) return true;

    return passed + failed + rework !== qty;
  }

  saveInlineItem(row: QcInspectionRowDto, item: QcInspectionItemRowDto): void {
    alert('Inline QC item editing is disabled. Use Edit QC, then Post QC.');
  }

  isSavingItem(item: QcInspectionItemRowDto): boolean {
    return false;
  }

  addNewItem(row: QcInspectionRowDto): void {
    alert('Adding QC items manually is disabled. QC is product/batch level.');
  }

  onItemKeydown(event: KeyboardEvent): void {
    const current = event.target as HTMLElement;

    if (event.key === 'Enter') {
      event.preventDefault();

      const inputs = Array.from(
        document.querySelectorAll('input.form-control-sm, textarea.form-control-sm, select.form-select-sm')
      ) as HTMLElement[];

      const index = inputs.indexOf(current);

      if (index >= 0 && index < inputs.length - 1) {
        inputs[index + 1].focus();
      }
    }
  }

  hasItemMismatch(row: QcInspectionRowDto): boolean {
    return row?.items?.some(i => this.itemHasError(i)) ?? false;
  }

  getEmptySummary() {
    return {
      batchQty: 0,
      inspectedQty: 0,
      passedQty: 0,
      failedQty: 0,
      reworkQty: 0
    };
  }

  // Phase N.0 - filter helpers
  onFiltersChange(): void {
    this.page = 1;
    this.loadData();
  }
  clearFilters(): void {
    this.searchText = '';
    this.statusFilter = '';
    this.showDeleted = false;
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterWorkOrderId = null;
    this.filterProductId = null;
    this.filterBranchId = null;
    this.filterPosted = '';
    this.filterInspectionType = '';
    this.filterSourceType = '';
    this.page = 1;
    this.loadData();
  }
  qcStatusPill(s: string): { cssClass: string; icon: string } {
    const u = (s ?? '').toString().trim().toUpperCase();
    if (u === 'POSTED' || u === 'COMPLETED' || u === 'APPROVED' || u === 'PASSED')
      return { cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'REJECTED' || u === 'FAILED' || u === 'CANCELLED' || u === 'CANCELED' || u === 'REVERSED')
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    if (u === 'REWORK' || u === 'PENDING_REWORK' || u === 'AWAITINGREWORK')
      return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-tools' };
    if (u === 'AWAITINGREINSPECTION')
      return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-clipboard-check' };
    if (u === 'PENDING' || u === 'OPEN' || u === 'DRAFT' || !u)
      return { cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
    if (u.includes('PROGRESS') || u === 'WIP' || u === 'IN_PROGRESS')
      return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }

  // Phase N.1 - Profile drawer
  openProfile(id: number): void {
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.svc.getProfile(id).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          alert(res?.message || 'Failed to load profile.');
          this.showProfile = false;
          return;
        }
        this.profile = res?.data ?? res?.Data ?? null;
        // Sprint-2: snapshot stepper state from profile + lazy-load
        // the audit log so the drawer streams in the timeline.
        const stages = this.profile?.workflowStages;
        const idx    = this.profile?.workflowStageIndex;
        if (Array.isArray(stages) && stages.length) this.workflowStages = stages;
        if (typeof idx === 'number') this.workflowStageIndex = idx;
        this.loadAuditLog(id);
      },
      error: err => {
        this.profileLoading = false;
        console.error('QC profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
    this.auditLog = [];
  }

  openQcItems(row: QcInspectionRowDto): void {
    if (!row?.id) return;

    this.router.navigate(['/qc-inspection-items'], {
      queryParams: { qcInspectionId: row.id }
    });
  }

  private getAutoQcStatus(
    inspectedQty: number,
    passedQty: number,
    failedQty: number,
    reworkQty: number
  ): 'Pending' | 'InProgress' {
    if (inspectedQty <= 0) return 'Pending';

    const total = passedQty + failedQty + reworkQty;
    if (total !== inspectedQty) return 'Pending';

    return 'InProgress';
  }

  isReversed(row: any): boolean {
    return row?.isReversed === true ||
      String(row?.status ?? '').toLowerCase() === 'reversed';
  }

  isCompleted(row: any): boolean {
    return row?.isPosted === true ||
      String(row?.status ?? '').toLowerCase() === 'completed' ||
      String(row?.status ?? '').toLowerCase() === 'posted';
  }

  canEdit(row: any): boolean {
    return !!row &&
      !this.isReversed(row) &&
      row?.isPosted !== true;
  }

  /** A row can be "Processed" (1-click batch-defaulted post) whenever
   *  it has remaining batch qty and isn't already posted or reversed.
   *  Intentionally DOES NOT honour the server's `canProcess` flag —
   *  that one is gated tightly to Status='Pending', which hid the
   *  button on InProgress rows the operator was actively working. The
   *  client owns visibility now: Pending, InProgress, AwaitingRework
   *  — anything not yet Posted/Reversed with qty left can be Processed. */
  canProcess(row: any): boolean {
    if (!row) return false;
    if (this.isReversed(row)) return false;
    if (row.isPosted === true) return false;
    const batch = Number(row.batchQty ?? 0);
    const inspected = Number(row.inspectedQty ?? 0);
    return batch > 0 && inspected < batch;
  }

  canDelete(row: any): boolean {
    return !!row &&
      !this.isReversed(row) &&
      row?.isPosted !== true;
  }

  canCompleteItems(row: QcInspectionRowDto): boolean {
    return false;
  }

  canContinueQc(row: any): boolean {
    const batch = Number(row?.batchQty ?? 0);
    const inspected = Number(row?.inspectedQty ?? 0);

    return !!row &&
      !this.isReversed(row) &&
      row?.isPosted !== true &&
      batch > 0 &&
      inspected > 0 &&
      inspected < batch;
  }

  canFinalize(row: QcInspectionRowDto): boolean {
    return false;
  }

  canPost(row: any): boolean {
    const batch = Number(row?.batchQty ?? 0);
    const inspected = Number(row?.inspectedQty ?? 0);
    const passed = Number(row?.passedQty ?? 0);
    const failed = Number(row?.failedQty ?? 0);
    const rework = Number(row?.reworkQty ?? 0);

    return !!row &&
      !this.isReversed(row) &&
      row?.isPosted !== true &&
      batch > 0 &&
      inspected >= batch &&
      inspected === passed + failed + rework;
  }

  canReinspect(row: any): boolean {
    // After the rework-loop refactor: only allow Reinspect when QC is
    // waiting for re-check (operator already posted the rework run).
    const status = String(row?.status ?? '').toLowerCase();
    return !!row &&
      !this.isReversed(row) &&
      status === 'awaitingreinspection';
  }

  isAwaitingRework(row: any): boolean {
    return !!row && String(row?.status ?? '').toLowerCase() === 'awaitingrework';
  }

  isAwaitingReinspection(row: any): boolean {
    return !!row && String(row?.status ?? '').toLowerCase() === 'awaitingreinspection';
  }

  canReverse(row: any): boolean {
    return !!row &&
      !this.isReversed(row) &&
      row?.isPosted === true;
  }

  continueQc(row: QcInspectionRowDto): void {
    if (!this.canContinueQc(row)) {
      alert('This QC record cannot be continued.');
      return;
    }

    this.modalAction = 'continue';
    this.selectedQc = row;

    this.qcForm = {
      id: row.id,
      inspectedQty: Number(row.inspectedQty || 0),
      passedQty: Number(row.passedQty || 0),
      failedQty: Number(row.failedQty || 0),
      reworkQty: Number(row.reworkQty || 0),
      remarks: null,
      issuesFound: null,
      status: 'InProgress',
      inspectorId: null,
      inspectionDate: row.inspectionDate || null,
      inspectionType: row.inspectionType || null,
      sourceType: row.sourceType || null
    };

    this.showModal = true;
  }

  reinspectQc(row: QcInspectionRowDto): void {
    if (!this.canReinspect(row)) {
      alert('This QC is not awaiting re-inspection yet. The operator must first post the rework entry from Daily Production.');
      return;
    }

    if (!confirm(`Re-open ${row.qcNo} for the next inspection round?\n\nThe row will revert to InProgress so you can enter this round's pass/fail/rework counts.`)) {
      return;
    }

    this.svc.reinspect(row.id).subscribe({
      next: (res) => {
        alert(res?.message || 'QC re-opened for next round.');
        if (res?.success) {
          this.loadData();
        }
      },
      error: (err) => {
        console.error(err);
        alert(err?.error?.message || 'Failed to re-open QC.');
      }
    });
  }

  finalizeQc(row: QcInspectionRowDto): void {
    alert('Separate finalize is disabled. Use Post QC.');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  PROCESS QC quick-action — opens the focused modal
  //  with batch-defaulted values. The operator can tweak Pass / Fail /
  //  Rework and click Process (saves & stays in InProgress) or
  //  Process & Post (chains into the existing Post flow on the server).
  // ──────────────────────────────────────────────────────────────────
  openProcess(row: QcInspectionRowDto): void {
    if (!this.canProcess(row)) return;
    const batch = Number(row.batchQty || 0);
    this.processForm = {
      qcId: row.id,
      qcNo: row.qcNo || ('QC#' + row.id),
      batchQty: batch,
      inspectedQty: batch,
      passedQty: batch,
      failedQty: 0,
      reworkQty: 0,
      issuesFound: '',
      remarks: ''
    };
    this.showProcessModal = true;
  }

  /** Keep Passed = Inspected − Failed − Rework as the operator slides. */
  recalcProcessPass(): void {
    const insp   = Number(this.processForm.inspectedQty || 0);
    const fail   = Number(this.processForm.failedQty   || 0);
    const rework = Number(this.processForm.reworkQty   || 0);
    const pass   = Math.max(0, insp - fail - rework);
    this.processForm.passedQty = pass;
  }

  closeProcess(): void {
    this.showProcessModal = false;
    this.processForm.qcId = null;
  }

  doProcess(andPost: boolean): void {
    if (!this.processForm.qcId) return;
    const f = this.processForm;
    // Client-side sanity — backend re-validates and is the source of truth.
    if (Number(f.inspectedQty) > Number(f.batchQty)) {
      return alert('Inspected qty cannot exceed batch qty.');
    }
    if ((Number(f.passedQty) + Number(f.failedQty) + Number(f.reworkQty)) !== Number(f.inspectedQty)) {
      return alert('Pass + Fail + Rework must equal Inspected qty.');
    }
    if (andPost && Number(f.reworkQty) > 0) {
      return alert('Cannot Process & Post when there is rework. Process first, then finish the rework loop.');
    }
    this.processBusy = true;
    this.svc.processQc(f.qcId, {
      inspectedQty: f.inspectedQty,
      passedQty:    f.passedQty,
      failedQty:    f.failedQty,
      reworkQty:    f.reworkQty,
      issuesFound:  f.issuesFound,
      remarks:      f.remarks,
      andPost
    }).subscribe({
      next: (res: any) => {
        this.processBusy = false;
        if (!res?.success) return alert(res?.message || 'Process failed.');
        this.closeProcess();
        this.loadData();
      },
      error: (err: any) => {
        this.processBusy = false;
        alert(err?.error?.message || err?.message || 'Process failed.');
      }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  CLICKABLE BADGES
  // ──────────────────────────────────────────────────────────────────
  gotoWorkOrder(row: QcInspectionRowDto): void {
    if (!row?.workOrderId) return;
    location.href = '/Content/emb-products/work-order?search=' + encodeURIComponent(row.workOrderLabel || '');
  }
  gotoProduct(row: QcInspectionRowDto): void {
    if (!row?.productId) return;
    location.href = '/Content/emb-products/products?id=' + row.productId;
  }
  gotoDesign(row: QcInspectionRowDto): void {
    if (!row?.designId) return;
    location.href = '/Content/emb-products/designs?id=' + row.designId;
  }

  /** Sprint-1 — friendly status label (the existing qcStatusPill()
   *  above is reused for the colour scheme). */
  qcStatusLabel(status: string | null | undefined): string {
    const m: { [k: string]: string } = {
      Pending: 'Pending', InProgress: 'In Progress',
      AwaitingRework: 'Awaiting Rework',
      AwaitingReinspection: 'Awaiting Reinspect',
      Completed: 'Completed', Posted: 'Posted',
      Reversed: 'Reversed', Cancelled: 'Cancelled'
    };
    return m[(status || '').trim()] || (status || '-');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  STEPPER state — same shape as the rest of the chain.
  // ──────────────────────────────────────────────────────────────────
  stepperState(stageIdx: number): { done: boolean; current: boolean; cancelled: boolean } {
    const idx = this.workflowStageIndex;
    const isCancelled = idx === 5;
    if (stageIdx === 5)
      return { done: false, current: isCancelled, cancelled: true };
    if (isCancelled)
      return { done: false, current: false, cancelled: false };
    return { done: stageIdx < idx, current: stageIdx === idx, cancelled: false };
  }
  stageLabel(stage: string): string {
    const m: { [k: string]: string } = {
      Pending: 'Pending', InProgress: 'In Progress',
      AwaitingRework: 'Awaiting Rework',
      AwaitingReinspection: 'Awaiting Reinspect',
      Completed: 'Completed', Reversed: 'Reversed', Cancelled: 'Cancelled'
    };
    return m[stage] || stage;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG loader — best-effort, empty on error.
  // ──────────────────────────────────────────────────────────────────
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.svc.getAuditLog(id).subscribe({
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
    if (a === 'CREATED')                                    return 'bg-primary';
    if (a === 'EDITED' || a === 'PROCESSED')                return 'bg-warning text-dark';
    if (a === 'POSTED')                                     return 'bg-success';
    if (a === 'REVERSED' || a === 'CANCELLED')              return 'bg-danger';
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
    return this.rows.length > 0 && this.rows.every(r => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.rows.forEach(r => this.selectedIds.delete(r.id));
    else this.rows.forEach(r => this.selectedIds.add(r.id));
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
  bulkProcess(andPost: boolean): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const verb = andPost ? 'process and post' : 'process';
    if (!confirm(`Auto-${verb} ${ids.length} pending QC(s)? Defaults: Inspected = Batch, Pass = Inspected, Fail = Rework = 0.`)) return;
    this.bulkBusy = true;
    this.svc.bulkProcess(ids, andPost).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, andPost ? 'processed & posted' : 'processed'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk process failed.'); }
    });
  }
  bulkPostQc(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} QC(s)? Finished stock will be written.`)) return;
    this.bulkBusy = true;
    this.svc.bulkPost(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'posted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk post failed.'); }
    });
  }
  bulkReverseQc(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const reason = prompt(`Reverse ${ids.length} posted QC(s)? Reason (optional):`, '');
    if (reason === null) return;
    this.bulkBusy = true;
    this.svc.bulkReverse(ids, reason || '').subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'reversed'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk reverse failed.'); }
    });
  }
  bulkCancelQc(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    const reason = prompt(`Cancel ${ids.length} pending QC(s)? Reason (optional):`, '');
    if (reason === null) return;
    this.bulkBusy = true;
    this.svc.bulkCancel(ids, reason || '').subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'cancelled'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk cancel failed.'); }
    });
  }
}
