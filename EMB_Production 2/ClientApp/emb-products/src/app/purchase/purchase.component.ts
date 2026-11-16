import { Component, ViewChild, ElementRef, OnInit, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink, ActivatedRoute } from '@angular/router';

import {
  PurchaseRowViewModel,
  PurchaseDetailViewModel,
  PurchaseDetailItemViewModel,
  PurchaseLedgerEntryViewModel
} from '../shared/models/purchase-row.viewdto';

import { PurchaseService } from '../services/purchase.service';
import { PurchaseDto } from '../shared/models/purchase.dto';
import { ToastService } from '../shared/services/toast.service';

declare const bootstrap: any;

// ===================== UI MODELS =====================
type PurchaseItemType = 'RAW' | 'THREAD' | 'FABRIC';

interface PurchaseItemUI {
  itemId: number | null;
  itemType: PurchaseItemType;
  rawItem: any | null;       // selected Gen_RawItems object when itemType === 'RAW'
  threadItem: any | null;    // selected Gen_Threads object when itemType === 'THREAD'
  fabricItem: any | null;    // selected Gen_RawItems (Type='FABRIC') when itemType === 'FABRIC'
  qty: number;
  rate: number;
  remarks?: string | null;
  // Set when this line was pre-filled from a Purchase Order. Saved
  // back via PurchaseDto.items[i].poItemId so PurchaseOrderItems
  // ReceivedQty rollup can find the parent line.
  poItemId?: number | null;
}

interface PurchaseFormUI {
  id?: number | null;
  purchaseDate: string;
  invoiceNo: string;
  supplierId: number | null;
  branchId: number | null;
  remarks: string;
  items: PurchaseItemUI[];
  // Optional PO header link. Set when modal was opened via ?poId=N
  // — drives /sync-receipts on the PO after a successful save.
  purchaseOrderId?: number | null;
  // Display-only: PO code shown in the banner above the items grid.
  poCode?: string | null;
}

@Component({
  selector: 'app-purchase',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './purchase.component.html',
  styleUrls: ['./purchase.component.css']
})
export class PurchaseComponent implements OnInit, AfterViewInit {
  // ===================== DATA =====================
  rows: PurchaseRowViewModel[] = [];
  previewItems: any[] = [];

  // Open POs available to receive against. Loaded on init and refreshed
  // after a GRN save so a fully-received PO drops off the picker.
  openPos: Array<{
    id: number;
    poCode: string;
    supplierName: string;
    orderDate: string;
    status: string;
    totalAmount: number;
    receivedAmount: number;
    outstanding: number;
  }> = [];

  branches: any[] = [];
  rawItems: any[] = [];
  threads: any[] = [];
  fabrics: any[] = [];
  suppliers: any[] = [];

  searchText = '';

  // Phase F.0 filters
  filterFromDate = '';
  filterToDate = '';
  filterSupplierId: number | null = null;
  filterStatus = '';
  filterItemSearch = '';

  summary = {
    count: 0,
    totalQty: 0,
    totalAmount: 0,
    postedCount: 0,
    draftCount: 0,
    // Sprint-1: count of reversed GRNs in the current filter window.
    // Drives the "Reversed: N" pill above the table.
    reversedCount: 0
  };

  detail: PurchaseDetailViewModel | null = null;

  // Phase F.2 - print voucher flag (renders the print-only template)
  printingVoucher = false;
  duplicating = false;

  // Phase F.3 - analytics
  analyticsLoading = false;
  analytics: {
    topSuppliers: Array<{ supplierId: number; supplierName: string; purchaseCount: number; totalAmount: number }>;
    topItems: Array<{ itemName: string; itemType: string; totalQty: number; totalAmount: number }>;
    monthlyTrend: Array<{ month: string; count: number; amount: number }>;
  } = { topSuppliers: [], topItems: [], monthlyTrend: [] };

  // ===================== FORM =====================
  form: PurchaseFormUI = this.createEmptyFormUI();
  modalMode: 'create' | 'edit' = 'create';

  selectedRow: any | null = null;
  deleteTarget: any | null = null;

  // ===================== MODALS =====================
  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;
  @ViewChild('postModal', { static: false }) postModal?: ElementRef;
  @ViewChild('returnModal') returnModal!: ElementRef;

  private formModalRef: any;
  private detailModalRef: any;
  private deleteModalRef: any;
  private postModalRef: any;
  private returnModalRef: any;

  // ===================== RETURN STATE =====================
  returnTargetId: number | null = null;
  returnInfo: any = null;
  returnReason: string = '';
  returnLoading = false;
  returnSaving = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT TIMELINE + WORKFLOW STEPPER
  //  workflowStages / workflowStageIndex come from the detail payload.
  //  auditLog is loaded lazily after the detail modal opens so the
  //  drawer renders fast and the timeline streams in.
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['DRAFT', 'POSTED', 'REVERSED'];
  workflowStageIndex = 0;
  auditLog: any[] = [];
  auditLogLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK ACTIONS — multi-select using a Set<number>.
  //  Selection resets on filter / search change so the bulk action
  //  always operates on what the user can see.
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  CLONE PREVIEW STATE
  //  When set, the form modal is showing an unsaved clone preview.
  //  Save Clone → POST /clone/{sourceId} with overrides; closing
  //  without saving abandons the preview (no DB write).
  // ──────────────────────────────────────────────────────────────────
  cloneSourceId: number | null = null;
  cloneSourceInvoice = '';
  /** Map of source PurchaseItem.Id → its row in form.items[]. Lets
   *  the clone Save build per-line overrides keyed by SourceItemId. */
  cloneSourceItemIds: number[] = [];
  cloningSaving = false;

  constructor(
    private purchaseSvc: PurchaseService,
    private route: ActivatedRoute,
    private toast: ToastService
  ) { }

  // ===================== LIFE CYCLE =====================
  // Resolves once ALL dropdown observables (branches, suppliers,
  // rawItems, threads, fabrics) complete. The ?poId= prefill flow
  // depends on `this.threads` being populated before openFromPo runs;
  // otherwise the THREAD item lookup .find() returns undefined and
  // the dropdown shows the placeholder forever.
  private dropdownsReady!: Promise<void>;

  ngOnInit(): void {
    this.form = this.createEmptyFormUI();
    this.previewItems = [];

    this.dropdownsReady = this.loadDropdowns();
    this.load();
    this.loadOpenPos();

    // Handle ?poId=N — wait until dropdowns are ready, THEN prefill.
    this.route.queryParams.subscribe(qp => {
      const poId = Number(qp['poId']);
      if (poId && poId > 0) {
        this.dropdownsReady.then(() => this.openFromPo(poId));
      }
    });
  }

  ngAfterViewInit(): void {
    if (this.formModal?.nativeElement) {
      this.formModalRef = new bootstrap.Modal(this.formModal.nativeElement);
    }

    if (this.detailModal?.nativeElement) {
      this.detailModalRef = new bootstrap.Modal(this.detailModal.nativeElement);
    }

    if (this.deleteModal?.nativeElement) {
      this.deleteModalRef = new bootstrap.Modal(this.deleteModal.nativeElement);
    }

    if (this.returnModal?.nativeElement) {
      this.returnModalRef = new bootstrap.Modal(this.returnModal.nativeElement);
    }

    if (this.postModal?.nativeElement) {
      this.postModalRef = new bootstrap.Modal(this.postModal.nativeElement);
    }
  }

  // ===================== LOAD LIST =====================
  load(): void {
    this.purchaseSvc.list({
      fromDate: this.filterFromDate || null,
      toDate: this.filterToDate || null,
      supplierId: this.filterSupplierId,
      status: this.filterStatus || null,
      itemSearch: this.filterItemSearch || null,
      invoiceSearch: this.searchText || null
    }).subscribe({
      next: (res: any) => {
        const payload: any = res?.data ?? {};
        const data: any[] = Array.isArray(payload) ? payload : (payload.rows ?? []);
        this.summary = payload.summary ?? this.summary;

        this.rows = data.map((r: any): PurchaseRowViewModel => ({
          id: Number(r.id ?? r.Id ?? 0),

          purchaseDate: r.purchaseDate ?? r.PurchaseDate ?? '',
          purchaseDateText: r.purchaseDateText ?? r.PurchaseDateText ?? '',
          invoiceNo: r.invoiceNo ?? r.InvoiceNo ?? '-',
          hasInvoice: (r.hasInvoice ?? r.HasInvoice) === true,

          branchId: Number(r.branchId ?? r.BranchId ?? 0),
          branchName: r.branchName ?? r.BranchName ?? '-',

          supplierId: r.supplierId ?? r.SupplierId ?? null,
          supplierName: r.supplierName ?? r.SupplierName ?? '-',

          purchaseOrderId: r.purchaseOrderId ?? r.PurchaseOrderId ?? null,
          poCode: r.poCode ?? r.PoCode ?? null,

          itemDisplay:
            (r.itemDisplay ?? r.ItemDisplay ?? '').toString().trim() !== ''
              ? (r.itemDisplay ?? r.ItemDisplay)
              : '-',

          itemTooltip: r.itemTooltip ?? r.ItemTooltip ?? '',
          itemCount: Number(r.itemCount ?? r.ItemCount ?? 0),

          totalQty: Number(r.totalQty ?? r.TotalQty ?? 0),
          totalAmount: Number(r.totalAmount ?? r.TotalAmount ?? 0),

          isPosted: (r.isPosted ?? r.IsPosted) === true,
          active: (r.active ?? r.Active) === true,

          status: r.status ?? r.Status ?? null,
          isReversed: (r.isReversed ?? r.IsReversed) === true
        }));
      },
      error: err => {
        console.error(err);
        this.rows = [];
      }
    });

    this.loadAnalytics();
  }

  // Phase F.3 - fetch analytics strip in parallel; honor current filter window.
  loadAnalytics(): void {
    this.analyticsLoading = true;
    this.purchaseSvc.analytics({
      fromDate: this.filterFromDate || null,
      toDate: this.filterToDate || null,
      supplierId: this.filterSupplierId
    }).subscribe({
      next: (res: any) => {
        this.analyticsLoading = false;
        const d = res?.data ?? {};
        this.analytics = {
          topSuppliers: Array.isArray(d.topSuppliers) ? d.topSuppliers : [],
          topItems:     Array.isArray(d.topItems)     ? d.topItems     : [],
          monthlyTrend: Array.isArray(d.monthlyTrend) ? d.monthlyTrend : []
        };
      },
      error: err => {
        this.analyticsLoading = false;
        console.error(err);
        this.analytics = { topSuppliers: [], topItems: [], monthlyTrend: [] };
      }
    });
  }

  // Phase F.3 - scale a trend amount to a 0-100% height for the mini bar chart.
  trendBarHeight(amount: number): number {
    const max = Math.max(...this.analytics.monthlyTrend.map(m => Number(m.amount || 0)), 1);
    const pct = (Number(amount || 0) / max) * 100;
    return Math.max(2, Math.round(pct));
  }

  // Compact K/M label for the trend chart so the amount is readable at a glance.
  formatTrendAmount(amount: number): string {
    const n = Number(amount || 0);
    if (n === 0) return '0';
    const abs = Math.abs(n);
    if (abs >= 1_000_000) return (n / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1) + 'M';
    if (abs >= 1_000)     return (n / 1_000).toFixed(abs >= 10_000 ? 0 : 1) + 'K';
    return String(Math.round(n));
  }

  // Returns a Promise<void> that resolves once every dropdown has either
  // loaded or errored. Callers that need a populated dropdown before
  // running (e.g. openFromPo + its thread lookup) await this.
  loadDropdowns(): Promise<void> {
    const wrap = <T>(obs: any, onValue: (r: any) => void): Promise<void> =>
      new Promise<void>(resolve => {
        obs.subscribe({
          next: (r: any) => { onValue(r); resolve(); },
          error: (err: any) => { console.error(err); resolve(); }
        });
      });

    const p1 = wrap(this.purchaseSvc.getBranches(),
      r => this.branches = r?.data ?? []);

    const p2 = wrap(this.purchaseSvc.getSuppliers(),
      r => this.suppliers = r?.data ?? []);

    const p3 = wrap(this.purchaseSvc.getRawItems(), r => {
      const data: any[] = r?.data ?? [];

      // Exclude FABRIC and THREAD - they have their own dedicated dropdowns
      this.rawItems = data
        .filter(x => {
          const type = (x.type ?? x.Type ?? '').toString().toUpperCase();
          if (type === 'FABRIC' || type === 'THREAD') return false;
          const text = (x.text ?? x.Text ?? x.name ?? x.Name ?? x.itemName ?? x.ItemName ?? '').toString().toUpperCase();
          if (text.startsWith('FABRIC -') || text.startsWith('FABRIC-')) return false;
          if (text.startsWith('THREAD -') || text.startsWith('THREAD-')) return false;
          return true;
        })
        .map(x => ({
          id: Number(x.id ?? x.Id ?? 0),
          text: x.text ?? x.Text ?? x.name ?? x.Name ?? x.itemName ?? x.ItemName ?? '',
          rate: Number(x.rate ?? x.Rate ?? 0)
        }));
    });

    const p4 = wrap(this.purchaseSvc.getThreads(), r => {
      const data: any[] = (r?.data as any[]) ?? [];
      this.threads = data.map(x => ({
        id: Number(x.id ?? 0),
        code: x.code ?? '',
        name: x.name ?? '',
        color: x.color ?? '',
        unit: x.unit ?? '',
        rate: Number(x.unitCost ?? 0),
        text: x.text ?? ((x.code ?? '') + ' ' + (x.name ?? '') + ' (' + (x.color ?? '') + ')')
      }));
    });

    const p5 = wrap(this.purchaseSvc.getFabrics(), r => {
      const data: any[] = (r?.data as any[]) ?? [];
      this.fabrics = data.map(x => ({
        id: Number(x.id ?? 0),
        code: x.code ?? '',
        name: x.name ?? '',
        text: x.text ?? ((x.code ?? '') + ' - ' + (x.name ?? ''))
      }));
    });

    return Promise.all([p1, p2, p3, p4, p5]).then(() => { /* all settled */ });
  }

  // ===================== SEARCH / FILTERS =====================
  onSearch(): void {
    this.load();
  }

  onFiltersChange(): void {
    this.load();
  }

  clearFilters(): void {
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterSupplierId = null;
    this.filterStatus = '';
    this.filterItemSearch = '';
    this.searchText = '';
    this.load();
  }

  refresh(): void {
    this.searchText = '';
    this.load();
  }

  statusOf(row: PurchaseRowViewModel): 'Draft' | 'Posted' {
    return row.isPosted ? 'Posted' : 'Draft';
  }

  // ===================== FORM HELPERS =====================
  private createEmptyFormUI(): PurchaseFormUI {
    return {
      id: null,
      purchaseDate: new Date().toISOString().substring(0, 10),
      branchId: null,
      supplierId: null,
      invoiceNo: '',
      remarks: '',
      items: []
    };
  }

  private uiToDtoPayload(form: PurchaseFormUI): PurchaseDto {
    return {
      id: form.id ?? null,
      purchaseDate: form.purchaseDate,
      branchId: Number(form.branchId ?? 0),
      supplierId: form.supplierId ?? null,
      invoiceNo: form.invoiceNo ?? '',
      remarks: form.remarks ?? '',
      purchaseOrderId: form.purchaseOrderId ?? null,

      items: (form.items || [])
        .filter(x => !!x.itemId && Number(x.qty || 0) > 0)
        .map(x => ({
          itemId: Number(x.itemId),
          itemType: x.itemType || 'RAW',
          qty: Number(x.qty || 0),
          rate: Number(x.rate || 0),
          remarks: x.remarks ?? '',
          poItemId: x.poItemId ?? null
        }))
    };
  }

  // ===================== OPEN CREATE =====================
  openCreate(): void {
    this.modalMode = 'create';
    this.form = this.createEmptyFormUI();
    this.addItem();
    this.formModalRef?.show();
  }

  // Powers the "Receive against PO" dropdown next to the Add button.
  loadOpenPos(): void {
    this.purchaseSvc.openPos().subscribe({
      next: r => { this.openPos = r?.data || []; },
      error: () => { this.openPos = []; }
    });
  }

  // ===================== OPEN FROM PURCHASE ORDER =====================
  // Triggered by Receive Goods button on the PO modal (→ navigates
  // here with ?poId=N). Calls /prefill-from-po/{poId}, populates the
  // form with supplier, branch, items (outstanding qty + last rate),
  // and shows the modal. User can edit qty (partial receipts) and
  // rate (price change) before saving.
  openFromPo(poId: number): void {
    this.purchaseSvc.prefillFromPo(poId).subscribe({
      next: res => {
        const d = res?.data;
        if (!res?.success || !d) {
          this.toast.error(res?.message || 'Could not pre-fill from PO.');
          return;
        }

        this.modalMode = 'create';
        this.form = this.createEmptyFormUI();
        this.form.purchaseOrderId = d.poId;
        this.form.poCode          = d.poCode;
        this.form.purchaseDate    = d.purchaseDate;
        this.form.supplierId      = d.supplierId;
        this.form.branchId        = d.branchId;
        this.form.invoiceNo       = '';
        this.form.remarks         = d.remarks || '';

        // Each PO line maps to a PurchaseItemUI. The form's existing
        // item type machinery (rawItem/threadItem/fabricItem refs) needs
        // the matching dropdown row so the select renders the choice.
        // Use l.itemId — the backend has already swapped Gen_Threads.Id
        // in for thread-backed lines so a simple find() works for every
        // type. l.rawItemId stays the underlying Gen_RawItems.Id.
        this.form.items = (d.items || []).map(l => {
          const rawObj    = l.itemType === 'RAW'
            ? this.rawItems.find(x => Number(x.id ?? x.Id) === l.itemId) ?? null
            : null;
          const threadObj = l.itemType === 'THREAD'
            ? this.threads.find(x => Number(x.id) === l.itemId) ?? null
            : null;
          const fabricObj = l.itemType === 'FABRIC'
            ? this.fabrics.find(x => Number(x.id) === l.itemId) ?? null
            : null;
          return {
            itemId: l.itemId,
            itemType: l.itemType as PurchaseItemType,
            rawItem: rawObj,
            threadItem: threadObj,
            fabricItem: fabricObj,
            qty: Number(l.suggestedQty || 0),
            rate: Number(l.rate || 0),
            remarks: l.remarks || '',
            poItemId: l.poItemId
          };
        });

        if (this.form.items.length === 0) this.addItem();
        this.formModalRef?.show();
      },
      error: e => this.toast.error(e?.error?.message || e?.message || 'Could not pre-fill from PO.')
    });
  }

  // ===================== OPEN EDIT =====================
  openEdit(id: number): void {
    const row = this.rows.find(r => r.id === id);

    if (row?.isPosted) {
      alert('This purchase is already posted and cannot be edited.');
      return;
    }

    this.modalMode = 'edit';

    this.purchaseSvc.detail(id).subscribe({
      next: res => {
        const d: any = res?.data;
        if (!d) {
          alert('Purchase detail not found.');
          return;
        }

        const itemsRaw = d.items ?? d.Items ?? [];

        const uiItems: PurchaseItemUI[] = itemsRaw.map((it: any) => {
          const itemId = Number(it.itemId ?? it.ItemId ?? 0) || null;
          const rawType = (it.itemType ?? it.ItemType ?? 'RAW').toString().toUpperCase();
          const itemType: PurchaseItemType =
            rawType === 'THREAD' ? 'THREAD'
            : rawType === 'FABRIC' ? 'FABRIC'
            : 'RAW';

          const rawObj = itemId && itemType === 'RAW'
            ? this.rawItems.find(x => Number(x.id ?? x.Id) === itemId) ?? null
            : null;
          const threadObj = itemId && itemType === 'THREAD'
            ? this.threads.find(x => Number(x.id) === itemId) ?? null
            : null;
          const fabricObj = itemId && itemType === 'FABRIC'
            ? this.fabrics.find(x => Number(x.id) === itemId) ?? null
            : null;

          return {
            itemId,
            itemType,
            rawItem: rawObj,
            threadItem: threadObj,
            fabricItem: fabricObj,
            qty: Number(it.qty ?? it.Qty ?? 0),
            rate: Number(it.rate ?? it.Rate ?? 0),
            remarks: it.remarks ?? it.Remarks ?? ''
          };
        });

        this.form = {
          id: Number(d.id ?? d.Id ?? id),
          purchaseDate: (d.purchaseDate ?? d.PurchaseDate ?? '').toString().substring(0, 10),
          branchId: Number(d.branchId ?? d.BranchId ?? 0) || null,
          supplierId: d.supplierId ?? d.SupplierId ?? null,
          invoiceNo: d.invoiceNo ?? d.InvoiceNo ?? '',
          remarks: d.remarks ?? d.Remarks ?? '',
          items: uiItems.length ? uiItems : []
        };

        if (!this.form.items.length) {
          this.addItem();
        }

        this.formModalRef?.show();
      },
      error: err => {
        console.error(err);
        alert('Failed to load purchase detail.');
      }
    });
  }

  closeForm(): void {
    // Sprint-3: drop clone-preview state so reopening the modal in a
    // different mode doesn't flash the previous clone's source data.
    this.resetCloneState();
    this.formModalRef?.hide();
  }

  // ===================== ITEMS =====================
  addItem(itemType: PurchaseItemType = 'RAW'): void {
    this.form.items.push({
      itemId: null,
      itemType,
      rawItem: null,
      threadItem: null,
      fabricItem: null,
      qty: 0,
      rate: 0,
      remarks: ''
    });
  }

  removeItem(index: number): void {
    this.form.items.splice(index, 1);
  }

  onItemTypeChange(item: PurchaseItemUI): void {
    item.itemId = null;
    item.rawItem = null;
    item.threadItem = null;
    item.fabricItem = null;
    item.rate = 0;
  }

  onRawItemChange(item: PurchaseItemUI): void {
    if (!item.rawItem) {
      item.itemId = null;
      item.rate = 0;
      return;
    }
    item.itemId = Number(item.rawItem.id ?? item.rawItem.Id ?? 0) || null;
    const defaultRate = Number(item.rawItem.rate ?? item.rawItem.Rate ?? 0);
    if (!item.rate || item.rate <= 0) item.rate = defaultRate;
  }

  onThreadItemChange(item: PurchaseItemUI): void {
    if (!item.threadItem) {
      item.itemId = null;
      item.rate = 0;
      return;
    }
    item.itemId = Number(item.threadItem.id ?? 0) || null;
    const defaultRate = Number(item.threadItem.rate ?? 0);
    if (!item.rate || item.rate <= 0) item.rate = defaultRate;
  }

  onFabricItemChange(item: PurchaseItemUI): void {
    if (!item.fabricItem) {
      item.itemId = null;
      item.rate = 0;
      return;
    }
    item.itemId = Number(item.fabricItem.id ?? 0) || null;
  }

  hasValidItems(): boolean {
    return this.form.items.some(x => !!x.itemId && Number(x.qty || 0) > 0);
  }

  // ===================== SAVE =====================
  save(): void {
    if (!this.form.purchaseDate) {
      alert('Purchase date is required.');
      return;
    }

    if (!this.form.branchId) {
      alert('Branch is required.');
      return;
    }

    if (!this.form.supplierId) {
      alert('Supplier is required.');
      return;
    }

    if (!this.hasValidItems()) {
      alert('At least one raw item with quantity is required.');
      return;
    }

    for (const it of this.form.items) {
      if (!it.itemId) continue;

      if (Number(it.qty || 0) <= 0) {
        alert('Invalid quantity.');
        return;
      }

      if (Number(it.rate || 0) < 0) {
        alert('Invalid rate.');
        return;
      }
    }

    const payload = this.uiToDtoPayload(this.form);

    const req$ =
      this.modalMode === 'create'
        ? this.purchaseSvc.create(payload)
        : this.purchaseSvc.update(payload);

    const linkedPoId = this.form.purchaseOrderId ?? null;

    req$.subscribe({
      next: r => {
        if (r?.success === false) {
          this.toast.error(r?.message || 'Save failed.');
          return;
        }

        this.toast.success(r?.message || 'Saved.');
        this.formModalRef?.hide();

        // If this GRN was tied to a PO, ask the PO controller to
        // recompute per-line ReceivedQty + header status (DRAFT →
        // PARTIALLY_RECEIVED → FULLY_RECEIVED). Fire-and-forget so a
        // sync failure doesn't undo the save.
        if (linkedPoId) {
          this.purchaseSvc.syncPoReceipts(linkedPoId).subscribe({
            next: () => this.loadOpenPos(),   // PO may have moved to FULLY_RECEIVED
            error: e => console.warn('PO sync-receipts failed:', e)
          });
        }

        this.load();
      },
      error: err => {
        console.error(err);
        this.toast.error(err?.error?.message || 'Save failed.');
      }
    });
  }

  // ===================== DETAIL =====================
  openDetail(id: number): void {
    this.detail = null;

    this.purchaseSvc.detail(id).subscribe({
      next: res => {
        const d: any = res?.data;
        if (!d) return;

        const itemsRaw = d.items ?? d.Items ?? [];

        this.detail = {
          id: Number(d.id ?? d.Id ?? 0),

          purchaseDate: d.purchaseDate ?? d.PurchaseDate ?? '',
          purchaseDateText: d.purchaseDateText ?? d.PurchaseDateText ?? '',

          invoiceNo: d.invoiceNo ?? d.InvoiceNo ?? '',
          remarks: d.remarks ?? d.Remarks ?? '',
          hasInvoice: (d.hasInvoice ?? d.HasInvoice) === true,

          supplierId: d.supplierId ?? d.SupplierId ?? null,
          supplierName: d.supplierName ?? d.SupplierName ?? '-',

          purchaseOrderId: d.purchaseOrderId ?? d.PurchaseOrderId ?? null,
          poCode: d.poCode ?? d.PoCode ?? null,

          branchId: Number(d.branchId ?? d.BranchId ?? 0),
          branchName: d.branchName ?? d.BranchName ?? '-',

          totalQty: Number(d.totalQty ?? d.TotalQty ?? 0),
          totalAmount: Number(d.totalAmount ?? d.TotalAmount ?? 0),

          isPosted: (d.isPosted ?? d.IsPosted) === true,
          active: (d.active ?? d.Active) === true,
          isReversed: (d.isReversed ?? d.IsReversed) === true,
          status: d.status ?? d.Status ?? null,

          createdByUserId: this.toNullableNumber(d.createdByUserId ?? d.CreatedByUserId),
          createdByEmployeeCode: d.createdByEmployeeCode ?? d.CreatedByEmployeeCode ?? '-',
          createdDate: this.parseApiDate(d.createdDate ?? d.CreatedDate),

          updatedByUserId: this.toNullableNumber(d.updatedByUserId ?? d.UpdatedByUserId),
          updatedByEmployeeCode: d.updatedByEmployeeCode ?? d.UpdatedByEmployeeCode ?? '-',
          updatedDate: this.parseApiDate(d.updatedDate ?? d.UpdatedDate),

          postedByUserId: this.toNullableNumber(d.postedByUserId ?? d.PostedByUserId),
          postedByEmployeeCode: d.postedByEmployeeCode ?? d.PostedByEmployeeCode ?? '-',
          postedDate: this.parseApiDate(d.postedDate ?? d.PostedDate),

          workflowStages: d.workflowStages ?? d.WorkflowStages ?? this.workflowStages,
          workflowStageIndex: Number(d.workflowStageIndex ?? d.WorkflowStageIndex ?? 0),

          items: itemsRaw.map((i: any): PurchaseDetailItemViewModel => ({
            id: Number(i.id ?? i.Id ?? 0),
            itemType: i.itemType ?? i.ItemType ?? 'RAW',
            itemId: Number(i.itemId ?? i.ItemId ?? 0),
            itemName: i.itemName ?? i.ItemName ?? '-',
            qty: Number(i.qty ?? i.Qty ?? 0),
            rate: Number(i.rate ?? i.Rate ?? 0),
            amount: Number(i.amount ?? i.Amount ?? 0),
            isPosted: (i.isPosted ?? i.IsPosted) === true,
            active: (i.active ?? i.Active) !== false,
            remarks: i.remarks ?? i.Remarks ?? ''
          })),

          ledger: ((d.ledger ?? d.Ledger ?? []) as any[]).map((l: any): PurchaseLedgerEntryViewModel => ({
            id: Number(l.id ?? l.Id ?? 0),
            transactionDateText: l.transactionDateText ?? l.TransactionDateText ?? '-',
            itemId: Number(l.itemId ?? l.ItemId ?? 0),
            itemName: l.itemName ?? l.ItemName ?? '-',
            unit: l.unit ?? l.Unit ?? '',
            transactionType: l.transactionType ?? l.TransactionType ?? '',
            referenceType: l.referenceType ?? l.ReferenceType ?? '',
            referenceNo: l.referenceNo ?? l.ReferenceNo ?? '',
            openingQty: Number(l.openingQty ?? l.OpeningQty ?? 0),
            inQty: Number(l.inQty ?? l.InQty ?? 0),
            outQty: Number(l.outQty ?? l.OutQty ?? 0),
            currentQty: Number(l.currentQty ?? l.CurrentQty ?? 0),
            rate: Number(l.rate ?? l.Rate ?? 0),
            amount: Number(l.amount ?? l.Amount ?? 0),
            remarks: l.remarks ?? l.Remarks ?? '',
            active: (l.active ?? l.Active) !== false
          }))
        };

        // Sprint-2: snapshot stepper state from the detail payload
        // and lazy-load the audit log timeline into the drawer.
        this.workflowStages = this.detail.workflowStages ?? this.workflowStages;
        this.workflowStageIndex = this.detail.workflowStageIndex ?? 0;
        this.loadAuditLog(this.detail.id);

        this.detailModalRef?.show();
      },
      error: err => {
        console.error(err);
        alert('Detail load failed.');
      }
    });
  }

  closeDetail(): void {
    this.detailModalRef?.hide();
    this.detail = null;
    this.auditLog = [];
  }

  // ──────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG LOADER
  //  Best-effort fetch — if the table doesn't exist yet or the
  //  endpoint errors, the timeline section just renders empty
  //  instead of blocking the detail drawer.
  // ──────────────────────────────────────────────────────────────
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.purchaseSvc.getAuditLog(id).subscribe({
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

  /** Turn an audit action into a pill colour. Keeps the timeline
   *  scannable at a glance — green for created/posted, amber for
   *  edited, red for reversed/deleted. */
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')  return 'bg-primary';
    if (a === 'UPDATED')  return 'bg-warning text-dark';
    if (a === 'POSTED')   return 'bg-success';
    if (a === 'REVERSED' || a === 'DELETED') return 'bg-danger';
    return 'bg-secondary';
  }

  // ──────────────────────────────────────────────────────────────
  //  Sprint-2  WORKFLOW STEPPER
  //  stepperState: which colour to paint each dot. POSTED + the
  //  REVERSED off-shoot sit at the same index in the array; the
  //  template uses isReversed to flip POSTED to grey and tint
  //  REVERSED red.
  // ──────────────────────────────────────────────────────────────
  stepperState(stageIdx: number): { done: boolean; current: boolean; cancelled: boolean } {
    const idx = this.workflowStageIndex;
    const isReversed = idx === 2;            // REVERSED is at index 2
    if (stageIdx === 2)
      return { done: false, current: isReversed, cancelled: true };
    if (isReversed)
      return { done: stageIdx <= 1, current: false, cancelled: false };
    return { done: stageIdx < idx, current: stageIdx === idx, cancelled: false };
  }

  stageLabel(stage: string): string {
    const m: { [k: string]: string } = {
      DRAFT: 'Draft',
      POSTED: 'Posted',
      REVERSED: 'Reversed'
    };
    return m[(stage || '').toUpperCase()] || stage;
  }

  // ──────────────────────────────────────────────────────────────
  //  Sprint-1  STATUS HELPERS
  // ──────────────────────────────────────────────────────────────
  statusLabel(r: PurchaseRowViewModel): string {
    if (r.isReversed) return 'Reversed';
    if (r.isPosted)   return 'Posted';
    return 'Draft';
  }
  statusBadgeClass(r: PurchaseRowViewModel): string {
    if (r.isReversed) return 'bg-danger-subtle text-danger border border-danger-subtle';
    if (r.isPosted)   return 'bg-success-subtle text-success border border-success-subtle';
    return 'bg-secondary-subtle text-secondary border border-secondary-subtle';
  }

  /** Sprint-1: badge for GRNs received in the last 3 days. */
  isRecent(r: PurchaseRowViewModel): boolean {
    if (!r?.purchaseDate) return false;
    const d = new Date(r.purchaseDate);
    if (isNaN(d.getTime())) return false;
    const ageMs = Date.now() - d.getTime();
    return ageMs >= 0 && ageMs <= 3 * 24 * 60 * 60 * 1000;
  }

  /** Sprint-1: clickable PO badge — jump to the PO detail drawer.
   *  Uses the existing PO list's deep-link query-param `search`. */
  gotoPo(r: PurchaseRowViewModel | PurchaseDetailViewModel): void {
    if (!r?.poCode) return;
    location.href = '/Content/emb-products/purchase-order?search=' + encodeURIComponent(r.poCode);
  }

  /** Sprint-1: clickable Supplier badge — jump to the Suppliers
   *  master with the supplier id pre-selected. */
  gotoSupplier(r: PurchaseRowViewModel | PurchaseDetailViewModel): void {
    if (!r?.supplierId) return;
    location.href = '/Content/emb-products/suppliers?id=' + r.supplierId;
  }

  // ──────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION
  // ──────────────────────────────────────────────────────────────
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.rows.length > 0 && this.rows.every(r => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) {
      this.rows.forEach(r => this.selectedIds.delete(r.id));
    } else {
      this.rows.forEach(r => this.selectedIds.add(r.id));
    }
  }
  clearSelection(): void { this.selectedIds.clear(); }

  /** Render the per-id-failure banner after any bulk action. */
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
    this.load();
  }

  bulkPost(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} draft GRN(s)? Posted rows can't be edited; they must be reversed.`)) return;
    this.bulkBusy = true;
    this.purchaseSvc.bulkPost(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'posted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk post failed.'); }
    });
  }

  bulkReverse(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Reverse ${ids.length} posted GRN(s)? Each will be put back into DRAFT and its Gen_Store IN rows soft-deleted.`)) return;
    this.bulkBusy = true;
    this.purchaseSvc.bulkReverse(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'reversed'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk reverse failed.'); }
    });
  }

  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} draft GRN(s)? Posted rows must be reversed first.`)) return;
    this.bulkBusy = true;
    this.purchaseSvc.bulkDelete(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────
  //  Sprint-3  CLONE PREVIEW — open the form modal pre-filled with
  //  source data, DO NOT commit yet. The new draft only appears in
  //  the list after the buyer clicks Save Clone. Closing without
  //  saving abandons the preview.
  // ──────────────────────────────────────────────────────────────
  cloneP(row: PurchaseRowViewModel): void {
    if (!row?.id) return;
    this.purchaseSvc.detail(row.id).subscribe(r => {
      const d: any = r?.data;
      if (!r?.success || !d) { alert('Failed to load source for clone preview.'); return; }

      this.cloneSourceId = row.id;
      this.cloneSourceInvoice = row.invoiceNo ?? '';
      this.modalMode = 'create';

      const today = new Date().toISOString().substring(0, 10);
      const srcItems: any[] = d.items ?? d.Items ?? [];
      this.cloneSourceItemIds = srcItems.map((i: any) => Number(i.id ?? i.Id ?? 0));

      this.form = {
        id: 0,
        purchaseDate: today,
        invoiceNo: '',          // start blank — invoice is per-shipment
        supplierId: d.supplierId ?? d.SupplierId ?? null,
        branchId: Number(d.branchId ?? d.BranchId ?? 0) || null,
        remarks: 'Cloned from invoice ' + (row.invoiceNo || '#' + row.id),
        items: srcItems.map((i: any) => this.buildCloneItemUI(i)),
        purchaseOrderId: null,
        poCode: null
      };

      this.formModalRef?.show();
    });
  }

  /** Build a single PurchaseItemUI from a detail-payload source line.
   *  The rawItem/threadItem/fabricItem refs are resolved from the
   *  already-loaded dropdown arrays so the form selectors snap to the
   *  correct option without a round-trip. */
  private buildCloneItemUI(srcItem: any): PurchaseItemUI {
    const itemType = (srcItem.itemType ?? srcItem.ItemType ?? 'RAW').toString().toUpperCase() as PurchaseItemType;
    const itemId = Number(srcItem.itemId ?? srcItem.ItemId ?? 0);
    let rawItem: any = null, threadItem: any = null, fabricItem: any = null;
    if (itemType === 'RAW')
      rawItem = this.rawItems.find((x: any) => Number(x.id ?? x.Id) === itemId) || null;
    else if (itemType === 'THREAD')
      threadItem = this.threads.find((x: any) => Number(x.id ?? x.Id) === itemId) || null;
    else
      fabricItem = this.fabrics.find((x: any) => Number(x.id ?? x.Id) === itemId) || null;
    return {
      itemId,
      itemType,
      rawItem, threadItem, fabricItem,
      qty:  Number(srcItem.qty  ?? srcItem.Qty  ?? 0),
      rate: Number(srcItem.rate ?? srcItem.Rate ?? 0),
      remarks: srcItem.remarks ?? srcItem.Remarks ?? '',
      poItemId: null               // a clone is a fresh draft, not a GRN-against-PO
    };
  }

  /** True when the form modal is currently showing a clone preview. */
  isCloning(): boolean { return this.cloneSourceId !== null; }

  /** Commit the clone preview. Builds per-line overrides keyed by the
   *  original source PurchaseItem.Id and POSTs /clone. On success the
   *  list reloads and the new draft appears. */
  saveClone(): void {
    if (!this.cloneSourceId) return;
    this.cloningSaving = true;

    const overrides = {
      purchaseDate: this.form.purchaseDate || null,
      invoiceNo:    this.form.invoiceNo || '',
      remarks:      this.form.remarks || '',
      items: this.form.items.map((edited, idx) => ({
        sourceItemId: this.cloneSourceItemIds[idx] || 0,
        qty:  Number(edited.qty),
        rate: Number(edited.rate),
        remarks: edited.remarks || ''
      })).filter(x => x.sourceItemId > 0)
    };

    this.purchaseSvc.clone(this.cloneSourceId, overrides).subscribe({
      next: (res: any) => {
        this.cloningSaving = false;
        if (!res?.success) { alert(res?.message || 'Clone save failed.'); return; }
        const d = res?.data ?? {};
        alert(`Cloned to invoice ${d.invoiceNo || '(no invoice)'} (${d.itemsCloned} line(s)).`);
        this.cloneSourceId = null;
        this.cloneSourceInvoice = '';
        this.cloneSourceItemIds = [];
        this.formModalRef?.hide();
        this.load();
      },
      error: (err: any) => {
        this.cloningSaving = false;
        alert(err?.error?.message || err?.message || 'Clone save failed.');
      }
    });
  }

  /** Reset clone-preview state when the form modal closes. Called by
   *  template close buttons + the modal's hidden.bs.modal event. */
  resetCloneState(): void {
    this.cloneSourceId = null;
    this.cloneSourceInvoice = '';
    this.cloneSourceItemIds = [];
  }

  // ===================== Phase F.2 - Print Voucher =====================
  printVoucher(): void {
    if (!this.detail) return;
    this.printingVoucher = true;
    // Wait one tick so the *ngIf="printingVoucher" template renders.
    setTimeout(() => {
      window.print();
      setTimeout(() => { this.printingVoucher = false; }, 250);
    }, 50);
  }

  // ===================== Phase F.2 - Duplicate =====================
  duplicatePurchase(id: number): void {
    if (this.duplicating) return;
    if (!confirm('Clone this purchase as a new draft? You can edit the copy before posting.')) return;

    this.duplicating = true;
    this.purchaseSvc.duplicate(id).subscribe({
      next: (res: any) => {
        this.duplicating = false;
        const ok = res?.success ?? res?.Success ?? false;
        const data = res?.data ?? res?.Data ?? null;
        if (ok && data?.id) {
          this.closeDetail();
          this.load();
          // Brief delay so the freshly-loaded list contains the new draft.
          setTimeout(() => this.openEdit(Number(data.id)), 300);
        } else {
          alert(res?.message || 'Failed to duplicate purchase.');
        }
      },
      error: err => {
        this.duplicating = false;
        console.error(err);
        alert(err?.error?.message || 'Failed to duplicate purchase.');
      }
    });
  }

  // ===================== DELETE =====================
  confirmDelete(row: any): void {
    if (row?.isPosted ?? row?.IsPosted) {
      alert('Posted purchase cannot be deleted.');
      return;
    }

    this.deleteTarget = row;
    this.deleteModalRef?.show();
  }

  deleteConfirmed(): void {
    if (!this.deleteTarget) return;

    const id = this.deleteTarget.id ?? this.deleteTarget.Id;
    if (!id) return;

    this.purchaseSvc.delete(id).subscribe({
      next: r => {
        this.toast.success(r?.message || 'Deleted.');
        this.deleteModalRef?.hide();
        this.deleteTarget = null;
        this.load();
      },
      error: err => {
        console.error(err);
        this.toast.error(err?.error?.message || 'Delete failed.');
      }
    });
  }

  closeDelete(): void {
    this.deleteModalRef?.hide();
    this.deleteTarget = null;
  }

  // ===================== POST =====================
  confirmPost(row: any): void {
    if (row?.isPosted ?? row?.IsPosted) {
      alert('Purchase already posted.');
      return;
    }

    this.selectedRow = row;
    this.previewItems = [];

    const id = row?.id ?? row?.Id;
    if (!id) return;

    this.purchaseSvc.previewPost(id).subscribe({
      next: res => {
        this.previewItems = res?.data ?? [];
        this.postModalRef?.show();
      },
      error: err => {
        console.error(err);
        alert('Preview failed.');
      }
    });
  }

  postConfirmed(): void {
    if (!this.selectedRow) return;

    const id = this.selectedRow?.id ?? this.selectedRow?.Id;
    if (!id) return;

    this.purchaseSvc.postPurchase(id).subscribe({
      next: res => {
        this.toast.success(res?.message || 'Purchase posted to raw store.');

        this.postModalRef?.hide();
        this.previewItems = [];
        this.selectedRow = null;

        this.load();
      },
      error: err => {
        console.error(err);
        this.toast.error(err?.error?.message || 'Post failed.');
      }
    });
  }

  closePost(): void {
    this.postModalRef?.hide();
    this.previewItems = [];
    this.selectedRow = null;
  }

  // ===================== REVERSE (Phase D) =====================
  confirmReverse(row: any): void {
    const id = row?.id ?? row?.Id;
    if (!id) return;
    if (!row?.isPosted && !row?.IsPosted) {
      alert('Only posted purchases can be reversed.');
      return;
    }
    if (!confirm(`Reverse purchase ${row.invoiceNo ?? id}? Threads consumed downstream will block this action.`)) return;

    this.purchaseSvc.reversePurchase(id).subscribe({
      next: r => {
        this.toast.success(r?.message || 'Reversed.');
        this.load();
      },
      error: err => {
        this.toast.error(err?.error?.message ?? err?.message ?? 'Reverse failed.');
      }
    });
  }

  // ===================== RETURN TO SUPPLIER =====================
  openReturnModal(row: any): void {
    const id = row?.id ?? row?.Id;
    if (!id) return;
    if (!row?.isPosted && !row?.IsPosted) {
      alert('Only posted purchases can be returned.');
      return;
    }

    this.returnTargetId = id;
    this.returnInfo = null;
    this.returnReason = '';
    this.returnLoading = true;
    this.returnModalRef?.show();

    this.purchaseSvc.getReturnInfo(id).subscribe({
      next: (res: any) => {
        this.returnLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          alert(res?.message || 'Failed to load return info.');
          this.closeReturnModal();
          return;
        }
        const data = res?.data ?? res?.Data ?? {};
        // Seed editable per-line fields the user will fill in.
        const lines = (data.lines || []).map((l: any) => ({
          ...l,
          returnQty: 0,
          lineRemarks: ''
        }));
        this.returnInfo = { ...data, lines };
      },
      error: err => {
        this.returnLoading = false;
        const msg = err?.error?.message ?? err?.message ?? 'Failed to load return info.';
        alert(msg);
        this.closeReturnModal();
      }
    });
  }

  closeReturnModal(): void {
    this.returnModalRef?.hide();
    this.returnTargetId = null;
    this.returnInfo = null;
    this.returnReason = '';
    this.returnLoading = false;
    this.returnSaving = false;
  }

  getReturnTotalQty(): number {
    if (!this.returnInfo?.lines) return 0;
    return this.returnInfo.lines.reduce(
      (sum: number, l: any) => sum + Number(l.returnQty ?? 0), 0);
  }

  getReturnTotalAmount(): number {
    if (!this.returnInfo?.lines) return 0;
    return this.returnInfo.lines.reduce(
      (sum: number, l: any) => sum + (Number(l.returnQty ?? 0) * Number(l.rate ?? 0)), 0);
  }

  submitReturn(): void {
    if (!this.returnTargetId || !this.returnInfo) return;

    const lines = (this.returnInfo.lines || [])
      .filter((l: any) => Number(l.returnQty ?? 0) > 0)
      .map((l: any) => ({
        purchaseItemId: Number(l.purchaseItemId),
        returnQty: Number(l.returnQty),
        remarks: l.lineRemarks ? String(l.lineRemarks).trim() : null
      }));

    if (lines.length === 0) {
      alert('Enter a return quantity for at least one item.');
      return;
    }

    // Client-side cap check (server re-validates).
    const overdraft = (this.returnInfo.lines || []).find((l: any) =>
      Number(l.returnQty ?? 0) > Number(l.returnableQty ?? 0)
    );
    if (overdraft) {
      alert(`Return qty for '${overdraft.itemName}' exceeds returnable (${overdraft.returnableQty}).`);
      return;
    }

    this.returnSaving = true;
    this.purchaseSvc.submitReturn(this.returnTargetId, {
      reason: this.returnReason ? this.returnReason.trim() : null,
      lines
    }).subscribe({
      next: r => {
        this.returnSaving = false;
        if (r?.success) {
          this.toast.success(r?.message || 'Returned.');
          this.closeReturnModal();
          this.load();
        } else {
          this.toast.error(r?.message || 'Return failed.');
        }
      },
      error: err => {
        this.returnSaving = false;
        this.toast.error(err?.error?.message ?? err?.message ?? 'Return failed.');
      }
    });
  }

  // ===================== EXPORTS =====================
  exportExcel(): void {
    const stamp = new Date().toISOString().substring(0, 10);
    this.purchaseSvc.exportExcel({
      fromDate: this.filterFromDate || null,
      toDate: this.filterToDate || null,
      supplierId: this.filterSupplierId,
      status: this.filterStatus || null,
      itemSearch: this.filterItemSearch || null,
      invoiceSearch: this.searchText || null
    }).subscribe({
      next: b => this.download(b, `Purchases_${stamp}.xlsx`),
      error: err => {
        console.error(err);
        alert('Excel export failed.');
      }
    });
  }

  exportPdf(): void {
    this.purchaseSvc.exportPdf().subscribe(b => this.download(b, 'purchase.pdf'));
  }

  exportPrint(): void {
    window.print();
  }

  private download(blob: Blob, name: string): void {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');

    a.href = url;
    a.download = name;
    a.click();

    URL.revokeObjectURL(url);
  }

  // ===================== TOTALS =====================
  get formTotalQty(): number {
    return this.form.items.reduce((s, i) => s + Number(i.qty || 0), 0);
  }

  get formTotalAmount(): number {
    return this.form.items.reduce(
      (s, i) => s + Number(i.qty || 0) * Number(i.rate || 0),
      0
    );
  }

  get detailTotalQty(): number {
    return this.detail?.items?.reduce((s, i) => s + Number(i.qty || 0), 0) ?? 0;
  }

  get detailTotalAmount(): number {
    return this.detail?.items?.reduce((s, i) => s + Number(i.amount || 0), 0) ?? 0;
  }

  // ===================== Phase F.1 Profile helpers =====================
  get detailItemCount(): number {
    return this.detail?.items?.length ?? 0;
  }

  get detailLedger(): PurchaseLedgerEntryViewModel[] {
    return this.detail?.ledger ?? [];
  }

  get detailLedgerInTotal(): number {
    return this.detailLedger.reduce((s, l) => s + Number(l.inQty || 0), 0);
  }

  get detailLedgerAmountTotal(): number {
    return this.detailLedger.reduce((s, l) => s + Number(l.amount || 0), 0);
  }

  get detailLedgerRowCount(): number {
    return this.detailLedger.length;
  }

  get detailLedgerInSync(): boolean {
    // ledger total IN should match the purchase items total Qty when posted
    if (!this.detail?.isPosted) return true;
    return Math.abs(this.detailLedgerInTotal - this.detailTotalQty) < 0.001;
  }

  private parseApiDate(val: any): string | null {
    if (!val) return null;

    if (typeof val === 'string') {
      const dotNet = /\/Date\((\d+)\)\//.exec(val);
      if (dotNet) {
        return new Date(+dotNet[1]).toISOString();
      }

      return val;
    }

    return null;
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
}
