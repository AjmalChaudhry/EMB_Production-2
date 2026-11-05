import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import {FormsModule,ReactiveFormsModule,
FormGroup,FormBuilder,Validators
} from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { Router } from '@angular/router';
import { JobOrdersService } from '../services/job-orders.service';
import {JobOrderDetailViewModel,JobOrderRowViewModel,
JobOrderDto,JobOrderType
} from '../shared/models/job-order.dto';

declare var window: any;

@Component({
  selector: 'app-job-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, HttpClientModule],
  templateUrl: './job-orders.component.html',
  styleUrls: ['./job-orders.component.css']
})
export class JobOrdersComponent implements OnInit, OnDestroy {

  searchText = '';
  activeFilter = '';
  loading = false;

  page = 1;
  pageSize = 20;
  totalItems = 0;
  totalPages = 1;
  pageArray: number[] = [];

  orders: JobOrderRowViewModel[] = [];

  // Phase M.0 filters
  filterFromDate = '';
  filterToDate = '';
  filterCustomerId: number | null = null;
  filterBranchId: number | null = null;
  filterPosted = '';
  filterOrderType = '';
  filterPriority = '';
  filterStatus = '';

  summary = {
    count: 0,
    totalQty: 0,
    postedCount: 0,
    draftCount: 0,
    customerOrderCount: 0,
    stockOrderCount: 0,
    byStatus: [] as Array<{ status: string; count: number }>,
    byPriority: [] as Array<{ priority: string; count: number }>
  };

  // Phase M.1 - 360 profile
  profile: any = null;
  profileLoading = false;

  // Sprint-4 audit timeline state — fetched alongside the profile so the
  // drawer renders both panels in a single render pass.
  auditLog: Array<{
    id: number;
    jobOrderId: number;
    action: string;
    changedBy: number | null;
    changedByName: string | null;
    changedDate: string;
    oldValue: string | null;
    newValue: string | null;
    detail: string | null;
  }> = [];
  auditLoading = false;
  showProfile = false;

  customers: any[] = [];
  designs: any[] = [];
  branches: any[] = [];
  products: any[] = [];

  selectedProductLabel = '—';
  componentPreviewRows: any[] = [];
  previewLoading = false;

  form!: FormGroup;
  modalMode: 'create' | 'edit' = 'create';
  editingId: number | null = null;

  selectedOrder: JobOrderDetailViewModel | null = null;
  deleteId: number | null = null;

  constructor(
    private fb: FormBuilder,
    private jobOrderSvc: JobOrdersService,
    private router: Router
  ) { }

  // Sprint-2: navigate to a related list page pre-filtered by THIS JO's id.
  // Earlier versions passed ?search=<code>, but each target page's search
  // matches its own code (WO-…, plan code, dispatch code) — never the JO
  // code — so the deep-link landed on an empty list. We now pass the JO's
  // primary key as ?jobOrderId=<id>; the target list endpoint joins on
  // WorkOrders.JobOrderId / ProductionPlanItems.JobOrderId / Dispatch.JobOrderId.
  // jobOrderCode is still passed as a fallback search term in case the user
  // wants to verify by reading the code in the search box.
  gotoRelated(target: 'work-orders' | 'production-plan' | 'dispatch',
              jobOrderId: number,
              jobOrderCode: string): void {
    if (!jobOrderId) return;
    this.router.navigate(['/' + target], {
      queryParams: { jobOrderId, search: jobOrderCode || '' }
    });
  }

  // ─────────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK ACTIONS
  //  Multi-select using a Set<number> of selected JO ids. Selection persists
  //  across the visible page only — paging or filter change resets it (the
  //  user's mental model: bulk operates on what's currently on screen).
  //  The bulk toolbar is shown only when ≥ 1 row is selected.
  // ─────────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  selectedCount(): number { return this.selectedIds.size; }
  allOnPageSelected(): boolean {
    return this.orders.length > 0 && this.orders.every(o => this.selectedIds.has(o.id));
  }
  someOnPageSelected(): boolean {
    return !this.allOnPageSelected() && this.orders.some(o => this.selectedIds.has(o.id));
  }

  toggleRow(id: number, checked: boolean): void {
    if (checked) this.selectedIds.add(id);
    else this.selectedIds.delete(id);
  }

  toggleAllOnPage(checked: boolean): void {
    if (checked) this.orders.forEach(o => this.selectedIds.add(o.id));
    else this.orders.forEach(o => this.selectedIds.delete(o.id));
  }

  clearSelection(): void { this.selectedIds.clear(); }

  // Sprint-3 clone: deep-copy the source JO into a new Draft and open the
  // edit modal so the operator can adjust priority / qty / due-date before
  // posting. Recurring orders go from "8 clicks to retype" to "2 clicks +
  // tweak". Posted source rows are still cloneable — the clone is a Draft.
  /** Clone preview — open the form modal in create mode pre-filled with
   *  the source order's customer / product / design / qty / dates. No
   *  backend call yet. The new draft only appears in the list after the
   *  buyer clicks Save (routes through jobOrderSvc.create because
   *  modalMode === 'create'). Closing without saving abandons the preview. */
  cloneOrder(id: number, code: string): void {
    if (!id) return;
    this.cleanupBootstrapModal();
    this.modalMode = 'create';
    this.editingId = null;
    this.selectedProductLabel = '—';
    this.componentPreviewRows = [];

    this.jobOrderSvc.getDetail(id).subscribe({
      next: res => {
        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load source order for clone preview.');
          return;
        }
        const d = res.data;

        // Pre-fill the form. Id = 0 marks this as a fresh create. The
        // jobOrderCode is left blank so the backend allocates a fresh one.
        this.form.reset({
          id: 0,
          jobOrderCode: '',
          orderType: d.orderType,
          isCompanyStockOrder: d.isCompanyStockOrder,
          customerId: d.customerId,
          productId: d.productId,
          designId: d.designId,
          branchId: d.branchId,
          orderQty: d.orderQty,
          orderDate: null,                // today's date filled in by the form
          dueDate: d.dueDate,
          priority: d.priority ?? 'Normal',
          status: 'Draft',
          active: true
        });
        this.onOrderTypeChange();

        // Designs list — same lookup the openEdit flow does so the dropdown
        // is populated when the user reviews the form.
        const productId = Number(d.productId || 0);
        if (productId > 0) {
          this.jobOrderSvc.getDesignsByProduct(productId).subscribe({
            next: res2 => {
              this.designs = Array.isArray(res2)
                ? res2.map((x: any) => ({
                  id: Number(x?.id ?? x?.Id ?? 0),
                  productId: Number(x?.productId ?? x?.ProductId ?? productId),
                  code: x?.code ?? x?.Code ?? x?.designCode ?? x?.DesignCode ?? '',
                  name: x?.name ?? x?.Name ?? x?.designName ?? x?.DesignName ?? x?.collectionName ?? x?.CollectionName ?? '',
                  text: x?.text ?? x?.Text ?? x?.designName ?? x?.DesignName ?? x?.collectionName ?? x?.CollectionName ?? x?.name ?? x?.Name ?? ''
                })).filter((x: any) => x.id > 0)
                : [];
              this.form.patchValue({ designId: d.designId }, { emitEvent: true });
              this.onDesignChange();
            },
            error: () => { this.designs = []; this.loadComponentPreview(); }
          });
        }

        setTimeout(() => this.showModal(), 50);
        // No alert — the modal IS the feedback. User reviews and clicks
        // Save to commit, or closes to discard.
      },
      error: (err: any) => {
        alert(err?.error?.message || err?.message || 'Clone failed.');
      }
    });
  }

  bulkPost(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} job order(s)? Drafts will become Posted.`)) return;
    this.bulkBusy = true;
    this.jobOrderSvc.bulkPost(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'posted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk post failed.'); }
    });
  }

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} job order(s)? Posted ones will be skipped.`)) return;
    this.bulkBusy = true;
    this.jobOrderSvc.bulkDelete(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'deleted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk delete failed.'); }
    });
  }

  bulkSetPriority(priority: string): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length || !priority) return;
    this.bulkBusy = true;
    this.jobOrderSvc.bulkChangePriority(ids, priority).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'updated'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk priority change failed.'); }
    });
  }

  // Common post-bulk handler: surfaces success / failure counts, lists the
  // failed rows so the user can fix and retry, then reloads the list and
  // clears the selection so the UI reflects the new state.
  private afterBulk(res: any, verbLabel: string): void {
    const data = res?.data ?? {};
    const successCount = Number(data.successCount ?? 0);
    const failureCount = Number(data.failureCount ?? 0);
    const failures: Array<{ id: number; code: string; reason: string }> = data.failures ?? [];

    let msg = `${successCount} ${verbLabel}` + (failureCount ? `, ${failureCount} failed.` : '.');
    if (failureCount && failures.length) {
      msg += '\n\nFailed rows:\n' +
        failures.slice(0, 10)
          .map(f => `  • ${f.code || ('#' + f.id)} — ${f.reason}`)
          .join('\n');
      if (failures.length > 10) msg += `\n  …and ${failures.length - 10} more.`;
    }
    alert(msg);

    this.clearSelection();
    this.loadOrders();
  }

  ngOnInit(): void {
    this.initForm();
    this.loadDropdowns();
    this.loadOrders();

    this.form.get('designId')?.valueChanges.subscribe(() => {
      this.loadComponentPreview();
    });

    this.form.get('orderQty')?.valueChanges.subscribe(() => {
      this.loadComponentPreview();
    });
  }

  ngOnDestroy(): void {
    this.cleanupBootstrapModal();
  }

  initForm(): void {
    this.form = this.fb.group({
      id: [0],
      jobOrderCode: [''],

      orderType: ['Customer', Validators.required],
      isCompanyStockOrder: [false],

      customerId: [null, Validators.required],
      productId: [null, Validators.required],
      designId: [null, Validators.required],
      branchId: [null, Validators.required],

      orderQty: [0, [Validators.required, Validators.min(1)]],
      orderDate: [null],
      dueDate: [null],

      priority: ['Normal'],
      status: ['Draft'],
      active: [true]
    });
  }

  cleanupBootstrapModal(): void {
    document.body.classList.remove('modal-open');
    document.body.style.removeProperty('overflow');
    document.body.style.removeProperty('padding-right');

    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());

    document.querySelectorAll('.modal.show').forEach((modal: any) => {
      modal.classList.remove('show');
      modal.style.display = 'none';
      modal.setAttribute('aria-hidden', 'true');
      modal.removeAttribute('aria-modal');
    });
  }

  showModal(): void {
    const modal = document.getElementById('jobOrderModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).show();
  }

  closeModal(): void {
    const modal = document.getElementById('jobOrderModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).hide();
  }

  showDetailModal(): void {
    const modal = document.getElementById('jobOrderDetailModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).show();
  }

  hideDetailModal(): void {
    const modal = document.getElementById('jobOrderDetailModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).hide();
  }

  showDeleteModal(): void {
    const modal = document.getElementById('deleteJobOrderModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).show();
  }

  hideDeleteModal(): void {
    const modal = document.getElementById('deleteJobOrderModal');
    if (modal) window.bootstrap.Modal.getOrCreateInstance(modal).hide();
  }

  loadOrders(): void {
    this.loading = true;

    this.jobOrderSvc.getAll(
      this.searchText,
      this.activeFilter,
      this.page,
      this.pageSize,
      {
        fromDate: this.filterFromDate || null,
        toDate: this.filterToDate || null,
        customerId: this.filterCustomerId,
        branchId: this.filterBranchId,
        posted: this.filterPosted || null,
        orderType: this.filterOrderType || null,
        priority: this.filterPriority || null,
        status: this.filterStatus || null
      }
    ).subscribe({
      next: (res: any) => {
        const payload = res?.data;

        this.orders = payload?.items ?? [];
        this.totalItems = payload?.totalItems ?? 0;
        this.page = payload?.page ?? this.page;
        this.pageSize = payload?.pageSize ?? this.pageSize;
        this.totalPages = payload?.totalPages ?? 1;

        const sm = res?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            totalQty: Number(sm.totalQty ?? 0),
            postedCount: Number(sm.postedCount ?? 0),
            draftCount: Number(sm.draftCount ?? 0),
            customerOrderCount: Number(sm.customerOrderCount ?? 0),
            stockOrderCount: Number(sm.stockOrderCount ?? 0),
            byStatus: Array.isArray(sm.byStatus) ? sm.byStatus : [],
            byPriority: Array.isArray(sm.byPriority) ? sm.byPriority : []
          };
        }

        this.setupPagination();
        this.loading = false;
      },
      error: err => {
        console.error('JOB ORDER LOAD FAILED:', err);
        this.orders = [];
        this.totalItems = 0;
        this.totalPages = 1;
        this.setupPagination();
        this.loading = false;
      }
    });
  }

  // Phase M.0 - filter helpers
  onFiltersChange(): void {
    this.page = 1;
    this.clearSelection();  // Sprint-3: paging out resets the visible-page selection model.
    this.loadOrders();
  }
  clearFilters(): void {
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterCustomerId = null;
    this.filterBranchId = null;
    this.filterPosted = '';
    this.filterOrderType = '';
    this.filterPriority = '';
    this.filterStatus = '';
    this.searchText = '';
    this.activeFilter = '';
    this.page = 1;
    this.clearSelection();
    this.loadOrders();
  }
  /** Sprint-WO-polish parity: merge IsPosted + Active + Status into a
   *  single chip so the JO list doesn't show three near-identical badges
   *  ("Posted / Active / Posted"). Inactive wins (visibility), then
   *  Draft (unposted), then Status-driven for posted rows. */
  jobWorkflowChip(o: any): { label: string; cssClass: string; icon: string } {
    if (o?.deleted) return { label: 'Deleted',  cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-trash3-fill' };
    if (o && o.active === false)
                    return { label: 'Inactive', cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pause-circle' };
    if (!o?.isPosted)
                    return { label: 'Draft',    cssClass: 'bg-light text-secondary border', icon: 'bi-pencil-square' };

    const raw = (o?.status ?? '').toString().trim();
    const u = raw.toUpperCase();
    if (u === 'CANCELLED' || u === 'CANCELED' || u === 'REJECTED')
      return { label: raw, cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    if (u === 'COMPLETED' || u === 'DONE' || u.includes('COMPLETE'))
      return { label: raw, cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'CLOSED')
      return { label: raw, cssClass: 'bg-success text-white border border-success', icon: 'bi-archive-fill' };
    if (u.includes('PRODUCTION') || u.includes('PROGRESS') || u === 'WIP' || u === 'IN_PROGRESS')
      return { label: raw, cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    // Posted row with no recognised forward-progress status — show
    // a single "Posted" chip rather than "Posted / Posted" duplicated.
    return { label: 'Posted', cssClass: 'bg-primary-subtle text-primary-emphasis border border-primary', icon: 'bi-check2-circle' };
  }

  jobStatusPill(s: string): { cssClass: string; icon: string } {
    const u = (s ?? '').toString().trim().toUpperCase();
    if (!u || u === 'PENDING' || u === 'OPEN')
      return { cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
    if (u.includes('PROGRESS') || u === 'WIP' || u === 'IN_PROGRESS')
      return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    if (u === 'COMPLETED' || u === 'DONE' || u.includes('COMPLETE'))
      return { cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'CANCELLED' || u === 'CANCELED' || u === 'REJECTED')
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }
  priorityPill(p: string): { cssClass: string; icon: string } {
    const u = (p ?? '').toString().trim().toUpperCase();
    if (u === 'HIGH' || u === 'URGENT')
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-exclamation-triangle-fill' };
    if (u === 'MEDIUM' || u === 'NORMAL')
      return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-flag-fill' };
    if (u === 'LOW')
      return { cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-flag' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-flag' };
  }

  // Sprint-1: classify a due date as Overdue / Due Soon / OK and return a
  // Bootstrap badge spec. Days are calendar-day truncated to local midnight
  // on both sides so a 23:59 timestamp doesn't roll over a day boundary.
  dueBadge(dueDate: string | Date | null | undefined):
    { cssClass: string; icon: string; label: string; days: number } | null {
    if (!dueDate) return null;
    const due = new Date(dueDate);
    if (isNaN(due.getTime())) return null;

    const startOfToday = new Date();
    startOfToday.setHours(0, 0, 0, 0);

    const dueMidnight = new Date(due);
    dueMidnight.setHours(0, 0, 0, 0);

    const days = Math.round((dueMidnight.getTime() - startOfToday.getTime()) / 86400000);

    if (days < 0)
      return { cssClass: 'bg-danger text-white', icon: 'bi-exclamation-triangle-fill',
               label: 'Overdue ' + Math.abs(days) + 'd', days };
    if (days === 0)
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger',
               icon: 'bi-alarm-fill', label: 'Due today', days };
    if (days <= 3)
      return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning',
               icon: 'bi-alarm', label: 'Due in ' + days + 'd', days };
    return { cssClass: 'bg-light text-muted border', icon: 'bi-calendar3',
             label: 'In ' + days + 'd', days };
  }

  // Sprint-1: progress-bar colour band based on completion percent.
  progressBarClass(percent: number | null | undefined): string {
    const p = Number(percent ?? 0);
    if (p <= 0)   return 'bg-secondary';
    if (p >= 100) return 'bg-success';
    if (p >= 75)  return 'bg-info';
    return 'bg-warning';
  }

  // Phase M.1 - Profile drawer
  openProfile(id: number): void {
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.auditLog = [];
    this.jobOrderSvc.getProfile(id).subscribe({
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
        console.error('Profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });

    // Sprint-4: fetch audit timeline in parallel with the profile so both
    // panels appear at roughly the same time. Audit failure is silent —
    // the timeline panel just renders empty.
    this.auditLoading = true;
    this.jobOrderSvc.getAuditLog(id).subscribe({
      next: (res: any) => {
        this.auditLoading = false;
        this.auditLog = Array.isArray(res?.data) ? res.data : [];
      },
      error: () => {
        this.auditLoading = false;
        this.auditLog = [];
      }
    });
  }
  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
    this.auditLog = [];
  }

  // ─────────────────────────────────────────────────────────────────────
  //  SPRINT-4  WORKFLOW STAGE STEPPER
  //  Renders the canonical stages (Draft → Posted → InProduction → QC →
  //  Dispatched → Closed) as a horizontal pipeline. Each step is "completed"
  //  if its index ≤ the current index, with the current step highlighted.
  // ─────────────────────────────────────────────────────────────────────
  stageDisplayName(stage: string): string {
    switch ((stage ?? '').trim()) {
      case 'Draft':         return 'Draft';
      case 'Posted':        return 'Posted';
      case 'InProduction':  return 'In Production';
      case 'QC':            return 'QC';
      case 'Dispatched':    return 'Dispatched';
      case 'Closed':        return 'Closed';
      default:              return stage || '—';
    }
  }

  stageIcon(stage: string): string {
    switch ((stage ?? '').trim()) {
      case 'Draft':         return 'bi-pencil';
      case 'Posted':        return 'bi-check-circle';
      case 'InProduction':  return 'bi-gear';
      case 'QC':            return 'bi-clipboard-check';
      case 'Dispatched':    return 'bi-truck';
      case 'Closed':        return 'bi-archive';
      default:              return 'bi-record-circle';
    }
  }

  // Returns the index of the JO's current stage in the canonical list.
  // -1 if the stage is missing or unrecognised — UI falls back to "all grey".
  currentStageIndex(): number {
    const stages: string[] = this.profile?.workflowStages ?? [];
    const stage = this.profile?.workflowStage ?? '';
    return stages.indexOf(stage);
  }

  // Mark-as-Closed is only enabled at the Dispatched stage. Closed is a
  // terminal stage; once set there's no in-app reopen (operator can
  // reverse-post + edit if the order really needs to come back to life).
  canMarkClosed(): boolean {
    return (this.profile?.workflowStage ?? '') === 'Dispatched';
  }

  markClosed(): void {
    const id = Number(this.profile?.header?.id ?? 0);
    if (!id) return;
    if (!confirm('Mark this job order as Closed? This is a terminal stage.')) return;
    this.jobOrderSvc.markClosed(id).subscribe({
      next: (res: any) => {
        if (!res?.success) {
          alert(res?.message || 'Failed to close.');
          return;
        }
        // Reload the profile so the stepper redraws and the audit timeline
        // picks up the new "Closed" entry.
        this.openProfile(id);
        this.loadOrders();
      },
      error: (err: any) => alert(err?.error?.message || err?.message || 'Failed to close.')
    });
  }

  // Sprint-4: action → Bootstrap colour band + icon for the timeline marker.
  auditPill(action: string): { cssClass: string; icon: string } {
    const a = (action ?? '').toString().trim().toLowerCase();
    if (a === 'created' || a === 'cloned')
      return { cssClass: 'bg-info text-white',    icon: 'bi-plus-circle-fill' };
    if (a === 'updated' || a === 'componentssynced')
      return { cssClass: 'bg-warning text-dark',  icon: 'bi-pencil-fill' };
    if (a === 'posted')
      return { cssClass: 'bg-success text-white', icon: 'bi-check-circle-fill' };
    if (a === 'reversed')
      return { cssClass: 'bg-secondary text-white', icon: 'bi-arrow-counterclockwise' };
    if (a === 'deleted')
      return { cssClass: 'bg-danger text-white', icon: 'bi-trash-fill' };
    if (a === 'prioritychanged')
      return { cssClass: 'bg-warning text-dark', icon: 'bi-flag-fill' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-record-circle' };
  }

  loadDropdowns(): void {
    this.jobOrderSvc.getCustomers().subscribe({
      next: res => {
        this.customers = Array.isArray(res)
          ? res.map(x => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            customerCode: x?.customerCode ?? x?.CustomerCode ?? '',
            customerName: x?.customerName ?? x?.CustomerName ?? x?.name ?? x?.Name ?? '',
            text: x?.text ?? x?.Text ?? x?.customerName ?? x?.CustomerName ?? x?.name ?? x?.Name ?? ''
          })).filter(x => x.id > 0)
          : [];
      },
      error: err => {
        console.error('CUSTOMERS LOAD FAILED:', err);
        this.customers = [];
      }
    });

    this.jobOrderSvc.getBranches().subscribe({
      next: res => {
        this.branches = Array.isArray(res)
          ? res.map(x => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            name: x?.name ?? x?.Name ?? x?.branchName ?? x?.BranchName ?? '',
            text: x?.text ?? x?.Text ?? x?.name ?? x?.Name ?? x?.branchName ?? x?.BranchName ?? ''
          })).filter(x => x.id > 0)
          : [];
      },
      error: err => {
        console.error('BRANCHES LOAD FAILED:', err);
        this.branches = [];
      }
    });

    this.jobOrderSvc.getProducts().subscribe({
      next: res => {
        this.products = Array.isArray(res)
          ? res.map(x => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            productCode: x?.productCode ?? x?.ProductCode ?? x?.prodCode ?? x?.ProdCode ?? x?.code ?? x?.Code ?? '',
            productName: x?.productName ?? x?.ProductName ?? x?.prodName ?? x?.ProdName ?? x?.name ?? x?.Name ?? '',
            text:
              x?.text ??
              x?.Text ??
              x?.productName ??
              x?.ProductName ??
              x?.prodName ??
              x?.ProdName ??
              x?.name ??
              x?.Name ??
              ''
          })).filter(x => x.id > 0)
          : [];
      },
      error: err => {
        console.error('PRODUCTS LOAD FAILED:', err);
        this.products = [];
      }
    });
  }

  loadDesigns(): void {
    const productId = Number(this.form.get('productId')?.value || 0);

    this.designs = [];
    this.componentPreviewRows = [];
    this.selectedProductLabel = '—';

    this.form.patchValue({ designId: null }, { emitEvent: true });

    if (!productId) return;

    this.jobOrderSvc.getDesignsByProduct(productId).subscribe({
      next: res => {
        this.designs = Array.isArray(res)
          ? res.map(x => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            productId: Number(x?.productId ?? x?.ProductId ?? productId),
            code: x?.code ?? x?.Code ?? x?.designCode ?? x?.DesignCode ?? '',
            name: x?.name ?? x?.Name ?? x?.designName ?? x?.DesignName ?? x?.collectionName ?? x?.CollectionName ?? '',
            text:
              x?.text ??
              x?.Text ??
              x?.designName ??
              x?.DesignName ??
              x?.collectionName ??
              x?.CollectionName ??
              x?.name ??
              x?.Name ??
              ''
          })).filter(x => x.id > 0)
          : [];

        if (this.designs.length === 1) {
          this.form.patchValue({ designId: this.designs[0].id }, { emitEvent: true });
          this.onDesignChange();
        }
      },
      error: err => {
        console.error('DESIGNS BY PRODUCT FAILED:', err);
        this.designs = [];
      }
    });
  }

  onProductChange(): void {
    this.loadDesigns();
  }

  onDesignChange(): void {
    const designId = Number(this.form.get('designId')?.value || 0);

    if (!designId) {
      this.selectedProductLabel = '—';
      return;
    }

    this.jobOrderSvc.getDesignDetail(designId).subscribe({
      next: d => {
        const productId = Number(d?.productId ?? d?.ProductId ?? 0);

        if (productId > 0) {
          this.form.patchValue({ productId }, { emitEvent: false });
        }

        const productCode = d?.productCode ?? d?.ProductCode ?? '';
        const productName = d?.productName ?? d?.ProductName ?? '';

        this.selectedProductLabel = `${productCode} - ${productName}`.trim() || '—';

        this.loadComponentPreview();
      },
      error: err => {
        console.error('DESIGN DETAIL LOAD FAILED:', err);
        this.selectedProductLabel = '—';
      }
    });
  }

  loadComponentPreview(): void {
    const designId = Number(this.form.get('designId')?.value || 0);
    const orderQty = Number(this.form.get('orderQty')?.value || 0);

    this.componentPreviewRows = [];

    if (designId <= 0 || orderQty <= 0) return;

    this.previewLoading = true;

    this.jobOrderSvc.getComponentsPreview(designId, orderQty).subscribe({
      next: res => {
        const raw = res?.data ?? res?.Data ?? res;

        this.componentPreviewRows = Array.isArray(raw)
          ? raw.map((x: any) => ({
            code: x?.code ?? x?.Code ?? x?.componentCode ?? x?.ComponentCode ?? '',
            componentName: x?.componentName ?? x?.ComponentName ?? x?.name ?? x?.Name ?? '',
            reqRepeats: Number(x?.reqRepeats ?? x?.ReqRepeats ?? 0),
            reqHead: Number(x?.reqHead ?? x?.ReqHead ?? 0),
            panni: Number(x?.panni ?? x?.Panni ?? 0),
            solving: Number(x?.solving ?? x?.Solving ?? 0)
          }))
          : [];

        this.previewLoading = false;
      },
      error: err => {
        console.error('COMPONENT PREVIEW LOAD FAILED:', err);
        this.componentPreviewRows = [];
        this.previewLoading = false;
      }
    });
  }

  onOrderTypeChange(): void {
    const orderType = this.form.get('orderType')?.value as JobOrderType;
    const isCompanyStock = orderType === 'CompanyStock';

    this.form.patchValue({
      isCompanyStockOrder: isCompanyStock,
      customerId: isCompanyStock ? null : this.form.get('customerId')?.value
    }, { emitEvent: false });

    const customerCtrl = this.form.get('customerId');

    if (isCompanyStock) {
      customerCtrl?.clearValidators();
    } else {
      customerCtrl?.setValidators([Validators.required]);
    }

    customerCtrl?.updateValueAndValidity();
  }

  openCreate(): void {
    this.cleanupBootstrapModal();

    this.modalMode = 'create';
    this.editingId = null;
    this.selectedProductLabel = '—';
    this.componentPreviewRows = [];
    this.designs = [];

    this.form.reset({
      id: 0,
      jobOrderCode: '',
      orderType: 'Customer',
      isCompanyStockOrder: false,
      customerId: null,
      productId: null,
      designId: null,
      branchId: null,
      orderQty: 0,
      orderDate: null,
      dueDate: null,
      priority: 'Normal',
      status: 'Draft',
      active: true
    });

    this.onOrderTypeChange();

    setTimeout(() => this.showModal(), 50);
  }

  openEdit(id: number): void {
    this.cleanupBootstrapModal();

    this.modalMode = 'edit';
    this.editingId = id;
    this.selectedProductLabel = '—';
    this.componentPreviewRows = [];

    this.jobOrderSvc.getDetail(id).subscribe({
      next: res => {
        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load job order.');
          return;
        }

        const d = res.data;

        this.form.patchValue({
          id: d.id,
          jobOrderCode: d.jobOrderCode,

          orderType: d.orderType,
          isCompanyStockOrder: d.isCompanyStockOrder,

          customerId: d.customerId,
          productId: d.productId,
          designId: d.designId,
          branchId: d.branchId,

          orderQty: d.orderQty,
          orderDate: d.orderDate,
          dueDate: d.dueDate,

          priority: d.priority ?? 'Normal',
          status: d.status ?? 'Draft',
          active: d.active === true
        }, { emitEvent: false });

        this.onOrderTypeChange();

        const productId = Number(d.productId || 0);

        if (productId > 0) {
          this.jobOrderSvc.getDesignsByProduct(productId).subscribe({
            next: res2 => {
              this.designs = Array.isArray(res2)
                ? res2.map((x: any) => ({
                  id: Number(x?.id ?? x?.Id ?? 0),
                  productId: Number(x?.productId ?? x?.ProductId ?? productId),
                  code: x?.code ?? x?.Code ?? x?.designCode ?? x?.DesignCode ?? '',
                  name: x?.name ?? x?.Name ?? x?.designName ?? x?.DesignName ?? x?.collectionName ?? x?.CollectionName ?? '',
                  text:
                    x?.text ??
                    x?.Text ??
                    x?.designName ??
                    x?.DesignName ??
                    x?.collectionName ??
                    x?.CollectionName ??
                    x?.name ??
                    x?.Name ??
                    ''
                })).filter((x: any) => x.id > 0)
                : [];

              this.form.patchValue({ designId: d.designId }, { emitEvent: true });
              this.onDesignChange();
            },
            error: () => {
              this.designs = [];
              this.loadComponentPreview();
            }
          });
        }

        this.showModal();
      },
      error: err => {
        console.error('EDIT LOAD FAILED:', err);
        alert('Failed to load job order.');
      }
    });
  }

  save(): void {
    this.onOrderTypeChange();

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const orderType: JobOrderType =
      this.form.value.orderType === 'CompanyStock' ? 'CompanyStock' : 'Customer';

    const isCompanyStock = orderType === 'CompanyStock';

    const dto: JobOrderDto = {
      id: Number(this.form.value.id || 0),
      jobOrderCode: this.form.value.jobOrderCode || null,

      orderType,
      isCompanyStockOrder: isCompanyStock,

      customerId: isCompanyStock ? null : Number(this.form.value.customerId),

      productId: Number(this.form.value.productId),
      designId: Number(this.form.value.designId),
      branchId: Number(this.form.value.branchId),

      orderQty: Number(this.form.value.orderQty),

      orderDate: this.form.value.orderDate || null,
      dueDate: this.form.value.dueDate || null,

      priority: this.form.value.priority || 'Normal',
      status: this.form.value.status || 'Draft',
      active: this.form.value.active === true
    };

    const request$ = this.modalMode === 'create'
      ? this.jobOrderSvc.create(dto)
      : this.jobOrderSvc.update(dto);

    request$.subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? true;
        alert(res?.message ?? res?.Message ?? (ok ? 'Saved successfully.' : 'Save failed.'));

        if (ok) {
          this.closeModal();
          this.loadOrders();
        }
      },
      error: err => {
        console.error('SAVE FAILED:', err);
        alert(err?.error?.message ?? err?.error?.Message ?? 'Save failed.');
      }
    });
  }

  openDetail(id: number): void {
    this.jobOrderSvc.getDetail(id).subscribe({
      next: res => {
        if (!res?.success || !res?.data) {
          alert(res?.message || 'Failed to load details.');
          return;
        }

        this.selectedOrder = res.data;
        this.showDetailModal();
      },
      error: err => {
        console.error('DETAIL LOAD ERROR:', err);
        alert('Failed to load details.');
      }
    });
  }

  openDelete(id: number): void {
    this.deleteId = id;
    this.showDeleteModal();
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.jobOrderSvc.delete(this.deleteId).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? true;
        alert(res?.message ?? res?.Message ?? (ok ? 'Deleted successfully.' : 'Delete failed.'));

        if (ok) {
          this.hideDeleteModal();
          this.loadOrders();
        }
      },
      error: err => {
        console.error('DELETE FAILED:', err);
        alert(err?.error?.message ?? err?.error?.Message ?? 'Delete failed.');
      }
    });
  }

  postOrder(id: number): void {
    if (!confirm('Post this Job Order? After posting, it cannot be edited.')) return;

    this.jobOrderSvc.post(id).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;
        alert(res?.message ?? res?.Message ?? (ok ? 'Posted successfully.' : 'Post failed.'));
        if (ok) this.loadOrders();
      },
      error: err => {
        console.error('POST FAILED:', err);
        alert(err?.error?.message ?? err?.error?.Message ?? 'Post failed.');
      }
    });
  }

  reversePostOrder(id: number): void {
    if (!confirm('Reverse post this Job Order?')) return;

    this.jobOrderSvc.reversePost(id).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;
        alert(res?.message ?? res?.Message ?? (ok ? 'Reverse posted successfully.' : 'Reverse post failed.'));
        if (ok) this.loadOrders();
      },
      error: err => {
        console.error('REVERSE POST FAILED:', err);
        alert(err?.error?.message ?? err?.error?.Message ?? 'Reverse post failed.');
      }
    });
  }

  syncComponents(id: number): void {
    if (!confirm('Sync components for this Job Order?')) return;

    this.jobOrderSvc.syncComponents(id).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;
        alert(res?.message ?? res?.Message ?? (ok ? 'Components synced.' : 'Sync failed.'));
        if (ok) this.loadOrders();
      },
      error: err => {
        console.error('SYNC FAILED:', err);
        alert(err?.error?.message ?? err?.error?.Message ?? 'Sync failed.');
      }
    });
  }

  setupPagination(): void {
    this.totalPages = Math.ceil((this.totalItems || 0) / Math.max(this.pageSize, 1)) || 1;
    this.pageArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  changePage(p: number): void {
    if (p < 1 || p > this.totalPages) return;
    this.page = p;
    this.loadOrders();
  }

  onSearch(): void {
    this.page = 1;
    this.loadOrders();
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadOrders();
  }

  refresh(): void {
    this.page = 1;
    this.loadOrders();
  }

  getCustomerName(customerId: number | null | undefined): string {
    if (!customerId) return '—';

    const found = this.customers.find(x => Number(x.id ?? x.Id ?? 0) === Number(customerId));

    return found?.customerName ?? found?.CustomerName ?? found?.name ?? found?.Name ?? '—';
  }

  getDesignName(designId: number | null | undefined): string {
    if (!designId) return '—';

    const found = this.designs.find(x => Number(x.id ?? x.Id ?? 0) === Number(designId));

    return found?.text ?? found?.name ?? found?.designName ?? found?.DesignName ?? '—';
  }

  getBranchName(branchId: number | null | undefined): string {
    if (!branchId) return '—';

    const found = this.branches.find(x => Number(x.id ?? x.Id ?? 0) === Number(branchId));

    return found?.text ?? found?.name ?? found?.branchName ?? found?.BranchName ?? '—';
  }

  getFilteredDesigns(): any[] {
    const productId = Number(this.form.get('productId')?.value || 0);

    if (!productId) return this.designs;

    const hasProductId = this.designs.some(x => Number(x.productId ?? x.ProductId ?? 0) > 0);

    if (!hasProductId) return this.designs;

    return this.designs.filter(x => Number(x.productId ?? x.ProductId ?? 0) === productId);
  }

  formatDate(value: any): string {
    if (!value) return '';

    try {
      if (typeof value === 'string' && value.includes('/Date(')) {
        const timestamp = parseInt(value.replace('/Date(', '').replace(')/', ''), 10);
        return new Date(timestamp).toLocaleDateString();
      }

      return new Date(value).toLocaleDateString();
    } catch {
      return '';
    }
  }

  convertJsonDate(value: any): string {
    return this.formatDate(value);
  }

  exportToExcel(id?: number): void {
    this.jobOrderSvc.exportToExcel(id, this.searchText, this.activeFilter);
  }

  exportToPDF(id?: number): void {
    this.jobOrderSvc.exportToPdf(id, this.searchText, this.activeFilter);
  }

  printTable(): void {
    window.print();
  }
}
