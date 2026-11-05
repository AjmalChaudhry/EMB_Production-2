// ================= IMPORTS =================
import {Component,OnInit,AfterViewInit,ViewChild,ElementRef} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import * as bootstrap from 'bootstrap';
import { DesignCostingService } from '../services/design-costing.service';
import {DesignCostingDto,DesignCostingListRow,DesignCostingDetail
} from '../shared/models/design-costing.dto';
import { ApiResponse } from '../shared/models/api-response';

// ================= COMPONENT =================
@Component({
  selector: 'app-design-costing',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './design-costing.html',
  styleUrls: ['./design-costing.css']
})
export class DesignCostingComponent implements OnInit, AfterViewInit {
  // ================= STATE =================
  rows: DesignCostingListRow[] = [];
  loading = false;
  isEdit = false;

  activeFilter = '';
  searchText = '';

  // Phase R.0 filters
  filterProductId: number | null = null;
  filterDesignId: number | null = null;
  filterMarginRange = '';

  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    totalBatchQty: 0,
    totalMaterialCost: 0,
    totalProductionCost: 0,
    totalServiceCost: 0,
    totalProductCost: 0,
    totalSellingPrice: 0,
    estimatedProfit: 0,
    avgMargin: 0,
    avgCostPerUnit: 0,
    avgSellingPerUnit: 0
  };

  // Phase R.0 profile drawer
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ================= PAGINATION =================
  page = 1;
  pageSize = 10;
  totalCount = 0;
  totalPages = 1;

  // ================= DETAIL & DELETE =================
  detailData: DesignCostingDetail | null = null;
  selectedRow: DesignCostingListRow | null = null;
  deleteId = 0;

  // ================= FORM =================
  form: DesignCostingDto = this.getEmptyForm();

  // ================= DROPDOWNS =================
  products: { id: number; code: string; name: string; display: string }[] = [];
  designs: { id: number; display: string; productId?: number | null }[] = [];
  filteredDesigns: { id: number; display: string; productId?: number | null }[] = [];

  selectedProductId: number | null = null;

  // ================= MODALS =================
  @ViewChild('detailModal') detailModal?: ElementRef;
  @ViewChild('deleteModal') deleteModal?: ElementRef;
  @ViewChild('formModal') formModal?: ElementRef;

  private detailModalInstance?: bootstrap.Modal;
  private deleteModalInstance?: bootstrap.Modal;
  private formModalInstance?: bootstrap.Modal;

  constructor(
    private svc: DesignCostingService,
    private http: HttpClient
  ) { }

  // ==========================================================
  // LIFECYCLE
  // ==========================================================
  ngOnInit(): void {
    this.loadDropdowns();
    this.load();
  }

  ngAfterViewInit(): void {
    this.initializeModals();
  }

  private initializeModals(): void {
    if (this.formModal?.nativeElement && !this.formModalInstance) {
      this.formModalInstance = new bootstrap.Modal(this.formModal.nativeElement, {
        backdrop: 'static',
        keyboard: false
      });
    }

    if (this.detailModal?.nativeElement && !this.detailModalInstance) {
      this.detailModalInstance = new bootstrap.Modal(this.detailModal.nativeElement);
    }

    if (this.deleteModal?.nativeElement && !this.deleteModalInstance) {
      this.deleteModalInstance = new bootstrap.Modal(this.deleteModal.nativeElement, {
        backdrop: 'static',
        keyboard: false
      });
    }
  }
  
  load(): void {
    this.loading = true;

    this.svc.list({
      page: this.page || 1,
      pageSize: this.pageSize || 10,
      activeFilter: this.activeFilter || '',
      search: this.searchText || '',
      productId: this.filterProductId,
      designId: this.filterDesignId,
      marginRange: this.filterMarginRange || null
    }).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success) {
          this.rows = [];
          this.totalCount = 0;
          this.totalPages = 1;
          return;
        }

        const data = res?.data ?? res?.Data ?? [];
        const items = Array.isArray(data) ? data : (data?.items ?? data?.Items ?? []);

        const sm = res?.summary ?? null;
        if (sm) {
          this.summary = {
            count: Number(sm.count ?? 0),
            activeCount: Number(sm.activeCount ?? 0),
            inactiveCount: Number(sm.inactiveCount ?? 0),
            totalBatchQty: Number(sm.totalBatchQty ?? 0),
            totalMaterialCost: Number(sm.totalMaterialCost ?? 0),
            totalProductionCost: Number(sm.totalProductionCost ?? 0),
            totalServiceCost: Number(sm.totalServiceCost ?? 0),
            totalProductCost: Number(sm.totalProductCost ?? 0),
            totalSellingPrice: Number(sm.totalSellingPrice ?? 0),
            estimatedProfit: Number(sm.estimatedProfit ?? 0),
            avgMargin: Number(sm.avgMargin ?? 0),
            avgCostPerUnit: Number(sm.avgCostPerUnit ?? 0),
            avgSellingPerUnit: Number(sm.avgSellingPerUnit ?? 0)
          };
        }

        this.rows = items.map((x: any) => ({
          id: Number(x.id ?? x.Id ?? 0),

          designId: Number(x.designId ?? x.DesignId ?? 0),
          designName: x.designName ?? x.DesignName ?? '-',

          productId: Number(x.productId ?? x.ProductId ?? 0),
          productCode: x.productCode ?? x.ProductCode ?? '',
          productName: x.productName ?? x.ProductName ?? '-',

          batchQty: Number(x.batchQty ?? x.BatchQty ?? 0),

          materialCostPerUnit: Number(x.materialCostPerUnit ?? x.MaterialCostPerUnit ?? 0),
          productionCostPerUnit: Number(x.productionCostPerUnit ?? x.ProductionCostPerUnit ?? 0),
          serviceCostPerUnit: Number(x.serviceCostPerUnit ?? x.ServiceCostPerUnit ?? 0),
          productCostPerUnit: Number(x.productCostPerUnit ?? x.ProductCostPerUnit ?? 0),
          totalProductCost: Number(x.totalProductCost ?? x.TotalProductCost ?? 0),

          sellingPricePerUnit: Number(x.sellingPricePerUnit ?? x.SellingPricePerUnit ?? 0),
          totalSellingPrice: Number(x.totalSellingPrice ?? x.TotalSellingPrice ?? 0),

          active: (x.active ?? x.Active ?? true) === true
        }));

        this.totalCount = this.rows.length;
        this.totalPages = 1;

        console.log('MAPPED COSTING ROWS:', this.rows);
      },
      error: err => {
        this.loading = false;
        console.error('Load error:', err);
        this.rows = [];
      }
    });
  }

  // ==========================================================
  // DROPDOWNS
  // ==========================================================
  loadDropdowns(): void {
    // Products — used to filter designs
    this.http.get<{ data: any[] }>('/emb-products/dropdowns/products').subscribe({
      next: res => {
        const data = res?.data ?? [];
        this.products = data.map(x => ({
          id: Number(x.id ?? x.Id ?? 0),
          code: x.code ?? x.Code ?? '',
          name: x.name ?? x.Name ?? '',
          display: (x.code ?? '') ? `${x.code} - ${x.name}` : (x.name ?? '')
        }));
      },
      error: err => {
        console.error('Product dropdown error:', err);
        this.products = [];
      }
    });

    // Designs (full list) — used when no product filter applied
    this.svc.getDesignDropdown().subscribe({
      next: (res: any) => {
        if (res?.success && Array.isArray(res.data)) {
          this.designs = res.data.map((x: any) => ({
            id: Number(x.id ?? x.Id ?? 0),
            display: x.display ?? x.name ?? x.Name ?? '',
            productId: x.productId ?? x.ProductId ?? null
          }));
        } else {
          this.designs = [];
        }
        this.filteredDesigns = this.designs.slice();
      },
      error: (err: any) => {
        console.error('Design dropdown error:', err);
        this.designs = [];
        this.filteredDesigns = [];
      }
    });
  }

  // ==========================================================
  // PRODUCT → DESIGN LINKAGE
  // ==========================================================
  onProductChange(): void {
    // Clear current design, then reload designs for the selected product
    this.form.designId = null;
    this.clearCostFields();

    if (!this.selectedProductId || this.selectedProductId <= 0) {
      this.filteredDesigns = this.designs.slice();
      return;
    }

    this.http.get<{ data: any[] }>(
      `/emb-products/dropdowns/designs-by-product?productId=${this.selectedProductId}`
    ).subscribe({
      next: res => {
        const list = res?.data ?? [];
        this.filteredDesigns = list.map((x: any) => ({
          id: Number(x.id ?? x.Id ?? 0),
          display: (x.code ?? x.Code ?? '') + ' - ' + (x.dNo ?? x.DNo ?? x.name ?? ''),
          productId: this.selectedProductId
        }));

        // Auto-select the only design if there's exactly one for this product
        if (this.filteredDesigns.length === 1) {
          this.form.designId = this.filteredDesigns[0].id;
          this.onDesignChange();
        }
      },
      error: err => {
        console.error('designs-by-product error:', err);
        this.filteredDesigns = [];
      }
    });
  }

  onDesignChange(): void {
    // Auto-recalculate as soon as a design is chosen (and batch qty is set)
    if (this.form.designId && this.form.designId > 0
        && this.form.batchQty && this.form.batchQty > 0) {
      this.recalculate();
    }
  }

  private clearCostFields(): void {
    this.form.threadCost = 0;
    this.form.machineCost = 0;
    this.form.laborCost = 0;
    this.form.electricityCost = 0;
    this.form.otherCost = 0;

    this.form.totalCost = 0;
    this.form.costPerUnit = 0;

    this.form.sellingPricePerUnit = 0;
    this.form.totalSellingPrice = 0;

    this.form.materialCostPerUnit = 0;
    this.form.productionCostPerUnit = 0;
    this.form.serviceCostPerUnit = 0;
    this.form.productCostPerUnit = 0;
    this.form.totalProductCost = 0;
  }

  // ==========================================================
  // PAGINATION / FILTER
  // ==========================================================
  changePage(p: number): void {
    if (p < 1 || p > this.totalPages || p === this.page) return;

    this.page = p;
    this.load();
  }

  // Phase R.0 - filter helpers + profile
  onFiltersChange(): void { this.page = 1; this.load(); }
  clearFilters(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.filterProductId = null;
    this.filterDesignId = null;
    this.filterMarginRange = '';
    this.page = 1;
    this.load();
  }
  openProfile(id: number): void {
    const numericId = Number(id);
    if (!numericId || numericId <= 0) return;
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;
    this.svc.getProfile(numericId).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) { alert(res?.message || 'Failed to load profile.'); this.showProfile = false; return; }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('DesignCosting profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }
  closeProfile(): void { this.showProfile = false; this.profile = null; }

  onFilterChange(): void {
    this.page = 1;
    this.load();
  }

  onSearch(): void {
    this.page = 1;
    this.load();
  }

  // ==========================================================
  // CALCULATE TOTAL
  // ==========================================================
  calculateTotal(): void {
    const total =
      (Number(this.form.threadCost) || 0) +
      (Number(this.form.machineCost) || 0) +
      (Number(this.form.laborCost) || 0) +
      (Number(this.form.electricityCost) || 0) +
      (Number(this.form.otherCost) || 0);

    this.form.totalCost = Math.round(total);

    const qty = Number(this.form.batchQty) || 0;
    this.form.costPerUnit = qty > 0 ? Math.round(total / qty) : 0;

    const margin = Number(this.form.marginPercent) || 20;

    this.form.sellingPricePerUnit = Math.round(
      this.form.costPerUnit * (1 + margin / 100)
    );

    this.form.totalSellingPrice = Math.round(
      this.form.sellingPricePerUnit * qty
    );
  }

  // ==========================================================
  // RECALCULATE
  // ==========================================================
  recalculate(): void {
    if (!this.selectedProductId || Number(this.selectedProductId) <= 0) {
      alert('Product is required');
      return;
    }

    if (!this.form.designId || Number(this.form.designId) <= 0) {
      alert('Design is required');
      return;
    }

    if (!this.form.batchQty || Number(this.form.batchQty) <= 0) {
      alert('Batch quantity must be greater than zero');
      return;
    }

    this.loading = true;

    this.svc.recalculate(
      Number(this.form.designId),
      Number(this.form.batchQty),
      Number(this.form.marginPercent || 20),
      Number(this.selectedProductId)
    ).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success || !res?.data) {
          // Backend's JsonError envelope is { success, message, detail }.
          // The catch wrapper sets message = "Failed to recalculate costing."
          // and the underlying ex.Message goes into detail — surface both
          // so the cause is actually debuggable.
          const msg = res?.message || 'Recalculation failed';
          const detail = res?.detail && res.detail !== res.message ? `\n\nDetail: ${res.detail}` : '';
          alert(msg + detail);
          return;
        }

        const d: any = res.data;

        this.form.productId = Number(this.selectedProductId);

        this.form.threadCost = Number(d.threadCost ?? d.ThreadCost ?? 0);
        this.form.machineCost = Number(d.machineCost ?? d.MachineCost ?? 0);
        this.form.laborCost = Number(d.laborCost ?? d.LaborCost ?? 0);
        this.form.electricityCost = Number(d.electricityCost ?? d.ElectricityCost ?? 0);
        this.form.otherCost = Number(d.otherCost ?? d.OtherCost ?? 0);

        this.form.totalCost = Number(d.totalCost ?? d.TotalCost ?? 0);
        this.form.costPerUnit = Number(d.costPerUnit ?? d.CostPerUnit ?? 0);

        this.form.marginPercent = Number(
          d.marginPercent ?? d.MarginPercent ?? this.form.marginPercent ?? 20
        );

        this.form.sellingPricePerUnit = Number(
          d.sellingPricePerUnit ?? d.SellingPricePerUnit ?? 0
        );

        this.form.totalSellingPrice = Number(
          d.totalSellingPrice ??
          d.TotalSellingPrice ??
          (this.form.sellingPricePerUnit * Number(this.form.batchQty ?? 0))
        );

        // Per-unit product cost breakdown — defensively computed.
        // If the backend returned a value, use it; otherwise fall back to a
        // local derivation from the cost breakdown that already populated.
        // This guarantees the form never shows zeros once the breakdown is
        // populated, even if the MVC backend hasn't been rebuilt with the
        // latest controller changes.

        const qty = Math.max(Number(this.form.batchQty || 0), 1);

        const backendMaterial   = Number(d.materialCostPerUnit   ?? d.MaterialCostPerUnit   ?? 0);
        const backendProduction = Number(d.productionCostPerUnit ?? d.ProductionCostPerUnit ?? 0);
        const backendService    = Number(d.serviceCostPerUnit    ?? d.ServiceCostPerUnit    ?? 0);
        const backendProductCpu = Number(d.productCostPerUnit    ?? d.ProductCostPerUnit    ?? 0);
        const backendTotalProd  = Number(d.totalProductCost      ?? d.TotalProductCost      ?? 0);

        // Production per unit always derivable from the cost breakdown.
        const derivedProduction =
          (this.form.machineCost + this.form.laborCost +
           this.form.electricityCost + this.form.otherCost) / qty;

        // Material per unit: thread is the absolute minimum; backend may
        // add BOM-walked raw material value on top.
        const derivedMaterial = this.form.threadCost / qty;

        this.form.materialCostPerUnit   = Math.round(Math.max(backendMaterial,   derivedMaterial));
        this.form.productionCostPerUnit = Math.round(Math.max(backendProduction, derivedProduction));
        this.form.serviceCostPerUnit    = Math.round(backendService);

        const derivedProductCpu =
          this.form.materialCostPerUnit +
          this.form.productionCostPerUnit +
          this.form.serviceCostPerUnit;

        this.form.productCostPerUnit = Math.round(
          backendProductCpu > 0 ? backendProductCpu : derivedProductCpu
        );

        this.form.totalProductCost = Math.round(
          backendTotalProd > 0 ? backendTotalProd : this.form.productCostPerUnit * qty
        );

        this.form.threadMetersPerPiece = Number(
          d.threadMetersPerPiece ?? d.ThreadMetersPerPiece ?? 0
        );

        this.form.totalThreadMeters = Number(
          d.totalThreadMeters ?? d.TotalThreadMeters ?? 0
        );

        this.form.costPer1000Stitches = Number(
          d.costPer1000Stitches ?? d.CostPer1000Stitches ?? 0
        );

        this.form.productionMinutes = Number(
          d.productionMinutes ?? d.ProductionMinutes ?? 0
        );

        this.form.productionHours = Number(
          d.productionHours ?? d.ProductionHours ?? 0
        );

        this.form.piecesPerHour = Number(
          d.piecesPerHour ?? d.PiecesPerHour ?? 0
        );

        this.form.piecesPerShift = Number(
          d.piecesPerShift ?? d.PiecesPerShift ?? 0
        );

        this.form.dailyCapacity = Number(
          d.dailyCapacity ?? d.DailyCapacity ?? 0
        );

        this.form.productionDays = Number(
          d.productionDays ?? d.ProductionDays ?? 0
        );

        // If backend didn't return a Service / Unit (either MVC isn't rebuilt
        // yet, or the per-product Service Rate isn't on the same branch as the
        // design), look it up via the existing service-rates list endpoint and
        // pull the first matching Charge Factor. Then recompute Product Cost.
        if (!this.form.serviceCostPerUnit && this.selectedProductId) {
          this.lookupServiceRate(Number(this.selectedProductId));
        }
      },
      error: (err) => {
        console.error('Recalculate error:', err);
        this.loading = false;
        const body = err?.error;
        const msg = body?.message || err?.message || 'Unexpected recalculation error';
        const detail = body?.detail && body.detail !== body.message ? `\n\nDetail: ${body.detail}` : '';
        alert(msg + detail);
      }
    });
  }

  /**
   * Resolves Service / Unit by querying the existing /service-rates/list
   * endpoint filtered by product. Doesn't require the MVC backend to be
   * rebuilt — this endpoint already exists. First matching Charge_Fector
   * becomes Service / Unit; downstream totals recompute automatically.
   */
  private lookupServiceRate(productId: number): void {
    if (!productId || productId <= 0) return;

    const url = `/emb-products/service-rates/list?productFilter=${productId}&activeFilter=active&pageSize=10`;

    this.http.get<any>(url).subscribe({
      next: (res: any) => {
        const items: any[] = res?.data?.items ?? res?.Data?.items ?? [];
        if (!items.length) return;

        let total = 0;
        for (const it of items) {
          const raw = it.ChargeFactor ?? it.chargeFactor ?? '0';
          const v = parseFloat(String(raw));
          if (!isNaN(v)) total += v;
        }

        if (total > 0) {
          this.form.serviceCostPerUnit = Math.round(total);
          this.recomputeProductCost();
        }
      },
      error: (e) => {
        console.warn('Service rate lookup failed:', e);
      }
    });
  }

  /**
   * Recomputes Product Cost / Unit and Total Product Cost from the three
   * per-unit components. Called whenever Service / Unit is edited or
   * auto-resolved post-Recalculate.
   */
  recomputeProductCost(): void {
    const qty = Math.max(Number(this.form.batchQty || 0), 1);
    this.form.productCostPerUnit =
      Math.round(
        (Number(this.form.materialCostPerUnit) || 0) +
        (Number(this.form.productionCostPerUnit) || 0) +
        (Number(this.form.serviceCostPerUnit) || 0)
      );
    this.form.totalProductCost = Math.round(this.form.productCostPerUnit * qty);
  }

  // ==========================================================
  // CREATE
  // ==========================================================
  createNew(): void {
    this.isEdit = false;
    this.resetForm();
    this.formModalInstance?.show();
  }

  cancelForm(): void {
    this.closeForm();
  }

  closeForm(): void {
    this.formModalInstance?.hide();
    this.isEdit = false;
    this.resetForm();
  }

  save(): void {
    const finalProductId = Number(this.form.productId ?? this.selectedProductId ?? 0);

    if (!finalProductId || finalProductId <= 0) {
      alert('Product is required. Please select product again.');
      return;
    }

    if (!this.form.designId || Number(this.form.designId) <= 0) {
      alert('Design is required');
      return;
    }

    if (!this.form.batchQty || Number(this.form.batchQty) <= 0) {
      alert('Batch quantity must be greater than zero');
      return;
    }

    this.form.productId = finalProductId;
    this.calculateTotal();

    const payload: any = {
      Id: Number(this.form.id ?? 0),
      DesignId: Number(this.form.designId ?? 0),
      ProductId: finalProductId,

      BatchQty: Number(this.form.batchQty ?? 0),

      ThreadCost: Number(this.form.threadCost ?? 0),
      MachineCost: Number(this.form.machineCost ?? 0),
      LaborCost: Number(this.form.laborCost ?? 0),
      ElectricityCost: Number(this.form.electricityCost ?? 0),
      OtherCost: Number(this.form.otherCost ?? 0),

      TotalCost: Number(this.form.totalCost ?? 0),
      CostPerUnit: Number(this.form.costPerUnit ?? 0),

      MarginPercent: Number(this.form.marginPercent ?? 20),
      SellingPricePerUnit: Number(this.form.sellingPricePerUnit ?? 0),

      MaterialCostPerUnit: Number(this.form.materialCostPerUnit ?? 0),
      ProductionCostPerUnit: Number(this.form.productionCostPerUnit ?? 0),
      ServiceCostPerUnit: Number(this.form.serviceCostPerUnit ?? 0),
      ProductCostPerUnit: Number(this.form.productCostPerUnit ?? 0),
      TotalProductCost: Number(this.form.totalProductCost ?? 0),

      Active: this.form.active !== false,
      Deleted: false
    };

    this.loading = true;

    this.svc.save(payload).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success) {
          const msg = res?.message || 'Save failed';
          const detail = res?.detail && res.detail !== res.message ? `\n\n${res.detail}` : '';
          alert(msg + detail);
          return;
        }

        this.formModalInstance?.hide();
        this.isEdit = false;
        this.resetForm();
        this.load();
      },
      error: (err) => {
        this.loading = false;
        console.error('Save error:', err);

        const body = err?.error;
        const msg = body?.message || err?.message || 'Unexpected save error';
        const detail = body?.detail && body.detail !== body.message ? `\n\n${body.detail}` : '';

        alert(msg + detail);
      }
    });
  }

  // ==========================================================
  // EDIT
  // ==========================================================
  openEdit(id: number): void {
    if (!id || id <= 0) return;

    this.loading = true;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        if (!res?.success || !res?.data) {
          this.loading = false;
          alert(res?.message || 'Record not found');
          return;
        }

        const d: any = res.data;
        const row = this.rows.find(x => x.id === id);

        let pid = Number(d.productId ?? d.ProductId ?? 0);
        if (!pid || pid <= 0) {
          pid = Number(row?.productId ?? 0);
        }

        const designId = Number(d.designId ?? d.DesignId ?? 0);

        this.selectedProductId = pid > 0 ? pid : null;

        this.form = {
          ...this.getEmptyForm(),

          id: Number(d.id ?? d.Id ?? id),

          designId: designId > 0 ? designId : null,
          productId: pid > 0 ? pid : null,

          batchQty: Number(d.batchQty ?? d.BatchQty ?? 0),
          marginPercent: Number(d.marginPercent ?? d.MarginPercent ?? 20),

          threadCost: Number(d.threadCost ?? d.ThreadCost ?? 0),
          machineCost: Number(d.machineCost ?? d.MachineCost ?? 0),
          laborCost: Number(d.laborCost ?? d.LaborCost ?? 0),
          electricityCost: Number(d.electricityCost ?? d.ElectricityCost ?? 0),
          otherCost: Number(d.otherCost ?? d.OtherCost ?? 0),

          totalCost: Number(d.totalCost ?? d.TotalCost ?? 0),
          costPerUnit: Number(d.costPerUnit ?? d.CostPerUnit ?? 0),

          sellingPricePerUnit: Number(d.sellingPricePerUnit ?? d.SellingPricePerUnit ?? 0),
          totalSellingPrice: Number(d.totalSellingPrice ?? d.TotalSellingPrice ?? 0),

          materialCostPerUnit: Number(d.materialCostPerUnit ?? d.MaterialCostPerUnit ?? 0),
          productionCostPerUnit: Number(d.productionCostPerUnit ?? d.ProductionCostPerUnit ?? 0),
          serviceCostPerUnit: Number(d.serviceCostPerUnit ?? d.ServiceCostPerUnit ?? 0),
          productCostPerUnit: Number(d.productCostPerUnit ?? d.ProductCostPerUnit ?? 0),
          totalProductCost: Number(d.totalProductCost ?? d.TotalProductCost ?? 0),

          threadMetersPerPiece: Number(d.threadMetersPerPiece ?? d.ThreadMetersPerPiece ?? 0),
          totalThreadMeters: Number(d.totalThreadMeters ?? d.TotalThreadMeters ?? 0),
          costPer1000Stitches: Number(d.costPer1000Stitches ?? d.CostPer1000Stitches ?? 0),

          productionMinutes: Number(d.productionMinutes ?? d.ProductionMinutes ?? 0),
          productionHours: Number(d.productionHours ?? d.ProductionHours ?? 0),
          piecesPerHour: Number(d.piecesPerHour ?? d.PiecesPerHour ?? 0),

          piecesPerShift: Number(d.piecesPerShift ?? d.PiecesPerShift ?? 0),
          dailyCapacity: Number(d.dailyCapacity ?? d.DailyCapacity ?? 0),
          productionDays: Number(d.productionDays ?? d.ProductionDays ?? 0),

          active: (d.active ?? d.Active ?? true) === true
        };

        const selectedDesignFallback = {
          id: designId,
          display: d.designName ?? d.DesignName ?? row?.designName ?? 'Selected Design',
          productId: pid > 0 ? pid : null
        };

        const showModal = () => {
          this.loading = false;
          this.isEdit = true;
          this.formModalInstance?.show();
        };

        if (pid > 0) {
          this.http.get<{ data: any[] }>(
            `/emb-products/dropdowns/designs-by-product?productId=${pid}`
          ).subscribe({
            next: r => {
              const list = r?.data ?? [];

              this.filteredDesigns = list.map((x: any) => {
                const code = x.code ?? x.Code ?? '';
                const name = x.dNo ?? x.DNo ?? x.name ?? x.Name ?? '';
                const display = (x.display ?? x.Display ?? `${code} - ${name}`).trim();

                return {
                  id: Number(x.id ?? x.Id ?? 0),
                  display: display || 'Unnamed Design',
                  productId: Number(x.productId ?? x.ProductId ?? pid)
                };
              });

              const exists = this.filteredDesigns.some(x => Number(x.id) === designId);

              if (!exists && designId > 0) {
                this.filteredDesigns.unshift(selectedDesignFallback);
              }

              showModal();
            },
            error: err => {
              console.error('Edit design dropdown error:', err);

              this.filteredDesigns = designId > 0
                ? [selectedDesignFallback]
                : [];

              showModal();
            }
          });
        } else {
          this.filteredDesigns = this.designs.slice();

          if (designId > 0 && !this.filteredDesigns.some(x => Number(x.id) === designId)) {
            this.filteredDesigns.unshift(selectedDesignFallback);
          }

          showModal();
        }
      },
      error: (err) => {
        this.loading = false;
        console.error('Edit load error:', err);
        alert('Unexpected edit load error');
      }
    });
  }

  // ==========================================================
  // DETAIL
  // ==========================================================
  openDetail(id: number): void {
    if (!id || id <= 0) return;

    this.loading = true;
    this.detailData = null;

    this.svc.detail(id).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success || !res?.data) {
          alert(res?.message || 'Detail not found');
          return;
        }

        this.detailData = res.data;
        this.detailModalInstance?.show();
      },
      error: (err) => {
        this.loading = false;
        console.error('Detail load error:', err);
        alert('Failed to load detail');
      }
    });
  }

  closeDetail(): void {
    this.detailModalInstance?.hide();
    this.detailData = null;
  }

  // ==========================================================
  // DELETE
  // ==========================================================
  confirmDelete(id: number): void {
    if (!id || id <= 0) return;

    this.deleteId = id;
    this.selectedRow = this.rows.find(x => x.id === id) ?? null;
    this.deleteModalInstance?.show();
  }

  doDelete(): void {
    if (!this.deleteId || this.deleteId <= 0) return;

    this.loading = true;

    this.svc.delete(this.deleteId).subscribe({
      next: (res: any) => {
        this.loading = false;

        if (!res?.success) {
          alert(res?.message || 'Delete failed');
          return;
        }

        this.deleteModalInstance?.hide();
        this.deleteId = 0;
        this.selectedRow = null;

        if (this.rows.length === 1 && this.page > 1) {
          this.page--;
        }

        this.load();
      },
      error: (err) => {
        this.loading = false;
        console.error('Delete error:', err);
        alert('Unexpected delete error');
      }
    });
  }

  closeDelete(): void {
    this.deleteModalInstance?.hide();
    this.deleteId = 0;
    this.selectedRow = null;
  }

  // ==========================================================
  // EXPORT
  // ==========================================================
  exportExcel(): void {
    this.svc.exportExcel({
      activeFilter: this.activeFilter,
      search: this.searchText
    });
  }

  exportPdfList(): void {
    this.svc.exportPdfList({
      activeFilter: this.activeFilter,
      search: this.searchText
    });
  }

  exportPdf(id: number): void {
    if (!id || id <= 0) return;
    this.svc.exportPdf(id);
  }

  printList(): void {
    // Real browser print dialog. The backend's /print-list endpoint doesn't
    // exist, and even if it did, "Print" should open the system print dialog
    // — not download a PDF. Build a self-contained HTML page from this.rows
    // and trigger window.print() on load.
    if (!this.rows.length) return alert('No data');

    const fmt = (n: number) => (Number(n) || 0).toLocaleString();

    const trs = this.rows.map(r => `
      <tr>
        <td>${this.escapeHtml(r.designName ?? '-')}</td>
        <td>${this.escapeHtml((r.productCode ? r.productCode + ' - ' : '') + (r.productName ?? '-'))}</td>
        <td class="num">${fmt(r.batchQty)}</td>
        <td class="num">${fmt(r.materialCostPerUnit)}</td>
        <td class="num">${fmt(r.productionCostPerUnit)}</td>
        <td class="num">${fmt(r.serviceCostPerUnit)}</td>
        <td class="num"><strong>${fmt(r.productCostPerUnit)}</strong></td>
        <td class="num">${fmt(r.sellingPricePerUnit)}</td>
        <td class="num">${fmt(r.totalSellingPrice)}</td>
        <td>${r.active ? 'Active' : 'Inactive'}</td>
      </tr>`).join('');

    const html = `<!doctype html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>Design Costing — Print</title>
        <style>
          @page { size: A4 landscape; margin: 12mm; }
          body { font-family: "Segoe UI", Arial, sans-serif; color: #212529; font-size: 10pt; }
          h2 { text-align: center; margin: 0 0 12px; }
          table { width: 100%; border-collapse: collapse; }
          thead { background: #0d6efd; color: #fff; }
          th, td { border: 1px solid #ccc; padding: 5px 7px; text-align: left; vertical-align: top; font-size: 9.5pt; }
          td.num, th.num { text-align: right; }
          tbody tr:nth-child(even) { background: #f6f8fb; }
          .footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
        </style>
      </head>
      <body>
        <h2>Design Costing &mdash; List</h2>
        <table>
          <thead>
            <tr>
              <th>Design</th><th>Product</th>
              <th class="num">Batch</th>
              <th class="num">Material/Unit</th>
              <th class="num">Production/Unit</th>
              <th class="num">Service/Unit</th>
              <th class="num">Product Cost/Unit</th>
              <th class="num">Selling/Unit</th>
              <th class="num">Total Selling</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>${trs}</tbody>
        </table>
        <div class="footer">Generated: ${new Date().toLocaleString()} | Rows: ${this.rows.length}</div>
        <script>
          window.addEventListener('load', function () {
            setTimeout(function () { window.print(); }, 100);
          });
        </script>
      </body>
      </html>`;

    const win = window.open('', '_blank');
    if (!win) return alert('Pop-up blocked. Please allow pop-ups for this site.');
    win.document.open();
    win.document.write(html);
    win.document.close();
  }

  private escapeHtml(value: any): string {
    return String(value ?? '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  print(id: number): void {
    if (!id || id <= 0) return;
    this.svc.print(id);
  }

  // ==========================================================
  // RESET
  // ==========================================================
  resetForm(): void {
    this.form = this.getEmptyForm();
    this.selectedProductId = null;
    this.filteredDesigns = this.designs.slice();
  }

  getEmptyForm(): DesignCostingDto {
    return {
      id: 0,
      designId: null,
      productId: null,

      batchQty: 0,
      marginPercent: 20,

      threadCost: 0,
      machineCost: 0,
      laborCost: 0,
      electricityCost: 0,
      otherCost: 0,

      totalCost: 0,
      costPerUnit: 0,

      sellingPricePerUnit: 0,
      totalSellingPrice: 0,

      materialCostPerUnit: 0,
      productionCostPerUnit: 0,
      serviceCostPerUnit: 0,
      productCostPerUnit: 0,
      totalProductCost: 0,

      threadMetersPerPiece: 0,
      totalThreadMeters: 0,
      costPer1000Stitches: 0,

      productionMinutes: 0,
      productionHours: 0,
      piecesPerHour: 0,

      piecesPerShift: 0,
      dailyCapacity: 0,
      productionDays: 0,

      active: true
    };
  }
}
