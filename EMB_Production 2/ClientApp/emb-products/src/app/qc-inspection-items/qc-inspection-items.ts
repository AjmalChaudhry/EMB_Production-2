import { Component, ElementRef, OnInit, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';

import { QCInspectionItemsService } from '../services/qc-inspection-items.service';

import {
  QCInspectionItemRowViewModel,
  QCInspectionItemDetailViewModel,
  QCInspectionItemForm
} from '../shared/models/qc-inspection-item.dto';

interface DropdownOption {
  id: number;
  name: string;
}

@Component({
  selector: 'app-qc-inspection-items',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './qc-inspection-items.html',
  styleUrls: ['./qc-inspection-items.css']
})
export class QcInspectionItemsComponent implements OnInit, OnDestroy {
  private queryParamsSub?: Subscription;

  ngOnDestroy(): void {
    this.queryParamsSub?.unsubscribe();
  }


  items: QCInspectionItemRowViewModel[] = [];
  loading = false;

  searchText = '';
  activeFilter = '';
  showDeleted = false;

  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 1;

  modalMode: 'create' | 'edit' = 'create';
  selectedId: number | null = null;

  detailModel: QCInspectionItemDetailViewModel | null = null;
  deleteModel: QCInspectionItemRowViewModel | null = null;

  formModel: QCInspectionItemForm = this.createEmptyForm();
  currentModal: any = null;

  qcInspectionOptions: DropdownOption[] = [];
  workOrderItemOptions: DropdownOption[] = [];
  productionOptions: DropdownOption[] = [];
  componentOptions: DropdownOption[] = [];

  @ViewChild('formModal') formModal!: ElementRef;
  @ViewChild('detailModal') detailModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  constructor(
    private svc: QCInspectionItemsService,
    private route: ActivatedRoute,
    private router: Router
  ) { }

  ngOnInit(): void {
    this.loadQcInspectionOptions();

    this.queryParamsSub = this.route.queryParams.subscribe(params => {
      const qcInspectionId = Number(params['qcInspectionId'] ?? 0);

      if (qcInspectionId > 0) {
        this.formModel.qcInspectionId = qcInspectionId;
        this.loadDataByQcInspection(qcInspectionId);
      } else {
        this.loadData();
      }
    });
  }

  createEmptyForm(): QCInspectionItemForm {
    return {
      id: 0,
      qcInspectionId: null,
      workOrderItemId: null,
      productionId: null,
      compId: null,
      qty: null,
      passedQty: 0,
      failedQty: 0,
      reworkQty: 0,
      result: null,
      remarks: null,
      active: true
    };
  }

  resetDependentOptions(): void {
    this.workOrderItemOptions = [];
    this.productionOptions = [];
    this.componentOptions = [];
  }

  get decisionTotal(): number {
    return Number(this.formModel.passedQty || 0)
      + Number(this.formModel.failedQty || 0)
      + Number(this.formModel.reworkQty || 0);
  }

  get validationMessage(): string {
    if (!this.formModel.qcInspectionId || this.formModel.qcInspectionId <= 0) {
      return 'QC Inspection is required.';
    }

    if (!this.formModel.workOrderItemId || this.formModel.workOrderItemId <= 0) {
      return 'Work Order Item is required.';
    }

    if (!this.formModel.compId || this.formModel.compId <= 0) {
      return 'Component is required.';
    }

    if (!this.formModel.qty || this.formModel.qty <= 0) {
      return 'Qty must be greater than zero.';
    }

    if ((this.formModel.passedQty ?? 0) < 0 ||
      (this.formModel.failedQty ?? 0) < 0 ||
      (this.formModel.reworkQty ?? 0) < 0) {
      return 'Passed, Failed and Rework quantities cannot be negative.';
    }

    if (this.decisionTotal !== Number(this.formModel.qty)) {
      return 'Qty must equal Passed + Failed + Rework.';
    }

    return '';
  }

  canSave(): boolean {
    return this.validationMessage === '';
  }

  autoResult(): void {
    const qty = Number(this.formModel.qty || 0);
    const pass = Number(this.formModel.passedQty || 0);
    const fail = Number(this.formModel.failedQty || 0);
    const rework = Number(this.formModel.reworkQty || 0);

    if (qty <= 0) {
      this.formModel.result = null;
      return;
    }

    if (pass === qty) {
      this.formModel.result = 'Pass';
    } else if (fail === qty) {
      this.formModel.result = 'Fail';
    } else if (rework === qty) {
      this.formModel.result = 'Rework';
    } else {
      this.formModel.result = 'Partial';
    }
  }

  normalizeForm(): QCInspectionItemForm {
    return {
      ...this.formModel,
      qcInspectionId: this.toNullableNumber(this.formModel.qcInspectionId),
      workOrderItemId: this.toNullableNumber(this.formModel.workOrderItemId),
      productionId: this.toNullableNumber(this.formModel.productionId),
      compId: this.toNullableNumber(this.formModel.compId),
      qty: this.toNullableNumber(this.formModel.qty),
      passedQty: this.toNullableNumber(this.formModel.passedQty) ?? 0,
      failedQty: this.toNullableNumber(this.formModel.failedQty) ?? 0,
      reworkQty: this.toNullableNumber(this.formModel.reworkQty) ?? 0,
      remarks: this.formModel.remarks?.trim() || null,
      result: this.formModel.result?.trim() || null,
      active: this.formModel.active === true
    };
  }

  loadData(): void {
    this.loading = true;

    this.svc.list({
      search: this.searchText,
      activeFilter: this.activeFilter,
      showDeleted: this.showDeleted,
      page: this.page,
      pageSize: this.pageSize
    }).subscribe({
      next: res => {
        this.items = res.success ? (res.data?.items ?? []) : [];
        this.page = res.data?.page ?? this.page;
        this.pageSize = res.data?.pageSize ?? this.pageSize;
        this.totalItems = res.data?.totalItems ?? 0;
        this.totalPages = res.data?.totalPages ?? 1;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.items = [];
        alert('Failed to load data');
      }
    });
  }

  loadDataByQcInspection(qcInspectionId: number): void {
    this.loading = true;

    this.svc.list({
      search: '',
      activeFilter: this.activeFilter,
      showDeleted: this.showDeleted,
      page: this.page,
      pageSize: this.pageSize,
      qcInspectionId
    }).subscribe({
      next: res => {
        this.items = res.success ? (res.data?.items ?? []) : [];
        this.page = res.data?.page ?? this.page;
        this.pageSize = res.data?.pageSize ?? this.pageSize;
        this.totalItems = res.data?.totalItems ?? 0;
        this.totalPages = res.data?.totalPages ?? 1;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.items = [];
        alert('Failed to load QC inspection items');
      }
    });
  }

  loadQcInspectionOptions(): void {
    if (!this.svc.getQcInspectionDropdown) {
      return;
    }

    this.svc.getQcInspectionDropdown().subscribe({
      next: (res: any) => {
        const data = res?.data ?? res?.Data ?? res ?? [];
        this.qcInspectionOptions = Array.isArray(data) ? data : [];
      },
      error: () => {
        this.qcInspectionOptions = [];
      }
    });
  }

  onSearchChange(): void {
    this.page = 1;

    const qcInspectionId = Number(this.route.snapshot.queryParams['qcInspectionId'] ?? 0);
    if (qcInspectionId > 0) {
      this.loadDataByQcInspection(qcInspectionId);
    } else {
      this.loadData();
    }
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages) return;

    this.page = p;

    const qcInspectionId = Number(this.route.snapshot.queryParams['qcInspectionId'] ?? 0);
    if (qcInspectionId > 0) {
      this.loadDataByQcInspection(qcInspectionId);
    } else {
      this.loadData();
    }
  }

  onQcInspectionChange(): void {
    const qcId = Number(this.formModel.qcInspectionId || 0);

    this.formModel.workOrderItemId = null;
    this.formModel.productionId = null;
    this.formModel.compId = null;
    this.resetDependentOptions();

    if (!qcId) {
      return;
    }

    this.svc.getAutoFillByQc(qcId).subscribe({
      next: (res: any) => {
        if (!res?.success || !res?.data) {
          return;
        }

        const data = res.data;
        this.workOrderItemOptions = Array.isArray(data.workOrderItems) ? data.workOrderItems : [];
        this.productionOptions = Array.isArray(data.productions) ? data.productions : [];
        this.componentOptions = Array.isArray(data.components) ? data.components : [];

        if (this.workOrderItemOptions.length === 1) {
          this.formModel.workOrderItemId = this.workOrderItemOptions[0].id;
        }

        if (this.productionOptions.length === 1) {
          this.formModel.productionId = this.productionOptions[0].id;
        }

        if (this.componentOptions.length === 1) {
          this.formModel.compId = this.componentOptions[0].id;
        }
      },
      error: () => {
        this.resetDependentOptions();
      }
    });
  }

  onWorkOrderItemChange(): void {
    const selectedWorkOrderItemId = Number(this.formModel.workOrderItemId || 0);
    if (!selectedWorkOrderItemId) {
      return;
    }

    const matchedComponent = this.componentOptions.find(x => x.id === this.formModel.compId);
    if (!matchedComponent && this.componentOptions.length === 1) {
      this.formModel.compId = this.componentOptions[0].id;
    }
  }

  onProductionChange(): void {
    const selectedProductionId = Number(this.formModel.productionId || 0);
    if (!selectedProductionId) {
      return;
    }
  }

  onComponentChange(): void {
    const selectedCompId = Number(this.formModel.compId || 0);
    if (!selectedCompId) {
      return;
    }
  }

  openModal(ref: ElementRef): void {
    this.closeModal();

    const bootstrapAny = (window as any).bootstrap;
    if (!bootstrapAny?.Modal) {
      console.error('Bootstrap Modal is not available.');
      return;
    }

    this.currentModal = new bootstrapAny.Modal(ref.nativeElement);
    this.currentModal.show();
  }

  closeModal(): void {
    if (this.currentModal) {
      this.currentModal.hide();
      this.currentModal = null;
    }
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.selectedId = null;
    this.formModel = this.createEmptyForm();
    this.resetDependentOptions();

    const qcInspectionId = Number(this.route.snapshot.queryParams['qcInspectionId'] ?? 0);
    if (qcInspectionId > 0) {
      this.formModel.qcInspectionId = qcInspectionId;
      this.onQcInspectionChange();
    }

    this.openModal(this.formModal);
  }

  openEdit(row: QCInspectionItemRowViewModel): void {
    this.modalMode = 'edit';
    this.selectedId = row.id;
    this.resetDependentOptions();

    this.svc.detail(row.id).subscribe({
      next: res => {
        if (!res.success || !res.data) {
          alert(res.message || 'Failed to load detail');
          return;
        }

        const d = res.data;

        this.formModel = {
          id: d.id,
          qcInspectionId: d.qcInspectionId,
          workOrderItemId: d.workOrderItemId,
          productionId: d.productionId,
          compId: d.compId,
          qty: d.qty,
          passedQty: d.passedQty,
          failedQty: d.failedQty,
          reworkQty: d.reworkQty,
          result: d.result,
          remarks: d.remarks,
          active: d.active
        };

        if (this.formModel.qcInspectionId) {
          this.svc.getAutoFillByQc(this.formModel.qcInspectionId).subscribe({
            next: (autoRes: any) => {
              if (autoRes?.success && autoRes?.data) {
                this.workOrderItemOptions = Array.isArray(autoRes.data.workOrderItems) ? autoRes.data.workOrderItems : [];
                this.productionOptions = Array.isArray(autoRes.data.productions) ? autoRes.data.productions : [];
                this.componentOptions = Array.isArray(autoRes.data.components) ? autoRes.data.components : [];
              }

              this.openModal(this.formModal);
            },
            error: () => {
              this.openModal(this.formModal);
            }
          });
        } else {
          this.openModal(this.formModal);
        }
      },
      error: () => {
        alert('Failed to load detail');
      }
    });
  }

  openDetail(row: QCInspectionItemRowViewModel): void {
    this.svc.detail(row.id).subscribe({
      next: res => {
        if (!res.success || !res.data) {
          alert(res.message || 'Failed to load detail.');
          return;
        }

        this.detailModel = res.data;
        this.openModal(this.detailModal);
      },
      error: () => {
        alert('Failed to load detail.');
      }
    });
  }

  confirmDelete(row: QCInspectionItemRowViewModel): void {
    this.deleteModel = row;
    this.openModal(this.deleteModal);
  }

  deleteConfirmed(): void {
    if (!this.deleteModel) return;

    this.svc.delete(this.deleteModel.id).subscribe({
      next: res => {
        if (res.success) {
          this.closeModal();
          this.loadData();
        } else {
          alert(res.message || 'Delete failed.');
        }
      },
      error: () => {
        alert('Delete failed.');
      }
    });
  }

  save(): void {
    if (!this.canSave()) {
      alert(this.validationMessage || 'Please correct the form.');
      return;
    }

    const payload = this.normalizeForm();

    if (this.modalMode === 'create') {
      this.svc.create(payload).subscribe({
        next: res => {
          if (res.success) {
            this.closeModal();
            this.loadData();
          } else {
            alert(res.message || 'Save failed.');
          }
        },
        error: () => {
          alert('Save failed.');
        }
      });
      return;
    }

    if (!this.selectedId) {
      alert('Invalid item id.');
      return;
    }

    this.svc.update(this.selectedId, payload).subscribe({
      next: res => {
        if (res.success) {
          this.closeModal();
          this.loadData();
        } else {
          alert(res.message || 'Save failed.');
        }
      },
      error: () => {
        alert('Save failed.');
      }
    });
  }

  toggleActive(row: QCInspectionItemRowViewModel): void {
    this.svc.toggleActive(row.id).subscribe({
      next: res => {
        if (res.success) {
          this.loadData();
        } else {
          alert(res.message || 'Status update failed.');
        }
      },
      error: () => {
        alert('Status update failed.');
      }
    });
  }

  getResultClass(result: string | null | undefined): string {
    const r = (result || '').toLowerCase();

    if (r === 'pass') return 'bg-success';
    if (r === 'fail') return 'bg-danger';
    if (r === 'rework') return 'bg-warning text-dark';
    if (r === 'partial') return 'bg-primary';

    return 'bg-secondary';
  }

  private toNullableNumber(value: any): number | null {
    if (value === null || value === undefined || value === '') {
      return null;
    }

    const n = Number(value);
    return Number.isNaN(n) ? null : n;
  }

  goBackToQc(): void {
    const qcInspectionId = this.route.snapshot.queryParams['qcInspectionId'];

    if (qcInspectionId) {
      this.router.navigate(['/qc-inspection'], {
        queryParams: { highlight: qcInspectionId }
      });
    } else {
      this.router.navigate(['/qc-inspection']);
    }
  }
}
