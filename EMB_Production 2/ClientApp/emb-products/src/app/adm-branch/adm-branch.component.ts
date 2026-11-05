import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import {FormsModule,ReactiveFormsModule,FormBuilder,FormGroup,Validators} from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { BranchService } from '../services/adm-branch.service';
import {BranchListViewModel,BranchDetailViewModel,BranchForm} from '../models/branch.dto';
import { ApiResponseData, ApiPagedResponse } from '../shared/models/api-response';
import { HttpErrorResponse } from '@angular/common/http';


declare const bootstrap: any;

@Component({
  selector: 'app-adm-branch',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    HttpClientModule
  ],
  templateUrl: './adm-branch.component.html'
})
export class AdmBranchComponent implements OnInit {

  // ============================
  // DATA
  // ============================
  branches: BranchListViewModel[] = [];
  companies: { companyId: number; companyName: string }[] = [];

  // ============================
  // FORM
  // ============================
  branchForm!: FormGroup;

  // ============================
  // STATE
  // ============================
  editingBranch: BranchListViewModel | null = null;
  selectedBranch: BranchDetailViewModel | null = null;
  deleteTarget: BranchListViewModel | null = null;

  // ============================
  // SEARCH / PAGINATION
  // ============================
  searchText = '';
  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 1;
  paginationArray: number[] = [];

  constructor(
    private branchSvc: BranchService,
    private fb: FormBuilder
  ) {}

  // ============================
  // INIT
  // ============================
  ngOnInit(): void {
    this.initForm();
    this.loadCompanies();
    this.loadBranches();
  }

  private initForm(): void {
    this.branchForm = this.fb.group({
      branchId: [null],            // ✅ manual input
      name: ['', Validators.required],
      location: [''],
      contect: [''],
      companyId: [0, [Validators.required, Validators.min(1)]],
      isActive: [true]
    });
  }

  // ============================
  // SERVER → UI MAPPING
  // ============================
  private mapServerToList(items: any[]): BranchListViewModel[] {
    return items.map(x => ({
      id: Number(x.id),          // 🔑 THIS MUST EXIST
      branchId: x.branchId != null ? Number(x.branchId) : null,
      name: x.name ?? '',
      location: x.location ?? '',
      contect: x.contect ?? '',
      companyId: Number(x.companyId),
      companyName: x.companyName ?? '',
      isActive: Boolean(x.isActive),
      deleted: Boolean(x.deleted)
    }));
  }


  // ============================
  // LOADERS
  // ============================
  loadBranches(): void {
    this.branchSvc.getAll(this.searchText, this.page, this.pageSize).subscribe({
      next: (res: ApiPagedResponse<any>) => {
        const data = res?.data;
        const items = Array.isArray(data) ? data : data?.items ?? [];

        this.branches = this.mapServerToList(items);
        this.totalItems = data?.totalItems ?? items.length;

        this.setupPagination();
      },
      error: err => console.error('Load branches failed:', err)
    });
  }

  loadCompanies(): void {
    this.branchSvc.getCompanies().subscribe({
      next: (res: ApiResponseData<any>) => {
        const arr = res?.data ?? [];
        this.companies = arr.map((c: any) => ({
          companyId: Number(c.CompanyID ?? c.companyId ?? 0),
          companyName: c.CompanyName ?? c.companyName ?? ''
        }));
      },
      error: err => console.error('Load companies failed:', err)
    });
  }

  // ============================
  // SEARCH
  // ============================
  onSearch(): void {
    this.page = 1;
    this.loadBranches();
  }

  clearSearch(): void {
    this.searchText = '';
    this.page = 1;
    this.loadBranches();
  }

  // ============================
  // PAGINATION
  // ============================
  private setupPagination(): void {
    this.totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.paginationArray = Array.from(
      { length: this.totalPages },
      (_, i) => i + 1
    );
  }

  onPageChange(pg: number): void {
    if (pg < 1 || pg > this.totalPages) return;
    this.page = pg;
    this.loadBranches();
  }

  // ============================
  // CREATE / EDIT MODAL
  // ============================
  openCreate(): void {
    this.editingBranch = null;
    this.branchForm.reset({
      branchId: null,
      name: '',
      location: '',
      contect: '',
      companyId: 0,
      isActive: true,
      deleted: false
    });

    new bootstrap.Modal(
      document.getElementById('branchFormModal')
    ).show();
  }

  openEdit(b: BranchListViewModel): void {
    this.editingBranch = b;

    this.branchForm.patchValue({
      branchId: b.branchId,
      name: b.name,
      location: b.location,
      contect: b.contect,
      companyId: b.companyId,
      isActive: b.isActive,
      deleted: false
    });

    new bootstrap.Modal(
      document.getElementById('branchFormModal')
    ).show();
  }

  closeFormModal(): void {
    bootstrap.Modal
      .getInstance(document.getElementById('branchFormModal'))
      ?.hide();
  }

  // ============================
  // SAVE (CREATE / UPDATE)
  // ============================
  saveBranch(): void {
    if (this.branchForm.invalid) {
      this.branchForm.markAllAsTouched();
      return;
    }

    const payload: BranchForm = {
      ...this.branchForm.value,
      companyId: Number(this.branchForm.value.companyId)
    };

    if (!payload.companyId || payload.companyId <= 0) {
      alert('Please select a company.');
      return;
    }

    // ============================
    // SAVE (CREATE / UPDATE)
    // ============================
    if (this.editingBranch) {
      // 🔹 UPDATE (use payload.branchId – manual & editable)
      this.branchSvc
        .updateBranch(payload.branchId!, payload)
        .subscribe({
          next: (res: ApiResponseData<boolean>) => {
            if (res?.success) {
              this.loadBranches();
              this.closeFormModal();
            } else {
              alert(res?.message || 'Update failed');
            }
          },
          error: (err: any) => {
            console.error('Update error:', err);
            alert('Error updating branch.');
          }
        });
      return;
    }

    // 🔹 CREATE
    this.branchSvc.createBranch(payload).subscribe({
      next: (res: ApiResponseData<number>) => {
        if (res?.success) {
          this.loadBranches();
          this.closeFormModal();
        } else {
          alert(res?.message || 'Create failed');
        }
      },
      error: (err: any) => {
        console.error('Create error:', err);
        alert('Error creating branch.');
      }
    });

  }

  openDetail(b: BranchListViewModel): void {
    console.log('DETAIL CLICK RAW:', b);

    const idToSend = b.id ?? b.branchId;

    console.log('DETAIL ID SENT:', idToSend);

    if (!idToSend) {
      alert('No valid ID found for branch.');
      return;
    }

    this.branchSvc.getById(idToSend).subscribe({
      next: res => {
        if (!res?.data) {
          alert('Branch detail not found.');
          return;
        }

        this.selectedBranch = res.data;

        new bootstrap.Modal(
          document.getElementById('branchDetailModal')
        ).show();
      },
      error: err => {
        console.error(err);
        alert('Detail API failed.');
      }
    });
  }




  closeDetail(): void {
    bootstrap.Modal
      .getInstance(document.getElementById('branchDetailModal'))
      ?.hide();
  }

  // ============================
  // DELETE
  // ============================
  confirmDelete(b: BranchListViewModel): void {
    this.deleteTarget = b;
    new bootstrap.Modal(
      document.getElementById('deleteBranchModal')
    ).show();
  }

  closeDeleteModal(): void {
    bootstrap.Modal
      .getInstance(document.getElementById('deleteBranchModal'))
      ?.hide();
  }

  deleteBranch(): void {
    if (!this.deleteTarget) return;

    this.branchSvc.deleteBranch(this.deleteTarget.id).subscribe({
      next: (res: ApiResponseData<boolean>) => {
        if (res.success) {
          this.loadBranches();
          this.closeDeleteModal();
        } else {
          alert(res.message || 'Delete failed');
        }
      },
      error: (err: any) => {
        console.error('Delete error:', err);
        alert('Error deleting branch.');
      }
    });
  }

  // ============================
  // EXPORT CSV
  // ============================
  exportToCsv(filename = 'branches.csv'): void {
    if (!this.branches.length) {
      alert('No data to export');
      return;
    }

    const rows = [
      ['BranchId', 'Name', 'Location', 'Contact', 'Company', 'Status'],
      ...this.branches.map(b => [
        b.branchId,
        b.name || '-',
        b.location || '-',
        b.contect || '-',
        b.companyName || '-',
        b.isActive ? 'Active' : 'Inactive'
      ])
    ];

    const csv = rows
      .map(r =>
        r.map(c => `"${String(c).replace(/"/g, '""')}"`).join(',')
      )
      .join('\n');

    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = filename;
    link.click();
  }

  // ============================
  // PDF / PRINT
  // ============================
  exportTableForPdf(): void {
    if (!this.branches.length) {
      alert('No data to export');
      return;
    }

    const rows = this.branches.map(b => `
      <tr>
        <td>${b.branchId}</td>
        <td>${b.name || '-'}</td>
        <td>${b.location || '-'}</td>
        <td>${b.contect || '-'}</td>
        <td>${b.companyName || '-'}</td>
        <td>${b.isActive ? 'Active' : 'Inactive'}</td>
      </tr>
    `).join('');

    const html = `
      <html>
      <head>
        <title>Branches</title>
        <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet">
      </head>
      <body class="p-3">
        <h4>Branches</h4>
        <table class="table table-bordered table-hover">
          <thead>
            <tr>
              <th>ID</th><th>Name</th><th>Location</th>
              <th>Contact</th><th>Company</th><th>Status</th>
            </tr>
          </thead>
          <tbody>${rows}</tbody>
        </table>
      </body>
      </html>
    `;

    const win = window.open('', '_blank', 'width=1000,height=700');
    if (!win) return;

    win.document.write(html);
    win.document.close();
    setTimeout(() => win.print(), 300);
  }

  exportPdf(): void { this.exportTableForPdf(); }
  printTable(): void { this.exportTableForPdf(); }
}
