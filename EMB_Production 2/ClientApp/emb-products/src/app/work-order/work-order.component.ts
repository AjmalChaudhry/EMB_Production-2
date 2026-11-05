import { Component, OnInit, ViewChild, ElementRef, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule, NgClass, DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink, Router, ActivatedRoute } from '@angular/router';
import {WorkOrderRowViewModel,WorkOrderDetailViewModel,WorkOrderDto,WorkOrderFormModel} from '../shared/models/work-order.dto';
import { WorkOrderService } from '../services/work-order.service';
import { ToastService } from '../shared/services/toast.service';
import { ConfirmService } from '../shared/services/confirm.service';

declare var bootstrap: any;

@Component({
  selector: 'app-work-orders',
  standalone: true,
  imports: [CommonModule, FormsModule, NgClass, DatePipe, RouterLink],
  templateUrl: './work-order.component.html',
  styleUrls: ['./work-order.component.css']
})
export class WorkOrderComponent implements OnInit, OnDestroy {
  
  workOrders: WorkOrderRowViewModel[] = [];
  items: WorkOrderRowViewModel[] = [];

  searchText = '';
  statusFilter = '';
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];
  isLoading = false;
  isProductLocked = false;

  // Phase L.0 filters
  filterFromDate = '';
  filterToDate = '';
  filterCustomerId: number | null = null;
  filterBranchId: number | null = null;
  filterPosted = '';        // '' | 'POSTED' | 'DRAFT'
  filterOrderType = '';     // '' | 'CUSTOMER' | 'STOCK'
  filterWorkOrderStatus = '';
  // Sprint-2 deep-link from Job Orders → Related → WO. When set via the
  // URL's ?jobOrderId=<id> query param, only WOs belonging to that JO are
  // returned. Clearing the search also clears this so the user can revert
  // to the full list with one click.
  filterJobOrderId: number | null = null;

  summary = {
    count: 0,
    totalQty: 0,
    postedCount: 0,
    draftCount: 0,
    customerOrderCount: 0,
    stockOrderCount: 0,
    byStatus: [] as Array<{ status: string; count: number }>
  };
 
  modalMode: 'create' | 'edit' = 'create';
  formWorkOrder: WorkOrderFormModel = this.createEmptyForm();
  
  selectedWorkOrder: WorkOrderDetailViewModel | null = null;

  // Phase L.1 - Work Order 360 Profile
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-2  AUDIT TIMELINE + WORKFLOW STEPPER STATE
  //  Loaded in parallel with the Profile so the cards are populated
  //  by the time the user scrolls down.
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLoading = false;
  workflowStages: string[] = ['DRAFT', 'POSTED', 'IN_PRODUCTION', 'QC', 'DISPATCHED', 'CLOSED'];
  workflowStageIndex = 0;

  // ──────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK SELECTION + CLONE STATE
  //  Multi-select using a Set<number> of selected WO ids. Selection
  //  persists within the page but resets on filter / page change so
  //  the operator's mental model is "bulk operates on what I see".
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  /* ================================
     DROPDOWNS
  ================================ */
  customers: any[] = [];
  products: any[] = [];
  jobOrders: any[] = [];
  branches: any[] = [];
  designs: any[] = [];
  
  /* ================================
     MODALS
  ================================ */
  @ViewChild('workOrderFormModal') workOrderFormModal!: ElementRef;
  @ViewChild('workOrderDetailModal') workOrderDetailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  deleteTarget: WorkOrderRowViewModel | null = null;

  constructor(
    private workOrderSvc: WorkOrderService,
    private http: HttpClient,
    private toast: ToastService,
    private confirmSvc: ConfirmService,
    private router: Router,
    private route: ActivatedRoute
  ) { }

  ngOnInit(): void {
    this.cleanupBootstrapModal();

    // Sprint-2: honor ?search= and ?jobOrderId= from the URL so quick-jump
    // links from the Job Orders list (e.g. "/work-orders?jobOrderId=42")
    // land here pre-filtered to that JO's Work Orders. The search box
    // shows the JO code (if passed) as a confirmation breadcrumb.
    const incomingSearch = this.route.snapshot.queryParamMap.get('search');
    if (incomingSearch) this.searchText = incomingSearch.trim();
    const incomingJobOrderId = this.route.snapshot.queryParamMap.get('jobOrderId');
    if (incomingJobOrderId) {
      const n = Number(incomingJobOrderId);
      if (n > 0) this.filterJobOrderId = n;
    }

    // Capture current operator's role for the Bypass-Plan permission
    // check. The auth flow stores the logged-in user in localStorage
    // (key: auth_user) per the app's auth service convention.
    try {
      const raw = localStorage.getItem('auth_user');
      if (raw) {
        const user = JSON.parse(raw);
        // Backend may serialize the role under either casing depending
        // on which controller endpoint produced the snapshot.
        this.currentRoleName =
          user?.roleName ?? user?.RoleName
       ?? user?.role     ?? user?.Role
       ?? '';
      }
    } catch { /* localStorage parse failure → no bypass UI, safe default */ }

    this.loadDropdowns();
    this.getWorkOrders();
  }

  ngOnDestroy(): void {
    this.cleanupBootstrapModal();
  }

  private cleanupBootstrapModal(): void {
    document.body.classList.remove('modal-open');
    document.body.style.removeProperty('overflow');
    document.body.style.removeProperty('padding-right');

    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());

    document.querySelectorAll('.modal.show').forEach((modal: Element) => {
      const el = modal as HTMLElement;

      el.classList.remove('show');
      el.style.display = 'none';
      el.setAttribute('aria-hidden', 'true');
      el.removeAttribute('aria-modal');
    });
  }

  createEmptyForm(): WorkOrderFormModel {
    return {
      id: 0,
      workOrderCode: '',

      orderType: 'Customer',
      isCompanyStockOrder: false,

      jobOrderId: null,
      jobOrderCode: '',

      branchId: null,
      designId: null,
      orderQty: 0,

      customerId: null,
      customerLabel: '',

      productId: null,
      productLabel: '',

      orderDate: '',
      dueDate: '',

      referenceNo: '',
      remarks: '',
      status: 'Draft',
      active: true,

      isPosted: false,

      // Production Plan gate override. Default false — the backend
      // will look for an APPROVED plan that covers the JO. Authorized
      // roles (Admin/Manager) can flip this in the UI and enter a
      // reason to create a WO outside any plan (rework, sample, R&D).
      bypassPlan: false,
      bypassReason: ''
    };
  }

  /** True when the current operator's role is allowed to bypass the
   *  Production Plan gate. Read from the AuthService snapshot — Admin
   *  and Manager only. Production Manager intentionally cannot bypass
   *  (they're expected to schedule through the plan). Recomputed every
   *  time the modal opens so a role change mid-session is honored. */
  get canBypassPlan(): boolean {
    const roleName = (this.currentRoleName || '').toLowerCase().trim();
    return roleName === 'administrator' || roleName === 'manager';
  }

  /** Cached role name from auth — populated in ngOnInit. */
  private currentRoleName = '';

  /* ================================
     MAIN LOAD
  ================================ */
  getWorkOrders(): void {
    this.isLoading = true;
    // Sprint-3: any list reload — filter change, search, page change —
    // resets the selection set. The operator never bulk-acts on rows
    // they can't see.
    this.selectedIds.clear();

    this.workOrderSvc.getAll(
      this.searchText,
      this.statusFilter,
      this.page,
      this.pageSize,
      {
        fromDate: this.filterFromDate || null,
        toDate: this.filterToDate || null,
        customerId: this.filterCustomerId,
        branchId: this.filterBranchId,
        posted: this.filterPosted || null,
        orderType: this.filterOrderType || null,
        workOrderStatus: this.filterWorkOrderStatus || null,
        jobOrderId: this.filterJobOrderId
      }
    ).subscribe({
      next: (res: any) => {
        const raw = res?.data ?? res?.Data ?? {};

        const list =
          Array.isArray(raw?.items) ? raw.items :
            Array.isArray(raw?.Items) ? raw.Items :
              Array.isArray(raw) ? raw : [];

        const total =
          typeof raw?.totalItems === 'number' ? raw.totalItems :
            typeof raw?.TotalItems === 'number' ? raw.TotalItems :
              typeof raw?.total === 'number' ? raw.total :
                typeof raw?.Total === 'number' ? raw.Total :
                  list.length;

        const mapped = list.map((x: any) => this.mapWorkOrderRow(x));

        this.workOrders = mapped;
        this.items = mapped;
        this.totalItems = total;

        // Overlay Extra Material Request roll-up onto each row so the user
        // sees, per WO, how much extra material was issued beyond the BOM.
        this.fetchEmrRollup(mapped.map((r: any) => r.id));

        // Phase L.0 summary block
        const sm = res?.summary ?? raw?.summary ?? raw?.Summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? sm.Count ?? 0),
            totalQty: Number(sm.totalQty ?? sm.TotalQty ?? 0),
            postedCount: Number(sm.postedCount ?? sm.PostedCount ?? 0),
            draftCount: Number(sm.draftCount ?? sm.DraftCount ?? 0),
            customerOrderCount: Number(sm.customerOrderCount ?? sm.CustomerOrderCount ?? 0),
            stockOrderCount: Number(sm.stockOrderCount ?? sm.StockOrderCount ?? 0),
            byStatus: Array.isArray(sm.byStatus ?? sm.ByStatus) ? (sm.byStatus ?? sm.ByStatus) : []
          };
        }

        this.setupPagination();
      },
      error: err => {
        console.error('Load failed:', err);
        this.workOrders = [];
        this.items = [];
        this.totalItems = 0;
        this.setupPagination();
      },
      complete: () => {
        this.isLoading = false;
      }
    });
  }

  /* ================================
     FILTER / SEARCH / PAGINATION
  ================================ */
  loadData(): void {
    this.page = 1;
    this.getWorkOrders();
  }

  onSearch(): void {
    this.page = 1;
    this.getWorkOrders();
  }

  onFilterChange(): void {
    this.page = 1;
    this.getWorkOrders();
  }

  // Phase L.0 helpers
  onFiltersChange(): void {
    this.page = 1;
    this.getWorkOrders();
  }

  clearFilters(): void {
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterCustomerId = null;
    this.filterBranchId = null;
    this.filterPosted = '';
    this.filterOrderType = '';
    this.filterWorkOrderStatus = '';
    this.filterJobOrderId = null;   // Sprint-2 deep-link filter
    this.searchText = '';
    this.statusFilter = '';
    this.page = 1;
    this.getWorkOrders();
  }

  statusOf(row: any): { label: string; cssClass: string; icon: string } {
    const raw = (row?.status ?? '').toString().trim();
    const s = raw.toUpperCase();
    if (s === 'PENDING' || s === 'OPEN' || s === '')
      return { label: raw || 'Pending', cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
    // Sprint-3 polish: DB stores "In Production" — match PRODUCTION
    // and PROGRESS so either flavour of in-flight maps to the same chip.
    if (s.includes('PRODUCTION') || s.includes('PROGRESS') || s === 'WIP' || s === 'IN_PROGRESS')
      return { label: raw, cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    if (s === 'COMPLETED' || s === 'DONE' || s === 'CLOSED')
      return { label: raw, cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (s === 'CANCELLED' || s === 'CANCELED')
      return { label: raw, cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    return { label: raw, cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }

  // ────────────────────────────────────────────────────────────────────
  //  SPRINT-1  ROW HELPERS — single workflow chip, progress, due badge,
  //  related-doc deep-link.
  // ────────────────────────────────────────────────────────────────────

  /** Single workflow chip merging IsPosted + Status. Removes the
   *  dual-badge confusion ([Posted] + [In Production]) the screenshot
   *  showed. Draft / Posted / In Production / Completed / Closed /
   *  Cancelled are mapped to one chip each. */
  workflowChip(row: any): { label: string; cssClass: string; icon: string } {
    if (!row?.isPosted) {
      return { label: 'Draft', cssClass: 'bg-light text-secondary border', icon: 'bi-pencil-square' };
    }
    const raw = (row?.status ?? '').toString().trim();
    const s = raw.toUpperCase();
    if (s === 'CANCELLED' || s === 'CANCELED')
      return { label: 'Cancelled', cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    if (s === 'COMPLETED' || s === 'DONE')
      return { label: 'Completed', cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (s === 'CLOSED')
      return { label: 'Closed', cssClass: 'bg-success text-white border border-success', icon: 'bi-archive-fill' };
    // Sprint-3 polish: DB stores "In Production" — match PRODUCTION
    // and PROGRESS so either flavour of in-flight maps to the same chip.
    if (s.includes('PRODUCTION') || s.includes('PROGRESS') || s === 'WIP' || s === 'IN_PROGRESS')
      return { label: 'In Production', cssClass: 'bg-info-subtle text-info-emphasis border border-info', icon: 'bi-arrow-repeat' };
    // Default for any posted state without a recognised Status — e.g.
    // "Posted" itself, or freshly-posted WOs with status = null.
    return { label: 'Posted', cssClass: 'bg-primary-subtle text-primary-emphasis border border-primary', icon: 'bi-check2-circle' };
  }

  /** Progress bar colour by completion threshold. */
  progressBarClass(percent: number | null | undefined): string {
    const p = Math.max(0, Math.min(100, Number(percent ?? 0)));
    if (p >= 100) return 'bg-success';
    if (p >= 75)  return 'bg-info';
    if (p >= 25)  return 'bg-warning';
    return 'bg-danger';
  }

  /** Due-date badge with overdue / due-soon / on-track gradient. Prefers
   *  the ISO `dueDateText` (Sprint-1 backend field) over the raw `dueDate`
   *  which can arrive as the legacy /Date(ms)/ format. Returns null when
   *  no due date is set so the template can hide the badge. */
  dueBadge(row: any): { label: string; cssClass: string; icon: string } | null {
    const raw = row?.dueDateText ?? row?.dueDate;
    if (!raw) return null;
    const due = new Date(raw);
    if (isNaN(due.getTime())) return null;

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    due.setHours(0, 0, 0, 0);

    const diffDays = Math.round((due.getTime() - today.getTime()) / 86_400_000);

    if (diffDays < 0)  return { label: `Overdue ${-diffDays}d`, cssClass: 'bg-danger text-white', icon: 'bi-exclamation-triangle-fill' };
    if (diffDays === 0) return { label: 'Due today',           cssClass: 'bg-warning text-dark',  icon: 'bi-exclamation-circle-fill' };
    if (diffDays <= 3) return { label: `Due in ${diffDays}d`, cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-clock-history' };
    if (diffDays <= 7) return { label: `In ${diffDays}d`,    cssClass: 'bg-info-subtle text-info-emphasis border', icon: 'bi-calendar-event' };
    return null;  // far-future dates: no badge clutter
  }

  // ───────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK SELECTION HELPERS
  // ───────────────────────────────────────────────────────────────────

  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  selectedCount(): number { return this.selectedIds.size; }

  allOnPageSelected(): boolean {
    return this.workOrders.length > 0 && this.workOrders.every((r: any) => this.selectedIds.has(r.id));
  }
  someOnPageSelected(): boolean {
    return !this.allOnPageSelected() && this.workOrders.some((r: any) => this.selectedIds.has(r.id));
  }

  toggleRow(id: number, checked: boolean): void {
    if (checked) this.selectedIds.add(id);
    else this.selectedIds.delete(id);
  }
  toggleAllOnPage(checked: boolean): void {
    if (checked) this.workOrders.forEach((r: any) => this.selectedIds.add(r.id));
    else this.workOrders.forEach((r: any) => this.selectedIds.delete(r.id));
  }
  clearSelection(): void { this.selectedIds.clear(); }

  bulkPost(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} Work Order(s)? Each one generates items from its BOM and creates an auto-Purchase Requisition for any material shortfall. Drafts only — already-posted rows will be skipped.`)) return;
    this.bulkBusy = true;
    this.workOrderSvc.bulkPost(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'posted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk post failed.'); }
    });
  }

  bulkReverse(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Reverse-Post ${ids.length} Work Order(s)? Each row that still has downstream production / QC / dispatch will be skipped with a reason — only safe rows roll back.`)) return;
    this.bulkBusy = true;
    this.workOrderSvc.bulkReverse(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'reversed'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk reverse failed.'); }
    });
  }

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} Work Order(s)? Posted rows will be skipped — reverse-post them first.`)) return;
    this.bulkBusy = true;
    this.workOrderSvc.bulkDelete(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'deleted'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk delete failed.'); }
    });
  }

  /** Common post-bulk handler: surfaces success/failure counts, lists
   *  up to 10 failed rows, reloads the list and clears the selection so
   *  the UI reflects the new state. Shared by Production Plan / Job
   *  Orders / Work Orders bulk endpoints. */
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
    this.getWorkOrders();
  }

  /** Sprint-3: clone a single WO into a new DRAFT. Reloads the list and
   *  surfaces a one-line confirmation. Refuses gracefully when the JO
   *  has no remaining qty (server returns success=false with reason). */
  /** Clone preview — open the form modal in create mode pre-filled with
   *  the source row's JO / design / product / qty. No backend call yet.
   *  The new draft only appears in the list after the buyer clicks Save
   *  (which routes through saveCreate via modalMode='create'). Closing
   *  without saving abandons the preview. */
  cloneWo(row: any): void {
    if (!row?.id) return;

    // Allocate a fresh PO code so the form has something to show. The
    // backend will overwrite it on Save if the user submits.
    this.workOrderSvc.getNextCode().subscribe({
      next: (codeRes: any) => {
        const newCode = codeRes?.data?.code ?? codeRes?.Data?.code ?? '';

        this.modalMode = 'create';
        this.formWorkOrder = {
          id: 0,
          workOrderCode: newCode,
          orderType: row?.orderType ?? 'Customer',
          isCompanyStockOrder: this.toBool(row?.isCompanyStockOrder),
          jobOrderId: Number(row?.jobOrderId ?? 0) || null,
          jobOrderCode: row?.jobOrderCode ?? '',
          branchId: Number(row?.branchId ?? 0) || null,
          designId: Number(row?.designId ?? 0) || null,
          orderQty: Number(row?.orderQty ?? 0),
          customerId: Number(row?.customerId ?? 0) || null,
          customerLabel: row?.customerName ?? '',
          productId: Number(row?.productId ?? 0) || null,
          productLabel:
            `${row?.productCode ?? ''} ${row?.productName ?? ''}`.trim(),
          orderDate: new Date().toISOString().substring(0, 10),
          dueDate: this.toDateInputValue(row?.dueDate),
          referenceNo: row?.referenceNo ?? '',
          remarks: 'Cloned from ' + (row?.workOrderCode ?? ''),
          status: 'Draft',
          active: true,
          isPosted: false
        };

        this.openModal(this.workOrderFormModal);
      },
      error: () => {
        // Fallback if next-code fails — just open the modal with empty code.
        this.modalMode = 'create';
        this.openModal(this.workOrderFormModal);
      }
    });
  }

  /** Deep-link to a related list, pre-filtered by this WO's identifiers.
   *  Mirrors the gotoRelated pattern from Job Orders + Production Plan. */
  gotoRelated(target: 'jo' | 'plan' | 'daily' | 'qc' | 'dispatch', row: any): void {
    if (!row) return;
    switch (target) {
      case 'jo':
        if (row.jobOrderCode) this.router.navigate(['/job-orders'], { queryParams: { search: row.jobOrderCode } });
        break;
      case 'plan':
        if (row.planCode) this.router.navigate(['/production-plan'], { queryParams: { search: row.planCode } });
        break;
      case 'daily':
        if (row.workOrderCode) this.router.navigate(['/daily-production'], { queryParams: { search: row.workOrderCode } });
        break;
      case 'qc':
        if (row.workOrderCode) this.router.navigate(['/qc-inspection'], { queryParams: { search: row.workOrderCode } });
        break;
      case 'dispatch':
        if (row.jobOrderCode) this.router.navigate(['/dispatch'], { queryParams: { search: row.jobOrderCode } });
        break;
    }
  }

  onPageChange(p: number): void {
    if (p > 0 && p !== this.page && p <= this.pages.length) {
      this.page = p;
      this.getWorkOrders();
    }
  }

  onJobOrderChange(): void {
    const jobOrderId = Number(this.formWorkOrder?.jobOrderId ?? 0);

    if (!jobOrderId) {
      this.formWorkOrder.branchId = null;
      this.formWorkOrder.designId = null;
      this.formWorkOrder.customerId = null;
      this.formWorkOrder.productId = null;

      this.formWorkOrder.orderType = 'Customer';
      this.formWorkOrder.isCompanyStockOrder = false;

      this.formWorkOrder.customerLabel = '';
      this.formWorkOrder.productLabel = '';

      this.formWorkOrder.orderQty = 0;
      this.formWorkOrder.orderDate = '';
      this.formWorkOrder.dueDate = null;
      return;
    }

    this.workOrderSvc.getJobOrderDetail(jobOrderId).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;
        const d = res?.data ?? res?.Data;

        if (!ok || !d) {
          alert('Failed to load job order details.');
          return;
        }

        const isCompanyStock = Boolean(
          d?.IsCompanyStockOrder ?? d?.isCompanyStockOrder ?? false
        );

        const branchId = Number(d?.BranchId ?? d?.branchId ?? 0) || null;
        const designId = Number(d?.DesignId ?? d?.designId ?? 0) || null;
        const productId = Number(d?.ProductId ?? d?.productId ?? 0) || null;

        const customerId = isCompanyStock
          ? null
          : Number(d?.CustomerId ?? d?.customerId ?? 0) || null;

        const customerCode = String(d?.CustomerCode ?? d?.customerCode ?? '').trim();
        const customerName = String(d?.CustomerName ?? d?.customerName ?? '').trim();

        const productCode = String(d?.ProductCode ?? d?.productCode ?? '').trim();
        let productName = String(d?.ProductName ?? d?.productName ?? '').trim();

        if (!productName && productId) {
          const foundProduct = this.products.find(x =>
            Number(x?.id ?? x?.Id ?? 0) === productId
          );

          productName = String(
            foundProduct?.name ??
            foundProduct?.Name ??
            foundProduct?.productName ??
            foundProduct?.ProductName ??
            ''
          ).trim();
        }

        const remainingQty = Number(
          d?.RemainingQty ??
          d?.remainingQty ??
          d?.OrderQty ??
          d?.orderQty ??
          0
        );

        this.formWorkOrder.orderType = isCompanyStock ? 'CompanyStock' : 'Customer';
        this.formWorkOrder.isCompanyStockOrder = isCompanyStock;

        this.formWorkOrder.branchId = branchId;
        this.formWorkOrder.designId = designId;
        this.formWorkOrder.customerId = customerId;
        this.formWorkOrder.productId = productId;

        this.formWorkOrder.orderQty = remainingQty;

        if (designId && !this.designs.some(x => Number(x.id) === designId)) {
          this.designs = [{
            id: designId,
            designCode: d?.DesignCode ?? d?.designCode ?? '',
            designName: d?.DesignName ?? d?.designName ?? ''
          }, ...this.designs];
        }

        this.formWorkOrder.customerLabel = isCompanyStock
          ? 'Stock'
          : customerCode && customerName
            ? `${customerCode} - ${customerName}`
            : customerName || customerCode || '—';

        this.formWorkOrder.productLabel =
          productCode && productName
            ? `${productCode} - ${productName}`
            : productName || productCode || '—';

        this.formWorkOrder.orderDate = this.toDateInputValue(
          d?.OrderDate ?? d?.orderDate ?? d?.CreatedDate ?? d?.createdDate
        ) || '';

        this.formWorkOrder.dueDate = this.toDateInputValue(
          d?.DueDate ?? d?.dueDate
        );

        this.formWorkOrder.status = 'Draft';
        this.formWorkOrder.active = true;

        console.log('JOB ORDER DETAIL:', d);
        console.log('PATCHED WORK ORDER:', this.formWorkOrder);
      },
      error: err => {
        console.error('JOB ORDER DETAIL LOAD FAILED:', err);
        alert('Failed to load job order details.');
      }
    });
  }

  setupPagination(): void {
    const totalPages = Math.ceil(this.totalItems / this.pageSize) || 1;
    this.pages = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  get endCount(): number {
    return Math.min(this.page * this.pageSize, this.totalItems);
  }

  private resetForm(): void {
    this.formWorkOrder = {
      id: 0,
      workOrderCode: '',

      orderType: 'Customer',
      isCompanyStockOrder: false,

      jobOrderId: null,
      jobOrderCode: '',

      branchId: null,
      designId: null,
      orderQty: 0,

      customerId: null,
      customerLabel: '',

      productId: null,
      productLabel: '',

      orderDate: '',
      dueDate: '',

      referenceNo: '',
      remarks: '',
      status: 'Draft',
      active: true,
      isPosted: false
    };

    this.isProductLocked = false;
  }

  /* ================================
     CREATE
  ================================ */
  openCreate(): void {
    this.modalMode = 'create';
    this.formWorkOrder = this.createEmptyForm();
  
    this.workOrderSvc.getNextCode().subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;

        if (ok) {
          this.formWorkOrder.workOrderCode =
            res?.data?.code ??
            res?.Data?.code ??
            '';
        }

        this.openModal(this.workOrderFormModal);
      },
      error: (err) => {
        console.error('Failed to get next work order code:', err);
        this.openModal(this.workOrderFormModal);
      }
    });
  }

  saveCreate(): void {
    if (!this.formWorkOrder.jobOrderId) {
      alert('Job Order is required.');
      return;
    }

    if (!this.formWorkOrder.branchId) {
      alert('Branch is required.');
      return;
    }

    if (!this.formWorkOrder.designId) {
      alert('Design is required.');
      return;
    }

    if (!this.formWorkOrder.orderQty || this.formWorkOrder.orderQty <= 0) {
      alert('Order quantity must be greater than zero.');
      return;
    }

    if (!this.formWorkOrder.orderDate) {
      alert('Order Date is required.');
      return;
    }

    // Bypass-plan UI safety guard. The backend re-validates both the
    // role and the reason, but block obvious user errors here too.
    const wantsBypass = !!this.formWorkOrder.bypassPlan;
    const bypassReason = (this.formWorkOrder.bypassReason || '').trim();
    if (wantsBypass && !bypassReason) {
      alert('Please enter a reason for bypassing the Production Plan.');
      return;
    }

    const payload: WorkOrderDto = {
      id: 0,
      workOrderCode: (this.formWorkOrder.workOrderCode || '').trim(),
      orderType: this.formWorkOrder.isCompanyStockOrder ? 'CompanyStock' : 'Customer',
      isCompanyStockOrder: this.formWorkOrder.isCompanyStockOrder,
      jobOrderId: this.formWorkOrder.jobOrderId ?? null,
      branchId: this.formWorkOrder.branchId ?? null,
      designId: this.formWorkOrder.designId ?? null,
      orderQty: this.formWorkOrder.orderQty ?? 0,
      customerId: this.formWorkOrder.customerId ?? null,
      productId: this.formWorkOrder.productId ?? null,
      orderDate: this.safeDate(this.formWorkOrder.orderDate),
      dueDate: this.safeDate(this.formWorkOrder.dueDate) || null,
      referenceNo: (this.formWorkOrder.referenceNo || '').trim(),
      remarks: (this.formWorkOrder.remarks || '').trim(),
      status: this.formWorkOrder.status || 'Draft',
      active: this.formWorkOrder.active === true,

      // Production Plan gate override. Sent on every create; backend
      // ignores the values when false / when the role isn't permitted.
      bypassPlan:   wantsBypass,
      bypassReason: wantsBypass ? bypassReason : ''
    };

    this.workOrderSvc.create(payload).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;

        if (ok) {
          this.toast.success(res?.message ?? res?.Message ?? 'Created successfully.');

          const savedJobOrderId = Number(this.formWorkOrder.jobOrderId || 0);

          this.closeModal(this.workOrderFormModal);

          this.getWorkOrders();
          
          this.loadJobOrders();
         
          if (savedJobOrderId > 0) {
            this.jobOrders = this.jobOrders.filter(x =>
              Number(x.id ?? x.Id ?? 0) !== savedJobOrderId
            );
          }
         
          this.resetForm();
        } else {
          this.toast.error(res?.message ?? res?.Message ?? 'Create failed.');
        }
      },
      error: (err) => {
        console.error('Create error:', err);
        this.toast.error(err?.error?.message || 'Create failed.');
      }
    });
  }

  /* ================================
     EDIT
  ================================ */
  openEdit(row: any): void {
    if (!row) {
      alert('Invalid work order row.');
      return;
    }

    if (row?.isPosted === true || row?.IsPosted === true) {
      alert('Posted Work Order cannot be edited. Please reverse post first.');
      return;
    }

    this.modalMode = 'edit';

    this.formWorkOrder = {
      id: Number(row?.id ?? row?.Id ?? 0),
      workOrderCode: row?.workOrderCode ?? row?.WorkOrderCode ?? '',

      orderType: row?.orderType ?? row?.OrderType ?? 'Customer',
      isCompanyStockOrder: this.toBool(row?.isCompanyStockOrder ?? row?.IsCompanyStockOrder),

      jobOrderId: Number(row?.jobOrderId ?? row?.JobOrderId ?? 0) || null,
      jobOrderCode: row?.jobOrderCode ?? row?.JobOrderCode ?? '',

      branchId: Number(row?.branchId ?? row?.BranchId ?? 0) || null,
      designId: Number(row?.designId ?? row?.DesignId ?? 0) || null,
      orderQty: Number(row?.orderQty ?? row?.OrderQty ?? 0),

      customerId: Number(row?.customerId ?? row?.CustomerId ?? 0) || null,
      customerLabel: row?.customerLabel ?? row?.customerName ?? row?.CustomerName ?? '',

      productId: Number(row?.productId ?? row?.ProductId ?? 0) || null,
      productLabel:
        row?.productLabel ??
        `${row?.productCode ?? row?.ProductCode ?? ''} ${row?.productName ?? row?.ProductName ?? ''}`.trim(),

      orderDate: this.toDateInputValue(row?.orderDate ?? row?.OrderDate),
      dueDate: this.toDateInputValue(row?.dueDate ?? row?.DueDate),

      referenceNo: row?.referenceNo ?? row?.ReferenceNo ?? '',
      remarks: row?.remarks ?? row?.Remarks ?? '',
      status: row?.status ?? row?.Status ?? 'Draft',
      active: this.toBool(row?.active ?? row?.Active ?? true),
      isPosted: this.toBool(row?.isPosted ?? row?.IsPosted)
    };

    this.openModal(this.workOrderFormModal);
  }

  saveUpdate(): void {
    if (!this.formWorkOrder.id) {
      alert('Invalid work order.');
      return;
    }

    if (!this.formWorkOrder.jobOrderId) {
      alert('Job Order is required.');
      return;
    }

    if (!this.formWorkOrder.branchId) {
      alert('Branch is required.');
      return;
    }

    if (!this.formWorkOrder.designId) {
      alert('Design is required.');
      return;
    }

    if (!this.formWorkOrder.orderQty || this.formWorkOrder.orderQty <= 0) {
      alert('Order quantity must be greater than zero.');
      return;
    }

    if (!this.formWorkOrder.orderDate) {
      alert('Order Date is required.');
      return;
    }

    const payload: WorkOrderDto = {
      id: this.formWorkOrder.id,
      workOrderCode: (this.formWorkOrder.workOrderCode || '').trim(),
      orderType: this.formWorkOrder.isCompanyStockOrder ? 'CompanyStock' : 'Customer',
      isCompanyStockOrder: this.formWorkOrder.isCompanyStockOrder,
      jobOrderId: this.formWorkOrder.jobOrderId ?? null,
      branchId: this.formWorkOrder.branchId ?? null,
      designId: this.formWorkOrder.designId ?? null,
      orderQty: this.formWorkOrder.orderQty ?? 0,     
      customerId: this.formWorkOrder.customerId ?? null,
      productId: this.formWorkOrder.productId ?? null,
      orderDate: this.safeDate(this.formWorkOrder.orderDate),
      dueDate: this.safeDate(this.formWorkOrder.dueDate) || null,
      referenceNo: (this.formWorkOrder.referenceNo || '').trim(),
      remarks: (this.formWorkOrder.remarks || '').trim(),
      status: this.formWorkOrder.status || 'Draft',
      active: this.formWorkOrder.active === true
    };

    this.workOrderSvc.update(payload).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;

        if (ok) {
          this.toast.success(res?.message ?? res?.Message ?? 'Updated successfully.');
          this.closeModal(this.workOrderFormModal);
          this.getWorkOrders();
        } else {
          this.toast.error(res?.message ?? res?.Message ?? 'Update failed.');
        }
      },
      error: err => {
        console.error('Update error:', err);
        this.toast.error(err?.error?.message || 'Update failed.');
      }
    });
  }

  saveForm(): void {
    if (this.modalMode === 'create') {
      this.saveCreate();
    } else {
      this.saveUpdate();
    }
  }

  save(): void {
    this.saveForm();
  }

  /* ================================
     DETAIL
  ================================ */
  openDetail(id: number): void {
    this.workOrderSvc.getDetail(id).subscribe({
      next: (res: any) => {
        this.selectedWorkOrder = res?.data ?? res?.Data ?? null;
        this.openModal(this.workOrderDetailModal);
      },
      error: err => {
        console.error('Detail error:', err);
      }
    });
  }

  /* ================================
     Phase L.1 - 360 PROFILE
  ================================ */
  openProfile(id: number): void {
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;

    this.workOrderSvc.getProfile(id).subscribe({
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

    // Sprint-2: kick off the audit + workflow loads in parallel so both
    // cards have data ready by the time the user scrolls. Each is wrapped
    // so a missing table (audit log not yet created) doesn't break the
    // Profile drawer — the section just stays empty.
    this.loadAuditLog(id);
    this.loadWorkflowStage(id);
  }

  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
    this.auditLog = [];
    this.workflowStageIndex = 0;
  }

  private loadAuditLog(id: number): void {
    this.auditLoading = true;
    this.auditLog = [];
    this.workOrderSvc.getAuditLog(id).subscribe({
      next: r => {
        this.auditLoading = false;
        if (r?.success) this.auditLog = r.data || [];
      },
      error: () => { this.auditLoading = false; }
    });
  }

  private loadWorkflowStage(id: number): void {
    this.workOrderSvc.getWorkflowStage(id).subscribe({
      next: r => {
        if (!r?.success) return;
        const d = r.data;
        this.workflowStages = (d?.workflowStages && d.workflowStages.length)
          ? d.workflowStages
          : ['DRAFT', 'POSTED', 'IN_PRODUCTION', 'QC', 'DISPATCHED', 'CLOSED'];
        this.workflowStageIndex = Number(d?.workflowStageIndex ?? 0);
      }
    });
  }

  /** Sprint-2: map an audit action → Bootstrap chip class + icon + label.
   *  Single switch so the look stays consistent and new actions can be
   *  added in one spot. */
  auditPill(action: string): { cls: string; icon: string; label: string } {
    switch ((action || '').trim()) {
      case 'Created':       return { cls: 'bg-secondary',           icon: 'bi-plus-circle',          label: 'Created'       };
      case 'Updated':       return { cls: 'bg-info',                icon: 'bi-pencil-square',        label: 'Updated'       };
      case 'QtyChanged':    return { cls: 'bg-warning text-dark',   icon: 'bi-arrow-left-right',     label: 'Qty changed'   };
      case 'StatusChanged': return { cls: 'bg-light text-dark border', icon: 'bi-tag',               label: 'Status changed'};
      case 'Posted':        return { cls: 'bg-primary',             icon: 'bi-check2-circle',        label: 'Posted'        };
      case 'Reversed':      return { cls: 'bg-warning text-dark',   icon: 'bi-arrow-counterclockwise', label: 'Reversed'    };
      case 'Deleted':       return { cls: 'bg-danger',              icon: 'bi-trash',                label: 'Deleted'       };
      default:              return { cls: 'bg-secondary',           icon: 'bi-clock-history',        label: action || '—'   };
    }
  }

  /** Stepper helper — colour each marker by position relative to the
   *  current stage. Past = filled green, current = filled primary,
   *  future = grey outline. Same pattern as Production Plan. */
  stepperState(stageIndex: number): { dotCls: string; lineCls: string; textCls: string } {
    if (stageIndex < this.workflowStageIndex) {
      return { dotCls: 'bg-success border-success text-white',
               lineCls: 'bg-success',
               textCls: 'text-success' };
    }
    if (stageIndex === this.workflowStageIndex) {
      return { dotCls: 'bg-primary border-primary text-white',
               lineCls: 'bg-light',
               textCls: 'text-primary fw-bold' };
    }
    return { dotCls: 'bg-light border text-muted',
             lineCls: 'bg-light',
             textCls: 'text-muted' };
  }

  /** Human-readable stage label (DRAFT → Draft, IN_PRODUCTION → In Production). */
  stageLabel(stage: string): string {
    if (!stage) return '';
    return stage.split('_')
      .map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase())
      .join(' ');
  }

  qcStatusPill(s: string): { cssClass: string; icon: string } {
    const u = (s ?? '').toString().trim().toUpperCase();
    if (u === 'PASSED' || u === 'APPROVED' || u === 'ACCEPTED')
      return { cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'FAILED' || u === 'REJECTED')
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-x-circle-fill' };
    if (u === 'REWORK' || u === 'RETURNED')
      return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-arrow-counterclockwise' };
    if (u === 'PENDING' || !u)
      return { cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }

  /* ================================
     DELETE
  ================================ */
  openDelete(row: WorkOrderRowViewModel): void {
    this.deleteTarget = row;
    this.openModal(this.deleteModal);
  }

  confirmDelete(): void {
    const id = (this.deleteTarget as any)?.id ?? (this.deleteTarget as any)?.Id;

    if (!id) {
      return;
    }

    this.workOrderSvc.delete(id).subscribe({
      next: (res: any) => {
        const ok = res?.success ?? res?.Success ?? false;

        if (ok) {
          this.toast.success(res?.message ?? res?.Message ?? 'Deleted successfully.');
          this.closeModal(this.deleteModal);
          this.deleteTarget = null;
          this.getWorkOrders();
        } else {
          this.toast.error(res?.message ?? res?.Message ?? 'Delete failed.');
        }
      },
      error: err => {
        console.error('Delete failed:', err);
        this.toast.error(err?.error?.message || 'Delete failed.');
      }
    });
  }

  /* ================================
     MODAL HELPERS
  ================================ */
  private getModalInstance(ref: ElementRef | any): any | null {
    const el = ref?.nativeElement ?? ref;

    if (!el) {
      console.error('Modal element is undefined:', ref);
      return null;
    }

    let modal = bootstrap.Modal.getInstance(el);

    if (!modal) {
      modal = new bootstrap.Modal(el, {
        backdrop: 'static',
        keyboard: false
      });
    }

    return modal;
  }

  openModal(ref: ElementRef | any): void {
    const modal = this.getModalInstance(ref);
    if (modal) {
      modal.show();
    }
  }

  closeModal(ref: ElementRef | any): void {
    const modal = this.getModalInstance(ref);
    if (modal) {
      modal.hide();
    }
  }

  /* ================================
     DROPDOWNS
  ================================ */
  loadDropdowns(): void {
    this.loadCustomers();
    this.loadProducts();
    this.loadJobOrders();
    this.loadBranches();
    this.loadDesigns();  
  }

  loadCustomers(callback?: () => void): void {
    this.workOrderSvc.getCustomers().subscribe({
      next: (res: any[]) => {
        this.customers = Array.isArray(res)
          ? res.map((x: any) => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            customerCode: x?.customerCode ?? x?.CustomerCode ?? '',
            customerName: x?.customerName ?? x?.CustomerName ?? '',
            name: `${x?.customerCode ?? x?.CustomerCode ?? ''} - ${x?.customerName ?? x?.CustomerName ?? ''}`
          }))
          : [];

        if (callback) callback();
      },
      error: () => {
        this.customers = [];
      }
    });
  }

  loadProducts(): void {
    this.workOrderSvc.getProducts().subscribe({
      next: (res: any[]) => {
        this.products = Array.isArray(res)
          ? res.map((x: any) => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            code: x?.code ?? x?.Code ?? x?.productCode ?? x?.ProductCode ?? '',
            name: x?.name ?? x?.Name ?? x?.productName ?? x?.ProductName ?? ''
          }))
          : [];
      },
      error: () => this.products = []
    });
  }

  loadJobOrders(): void {
    this.workOrderSvc.getJobOrders().subscribe({
      next: (res: any[]) => {
        this.jobOrders = Array.isArray(res)
          ? res.map((x: any) => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            jobOrderCode: x?.jobOrderCode ?? x?.JobOrderCode ?? x?.text ?? x?.Text ?? '',
            remainingQty: Number(x?.remainingQty ?? x?.RemainingQty ?? x?.orderQty ?? x?.OrderQty ?? 0)
          }))
          : [];
      },
      error: () => {
        this.jobOrders = [];
      }
    });
  }

  loadBranches(): void {
    this.workOrderSvc.getBranches().subscribe({
      next: res => this.branches = res || [],
      error: () => this.branches = []
    });
  }

  loadDesigns(callback?: () => void): void {
    this.workOrderSvc.getDesigns().subscribe({
      next: (res: any[]) => {
        this.designs = Array.isArray(res)
          ? res.map((x: any) => ({
            id: Number(x?.id ?? x?.Id ?? 0),
            designCode: x?.designCode ?? x?.DesignCode ?? x?.dCode ?? x?.D_Code ?? '',
            designName: x?.designName ?? x?.DesignName ?? x?.collectionName ?? ''
          }))
          : [];

        // ✅ callback execution
        if (callback) callback();
      },
      error: () => {
        this.designs = [];
      }
    });
  }

  /* ================================
     EXPORTS
  ================================ */
  exportExcel(): void {
    window.open('/emb-products/work-orders/export-excel', '_blank');
  }

  exportPdf(): void {
    window.open('/emb-products/work-orders/export-pdf', '_blank');
  }

  printList(): void {
    window.print();
  }

  /* ================================
     DATE UTILITIES
  ================================ */
  convertJsonDate(value: any): string {
    if (!value) return '';

    if (typeof value === 'string' && value.startsWith('/Date(')) {
      const t = parseInt(value.replace('/Date(', '').replace(')/', ''), 10);
      return new Date(t).toISOString().substring(0, 10);
    }

    return value.toString().substring(0, 10);
  }

  safeDate(input: any): string {
    if (!input) return '';

    if (typeof input === 'string' && input.includes('/Date(')) {
      const m = /\/Date\((\d+)\)\//.exec(input);
      if (m) return new Date(Number(m[1])).toISOString().substring(0, 10);
    }

    if (typeof input === 'string' && /^\d{4}-\d{2}-\d{2}/.test(input)) {
      return input.substring(0, 10);
    }

    if (input instanceof Date) {
      return input.toISOString().substring(0, 10);
    }

    return '';
  }

  /* ================================
     DISPLAY HELPERS
  ================================ */
  getJobOrderDisplay(x: any): string {
    const code = x?.code ?? x?.Code ?? x?.jobOrderCode ?? x?.JobOrderCode ?? '';
    const name = x?.name ?? x?.Name ?? '';
    return `${code}${code && name ? ' - ' : ''}${name}`;
  }

  getDesignDisplay(x: any): string {
    const code = x?.code ?? x?.Code ?? x?.d_Code ?? x?.D_Code ?? '';
    const name = x?.name ?? x?.Name ?? x?.collectionName ?? x?.CollectionName ?? '';
    return `${code}${code && name ? ' - ' : ''}${name}`;
  }

  getProductDisplay(x: any): string {
    const code = x?.code ?? x?.Code ?? '';
    const name = x?.name ?? x?.Name ?? x?.prod_Name ?? x?.Prod_Name ?? '';
    return `${code}${code && name ? ' - ' : ''}${name}`;
  }

  getBranchDisplay(x: any): string {
    return x?.name ?? x?.Name ?? '—';
  }

  toDateInputValue(value: any): string | null {
    if (!value) return null;

    if (typeof value === 'string' && value.includes('/Date(')) {
      const timestamp = Number(value.replace('/Date(', '').replace(')/', ''));
      return new Date(timestamp).toISOString().substring(0, 10);
    }

    const date = new Date(value);
    if (isNaN(date.getTime())) return null;

    return date.toISOString().substring(0, 10);
  }

  private toNumberOrNull(value: any): number | null {
    const n = Number(value);
    return Number.isFinite(n) && n > 0 ? n : null;
  }

  private toBool(value: any): boolean {
    return value === true || value === 'true' || value === 1 || value === '1';
  }

  // ====================================================================
  // EMR roll-up overlay
  // --------------------------------------------------------------------
  // Asks the EMR controller for { workOrderId, extraCount, extraValue }
  // per visible WO and patches each WorkOrderRowViewModel in place. The
  // template uses these fields to show a badge: "Extra: 2 (1,250)".
  // ====================================================================
  private fetchEmrRollup(woIds: number[]): void {
    if (!woIds || !woIds.length) return;
    this.http.post<any>('/emb-products/extra-material-request/rollup-by-workorders',
                        { Ids: woIds })
      .subscribe({
        next: (res: any) => {
          const rows: any[] = res?.data ?? res?.Data ?? [];
          if (!rows.length) return;
          const map = new Map<number, { count: number; value: number }>();
          for (const r of rows) {
            map.set(Number(r.workOrderId ?? r.WorkOrderId),
                    { count: Number(r.extraCount ?? r.ExtraCount ?? 0),
                      value: Number(r.extraValue ?? r.ExtraValue ?? 0) });
          }
          const apply = (list: any[]) => {
            for (const w of list) {
              const m = map.get(Number(w.id));
              w.extraIssuedCount = m ? m.count : 0;
              w.extraIssuedValue = m ? m.value : 0;
            }
          };
          apply(this.workOrders);
          apply(this.items);
        },
        error: () => { /* roll-up failure is non-fatal */ }
      });
  }

  private mapWorkOrderRow(x: any): WorkOrderRowViewModel {
    return {
      id: Number(x?.id ?? x?.Id ?? 0),
      workOrderCode: x?.workOrderCode ?? x?.WorkOrderCode ?? '',

      orderType: x?.orderType ?? x?.OrderType ?? 'Customer',
      isCompanyStockOrder: this.toBool(x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder ?? false),

      jobOrderId: this.toNumberOrNull(x?.jobOrderId ?? x?.JobOrderId),
      jobOrderCode: x?.jobOrderCode ?? x?.JobOrderCode ?? null,

      customerId: this.toBool(x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder ?? false)
        ? null : this.toNumberOrNull(x?.customerId ?? x?.CustomerId),

      customerName: this.toBool(x?.isCompanyStockOrder ?? x?.IsCompanyStockOrder ?? false)
        ? 'Stock' : (x?.customerName ?? x?.CustomerName ?? null),

      productId: Number(x?.productId ?? x?.ProductId ?? 0) || null,
      productCode: x?.productCode ?? x?.ProductCode ?? null,
      productName: x?.productName ?? x?.ProductName ?? null,

      designId: Number(x?.designId ?? x?.DesignId ?? 0) || null,
      designCode: x?.designCode ?? x?.DesignCode ?? null,
      designName: x?.designName ?? x?.DesignName ?? null,

      branchId: this.toNumberOrNull(x?.branchId ?? x?.BranchId),
      branchName: x?.branchName ?? x?.BranchName ?? null,

      orderQty: Number(x?.orderQty ?? x?.OrderQty ?? 0),
      orderDate: this.safeDate(x?.orderDate ?? x?.OrderDate),
      dueDate: this.safeDate(x?.dueDate ?? x?.DueDate) || null,

      referenceNo: x?.referenceNo ?? x?.ReferenceNo ?? null,
      remarks: x?.remarks ?? x?.Remarks ?? null,

      status: x?.status ?? x?.Status ?? null,
      active: this.toBool(x?.active ?? x?.Active ?? true),
      deleted: x?.deleted ?? x?.Deleted ?? null,

      isPosted: this.toBool(x?.isPosted ?? x?.IsPosted ?? false),
      postedBy: this.toNumberOrNull(x?.postedBy ?? x?.PostedBy),
      postedByEmployeeId: this.toNumberOrNull(x?.postedByEmployeeId ?? x?.PostedByEmployeeId),
      postedByName: x?.postedByName ?? x?.PostedByName ?? null,
      postedDate: this.safeDate(x?.postedDate ?? x?.PostedDate) || null,

      recordTimeStamp: x?.recordTimeStamp ?? x?.RecordTimeStamp ?? null,
      createdDate: this.safeDate(x?.createdDate ?? x?.CreatedDate) || null,
      updatedDate: this.safeDate(x?.updatedDate ?? x?.UpdatedDate) || null
    };
  }
  async post(row: WorkOrderRowViewModel): Promise<void> {
    if (!row?.id) return;

    const ok = await this.confirmSvc.ask(
      `Post Work Order ${row.workOrderCode}? Items will be generated automatically.`,
      { title: 'Post Work Order', confirmText: 'Post' }
    );
    if (!ok) return;

    this.workOrderSvc.post(row.id).subscribe({
      next: (res: any) => {
        this.toast.success(res?.message ?? res?.Message ?? 'Posted.');
        this.getWorkOrders();

        // The auto-PR now always lands as DRAFT. Jump straight to its
        // Edit modal so the buyer can review supplier / items / qty
        // and then drive Submit + Approve. PR id arrives as data.autoPrId.
        const autoPrId = Number(res?.data?.autoPrId ?? res?.Data?.autoPrId ?? 0);
        if (autoPrId > 0) {
          this.router.navigate(['/purchase-requisition'], { queryParams: { openId: autoPrId } });
        }
      },
      error: err => this.toast.error(err?.error?.message || 'Post failed.')
    });
  }

  async reversePost(row: WorkOrderRowViewModel): Promise<void> {
    if (!row?.id) return;

    const ok = await this.confirmSvc.danger(
      `Reverse post ${row.workOrderCode}? Generated items will be removed and any linked PR/PO will be cancelled.`,
      { title: 'Reverse Work Order', confirmText: 'Reverse' }
    );
    if (!ok) return;

    this.workOrderSvc.reversePost(row.id).subscribe({
      next: (res: any) => {
        this.toast.success(res?.message ?? res?.Message ?? 'Reverse posted.');
        this.getWorkOrders();
      },
      error: err => this.toast.error(err?.error?.message || 'Reverse post failed.')
    });
  }

  canEdit(row: WorkOrderRowViewModel): boolean {
    return row?.isPosted !== true;
  }

  canDelete(row: WorkOrderRowViewModel): boolean {
    return row?.isPosted !== true;
  }

  canPost(row: WorkOrderRowViewModel): boolean {
    return row?.isPosted !== true;
  }

  canReversePost(row: WorkOrderRowViewModel): boolean {
    return row?.isPosted === true;
  }
 
}
