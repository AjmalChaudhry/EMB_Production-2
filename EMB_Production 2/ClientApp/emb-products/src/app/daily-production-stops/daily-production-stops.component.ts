import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DailyProductionStopsService } from '../services/daily-production-stops.service';

import {
  DailyProductionStopRowViewModel,
  DailyProductionStopDetailViewModel,
  DailyProductionStopInputDto,
  DowntimeReasonStatVm
} from '../shared/models/daily-production-stops.dto';

import { DropdownItem } from '../shared/models/api-response';

declare var bootstrap: any;

@Component({
  selector: 'app-daily-production-stops',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './daily-production-stops.component.html',
  styleUrls: ['./daily-production-stops.component.css']
})
export class DailyProductionStopsComponent implements OnInit {

  Math = Math;

  // list
  items: DailyProductionStopRowViewModel[] = [];
  totalItems = 0;
  page = 1;
  pageSize = 10;
  loading = false;

  searchText = '';
  productionFilter: number | null = null;

  // dropdown
  productions: DropdownItem[] = [];

  // analytics
  totalDowntimeMinutes = 0;
  reasonStats: DowntimeReasonStatVm[] = [];

  // modals
  private currentModal: any = null;

  @ViewChild('stopModal') stopModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  modalMode: 'create' | 'edit' = 'create';
  selectedId: number | null = null;

  detailModel: DailyProductionStopDetailViewModel | null = null;
  deleteModel: DailyProductionStopRowViewModel | null = null;

  formModel: DailyProductionStopInputDto = this.createEmptyForm();

  constructor(private svc: DailyProductionStopsService, private stopSvc: DailyProductionStopsService) { }

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadData();
  }

  createEmptyForm(): DailyProductionStopInputDto {
    return {
      ProductionId: null,
      StopStart: null,
      StopEnd: null,
      Reason: ''
    };
  }

  // -------------------------
  // dropdowns
  // -------------------------
  loadDropdowns(): void {
    this.svc.getProductionDropdown().subscribe({
      next: res => this.productions = res?.data ?? [],
      error: () => this.productions = []
    });
  }

  // -------------------------
  // list + analytics
  // -------------------------
  loadData(): void {
    this.loading = true;

    this.svc.getPaged(this.productionFilter, this.searchText, this.page, this.pageSize).subscribe({
      next: res => {
        const data = res.data;
        const arr = data?.items ?? [];
        this.items = Array.isArray(arr) ? arr : [];
        this.totalItems = data?.totalItems ?? data?.total ?? this.items.length;

        // refresh analytics on each list load
        this.loadAnalytics();
      },
      error: () => {
        this.items = [];
        this.totalItems = 0;
        this.totalDowntimeMinutes = 0;
        this.reasonStats = [];
      },
      complete: () => this.loading = false
    });
  }

  loadAnalytics(): void {
    this.svc.analytics(this.productionFilter).subscribe({
      next: res => {
        const d = res.data;
        this.totalDowntimeMinutes = d?.totalMinutes ?? 0;
        this.reasonStats = d?.reasons ?? [];
      },
      error: () => {
        this.totalDowntimeMinutes = 0;
        this.reasonStats = [];
      }
    });
  }

  onSearch(): void {
    this.page = 1;
    this.loadData();
  }

  onPageChange(p: number): void {
    if (p < 1) return;
    const max = Math.ceil(this.totalItems / this.pageSize) || 1;
    if (p > max) return;
    this.page = p;
    this.loadData();
  }

  closeDatePicker(ev: any) {
    ev?.target?.blur();
  }

  // -------------------------
  // modal helpers (NO DARK SCREEN)
  // -------------------------
  openModal(modalRef: ElementRef) {
    this.closeModal(); // always cleanup first

    this.currentModal = new bootstrap.Modal(modalRef.nativeElement, {
      backdrop: true,
      keyboard: true
    });

    // cleanup when hidden (critical)
    modalRef.nativeElement.addEventListener('hidden.bs.modal', () => this.forceRemoveBackdrops(), { once: true });

    this.currentModal.show();
  }

  closeModal() {
    try {
      if (this.currentModal) {
        this.currentModal.hide();
        this.currentModal = null;
      }
    } finally {
      this.forceRemoveBackdrops();
    }
  }

  forceRemoveBackdrops() {
    document.body.classList.remove('modal-open');
    document.body.style.removeProperty('padding-right');
    document.querySelectorAll('.modal-backdrop').forEach(b => b.remove());
  }

  // -------------------------
  // create/edit
  // -------------------------
  openCreate(): void {
    this.modalMode = 'create';
    this.selectedId = null;
    this.formModel = this.createEmptyForm();
    this.openModal(this.stopModal);
  }

  openEdit(row: DailyProductionStopRowViewModel): void {
    this.modalMode = 'edit';
    this.selectedId = row.Id;

    this.svc.getDetail(row.Id).subscribe({
      next: res => {
        const d = res.data;
        this.formModel = {
          ProductionId: d?.ProductionId ?? null,
          StopStart: d?.StopStart ?? null,
          StopEnd: d?.StopEnd ?? null,
          Reason: d?.Reason ?? ''
        };
        this.openModal(this.stopModal);
      },
      error: () => alert('Failed to load detail')
    });
  }

  save(): void {
    if (!this.formModel.ProductionId || !this.formModel.StopStart || !this.formModel.Reason) {
      alert('Production, StopStart and Reason are required.');
      return;
    }

    const payload: DailyProductionStopInputDto = {
      ProductionId: this.formModel.ProductionId,
      StopStart: this.formModel.StopStart,
      StopEnd: this.formModel.StopEnd || null,
      Reason: this.formModel.Reason
    };

    if (this.modalMode === 'create') {
      this.svc.create(payload).subscribe({
        next: res => {
          alert(res.message || 'Created');
          if (res.success) {
            this.closeModal();
            this.loadData();
          }
        },
        error: () => alert('Create failed')
      });
    } else {
      this.svc.update(this.selectedId!, payload).subscribe({
        next: res => {
          alert(res.message || 'Updated');
          if (res.success) {
            this.closeModal();
            this.loadData();
          }
        },
        error: () => alert('Update failed')
      });
    }
  }

  // -------------------------
  // detail
  // -------------------------
  openDetail(row: DailyProductionStopRowViewModel): void {
    this.svc.getDetail(row.Id).subscribe({
      next: res => {
        if (res.success) {
          this.detailModel = res.data;
          this.openModal(this.detailModal);
        }
      },
      error: () => alert('Failed to load details')
    });
  }

  // -------------------------
  // delete
  // -------------------------
  confirmDelete(row: DailyProductionStopRowViewModel): void {
    this.deleteModel = row;
    this.openModal(this.deleteModal);
  }

  deleteConfirmed(): void {
    if (!this.deleteModel) return;

    this.svc.delete(this.deleteModel.Id).subscribe({
      next: res => {
        alert(res.message || 'Deleted');
        if (res.success) {
          this.closeModal();
          this.loadData();
        }
      },
      error: () => alert('Delete failed')
    });
  }

  exportExcel(): void {
    this.stopSvc.exportExcel(this.productionFilter, this.searchText).subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'DailyProductionStops.xlsx';
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  exportPdf(): void {
    this.stopSvc.exportPdf().subscribe(blob => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'DailyProductionStops.pdf';
      a.click();
      URL.revokeObjectURL(url);
    });
  }

  openPrint(): void {
    window.open(this.stopSvc.printView(), '_blank');
  }

}
