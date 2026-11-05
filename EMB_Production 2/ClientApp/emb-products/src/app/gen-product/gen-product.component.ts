import { Component, OnInit, AfterViewInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule, NgIf } from '@angular/common';
import {
  FormsModule,
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators
} from '@angular/forms';
import { Modal } from 'bootstrap';
import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

import { GenProductService } from '../services/gen-products.service';
import { ProductForm, ProductListViewModel, ProductDto } from '../models/product.dto';

type UnitMap = Record<
  number,
  { qty?: string; length?: string; weight?: string; price?: string }
>;

@Component({
  selector: 'app-gen-product',
  standalone: true,
  templateUrl: './gen-product.component.html',
  styleUrls: ['./gen-product.component.css'],
  imports: [CommonModule, FormsModule, ReactiveFormsModule, NgIf]
})
export class GenProductComponent implements OnInit, AfterViewInit {

  // ===============================
  // DATA
  // ===============================
  products: ProductListViewModel[] = [];
  selectedProduct: ProductDto | null = null;

  categories: { id: number; name: string }[] = [];
  branches: { id: number; name: string }[] = [];

  // ===============================
  // FORM
  // ===============================
  form: FormGroup;
  editingProduct = false;
  loading = false;

  // ===============================
  // FILTERS / PAGINATION
  // ===============================
  search = '';
  page = 1;
  pageSize = 10;
  totalPages = 1;
  paginationArray: number[] = [];

  // ===============================
  // DELETE TARGET
  // ===============================
  deleteTarget: ProductListViewModel | null = null;

  // ===============================
  // UNIT LABELS (UI ONLY)
  // ===============================
  qtyUnitLabel = 'Qty';
  lengthUnitLabel = 'Length';
  weightUnitLabel = 'Weight';
  priceUnitLabel = 'Price';

  // ===============================
  // UNIT MAP (CATEGORY -> UNITS)
  // ===============================
  categoryUnitMap: UnitMap = {};

  selectedCategoryUnits: {
    qty?: string;
    length?: string;
    weight?: string;
    price?: string;
  } | null = null;

  // ===============================
  // MODAL REFERENCES
  // ===============================
  @ViewChild('productFormModal') productFormModal!: ElementRef;
  @ViewChild('productDetailModal') productDetailModal!: ElementRef;
  @ViewChild('productDeleteModal') productDeleteModal!: ElementRef;

  private formModal!: Modal;
  private detailModal!: Modal;
  private deleteModal!: Modal;

  // ===============================
  // CONSTRUCTOR
  // ===============================
  constructor(
    private fb: FormBuilder,
    private genProductService: GenProductService
  ) {
    this.form = this.fb.group({
      id: [null],
      prodName: ['', Validators.required],
      prodCode: [{ value: '', disabled: true }], // auto-generated
      prodDescription: [''],
      catId: [null, Validators.required],
      branchId: [null, Validators.required],
      active: [true]
    });
  }

  // ===============================
  // INIT
  // ===============================
  ngOnInit(): void {
    this.loadProducts();
    this.loadCategories();
    this.loadBranches();

    this.form.get('catId')?.valueChanges.subscribe(catId => {
      this.applyCategoryUnits(catId);
    });
  }

  // ===============================
  // AFTER VIEW INIT
  // ===============================
  ngAfterViewInit(): void {
    this.formModal = new Modal(this.productFormModal.nativeElement, {
      backdrop: 'static',
      keyboard: false
    });

    this.detailModal = new Modal(this.productDetailModal.nativeElement, {
      backdrop: true,
      keyboard: true
    });

    this.deleteModal = new Modal(this.productDeleteModal.nativeElement, {
      backdrop: 'static',
      keyboard: false
    });
  }

  // ===============================
  // LOOKUPS
  // ===============================
  loadBranches(): void {
    this.genProductService.getBranches().subscribe({
      next: res => {
        this.branches = res || [];
      },
      error: err => {
        console.error('Failed to load branches', err);
        this.branches = [];
      }
    });
  }

  loadCategories(): void {
    this.genProductService.getCategories().subscribe({
      next: res => {
        this.categories = res || [];
        this.populateCategoryUnitMap();

        const currentCatId = this.form.get('catId')?.value;
        if (currentCatId) {
          this.applyCategoryUnits(currentCatId);
        }
      },
      error: err => {
        console.error('Failed to load categories', err);
        this.categories = [];
      }
    });
  }

  populateCategoryUnitMap(): void {
    this.categoryUnitMap = {};

    this.categories.forEach(c => {
      switch (c.name) {
        // ================= DORI / CORD =================
        case 'Dori':
        case 'Cotton Cord':
        case 'Polyester Cord':
        case 'Elastic Dori':
        case 'Twisted Rope':
          this.categoryUnitMap[c.id] = {
            qty: 'Rolls',
            length: 'Meters',
            weight: 'Kg',
            price: 'Per Roll / Meter'
          };
          break;

        // ================= FABRICS =================
        case 'Cotton Fabric':
        case 'Lawn':
        case 'Net':
        case 'Organza':
        case 'Velvet':
        case 'Silk':
          this.categoryUnitMap[c.id] = {
            qty: 'Rolls / Pieces',
            length: 'Meters',
            weight: 'GSM (optional)',
            price: 'Per Meter / Roll'
          };
          break;

        // ================= DECORATIVE =================
        case 'Sequins':
        case 'Stones':
        case 'Beads':
        case 'Pearls':
        case 'Crystals':
          this.categoryUnitMap[c.id] = {
            qty: 'Packets',
            weight: 'Grams',
            price: 'Per Packet / Gram'
          };
          break;

        // ================= ACCESSORIES =================
        case 'Needles':
        case 'Bobbins':
        case 'Backing Paper':
        case 'Stabilizer Sheets':
          this.categoryUnitMap[c.id] = {
            qty: 'Pieces / Packets',
            length: 'Sheets / Meters (optional)',
            weight: 'Optional',
            price: 'Per Unit'
          };
          break;

        default:
          break;
      }
    });
  }

  applyCategoryUnits(catId: number | null): void {
    this.qtyUnitLabel = 'Qty';
    this.lengthUnitLabel = 'Length';
    this.weightUnitLabel = 'Weight';
    this.priceUnitLabel = 'Price';
    this.selectedCategoryUnits = null;

    if (!catId || !this.categoryUnitMap[catId]) {
      return;
    }

    const u = this.categoryUnitMap[catId];
    this.selectedCategoryUnits = u;

    if (u.qty) this.qtyUnitLabel = `Qty (${u.qty})`;
    if (u.length) this.lengthUnitLabel = `Length (${u.length})`;
    if (u.weight) this.weightUnitLabel = `Weight (${u.weight})`;
    if (u.price) this.priceUnitLabel = `Price (${u.price})`;
  }

  // =====================================================
  // LIST
  // =====================================================
  loadProducts(): void {
    this.loading = true;

    this.genProductService.listPaged(this.search, this.page, this.pageSize).subscribe({
      next: res => {
        const items = (res?.items || []) as ProductListViewModel[];
        const total = Number(res?.total ?? 0);

        this.products = items;
        this.totalPages = Math.max(1, Math.ceil(total / this.pageSize));
        this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
        this.loading = false;
      },
      error: err => {
        console.error('Failed to load products', err);
        this.products = [];
        this.totalPages = 1;
        this.paginationArray = [1];
        this.loading = false;
      }
    });
  }

  changePage(pg: number): void {
    if (pg < 1 || pg > this.totalPages || pg === this.page) {
      return;
    }

    this.page = pg;
    this.loadProducts();
  }

  onSearch(): void {
    this.page = 1;
    this.loadProducts();
  }

  clearSearch(): void {
    this.search = '';
    this.page = 1;
    this.loadProducts();
  }

  // =====================================================
  // CREATE / EDIT
  // =====================================================
  openCreate(): void {
    this.editingProduct = false;

    this.form.reset({
      id: null,
      prodName: '',
      prodCode: 'Auto-generated',
      prodDescription: '',
      catId: null,
      branchId: null,
      active: true
    });

    this.applyCategoryUnits(null);
    this.formModal.show();
  }

  openEdit(row: ProductListViewModel): void {
    this.editingProduct = true;

    this.genProductService.getDetail(row.id).subscribe({
      next: res => {
        const data = (res as any)?.data;
        if (!data) {
          alert('Invalid product data received.');
          return;
        }

        this.form.patchValue({
          id: data.id,
          prodName: data.prodName,
          prodCode: data.prodCode || '',
          prodDescription: data.prodDescription,
          catId: data.catId,
          branchId: data.branchId,
          active: data.active ?? true
        });

        this.applyCategoryUnits(data.catId ?? null);
        this.formModal.show();
      },
      error: err => {
        console.error('Failed to load product for edit', err);
        alert('Failed to load product for edit.');
      }
    });
  }

  saveProduct(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      alert('Please fill required fields.');
      return;
    }

    this.loading = true;

    const raw = this.form.getRawValue();

    const payload: ProductForm = {
      id: this.editingProduct ? raw.id : null,
      prodName: raw.prodName?.trim() || '',
      // create = backend auto-generates; update = keep current value if service/controller expects it
      prodCode: this.editingProduct ? (raw.prodCode || '') : '',
      prodDescription: raw.prodDescription?.trim() || '',
      catId: raw.catId,
      branchId: raw.branchId,
      active: raw.active === true
    };

    const action$ = this.editingProduct
      ? this.genProductService.update(payload)
      : this.genProductService.create(payload);

    action$.subscribe({
      next: res => {
        this.loading = false;

        if (res?.success) {
          alert(res.message || 'Saved successfully.');
          this.formModal.hide();
          this.form.reset({
            id: null,
            prodName: '',
            prodCode: '',
            prodDescription: '',
            catId: null,
            branchId: null,
            active: true
          });
          this.editingProduct = false;
          this.applyCategoryUnits(null);
          this.loadProducts();
        } else {
          alert(res?.message || 'Save failed.');
        }
      },
      error: err => {
        this.loading = false;
        console.error('Save failed', err);
        alert(err?.error?.message || 'Save failed. Check console.');
      }
    });
  }

  // =====================================================
  // DETAIL
  // =====================================================
  openDetails(product: ProductListViewModel): void {
    this.genProductService.getDetail(product.id).subscribe({
      next: res => {
        this.selectedProduct = (res as any)?.data ?? null;

        if (!this.selectedProduct) {
          alert('No details returned.');
          return;
        }

        this.detailModal.show();
      },
      error: err => {
        console.error('Failed to load product details', err);
        alert('Failed to load product details.');
      }
    });
  }

  // =====================================================
  // DELETE
  // =====================================================
  openDelete(row: ProductListViewModel): void {
    this.deleteTarget = row;
    this.deleteModal.show();
  }

  deleteProduct(): void {
    if (!this.deleteTarget) {
      return;
    }

    this.loading = true;

    this.genProductService.delete(this.deleteTarget.id).subscribe({
      next: res => {
        this.loading = false;

        if (res?.success) {
          alert(res.message || 'Deleted.');
          this.deleteModal.hide();
          this.deleteTarget = null;
          this.loadProducts();
        } else {
          alert(res?.message || 'Delete failed.');
        }
      },
      error: err => {
        console.error('Delete failed', err);
        this.loading = false;
        alert(err?.error?.message || 'Delete failed.');
      }
    });
  }

  // =====================================================
  // COMMON
  // =====================================================
  closeModal(type: 'form' | 'detail' | 'delete'): void {
    if (type === 'form') this.formModal?.hide();
    if (type === 'detail') this.detailModal?.hide();
    if (type === 'delete') this.deleteModal?.hide();
  }

  trackById(_: number, item: ProductListViewModel): number {
    return item.id;
  }

  getUnit(catId: number, type: 'qty' | 'length' | 'weight' | 'price'): string {
    return this.categoryUnitMap[catId]?.[type] ?? '';
  }

  // =====================================================
  // EXPORT
  // =====================================================
  exportExcel(): void {
    const exportRows = this.products.map(p => ({
      Name: p.prodName ?? '',
      Code: p.prodCode ?? '',
      Category: p.categoryName ?? '',
      Branch: p.branchName ?? '',
      Active: p.active ? 'Yes' : 'No'
    }));

    const ws = XLSX.utils.json_to_sheet(exportRows);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Products');
    XLSX.writeFile(wb, 'Products.xlsx');
  }

  exportPdf(): void {
    const doc = new jsPDF();

    autoTable(doc, {
      head: [['Name', 'Code', 'Category', 'Branch', 'Active']],
      body: this.products.map(p => [
        p.prodName ?? '',
        p.prodCode ?? '',
        p.categoryName ?? '',
        p.branchName ?? '',
        p.active ? 'Yes' : 'No'
      ])
    });

    doc.save('Products.pdf');
  }

  printTable(): void {
    // Real browser print dialog. Builds a self-contained HTML page from
    // this.products in memory. The previous implementation looked for an
    // element with id "productTable" which does not exist in the template,
    // so it silently returned and did nothing.
    if (!this.products.length) return alert('No data');

    const rows = this.products.map(p => `
      <tr>
        <td>${this.escapeHtml(p.prodName ?? '')}</td>
        <td>${this.escapeHtml(p.prodCode ?? '')}</td>
        <td>${this.escapeHtml(p.categoryName ?? '')}</td>
        <td>${this.escapeHtml(p.branchName ?? '')}</td>
        <td>${p.active ? 'Active' : 'Inactive'}</td>
      </tr>`).join('');

    const html = `<!doctype html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>Products — Print</title>
        <style>
          body { font-family: "Segoe UI", Arial, sans-serif; font-size: 11pt; color: #212529; margin: 14mm; }
          h2 { text-align: center; margin: 0 0 12px; }
          table { width: 100%; border-collapse: collapse; }
          thead { background: #0d6efd; color: #fff; }
          th, td { border: 1px solid #ccc; padding: 5px 7px; text-align: left; vertical-align: top; font-size: 10pt; }
          tbody tr:nth-child(even) { background: #f6f8fb; }
          .footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
        </style>
      </head>
      <body>
        <h2>Products &mdash; List</h2>
        <table>
          <thead>
            <tr><th>Name</th><th>Code</th><th>Category</th><th>Branch</th><th>Status</th></tr>
          </thead>
          <tbody>${rows}</tbody>
        </table>
        <div class="footer">Generated: ${new Date().toLocaleString()} | Rows: ${this.products.length}</div>
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
}
