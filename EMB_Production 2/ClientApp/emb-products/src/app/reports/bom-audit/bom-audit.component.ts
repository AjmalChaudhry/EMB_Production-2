import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { BomAuditService } from '../../services/bom-audit.service';
import {
  BomAuditKpis, BomAuditDesignRow, BomAuditDetailRow, BomDriftKind,
  BomAuditDiagnostics
} from '../../shared/models/bom-audit.dto';

@Component({
  selector: 'app-bom-audit',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './bom-audit.component.html',
  styleUrls: ['./bom-audit.component.css']
})
export class BomAuditComponent implements OnInit {
  // ---- Filters ----
  fromDate: string | null = null;
  toDate:   string | null = null;
  // Tolerance is a 0..100 percent in the UI; we send 0..1 to the API.
  tolerancePctUi = 20;

  // ---- Summary table ----
  loadingSummary = false;
  kpis: BomAuditKpis = {
    designsAudited: 0, totalRows: 0, okRows: 0,
    wrongItem: 0, wrongQty: 0, missing: 0, driftRate: 0
  };
  designs: BomAuditDesignRow[] = [];
  diagnostics: BomAuditDiagnostics | null = null;

  // ---- Drill-down ----
  expandedDesignId: number | null = null;
  loadingDetail = false;
  detailRows: BomAuditDetailRow[] = [];
  showOnlyDrift = true;

  constructor(private svc: BomAuditService) {}

  ngOnInit(): void {
    this.loadSummary();
  }

  loadSummary(): void {
    this.loadingSummary = true;
    this.svc.summary({
      fromDate: this.fromDate,
      toDate: this.toDate,
      tolerancePct: this.tolerancePctUi / 100
    }).subscribe({
      next: r => {
        this.loadingSummary = false;
        if (r?.success && r.data) {
          this.kpis        = r.data.kpis;
          this.designs     = r.data.items || [];
          this.diagnostics = r.data.diagnostics || null;
        }
      },
      error: () => { this.loadingSummary = false; }
    });
  }

  resetFilters(): void {
    this.fromDate = null;
    this.toDate = null;
    this.tolerancePctUi = 20;
    this.loadSummary();
  }

  toggleDesign(d: BomAuditDesignRow): void {
    if (this.expandedDesignId === d.designId) {
      this.expandedDesignId = null;
      this.detailRows = [];
      return;
    }
    this.expandedDesignId = d.designId;
    this.detailRows = [];
    this.loadingDetail = true;
    this.svc.detail(d.designId, {
      fromDate: this.fromDate,
      toDate: this.toDate,
      tolerancePct: this.tolerancePctUi / 100
    }).subscribe({
      next: r => {
        this.loadingDetail = false;
        this.detailRows = r?.data?.items || [];
      },
      error: () => { this.loadingDetail = false; }
    });
  }

  visibleDetailRows(): BomAuditDetailRow[] {
    return this.showOnlyDrift
      ? this.detailRows.filter(r => r.kind !== 'OK')
      : this.detailRows;
  }

  driftTotal(d: BomAuditDesignRow): number {
    return d.wrongItemCount + d.wrongQtyCount + d.missingCount;
  }

  // Class helpers
  driftBadgeClass(k: BomDriftKind): string {
    switch (k) {
      case 'WRONG_ITEM':       return 'bg-danger text-white';
      case 'WRONG_QTY':        return 'bg-warning text-dark';
      case 'MISSING_FROM_BOM': return 'bg-info text-white';
      case 'OK':
      default:                 return 'bg-success-subtle text-success-emphasis border border-success-subtle';
    }
  }
  driftLabel(k: BomDriftKind): string {
    switch (k) {
      case 'WRONG_ITEM':       return 'Wrong item';
      case 'WRONG_QTY':        return 'Wrong qty';
      case 'MISSING_FROM_BOM': return 'Missing from BOM';
      case 'OK':
      default:                 return 'OK';
    }
  }
}
