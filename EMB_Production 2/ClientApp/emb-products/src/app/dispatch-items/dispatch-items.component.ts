import { Component, ElementRef, ViewChild, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { DispatchItemsService, SaleType } from '../services/dispatch-items.service';
import {DispatchItemRowViewModel,DispatchItemDetailViewModel,DispatchStockLookupViewModel
} from '../shared/models/dispatch-item.dto';
import { DispatchItemDto } from '../shared/models/dispatch.dto';

declare const bootstrap: any;

@Component({
  selector: 'app-dispatch-items',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dispatch-items.component.html',
  styleUrls: ['./dispatch-items.component.css']
})
export class DispatchItemsComponent implements OnInit {
  dispatchId = 0;
  dispatchIsPosted = false;
  dispatchIsReversed = false;
  saleType: SaleType = 'CUSTOMER_DEMAND';

  items: DispatchItemRowViewModel[] = [];
  pagedItems: DispatchItemRowViewModel[] = [];
  availableProducts: DispatchStockLookupViewModel[] = [];

  loading = false;
  editingQtyId: number | null = null;

  searchText = '';
  page = 1;
  pageSize = 10;
  totalCount = 0;

  form: DispatchItemDto = this.createEmptyForm();
  detailData: DispatchItemDetailViewModel | null = null;
  deleteId: number | null = null;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(
    private route: ActivatedRoute,
    private svc: DispatchItemsService
  ) { }

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('dispatchId');
    this.dispatchId = idParam ? Number(idParam) : 0;

    if (!this.dispatchId || isNaN(this.dispatchId)) {
      console.error('Invalid dispatchId');
      return;
    }

    this.form = this.createEmptyForm();
    this.loadItems();
  }

  createEmptyForm(): DispatchItemDto {
    return {
      id: 0,
      dispatchId: this.dispatchId,
      productId: 0,
      dispatchQty: null,

      costPrice: 0,
      baseSalePrice: 0,
      discountPercent: 0,
      discountAmount: 0,
      salePrice: 0,
      amount: 0,

      unit: 'PCS',
      remarks: '',
      active: true
    };
  }

  loadItems(): void {
    this.loading = true;

    this.svc.list(this.dispatchId).subscribe({
      next: res => {
        if (!res?.success || !res.data) {
          this.resetGrid();
          return;
        }

        const payload = res.data;

        this.dispatchIsPosted = payload.dispatchIsPosted === true;
        this.dispatchIsReversed = payload.dispatchIsReversed === true;
        this.saleType = payload.saleType ?? 'CUSTOMER_DEMAND';

        this.items = (payload.items || []).map((x: any) => ({
          id: Number(x.id ?? x.Id ?? 0),
          dispatchId: Number(x.dispatchId ?? x.DispatchId ?? this.dispatchId),

          productId: x.productId ?? x.ProductId ?? null,
          productName: x.productName ?? x.ProductName ?? '-',

          branchId: x.branchId ?? x.BranchId ?? null,
          branchName: x.branchName ?? x.BranchName ?? null,

          customerReservedStock: Number(x.customerReservedStock ?? x.CustomerReservedStock ?? 0),
          reservedStock: Number(x.reservedStock ?? x.ReservedStock ?? 0),
          freeCompanyStock: Number(x.freeCompanyStock ?? x.FreeCompanyStock ?? 0),
          totalFinishedStock: Number(x.totalFinishedStock ?? x.TotalFinishedStock ?? 0),

          availableQty: Number(x.availableQty ?? x.AvailableQty ?? 0),
          availableQtySnapshot: Number(x.availableQtySnapshot ?? x.AvailableQtySnapshot ?? 0),

          dispatchQty: Number(x.dispatchQty ?? x.DispatchedQty ?? x.DispatchQty ?? 0),

          costPrice: Number(x.costPrice ?? x.CostPrice ?? 0),
          baseSalePrice: Number(x.baseSalePrice ?? x.BaseSalePrice ?? 0),
          discountPercent: Number(x.discountPercent ?? x.DiscountPercent ?? 0),
          discountAmount: Number(x.discountAmount ?? x.DiscountAmount ?? 0),
          salePrice: Number(x.salePrice ?? x.SalePrice ?? 0),
          amount: Number(x.amount ?? x.Amount ?? 0),

          unit: x.unit ?? x.Unit ?? 'PCS',
          remarks: x.remarks ?? x.Remarks ?? '',

          active: x.active ?? x.Active ?? true,
          deleted: x.deleted ?? x.Deleted ?? false,
          isPosted: x.isPosted ?? x.IsPosted ?? false
        }));

        this.applyFiltersAndPaging();
        this.loading = false;
      },
      error: err => {
        console.error(err);
        this.resetGrid();
      }
    });
  }

  private resetGrid(): void {
    this.items = [];
    this.pagedItems = [];
    this.availableProducts = [];
    this.dispatchIsPosted = false;
    this.dispatchIsReversed = false;
    this.totalCount = 0;
    this.loading = false;
  }

  loadAvailableProducts(): void {
    this.svc.getAvailableProducts(this.saleType).subscribe({
      next: res => {
        const raw = res?.data ?? [];

        this.availableProducts = raw.map((x: any) => ({
          productId: Number(x.productId ?? x.ProductId ?? 0),
          productName: x.productName ?? x.ProductName ?? '-',

          branchId: x.branchId ?? x.BranchId ?? null,
          branchName: x.branchName ?? x.BranchName ?? null,

          customerReservedStock: Number(x.customerReservedStock ?? x.CustomerReservedStock ?? 0),
          reservedStock: Number(x.reservedStock ?? x.ReservedStock ?? 0),
          freeCompanyStock: Number(x.freeCompanyStock ?? x.FreeCompanyStock ?? 0),
          totalFinishedStock: Number(x.totalFinishedStock ?? x.TotalFinishedStock ?? 0),

          availableQty: Number(x.availableQty ?? x.AvailableQty ?? 0),
          dispatchQty: 0,

          costPrice: Number(x.costPrice ?? x.CostPrice ?? 0),
          baseSalePrice: Number(x.baseSalePrice ?? x.BaseSalePrice ?? 0),
          discountPercent: Number(x.discountPercent ?? x.DiscountPercent ?? 0),
          discountAmount: Number(x.discountAmount ?? x.DiscountAmount ?? 0),
          salePrice: Number(x.salePrice ?? x.SalePrice ?? 0),
          amount: Number(x.amount ?? x.Amount ?? 0),

          unit: x.unit ?? x.Unit ?? 'PCS'
        }));
      },
      error: () => {
        this.availableProducts = [];
      }
    });
  }

  onProductChange(): void {
    const selected = this.availableProducts.find(x =>
      Number(x.productId) === Number(this.form.productId)
    );

    if (!selected) return;

    this.form.unit = selected.unit || 'PCS';

    this.form.costPrice = Number(selected.costPrice ?? 0);
    this.form.baseSalePrice = Number(selected.baseSalePrice ?? 0);
    this.form.discountPercent = Number(selected.discountPercent ?? 0);
    this.form.discountAmount = Number(selected.discountAmount ?? 0);
    this.form.salePrice = Number(selected.salePrice ?? selected.baseSalePrice ?? 0);

    this.recalculateAmount();
  }

  recalculateAmount(): void {
    const qty = Number(this.form.dispatchQty ?? 0);
    const baseSalePrice = Number(this.form.baseSalePrice ?? 0);
    const discountPercent = Number(this.form.discountPercent ?? 0);
    const discountAmount = Number(this.form.discountAmount ?? 0);

    let salePrice = baseSalePrice;

    if (discountPercent > 0) {
      salePrice -= baseSalePrice * discountPercent / 100;
    }

    if (discountAmount > 0) {
      salePrice -= discountAmount;
    }

    if (salePrice < 0) salePrice = 0;

    this.form.salePrice = Number(salePrice.toFixed(2));
    this.form.amount = Number((qty * salePrice).toFixed(2));
  }

  onSearch(): void {
    this.page = 1;
    this.applyFiltersAndPaging();
  }

  clearFilters(): void {
    this.searchText = '';
    this.page = 1;
    this.applyFiltersAndPaging();
  }

  private applyFiltersAndPaging(): void {
    const s = this.searchText.toLowerCase().trim();

    const filtered = !s
      ? this.items
      : this.items.filter(x =>
        (x.productName ?? '').toLowerCase().includes(s) ||
        (x.remarks ?? '').toLowerCase().includes(s)
      );

    this.totalCount = filtered.length;

    const start = (this.page - 1) * this.pageSize;
    this.pagedItems = filtered.slice(start, start + this.pageSize);
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages()) return;
    this.page = p;
    this.applyFiltersAndPaging();
  }

  openCreate(): void {
    if (this.dispatchIsPosted || this.dispatchIsReversed) {
      alert('Posted/reversed dispatch cannot be modified.');
      return;
    }

    this.form = this.createEmptyForm();
    this.loadAvailableProducts();
    this.openModal(this.formModal);
  }

  openEdit(row: DispatchItemRowViewModel): void {
    if (this.dispatchIsPosted || this.dispatchIsReversed || row.isPosted) {
      alert('Posted item cannot be edited.');
      return;
    }

    this.form = {
      id: row.id,
      dispatchId: this.dispatchId,
      productId: Number(row.productId || 0),
      dispatchQty: row.dispatchQty ?? null,

      costPrice: Number(row.costPrice ?? 0),
      baseSalePrice: Number(row.baseSalePrice ?? 0),
      discountPercent: Number(row.discountPercent ?? 0),
      discountAmount: Number(row.discountAmount ?? 0),
      salePrice: Number(row.salePrice ?? 0),
      amount: Number(row.amount ?? 0),

      unit: row.unit ?? 'PCS',
      remarks: row.remarks ?? '',
      active: row.active ?? true
    };

    this.loadAvailableProducts();
    this.openModal(this.formModal);
  }

  save(): void {
    if (this.dispatchIsPosted || this.dispatchIsReversed) return;

    const qty = Number(this.form.dispatchQty ?? 0);

    if (!this.form.productId || this.form.productId <= 0 || qty <= 0) {
      alert('Invalid product or dispatch quantity.');
      return;
    }

    this.recalculateAmount();

    const req = this.form.id === 0
      ? this.svc.create(this.form)
      : this.svc.update(this.form);

    req.subscribe({
      next: res => {
        if (!res.success) {
          alert(res.message);
          return;
        }

        this.closeForm();
        this.loadItems();
      },
      error: err => {
        alert(err?.error?.message || 'Save failed.');
      }
    });
  }

  closeForm(): void {
    this.closeModal(this.formModal);
  }

  startQtyEdit(id: number): void {
    this.editingQtyId = id;
  }

  saveQty(row: DispatchItemRowViewModel): void {
    if (this.dispatchIsPosted || this.dispatchIsReversed || row.isPosted) {
      this.editingQtyId = null;
      return;
    }

    const qty = Number(row.dispatchQty ?? 0);

    if (qty <= 0) {
      alert('Dispatch quantity must be greater than zero.');
      this.editingQtyId = null;
      return;
    }

    const amount = qty * Number(row.salePrice ?? 0);

    this.svc.update({
      id: row.id,
      dispatchId: this.dispatchId,
      productId: Number(row.productId || 0),
      dispatchQty: qty,

      costPrice: Number(row.costPrice ?? 0),
      baseSalePrice: Number(row.baseSalePrice ?? 0),
      discountPercent: Number(row.discountPercent ?? 0),
      discountAmount: Number(row.discountAmount ?? 0),
      salePrice: Number(row.salePrice ?? 0),
      amount: amount,

      unit: row.unit || 'PCS',
      remarks: row.remarks ?? '',
      active: row.active ?? true
    }).subscribe(() => {
      this.editingQtyId = null;
      this.loadItems();
    });
  }

  openDetail(id: number): void {
    this.svc.detail(id).subscribe(res => {
      if (!res.success || !res.data) return;
      this.detailData = res.data;
      this.openModal(this.detailModal);
    });
  }

  closeDetail(): void {
    this.detailData = null;
    this.closeModal(this.detailModal);
  }

  openDelete(id: number): void {
    if (this.dispatchIsPosted || this.dispatchIsReversed) return;
    this.deleteId = id;
    this.openModal(this.deleteModal);
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe(res => {
      if (res.success) {
        this.items = this.items.filter(x => x.id !== this.deleteId);
        this.applyFiltersAndPaging();
        this.closeDelete();
      }
    });
  }

  closeDelete(): void {
    this.deleteId = null;
    this.closeModal(this.deleteModal);
  }

  openModal(ref: ElementRef): void {
    new bootstrap.Modal(ref.nativeElement, {
      backdrop: 'static',
      keyboard: false
    }).show();
  }

  closeModal(ref: ElementRef): void {
    bootstrap.Modal.getInstance(ref.nativeElement)?.hide();
  }

  get canPost(): boolean {
    if (this.dispatchIsPosted || this.dispatchIsReversed) return false;
    if (!this.items.length) return false;

    return this.items.every(x =>
      !x.deleted &&
      !x.isPosted &&
      Number(x.dispatchQty || 0) > 0
    );
  }

  getPageEnd(): number {
    return Math.min(this.page * this.pageSize, this.totalCount);
  }

  parseDotNetDate(value: any): Date | null {
    if (!value) return null;

    const match = /\/Date\((\d+)\)\//.exec(value);
    if (match) return new Date(+match[1]);

    const parsed = new Date(value);
    return isNaN(parsed.getTime()) ? null : parsed;
  }

  exportExcel(): void {
    const rows = this.items.map(x => ({
      Id: x.id,
      Product: x.productName ?? '-',
      Reserved: x.customerReservedStock ?? 0,
      FreeStock: x.freeCompanyStock ?? 0,
      Available: x.availableQtySnapshot ?? x.availableQty ?? 0,
      DispatchQty: x.dispatchQty ?? 0,
      CostPrice: x.costPrice ?? 0,
      BaseSalePrice: x.baseSalePrice ?? 0,
      DiscountPercent: x.discountPercent ?? 0,
      DiscountAmount: x.discountAmount ?? 0,
      SalePrice: x.salePrice ?? 0,
      Amount: x.amount ?? 0,
      Unit: x.unit ?? 'PCS',
      Remarks: x.remarks ?? '',
      Posted: x.isPosted ? 'Yes' : 'No'
    }));

    const ws = XLSX.utils.json_to_sheet(rows);
    const wb = XLSX.utils.book_new();

    XLSX.utils.book_append_sheet(wb, ws, 'DispatchItems');
    XLSX.writeFile(wb, `DispatchItems_${this.dispatchId}.xlsx`);
  }

  exportPdf(): void {
    const doc = new jsPDF();

    autoTable(doc, {
      head: [[
        'Id',
        'Product',
        'Reserved',
        'Free',
        'Available',
        'Dispatch Qty',
        'Sale Price',
        'Amount',
        'Unit',
        'Posted'
      ]],
      body: this.items.map(x => [
        x.id,
        x.productName ?? '-',
        x.customerReservedStock ?? 0,
        x.freeCompanyStock ?? 0,
        x.availableQtySnapshot ?? x.availableQty ?? 0,
        x.dispatchQty ?? 0,
        x.salePrice ?? 0,
        x.amount ?? 0,
        x.unit ?? 'PCS',
        x.isPosted ? 'Yes' : 'No'
      ]),
      styles: { fontSize: 8 }
    });

    doc.save(`DispatchItems_${this.dispatchId}.pdf`);
  }

  print(): void {
    window.print();
  }

  goBackToDispatch(): void {
    window.history.back();
  }
}
