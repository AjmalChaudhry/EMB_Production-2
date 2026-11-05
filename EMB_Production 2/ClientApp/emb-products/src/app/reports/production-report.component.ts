import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportsService } from '../services/reports.service';
import { ProductionReport, DATE_PRESETS } from '../shared/models/reports.dto';

@Component({
  selector: 'app-production-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-wrap">
      <div class="d-flex justify-content-between align-items-center mb-3">
        <div>
          <h5 class="mb-0 d-flex align-items-center">
            <i class="bi bi-gear-fill me-2 text-primary"></i>Production Summary
          </h5>
          <div class="text-muted small">
            Daily output, machine breakdown, QC pass rate. Period: <strong>{{ fromText }}</strong> → <strong>{{ toText }}</strong>
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
        <div class="col-md kpi-tile primary"><div class="lbl">Days</div><div class="val">{{ data.summary.daysCovered }}</div></div>
        <div class="col-md kpi-tile info"><div class="lbl">Produced</div><div class="val">{{ data.summary.totalProduced | number }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Accepted</div><div class="val">{{ data.summary.totalAccepted | number }}</div></div>
        <div class="col-md kpi-tile warning"><div class="lbl">Rejected</div><div class="val">{{ data.summary.totalRejected | number }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">QC Pass</div><div class="val">{{ data.summary.qcPassRate }}%</div></div>
      </div>

      <div class="row g-3" *ngIf="data">
        <div class="col-md-6">
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-calendar3 me-1"></i>Daily Output</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Date</th><th class="text-end">Produced</th><th class="text-end">Accepted</th><th class="text-end">Rejected</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byDay"><td>{{ r.date }}</td><td class="text-end">{{ r.produced | number }}</td><td class="text-end">{{ r.accepted | number }}</td><td class="text-end">{{ r.rejected | number }}</td></tr>
                  <tr *ngIf="!data.byDay.length"><td colspan="4" class="text-center text-muted py-3">No daily production in this period.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
        <div class="col-md-6">
          <div class="card mb-3">
            <div class="card-header py-2"><strong><i class="bi bi-cpu me-1"></i>By Machine</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Machine</th><th class="text-end">Produced</th><th class="text-end">Accepted</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byMachine"><td>{{ r.machineName }}</td><td class="text-end">{{ r.produced | number }}</td><td class="text-end">{{ r.accepted | number }}</td></tr>
                  <tr *ngIf="!data.byMachine.length"><td colspan="3" class="text-center text-muted py-3">-</td></tr>
                </tbody>
              </table>
            </div>
          </div>
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-box me-1"></i>By Product</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light"><tr><th>Product</th><th class="text-end">Produced</th><th class="text-end">Accepted</th></tr></thead>
                <tbody>
                  <tr *ngFor="let r of data.byProduct"><td>{{ r.productName }}</td><td class="text-end">{{ r.produced | number }}</td><td class="text-end">{{ r.accepted | number }}</td></tr>
                  <tr *ngIf="!data.byProduct.length"><td colspan="3" class="text-center text-muted py-3">-</td></tr>
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
    .kpi-tile {
      background: #fff; border: 1px solid #e9ecef; border-radius: 10px;
      padding: 12px 14px; border-left-width: 4px;
    }
    .kpi-tile.primary { border-left-color: #0d6efd; }
    .kpi-tile.success { border-left-color: #198754; }
    .kpi-tile.info    { border-left-color: #0dcaf0; }
    .kpi-tile.warning { border-left-color: #ffc107; }
    .kpi-tile .lbl { font-size: 0.7rem; text-transform: uppercase; color: #6c757d; letter-spacing: 0.04em; font-weight: 600; }
    .kpi-tile .val { font-size: 1.3rem; font-weight: 700; color: #212529; }
  `]
})
export class ProductionReportComponent implements OnInit {
  presets = DATE_PRESETS;
  preset = 'LAST_30';
  from = ''; to = '';
  fromText = ''; toText = '';
  data: ProductionReport | null = null;
  loading = false;

  constructor(private svc: ReportsService) {}

  ngOnInit(): void { this.applyPreset(); this.load(); }

  applyPreset(): void {
    const today = new Date();
    const ymd = (d: Date) => d.toISOString().substring(0, 10);
    let f = new Date(), t = new Date();
    switch (this.preset) {
      case 'TODAY':     f = t = today; break;
      case 'YESTERDAY': f.setDate(today.getDate() - 1); t = new Date(f); break;
      case 'THIS_WEEK':
        f.setDate(today.getDate() - ((today.getDay() + 6) % 7));
        t = new Date(); break;
      case 'THIS_MONTH': f = new Date(today.getFullYear(), today.getMonth(), 1); t = new Date(); break;
      case 'LAST_30':    f.setDate(today.getDate() - 30); t = new Date(); break;
      case 'CUSTOM': return;
    }
    this.from = ymd(f); this.to = ymd(t);
  }

  load(): void {
    this.loading = true;
    this.fromText = this.from || '-'; this.toText = this.to || '-';
    this.svc.production({ from: this.from, to: this.to }).subscribe({
      next: r => { this.loading = false; if (r?.success) this.data = r.data; },
      error: () => { this.loading = false; }
    });
  }

  private params() { return { from: this.from, to: this.to }; }
  excel(): void { window.open(this.svc.productionExcelUrl(this.params()), '_blank'); }
  pdf():   void { window.open(this.svc.productionPdfUrl(this.params()),   '_blank'); }

  print(): void {
    if (!this.data) return;
    const d = this.data;
    const esc = (s: any) => this.svc.escape(s);
    const num = (n: any) => (n ?? 0).toLocaleString();

    const filtersHtml = `
      <div class="filters">
        <span>Range: <b>${esc(this.fromText)} → ${esc(this.toText)}</b></span>
      </div>`;

    const kpisHtml = `
      <div class="kpis">
        <div class="kpi"><div class="lbl">Days</div><div class="val">${num(d.summary.daysCovered)}</div></div>
        <div class="kpi"><div class="lbl">Produced</div><div class="val">${num(d.summary.totalProduced)}</div></div>
        <div class="kpi"><div class="lbl">Accepted</div><div class="val">${num(d.summary.totalAccepted)}</div></div>
        <div class="kpi"><div class="lbl">Rejected</div><div class="val">${num(d.summary.totalRejected)}</div></div>
        <div class="kpi"><div class="lbl">QC Pass</div><div class="val">${d.summary.qcPassRate}%</div></div>
      </div>`;

    const dayRows = d.byDay.map(r =>
      `<tr><td>${esc(r.date)}</td><td class="num">${num(r.produced)}</td><td class="num">${num(r.accepted)}</td><td class="num">${num(r.rejected)}</td></tr>`).join('');
    const dayTable = `
      <h2>Daily Output</h2>
      <table><thead><tr><th>Date</th><th class="num">Produced</th><th class="num">Accepted</th><th class="num">Rejected</th></tr></thead>
      <tbody>${dayRows || '<tr><td colspan="4">No data</td></tr>'}</tbody></table>`;

    const machineRows = d.byMachine.map(r =>
      `<tr><td>${esc(r.machineName)}</td><td class="num">${num(r.produced)}</td><td class="num">${num(r.accepted)}</td></tr>`).join('');
    const machineTable = `
      <h2>By Machine</h2>
      <table><thead><tr><th>Machine</th><th class="num">Produced</th><th class="num">Accepted</th></tr></thead>
      <tbody>${machineRows || '<tr><td colspan="3">No data</td></tr>'}</tbody></table>`;

    const productRows = d.byProduct.map(r =>
      `<tr><td>${esc(r.productName)}</td><td class="num">${num(r.produced)}</td><td class="num">${num(r.accepted)}</td></tr>`).join('');
    const productTable = `
      <h2>By Product</h2>
      <table><thead><tr><th>Product</th><th class="num">Produced</th><th class="num">Accepted</th></tr></thead>
      <tbody>${productRows || '<tr><td colspan="3">No data</td></tr>'}</tbody></table>`;

    this.svc.openPrintWindow('Production Summary Report',
      filtersHtml + kpisHtml + dayTable + machineTable + productTable);
  }
}
