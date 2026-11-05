import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProductionLedgerService } from '../services/production-ledger.service';
import { ProductionQcLedgerRowDto } from '../shared/models/production-ledger.dto';

interface RecentProductionVm {
  productionId: number;
  postedDate: string;
  acceptedQty: number;
  rejectedQty: number;
  workOrderCode: string;
  productName: string;
  designCode: string;
}

@Component({
  standalone: true,
  selector: 'app-production-ledger',
  imports: [CommonModule, FormsModule],
  templateUrl: './production-ledger.html'
})
export class ProductionLedgerComponent implements OnInit {

  // When @Input() productionId is provided (embedded use), the component
  // loads that production's ledger directly and hides the picker.
  // When omitted (standalone /production-ledger route), the component
  // fetches a list of recent posted productions and shows a picker.
  @Input() productionId: number | null = null;

  rows: ProductionQcLedgerRowDto[] = [];
  loading = false;

  // Picker state (only used when @Input productionId is not provided)
  recent: RecentProductionVm[] = [];
  recentLoading = false;
  selectedProductionId: number | null = null;

  get isStandaloneMode(): boolean {
    return this.productionId === null || this.productionId === undefined;
  }

  constructor(private svc: ProductionLedgerService) { }

  ngOnInit(): void {
    if (this.isStandaloneMode) {
      this.loadRecent();
    } else {
      this.load();
    }
  }

  loadRecent(): void {
    this.recentLoading = true;
    this.svc.recent(50).subscribe({
      next: res => {
        this.recent = res?.data ?? [];
      },
      error: err => {
        console.error('Failed to load recent productions', err);
        this.recent = [];
      },
      complete: () => { this.recentLoading = false; }
    });
  }

  onPickProduction(): void {
    this.rows = [];
    if (!this.selectedProductionId || this.selectedProductionId <= 0) return;
    this.loadFor(this.selectedProductionId);
  }

  load(): void {
    const id = this.isStandaloneMode ? this.selectedProductionId : this.productionId;
    if (!id || id <= 0) {
      if (this.isStandaloneMode) {
        // Standalone: prompt the user to pick instead of alerting
        return;
      }
      alert('Invalid production id.');
      return;
    }
    this.loadFor(id);
  }

  private loadFor(id: number): void {
    this.loading = true;
    this.svc.byProduction(id).subscribe({
      next: res => {
        const data = res?.data ?? {};

        const rawRows =
          data.items ??
          data.Items ??
          data.rows ??
          data.Rows ??
          [];

        // Backend's default JSON serializer is camelCase, so prefer
        // camelCase fields and fall back to PascalCase for any older
        // callers still emitting that shape.
        this.rows = rawRows.map((x: any) => ({
          date:
            x.date ??
            x.Date ??
            x.createdDateTimeText ??
            x.CreatedDateTimeText ??
            x.createdDate ??
            x.CreatedDate ??
            null,

          itemType:
            x.itemType ??
            x.ItemType ??
            x.type ??
            x.Type ??
            '',

          inQty:
            x.inQty ??
            x.InQty ??
            x.qtyIn ??
            x.QtyIn ??
            0,

          outQty:
            x.outQty ??
            x.OutQty ??
            x.qtyOut ??
            x.QtyOut ??
            0,

          description:
            x.description ??
            x.Description ??
            x.remarks ??
            x.Remarks ??
            ''
        }));
      },
      error: err => {
        console.error('Production ledger load failed', err);
        this.rows = [];
      },
      complete: () => {
        this.loading = false;
      }
    });
  }
}
