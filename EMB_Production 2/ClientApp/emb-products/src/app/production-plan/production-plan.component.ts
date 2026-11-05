import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import * as bootstrap from 'bootstrap';

import { ProductionPlanService, PlanAuditEntry } from '../services/production-plan.service';
import {
  PlanRow, PlanItemRow, PlanDetail, PlanSummary, BomRollupRow, CapacityRow,
  EligibleJobOrder, MachineDropdownItem
} from '../shared/models/production-plan.dto';

interface EditableItem {
  id: number;
  jobOrderId: number | null;
  jobOrderCode: string;
  isCompanyStockOrder: boolean;
  productId: number | null;
  productName: string;
  designId: number | null;
  designCode: string;
  plannedQty: number;
  machineId: number | null;
  plannedStartDate: string;
  plannedEndDate: string;
  remarks: string;
  status: string;
  generatedWorkOrderCode: string;
}

@Component({
  selector: 'app-production-plan',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './production-plan.component.html',
  styleUrls: ['./production-plan.component.css']
})
export class ProductionPlanComponent implements OnInit, AfterViewInit {
  // ============ STATE ============
  rows: PlanRow[] = [];
  summary: PlanSummary = { count: 0, draftCount: 0, approvedCount: 0, inProgressCount: 0, closedCount: 0 };
  loading = false;

  // Filters
  statusFilter = '';
  yearFilter: number | null = null;
  weekFilter: number | null = null;
  searchText = '';
  // Sprint-1: Branch filter on the list (multi-branch sites need to isolate
  // their plans). Wired to the existing backend `branchId` parameter.
  branchFilter: number | null = null;
  // Sprint-2 deep-link from Job Orders → Related → Plan. Narrows the list
  // to Plans that contain at least one item for the given JobOrderId.
  filterJobOrderId: number | null = null;

  // Editor state
  modalMode: 'create' | 'edit' = 'create';
  current: {
    id: number;
    planCode: string;
    branchId: number | null;
    branchName: string;
    year: number;
    weekNo: number;
    weekStartDate: string;
    weekEndDate: string;
    status: string;
    remarks: string;
  } = this.emptyHeader();

  items: EditableItem[] = [];
  bom: BomRollupRow[] = [];
  capacity: CapacityRow[] = [];

  eligibleJOs: EligibleJobOrder[] = [];
  machines: MachineDropdownItem[] = [];
  branches: { id: number; name: string }[] = [];

  // Add-item picker
  pickJobOrderId: number | null = null;
  pickMachineId: number | null = null;
  pickStartDate = '';
  pickEndDate = '';
  pickQty: number | null = null;

  saving = false;

  // ============ MODALS ============
  @ViewChild('editorModal') editorModal?: ElementRef;
  private editorModalInstance?: bootstrap.Modal;

  constructor(
    private svc: ProductionPlanService,
    private http: HttpClient,
    private router: Router,
    private route: ActivatedRoute
  ) {}

  ngOnInit(): void {
    // Sprint-2: honor ?search= and ?jobOrderId= from the URL so quick-jump
    // links from Job Orders land here pre-filtered.
    const incomingSearch = this.route.snapshot.queryParamMap.get('search');
    if (incomingSearch) this.searchText = incomingSearch.trim();
    const incomingJobOrderId = this.route.snapshot.queryParamMap.get('jobOrderId');
    if (incomingJobOrderId) {
      const n = Number(incomingJobOrderId);
      if (n > 0) this.filterJobOrderId = n;
    }

    // Sprint-1: load branches up front so the filter-bar dropdown is
    // populated on page open (loadDropdowns runs again when the editor
    // modal opens, but the filter UX needs them earlier).
    this.loadDropdowns();
    this.loadList();
  }

  ngAfterViewInit(): void {
    if (this.editorModal) this.editorModalInstance = new bootstrap.Modal(this.editorModal.nativeElement);
  }

  // ============ LIST ============
  loadList(): void {
    this.loading = true;
    this.svc.list({
      status: this.statusFilter,
      branchId: this.branchFilter || undefined,   // Sprint-1
      year: this.yearFilter || undefined,
      weekNo: this.weekFilter || undefined,
      search: this.searchText,
      jobOrderId: this.filterJobOrderId || undefined
    }).subscribe({
      next: r => {
        this.loading = false;
        if (r?.success) {
          // Normalize every date field on every row so Angular's `| date`
          // pipe (which only parses ISO 8601) doesn't choke on the MVC
          // JavaScriptSerializer's "/Date(ms)/" format — same root cause
          // as the Week Range field showing raw "/Date(178" earlier.
          const rawRows = r.data?.items || [];
          this.rows = rawRows.map((row: any) => ({
            ...row,
            weekStartDate: this.dateOnly(row.weekStartDate),
            weekEndDate:   this.dateOnly(row.weekEndDate),
            createdDate:   this.dateOnly(row.createdDate),
            updatedDate:   this.dateOnly(row.updatedDate),
            approvedDate:  this.dateOnly(row.approvedDate)
          }));
          this.summary = r.data?.summary || this.summary;
        }
      },
      error: () => { this.loading = false; }
    });
  }

  applyFilters(): void {
    // Sprint-3: paging out the visible set resets the selection model so
    // a bulk action never silently operates on rows the user can't see.
    this.clearSelection();
    this.loadList();
  }
  clearFilters(): void {
    this.statusFilter = '';
    this.yearFilter = null;
    this.weekFilter = null;
    this.branchFilter = null;       // Sprint-1
    this.searchText = '';
    this.clearSelection();          // Sprint-3
    this.loadList();
  }

  // ────────────────────────────────────────────────────────────────────
  //  Sprint-1: ISO week helpers + filter shortcuts.
  //  Mirrors the backend's GetWeekOfYear logic (FirstFourDayWeek, Monday)
  //  so the "This Week" button always matches what the server flags as
  //  isCurrentWeek on the row payload.
  // ────────────────────────────────────────────────────────────────────
  private isoWeekOf(date: Date): { year: number; weekNo: number } {
    // ISO-8601: week 1 is the one containing Jan 4th; weeks start Monday.
    const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
    const dayNum = d.getUTCDay() || 7;
    d.setUTCDate(d.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
    const weekNo = Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
    return { year: d.getUTCFullYear(), weekNo };
  }

  /** Filter the list to this week's plan (and reload). */
  goCurrentWeek(): void {
    const w = this.isoWeekOf(new Date());
    this.yearFilter = w.year;
    this.weekFilter = w.weekNo;
    this.loadList();
  }

  /** Filter the list to next week's plan (and reload). */
  goNextWeek(): void {
    const next = new Date();
    next.setDate(next.getDate() + 7);
    const w = this.isoWeekOf(next);
    this.yearFilter = w.year;
    this.weekFilter = w.weekNo;
    this.loadList();
  }

  /** Sprint-1: deep-link to a generated Work Order from inside the detail
   *  modal. Reuses the WO list's search-by-code path, same UX as the
   *  Job Orders → Related → WO quick-jump. */
  gotoWorkOrder(code: string | null | undefined): void {
    if (!code) return;
    this.router.navigate(['/work-orders'], { queryParams: { search: code } });
  }

  /** Sprint-2: clone the row into a new DRAFT for next week. Recurring
   *  weekly plans are the norm; this kills the data-entry tax. After the
   *  clone succeeds we reload the list AND open the new draft so the
   *  planner can tweak quantities / dates before approving. */
  /** Clone preview — open the editor pre-filled with next week's metadata.
   *  Items aren't shown in this preview because they're committed atomically
   *  by /clone-next-week (qty, dates, machines copied verbatim from source).
   *  The new plan only appears in the list after Save Clone is clicked.
   *  Closing without saving abandons the preview. */
  cloneToNextWeek(row: PlanRow): void {
    if (!row?.id) return;

    // Stage a fresh header pre-filled for next week. id = 0 marks this as
    // not-yet-persisted; cloneSourceId carries the source so saveHeader()
    // routes through /clone-next-week on Save.
    this.cloneSourceId   = row.id;
    this.cloneSourceCode = row.planCode;
    this.modalMode = 'create';
    this.current = this.emptyHeader();
    this.items = []; this.bom = []; this.capacity = [];
    this.resetPicker();

    const nextWeek = new Date();
    nextWeek.setDate(nextWeek.getDate() + 7);
    this.current.year = nextWeek.getFullYear();
    this.current.weekNo = this.getIsoWeek(nextWeek);
    this.current.branchId = row.branchId ?? null;
    this.current.remarks = 'Cloned from ' + row.planCode;
    this.current.planCode = '(new clone of ' + row.planCode + ')';
    this.recomputeWeekRange();

    this.loadDropdowns();
    this.editorModalInstance?.show();
    // saveHeader() will detect cloneSourceId and call /clone-next-week.
    return;

    // (legacy immediate-commit code path retained below for reference but
    //  unreachable — kept inside a `if (false)` to preserve behaviour
    //  notes for the reviewer.)
    /*
    this.svc.cloneNextWeek(row.id).subscribe({
      next: (r: any) => {
        if (!r?.success) {
          alert(r?.message || 'Clone failed.');
          return;
        }
        const newId       = Number(r?.data?.id ?? 0);
        const itemsCloned = Number(r?.data?.itemsCloned  ?? 0);
        const itemsSkipped = Number(r?.data?.itemsSkipped ?? 0);
        const skippedJoCodes: string[] = r?.data?.skippedJoCodes ?? [];
        const newCode = r?.data?.planCode || '';

        // Build a message that explains both halves of the result:
        // what made it across, and what didn't (with reasons).
        let msg = `Cloned to ${newCode} (W${r?.data?.weekNo}/${r?.data?.year}) — ${itemsCloned} item(s) copied`;
        if (itemsSkipped > 0) {
          msg += `, ${itemsSkipped} skipped.`
              + `\n\nSkipped items reference Job Orders that are already in another plan`
              + ` (one JO can only be in one plan):\n`
              + skippedJoCodes.slice(0, 8).map(c => `  • ${c}`).join('\n');
          if (skippedJoCodes.length > 8) msg += `\n  …and ${skippedJoCodes.length - 8} more.`;
          msg += `\n\nThe clone was created with the remaining items. You can add fresh items to it after opening.`;
        } else {
          msg += '.';
        }
        alert(msg);

        // Reload list first so the new row exists, then open the new draft.
        this.loadList();
        if (newId) setTimeout(() => this.openEdit(newId), 0);
      },
      error: (err: any) => alert(err?.error?.message || err?.message || 'Clone failed.')
    });
    */
  }

  /** Sprint-2: quick-jump to the Job Orders list, pre-filtered to the
   *  customer-demand orders that this plan touches. We pass a placeholder
   *  search term — the items are linked back through ProductionPlanItems.JobOrderId,
   *  so the JO list's normal search-by-code will surface them once the
   *  planner clicks through the detail modal first. For now, opening the
   *  detail modal is the most informative move. */
  showJobOrdersForPlan(row: PlanRow): void {
    if (!row?.id) return;
    this.openEdit(row.id);
  }

  // ───────────────────────────────────────────────────────────────────
  //  SPRINT-3  BULK ACTIONS
  //  Multi-select using a Set<number> of selected plan ids. Selection
  //  persists within the page but resets on filter change so the operator's
  //  mental model is "bulk operates on what I can see".
  // ───────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  // ──────────────────────────────────────────────────────────────────
  //  CLONE PREVIEW STATE
  //  When set, the editor modal is showing an unsaved clone preview.
  //  Save Clone → POST /clone-next-week/{sourceId} (which commits header
  //  + items + dates). Closing without saving abandons the preview.
  // ──────────────────────────────────────────────────────────────────
  cloneSourceId: number | null = null;
  cloneSourceCode = '';

  // ───────────────────────────────────────────────────────────────────
  //  SPRINT-4  WORKFLOW STAGE + AUDIT TIMELINE STATE
  // ───────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['DRAFT', 'APPROVED', 'IN_PROGRESS', 'CLOSED'];
  workflowStageIndex = 0;
  auditLog: PlanAuditEntry[] = [];
  auditLoading = false;

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
    if (!confirm(`Approve ${ids.length} plan(s)? Each one will generate Work Orders for its items and create an auto-Purchase Requisition for any material shortfall. Drafts only.`)) return;
    this.bulkBusy = true;
    this.svc.bulkApprove(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'approved'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk approve failed.'); }
    });
  }

  bulkClose(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Close ${ids.length} plan(s)? Drafts and already-closed plans will be skipped.`)) return;
    this.bulkBusy = true;
    this.svc.bulkClose(ids).subscribe({
      next: (res: any) => { this.bulkBusy = false; this.afterBulk(res, 'closed'); },
      error: (err: any) => { this.bulkBusy = false;
        alert(err?.error?.message || err?.message || 'Bulk close failed.'); }
    });
  }

  /** Common post-bulk handler: surfaces success/failure counts, lists the
   *  failed rows so the user can fix and retry, then reloads the list and
   *  clears the selection so the UI reflects the new state. */
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
    this.loadList();
  }

  // ============ CREATE / EDIT ============
  openCreate(): void {
    this.modalMode = 'create';
    this.current = this.emptyHeader();
    this.items = []; this.bom = []; this.capacity = [];
    this.resetPicker();

    // Pre-fill NEXT ISO year/week, not the current one. The current
    // week usually already has a plan; defaulting to this week makes
    // Save Plan collide with "A plan already exists for this branch
    // and week" on the very first click. Planners almost always
    // create plans one week ahead anyway.
    const nextWeek = new Date();
    nextWeek.setDate(nextWeek.getDate() + 7);
    this.current.year = nextWeek.getFullYear();
    this.current.weekNo = this.getIsoWeek(nextWeek);
    this.recomputeWeekRange();

    this.svc.nextCode().subscribe(r => {
      if (r?.success && r.data?.code) this.current.planCode = r.data.code;
    });

    this.loadDropdowns();
    this.editorModalInstance?.show();
  }

  openEdit(id: number): void {
    this.svc.detail(id).subscribe(r => {
      if (!r?.success || !r.data) return;
      this.applyDetail(r.data);
      this.modalMode = 'edit';
      this.resetPicker();
      this.loadDropdowns();
      // Sprint-4: pull audit timeline in parallel with the modal opening
      // so the timeline card is populated by the time the user scrolls
      // down to it.
      this.loadAuditLog(id);
      this.editorModalInstance?.show();
    });
  }

  refreshDetail(): void {
    if (!this.current.id) return;
    this.svc.detail(this.current.id).subscribe(r => {
      if (r?.success && r.data) this.applyDetail(r.data);
    });
  }

  private applyDetail(d: PlanDetail): void {
    const h = d.header;
    this.current = {
      id: h.id,
      planCode: h.planCode,
      branchId: h.branchId,
      branchName: h.branchName || '',
      year: h.year,
      weekNo: h.weekNo,
      weekStartDate: this.dateOnly(h.weekStartDate),
      weekEndDate: this.dateOnly(h.weekEndDate),
      status: h.status,
      remarks: h.remarks || ''
    };
    this.items = (d.items || []).map(i => ({
      id: i.id,
      jobOrderId: i.jobOrderId,
      jobOrderCode: i.jobOrderCode || (i.isCompanyStockOrder ? 'Stock' : '-'),
      isCompanyStockOrder: i.isCompanyStockOrder,
      productId: i.productId,
      productName: i.productName || '-',
      designId: i.designId,
      designCode: i.designCode || '-',
      plannedQty: i.plannedQty,
      machineId: i.machineId,
      plannedStartDate: this.dateOnly(i.plannedStartDate),
      plannedEndDate: this.dateOnly(i.plannedEndDate),
      remarks: i.remarks || '',
      status: i.status,
      generatedWorkOrderCode: i.generatedWorkOrderCode || ''
    }));
    this.bom = d.bom || [];
    // Normalize capacity row dates so the Range column renders properly.
    // Without this the server's "/Date(ms)/" format reaches Angular's
    // `| date` pipe which can't parse it and falls back to "—".
    this.capacity = (d.capacity || []).map((c: any) => ({
      ...c,
      earliestStart: this.dateOnly(c.earliestStart),
      latestEnd:     this.dateOnly(c.latestEnd)
    }));

    // Sprint-4: workflow stepper state. Server returns the canonical
    // stage list and the current stage index so the UI never has to map
    // status strings to positions itself.
    const anyData: any = d as any;
    this.workflowStages = Array.isArray(anyData.workflowStages) && anyData.workflowStages.length
      ? anyData.workflowStages
      : ['DRAFT', 'APPROVED', 'IN_PROGRESS', 'CLOSED'];
    this.workflowStageIndex = Number(anyData.workflowStageIndex ?? 0);
  }

  // ───────────────────────────────────────────────────────────────────
  //  SPRINT-4  AUDIT TIMELINE + STEPPER HELPERS
  // ───────────────────────────────────────────────────────────────────

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

  /** Map an audit action string to a Bootstrap pill class + Bootstrap
   *  Icons class + display label. Single switch keeps the look
   *  consistent and lets new action types be added in one place. */
  auditPill(action: string): { cls: string; icon: string; label: string } {
    switch ((action || '').trim()) {
      case 'Created':     return { cls: 'bg-secondary',  icon: 'bi-plus-circle',          label: 'Created'      };
      case 'Updated':     return { cls: 'bg-info',       icon: 'bi-pencil-square',        label: 'Updated'      };
      case 'ItemAdded':   return { cls: 'bg-light text-dark border', icon: 'bi-plus-square', label: 'Item added' };
      case 'ItemUpdated': return { cls: 'bg-light text-dark border', icon: 'bi-pencil',      label: 'Item updated' };
      case 'ItemRemoved': return { cls: 'bg-light text-dark border', icon: 'bi-x-square',    label: 'Item removed' };
      case 'Approved':    return { cls: 'bg-primary',    icon: 'bi-check2-circle',        label: 'Approved'     };
      case 'Reversed':    return { cls: 'bg-warning text-dark', icon: 'bi-arrow-counterclockwise', label: 'Reversed' };
      case 'Closed':      return { cls: 'bg-success',    icon: 'bi-archive',              label: 'Closed'       };
      case 'Deleted':     return { cls: 'bg-danger',     icon: 'bi-trash',                label: 'Deleted'      };
      case 'Cloned':      return { cls: 'bg-info',       icon: 'bi-files',                label: 'Cloned'       };
      case 'ClonedTo':    return { cls: 'bg-info',       icon: 'bi-files',                label: 'Source for clone' };
      default:            return { cls: 'bg-secondary',  icon: 'bi-clock-history',        label: action || '—'  };
    }
  }

  /** Stepper helper — colour each marker by position relative to current
   *  stage. Past = filled green, current = filled primary, future = grey. */
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

  /** Human-friendly stage label (DRAFT → Draft, IN_PROGRESS → In Progress) */
  stageLabel(stage: string): string {
    if (!stage) return '';
    return stage.split('_')
      .map(p => p.charAt(0).toUpperCase() + p.slice(1).toLowerCase())
      .join(' ');
  }

  closeEditor(): void {
    // Drop clone-preview state so reopening a different row doesn't flash
    // the previous source's data on the form.
    this.cloneSourceId = null;
    this.cloneSourceCode = '';
    this.editorModalInstance?.hide();
  }

  /** True when the editor modal is currently showing a clone preview. */
  isCloning(): boolean { return this.cloneSourceId !== null; }

  private loadDropdowns(): void {
    this.svc.eligibleJobOrders().subscribe(r => { if (r?.success) this.eligibleJOs = r.data || []; });
    this.svc.machines().subscribe(r => { if (r?.success) this.machines = r.data || []; });
    // /dropdowns/branches returns { data: [...] } directly (no success wrapper)
    this.http.get<any>('/emb-products/dropdowns/branches').subscribe(r => {
      const arr = r?.data ?? r?.Data ?? [];
      this.branches = (arr || []).map((b: any) => ({
        id: Number(b.id ?? b.Id),
        name: String(b.name ?? b.Name ?? '-')
      }));
    });
  }

  saveHeader(): void {
    if (this.current.year <= 0 || this.current.weekNo <= 0) {
      return alert('Year and Week No are required.');
    }

    // Clone preview Save → /clone-next-week (which commits header + items
    // + date-shifted items atomically). Bypass the create / update branch.
    if (this.cloneSourceId !== null) {
      this.saving = true;
      this.svc.cloneNextWeek(this.cloneSourceId).subscribe({
        next: r => {
          this.saving = false;
          if (!r?.success) return alert(r?.message || 'Clone save failed.');
          const d: any = r.data ?? {};
          const newId = Number(d?.id ?? 0);
          const itemsCloned = Number(d?.itemsCloned ?? 0);
          const itemsSkipped = Number(d?.itemsSkipped ?? 0);
          const skippedJoCodes: string[] = d?.skippedJoCodes ?? [];
          let msg = `Cloned to ${d?.planCode} (W${d?.weekNo}/${d?.year}) — ${itemsCloned} item(s) copied`;
          if (itemsSkipped > 0) {
            msg += `, ${itemsSkipped} skipped.\n\nSkipped JOs:\n` +
              skippedJoCodes.slice(0, 8).map(c => `  • ${c}`).join('\n');
            if (skippedJoCodes.length > 8) msg += `\n  …and ${skippedJoCodes.length - 8} more.`;
          } else { msg += '.'; }
          alert(msg);
          this.cloneSourceId = null;
          this.cloneSourceCode = '';
          this.editorModalInstance?.hide();
          this.loadList();
          if (newId) setTimeout(() => this.openEdit(newId), 0);
        },
        error: e => {
          this.saving = false;
          alert(e?.error?.message || e?.message || 'Clone save failed.');
        }
      });
      return;
    }

    this.doSaveHeader(false);
  }

  /** Inner save — runs once normally, then a second time if the
   *  operator confirms creating a parallel plan for the same week.
   *  `confirmDuplicateWeek=true` tells the server to skip its
   *  duplicate-week warning. */
  private doSaveHeader(confirmDuplicateWeek: boolean): void {
    const payload: any = {
      id: this.current.id || undefined,
      planCode: this.current.planCode,
      branchId: this.current.branchId || undefined,
      year: this.current.year,
      weekNo: this.current.weekNo,
      remarks: this.current.remarks,
      confirmDuplicateWeek
    };

    this.saving = true;
    const obs = this.modalMode === 'create' ? this.svc.create(payload) : this.svc.update(payload);
    obs.subscribe({
      next: r => {
        this.saving = false;
        if (!r?.success) {
          // Soft warning: another plan already exists for the same
          // (Branch, Year, Week). Give the operator three choices:
          // open the existing plan, create a parallel plan anyway,
          // or cancel. Parallel plans are legitimate when running
          // multiple machines / product batches in the same week.
          const conflictId = (r as any)?.conflictId;
          const isWarning  = (r as any)?.warning === true;
          if (isWarning && conflictId && conflictId > 0) {
            const existing = (r as any)?.existingPlans as Array<{ planCode: string; status: string }> | undefined;
            const list = (existing && existing.length)
                ? '\n\nExisting plan(s) for this week:\n' +
                  existing.map(p => `  • ${p.planCode} (${p.status})`).join('\n')
                : '';
            const choice = confirm(
                (r?.message || 'Another plan already exists for this week.') + list
              + '\n\nClick OK to OPEN the existing plan,'
              + '\nor Cancel to create a PARALLEL plan for this week.'
            );
            if (choice) {
              this.closeEditor();
              this.openEdit(conflictId);
            } else {
              // Retry save with the duplicate-week warning suppressed.
              this.doSaveHeader(true);
            }
            return;
          }
          // Surface the server-side detail (exception message) too.
          // The MVC BaseController.JsonError ships it as `detail`.
          const detail = (r as any)?.detail ? '\n\nDetails: ' + (r as any).detail : '';
          return alert((r?.message || 'Save failed.') + detail);
        }
        if (this.modalMode === 'create' && (r.data as any)?.id) {
          this.current.id = (r.data as any).id;
          this.current.planCode = (r.data as any).planCode || this.current.planCode;
          this.modalMode = 'edit';
        }
        this.loadList();
      },
      error: e => {
        this.saving = false;
        const body = e?.error || {};
        const msg    = body.message || e?.message || 'Save failed.';
        const detail = body.detail  ? '\n\nDetails: ' + body.detail : '';
        alert(msg + detail);
      }
    });
  }

  /** Auto-fill the picker's Start/End dates when a Job Order is
   *  selected. Defaults to the plan's week range (Mon-Sun), which is
   *  almost always what a planner wants — items default to fitting
   *  inside the week the plan is for. The operator can still change
   *  either date manually before clicking + to add the item. */
  onPickJoChange(): void {
    if (!this.pickJobOrderId) return;
    // Don't overwrite if the planner has already typed something
    if (!this.pickStartDate && this.current.weekStartDate) {
      this.pickStartDate = this.current.weekStartDate;
    }
    if (!this.pickEndDate && this.current.weekEndDate) {
      this.pickEndDate = this.current.weekEndDate;
    }
    // Auto-fill Qty from the JO's order qty if no override is set
    if ((this.pickQty == null || this.pickQty <= 0)) {
      const jo = this.eligibleJOs.find(x => x.id === Number(this.pickJobOrderId));
      if (jo) this.pickQty = jo.orderQty;
    }
  }

  /** True when the JO currently selected in the picker is still a Draft
   *  (not posted). Drives the amber warning shown under the dropdown so
   *  the operator knows they need to Post the JO before hitting Approve
   *  on the plan — otherwise WO auto-generation may not resolve JO
   *  fields correctly. */
  pickedJoIsDraft(): boolean {
    if (!this.pickJobOrderId) return false;
    const jo = this.eligibleJOs.find(x => x.id === Number(this.pickJobOrderId));
    return !!jo && (jo as any).isPosted === false;
  }

  addItemFromPicker(): void {
    if (!this.current.id) return alert('Save the plan header first.');
    if (!this.pickJobOrderId) return alert('Pick a Job Order.');
    if (!this.pickMachineId) return alert('Pick a Machine.');
    if (!this.pickStartDate || !this.pickEndDate)
      return alert('Pick Planned Start and End dates.');

    const jo = this.eligibleJOs.find(x => x.id === Number(this.pickJobOrderId));
    if (!jo) return;

    this.svc.addItem({
      planId: this.current.id,
      jobOrderId: jo.id,
      isCompanyStockOrder: jo.isCompanyStockOrder,
      productId: jo.productId,
      designId: jo.designId,
      plannedQty: this.pickQty != null && this.pickQty > 0 ? this.pickQty : jo.orderQty,
      machineId: this.pickMachineId,
      plannedStartDate: this.pickStartDate,
      plannedEndDate: this.pickEndDate
    }).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Add failed.');
      this.resetPicker();
      this.refreshDetail();
      this.svc.eligibleJobOrders().subscribe(r2 => { if (r2?.success) this.eligibleJOs = r2.data || []; });
    });
  }

  updateItemInline(i: EditableItem): void {
    if (this.current.status !== 'DRAFT') return;
    this.svc.updateItem({
      id: i.id,
      planId: this.current.id,
      plannedQty: i.plannedQty,
      machineId: i.machineId,
      plannedStartDate: i.plannedStartDate || null,
      plannedEndDate: i.plannedEndDate || null,
      remarks: i.remarks
    }).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Update failed.');
      this.refreshDetail();
    });
  }

  removeItem(id: number): void {
    if (!confirm('Remove this item from the plan?')) return;
    this.svc.removeItem(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Remove failed.');
      this.refreshDetail();
      this.svc.eligibleJobOrders().subscribe(r2 => { if (r2?.success) this.eligibleJOs = r2.data || []; });
    });
  }

  approve(): void {
    if (!this.current.id) return;
    if (!confirm('Approve plan? This will auto-generate one Draft Work Order per item and lock the plan.')) return;

    this.svc.approve(this.current.id).subscribe({
      next: r => {
        if (r?.success) {
          const d: any = r.data || {};
          alert(`Approved. ${d.generatedWorkOrders ?? 0} Work Orders generated.`);
          this.refreshDetail();
          this.loadList();

          // Auto-PR now lands as DRAFT. Take the manager straight to the
          // PR Edit modal so they can review supplier + items before the
          // PO gets created on manual Approve. autoPrId arrives in the
          // approve response when the plan had a material shortfall.
          const autoPrId = Number(d.autoPrId ?? 0);
          if (autoPrId > 0) {
            this.closeEditor();
            this.router.navigate(['/purchase-requisition'], { queryParams: { openId: autoPrId } });
          }
          return;
        }
        const conflicts: any[] = (r as any)?.conflicts || [];
        if (conflicts.length) {
          const lines: string[] = [];
          for (const c of conflicts) {
            lines.push(` • ${c.machineName || ('Machine ' + c.machineId)}:`);
            for (const d of (c.conflictDetails || [])) lines.push(`     - ${d}`);
          }
          alert(`${r?.message || 'Capacity over-allocated'}\n\n${lines.join('\n')}`);
        } else {
          alert(r?.message || 'Approve failed.');
        }
      },
      error: e => alert(e?.error?.message || e?.message || 'Approve failed.')
    });
  }

  close(): void {
    if (!this.current.id) return;
    if (!confirm('Mark plan as CLOSED?')) return;
    this.svc.close(this.current.id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Close failed.');
      this.refreshDetail();
      this.loadList();
    });
  }

  remove(id: number): void {
    if (!confirm('Soft-delete this draft plan?')) return;
    this.svc.remove(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Delete failed.');
      this.loadList();
    });
  }

  /** Reverse an APPROVED plan — rolls back the auto-generated Work
   *  Orders and pushes the plan back to DRAFT so it can be edited and
   *  re-approved. The server refuses if any of the generated WOs have
   *  already been posted (materials issued / production started). */
  reverse(id: number): void {
    if (!confirm(
        'Reverse this approved plan?\n\n'
      + 'This will:\n'
      + ' • Soft-delete all Work Orders generated from this plan\n'
      + ' • Reset every plan item to PENDING\n'
      + ' • Move the plan back to DRAFT so it can be edited'
    )) return;

    this.svc.reverse(id).subscribe(r => {
      if (!r?.success) {
        const detail = (r as any)?.detail ? '\n\nDetails: ' + (r as any).detail : '';
        return alert((r?.message || 'Reverse failed.') + detail);
      }
      const data = (r as any).data;
      alert('Plan reversed. '
        + (data?.reversedWorkOrders ?? 0)
        + ' Work Order(s) rolled back.');
      // If the modal is open for this plan, refresh its detail too.
      if (this.current.id === id) this.refreshDetail();
      this.loadList();
    });
  }

  /** Sprint-1: Reopen the current modal-loaded plan back to DRAFT so the
   *  picker + inline edit fields unlock, and the operator can add another
   *  Job Order to it. Shares the server-side Reverse endpoint — the same
   *  WO-posted safety check still applies. Confirms with a status-aware
   *  message so the operator knows what they're about to undo. */
  reopenToDraft(): void {
    if (!this.current.id) return;
    const status = (this.current.status || '').toUpperCase();
    const msg =
        'Reopen plan ' + (this.current.planCode || '#' + this.current.id) + '?\n\n'
      + 'Current status: ' + status + '\n\n'
      + 'This will:\n'
      + '  • Soft-delete every Work Order generated from this plan\n'
      + '  • Reset every plan item to PENDING\n'
      + '  • Move the plan back to DRAFT so you can edit items / add JOs\n\n'
      + 'Refused if any generated Work Order has already been Posted.';
    if (!confirm(msg)) return;

    this.saving = true;
    this.svc.reverse(this.current.id).subscribe({
      next: r => {
        this.saving = false;
        if (!r?.success) {
          const detail = (r as any)?.detail ? '\n\nDetails: ' + (r as any).detail : '';
          alert((r?.message || 'Reopen failed.') + detail);
          return;
        }
        const data = (r as any).data;
        alert('Plan reopened. '
            + (data?.reversedWorkOrders ?? 0)
            + ' Work Order(s) rolled back. The plan is now DRAFT.');
        this.refreshDetail();
        this.loadList();
      },
      error: e => {
        this.saving = false;
        alert(e?.error?.message || 'Reopen failed.');
      }
    });
  }

  // ============ HELPERS ============
  badgeClass(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'DRAFT':       return 'bg-secondary';
      case 'APPROVED':    return 'bg-primary';
      case 'IN_PROGRESS': return 'bg-info';
      case 'CLOSED':      return 'bg-success';
      default:            return 'bg-light text-dark';
    }
  }

  capacityOver(): boolean { return this.capacity.some(c => c.hasConflict); }
  bomShortfall(): number { return this.bom.reduce((s, r) => s + (r.shortfall || 0), 0); }

  private getIsoWeek(d: Date): number {
    const t = new Date(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate()));
    const dayNum = t.getUTCDay() || 7;
    t.setUTCDate(t.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(t.getUTCFullYear(), 0, 1));
    return Math.ceil((((t.getTime() - yearStart.getTime()) / 86400000) + 1) / 7);
  }

  /**
   * Normalize any backend-emitted date string to ISO `YYYY-MM-DD` so
   * HTML5 `<input type="date">` accepts and displays it.
   *
   * The backend pipeline can yield three shapes depending on the
   * serializer that touched the value:
   *   1. ISO 8601:  "2026-06-25T00:00:00"      (camelCase Newtonsoft)
   *   2. MS format: "/Date(1782345600000)/"    (JavaScriptSerializer)
   *   3. MS format: "/Date(1782345600000+0500)/"  (with TZ offset)
   *
   * Without this normalization, an unparsed "/Date(178..." flows into
   * <input type="date">, the input rejects it as a non-ISO string,
   * and you get a blank "mm/dd/yyyy" with no error — which feels like
   * "date doesn't enter" but is actually "date can't render".
   */
  private dateOnly(d: string | null): string {
    if (!d) return '';

    // Microsoft JSON date format (with or without timezone offset)
    const msMatch = /^\/Date\((-?\d+)(?:[+-]\d{4})?\)\/?$/.exec(d);
    if (msMatch) {
      const ms = parseInt(msMatch[1], 10);
      const dt = new Date(ms);
      if (!isNaN(dt.getTime())) {
        // Use UTC parts so a stored midnight-UTC date doesn't shift a
        // day backward in PKT/IST/other forward timezones.
        const yyyy = dt.getUTCFullYear();
        const mm   = String(dt.getUTCMonth() + 1).padStart(2, '0');
        const dd   = String(dt.getUTCDate()).padStart(2, '0');
        return `${yyyy}-${mm}-${dd}`;
      }
    }

    // ISO 8601 — first 10 chars are already YYYY-MM-DD
    return d.length >= 10 ? d.substring(0, 10) : d;
  }

  private emptyHeader() {
    return { id: 0, planCode: '',
             branchId: null as number | null, branchName: '',
             year: 0, weekNo: 0,
             weekStartDate: '', weekEndDate: '', status: 'DRAFT', remarks: '' };
  }

  // Recompute the week range whenever Year or Week No changes (UI feedback).
  recomputeWeekRange(): void {
    const y = Number(this.current.year);
    const w = Number(this.current.weekNo);
    if (!y || !w || w < 1 || w > 53) {
      this.current.weekStartDate = '';
      this.current.weekEndDate = '';
      return;
    }
    // ISO 8601: same approach the backend uses.
    const jan1 = new Date(y, 0, 1);
    const offset = 1 - jan1.getDay();              // Monday = 1
    const firstMonday = new Date(jan1);
    firstMonday.setDate(jan1.getDate() + (offset < 0 ? offset + 7 : offset));
    const start = new Date(firstMonday);
    start.setDate(firstMonday.getDate() + (w - 1) * 7);
    const end = new Date(start);
    end.setDate(start.getDate() + 6);
    // Format via local getters, NOT toISOString(). toISOString() rolls
    // the local Monday-00:00 back into Sunday-19:00 UTC for any zone
    // east of UTC (Pakistan is UTC+5), so its date substring lands one
    // day early — the picker then auto-fills a Sunday and the server
    // rejects it as outside the plan's Mon–Sun week.
    const ymd = (d: Date) =>
      d.getFullYear() + '-' +
      String(d.getMonth() + 1).padStart(2, '0') + '-' +
      String(d.getDate()).padStart(2, '0');
    this.current.weekStartDate = ymd(start);
    this.current.weekEndDate   = ymd(end);
  }

  private resetPicker(): void {
    this.pickJobOrderId = null;
    this.pickMachineId = null;
    this.pickStartDate = '';
    this.pickEndDate = '';
    this.pickQty = null;
  }
}
