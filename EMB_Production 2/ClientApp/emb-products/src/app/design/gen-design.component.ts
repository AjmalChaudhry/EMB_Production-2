import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { DropdownDataService } from '../shared/services/dropdown-data.service';
import { DesignService } from '../services/design.service';

import {
  DesignListViewModel,
  DesignDetailViewModel
} from '../shared/models/design.dto';

import {
  ApiPagedResponse,
  DropdownItem
} from '../shared/models/api-response';

import { finalize } from 'rxjs/operators';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-design',
  standalone: true,
  templateUrl: './gen-design.component.html',
  styleUrls: ['./gen-design.component.css'],
  imports: [CommonModule, FormsModule, RouterLink]
})
export class GenDesignComponent implements OnInit {

  designs: DesignListViewModel[] = [];

  customers: DropdownItem[] = [];
  branches: DropdownItem[] = [];
  products: DropdownItem[] = [];
  machines: DropdownItem[] = [];

  searchText = '';
  activeFilter = '';
  productFilter: number | null = null;
  machineFilter: number | null = null;
  customerFilter: number | null = null;
  sortBy = 'id_desc';

  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  modalMode: 'create' | 'edit' | 'detail' = 'create';

  formDesign: any = this.buildForm();
  selectedDesign: DesignDetailViewModel | null = null;

  deleteId: number | null = null;

  saving = false;

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(
    private designSvc: DesignService,
    private dropdownSvc: DropdownDataService
  ) { }

  ngOnInit(): void {
    this.loadDesigns();
    this.loadCustomers();
    this.loadBranches();
    this.loadProducts();
    this.loadMachines();
  }

  // ================= DROPDOWNS =================

  loadCustomers(): void {

    this.dropdownSvc.getCustomers().subscribe(list => {
      this.customers = list ?? [];
    });

  }

  loadBranches(): void {

    this.dropdownSvc.getBranches().subscribe({
      next: list => {
        this.branches = list ?? [];
      },
      error: err => {
        console.error('Failed to load branches', err);
        this.branches = [];
      }
    });

  }

  loadMachines(): void {

    this.dropdownSvc.getMachines().subscribe({
      next: list => {
        this.machines = list ?? [];
      },
      error: err => {
        console.error('Failed to load machines', err);
        this.machines = [];
      }
    });

  }

  loadProducts(): void {

    this.dropdownSvc.getProducts().subscribe({
      next: list => {
        this.products = list ?? [];
      },
      error: err => {
        console.error('Failed to load products', err);
        this.products = [];
      }
    });

  }

  // ================= LIST =================

  loadDesigns(): void {

    this.designSvc
      .getAll(this.searchText, this.page, this.pageSize, this.activeFilter, {
        productFilter: this.productFilter,
        machineFilter: this.machineFilter,
        customerFilter: this.customerFilter,
        sortBy: this.sortBy
      })
      .subscribe({

        next: res => {

          if (!res?.success) {
            this.resetDesigns();
            return;
          }

          this.designs = res.data?.items ?? [];
          this.totalItems = res.data?.totalItems ?? 0;

          this.setupPagination();
        },

        error: err => {
          console.error('Failed to load designs', err);
          this.resetDesigns();
        }

      });
  }

  private resetDesigns(): void {
    this.designs = [];
    this.totalItems = 0;
    this.setupPagination();
  }

  setupPagination(): void {

    const count = Math.ceil(this.totalItems / this.pageSize);

    this.pages = Array.from({ length: count }, (_, i) => i + 1);

  }

  onPageChange(p: number): void {

    if (p < 1 || p === this.page) return;

    this.page = p;
    this.loadDesigns();

  }

  onSearch(): void {
    this.page = 1;
    this.loadDesigns();
  }

  refresh(): void {

    this.searchText = '';
    this.activeFilter = '';
    this.productFilter = null;
    this.machineFilter = null;
    this.customerFilter = null;
    this.sortBy = 'id_desc';
    this.page = 1;

    this.loadDesigns();
  }

  onActiveFilterChange(val: string): void {

    this.activeFilter = val;
    this.page = 1;

    this.loadDesigns();
  }

  onFilterChange(): void {
    this.page = 1;
    this.loadDesigns();
  }

  cloneDesign(id: number): void {

    if (!confirm('Create a copy of this design?')) return;

    this.designSvc.clone(id).subscribe({
      next: res => {
        if (res?.success) {
          alert(res.message || 'Design cloned.');
          this.loadDesigns();
        } else {
          alert(res?.message || 'Clone failed.');
        }
      },
      error: err => {
        console.error('Clone failed', err);
        alert('Clone failed. Please try again.');
      }
    });
  }

  // ================= FORM =================

  buildForm() {
    return {
      id: 0,
      d_Code: null,
      dNo: '',
      collectionName: '',
      productId: null,
      machineId: null,
      customerId: null,
      active: true
    };
  }

  // ================= MODALS =================

  private openModal(ref: ElementRef): void {
    new bootstrap.Modal(ref.nativeElement).show();
  }

  private closeModal(ref: ElementRef): void {

    const modal = bootstrap.Modal.getInstance(ref.nativeElement);

    if (modal) modal.hide();

  }

  // ================= CREATE =================

  openCreate(): void {

    this.modalMode = 'create';

    this.formDesign = {
      dNo: '',
      collectionName: '',
      productId: null,
      machineId: null,
      customerId: null,
      active: true
    };

    this.openModal(this.formModal);
  }

  saveCreate(): void {

    if (this.saving) return;

    this.saving = true;

    const payload = {

      dNo: this.formDesign.dNo,
      collectionName: this.formDesign.collectionName,

      productId: this.formDesign.productId != null
        ? Number(this.formDesign.productId)
        : null,

      machineId: this.formDesign.machineId != null
        ? Number(this.formDesign.machineId)
        : null,

      customerId: this.formDesign.customerId != null
        ? Number(this.formDesign.customerId)
        : null,

      active: this.formDesign.active ?? true

    };

    this.designSvc.create(payload)
      .pipe(finalize(() => this.saving = false))
      .subscribe({

        next: res => {

          if (res?.success) {

            this.formDesign = this.buildForm();
            this.loadDesigns();
            this.closeModal(this.formModal);

          } else {
            console.error('Save failed:', res?.message);
          }

        },

        error: err => console.error('Create failed', err)

      });
  }

  // ================= EDIT =================

  openEdit(id: number): void {

    this.modalMode = 'edit';

    this.designSvc.getDetail(id).subscribe(res => {

      if (res?.success) {

        this.formDesign = {

          id: res.data.id,
          dNo: res.data.dNo,
          collectionName: res.data.collectionName,

          productId: res.data.productId,
          machineId: res.data.machineId,

          customerId: res.data.customerId,

          active: res.data.active

        };

        this.openModal(this.formModal);

      }

    });

  }

  saveEdit(): void {

    if (!this.formDesign?.id) return;

    const payload = {

      id: this.formDesign.id,

      dNo: this.formDesign.dNo,
      collectionName: this.formDesign.collectionName,

      productId: this.formDesign.productId != null
        ? Number(this.formDesign.productId)
        : null,

      machineId: this.formDesign.machineId != null
        ? Number(this.formDesign.machineId)
        : null,

      customerId: this.formDesign.customerId != null
        ? Number(this.formDesign.customerId)
        : null,

      active: this.formDesign.active ?? true

    };

    this.designSvc.update(payload).subscribe({

      next: res => {

        if (res?.success) {

          this.closeModal(this.formModal);
          this.loadDesigns();

        } else {

          alert(res?.message || 'Update failed.');

        }

      },

      error: err => {
        console.error('Update failed', err);
        alert('Update failed. Please try again.');
      }

    });

  }

  // ================= DETAIL =================

  openDetail(id: number): void {

    this.modalMode = 'detail';

    this.designSvc.getDetail(id).subscribe({

      next: res => {

        this.selectedDesign = res.data;
        this.openModal(this.formModal);

      },

      error: err => console.error(err)

    });

  }

  // ================= DELETE =================

  openDelete(id: number): void {

    this.deleteId = id;
    this.openModal(this.deleteModal);

  }

  confirmDelete(): void {

    if (!this.deleteId) return;

    this.designSvc.delete(this.deleteId).subscribe({

      next: res => {

        alert(res.message || 'Deleted successfully.');

        this.closeModal(this.deleteModal);
        this.loadDesigns();

      },

      error: err => console.error('Delete failed', err)

    });

  }

  // ================= EXPORT =================

  exportExcel(): void {

    if (!this.designs.length) {
      alert('No data available.');
      return;
    }

    // Hit the server's real Excel endpoint (EPPlus on the backend) instead of
    // building an HTML table and saving it as a .xls file. The backend
    // streams back a proper .xlsx workbook.
    const params = new URLSearchParams();
    if (this.searchText) params.set('search', this.searchText);
    if (this.activeFilter) params.set('activeFilter', this.activeFilter);

    const query = params.toString();
    const url = '/emb-products/gen_design/exporttoexcel' + (query ? '?' + query : '');

    window.open(url, '_blank');
  }

  exportPdf(): void {
    // Hit the server's real PDF endpoint (iTextSharp on the backend) instead
    // of opening an HTML window and triggering window.print(). The backend
    // streams back a proper application/pdf response which the browser will
    // download or render in a PDF viewer — not the print dialog.
    const params = new URLSearchParams();
    if (this.searchText) params.set('search', this.searchText);
    if (this.activeFilter) params.set('activeFilter', this.activeFilter);

    const query = params.toString();
    const url = '/emb-products/gen_design/exporttopdf' + (query ? '?' + query : '');

    window.open(url, '_blank');
  }

  printTable(): void {

    const content = document.querySelector('.table-responsive')?.innerHTML;

    const win = window.open('', '_blank');

    win!.document.write(`
      <html>
      <head><title>Print Designs</title></head>
      <body>${content}</body>
      </html>
    `);

    win!.print();
    win!.close();
  }

}
