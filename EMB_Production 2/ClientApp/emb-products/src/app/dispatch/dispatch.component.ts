import { Component, ElementRef, ViewChild, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { DispatchService } from '../services/dispatch.service';
import {DispatchRowViewModel,DispatchDetailViewModel,DispatchDto,DispatchStockRow,SaleType
} from '../shared/models/dispatch.dto';
import { DropdownItem, ROLES } from '../shared/models/api-response';

declare const bootstrap: any;
type DispatchSaleType = 'CUSTOMER_DEMAND' | 'DIRECT_SALE';

@Component({
  selector: 'app-dispatch',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dispatch.component.html',
  styleUrls: ['./dispatch.component.css']
})
export class DispatchComponent implements OnInit {
  items: DispatchRowViewModel[] = [];
  stockRows: DispatchStockRow[] = [];

  customers: DropdownItem[] = [];
  detailData: DispatchDetailViewModel | null = null;

  loading = false;
  loadingEdit = false;
  saving = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  WORKFLOW STEPPER + AUDIT TIMELINE
  // ──────────────────────────────────────────────────────────────────
  workflowStages: string[] = ['DRAFT', 'POSTED', 'REVERSED'];
  workflowStageIndex = 0;
  auditLog: any[] = [];
  auditLogLoading = false;

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;

  searchText = '';
  activeFilter = '';
  showDeleted = '';

  page = 1;
  pageSize = 10;
  totalCount = 0;

  // Phase O.0 filters
  filterFromDate = '';
  filterToDate = '';
  filterCustomerId: number | null = null;
  filterJobOrderId: number | null = null;
  filterBranchId: number | null = null;
  filterPosted = '';
  filterDispatchType = '';
  filterSaleType = '';

  summary = {
    count: 0,
    totalQty: 0,
    totalAmount: 0,
    postedCount: 0,
    draftCount: 0,
    reversedCount: 0,
    byDispatchType: [] as Array<{ type: string; count: number }>,
    bySaleType: [] as Array<{ saleType: string; count: number }>
  };

  // Phase O.1 - 360 profile
  profile: any = null;
  profileLoading = false;
  showProfile = false;
  printingChallan = false;

  modalMode: 'create' | 'edit' = 'create';
  selectedDispatchId: number | null = null;

  form: DispatchDto = this.createEmptyForm();
  formIsPosted = false;

  canPostRole = false;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;
  @ViewChild('reverseModal') reverseModal!: ElementRef;
  @ViewChild('ledgerModal') ledgerModal!: ElementRef;
  @ViewChild('postModal') postModal!: ElementRef;

  deleteId: number | null = null;
  reverseId: number | null = null;
  ledgerRows: any[] = [];
  jobOrders: any[] = [];

  constructor(
    private svc: DispatchService,
    private router: Router,
    private http: HttpClient,
    private route: ActivatedRoute
  ) { }

  ngOnInit(): void {
    // Sprint-2: honor ?search= and ?jobOrderId= from the URL so quick-jump
    // links from Job Orders land here pre-filtered to that JO's dispatches.
    const incomingSearch = this.route.snapshot.queryParamMap.get('search');
    if (incomingSearch) this.searchText = incomingSearch.trim();
    const incomingJobOrderId = this.route.snapshot.queryParamMap.get('jobOrderId');
    if (incomingJobOrderId) {
      const n = Number(incomingJobOrderId);
      if (n > 0) this.filterJobOrderId = n;
    }

    this.resolveRolePermissions();
    this.loadCustomers();
    this.loadData();
  }

  recalculateDispatchRow(r: any): void {
    const qty = Number(r.dispatchQty ?? 0);
    const base = Number(r.baseSalePrice ?? 0);
    const discountPercent = Number(r.discountPercent ?? 0);
    const discountAmount = Number(r.discountAmount ?? 0);

    // Net Sale Price always cascades from Base Price (which the operator
    // can type if product master has none). Base is the single source of
    // truth — discounts deduct from it, salePrice is the computed result.
    // Amount = Dispatch Qty × Net Sale Price.
    let salePrice = base;
    if (discountPercent > 0) salePrice -= base * discountPercent / 100;
    if (discountAmount > 0)  salePrice -= discountAmount;
    if (salePrice < 0)       salePrice = 0;

    r.salePrice = Number(salePrice.toFixed(2));
    r.amount    = Number((qty * salePrice).toFixed(2));
  }

  /** Called when the operator types in the Net Sale Price field directly
   *  (manual override). We only refresh the line's Amount — we do NOT
   *  re-run the discount cascade, otherwise the override would be wiped. */
  onSalePriceOverride(r: any): void {
    const qty = Number(r.dispatchQty ?? 0);
    const salePrice = Number(r.salePrice ?? 0);
    r.amount = Number((qty * salePrice).toFixed(2));
  }

  private resolveRolePermissions(): void {
    try {
      const raw = localStorage.getItem('auth_user');
      if (!raw) return;

      const user = JSON.parse(raw);
      const roleId = Number(user.RoleId || 0);

      this.canPostRole =
        roleId === ROLES.ADMIN ||
        roleId === ROLES.STORE_MANAGER;
    } catch {
      this.canPostRole = false;
    }
  }

  createEmptyForm(): any {
  return {
    id: 0,
    dispatchDate: new Date().toISOString().substring(0, 10),
    saleType: 'CUSTOMER_DEMAND' as DispatchSaleType,

    customerId: null,
    jobOrderId: null,

    status: 'Active',
    vehicleNo: '',
    driverName: '',
    remarks: '',

    branchId: null
  };
}

  loadData(): void {
    this.loading = true;

    this.svc.list(
      this.searchText,
      this.activeFilter,
      this.showDeleted,
      this.page,
      this.pageSize,
      {
        fromDate: this.filterFromDate || null,
        toDate: this.filterToDate || null,
        customerId: this.filterCustomerId,
        jobOrderId: this.filterJobOrderId,
        branchId: this.filterBranchId,
        posted: this.filterPosted || null,
        dispatchType: this.filterDispatchType || null,
        saleType: this.filterSaleType || null
      }
    ).subscribe({
      next: (res: any) => {
        const data: any = res?.data ?? {};
        const rows = data.items ?? [];

        this.totalCount = Number(data.totalItems ?? data.total ?? 0);

        const sm = data?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            totalQty: Number(sm.totalQty ?? 0),
            totalAmount: Number(sm.totalAmount ?? 0),
            postedCount: Number(sm.postedCount ?? 0),
            draftCount: Number(sm.draftCount ?? 0),
            reversedCount: Number(sm.reversedCount ?? 0),
            byDispatchType: Array.isArray(sm.byDispatchType) ? sm.byDispatchType : [],
            bySaleType: Array.isArray(sm.bySaleType) ? sm.bySaleType : []
          };
        }

        this.items = rows.map((x: any) => ({
       
          id: Number(x.id ?? x.Id ?? 0),

          dispatchDate: x.dispatchDate ?? x.DispatchDate ?? null,
          dispatchDateText: x.dispatchDateText ?? x.DispatchDateText ?? '',

          dispatchType: x.dispatchType ?? x.DispatchType ?? 'SALES',
          saleType: x.saleType ?? x.SaleType ?? 'CUSTOMER_DEMAND',
          saleTypeText: x.saleTypeText ?? x.SaleTypeText ?? null,

          jobOrderId: x.jobOrderId ?? x.JobOrderId ?? null,
          jobOrderCode: x.jobOrderCode ?? x.JobOrderCode ?? null,

          customerId: x.customerId ?? x.CustomerId ?? null,
          customerName: x.customerName ?? x.CustomerName ?? null,

          directCustomerName: x.directCustomerName ?? x.DirectCustomerName ?? null,
          directCustomerMobile: x.directCustomerMobile ?? x.DirectCustomerMobile ?? null,
          directCustomerAddress: x.directCustomerAddress ?? x.DirectCustomerAddress ?? null,

          displayCustomerName:
            x.displayCustomerName ??
            x.DisplayCustomerName ??
            x.customerName ??
            x.CustomerName ??
            x.directCustomerName ??
            x.DirectCustomerName ??
            '-',

          branchId: x.branchId ?? x.BranchId ?? null,
          branchName: x.branchName ?? x.BranchName ?? null,

          vehicleNo: x.vehicleNo ?? x.VehicleNo ?? null,
          driverName: x.driverName ?? x.DriverName ?? null,

          productName: x.productName ?? x.ProductName ?? null,

          itemCount: Number(x.itemCount ?? x.ItemCount ?? 0),
          dispatchQtyTotal: Number(x.dispatchQtyTotal ?? x.DispatchQtyTotal ?? 0),

          salePrice: Number(x.salePrice ?? x.SalePrice ?? 0),
          totalAmount: Number(x.totalAmount ?? x.TotalAmount ?? 0),

          totalCost: Number(x.totalCost ?? x.TotalCost ?? 0),
          costPerPc: Number(x.costPerPc ?? x.CostPerPc ?? 0),
          totalMargin: Number(x.totalMargin ?? x.TotalMargin ?? 0),
          marginPct: Number(x.marginPct ?? x.MarginPct ?? 0),
          isLoss: Boolean(x.isLoss ?? x.IsLoss ?? false),

          stockStatus: x.stockStatus ?? x.StockStatus ?? null,

          active: x.active ?? x.Active ?? true,
          isPosted: x.isPosted ?? x.IsPosted ?? false,
          isReversed: x.isReversed ?? x.IsReversed ?? false,
          statusText: x.statusText ?? x.StatusText ?? null
        }));

        this.loading = false;
      },
      error: err => {
        console.error('Dispatch load failed', err);
        this.items = [];
        this.totalCount = 0;
        this.loading = false;
      }
    });
  }

  // Phase O.0 - filter helpers
  onFiltersChange(): void {
    this.page = 1;
    this.loadData();
  }
  clearFilters(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.showDeleted = '';
    this.filterFromDate = '';
    this.filterToDate = '';
    this.filterCustomerId = null;
    this.filterJobOrderId = null;
    this.filterBranchId = null;
    this.filterPosted = '';
    this.filterDispatchType = '';
    this.filterSaleType = '';
    this.page = 1;
    this.loadData();
  }
  dispatchStatusPill(row: any): { label: string; cssClass: string; icon: string } {
    if (row?.isReversed)
      return { label: 'Reversed', cssClass: 'bg-danger-subtle text-danger-emphasis border border-danger', icon: 'bi-arrow-counterclockwise' };
    if (row?.isPosted)
      return { label: 'Posted', cssClass: 'bg-success-subtle text-success-emphasis border border-success', icon: 'bi-check-circle-fill' };
    return { label: 'Draft', cssClass: 'bg-secondary-subtle text-secondary-emphasis border', icon: 'bi-pencil' };
  }

  // Phase O.1 - Dispatch 360 Profile
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
      },
      error: err => {
        this.profileLoading = false;
        console.error('Dispatch profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
  }

  // Phase O.1 - Print Challan (uses print-only CSS template)
  printChallan(): void {
    if (!this.profile) return;
    this.printingChallan = true;
    setTimeout(() => {
      window.print();
      setTimeout(() => { this.printingChallan = false; }, 250);
    }, 50);
  }

  loadCustomers(): void {
    this.svc.getCustomers(this.form.branchId).subscribe({
      next: res => {
        const raw = res?.data ?? [];

        this.customers = raw.map((x: any) => ({
          id: Number(x.id ?? x.Id ?? 0),
          name:
            x.name ??
            x.Name ??
            x.customerName ??
            x.CustomerName ??
            ''
        }));
      },
      error: () => {
        this.customers = [];
      }
    });
  }

  loadCustomerJobOrders(): void {
    this.jobOrders = [];
    this.form.jobOrderId = null;

    if (!this.form.customerId) {
      return;
    }

    this.svc.getCustomerJobOrders(this.form.customerId, this.form.branchId).subscribe({
      next: (res: any) => {
        console.log('Customer Job Orders response:', res);

        const raw: any[] = res?.data ?? res?.Data ?? [];

        this.jobOrders = raw.map((x: any) => ({
          id: Number(x.id ?? x.Id ?? x.jobOrderId ?? x.JobOrderId ?? 0),
          code: x.code ?? x.Code ?? x.jobOrderCode ?? x.JobOrderCode ?? '',
          productId: Number(x.productId ?? x.ProductId ?? 0),
          productName: x.productName ?? x.ProductName ?? '',
          displayText: x.displayText ?? x.DisplayText ?? ''
        })).filter(x => x.id > 0);

        console.log('Mapped Job Orders:', this.jobOrders);

        if (this.jobOrders.length === 1) {
          this.form.jobOrderId = this.jobOrders[0].id;
        }

        this.loadAvailableProducts();
      },
      error: err => {
        console.error('Customer job order load failed:', err);
        this.jobOrders = [];
        this.loadAvailableProducts();
      }
    });
  }

  isDirectSale(): boolean {
    return String(this.form?.saleType || '').toUpperCase() === 'DIRECT_SALE';
  }

  onCustomerChange(): void {

    console.log('Selected customer:', this.form.customerId);

    this.form.jobOrderId = null;
    this.jobOrders = [];

    if (this.isDirectSale()) {
      this.loadAvailableProducts();
      return;
    }

    this.loadCustomerJobOrders();
  }

  onJobOrderChange(): void {
    this.stockRows = [];
    this.form.items = [];

    if (this.form.saleType === 'CUSTOMER_DEMAND' && this.form.jobOrderId) {
      this.loadAvailableProducts();
    }
  }

  onSearch(): void {
    this.page = 1;
    this.loadData();
  }

  getSelectedJobOrderId(): number | null {
    const selected = (this.stockRows || []).find(x =>
      Number(x.dispatchQty || 0) > 0
    );

    return Number(
      (selected as any)?.jobOrderId ??
      (selected as any)?.JobOrderId ??
      0
    ) || null;
  }

  goToPage(p: number): void {
    const max = this.totalPages();
    if (p < 1 || p > max) return;

    this.page = p;
    this.loadData();
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  getPageStart(): number {
    return this.totalCount === 0 ? 0 : (this.page - 1) * this.pageSize + 1;
  }

  getPageEnd(): number {
    return Math.min(this.page * this.pageSize, this.totalCount);
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.selectedDispatchId = null;

    this.form = this.createEmptyForm();
    this.formIsPosted = false;

    this.stockRows = [];
    this.jobOrders = [];

    this.form.jobOrderId = null;

    this.openModal(this.formModal);
    
    if (this.form.saleType === 'DIRECT_SALE') {
      this.loadAvailableProducts();
    }
  }

 

  openEdit(id: number): void {
    this.modalMode = 'edit';
    this.selectedDispatchId = id;
    this.loadingEdit = true;

    this.svc.detail(id).subscribe({
      next: res => {
        this.loadingEdit = false;

        if (!res?.success || !res.data) {
          alert('Dispatch not found.');
          return;
        }

        const d = res.data;

        this.form = {
          ...this.createEmptyForm(),

          id: d.id,

          dispatchDate:
            d.dispatchDateInput ??
            new Date().toISOString().substring(0, 10),

          dispatchType: 'SALES',
          saleType: d.saleType ?? 'CUSTOMER_DEMAND',

          jobOrderId: d.jobOrderId ?? null,
          customerId: d.customerId ?? null,

          directCustomerName: d.directCustomerName ?? null,
          directCustomerMobile: d.directCustomerMobile ?? null,
          directCustomerAddress: d.directCustomerAddress ?? null,

          vehicleNo: d.vehicleNo ?? '',
          driverName: d.driverName ?? '',
          remarks: d.remarks ?? '',

          branchId: d.branchId ?? null,
          active: d.active ?? true,

          items: []
        };

        this.formIsPosted = d.isPosted === true;

        this.loadAvailableProducts();
        this.openModal(this.formModal);
      },
      error: () => {
        this.loadingEdit = false;
        alert('Failed to load dispatch.');
      }
    });
  }

  onSaleTypeChange(): void {
    this.stockRows = [];

    if (this.form.saleType === 'CUSTOMER_DEMAND') {
      this.form.directCustomerName = null;
      this.form.directCustomerMobile = null;
      this.form.directCustomerAddress = null;
    }

    if (this.form.saleType === 'DIRECT_SALE') {
      this.form.jobOrderId = null;
    }

    this.loadAvailableProducts();
  }
 
  onBranchChange(): void {
    this.stockRows = [];
    this.loadCustomers();
    this.loadAvailableProducts();
  }

  loadAvailableProducts(): void {
    const saleType = this.form.saleType as SaleType;

    const customerId =
      saleType === 'CUSTOMER_DEMAND'
        ? Number(this.form.customerId || 0)
        : null;

    const jobOrderId =
      saleType === 'CUSTOMER_DEMAND'
        ? Number(this.form.jobOrderId || 0)
        : null;

    if (saleType === 'CUSTOMER_DEMAND') {
      if (!customerId || customerId <= 0 || !jobOrderId || jobOrderId <= 0) {
        this.stockRows = [];
        this.form.items = [];
        return;
      }
    }

    this.svc.getAvailableProducts(
      this.form.branchId,
      '',
      customerId,
      saleType,
      jobOrderId,
      this.form.id && this.form.id > 0 ? this.form.id : null
    ).subscribe({
      next: (res: any) => {
        const raw: any[] = res?.data ?? res?.Data ?? [];

        this.stockRows = raw.map((x: any) => ({
          productId: Number(x.productId ?? x.ProductId ?? 0),
          productName: x.productName ?? x.ProductName ?? '-',

          jobOrderId: x.jobOrderId ?? x.JobOrderId ?? null,
          jobOrderCode: x.jobOrderCode ?? x.JobOrderCode ?? '',

          customerReservedStock: Number(x.customerReservedStock ?? x.CustomerReservedStock ?? 0),
          freeCompanyStock: Number(x.freeCompanyStock ?? x.FreeCompanyStock ?? 0),
          totalFinishedStock: Number(x.totalFinishedStock ?? x.TotalFinishedStock ?? 0),

          availableQty: Number(x.availableQty ?? x.AvailableQty ?? 0),
          dispatchQty: 0,

          costPrice: Number(x.costPrice ?? x.CostPrice ?? 0),
          baseSalePrice: Number(x.baseSalePrice ?? x.BaseSalePrice ?? 0),
          discountPercent: 0,
          discountAmount: 0,
          salePrice: Number(x.salePrice ?? x.SalePrice ?? x.baseSalePrice ?? x.BaseSalePrice ?? 0),
          amount: 0,

          unit: x.unit ?? x.Unit ?? 'PCS'
        }));

        this.form.items = this.stockRows.map(x => ({
          id: 0,
          dispatchId: this.form.id || 0,
          productId: x.productId,
          dispatchQty: 0,

          costPrice: x.costPrice,
          baseSalePrice: x.baseSalePrice,
          discountPercent: 0,
          discountAmount: 0,
          salePrice: x.salePrice,
          amount: 0,

          unit: x.unit,
          remarks: '',
          active: true
        }));

        // Defensive fallback: for any row whose Cost / Base Price are still
        // zero (backend's fallback chain ran dry — e.g. no QC history,
        // no Gen_Products.Price, MVC not rebuilt), look up the saved Design
        // Costing for that product and patch in productCostPerUnit and
        // sellingPricePerUnit. Goes through an endpoint that already exists.
        this.stockRows.forEach((row, idx) => {
          if (Number(row.costPrice) > 0 && Number(row.baseSalePrice) > 0) return;
          this.fillFromDesignCosting(row, idx);
        });
      },
      error: () => {
        this.stockRows = [];
        this.form.items = [];
      }
    });
  }

  /**
   * Patches a single dispatch row's Cost / PC and Base Price from the saved
   * Design Costing for that product, when the backend's own fallback chain
   * came up empty. Hits the existing `/design-costing/list?productId={id}`
   * endpoint — no MVC rebuild required.
   */
  private fillFromDesignCosting(row: any, idx: number): void {
    const pid = Number(row.productId || 0);
    if (pid <= 0) return;

    const url = `/emb-products/design-costing/list?productId=${pid}&activeFilter=active`;

    this.http.get<any>(url).subscribe({
      next: (res: any) => {
        const items: any[] = res?.data?.items ?? res?.Data?.items ?? [];
        if (!items.length) return;

        const top = items[0];
        const cost = Number(top.productCostPerUnit ?? top.ProductCostPerUnit ?? 0);
        const sell = Number(top.sellingPricePerUnit ?? top.SellingPricePerUnit ?? 0);

        if (Number(row.costPrice) <= 0 && cost > 0) row.costPrice = cost;
        if (Number(row.baseSalePrice) <= 0 && sell > 0) row.baseSalePrice = sell;

        if (this.form.items && this.form.items[idx]) {
          if (Number(this.form.items[idx].costPrice) <= 0 && cost > 0)
            this.form.items[idx].costPrice = cost;
          if (Number(this.form.items[idx].baseSalePrice) <= 0 && sell > 0)
            this.form.items[idx].baseSalePrice = sell;
        }

        this.recalculateDispatchRow(row);
      },
      error: (e) => {
        console.warn('Design costing lookup failed:', e);
      }
    });
  }

  onQtyChange(r: any): void {
    const qty = Number(r.dispatchQty ?? 0);
    const available = Number(r.availableQty ?? 0);

    if (qty > available) {
      r.dispatchQty = available;
    }

    if (qty < 0) {
      r.dispatchQty = 0;
    }

    this.recalculateDispatchRow(r);
  }

  hasDispatchQty(): boolean {
    return this.stockRows.some(x => Number(x.dispatchQty || 0) > 0);
  }

  getBalance(row: DispatchStockRow): number {
    return Number(row.availableQty || 0) - Number(row.dispatchQty || 0);
  }

  save(): void {
    if (this.saving) return;
    this.stockRows.forEach(r => this.recalculateDispatchRow(r));
    this.saving = true;

    const dto: DispatchDto = {
      id: this.form.id || 0,
      dispatchDate: this.form.dispatchDate,

      dispatchType: 'SALES',
      saleType: this.form.saleType,

      jobOrderId:
        this.form.saleType === 'CUSTOMER_DEMAND'
          ? this.form.jobOrderId
          : null,

      customerId: this.form.customerId,

      directCustomerName:
        this.form.saleType === 'DIRECT_SALE'
          ? this.form.directCustomerName
          : null,

      directCustomerMobile:
        this.form.saleType === 'DIRECT_SALE'
          ? this.form.directCustomerMobile
          : null,

      directCustomerAddress:
        this.form.saleType === 'DIRECT_SALE'
          ? this.form.directCustomerAddress
          : null,

      branchId: this.form.branchId,

      vehicleNo: this.form.vehicleNo || '',
      driverName: this.form.driverName || '',
      remarks: this.form.remarks || '',

      active: this.form.active,

      items: this.stockRows
        .filter(x => Number(x.dispatchQty ?? 0) > 0)
        .map(x => {
          const dispatchQty = Number(x.dispatchQty ?? 0);
          const salePrice = Number(x.salePrice ?? 0);

          return {
            id: 0,
            dispatchId: this.form.id || 0,
            productId: Number(x.productId ?? 0),

            dispatchQty,

            costPrice: Number(x.costPrice ?? 0),
            baseSalePrice: Number(x.baseSalePrice ?? 0),
            discountPercent: Number(x.discountPercent ?? 0),
            discountAmount: Number(x.discountAmount ?? 0),
            salePrice,
            amount: dispatchQty * salePrice,

            unit: x.unit || 'PCS',
            remarks: '',
            active: true
          };
        })
    };

    if (!dto.dispatchDate) {
      alert('Dispatch date is required.');
      this.saving = false;
      return;
    }

    if (dto.saleType === 'CUSTOMER_DEMAND') {
      if (!dto.customerId) {
        alert('Customer is required for customer demand dispatch.');
        this.saving = false;
        return;
      }

      if (!dto.jobOrderId) {
        alert('Job Order is required for customer demand dispatch.');
        this.saving = false;
        return;
      }
    }

    if (dto.saleType === 'DIRECT_SALE') {
      if (!dto.customerId && !dto.directCustomerName?.trim()) {
        alert('Customer or walk-in customer name is required for direct sale.');
        this.saving = false;
        return;
      }
    }

    if (!dto.items.length) {
      alert('Enter dispatch quantity.');
      this.saving = false;
      return;
    }

    if (dto.items.some(x => Number(x.salePrice ?? 0) < 0 || Number(x.amount ?? 0) < 0)) {
      alert('Sale price or amount cannot be negative.');
      this.saving = false;
      return;
    }

    if (dto.items.some(x => x.productId <= 0)) {
      alert('Invalid product found.');
      this.saving = false;
      return;
    }

    // Below-cost guard: any line where SalePrice < CostPrice forces a
    // confirmation so the user can't accidentally book a loss.
    const belowCost = dto.items.filter(x =>
      Number(x.costPrice ?? 0) > 0 &&
      Number(x.salePrice ?? 0) > 0 &&
      Number(x.salePrice ?? 0) < Number(x.costPrice ?? 0)
    );

    if (belowCost.length > 0) {
      const totalLoss = belowCost.reduce(
        (sum, x) => sum + (Number(x.costPrice) - Number(x.salePrice)) * Number(x.dispatchQty ?? 0),
        0
      );
      const ok = confirm(
        `Warning: ${belowCost.length} line(s) are priced BELOW cost.\n\n` +
        `Estimated loss on this dispatch: ${totalLoss.toLocaleString(undefined, { maximumFractionDigits: 2 })}\n\n` +
        `Save anyway?`
      );
      if (!ok) {
        this.saving = false;
        return;
      }
    }

    const request$ = dto.id > 0
      ? this.svc.update(dto.id, dto)
      : this.svc.create(dto);

    request$.subscribe({
      next: (res: any) => {
        this.saving = false;

        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? 'Dispatch saved successfully.';

        if (!success) {
          alert(message);
          return;
        }

        alert(message);
        this.loadData();
        this.closeForm();
      },
      error: err => {
        this.saving = false;

        const msg =
          err?.error?.message ||
          err?.error?.Message ||
          err?.message ||
          'Dispatch save failed.';

        alert(msg);
      }
    });
  }

  canPostRow(row: DispatchRowViewModel): boolean {
    return this.canPostRole && !row.isPosted && !row.isReversed;
  }

  postDispatch(id: number): void {
    if (!this.canPostRole) {
      alert('Not authorized.');
      return;
    }

    if (!confirm(`Post dispatch #${id}?`)) return;

    this.svc.post(id).subscribe({
      next: (res: any) => {
        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? 'Dispatch posted successfully.';

        if (!success) {
          alert(message);
          return;
        }

        alert(message);

        // Auto-open the Sale Bill (Invoice) PDF after a successful post.
        this.openSaleBill(id);

        this.loadData();

        this.stockRows = [];
        this.jobOrders = [];

        if (this.form?.customerId) {
          this.loadCustomerJobOrders();
        }

        this.closeModal(this.formModal);
      },
      error: err => {
        console.error(err);
        alert(err?.error?.message || 'Failed to post dispatch.');
      }
    });
  }

  confirmPost(): void {
    const id = Number(this.selectedDispatchId || 0);

    if (id <= 0) {
      alert('Invalid dispatch selected.');
      return;
    }

    this.loading = true;

    this.svc.post(id).subscribe({
      next: (res: any) => {
        this.loading = false;

        const success = res?.success ?? res?.Success ?? false;
        const message = res?.message ?? res?.Message ?? 'Dispatch posted successfully.';

        if (!success) {
          alert(message);
          return;
        }

        alert(message);
        this.closeModalByElementId('postModal');
        // Auto-open the Sale Bill (Invoice) PDF in a new tab right after a
        // successful post. The user explicitly asked for this behaviour so
        // the printable invoice is one click away from the post action.
        this.openSaleBill(id);
        this.loadData();
      },
      error: err => {
        this.loading = false;
        alert(err?.error?.message || 'Post failed.');
      }
    });
  }

  openDetail(id: number): void {
    this.svc.detail(id).subscribe(res => {
      this.detailData = res.data ?? null;
      // Sprint-2: snapshot stepper state from the detail payload and
      // lazy-load the audit timeline into the drawer so the workflow
      // + history render side-by-side.
      const stages = (this.detailData as any)?.workflowStages;
      const idx    = (this.detailData as any)?.workflowStageIndex;
      if (Array.isArray(stages) && stages.length) this.workflowStages = stages;
      if (typeof idx === 'number') this.workflowStageIndex = idx;
      this.loadAuditLog(id);
      this.openModal(this.detailModal);
    });
  }

  closeDetail(): void {
    this.detailData = null;
    this.auditLog = [];
    this.closeModal(this.detailModal);
  }

  openItems(dispatchId: number): void {
    this.router.navigate(['/dispatch', dispatchId, 'items']);
  }

  openDelete(id: number): void {
    this.deleteId = id;
    this.openModal(this.deleteModal);
  }

  closeDelete(): void {
    this.deleteId = null;
    this.closeModal(this.deleteModal);
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe({
      next: (res: any) => {
        const success = res?.success ?? res?.Success ?? true;
        const message = res?.message ?? res?.Message ?? 'Delete failed.';

        if (!success) {
          alert(message);
          return;
        }

        this.closeDelete();
        this.loadData();
      },
      error: err => {
        alert(err?.error?.message || 'Delete failed.');
      }
    });
  }

  openReverse(id: number): void {
    this.reverseId = id;
    this.openModal(this.reverseModal);
  }

  closeReverse(): void {
    this.reverseId = null;
    this.closeModal(this.reverseModal);
  }

  confirmReverse(): void {
    if (!this.reverseId) return;

    this.svc.reverse(this.reverseId).subscribe({
      next: (res: any) => {
        const success = res?.success ?? res?.Success ?? true;
        const message = res?.message ?? res?.Message ?? 'Reverse failed.';

        if (!success) {
          alert(message);
          return;
        }

        this.closeReverse();
        this.loadData();
      },
      error: err => {
        alert(err?.error?.message || 'Reverse failed.');
      }
    });
  }

  // Sale Bill / Invoice — opens the server-rendered PDF in a new tab.
  // The backend endpoint generates the invoice on-demand from the dispatch
  // header + items + customer info. Inline disposition so the browser shows
  // it in-tab; operator can print or save from the PDF viewer.
  openSaleBill(id: number): void {
    if (!id) return;
    window.open('/emb-products/dispatch/sale-bill/' + id, '_blank');
  }

  openLedger(id: number): void {

    this.svc.getLedger(id).subscribe({
      next: (res: any) => {

        const raw: any[] = res?.data ?? res?.Data ?? [];

        this.ledgerRows = raw.map((x: any) => ({
          date:
            x.date ??
            x.Date ??
            x.createdDate ??
            x.CreatedDate ??
            '',

          product:
            x.product ??
            x.Product ??
            x.productName ??
            x.ProductName ??
            '-',

          qtyIn: Number(
            x.qtyIn ??
            x.QtyIn ??
            0
          ),

          qtyOut: Number(
            x.qtyOut ??
            x.QtyOut ??
            0
          ),

          // Running balance after this row (IN - OUT cumulative).
          balance: Number(
            x.balance ??
            x.Balance ??
            0
          ),

          type:
            x.type ??
            x.Type ??
            x.transactionType ??
            x.TransactionType ??
            '',

          // True for rows that belong to the dispatch the user clicked on —
          // template highlights these in bold + amber background.
          isCurrent: Boolean(x.isCurrent ?? x.IsCurrent ?? false),

          remarks:
            x.remarks ??
            x.Remarks ??
            ''
        }));

        console.log('Ledger Rows:', this.ledgerRows);

        this.openModal(this.ledgerModal);
      },

      error: err => {
        console.error('Ledger load failed:', err);
        this.ledgerRows = [];
      }
    });
  }

  closeLedger(): void {
    this.ledgerRows = [];
    this.closeModal(this.ledgerModal);
  }

  closeForm(): void {
    this.form = this.createEmptyForm();
    this.formIsPosted = false;
    this.selectedDispatchId = null;
    this.stockRows = [];
    this.closeModal(this.formModal);
  }

  onDispatchTypeChange(): void {
    this.form.dispatchType = 'SALES';
  }

  openModal(ref: ElementRef | HTMLElement): void {
    const el = ref instanceof ElementRef ? ref.nativeElement : ref;
    new bootstrap.Modal(el, { backdrop: 'static', keyboard: false }).show();
  }

  closeModal(modalRef: any): void {
    const modalEl = modalRef?.nativeElement || modalRef;
    const modal = bootstrap.Modal.getInstance(modalEl);

    if (modal) {
      modal.hide();
    }

    document.body.classList.remove('modal-open');
    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());
  }

  closeModalByElementId(id: string): void {
    const el = document.querySelector(`[data-modal-id="${id}"]`) as HTMLElement;

    if (el && typeof bootstrap !== 'undefined') {
      const modal = bootstrap.Modal.getInstance(el);
      if (modal) modal.hide();
    }

    document.body.classList.remove('modal-open');
    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());
  }

  getTotalDispatchQty(): number {
    return this.stockRows.reduce((sum, x) =>
      sum + Number(x.dispatchQty ?? 0), 0);
  }

  getTotalDiscountAmount(): number {
    return this.stockRows.reduce((sum, x) => {
      const base = Number(x.baseSalePrice ?? 0);
      const qty = Number(x.dispatchQty ?? 0);
      const discountPercent = Number(x.discountPercent ?? 0);
      const discountAmount = Number(x.discountAmount ?? 0);

      const percentDiscount = base * discountPercent / 100;
      const totalDiscountPerUnit = percentDiscount + discountAmount;

      return sum + (totalDiscountPerUnit * qty);
    }, 0);
  }

  getTotalDiscountPercent(): number {
    const totalBase = this.stockRows.reduce((sum, x) =>
      sum + (Number(x.baseSalePrice ?? 0) * Number(x.dispatchQty ?? 0)), 0);

    const totalDiscount = this.getTotalDiscountAmount();

    if (totalBase <= 0) return 0;

    return (totalDiscount / totalBase) * 100;
  }

  getGrandTotal(): number {
    return this.stockRows.reduce((sum, x) =>
      sum + Number(x.amount ?? 0), 0);
  }

  getTotalManualDiscountAmount(): number {
    return this.stockRows.reduce((sum, x) => {
      const qty = Number(x.dispatchQty ?? 0);
      const manualDiscount = Number(x.discountAmount ?? 0);
      return sum + (manualDiscount * qty);
    }, 0);
  }

  print(): void {
    window.print();
  }

  parseDotNetDate(value: any): Date | null {
    if (!value) return null;

    const match = /\/Date\((\d+)\)\//.exec(value);
    return match ? new Date(+match[1]) : new Date(value);
  }

  /** Margin badge color: green for profit, red for loss, muted when no cost yet. */
  getMarginClass(row: DispatchRowViewModel): string {
    const cost = Number(row?.totalCost ?? 0);
    if (cost <= 0) return 'bg-light text-muted border';
    return Number(row?.totalMargin ?? 0) < 0
      ? 'bg-danger-subtle text-danger-emphasis border border-danger'
      : 'bg-success-subtle text-success-emphasis border border-success';
  }

  /** Human label for the margin pill. */
  getMarginLabel(row: DispatchRowViewModel): string {
    const cost = Number(row?.totalCost ?? 0);
    if (cost <= 0) return 'No cost data';
    const margin = Number(row?.totalMargin ?? 0);
    const pct = Number(row?.marginPct ?? 0);
    const sign = margin >= 0 ? '+' : '';
    return `${sign}${margin.toLocaleString(undefined, { maximumFractionDigits: 0 })} (${pct.toFixed(1)}%)`;
  }

  exportExcel(): void {
    const rows = this.items.map(x => ({
      ID: x.id,
      Date: x.dispatchDateText,
      Type: x.saleTypeText ?? x.saleType,
      Customer: x.displayCustomerName ?? x.customerName ?? x.directCustomerName ?? '',
      Product: x.productName ?? '',
      Qty: x.dispatchQtyTotal,
      'Sale Price': x.salePrice ?? x.baseSalePrice ?? 0,
      Amount: x.totalAmount ?? 0,
      Status: x.statusText ?? (x.isPosted ? 'Posted' : 'Draft')
    }));

    const ws = XLSX.utils.json_to_sheet(rows);
    const wb = XLSX.utils.book_new();

    XLSX.utils.book_append_sheet(wb, ws, 'Dispatch');
    XLSX.writeFile(wb, 'Dispatch.xlsx');
  }

  exportPdf(): void {
    const doc = new jsPDF();

    autoTable(doc, {
      head: [['ID', 'Date', 'Sale Type', 'Customer', 'Product', 'Qty', 'Sale Price', 'Amount', 'Status']],
      body: this.items.map(x => [
        x.id,
        x.dispatchDateText,
        x.saleTypeText ?? x.saleType,
        x.displayCustomerName ?? x.customerName ?? x.directCustomerName ?? '',
        x.productName ?? '',
        x.dispatchQtyTotal,
        x.salePrice ?? x.baseSalePrice ?? 0,
        x.totalAmount ?? 0,
        x.statusText ?? (x.isPosted ? 'Posted' : 'Draft')
      ])
    });

    doc.save('Dispatch.pdf');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-1  CLICKABLE BADGES
  // ──────────────────────────────────────────────────────────────────
  gotoCustomer(row: any): void {
    if (!row?.customerId) return;
    location.href = '/Content/emb-products/customers?id=' + row.customerId;
  }
  gotoProduct(row: any): void {
    if (!row?.productId) return;
    location.href = '/Content/emb-products/products?id=' + row.productId;
  }

  // Sprint-1: status pill helper already exists earlier in the class
  // (line 318); the template binds to it directly.

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  STEPPER state (Draft → Posted → Reversed).
  // ──────────────────────────────────────────────────────────────────
  stepperState(stageIdx: number): { done: boolean; current: boolean; cancelled: boolean } {
    const idx = this.workflowStageIndex;
    const isReversed = idx === 2;
    if (stageIdx === 2)
      return { done: false, current: isReversed, cancelled: true };
    if (isReversed)
      return { done: stageIdx <= 0, current: false, cancelled: false };
    return { done: stageIdx < idx, current: stageIdx === idx, cancelled: false };
  }
  stageLabel(stage: string): string {
    const m: { [k: string]: string } = { DRAFT: 'Draft', POSTED: 'Posted', REVERSED: 'Reversed' };
    return m[(stage || '').toUpperCase()] || stage;
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  AUDIT LOG loader.
  // ──────────────────────────────────────────────────────────────────
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.svc.getAuditLog(id).subscribe({
      next: (res: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(res?.data) ? res.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')                           return 'bg-primary';
    if (a === 'EDITED')                            return 'bg-warning text-dark';
    if (a === 'POSTED')                            return 'bg-success';
    if (a === 'REVERSED' || a === 'DELETED')       return 'bg-danger';
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
    return this.items.length > 0 && this.items.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.items.forEach((r: any) => this.selectedIds.delete(r.id));
    else this.items.forEach((r: any) => this.selectedIds.add(r.id));
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

  bulkPost(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Post ${ids.length} draft dispatch(es)? Stock will be written out and cost prices locked.`)) return;
    this.bulkBusy = true;
    this.svc.bulkPost(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'posted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk post failed.'); }
    });
  }
  bulkReverse(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Reverse ${ids.length} posted dispatch(es)? Stock will be restored.`)) return;
    this.bulkBusy = true;
    this.svc.bulkReverse(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'reversed'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk reverse failed.'); }
    });
  }
  bulkDelete(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} draft dispatch(es)? Posted ones are skipped.`)) return;
    this.bulkBusy = true;
    this.svc.bulkDelete(ids).subscribe({
      next: r => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: e => { this.bulkBusy = false; alert(e?.error?.message || e?.message || 'Bulk delete failed.'); }
    });
  }
}
