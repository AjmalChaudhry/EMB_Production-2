import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportsService } from '../services/reports.service';
import { SalesReport, DATE_PRESETS } from '../shared/models/reports.dto';

@Component({
  selector: 'app-sales-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-wrap">
      <div class="d-flex justify-content-between align-items-center mb-3">
        <div>
          <h5 class="mb-0 d-flex align-items-center">
            <i class="bi bi-truck me-2 text-success"></i>Sales & Dispatch
          </h5>
          <div class="text-muted small">
            Period: <strong>{{ fromText }}</strong> → <strong>{{ toText }}</strong>
          </div>
        </div>
        <div class="d-flex gap-2">
          <button class="btn btn-sm btn-outline-primary" (click)="load()" [disabled]="loading" title="Refresh">
            <i class="bi bi-arrow-clockwise me-1"></i>{{ loading ? 'Loading…' : 'Refresh' }}
          </button>
          <button class="btn btn-sm btn-outline-secondary" (click)="print()" [disabled]="!data" title="Print">
            <i class="bi bi-printer me-1"></i>Print
          </button>
          <button class="btn btn-sm btn-outline-success" (click)="excel()">
            <i class="bi bi-file-earmark-excel me-1"></i>Excel
          </button>
          <button class="btn btn-sm btn-outline-danger" (click)="pdf()">
            <i class="bi bi-file-earmark-pdf me-1"></i>PDF
          </button>
        </div>
      </div>

      <div class="card mb-3">
        <div class="card-body py-2">
          <div class="row g-2">
            <div class="col-md-3">
              <label class="form-label small mb-0">Range</label>
              <select class="form-select form-select-sm" [(ngModel)]="preset" (change)="applyPreset(); load()">
                <option *ngFor="let p of presets" [value]="p.value">{{ p.label }}</option>
              </select>
            </div>
            <div class="col-md-2" *ngIf="preset === 'CUSTOM'">
              <label class="form-label small mb-0">From</label>
              <input type="date" class="form-control form-control-sm" [(ngModel)]="from">
            </div>
            <div class="col-md-2" *ngIf="preset === 'CUSTOM'">
              <label class="form-label small mb-0">To</label>
              <input type="date" class="form-control form-control-sm" [(ngModel)]="to">
            </div>
            <div class="col-md-2 d-flex align-items-end" *ngIf="preset === 'CUSTOM'">
              <button class="btn btn-sm btn-primary" (click)="load()" [disabled]="loading">
                <i class="bi bi-search me-1"></i>{{ loading ? 'Loading…' : 'Apply' }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <div *ngIf="data" class="row g-2 mb-3">
        <div class="col-md kpi-tile primary"><div class="lbl">Dispatches</div><div class="val">{{ data.summary.dispatchCount }}</div></div>
        <div class="col-md kpi-tile info"><div class="lbl">Total Qty</div><div class="val">{{ data.summary.totalQty | number }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Total Value</div><div class="val">{{ data.summary.totalValue | number:'1.0-2' }}</div></div>
        <div class="col-md kpi-tile warning"><div class="lbl">Cost</div><div class="val">{{ data.summary.totalCost | number:'1.0-2' }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Gross Profit</div><div class="val">{{ data.summary.grossProfit | number:'1.0-2' }}</div></div>
        <div class="col-md kpi-tile info"><div class="lbl">Margin %</div><div class="val">{{ data.summary.marginPercent }}%</div></div>
      </div>

      <div class="row g-3" *ngIf="data">
        <div class="col-md-6">
          <div class="card mb-3">
            <div class="card-header py-2"><strong><i class="bi bi-person me-1"></i>By Customer</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Customer</th><th class="text-end">Dispatches</th><th class="text-end">Qty</th><th class="text-end">Value</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byCustomer"><td>{{ r.customerName }}</td><td class="text-end">{{ r.dispatchCount }}</td><td class="text-end">{{ r.qty | number }}</td><td class="text-end">{{ r.value | number:'1.0-2' }}</td></tr>
                  <tr *ngIf="!data.byCustomer.length"><td colspan="4" class="text-center text-muted py-3">No dispatches in this period.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-calendar3 me-1"></i>Daily</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Date</th><th class="text-end">Dispatches</th><th class="text-end">Qty</th><th class="text-end">Value</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byDay"><td>{{ r.date }}</td><td class="text-end">{{ r.dispatches }}</td><td class="text-end">{{ r.qty | number }}</td><td class="text-end">{{ r.value | number:'1.0-2' }}</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
        <div class="col-md-6">
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-box me-1"></i>By Product</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Product</th><th class="text-end">Qty</th><th class="text-end">Value</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byProduct"><td>{{ r.productName }}</td><td class="text-end">{{ r.qty | number }}</td><td class="text-end">{{ r.value | number:'1.0-2' }}</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-wrap { padding: 10px 6px; }
    .kpi-tile { background: #fff; border: 1px solid #e9ecef; border-radius: 10px; padding: 12px 14px; border-left-width: 4px; }
    .kpi-tile.primary { border-left-color: #0d6efd; }
    .kpi-tile.success { border-left-color: #198754; }
    .kpi-tile.info    { border-left-color: #0dcaf0; }
    .kpi-tile.warning { border-left-color: #ffc107; }
    .kpi-tile .lbl { font-size: 0.7rem; text-transform: uppercase; color: #6c757d; letter-spacing: 0.04em; font-weight: 600; }
    .kpi-tile .val { font-size: 1.15rem; font-weight: 700; color: #212529; }
  `]
})
export class SalesReportComponent implements OnInit {
  presets = DATE_PRESETS;
  preset = 'THIS_MONTH';
  from = ''; to = ''; fromText = ''; toText = '';
  data: SalesReport | null = null;
  loading = false;

  constructor(private svc: ReportsService) {}

  ngOnInit(): void { this.applyPreset(); this.load(); }

  applyPreset(): void {
    const today = new Date();
    const ymd = (d: Date) => d.toISOString().substring(0, 10);
    let f = new Date(), t = new Date();
    switch (this.preset) {
      case 'TODAY': f = t = today; break;
      case 'YESTERDAY': f.setDate(today.getDate() - 1); t = new Date(f); break;
      case 'THIS_WEEK': f.setDate(today.getDate() - ((today.getDay() + 6) % 7)); t = new Date(); break;
      case 'THIS_MONTH': f = new Date(today.getFullYear(), today.getMonth(), 1); t = new Date(); break;
      case 'LAST_30': f.setDate(today.getDate() - 30); t = new Date(); break;
      case 'CUSTOM': return;
    }
    this.from = ymd(f); this.to = ymd(t);
  }

  load(): void {
    this.loading = true;
    this.fromText = this.from || '-'; this.toText = this.to || '-';
    this.svc.sales({ from: this.from, to: this.to }).subscribe({
      next: r => { this.loading = false; if (r?.success) this.data = r.data; },
      error: () => { this.loading = false; }
    });
  }

  private params() { return { from: this.from, to: this.to }; }
  excel(): void { window.open(this.svc.salesExcelUrl(this.params()), '_blank'); }
  pdf():   void { window.open(this.svc.salesPdfUrl(this.params()),   '_blank'); }

  print(): void {
    if (!this.data) return;
    const d = this.data;
    const esc = (s: any) => this.svc.escape(s);
    const num = (n: any, decimals = 0) => (n ?? 0).toLocaleString(undefined,
      decimals > 0 ? { minimumFractionDigits: decimals, maximumFractionDigits: decimals } : undefined);

    const filtersHtml = `
      <div class="filters">
        <span>Range: <b>${esc(this.fromText)} → ${esc(this.toText)}</b></span>
      </div>`;

    const kpisHtml = `
      <div class="kpis">
        <div class="kpi"><div class="lbl">Dispatches</div><div class="val">${num(d.summary.dispatchCount)}</div></div>
        <div class="kpi"><div class="lbl">Total Qty</div><div class="val">${num(d.summary.totalQty)}</div></div>
        <div class="kpi"><div class="lbl">Total Value</div><div class="val">${num(d.summary.totalValue, 2)}</div></div>
        <div class="kpi"><div class="lbl">Total Cost</div><div class="val">${num(d.summary.totalCost, 2)}</div></div>
        <div class="kpi"><div class="lbl">Gross Profit</div><div class="val">${num(d.summary.grossProfit, 2)}</div></div>
        <div class="kpi"><div class="lbl">Margin %</div><div class="val">${d.summary.marginPercent}%</div></div>
      </div>`;

    const custRows = d.byCustomer.map(r =>
      `<tr><td>${esc(r.customerName)}</td><td class="num">${num(r.dispatchCount)}</td><td class="num">${num(r.qty)}</td><td class="num">${num(r.value, 2)}</td></tr>`).join('');
    const custTable = `
      <h2>By Customer</h2>
      <table><thead><tr><th>Customer</th><th class="num">Dispatches</th><th class="num">Qty</th><th class="num">Value</th></tr></thead>
      <tbody>${custRows || '<tr><td colspan="4">No data</td></tr>'}</tbody></table>`;

    const prodRows = d.byProduct.map(r =>
      `<tr><td>${esc(r.productName)}</td><td class="num">${num(r.qty)}</td><td class="num">${num(r.value, 2)}</td></tr>`).join('');
    const prodTable = `
      <h2>By Product</h2>
      <table><thead><tr><th>Product</th><th class="num">Qty</th><th class="num">Value</th></tr></thead>
      <tbody>${prodRows || '<tr><td colspan="3">No data</td></tr>'}</tbody></table>`;

    const dayRows = d.byDay.map(r =>
      `<tr><td>${esc(r.date)}</td><td class="num">${num(r.dispatches)}</td><td class="num">${num(r.qty)}</td><td class="num">${num(r.value, 2)}</td></tr>`).join('');
    const dayTable = `
      <h2>By Day</h2>
      <table><thead><tr><th>Date</th><th class="num">Dispatches</th><th class="num">Qty</th><th class="num">Value</th></tr></thead>
      <tbody>${dayRows || '<tr><td colspan="4">No data</td></tr>'}</tbody></table>`;

    this.svc.openPrintWindow('Sales & Dispatch Report',
      filtersHtml + kpisHtml + custTable + prodTable + dayTable);
  }
}
