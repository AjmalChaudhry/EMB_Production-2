import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {DropdownItem,StoreDashboardViewModel,StoreDetailViewModel,StoreEntryDto,StoreRowViewModel,
  StoreItemBalanceViewModel,WorkOrderMaterialRequirementViewModel,MaterialIssueDto
} from '../shared/models/gen-store.dto';
import { GenStoreService } from '../services/gen-store.service';

interface MaterialIssueFormUI {
  workOrderId: number;
  branchId: number | null;
  warehouseId: number | null;
  transactionDate: string | null;
  referenceNo: string;
  remarks: string;
}

@Component({
  selector: 'app-gen-store',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gen-store.html',
  styleUrls: ['./gen-store.css']
})
export class GenStoreComponent implements OnInit {
  loading = false;
  saving = false;

  rows: StoreRowViewModel[] = [];
  itemBalances: StoreItemBalanceViewModel[] = [];
  detailModel: StoreDetailViewModel | null = null;

  // Per-item ledger history modal state
  historyOpen = false;
  historyLoading = false;
  historyItem: StoreItemBalanceViewModel | null = null;
  historyRows: Array<{
    id: number;
    dateText: string;
    transactionType: string;
    referenceType: string;
    referenceNo: string;
    inQty: number;
    outQty: number;
    runningBalance: number;
    rate: number;
    amount: number;
    remarks: string;
  }> = [];

  dashboard: StoreDashboardViewModel = this.emptyDashboard();

  rawItems: DropdownItem[] = [];
  branches: DropdownItem[] = [];
  workOrders: DropdownItem[] = [];
  issueMaterialRows: WorkOrderMaterialRequirementViewModel[] = [];
  issuedMaterialRows: any[] = [];
  issuedSearchWorkOrderId: number | null = null;
  issuedWorkOrders: DropdownItem[] = [];
  

  search = '';
  branchId: number | null = null;
  activeFilter = '';

  // Phase G.0 filters
  fromDate = '';
  toDate = '';
  referenceType = '';
  // Sprint-1: matches the StoreReferenceType.All whitelist on the
  // backend so the dropdown can't request a ref-type the server
  // rejects. Keep these in sync if a new flow ships a new ref-type.
  referenceTypes = [
    'OPENING', 'OPENING_IN',
    'PURCHASE', 'PURCHASE_RETURN',
    'MATERIAL_ISSUE', 'MATERIAL_ISSUE_REVERSE',
    'STOCK_ADJUSTMENT', 'BRANCH_TRANSFER',
    'QC_INSPECTION', 'DISPATCH', 'EMR_ISSUE'
  ];

  // Phase G.2 - Branch transfer state
  showTransferForm = false;
  formTransfer: {
    itemId: number | null;
    fromBranchId: number | null;
    toBranchId: number | null;
    qty: number;
    transactionDate: string;
    rate: number | null;
    remarks: string;
  } = this.createEmptyTransfer();

  // Phase G.3 - Stock adjustment state
  showAdjustForm = false;
  adjustSaving = false;
  adjustReasons = [
    { value: 'DAMAGE',              label: 'Damage' },
    { value: 'LOSS',                label: 'Loss / Shrinkage' },
    { value: 'GAIN',                label: 'Found / Gain' },
    { value: 'RECOUNT',             label: 'Recount Correction' },
    { value: 'OPENING_CORRECTION',  label: 'Opening Correction' },
    { value: 'OTHER',               label: 'Other' }
  ];
  formAdjust: {
    itemId: number | null;
    branchId: number | null;
    sign: 'IN' | 'OUT';
    qty: number;
    reason: string;
    transactionDate: string;
    rate: number | null;
    remarks: string;
  } = this.createEmptyAdjust();

  kpis = {
    distinctItems: 0,
    activeItemCount: 0,
    negativeCount: 0,
    zeroCount: 0,
    totalOpening: 0,
    totalIn: 0,
    totalOut: 0,
    netBalance: 0,
    stockValue: 0,
    belowReorderCount: 0,
    aboveMaxCount: 0,
    // Echo of applied filters so the chip strip can label scope.
    // KPI numbers themselves are always lifetime (Opening + IN - OUT
    // ties out to Net Balance regardless of any date filter — date
    // only narrows the transactions table below).
    appliedFromDate: null as string | null,
    appliedToDate:   null as string | null
  };

  // Phase G.1 - stock-status filter (separate from active/inactive)
  stockStatusFilter = '';
  stockStatusOptions = [
    { value: '', label: 'All' },
    { value: 'BELOW_REORDER', label: 'Below Reorder' },
    { value: 'ABOVE_MAX', label: 'Above Max' },
    { value: 'OK', label: 'OK (within levels)' },
    { value: 'NO_LEVELS', label: 'No Levels Set' }
  ];

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION — multi-select using a Set<number>.
  //  Only MATERIAL_ISSUE OUT rows are eligible for bulk reverse, but
  //  the checkbox is rendered for every row; the bulk-reverse backend
  //  filters and reports per-row failures for non-eligible ones so
  //  the operator gets a clear message instead of silent skips.
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT TIMELINE — lazy-loaded after the detail drawer
  //  opens. Empty array when the audit table isn't migrated yet or
  //  the row has no recorded events.
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;

  // Phase G.1 - Set Levels modal state
  showLevelsForm = false;
  levelsSaving = false;
  formLevels: {
    itemId: number | null;
    itemName: string;
    branchName: string;
    unit: string;
    currentBalance: number;
    reorderLevel: number | null;
    maxLevel: number | null;
    reorderQty: number | null;
  } = this.createEmptyLevels();

  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 0;

  showOpeningForm = false;
  showIssueForm = false;

  formOpening: StoreEntryDto = this.createEmptyStoreEntry();
  formIssue: MaterialIssueFormUI = this.createEmptyMaterialIssueForm();

  constructor(private service: GenStoreService) { }

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadAll();
  }

  private emptyDashboard(): StoreDashboardViewModel {
    return {
      openingQty: 0,
      totalIn: 0,
      totalOut: 0,
      currentBalance: 0,
      distinctItems: 0,
      branches: 0,
      totalStockValue: 0
    };
  }

  loadAll(): void {
    this.loadList();
  }

  loadDropdowns(): void {
    this.service.getRawItems().subscribe({
      next: res => this.rawItems = res || [],
      error: () => this.rawItems = []
    });

    this.service.getBranches().subscribe({
      next: res => this.branches = res || [],
      error: () => this.branches = []
    });

    this.loadWorkOrders();
  }

  loadWorkOrders(): void {
    const branchId = this.formIssue?.branchId ?? this.branchId ?? null;

    this.service.getWorkOrders(branchId).subscribe({
      next: res => this.workOrders = res || [],
      error: () => this.workOrders = []
    });
  }

  loadIssuedWorkOrders(): void {
    this.service.getAllWorkOrders().subscribe({
      next: res => this.issuedWorkOrders = res || [],
      error: () => this.issuedWorkOrders = []
    });
  }

  loadList(): void {
    this.loading = true;

    this.service.list({
      search: this.search,
      branchId: this.branchId,
      workOrderId: this.issuedSearchWorkOrderId,
      page: this.page,
      pageSize: this.pageSize,
      activeFilter: this.activeFilter,
      fromDate: this.fromDate || null,
      toDate: this.toDate || null,
      referenceType: this.referenceType || null,
      stockStatus: this.stockStatusFilter || null
    }).subscribe({
      next: (res: any) => {
        this.loading = false;

        const success = res?.success ?? res?.Success ?? false;
        const data: any = res?.data ?? res?.Data ?? {};

        if (!success) {
          this.rows = [];
          this.itemBalances = [];
          this.totalItems = 0;
          this.totalPages = 0;
          alert(res?.message ?? res?.Message ?? 'Failed to load store list.');
          return;
        }

        const rawRows: any[] =
          data.transactions ??
          data.Transactions ??
          data.items ??
          data.Items ??
          [];

        this.rows = Array.isArray(rawRows)
          ? rawRows.map((x: any) => ({
            ...x,
            id: Number(x.id ?? x.Id ?? 0),
            itemId: Number(x.itemId ?? x.ItemId ?? 0),
            itemName: x.itemName ?? x.ItemName ?? '-',

            branchId: this.toNullableNumber(x.branchId ?? x.BranchId),
            branchName: x.branchName ?? x.BranchName ?? '-',

            warehouseId: this.toNullableNumber(x.warehouseId ?? x.WarehouseId),
            warehouseName: x.warehouseName ?? x.WarehouseName ?? '',

            transactionDate: this.parseApiDate(
              x.transactionDate ??
              x.TransactionDate ??
              x.createdDate ??
              x.CreatedDate
            ),

            transactionType: x.transactionType ?? x.TransactionType ?? '',
            referenceType: x.referenceType ?? x.ReferenceType ?? '',
            referenceId: this.toNullableNumber(x.referenceId ?? x.ReferenceId),
            referenceNo: x.referenceNo ?? x.ReferenceNo ?? '',

            openingQty: Number(x.openingQty ?? x.OpeningQty ?? 0),
            inQty: Number(x.inQty ?? x.InQty ?? 0),
            outQty: Number(x.outQty ?? x.OutQty ?? 0),

            currentQty: Number(
              x.currentQty ??
              x.CurrentQty ??
              (
                Number(x.openingQty ?? x.OpeningQty ?? 0) +
                Number(x.inQty ?? x.InQty ?? 0) -
                Number(x.outQty ?? x.OutQty ?? 0)
              )
            ),

            unit: x.unit ?? x.Unit ?? 'PCS',
            rate: Number(x.rate ?? x.Rate ?? 0),
            amount: Number(x.amount ?? x.Amount ?? 0),

            remarks: x.remarks ?? x.Remarks ?? '',
            active: Boolean(x.active ?? x.Active ?? false),
            deleted: Boolean(x.deleted ?? x.Deleted ?? false),

            createdDate: this.parseApiDate(x.createdDate ?? x.CreatedDate),
            updatedDate: this.parseApiDate(x.updatedDate ?? x.UpdatedDate),

            // Sprint-1 — deep-link slug + recency flag from the backend.
            referenceModule: x.referenceModule ?? x.ReferenceModule ?? null,
            isRecent: (x.isRecent ?? x.IsRecent) === true
          }))
          : [];

        const rawBalances: any[] =
          data.itemBalances ??
          data.ItemBalances ??
          [];

          this.itemBalances = Array.isArray(rawBalances)
          ? rawBalances.map((x: any) => ({
            itemId: Number(x.itemId ?? x.ItemId ?? 0),
            itemName: x.itemName ?? x.ItemName ?? '-',

            branchId: this.toNullableNumber(x.branchId ?? x.BranchId),
            branchName: x.branchName ?? x.BranchName ?? '-',

            openingQty: Number(x.openingQty ?? x.OpeningQty ?? 0),
            totalIn: Number(x.totalIn ?? x.TotalIn ?? 0),
            totalOut: Number(x.totalOut ?? x.TotalOut ?? 0),
            currentBalance: Number(x.currentBalance ?? x.CurrentBalance ?? 0),

            unit: x.unit ?? x.Unit ?? 'PCS',
            status: x.status ?? x.Status ?? '-',

            // Phase G.1
            reorderLevel: this.toNullableNumber(x.reorderLevel ?? x.ReorderLevel),
            maxLevel:     this.toNullableNumber(x.maxLevel ?? x.MaxLevel),
            reorderQty:   this.toNullableNumber(x.reorderQty ?? x.ReorderQty),
            stockStatus:  x.stockStatus ?? x.StockStatus ?? 'NO_LEVELS'
          }))
          : [];

        this.page = Number(data.page ?? data.Page ?? this.page ?? 1);
        this.pageSize = Number(data.pageSize ?? data.PageSize ?? this.pageSize ?? 20);
        this.totalItems = Number(data.totalItems ?? data.TotalItems ?? this.rows.length);
        this.totalPages = Number(data.totalPages ?? data.TotalPages ?? 1);

        // Phase G.0 + G.1 — KPIs from server
        const k = data.kpis ?? data.Kpis ?? null;
        if (k) {
          this.kpis = {
            distinctItems:     Number(k.distinctItems     ?? k.DistinctItems     ?? 0),
            activeItemCount:   Number(k.activeItemCount   ?? k.ActiveItemCount   ?? 0),
            negativeCount:     Number(k.negativeCount     ?? k.NegativeCount     ?? 0),
            zeroCount:         Number(k.zeroCount         ?? k.ZeroCount         ?? 0),
            totalOpening:      Number(k.totalOpening      ?? k.TotalOpening      ?? 0),
            totalIn:           Number(k.totalIn           ?? k.TotalIn           ?? 0),
            totalOut:          Number(k.totalOut          ?? k.TotalOut          ?? 0),
            netBalance:        Number(k.netBalance        ?? k.NetBalance        ?? 0),
            stockValue:        Number(k.stockValue        ?? k.StockValue        ?? 0),
            belowReorderCount: Number(k.belowReorderCount ?? k.BelowReorderCount ?? 0),
            aboveMaxCount:     Number(k.aboveMaxCount     ?? k.AboveMaxCount     ?? 0),
            appliedFromDate:   k.appliedFromDate ?? k.AppliedFromDate ?? null,
            appliedToDate:     k.appliedToDate   ?? k.AppliedToDate   ?? null
          };
        }
      },

      error: () => {
        this.loading = false;
        this.rows = [];
        this.itemBalances = [];
        this.totalItems = 0;
        this.totalPages = 0;
        alert('Failed to load store list.');
      }
    });
  }

  private toNullableNumber(value: any): number | null {
    const n = Number(value);
    return Number.isFinite(n) && n > 0 ? n : null;
  }

  loadIssuedMaterials(): void {
    if (!this.issuedSearchWorkOrderId) {
      alert('Select Work Order.');
      return;
    }

    this.service.getIssuedMaterialsByWorkOrder(this.issuedSearchWorkOrderId)
      .subscribe({
        next: res => {
          this.issuedMaterialRows = res?.data ?? res?.Data ?? [];
        },
        error: err => {
          console.error(err);
          alert('Failed to load issued material.');
        }
      });
  }

  onSearch(): void {
    this.page = 1;
    this.loadIssuedWorkOrders();
    this.loadAll();
  }

  resetFilters(): void {
    this.search = '';
    this.branchId = null;
    this.activeFilter = '';
    this.issuedSearchWorkOrderId = null;
    this.fromDate = '';
    this.toDate = '';
    this.referenceType = '';
    this.stockStatusFilter = '';
    this.page = 1;
    this.loadAll();
  }

  onFiltersChange(): void {
    this.page = 1;
    this.loadList();
  }

  changePage(p: number): void {
    if (p < 1 || p > this.totalPages) return;
    this.page = p;
    this.loadList();
  }

  openItemHistory(item: StoreItemBalanceViewModel): void {
    this.historyItem = item;
    this.historyOpen = true;
    this.historyLoading = true;
    this.historyRows = [];

    this.service.itemHistory(item.itemId, item.branchId).subscribe({
      next: (res: any) => {
        this.historyLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        const data: any = res?.data ?? res?.Data;
        if (!ok || !data) {
          alert(res?.message ?? 'Failed to load item history.');
          return;
        }
        const ledger: any[] = data.ledger ?? data.Ledger ?? [];
        this.historyRows = ledger.map((r: any) => ({
          id: Number(r.id ?? r.Id ?? 0),
          dateText: r.dateText ?? r.DateText ?? this.formatApiDate(r.transactionDate ?? r.TransactionDate),
          transactionType: r.transactionType ?? r.TransactionType ?? '',
          referenceType: r.referenceType ?? r.ReferenceType ?? '',
          referenceNo: r.referenceNo ?? r.ReferenceNo ?? '',
          inQty: Number(r.inQty ?? r.InQty ?? 0),
          outQty: Number(r.outQty ?? r.OutQty ?? 0),
          runningBalance: Number(r.balance ?? r.Balance ?? 0),
          rate: Number(r.rate ?? r.Rate ?? 0),
          amount: Number(r.amount ?? r.Amount ?? 0),
          remarks: r.remarks ?? r.Remarks ?? ''
        }));
      },
      error: err => {
        this.historyLoading = false;
        console.error(err);
        alert(err?.error?.message ?? 'Failed to load item history.');
      }
    });
  }

  private formatApiDate(value: any): string {
    if (!value) return '-';
    const ms = /\/Date\((\d+)\)\//.exec(String(value));
    const d = ms ? new Date(Number(ms[1])) : new Date(value);
    if (isNaN(d.getTime())) return '-';
    const day = String(d.getDate()).padStart(2, '0');
    const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
    return `${day}-${months[d.getMonth()]}-${d.getFullYear()}`;
  }

  closeItemHistory(): void {
    this.historyOpen = false;
    this.historyItem = null;
    this.historyRows = [];
  }

  get historyTotalIn(): number {
    return this.historyRows.reduce((s, r) => s + Number(r.inQty ?? 0), 0);
  }

  get historyTotalOut(): number {
    return this.historyRows.reduce((s, r) => s + Number(r.outQty ?? 0), 0);
  }

  viewDetail(id: number): void {
    this.service.detail(id).subscribe({
      next: res => {
        if (res.success) {
          this.detailModel = res.data;
          // Sprint-2: lazy-load the audit timeline into the drawer so
          // the operator sees who created / edited / reversed this row.
          this.loadAuditLog(id);
        } else {
          alert(res.message || 'Failed to load detail.');
        }
      },
      error: () => alert('Failed to load detail.')
    });
  }

  // ───────── Material Issue: Reverse / Edit ─────────
  //
  // An issue row in Gen_Store is an immutable ledger entry. To "edit" we
  // reverse it (which writes a compensating IN row + soft-deletes the OUT
  // row, restoring stock) and then create a fresh issue with the corrected
  // values. This preserves a full audit trail.

  /** True when this row is a Material Issue OUT that can still be reversed. */
  canReverseIssue(row: any): boolean {
    if (!row) return false;
    if ((row.transactionType || '').toUpperCase() !== 'OUT') return false;
    if ((row.referenceType || '').toUpperCase() !== 'MATERIAL_ISSUE') return false;
    if (!row.referenceNo) return false;
    // If the row was already reversed earlier, its remarks carry the tag.
    const remarks = (row.remarks || '').toString().toUpperCase();
    if (remarks.includes('[REVERSED')) return false;
    return true;
  }

  /** Build a human-readable error message from whatever the backend returned. */
  private describeReverseError(r: any, e: any): string {
    const fromSuccess: string = (r && (r.message || r.Message)) || '';
    const fromError: string =
      (e && e.error && (e.error.message || e.error.Message)) ||
      (e && e.message) ||
      '';
    const status: string = e && e.status ? ` (HTTP ${e.status})` : '';
    const raw = fromSuccess || fromError;
    if (!raw) {
      return 'Reverse failed' + status + '. The server did not return a message. ' +
             'Check the browser console for the raw response.';
    }
    // Common case: the IssueCode is shared by earlier sessions (e.g. all your
    // pre-fix issues used ReferenceNo "xxx"), so the first reverse cleared every
    // row tagged that way. Make this hint actionable.
    if (/not found|already reversed/i.test(raw)) {
      return raw +
             '\n\nThis usually means every row sharing this Reference No was ' +
             'already reversed in an earlier click — Material Issues from BEFORE ' +
             'the unique-IssueCode fix all shared the same Reference. New issues ' +
             '(MI-YYYY-NNNN) each get a unique code and can be reversed individually.';
    }
    return raw;
  }

  /** Reverse the issue identified by this row's IssueCode. */
  reverseIssueRow(row: any): void {
    if (!this.canReverseIssue(row)) return;
    const issueCode = row.referenceNo;
    const ok = confirm(
      `Reverse issue ${issueCode}?\n\n` +
      `All items in this issue will be returned to stock. The ledger keeps ` +
      `the original OUT row tagged [REVERSED] plus a new compensating IN row, ` +
      `so the audit trail is preserved. After reversal you can re-issue with corrected values.`
    );
    if (!ok) return;

    this.service.reverseMaterialIssue(issueCode).subscribe({
      next: r => {
        if (!r?.success) {
          console.warn('Reverse response (not success):', r);
          alert(this.describeReverseError(r, null));
          return;
        }
        alert(`Issue ${issueCode} reversed. Stock restored.`);
        this.loadList();
      },
      error: e => {
        console.error('Reverse request errored:', e);
        alert(this.describeReverseError(null, e));
      }
    });
  }

  /**
   * Edit = reverse + re-issue.
   * Reverse the existing issue, then open the Material Issue form pre-filled
   * with the same Work Order so the user can re-enter corrected quantities.
   */
  editIssueRow(row: any): void {
    if (!this.canReverseIssue(row)) return;
    const issueCode = row.referenceNo;
    const workOrderId = row.referenceId;
    const ok = confirm(
      `Edit issue ${issueCode}?\n\n` +
      `This will REVERSE the existing issue (restoring all items to stock) and ` +
      `re-open the Material Issue form for the same Work Order, so you can ` +
      `re-enter the corrected quantities and Save as a new issue.\n\n` +
      `Cancel here if you'd rather only reverse without re-issuing.`
    );
    if (!ok) return;

    this.service.reverseMaterialIssue(issueCode).subscribe({
      next: r => {
        if (!r?.success) {
          console.warn('Reverse response (not success):', r);
          alert(this.describeReverseError(r, null));
          return;
        }
        // Open the Material Issue form pre-selected to the same WO.
        this.formIssue = this.createEmptyMaterialIssueForm();
        this.formIssue.branchId = this.branchId;
        this.formIssue.transactionDate = this.todayLocal();
        if (workOrderId) this.formIssue.workOrderId = workOrderId;
        this.issueMaterialRows = [];
        this.showIssueForm = true;

        // Re-pull the requirements grid for that WO so the user sees Required
        // / Available / Pending values pre-loaded.
        if (workOrderId) this.loadWorkOrderMaterials();

        // Refresh the ledger in the background so the reversal is visible
        // once the user closes the issue form.
        this.loadList();
      },
      error: e => {
        console.error('Reverse request errored:', e);
        alert(this.describeReverseError(null, e));
      }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  CLICKABLE BADGES
  //  Resolve a ledger row's Reference column into a deep-link URL of
  //  the source module. The backend ships `referenceModule` (the
  //  Angular route slug) so the mapping isn't duplicated in TS. We
  //  pass the ledger row's referenceNo as a search query so the
  //  target module's list scrolls / focuses on the linked record.
  // ──────────────────────────────────────────────────────────────────
  gotoReference(row: any): void {
    if (!row?.referenceModule) return;
    const search = encodeURIComponent(row.referenceNo || '');
    location.href = `/Content/emb-products/${row.referenceModule}?search=${search}`;
  }

  /** Sprint-1: clickable item name → raw-items master with id pre-set. */
  gotoItem(row: any): void {
    if (!row?.itemId) return;
    location.href = `/Content/emb-products/raw-items?id=${row.itemId}`;
  }

  /** Sprint-1: clickable branch name → branches master. */
  gotoBranch(row: any): void {
    if (!row?.branchId) return;
    location.href = `/Content/emb-products/branches?id=${row.branchId}`;
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
    return this.rows.length > 0
      && this.rows.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) {
      this.rows.forEach((r: any) => this.selectedIds.delete(r.id));
    } else {
      this.rows.forEach((r: any) => this.selectedIds.add(r.id));
    }
  }
  clearSelection(): void { this.selectedIds.clear(); }

  /** Sprint-3: bulk reverse selected MATERIAL_ISSUE OUT rows. */
  bulkReverse(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(
      `Reverse ${ids.length} selected ledger row(s)?\n\n` +
      `Only MATERIAL_ISSUE OUT rows are eligible; ineligible rows are reported back per-row.`
    )) return;
    this.bulkBusy = true;
    this.service.bulkReverse(ids).subscribe({
      next: (res: any) => {
        this.bulkBusy = false;
        const d = res?.data ?? {};
        const ok = Number(d.successCount || 0);
        const failed = Number(d.failureCount || 0);
        let msg = `${ok} reversed, ${failed} failed.`;
        if (failed > 0 && Array.isArray(d.failures)) {
          msg += '\n\nFailures:\n' + d.failures.slice(0, 10)
            .map((f: any) => `  • #${f.id} ${f.code}: ${f.reason}`).join('\n');
          if (d.failures.length > 10) msg += `\n  …and ${d.failures.length - 10} more.`;
        }
        alert(msg);
        this.clearSelection();
        this.loadList();
      },
      error: (e: any) => {
        this.bulkBusy = false;
        alert(e?.error?.message || e?.message || 'Bulk reverse failed.');
      }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG
  //  Best-effort fetch — if the table doesn't exist or the endpoint
  //  errors, the timeline section just renders empty instead of
  //  blocking the detail drawer.
  // ──────────────────────────────────────────────────────────────────
  loadAuditLog(storeId: number): void {
    if (!storeId) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.service.getAuditLog(storeId).subscribe({
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

  /** Map an audit action to a chip class — green for creates, amber
   *  for edits, red for reversals / deletions, blue for everything
   *  else so the timeline reads at a glance. */
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')                 return 'bg-primary';
    if (a === 'EDITED')                  return 'bg-warning text-dark';
    if (a === 'REVERSED' || a === 'SOFT_DELETED') return 'bg-danger';
    return 'bg-secondary';
  }

  openOpening(): void {
    this.formOpening = this.createEmptyStoreEntry();
    this.formOpening.transactionDate = this.todayLocal();
    this.showOpeningForm = true;
  }
 
  openIssue(): void {
    this.formIssue = this.createEmptyMaterialIssueForm();
    this.formIssue.branchId = this.branchId;
    this.formIssue.transactionDate = this.todayLocal();
    this.issueMaterialRows = [];
    this.showIssueForm = true;
  }

  // ===================== Phase G.1 - Set Reorder Levels =====================
  private createEmptyLevels() {
    return {
      itemId: null,
      itemName: '',
      branchName: '',
      unit: '',
      currentBalance: 0,
      reorderLevel: null,
      maxLevel: null,
      reorderQty: null
    };
  }

  openLevels(b: StoreItemBalanceViewModel): void {
    this.formLevels = {
      itemId: b.itemId,
      itemName: b.itemName,
      branchName: b.branchName,
      unit: b.unit,
      currentBalance: Number(b.currentBalance || 0),
      reorderLevel: b.reorderLevel ?? null,
      maxLevel:     b.maxLevel ?? null,
      reorderQty:   b.reorderQty ?? null
    };
    this.showLevelsForm = true;
  }

  // Live status preview based on what the user has typed
  get levelsPreviewStatus(): string {
    const f = this.formLevels;
    const rl = f.reorderLevel != null ? Number(f.reorderLevel) : null;
    const ml = f.maxLevel != null ? Number(f.maxLevel) : null;
    const bal = Number(f.currentBalance || 0);

    if (rl == null && ml == null) return 'NO_LEVELS';
    if (rl != null && bal < rl)   return 'BELOW_REORDER';
    if (ml != null && bal > ml)   return 'ABOVE_MAX';
    return 'OK';
  }

  closeLevels(): void {
    this.showLevelsForm = false;
  }

  saveLevels(): void {
    if (this.levelsSaving) return;
    if (!this.formLevels.itemId) { alert('Item required.'); return; }

    const rl = this.formLevels.reorderLevel;
    const ml = this.formLevels.maxLevel;
    if (rl != null && ml != null && Number(ml) < Number(rl)) {
      alert('Max level cannot be below the reorder level.');
      return;
    }

    this.levelsSaving = true;
    this.service.setLevels(Number(this.formLevels.itemId), {
      reorderLevel: this.formLevels.reorderLevel,
      maxLevel:     this.formLevels.maxLevel,
      reorderQty:   this.formLevels.reorderQty
    }).subscribe({
      next: res => {
        this.levelsSaving = false;
        if (res.success) {
          this.showLevelsForm = false;
          this.loadAll();
        } else {
          alert(res.message || 'Failed to save levels.');
        }
      },
      error: err => {
        this.levelsSaving = false;
        console.error(err);
        alert(err?.error?.message || 'Failed to save levels.');
      }
    });
  }

  stockStatusBadgeClass(s: string): string {
    if (s === 'BELOW_REORDER') return 'bg-danger-subtle text-danger-emphasis border border-danger';
    if (s === 'ABOVE_MAX')     return 'bg-warning-subtle text-warning-emphasis border border-warning';
    if (s === 'OK')            return 'bg-success-subtle text-success-emphasis border border-success';
    return 'bg-light text-secondary border';
  }

  stockStatusLabel(s: string): string {
    if (s === 'BELOW_REORDER') return 'Below Reorder';
    if (s === 'ABOVE_MAX')     return 'Above Max';
    if (s === 'OK')            return 'OK';
    return 'No Levels';
  }

  rowTintClass(b: StoreItemBalanceViewModel): string {
    if (b.stockStatus === 'BELOW_REORDER') return 'table-danger-soft';
    if (b.stockStatus === 'ABOVE_MAX')     return 'table-warning-soft';
    if (b.currentBalance < 0)              return 'table-danger-soft';
    if (b.currentBalance === 0)            return 'table-muted-soft';
    return '';
  }

  // ===================== Phase G.2 - Branch Transfer =====================
  private createEmptyTransfer() {
    return {
      itemId: null,
      fromBranchId: null,
      toBranchId: null,
      qty: 0,
      transactionDate: '',
      rate: null,
      remarks: ''
    };
  }

  openTransfer(): void {
    this.formTransfer = this.createEmptyTransfer();
    this.formTransfer.transactionDate = this.todayLocal();
    this.formTransfer.fromBranchId = this.branchId;
    this.showTransferForm = true;
  }

  closeTransfer(): void {
    this.showTransferForm = false;
  }

  saveTransfer(): void {
    if (this.saving) return;

    const f = this.formTransfer;

    if (!f.itemId || f.itemId <= 0) { alert('Item is required.'); return; }
    if (!f.fromBranchId || f.fromBranchId <= 0) { alert('Source branch is required.'); return; }
    if (!f.toBranchId || f.toBranchId <= 0) { alert('Destination branch is required.'); return; }
    if (f.fromBranchId === f.toBranchId) { alert('Source and destination must be different.'); return; }
    if (!f.qty || f.qty <= 0) { alert('Quantity must be greater than zero.'); return; }

    this.saving = true;

    this.service.transfer({
      itemId: Number(f.itemId),
      fromBranchId: Number(f.fromBranchId),
      toBranchId: Number(f.toBranchId),
      qty: Number(f.qty),
      transactionDate: f.transactionDate || null,
      rate: f.rate ?? null,
      remarks: (f.remarks || '').trim()
    }).subscribe({
      next: res => {
        this.saving = false;
        if (res.success) {
          this.showTransferForm = false;
          alert(res.message || 'Branch transfer posted.');
          this.loadAll();
        } else {
          alert(res.message || 'Failed to post transfer.');
        }
      },
      error: err => {
        this.saving = false;
        console.error(err);
        alert(err?.error?.message || 'Failed to post transfer.');
      }
    });
  }

  // ===================== Phase G.3 - Stock Adjustment =====================
  private createEmptyAdjust() {
    return {
      itemId: null,
      branchId: null,
      sign: 'IN' as 'IN' | 'OUT',
      qty: 0,
      reason: 'DAMAGE',
      transactionDate: '',
      rate: null,
      remarks: ''
    };
  }

  openAdjust(): void {
    this.formAdjust = this.createEmptyAdjust();
    this.formAdjust.transactionDate = this.todayLocal();
    this.formAdjust.branchId = this.branchId;
    this.showAdjustForm = true;
  }

  closeAdjust(): void {
    this.showAdjustForm = false;
  }

  // Live preview of current and post-adjustment balance for the chosen (item, branch).
  get adjustCurrentBalance(): number {
    const f = this.formAdjust;
    if (!f.itemId || !f.branchId) return 0;
    const match = this.itemBalances.find(
      b => Number(b.itemId) === Number(f.itemId)
        && Number(b.branchId) === Number(f.branchId)
    );
    return match ? Number(match.currentBalance || 0) : 0;
  }

  get adjustAfterBalance(): number {
    const f = this.formAdjust;
    const cur = this.adjustCurrentBalance;
    const q = Number(f.qty || 0);
    if (!q) return cur;
    return f.sign === 'IN' ? cur + q : cur - q;
  }

  get adjustWillGoNegative(): boolean {
    return this.formAdjust.sign === 'OUT' && this.adjustAfterBalance < 0;
  }

  saveAdjust(): void {
    if (this.adjustSaving) return;
    const f = this.formAdjust;

    if (!f.itemId || f.itemId <= 0) { alert('Item is required.'); return; }
    if (!f.branchId || f.branchId <= 0) { alert('Branch is required.'); return; }
    if (!f.qty || f.qty <= 0) { alert('Quantity must be greater than zero.'); return; }
    if (!f.reason) { alert('Please pick a reason.'); return; }

    if (this.adjustWillGoNegative) {
      alert('This OUT adjustment would push the balance negative.');
      return;
    }

    this.adjustSaving = true;
    this.service.adjust({
      itemId: Number(f.itemId),
      branchId: Number(f.branchId),
      sign: f.sign,
      qty: Number(f.qty),
      reason: f.reason,
      transactionDate: f.transactionDate || null,
      rate: f.rate ?? null,
      remarks: (f.remarks || '').trim()
    }).subscribe({
      next: res => {
        this.adjustSaving = false;
        if (res.success) {
          this.showAdjustForm = false;
          alert(res.message || 'Stock adjustment posted.');
          this.loadAll();
        } else {
          alert(res.message || 'Failed to post adjustment.');
        }
      },
      error: err => {
        this.adjustSaving = false;
        console.error(err);
        alert(err?.error?.message || 'Failed to post adjustment.');
      }
    });
  }

  private parseApiDate(value: any): string | null {
    if (!value) return null;

    if (typeof value === 'string') {
      const match = /\/Date\((\d+)\)\//.exec(value);
      if (match) {
        return new Date(Number(match[1])).toISOString();
      }

      return value;
    }

    return null;
  }

  saveOpening(): void {
    if (this.saving) return;

    if (!this.formOpening.itemId || this.formOpening.itemId <= 0) {
      alert('Raw item is required.');
      return;
    }

    if (!this.formOpening.openingQty || this.formOpening.openingQty <= 0) {
      alert('Opening quantity must be greater than zero.');
      return;
    }

    this.saving = true;
    this.service.openingIn(this.formOpening).subscribe({
      next: res => {
        this.saving = false;
        if (res.success) {
          this.showOpeningForm = false;
          alert(res.message || 'Opening stock saved successfully.');
          this.loadAll();
        } else {
          alert(res.message || 'Failed to save opening stock.');
        }
      },
      error: () => {
        this.saving = false;
        alert('Failed to save opening stock.');
      }
    });
  }

  saveIssue(): void {
    if (this.saving) return;

    if (!this.formIssue.workOrderId || this.formIssue.workOrderId <= 0) {
      alert('Work Order is required.');
      return;
    }

    if (!this.formIssue.branchId || this.formIssue.branchId <= 0) {
      alert('Branch is required.');
      return;
    }

    const issuedWorkOrderId = Number(this.formIssue.workOrderId || 0);

    const pendingRows = this.issueMaterialRows.filter(x =>
      Number(x.pendingQty || 0) > 0
    );

    const isCompleteIssue = pendingRows.length > 0 && pendingRows.every(x =>
      Number(x.issueQty || 0) >= Number(x.pendingQty || 0)
    );

    const lines = this.issueMaterialRows
      .filter(x => Number(x.issueQty || 0) > 0)
      .map(x => ({
        itemId: Number(x.itemId || 0),
        itemType: x.itemType || 'RAW',
        issueQty: Number(x.issueQty || 0),
        department: x.department || '',
        rate: Number(x.rate || 0),
        unit: x.unit || 'PCS',
        remarks: x.remarks || ''
      }));

    if (!lines.length) {
      alert('Please enter issue quantity.');
      return;
    }

    const payload: MaterialIssueDto = {
      workOrderId: issuedWorkOrderId,
      branchId: Number(this.formIssue.branchId),
      warehouseId: this.formIssue.warehouseId,
      transactionDate: this.formIssue.transactionDate,
      referenceNo: (this.formIssue.referenceNo || '').trim(),
      remarks: (this.formIssue.remarks || '').trim(),
      items: lines
    };

    this.saving = true;

    this.service.issueAgainstWorkOrder(payload).subscribe({
      next: (res: any) => {
        this.saving = false;

        const ok = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? '';

        if (ok) {
          alert(message || 'Material issued successfully.');

          if (isCompleteIssue && issuedWorkOrderId > 0) {
            this.workOrders = this.workOrders.filter(x =>
              Number(x.id || 0) !== issuedWorkOrderId
            );
          }

          this.showIssueForm = false;
          this.issueMaterialRows = [];
          this.formIssue = this.createEmptyMaterialIssueForm();

          this.loadAll();
        } else {
          alert(message || 'Failed to issue material.');
        }
      },
      error: err => {
        this.saving = false;
        console.error(err);
        alert(err?.error?.message || 'Failed to issue material.');
      }
    });
  }

  getPageNumbers(): number[] {
    const pages: number[] = [];
    for (let i = 1; i <= this.totalPages; i++) {
      pages.push(i);
    }
    return pages;
  }

  private createEmptyStoreEntry(): any {
    return {
      workOrderId: 0,
      itemId: 0,
      branchId: null,
      warehouseId: null,
      transactionDate: null,
      transactionType: '',
      referenceType: '',
      referenceId: null,
      referenceNo: '',
      openingQty: null,
      inQty: null,
      outQty: null,
      unit: '',
      rate: null,
      remarks: '',
      active: true
    };
  }

  private todayLocal(): string {
    const d = new Date();
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  loadBalance(): void {
    this.loading = true;

    this.service.getBalance(
      this.branchId,
      this.search,
      ''
    ).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (res?.success) {
          const data = res.data || [];

          this.rows = data.map((x: any, index: number) => {
            const balanceQty = Number(x.balanceQty ?? x.BalanceQty ?? 0);
            const rate = Number(x.lastRate ?? x.LastRate ?? 0);

            return {
              id: index + 1,
              transactionDate: new Date().toISOString(),

              itemId: Number(x.itemId ?? x.ItemId ?? 0),
              itemName: x.itemName ?? x.ItemName ?? '-',

              branchId: x.branchId ?? x.BranchId ?? null,
              branchName: x.branchName ?? x.BranchName ?? '-',

              transactionType: 'BALANCE',
              referenceType: 'BALANCE',
              referenceNo: 'As of Today',

              openingQty: Number(x.openingQty ?? x.OpeningQty ?? 0),
              inQty: Number(x.totalIn ?? x.TotalIn ?? 0),
              outQty: Number(x.totalOut ?? x.TotalOut ?? 0),
              currentQty: balanceQty,

              unit: x.unit ?? x.Unit ?? 'PCS',
              rate: rate,
              amount: balanceQty * rate,

              remarks: 'Item-wise balance',
              active: true
            };
          });

          this.totalItems = this.rows.length;
          this.page = 1;
          this.totalPages = 1;
        } else {
          this.rows = [];
          alert(res?.message || 'Failed to load balance.');
        }
      },
      error: err => {
        this.loading = false;
        this.rows = [];
        console.error(err);
        alert('Failed to load balance.');
      }
    });
  }

  loadWorkOrderMaterials(): void {
    if (!this.formIssue.workOrderId || this.formIssue.workOrderId <= 0) {
      this.issueMaterialRows = [];
      return;
    }

    this.service.getWorkOrderMaterials(
      this.formIssue.workOrderId,
      this.formIssue.branchId
    ).subscribe({
      next: res => {
        if (res.success) {
          this.issueMaterialRows = res.data || [];
        } else {
          this.issueMaterialRows = [];
          alert(res.message || 'Failed to load materials.');
        }
      },
      error: err => {
        console.error(err);
        this.issueMaterialRows = [];
        alert('Failed to load work order materials.');
      }
    });
  }

  private createEmptyMaterialIssueForm(): MaterialIssueFormUI {
    return {
      workOrderId: 0,
      branchId: null,
      warehouseId: null,
      transactionDate: null,
      referenceNo: '',
      remarks: ''
    };
  }

  onBranchChange(): void {
    this.issueMaterialRows = [];

    if (this.formIssue.workOrderId && this.formIssue.workOrderId > 0) {
      this.loadWorkOrderMaterials();
    }
  }

  onWorkOrderChange(): void {
    if (!this.formIssue.workOrderId || this.formIssue.workOrderId <= 0) {
      this.issueMaterialRows = [];
      return;
    }

    this.loadWorkOrderMaterials();
  }

  isOverallBalancePositive(): boolean {
    return Number(this.dashboard?.currentBalance ?? 0) > 0;
  }

  isOverallBalanceNegative(): boolean {
    return Number(this.dashboard?.currentBalance ?? 0) < 0;
  }

  isOverallBalanceZero(): boolean {
    return Number(this.dashboard?.currentBalance ?? 0) === 0;
  }

  isBalancePositive(value: any): boolean {
    return Number(value ?? 0) > 0;
  }

  isBalanceNegative(value: any): boolean {
    return Number(value ?? 0) < 0;
  }

  isBalanceZero(value: any): boolean {
    return Number(value ?? 0) === 0;
  }

  private buildDashboardFromRows(rows: StoreRowViewModel[]): StoreDashboardViewModel {
    return {
      openingQty: rows.reduce((s, x) => s + Number(x.openingQty ?? 0), 0),
      totalIn: rows.reduce((s, x) => s + Number(x.inQty ?? 0), 0),
      totalOut: rows.reduce((s, x) => s + Number(x.outQty ?? 0), 0),
      currentBalance: rows.reduce(
        (s, x) => s + Number(x.openingQty ?? 0) + Number(x.inQty ?? 0) - Number(x.outQty ?? 0),
        0
      ),
      distinctItems: new Set(rows.map(x => x.itemId).filter(x => x > 0)).size,
      branches: new Set(rows.map(x => x.branchId).filter(x => x != null)).size,
      totalStockValue: rows.reduce((s, x) => s + Number(x.amount ?? 0), 0)
    };
  }

  private buildItemBalancesFromRows(rows: StoreRowViewModel[]): StoreItemBalanceViewModel[] {
    const map = new Map<string, StoreItemBalanceViewModel>();

    rows.forEach(x => {
      const key = `${x.itemId || 0}_${x.branchId || 0}_${x.unit || 'PCS'}`;

      if (!map.has(key)) {
        map.set(key, {
          itemId: x.itemId,
          itemName: x.itemName || '-',
          branchId: x.branchId,
          branchName: x.branchName || '-',
          openingQty: 0,
          totalIn: 0,
          totalOut: 0,
          currentBalance: 0,
          unit: x.unit || 'PCS',
          status: 'Empty'
        });
      }

      const r = map.get(key)!;

      r.openingQty += Number(x.openingQty ?? 0);
      r.totalIn += Number(x.inQty ?? 0);
      r.totalOut += Number(x.outQty ?? 0);
      r.currentBalance = r.openingQty + r.totalIn - r.totalOut;

      r.status =
        r.currentBalance < 0
          ? 'Negative'
          : r.currentBalance === 0
            ? 'Empty'
            : 'Available';
    });

    return Array.from(map.values())
      .sort((a, b) =>
        (a.itemName || '').localeCompare(b.itemName || '') ||
        (a.branchName || '').localeCompare(b.branchName || '')
      );
  }
}
