import { Component, OnInit, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import * as XLSX from 'xlsx';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

import {
  ModuleTemplateRowViewModel,
  ModuleTemplateDetailViewModel,
  ModuleTemplateDto,
  ApiResponseData,
  ApiResponsePaged,
} from '../../shared/models/module-template.dto';

import { ModuleTemplateService } from '../../services/module-template.service';

declare var bootstrap: any;

interface ModuleTemplateFormModel {
  id: number;
  code: string;
  name: string;
  remarks: string;
  branchId: number | null;
  active: boolean;
}

interface BranchOption {
  id: number;
  name: string;
}

@Component({
  selector: 'app-module-template',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './module-template.component.html',
  styleUrls: ['./module-template.component.css'],
})
export class ModuleTemplateComponent implements OnInit {
  // =======================================================
  // LIST + FILTERS
  // =======================================================
  items: ModuleTemplateRowViewModel[] = [];
  searchText = '';
  activeFilter = '';
  showDeleted = '';

  // Pagination
  page = 1;
  pageSize = 10;
  totalItems = 0;
  pages: number[] = [];

  // =======================================================
  // MODALS & FORM
  // =======================================================
  modalMode: 'create' | 'edit' = 'create';
  formModel: ModuleTemplateFormModel = this.createEmptyForm();

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  selectedDetail: ModuleTemplateDetailViewModel | null = null;
  itemToDelete: ModuleTemplateRowViewModel | null = null;

  // =======================================================
  // BRANCH DROPDOWN (temporary static)
  // =======================================================
  branches: BranchOption[] = [
    { id: 1, name: 'Head Office' },
    { id: 2, name: 'Branch 2' },
  ];

  constructor(private moduleSvc: ModuleTemplateService) { }

  ngOnInit(): void {
    this.loadData();
  }

  // =======================================================
  // EMPTY FORM
  // =======================================================
  createEmptyForm(): ModuleTemplateFormModel {
    return {
      id: 0,
      code: '',
      name: '',
      remarks: '',
      branchId: null,
      active: true,
    };
  }

  // =======================================================
  // LOAD DATA
  // =======================================================
  loadData(): void {
    this.moduleSvc
      .getAll(this.searchText, this.activeFilter, this.showDeleted, this.page, this.pageSize)
      .subscribe({
        next: (res: ApiResponsePaged<ModuleTemplateRowViewModel>) => {
          this.items = res.data?.items ?? [];
          this.totalItems = res.data?.total ?? 0;
          this.setupPagination();
        },
        error: (err) => {
          console.error('Failed to load module templates', err);
          this.items = [];
          this.totalItems = 0;
          this.setupPagination();
        },
      });
  }

  setupPagination(): void {
    const totalPages = Math.ceil(this.totalItems / this.pageSize) || 1;
    this.pages = Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  changePage(p: number): void {
    if (p < 1 || p === this.page) return;
    this.page = p;
    this.loadData();
  }

  onSearch(): void {
    this.page = 1;
    this.loadData();
  }

  onActiveFilterChange(): void {
    this.page = 1;
    this.loadData();
  }

  onShowDeletedChange(): void {
    this.page = 1;
    this.loadData();
  }

  // =======================================================
  // CREATE
  // =======================================================
  openCreate(): void {
    this.modalMode = 'create';
    this.formModel = this.createEmptyForm();
    this.openModal(this.formModal);
  }

  saveCreate(): void {
    if (!this.formModel.code || !this.formModel.name) {
      alert('Code and Name are required.');
      return;
    }

    const payload: ModuleTemplateDto = {
      Id: 0,
      Code: this.formModel.code,
      Name: this.formModel.name,
      Remarks: this.formModel.remarks,
      BranchId: this.formModel.branchId,
      Active: this.formModel.active,
    };

    this.moduleSvc.create(payload).subscribe({
      next: (res: ApiResponseData<number>) => {
        alert(res.message || 'Record created.');
        if (res.success) {
          this.closeModal(this.formModal);
          this.loadData();
        }
      },
      error: (err) => {
        console.error('Create failed', err);
        alert('Failed to create record.');
      },
    });
  }

  // =======================================================
  // EDIT
  // =======================================================
  openEdit(id: number): void {
    this.modalMode = 'edit';
    this.formModel = this.createEmptyForm();

    this.moduleSvc.getDetail(id).subscribe({
      next: (res) => {
        if (res.success && res.data) {
          const d = res.data;

          this.formModel = {
            id: d.Id,
            code: d.Code,
            name: d.Name,
            remarks: d.Remarks || '',
            branchId: d.BranchId || null,
            active: d.Active ?? true,
          };

          this.openModal(this.formModal);
        } else {
          alert(res.message || 'Failed to load details.');
        }
      },
      error: (err) => {
        console.error('Edit detail failed', err);
        alert('Failed to load details.');
      },
    });
  }

  saveEdit(): void {
    if (!this.formModel.code || !this.formModel.name) {
      alert('Code and Name are required.');
      return;
    }

    const payload: ModuleTemplateDto = {
      Id: this.formModel.id,
      Code: this.formModel.code,
      Name: this.formModel.name,
      Remarks: this.formModel.remarks,
      BranchId: this.formModel.branchId,
      Active: this.formModel.active,
    };

    this.moduleSvc.update(this.formModel.id, payload).subscribe({
      next: (res) => {
        alert(res.message || 'Record updated.');
        if (res.success) {
          this.closeModal(this.formModal);
          this.loadData();
        }
      },
      error: (err) => {
        console.error('Update failed', err);
        alert('Failed to update record.');
      },
    });
  }

  // =======================================================
  // DETAIL
  // =======================================================
  openDetail(id: number): void {
    this.selectedDetail = null;

    this.moduleSvc.getDetail(id).subscribe({
      next: (res) => {
        if (res.success && res.data) {
          this.selectedDetail = res.data;
          this.openModal(this.detailModal);
        } else {
          alert(res.message || 'Failed to load details.');
        }
      },
      error: (err) => console.error('Detail failed', err),
    });
  }

  // =======================================================
  // DELETE
  // =======================================================
  confirmDelete(item: ModuleTemplateRowViewModel): void {
    this.itemToDelete = item;
    this.openModal(this.deleteModal);
  }

  confirmDeleteFinal(): void {
    if (!this.itemToDelete) return;

    this.moduleSvc.delete(this.itemToDelete.Id).subscribe({
      next: (res) => {
        alert(res.message || 'Deleted successfully.');

        const modal = bootstrap.Modal.getInstance(this.deleteModal.nativeElement);
        if (modal) modal.hide();

        this.loadData();
      },
      error: (err) => {
        console.error('Delete failed', err);
        alert('Failed to delete record.');
      },
    });
  }

  // =======================================================
  // EXPORT EXCEL
  // =======================================================
  exportExcel(): void {
    if (!this.items.length) {
      alert('No data to export.');
      return;
    }

    const exportData = this.items.map((x) => ({
      Code: x.Code,
      Name: x.Name,
      Branch: x.BranchName ?? '',
      Active: x.Active ? 'Yes' : 'No',
      Deleted: x.Deleted ? 'Yes' : 'No',
      Created: x.CreatedDate ?? '',
      Updated: x.UpdatedDate ?? '',
    }));

    const ws = XLSX.utils.json_to_sheet(exportData);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Data');

    XLSX.writeFile(wb, 'ModuleTemplate.xlsx');
  }

  // =======================================================
  // EXPORT PDF
  // =======================================================
  exportPDF(): void {
    if (!this.items.length) {
      alert('No data to export.');
      return;
    }

    const doc = new jsPDF('landscape');

    autoTable(doc, {
      head: [['Code', 'Name', 'Branch', 'Active', 'Deleted', 'Created', 'Updated']],
      body: this.items.map((x) => [
        x.Code,
        x.Name,
        x.BranchName ?? '',
        x.Active ? 'Yes' : 'No',
        x.Deleted ? 'Yes' : 'No',
        x.CreatedDate ?? '',
        x.UpdatedDate ?? '',
      ]),
      startY: 20,
      styles: { fontSize: 9 },
      headStyles: { fillColor: [13, 110, 253] },
    });

    doc.save('ModuleTemplate.pdf');
  }

  // =======================================================
  // PRINT
  // =======================================================
  printTable(): void {
    if (!this.items.length) {
      alert('No data to print.');
      return;
    }

    let html = `
      <h3 style="text-align:center;margin-bottom:20px;">Module Template Report</h3>
      <table border="1" cellspacing="0" cellpadding="6" style="width:100%;border-collapse:collapse;font-size:13px;">
      <thead>
        <tr style="background:#0d6efd;color:#fff;">
          <th>Code</th>
          <th>Name</th>
          <th>Branch</th>
          <th>Active</th>
          <th>Deleted</th>
          <th>Created</th>
          <th>Updated</th>
        </tr>
      </thead>
      <tbody>
    `;

    this.items.forEach((x) => {
      html += `
        <tr>
          <td>${x.Code}</td>
          <td>${x.Name}</td>
          <td>${x.BranchName ?? ''}</td>
          <td>${x.Active ? 'Yes' : 'No'}</td>
          <td>${x.Deleted ? 'Yes' : 'No'}</td>
          <td>${x.CreatedDate ?? ''}</td>
          <td>${x.UpdatedDate ?? ''}</td>
        </tr>`;
    });

    html += `</tbody></table>`;

    const win = window.open('', '_blank', 'width=1000,height=800');
    if (!win) return;

    win.document.write(`
      <html>
        <head>
          <title>Module Template Report</title>
          <style>
            body { font-family: Arial; padding: 20px; }
            table { width: 100%; border-collapse: collapse; }
            th, td { border: 1px solid #777; padding: 8px; }
            th { background: #0d6efd; color: white; }
          </style>
        </head>
        <body>${html}</body>
      </html>
    `);

    win.document.close();
    win.print();
  }

  // =======================================================
  // MODAL HELPERS
  // =======================================================
  openModal(elRef: ElementRef): void {
    new bootstrap.Modal(elRef.nativeElement, { backdrop: 'static' }).show();
  }

  closeModal(elRef: ElementRef): void {
    const instance = bootstrap.Modal.getInstance(elRef.nativeElement);
    if (instance) instance.hide();
  }
}
