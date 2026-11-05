import { Component, OnInit, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { HrCustomerService } from '../services/hr-customer.service';
import { DropdownDataService } from '../shared/services/dropdown-data.service';

import { CustomerListViewModel, CustomerDetailViewModel } from '../shared/models/customer.dto';
import { ApiDataService, ExportColumn } from '../shared/services/api-data.service';

declare var bootstrap: any;

@Component({
  selector: 'app-hr-customers',
  standalone: true,
  templateUrl: './hr-customer.component.html',
  styleUrls: ['./hr-customer.component.css'],
  imports: [CommonModule, FormsModule, ReactiveFormsModule, HttpClientModule]
})
export class HrCustomerComponent implements OnInit {

  @ViewChild('customerDetailModal', { static: false })
  customerDetailModal!: ElementRef;

  customers: CustomerListViewModel[] = [];
  branches: { id: number; name: string }[] = [];

  customerForm!: FormGroup;

  selectedCustomer: CustomerDetailViewModel | null = null;
  editingCustomer: CustomerListViewModel | null = null;
  deleteTarget: CustomerListViewModel | null = null;

  searchText = '';
  activeFilter = '';

  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 1;
  paginationArray: number[] = [];

  loading = false;

  private fb = inject(FormBuilder);
  private service = inject(HrCustomerService);
  private dropdowns = inject(DropdownDataService);
  private apiData = inject(ApiDataService);

  ngOnInit(): void {
    this.initForm();
    this.loadData();
  }

  private initForm(): void {
    this.customerForm = this.fb.group({
      customerId: ['', Validators.required],
      name: ['', Validators.required],

      ntnNumber: [''],
      strnNumber: [''],

      customerAddress: [''],
      customerCity: [''],
      customerCountry: [''],

      customerEmail: ['', Validators.email],
      customerEmail2: ['', Validators.email],

      customerPhone: [''],
      customerPhone2: [''],
      customerMobile: [''],
      customerFax: [''],

      customerCreditLimitCap: [null],
      customerInvoicePeriod: [null],

      branchId: [null, Validators.required],
      active: [true]
    });
  }

  // ---------------------- LOAD ----------------------
  loadData(): void {
    forkJoin({
      branches: this.dropdowns.getBranches().pipe(
        catchError(() => of([]))
      ),

      customers: this.service
        .getAll(this.searchText, this.page, this.pageSize, this.activeFilter)
        .pipe(
          catchError(() =>
            of({
              success: false,
              data: { items: [], totalItems: 0 }
            })
          )
        )
    }).subscribe(({ branches, customers }) => {

      // ------------------ DROPDOWN ------------------
      this.branches = Array.isArray(branches) ? branches : [];

      // ------------------ PAGED CUSTOMERS ------------------
      const paged = customers?.data ?? { items: [], totalItems: 0 };

      this.customers = Array.isArray(paged.items)
        ? paged.items
        : [];

      // ❗❗ ONLY USE totalItems (remove total)
      this.totalItems =
        paged.totalItems ??
        this.customers.length;

      this.setupPagination();
    });
  }




  // ---------------------- PAGINATION ----------------------
  private setupPagination(): void {
    this.totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  onPageChange(pg: number): void {
    if (pg < 1 || pg > this.totalPages) return;
    this.page = pg;
    this.loadData();
  }

  // ---------------------- SEARCH & FILTER ----------------------
  onSearch(): void {
    this.page = 1;
    this.loadData();
  }

  refresh(): void {
    this.searchText = '';
    this.activeFilter = '';
    this.page = 1;
    this.loadData();
  }

  onActiveFilterChange(val: string): void {
    this.activeFilter = val;
    this.page = 1;
    this.loadData();
  }

  // ---------------------- CREATE ----------------------
  openCreate(): void {
    this.editingCustomer = null;
    this.customerForm.reset({
      customerId: '',
      name: '',
      ntnNumber: '',
      strnNumber: '',
      customerAddress: '',
      customerCity: '',
      customerCountry: '',
      customerEmail: '',
      customerEmail2: '',
      customerPhone: '',
      customerPhone2: '',
      customerMobile: '',
      customerFax: '',
      customerCreditLimitCap: null,
      customerInvoicePeriod: null,
      branchId: null,
      active: true
    });

    const modalEl = document.getElementById('customerFormModal');
    if (modalEl) {
      const modal = new bootstrap.Modal(modalEl);
      modal.show();
    }
  }

  // ---------------------- EDIT ----------------------
  openEdit(c: CustomerListViewModel): void {
    this.editingCustomer = c;

    this.customerForm.patchValue({
      customerId: c.customerId,
      name: c.name,

      customerCity: c.customerCity,
      customerCountry: c.customerCountry,
      customerEmail: c.customerEmail,
      customerPhone: c.customerPhone,
      customerMobile: c.customerMobile,

      branchId: c.branchId,
      active: c.active
    });

    const modalEl = document.getElementById('customerFormModal');
    if (modalEl) {
      const modal = new bootstrap.Modal(modalEl);
      modal.show();
    }
  }

  // ---------------------- SAVE ----------------------
  saveCustomer(): void {
    if (this.customerForm.invalid || this.loading) return;

    this.loading = true;
    const f = this.customerForm.value;

    const fd = new FormData();
    Object.entries({
      Customer_Id: f.customerId,
      Customer_Name: f.name,

      NTN_Number: f.ntnNumber,
      STRN_Number: f.strnNumber,

      Customer_Address: f.customerAddress,
      Customer_City: f.customerCity,
      Customer_Country: f.customerCountry,

      Customer_Email: f.customerEmail,
      Customer_Email2: f.customerEmail2,

      Customer_Phone: f.customerPhone,
      Customer_Phone2: f.customerPhone2,
      Customer_Mobile: f.customerMobile,
      Customer_Fax: f.customerFax,

      Customer_Credit_Limit_CAP: f.customerCreditLimitCap,
      Customer_Invoice_Period: f.customerInvoicePeriod,

      BranchId: f.branchId,
      Active: f.active
    }).forEach(([k, v]) => fd.append(k, v ?? ''));

    const request$ = this.editingCustomer
      ? this.service.update(this.editingCustomer.id, fd)
      : this.service.create(fd);

    request$.subscribe({
      next: res => {
        this.loading = false;
        alert(res.message || 'Saved successfully');

        const modalEl = document.getElementById('customerFormModal');
        if (modalEl) {
          bootstrap.Modal.getInstance(modalEl)?.hide();
        }

        this.loadData();
      },
      error: err => {
        this.loading = false;
        console.error('Error saving customer', err);
        alert('Error saving customer.');
      }
    });
  }

  closeFormModal(): void {
    const modalEl = document.getElementById('customerFormModal');
    if (modalEl) {
      bootstrap.Modal.getInstance(modalEl)?.hide();
    }
  }

  // ---------------------- DELETE ----------------------
  confirmDelete(c: CustomerListViewModel): void {
    this.deleteTarget = c;
    const modalEl = document.getElementById('deleteCustomerModal');
    if (modalEl) {
      const modal = new bootstrap.Modal(modalEl);
      modal.show();
    }
  }

  deleteCustomer(): void {
    if (!this.deleteTarget) return;

    this.service.delete(this.deleteTarget.id).subscribe({
      next: res => {
        alert(res.message || 'Deleted successfully');
        const modalEl = document.getElementById('deleteCustomerModal');
        if (modalEl) {
          bootstrap.Modal.getInstance(modalEl)?.hide();
        }
        this.loadData();
      },
      error: err => {
        console.error('Error deleting', err);
        alert('Error deleting customer.');
      }
    });
  }
  // DETAIL
  openDetail(id: number): void {
    this.service.getDetail(id).subscribe({
      next: detail => {
        this.selectedCustomer = detail;

        const modalEl = document.getElementById('customerDetailModal');
        if (modalEl) {
          const modal = new bootstrap.Modal(modalEl);
          modal.show();
        }
      },
      error: err => {
        console.error(err);
        alert('Failed to load detail');
      }
    });
  }


  closeDetail(): void {
    bootstrap.Modal.getInstance(this.customerDetailModal.nativeElement)?.hide();
    this.selectedCustomer = null;
  }

  // EXPORTS
  exportColumns: ExportColumn[] = [
    { header: 'Customer ID', field: 'customerId' },
    { header: 'Name', field: 'name' },
    { header: 'City', field: 'customerCity' },
    { header: 'Country', field: 'customerCountry' },
    { header: 'Phone', field: 'customerPhone' },
    { header: 'Mobile', field: 'customerMobile' },
    { header: 'Branch', field: 'branchName' },
    { header: 'Status', field: 'active' }
  ];

  exportCustomersToExcel(): void {
    this.apiData.exportToExcel(this.customers, 'CustomersList', this.exportColumns);
  }

  exportCustomersToPDF(): void {
    this.apiData.exportToPDF(this.customers, 'CustomersList', this.exportColumns);
  }

  printCustomers(): void {
    this.apiData.printTable(this.customers, this.exportColumns, 'Customers List');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.customers.length > 0 && this.customers.every((r: any) => this.selectedIds.has(r.id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.customers.forEach((r: any) => this.selectedIds.delete(r.id));
    else this.customers.forEach((r: any) => this.selectedIds.add(r.id));
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
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} customer(s)?`)) return;
    this.bulkBusy = true;
    this.service.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} customer(s)?`)) return;
    this.bulkBusy = true;
    this.service.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} customer(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.service.bulkDelete(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  Audit log loader
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.service.getAuditLog(id).subscribe({
      next: (r: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(r?.data) ? r.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')       return 'bg-primary';
    if (a === 'EDITED')        return 'bg-warning text-dark';
    if (a === 'ACTIVATED')     return 'bg-success';
    if (a === 'DEACTIVATED')   return 'bg-secondary';
    if (a === 'DELETED')       return 'bg-danger';
    return 'bg-light text-dark';
  }
}
