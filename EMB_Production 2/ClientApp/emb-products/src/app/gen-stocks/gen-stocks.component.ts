import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {StockRowViewModel,StockDetailViewModel,StockDashboardViewModel} from '../shared/models/gen-stocks.dto';
import {ApiPagedResponse,ApiResponseData,DropdownItem} from '../shared/models/api-response';
import { GenStocksService } from '../services/gen-stocks.service';

@Component({
  selector: 'app-gen-stocks',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gen-stocks.component.html',
  styleUrls: ['./gen-stocks.component.css']
})
export class GenStocksComponent implements OnInit {
  rows: StockRowViewModel[] = [];
  dashboard: StockDashboardViewModel | null = null;
  branches: DropdownItem[] = [];
  selectedDetail: StockDetailViewModel | null = null;

  searchText = '';
  branchId: number | null = null;

  // Phase P.0 filter
  stockStatusFilter = '';

  // Phase P.0 dashboard chips
  byStockStatus: Array<{ status: string; count: number }> = [];
  byBranch: Array<{ branch: string; count: number; qty: number }> = [];

  // Phase P.1 per-item history modal state
  historyOpen = false;
  historyLoading = false;
  historyItem: StockRowViewModel | null = null;
  historyRows: Array<{
    id: number;
    dateText: string;
    transactionType: string;
    referenceType: string;
    referenceNo: string;
    stockStage: string;
    inQty: number;
    outQty: number;
    balance: number;
    rate: number;
    amount: number;
    branchName: string;
    remarks: string;
  }> = [];
  historyMeta = { totalIn: 0, totalOut: 0, balance: 0, rowCount: 0, unit: 'PCS' };

  page = 1;
  pageSize = 20;
  totalItems = 0;
  totalPages = 1;
  paginationPages: number[] = [];

  loading = false;
  loadingDetail = false;

  constructor(private svc: GenStocksService) { }

  ngOnInit(): void {
    this.loadBranches();
    this.load();
  }

  load(): void {
    this.loading = true;

    this.svc.list(
      this.searchText,
      this.page,
      this.pageSize,
      this.branchId,
      { stockStatusFilter: this.stockStatusFilter || null }
    ).subscribe({
      next: (res: any) => {
        const normalized = this.normalizePagedResponse<StockRowViewModel>(res);

        this.rows = normalized.items;
        this.totalItems = normalized.totalItems;
        this.page = normalized.page;
        this.pageSize = normalized.pageSize;
        this.totalPages = normalized.totalPages;

        // Phase P.0 - dashboard chips from server
        const dash = res?.dashboard ?? null;
        this.byStockStatus = Array.isArray(dash?.byStockStatus) ? dash.byStockStatus : [];
        this.byBranch = Array.isArray(dash?.byBranch) ? dash.byBranch : [];

        this.loadDashboard();

        this.setupPagination();
        this.loading = false;
      },

      error: (err) => {
        console.error('STOCK LOAD FAILED', err);

        this.rows = [];
        this.dashboard = this.emptyDashboard();
        this.totalItems = 0;
        this.totalPages = 1;

        this.setupPagination();
        this.loading = false;
      }
    });
  }

  loadDashboard(): void {
    const rows = this.rows || [];

    const totalExpectedRevenue = rows.reduce(
      (sum, x) => sum + Number(x.expectedRevenue ?? 0),
      0
    );

    const totalEstimatedProfit = rows.reduce(
      (sum, x) => sum + Number(x.estimatedProfit ?? 0),
      0
    );

    this.dashboard = {
      customerReservedStock: rows.reduce(
        (sum, x) => sum + Number(x.customerReservedStock ?? 0),
        0
      ),

      freeCompanyStock: rows.reduce(
        (sum, x) => sum + Number(x.freeCompanyStock ?? 0),
        0
      ),

      totalFinishedStock: rows.reduce(
        (sum, x) => sum + Number(x.totalFinishedStock ?? 0),
        0
      ),

      totalInventoryCost: rows.reduce(
        (sum, x) => sum + Number(x.totalCost ?? x.totalFinishedCost ?? 0),
        0
      ),

      totalReservedValue: rows.reduce(
        (sum, x) => sum + Number(x.reservedValue ?? 0),
        0
      ),

      freeStockValue: rows.reduce(
        (sum, x) => sum + Number(x.freeStockValue ?? 0),
        0
      ),

      totalExpectedRevenue: totalExpectedRevenue,
      totalEstimatedProfit: totalEstimatedProfit,

      overallProfitMarginPercent:
        totalExpectedRevenue > 0
          ? (totalEstimatedProfit / totalExpectedRevenue) * 100
          : 0,

      totalQcPostCount: rows.reduce(
        (sum, x) => sum + Number(x.qcPostCount ?? 0),
        0
      ),

      totalDispatchCount: rows.reduce(
        (sum, x) => sum + Number(x.dispatchCount ?? 0),
        0
      ),

      totalReverseCount: rows.reduce(
        (sum, x) => sum + Number(x.reverseCount ?? 0),
        0
      ),

      totalMovementCount: rows.reduce(
        (sum, x) => sum + Number(x.movementCount ?? 0),
        0
      ),

      totalProducts: new Set(
        rows.map(x => x.prodId ?? x.itemId).filter(x => x != null)
      ).size,

      totalBranches: new Set(
        rows.map(x => x.branchId).filter(x => x != null)
      ).size
    };
  }

  private emptyDashboard(): StockDashboardViewModel {
    return {
      customerReservedStock: 0,
      freeCompanyStock: 0,
      totalFinishedStock: 0,

      totalInventoryCost: 0,
      totalReservedValue: 0,
      freeStockValue: 0,

      totalExpectedRevenue: 0,
      totalEstimatedProfit: 0,
      overallProfitMarginPercent: 0,

      totalQcPostCount: 0,
      totalDispatchCount: 0,
      totalReverseCount: 0,
      totalMovementCount: 0,

      totalProducts: 0,
      totalBranches: 0
    };
  }

  private normalizePagedResponse<T>(
    res: ApiPagedResponse<T>
  ): {
    items: T[];
    totalItems: number;
    page: number;
    pageSize: number;
    totalPages: number;
  } {
    const data: any = res?.data ?? {};

    const items: T[] = Array.isArray(data?.items)
      ? data.items
      : Array.isArray(data?.rows)
        ? data.rows
        : [];

    const totalItems = Number(data?.totalItems ?? data?.total ?? items.length);
    const page = Number(data?.page ?? this.page);
    const pageSize = Number(data?.pageSize ?? this.pageSize);
    const totalPages = Number(
      data?.totalPages ?? Math.max(1, Math.ceil(totalItems / Math.max(pageSize, 1)))
    );

    return {
      items,
      totalItems,
      page,
      pageSize,
      totalPages
    };
  }

  loadBranches(): void {
    this.svc.getBranches().subscribe({
      next: (res: ApiResponseData<DropdownItem[]>) => {
        this.branches = res?.data ?? [];
      },
      error: () => {
        this.branches = [];
      }
    });
  }

  quickSearch(itemName: string | null): void {
    this.searchText = itemName ?? '';
    this.page = 1;
    this.load();
  }

  isLowStock(row: StockRowViewModel): boolean {
    const freeStock = Number(row.freeCompanyStock ?? 0);

    return freeStock > 0 && freeStock <= 5;
  }

  setupPagination(): void {
    const maxPagesToShow = 7;
    const total = Math.max(1, this.totalPages);
    const current = Math.min(Math.max(1, this.page), total);

    let start = Math.max(1, current - Math.floor(maxPagesToShow / 2));
    let end = Math.min(total, start + maxPagesToShow - 1);

    if (end - start + 1 < maxPagesToShow) {
      start = Math.max(1, end - maxPagesToShow + 1);
    }

    this.paginationPages = Array.from(
      { length: end - start + 1 },
      (_, i) => start + i
    );
  }

  changePage(p: number): void {
    if (p < 1 || p > this.totalPages || p === this.page) return;

    this.page = p;
    this.load();
  }

  onSearch(): void {
    this.page = 1;
    this.load();
  }

  onBranchChange(): void {
    this.page = 1;
    this.load();
  }

  // Phase P.0 - filter helpers
  onFiltersChange(): void {
    this.page = 1;
    this.load();
  }
  clearFilters(): void {
    this.searchText = '';
    this.branchId = null;
    this.stockStatusFilter = '';
    this.page = 1;
    this.load();
  }
  stockStatusPill(s: string): { cssClass: string; icon: string } {
    const u = (s ?? '').toString().trim().toUpperCase();
    if (u === 'AVAILABLE')
      return { cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    if (u === 'NEGATIVE')
      return { cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-exclamation-triangle-fill' };
    if (u === 'COST MISSING')
      return { cssClass: 'bg-warning-subtle text-warning-emphasis border border-warning', icon: 'bi-cash-stack' };
    if (u === 'EMPTY')
      return { cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-dash-circle' };
    return { cssClass: 'bg-light text-dark border', icon: 'bi-tag' };
  }
  rowTintClass(r: any): string {
    const u = (r?.stockStatus ?? '').toString().trim().toUpperCase();
    if (u === 'NEGATIVE')      return 'table-danger-soft';
    if (u === 'COST MISSING')  return 'table-warning-soft';
    if (u === 'EMPTY')         return 'table-muted-soft';
    return '';
  }

  // Phase P.1 - per-item history modal
  openItemHistory(row: StockRowViewModel): void {
    if (!row?.prodId) return;
    this.historyOpen = true;
    this.historyLoading = true;
    this.historyItem = row;
    this.historyRows = [];
    this.historyMeta = { totalIn: 0, totalOut: 0, balance: 0, rowCount: 0, unit: row?.unit || 'PCS' };

    this.svc.itemHistory(Number(row.prodId), row.branchId).subscribe({
      next: (res: any) => {
        this.historyLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          alert(res?.message || 'Failed to load item history.');
          this.historyOpen = false;
          return;
        }
        const d = res?.data ?? res?.Data ?? {};
        const ledger: any[] = Array.isArray(d?.ledger) ? d.ledger : [];
        this.historyRows = ledger.map(l => ({
          id: Number(l.id ?? 0),
          dateText: l.dateText ?? '-',
          transactionType: l.transactionType ?? '',
          referenceType: l.referenceType ?? '',
          referenceNo: l.referenceNo ?? '',
          stockStage: l.stockStage ?? '',
          inQty: Number(l.inQty ?? 0),
          outQty: Number(l.outQty ?? 0),
          balance: Number(l.balance ?? 0),
          rate: Number(l.rate ?? 0),
          amount: Number(l.amount ?? 0),
          branchName: l.branchName ?? '-',
          remarks: l.remarks ?? ''
        }));
        this.historyMeta = {
          totalIn: Number(d?.totalIn ?? 0),
          totalOut: Number(d?.totalOut ?? 0),
          balance: Number(d?.balance ?? 0),
          rowCount: Number(d?.rowCount ?? this.historyRows.length),
          unit: row?.unit || 'PCS'
        };
      },
      error: err => {
        this.historyLoading = false;
        console.error('Stock item history error:', err);
        alert(err?.error?.message || 'Failed to load item history.');
        this.historyOpen = false;
      }
    });
  }
  closeItemHistory(): void {
    this.historyOpen = false;
    this.historyItem = null;
    this.historyRows = [];
  }

  refresh(): void {
    this.searchText = '';
    this.branchId = null;
    this.page = 1;
    this.selectedDetail = null;
    this.load();
  }

  openDetail(row: StockRowViewModel): void {
    if (!row?.id) {
      this.selectedDetail = row as StockDetailViewModel;
      return;
    }

    this.loadingDetail = true;

    this.svc.detail(row.id).subscribe({
      next: (res: ApiResponseData<StockDetailViewModel>) => {
        this.selectedDetail = res?.data ?? (row as StockDetailViewModel);
        this.loadingDetail = false;
      },
      error: () => {
        this.selectedDetail = row as StockDetailViewModel;
        this.loadingDetail = false;
      }
    });
  }

  closeDetail(): void {
    this.selectedDetail = null;
  }

  getTotalFinishedStock(row: StockRowViewModel | StockDetailViewModel | null): number {
    return Number(row?.totalFinishedStock ?? row?.currentQty ?? 0);
  }

  getCustomerReservedStock(row: StockRowViewModel | StockDetailViewModel | null): number {
    return Number(row?.customerReservedStock ?? 0);
  }

  getFreeCompanyStock(row: StockRowViewModel | StockDetailViewModel | null): number {
    const free = Number(row?.freeCompanyStock ?? 0);

    if (free !== 0) return free;

    return this.getTotalFinishedStock(row) - this.getCustomerReservedStock(row);
  }

  formatNumber(value: any): string {
    const n = Number(value ?? 0);
    return n.toLocaleString(undefined, {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });
  }

  getReservationsSum(field: 'reservedQty' | 'dispatchedQty' | 'remainingQty'): number {
    const list = this.selectedDetail?.customerReservations || [];
    return list.reduce((sum, r) => sum + Number((r as any)?.[field] ?? 0), 0);
  }

  formatCurrency(value: any): string {

    const num = Number(value ?? 0);

    return num.toLocaleString('en-US', {
      minimumFractionDigits: 0,
      maximumFractionDigits: 2
    });

  }

  formatDotNetDate(value: any): string {
    if (!value) return '-';

    if (value instanceof Date) {
      return this.formatDate(value);
    }

    const str = String(value);
    const match = /\/Date\((\d+)\)\//.exec(str);

    if (match?.[1]) {
      return this.formatDate(new Date(Number(match[1])));
    }

    const normalDate = new Date(str);
    if (!isNaN(normalDate.getTime())) {
      return this.formatDate(normalDate);
    }

    return str;
  }

  private formatDate(d: Date): string {
    const day = String(d.getDate()).padStart(2, '0');
    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const month = monthNames[d.getMonth()];
    const year = d.getFullYear();

    let hours = d.getHours();
    const minutes = String(d.getMinutes()).padStart(2, '0');
    const ampm = hours >= 12 ? 'PM' : 'AM';

    hours = hours % 12 || 12;

    return `${day}-${month}-${year} ${hours}:${minutes} ${ampm}`;
  }

  isProfitPositive(): boolean {
    return Number(this.dashboard?.totalEstimatedProfit ?? 0) >= 0;
  }

  isProfitNegative(): boolean {
    return Number(this.dashboard?.totalEstimatedProfit ?? 0) < 0;
  }

  exportExcel(): void {
    this.svc.exportExcel(this.searchText, this.branchId).subscribe({
      next: (blob: Blob) => this.downloadBlob(blob, 'finished-stock.xlsx'),
      error: (err) => console.error('EXPORT EXCEL FAILED', err)
    });
  }

  exportPdf(): void {
    this.svc.exportPdf(this.searchText, this.branchId).subscribe({
      next: (blob: Blob) => this.downloadBlob(blob, 'finished-stock.pdf'),
      error: (err) => console.error('EXPORT PDF FAILED', err)
    });
  }

  exportToPrint(): void {
    window.open(this.svc.printUrl(this.searchText, this.branchId), '_blank');
  }

  private downloadBlob(blob: Blob, fileName: string): void {
    const url = window.URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();

    window.URL.revokeObjectURL(url);
  }
}
