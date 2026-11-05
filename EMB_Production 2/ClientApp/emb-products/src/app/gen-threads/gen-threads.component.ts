import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DropdownItem } from '../shared/models/api-response';
import {
  ThreadRowViewModel,
  ThreadDetailViewModel,
  ThreadFormDto
} from '../shared/models/thread.dto';

import { ThreadService } from '../services/thread.service';

declare var bootstrap: any;

@Component({
  selector: 'app-gen-threads',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './gen-threads.component.html',
  styleUrls: ['./gen-threads.component.css']
})
export class GenThreadsComponent implements OnInit {

  // ============================================================
  // DATA
  // ============================================================
  threads: ThreadRowViewModel[] = [];
  selectedThread: ThreadDetailViewModel | null = null;
  pendingDeleteId: number | null = null;

  // Dropdowns
  supplierList: DropdownItem[] = [];

  branchList: { id: number; name: string }[] = [];

  supplierFilter = '';
  branchFilter = '';

  // ============================================================
  // PAGINATION
  // ============================================================
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  // ============================================================
  // FILTERS
  // ============================================================
  searchText = '';
  activeFilter = '';
  isLoading = false;

  // ============================================================
  // FORM
  // ============================================================
  modalMode: 'create' | 'edit' = 'create';
  formThread: ThreadFormDto = this.createEmptyThread();

  // ============================================================
  // MODALS
  // ============================================================
  @ViewChild('threadFormModal') threadFormModal!: ElementRef;
  @ViewChild('threadDetailModal') threadDetailModal!: ElementRef;
  @ViewChild('threadDeleteModal') threadDeleteModal!: ElementRef;

 
  constructor(private threadSvc: ThreadService) { }

  // ============================================================
  // INIT
  // ============================================================
  ngOnInit(): void {
    this.loadThreads();
    this.loadBranches();
    this.loadSuppliers();
  }

  // ============================================================
  // HELPERS
  // ============================================================
  createEmptyThread(): ThreadFormDto {
    return {
      Id: 0,     
      Name: '',
      Type: '',
      Material: '',
      YarnCount: '',
      Color: '',
      ColorCode: '',
      UnitLength: null,
      UnitOfMeasure: '',
      UnitCost: null,
      SupplierId: null,
      BranchId: null,
      Active: true
    };
  }


  setupPagination(): void {
    const totalPages = Math.ceil(this.totalItems / this.pageSize) || 1;
    this.pages = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  // ============================================================
  // BRANCH DROPDOWN
  // ============================================================
  loadBranches(): void {
    this.threadSvc.getBranchesDropdown().subscribe({
      next: res => {
        // central dropdowns controller returns { data: [{id, name}] }
        this.branchList = res?.data ?? [];
      },
      error: () => {
        console.error('Branch dropdown load failed');
        this.branchList = [];
      }
    });
  }

  onBranchFilterChange(): void {
    this.page = 1;
    this.loadThreads();
  }

  // ============================================================
  // SUPPLIER DROPDOWN (FROM GEN_SUPPLIERS DROPDOWN)
  // ============================================================
  loadSuppliers(): void {
    this.threadSvc.getSuppliersDropdown().subscribe({
      next: res => {
        this.supplierList = res?.data ?? [];
      },
      error: () => {
        console.error('Supplier dropdown load failed');
        this.supplierList = [];
      }
    });
  }


  onSupplierFilterChange(): void {
    this.page = 1;
    this.loadThreads();
  }

  // ============================================================
  // SEARCH & FILTER
  // ============================================================
  onSearch(): void {
    this.page = 1;
    this.loadThreads();
  }

  onActiveFilterChange(): void {
    this.page = 1;
    this.loadThreads();
  }

  changePage(p: number): void {
    if (p < 1 || p > this.pages.length) return;
    this.page = p;
    this.loadThreads();
  }

  // ============================================================
  // LOAD THREAD LIST
  // ============================================================
  loadThreads(): void {
    this.isLoading = true;

    this.threadSvc
      .getAll(this.searchText, this.page, this.pageSize, this.activeFilter)
      .subscribe({

        next: res => {
          this.isLoading = false;

          const raw = res?.data;
          let list: any[] = [];

          if (Array.isArray(raw)) {
            list = raw;
            this.totalItems = raw.length;
          }
          else if (raw && typeof raw === "object") {

            list =
              Array.isArray(raw.items) ? raw.items :
                Array.isArray(raw.Items) ? raw.Items :
                  [];

            this.totalItems =
              (typeof raw.totalItems === "number" ? raw.totalItems : null) ??
              (typeof raw.TotalItems === "number" ? raw.TotalItems : null) ??
              (typeof raw.total === "number" ? raw.total : null) ??
              (typeof raw.Total === "number" ? raw.Total : null) ??
              list.length;
          }
          else {
            list = [];
            this.totalItems = 0;
          }

          this.threads = list;
          this.setupPagination();
        },

        error: () => {
          this.isLoading = false;
          this.threads = [];
          this.totalItems = 0;
          this.setupPagination();
        }
      });
  }


  // ============================================================
  // CREATE
  // ============================================================
  openCreate(): void {
    this.modalMode = 'create';
    this.formThread = this.createEmptyThread();

    this.loadBranches();
    this.loadSuppliers();

    setTimeout(() => this.openModal(this.threadFormModal), 150);
  }

  saveCreate(): void {
    if ( !this.formThread.Name) {
      alert('Code and Name are required.');
      return;
    }

    const payload: ThreadFormDto = { ...this.formThread, Id: 0 };

    this.threadSvc.create(payload).subscribe({
      next: res => {
        alert(res.message || 'Thread created.');
        if (res.success) {
          this.closeModal(this.threadFormModal);
          this.loadThreads();
        }
      },
      error: err => {
        console.error('Create failed', err);
        alert('Failed to create thread.');
      }
    });
  }

  // ============================================================
  // EDIT
  // ============================================================
  openEdit(id: number): void {
    this.modalMode = 'edit';

    this.threadSvc.getDetail(id).subscribe({
      next: res => {
        if (res.success && res.data) {
          const d = res.data;

          this.formThread = {
            Id: d.Id,           
            Name: d.Name,
            Type: d.Type,
            Material: d.Material,
            YarnCount: d.YarnCount,
            Color: d.Color,
            ColorCode: d.ColorCode,
            UnitLength: d.UnitLength,
            UnitOfMeasure: d.UnitOfMeasure,
            UnitCost: d.UnitCost,
            SupplierId: d.SupplierId,
            BranchId: d.BranchId,
            Active: d.Active
          };

          setTimeout(() => this.openModal(this.threadFormModal), 150);
        } else {
          alert('Failed to load thread for edit.');
        }
      },
      error: err => {
        console.error(err);
        alert('Failed to load thread for edit.');
      }
    });
  }


  saveEdit(): void {
    if (!this.formThread.Name) {
      alert('Code and Name are required.');
      return;
    }

    this.threadSvc.update(this.formThread).subscribe({
      next: res => {
        alert(res.message || 'Thread updated.');
        if (res.success) {
          this.closeModal(this.threadFormModal);
          this.loadThreads();
        }
      },
      error: err => {
        console.error('Update failed', err);
        alert('Failed to update thread.');
      }
    });
  }

  // ============================================================
  // DETAIL
  // ============================================================
 
  openDetail(id: number) {
    this.threadSvc.getDetail(id).subscribe({
      next: res => {
        if (res.success) {
          this.selectedThread = res.data;
          this.openModal(this.threadDetailModal);
        }
      }
    });
  }

  closeDetailModal(): void {
    this.closeModal(this.threadDetailModal);
  }


  // ============================================================
  // DELETE
  // ============================================================
  confirmDelete(): void {
    if (this.pendingDeleteId == null) return;

    const id = this.pendingDeleteId;

    this.threadSvc.delete(id).subscribe({
      next: res => {
        alert(res.message || 'Thread deleted.');
        if (res.success) {
          this.loadThreads();
        }
        this.pendingDeleteId = null;
        this.closeDelete();
      },
      error: () => {
        this.pendingDeleteId = null;
        this.closeDelete();
      }
    });
  }


  // ============================================================
  // EXPORTS
  // ============================================================
  // ============================================================
  // EXPORTS
  // Excel and PDF hit the server's real endpoints (EPPlus + iTextSharp).
  // Print is client-side — opens the table in a new tab and triggers
  // window.print(). The previous implementations were stubs that alerted
  // "coming soon" or printed the entire page (sidebar + topbar included).
  // ============================================================

  private buildExportQuery(): string {
    const params = new URLSearchParams();
    if (this.searchText)   params.set('search', this.searchText);
    if (this.activeFilter) params.set('activeFilter', this.activeFilter);
    const q = params.toString();
    return q ? '?' + q : '';
  }

  exportToExcel(): void {
    if (!this.threads.length) return alert('No data');
    window.open('/emb-products/threads/exporttoexcel' + this.buildExportQuery(), '_blank');
  }

  exportToPdf(): void {
    if (!this.threads.length) return alert('No data');
    window.open('/emb-products/threads/exporttopdf' + this.buildExportQuery(), '_blank');
  }

  printTable(): void {
    if (!this.threads.length) return alert('No data');

    const esc = (v: any) => String(v ?? '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

    const rows = this.threads.map((t: any) => `
      <tr>
        <td>${esc(t.Code)}</td>
        <td>${esc(t.Name)}</td>
        <td>${esc(t.Type)}</td>
        <td>${esc(t.Material)}</td>
        <td>${esc(t.YarnCount)}</td>
        <td>${esc(t.Color)}</td>
        <td>${esc(t.ColorCode)}</td>
        <td class="num">${esc(t.UnitLength)}</td>
        <td>${esc(t.UnitOfMeasure)}</td>
        <td class="num">${esc(t.UnitCost)}</td>
        <td>${esc(t.BranchName)}</td>
        <td>${esc(t.SupplierName)}</td>
        <td>${t.Active ? 'Active' : 'Inactive'}</td>
      </tr>`).join('');

    const html = `<!doctype html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>Threads — Print</title>
        <style>
          @page { size: A4 landscape; margin: 10mm; }
          body { font-family: "Segoe UI", Arial, sans-serif; color: #212529; font-size: 10pt; }
          h2 { text-align: center; margin: 0 0 10px; }
          table { width: 100%; border-collapse: collapse; }
          thead { background: #0d6efd; color: #fff; }
          th, td { border: 1px solid #ccc; padding: 4px 6px; text-align: left; vertical-align: top; font-size: 9pt; }
          td.num, th.num { text-align: right; }
          tbody tr:nth-child(even) { background: #f6f8fb; }
          .footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
        </style>
      </head>
      <body>
        <h2>Threads &mdash; List</h2>
        <table>
          <thead>
            <tr>
              <th>Code</th><th>Name</th><th>Type</th><th>Material</th>
              <th>Yarn Count</th><th>Color</th><th>Color Code</th>
              <th class="num">Unit Length</th><th>Unit</th>
              <th class="num">Unit Cost</th><th>Branch</th><th>Supplier</th><th>Status</th>
            </tr>
          </thead>
          <tbody>${rows}</tbody>
        </table>
        <div class="footer">Generated: ${new Date().toLocaleString()} | Rows: ${this.threads.length}</div>
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

  // ============================================================
  // MODALS
  // ============================================================
  openModal(el: ElementRef): void {
    new bootstrap.Modal(el.nativeElement).show();
  }

  closeModal(el: ElementRef): void {
    const modal = bootstrap.Modal.getInstance(el.nativeElement);
    if (modal) modal.hide();
  }

  closeDetail(): void {
    this.closeModal(this.threadDetailModal);
  }
  // OPEN DELETE MODAL
  openDeleteConfirm(id: number): void {
    this.pendingDeleteId = id;
    this.openModal(this.threadDeleteModal);
  }

  // CLOSE DELETE MODAL
  closeDelete(): void {
    if (this.threadDeleteModal) {
      this.closeModal(this.threadDeleteModal);
    }
  }

}
