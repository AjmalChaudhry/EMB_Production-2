import { Component, ElementRef, ViewChild, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule, ActivatedRoute } from '@angular/router';

import { JobTrackingHistoryService } from '../services/job-tracking-history.service';
import {
  JobTrackingHistoryRowViewModel,
  JobTrackingHistoryDetailViewModel,
  JobTrackingHistoryDto
} from '../shared/models/job-tracking-history.dto';

import {
  ApiPagedResponse,
  ApiResponseData,
  ApiResponse
} from '../shared/models/api-response';

import { DropdownDataService } from '../shared/services/dropdown-data.service';
import { DropdownItem } from '../shared/services/dropdown-data.service';

import { jsPDF } from 'jspdf';
import autoTable from 'jspdf-autotable';

@Component({
  selector: 'app-job-tracking-history',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './job-tracking-history.component.html',
  styleUrls: ['./job-tracking-history.component.css']
})
export class JobTrackingHistoryComponent implements OnInit {

  // ================= STATE =================
  workOrderItemId = 0;

  items: JobTrackingHistoryRowViewModel[] = [];
  detailItem: JobTrackingHistoryDetailViewModel | null = null;

  branches: DropdownItem[] = [];
  statusList: string[] = ['Started', 'In Progress', 'Completed'];

  page = 1;
  pageSize = 10;
  totalCount = 0;
  totalPages = 1;
  loading = false;

  deleteId = 0;
  mode: 'create' | 'edit' | 'detail' = 'create';

  searchText: string = '';
  branchId: number | null = null;
  statusFilter: string = '';

  // ================= FORM (DTO) =================
  form: JobTrackingHistoryDto = {
    id: 0,
    workOrderItemId: 0,
    branchId: null,
    status: '',
    statusDate: new Date(),
    remarks: '',
    active: true
  };

  // ================= MODALS =================
  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(
    private svc: JobTrackingHistoryService,
    private dropdownSvc: DropdownDataService,
    private route: ActivatedRoute
  ) { }

  // ================= INIT =================
  ngOnInit(): void {
    const param = this.route.snapshot.paramMap.get('workOrderItemId');
    this.workOrderItemId = param ? Number(param) : 0;

    console.log('WOI ID =', this.workOrderItemId);

    if (this.workOrderItemId <= 0) {
      alert('Invalid Work Order Item');
      return;
    }

    this.loadBranches();
    this.load();
  }


  // ================= DROPDOWNS =================
  loadBranches(): void {
    this.dropdownSvc.getBranches().subscribe({
      next: list => this.branches = list || [],
      error: () => this.branches = []
    });
  }

  // ================= DATA =================
  load(): void {
    if (this.page < 1) this.page = 1;

    this.loading = true;

    this.svc.listPaged(this.workOrderItemId, this.page, this.pageSize)
      .subscribe({
        next: (res: any) => {
          this.loading = false;

          if (!res?.success || !res?.data) {
            this.items = [];
            this.totalCount = 0;
            this.totalPages = 1;
            return;
          }

          this.items = (res.data.items ?? []).map((x: any) => ({
            id: x.Id,
            workOrderItemId: x.WorkOrderItemId,
            workOrderItemLabel: x.WorkOrderItemLabel,

            status: x.Status,
            statusDate: this.parseNetDate(x.StatusDate), // ✅ Date

            remarks: x.Remarks,

            active: x.Active,
            deleted: x.Deleted,

            branchId: x.BranchId,
            branchName: x.BranchName,

            createdBy: x.CreatedBy,
            createdByName: x.CreatedByName,
            createdDate: this.parseNetDate(x.CreatedDate),

            updatedBy: x.UpdatedBy,
            updatedByName: x.UpdatedByName,
            updatedDate: this.parseNetDate(x.UpdatedDate)
          }));

          this.totalCount = res.data.total ?? res.data.totalItems ?? 0;
          this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
        },
        error: err => {
          console.error('Load failed', err);
          this.loading = false;
          this.items = [];
          this.totalCount = 0;
          this.totalPages = 1;
        }
      });
  }
  // 🔧 Parses .NET JSON dates like /Date(1765805342803)/
  parseNetDate(v: any): Date {
    if (!v) return new Date();

    // .NET JSON format: /Date(1765805342803)/
    if (typeof v === 'string' && v.startsWith('/Date(')) {
      const ms = Number(v.replace('/Date(', '').replace(')/', ''));
      return isNaN(ms) ? new Date() : new Date(ms);
    }

    const d = new Date(v);
    return isNaN(d.getTime()) ? new Date() : d;
  }


  nextPage(): void {
    if (this.page < this.totalPages) {
      this.page++;
      this.load();
    }
  }

  prevPage(): void {
    if (this.page > 1) {
      this.page--;
      this.load();
    }
  }

  // ================= CREATE =================
  openCreate(): void {
    this.mode = 'create';

    this.form = {
      id: 0,
      workOrderItemId: this.workOrderItemId, // 🔒 LOCKED FK
      branchId: null,
      status: '',
      statusDate: new Date(),
      remarks: '',
      active: true
    };

    this.openModal(this.formModal);
  }

  saveCreate(): void {
    if (!this.form.status.trim()) {
      alert('Status is required');
      return;
    }

    this.svc.create(this.form).subscribe({
      next: res => {
        if (res.success) {
          this.closeForm();
          this.load();
        } else {
          alert(res.message || 'Create failed');
        }
      }
    });
  }

  // ================= EDIT =================
  openEdit(id: number): void {
    this.mode = 'edit';

    this.svc.detail(id).subscribe({
      next: res => {
        if (!res.success || !res.data) {
          alert('Invalid record');
          return;
        }

        const d: any = res.data;

        console.log('EDIT DATA FROM API:', d);

        this.form = {
          id: d.Id ?? d.id,
          workOrderItemId: d.WorkOrderItemId,
          branchId: d.BranchId ?? null,
          status: d.Status ?? '',
          statusDate: this.parseNetDate(d.StatusDate), // ✅ Date ONLY
          remarks: d.Remarks ?? '',
          active: d.Active ?? true
        };

        console.log('FORM AFTER EDIT BIND:', this.form);

        this.openModal(this.formModal);
      },
      error: err => console.error(err)
    });
  }


  saveUpdate(): void {
    if (!this.form.id || this.form.id <= 0) {
      alert('Invalid record');
      return;
    }

    this.svc.update(this.form).subscribe(res => {
      if (res.success) {
        this.closeForm();
        this.load();
      } else {
        alert(res.message || 'Update failed');
      }
    });
  }


  // ================= DETAIL =================
  openDetail(id: number): void {
    this.mode = 'detail';
    this.detailItem = null;

    this.svc.detail(id).subscribe(res => {
      if (!res.success || !res.data) return;

      const d: any = res.data;

      // 🔑 MAP PascalCase → camelCase
      this.detailItem = {
        id: d.Id,
        workOrderItemId: d.WorkOrderItemId,
        workOrderItemLabel: d.WorkOrderItemLabel,

        status: d.Status,
        statusDate: this.parseNetDate(d.StatusDate),
        statusDateText: d.StatusDateText,

        remarks: d.Remarks,

        branchId: d.BranchId,
        branchName: d.BranchName,

        active: d.Active,
        deleted: d.Deleted,

        createdBy: d.CreatedBy,
        createdByEmployeeId: d.CreatedByName,
        createdDate: this.parseNetDate(d.CreatedDate),
        createdDateText: d.CreatedDateText,

        updatedBy: d.UpdatedBy,
        updatedByEmployeeId: d.UpdatedByName,
        updatedDate: this.parseNetDate(d.UpdatedDate),
        updatedDateText: d.UpdatedDateText
      };

      this.openModal(this.detailModal);
    });
  }


  // ================= DELETE =================
  openDelete(id: number): void {
    this.deleteId = id;
    this.openModal(this.deleteModal);
  }

  confirmDelete(): void {
    if (!this.deleteId) return;

    this.svc.delete(this.deleteId).subscribe({
      next: res => {
        console.log('DELETE RESPONSE:', res);
        if (res.success) {
          this.closeDelete();
          this.load();
        } else {
          alert(res.message);
        }
      },
      error: err => console.error('DELETE ERROR', err)
    });
  }


  // ================= MODAL HELPERS =================
  closeForm(): void {
    this.closeModal(this.formModal);
  }

  closeDetail(): void {
    this.closeModal(this.detailModal);
  }

  closeDelete(): void {
    this.closeModal(this.deleteModal);
  }

  private openModal(ref: ElementRef): void {
    const el = ref.nativeElement;
    el.classList.add('show');
    el.style.display = 'block';
    document.body.classList.add('modal-open');

    const b = document.createElement('div');
    b.className = 'modal-backdrop fade show';
    document.body.appendChild(b);
  }

  private closeModal(ref: ElementRef): void {
    const el = ref.nativeElement;
    el.classList.remove('show');
    el.style.display = 'none';
    document.body.classList.remove('modal-open');
    document.querySelectorAll('.modal-backdrop').forEach(x => x.remove());
  }

  // ================= EXPORT =================
  exportPdf(): void {
    const doc = new jsPDF();
    doc.text(`Job Tracking History (WOI #${this.workOrderItemId})`, 14, 12);

    autoTable(doc, {
      startY: 18,
      head: [['Id', 'Status', 'Date', 'Remarks', 'Branch', 'Active']],
      body: this.items.map(x => [
        x.id,
        x.status ?? '',
        x.statusDate ? String(x.statusDate) : '',
        x.remarks ?? '',
        x.branchName ?? '',
        x.active ? 'Yes' : 'No'
      ])
    });

    doc.save(`JobTrackingHistory_WOI_${this.workOrderItemId}.pdf`);
  }

  exportExcel(): void {
    if (!this.items.length) {
      alert('No data to export.');
      return;
    }

    const rows = this.items.map(x => ({
      Id: x.id,
      WorkOrderItemId: this.workOrderItemId,
      Status: x.status ?? '',
      StatusDate: x.statusDate ?? '',
      Remarks: x.remarks ?? '',
      Branch: x.branchName ?? '',
      Active: x.active ? 'Yes' : 'No'
    }));

    const headers = Object.keys(rows[0]);

    const csv = [
      headers.join(','),                                  // header row
      ...rows.map(r =>
        headers.map(h =>
          `"${String((r as any)[h] ?? '').replace(/"/g, '""')}"`
        ).join(',')
      )
    ].join('\n');

    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = `JobTrackingHistory_WOI_${this.workOrderItemId}.csv`;
    a.click();

    URL.revokeObjectURL(url);
  }
  print(): void {
    if (!this.items.length) {
      alert('No data to print.');
      return;
    }

    const win = window.open('', '_blank', 'width=900,height=650');
    if (!win) return;

    win.document.write(`
    <html>
    <head>
      <title>Job Tracking History</title>
      <style>
        body { font-family: Arial, sans-serif; padding: 16px; }
        h2 { margin-bottom: 10px; }
        table { width: 100%; border-collapse: collapse; }
        th, td {
          border: 1px solid #ccc;
          padding: 6px;
          font-size: 12px;
          text-align: left;
        }
        th { background: #f2f2f2; }
      </style>
    </head>
    <body>
      <h2>Job Tracking History</h2>
      <div><strong>Work Order Item:</strong> ${this.workOrderItemId}</div>
      <br/>

      <table>
        <thead>
          <tr>
            <th>Id</th>
            <th>Status</th>
            <th>Date</th>
            <th>Remarks</th>
            <th>Branch</th>
            <th>Active</th>
          </tr>
        </thead>
        <tbody>
          ${this.items.map(x => `
            <tr>
              <td>${x.id}</td>
              <td>${x.status ?? ''}</td>
              <td>${x.statusDate ?? ''}</td>
              <td>${x.remarks ?? ''}</td>
              <td>${x.branchName ?? ''}</td>
              <td>${x.active ? 'Yes' : 'No'}</td>
            </tr>
          `).join('')}
        </tbody>
      </table>

      <script>
        window.onload = function () {
          window.print();
          window.close();
        };
      </script>
    </body>
    </html>
  `);

    win.document.close();
  }
  private cell(v: any): string | number | null {
    return v === undefined ? '' : v;
  }

}
