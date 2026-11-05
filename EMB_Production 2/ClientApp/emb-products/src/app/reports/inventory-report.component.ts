import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ReportsService } from '../services/reports.service';
import { InventoryReport } from '../shared/models/reports.dto';

@Component({
  selector: 'app-inventory-report',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-wrap">
      <div class="d-flex justify-content-between align-items-center mb-3">
        <div>
          <h5 class="mb-0 d-flex align-items-center">
            <i class="bi bi-boxes me-2 text-warning"></i>Inventory Valuation
          </h5>
          <div class="text-muted small">
            Stock balance and value at current rates.
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
              <label class="form-label small mb-0">Stock Type</label>
              <select class="form-select form-select-sm" [(ngModel)]="stockType" (change)="load()">
                <option value="all">All (raw + finished)</option>
                <option value="raw">Raw materials only</option>
                <option value="finished">Finished stock only</option>
              </select>
            </div>
            <div class="col-md-3">
              <label class="form-label small mb-0">Branch</label>
              <select class="form-select form-select-sm" [(ngModel)]="branchId" (change)="load()">
                <option [ngValue]="0">All branches</option>
                <option *ngFor="let b of branches" [ngValue]="b.id">{{ b.name }}</option>
              </select>
            </div>
            <div class="col-md-2 d-flex align-items-end">
              <button class="btn btn-sm btn-primary" (click)="load()" [disabled]="loading">
                <i class="bi bi-arrow-clockwise me-1"></i>{{ loading ? 'Loading…' : 'Refresh' }}
              </button>
            </div>
          </div>
        </div>
      </div>

      <div *ngIf="data" class="row g-2 mb-3">
        <div class="col-md kpi-tile info"><div class="lbl">Raw Items</div><div class="val">{{ data.summary.rawItemCount }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Raw Value</div><div class="val">{{ data.summary.rawValue | number:'1.0-2' }}</div></div>
        <div class="col-md kpi-tile warning"><div class="lbl">Finished Items</div><div class="val">{{ data.summary.finishedItemCount }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Finished Value</div><div class="val">{{ data.summary.finishedValue | number:'1.0-2' }}</div></div>
        <div class="col-md kpi-tile primary"><div class="lbl">Grand Total</div><div class="val">{{ data.summary.grandTotal | number:'1.0-2' }}</div></div>
      </div>

      <div class="row g-3" *ngIf="data">
        <div class="col-md-6">
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-bag me-1"></i>Raw Materials</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light">
                  <tr><th>Item</th><th>Type</th><th class="text-end">Balance</th><th class="text-end">Rate</th><th class="text-end">Value</th></tr>
                </thead>
                <tbody>
                  <tr *ngFor="let r of data.rawItems">
                    <td>{{ r.name }}</td><td>{{ r.type }}</td>
                    <td class="text-end">{{ r.balance | number:'1.0-2' }}</td>
                    <td class="text-end">{{ r.lastRate | number:'1.0-2' }}</td>
                    <td class="text-end fw-semibold">{{ r.value | number:'1.0-2' }}</td>
                  </tr>
                  <tr *ngIf="!data.rawItems.length"><td colspan="5" class="text-center text-muted py-3">No raw stock.</td></tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
        <div class="col-md-6">
          <div class="card">
            <div class="card-header py-2"><strong><i class="bi bi-box-seam me-1"></i>Finished Stock</strong></div>
            <div class="table-responsive">
              <table class="table table-sm mb-0">
                <thead class="table-light">
                  <tr>
                    <th>Product</th>
                    <th class="text-end">Reserved</th>
                    <th class="text-end">Free</th>
                    <th class="text-end">Total</th>
                    <th class="text-end">Unit Cost</th>
                    <th class="text-end">Value</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let r of data.finishedStock">
                    <td>{{ r.productName }}</td>
                    <td class="text-end">{{ r.reserved | number }}</td>
                    <td class="text-end">{{ r.free | number }}</td>
                    <td class="text-end fw-semibold">{{ r.balance | number }}</td>
                    <td class="text-end">{{ r.unitCost | number:'1.0-2' }}</td>
                    <td class="text-end fw-semibold">{{ r.value | number:'1.0-2' }}</td>
                  </tr>
                  <tr *ngIf="!data.finishedStock.length"><td colspan="6" class="text-center text-muted py-3">No finished stock.</td></tr>
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
export class InventoryReportComponent implements OnInit {
  stockType = 'all';
  branchId = 0;
  branches: Array<{ id: number; name: string }> = [];
  data: InventoryReport | null = null;
  loading = false;

  constructor(private svc: ReportsService, private http: HttpClient) {}

  ngOnInit(): void {
    this.loadBranches();
    this.load();
  }

  private loadBranches(): void {
    this.http.get<any>('/emb-products/dropdowns/branches').subscribe({
      next: res => {
        const list = res?.data ?? res ?? [];
        this.branches = list.map((b: any) => ({
          id: b.id ?? b.Id,
          name: b.name ?? b.Name ?? b.branchName ?? `Branch #${b.id ?? b.Id}`
        }));
      }
    });
  }

  load(): void {
    this.loading = true;
    this.svc.inventory(this.params()).subscribe({
      next: r => { this.loading = false; if (r?.success) this.data = r.data; },
      error: () => { this.loading = false; }
    });
  }

  private params() {
    return this.branchId > 0
      ? { stockType: this.stockType, branchId: this.branchId }
      : { stockType: this.stockType };
  }
  excel(): void { window.open(this.svc.inventoryExcelUrl(this.params()), '_blank'); }
  pdf():   void { window.open(this.svc.inventoryPdfUrl(this.params()),   '_blank'); }

  print(): void {
    if (!this.data) return;
    const d = this.data;
    const esc = (s: any) => this.svc.escape(s);
    const num = (n: any, decimals = 0) => (n ?? 0).toLocaleString(undefined,
      decimals > 0 ? { minimumFractionDigits: decimals, maximumFractionDigits: decimals } : undefined);

    const stockLabel = this.stockType === 'raw' ? 'Raw materials only'
      : this.stockType === 'finished' ? 'Finished stock only'
      : 'All (raw + finished)';
    const branchLabel = this.branchId > 0
      ? (this.branches.find(b => b.id === this.branchId)?.name ?? 'Branch')
      : 'All branches';

    const filtersHtml = `
      <div class="filters">
        <span>Stock Type: <b>${esc(stockLabel)}</b></span>
        <span>Branch: <b>${esc(branchLabel)}</b></span>
      </div>`;

    const kpisHtml = `
      <div class="kpis">
        <div class="kpi"><div class="lbl">Raw Items</div><div class="val">${num(d.summary.rawItemCount)}</div></div>
        <div class="kpi"><div class="lbl">Raw Value</div><div class="val">${num(d.summary.rawValue, 2)}</div></div>
        <div class="kpi"><div class="lbl">Finished Items</div><div class="val">${num(d.summary.finishedItemCount)}</div></div>
        <div class="kpi"><div class="lbl">Finished Value</div><div class="val">${num(d.summary.finishedValue, 2)}</div></div>
        <div class="kpi"><div class="lbl">Grand Total</div><div class="val">${num(d.summary.grandTotal, 2)}</div></div>
      </div>`;

    const rawRows = d.rawItems.map(r =>
      `<tr><td>${esc(r.name)}</td><td>${esc(r.type)}</td><td class="num">${num(r.balance, 2)}</td><td class="num">${num(r.lastRate, 2)}</td><td class="num">${num(r.value, 2)}</td></tr>`).join('');
    const rawTotal = `<tr class="total"><td colspan="2">Total</td><td class="num">${num(d.rawItems.reduce((s, r) => s + Number(r.balance || 0), 0), 2)}</td><td></td><td class="num">${num(d.summary.rawValue, 2)}</td></tr>`;
    const rawTable = `
      <h2>Raw Materials</h2>
      <table><thead><tr><th>Item</th><th>Type</th><th class="num">Balance</th><th class="num">Last Rate</th><th class="num">Value</th></tr></thead>
      <tbody>${rawRows || '<tr><td colspan="5">No raw stock</td></tr>'}${rawRows ? rawTotal : ''}</tbody></table>`;

    const finRows = d.finishedStock.map(r =>
      `<tr><td>${esc(r.productName)}</td><td class="num">${num(r.reserved)}</td><td class="num">${num(r.free)}</td><td class="num">${num(r.balance)}</td><td class="num">${num(r.unitCost, 2)}</td><td class="num">${num(r.value, 2)}</td></tr>`).join('');
    const finTable = `
      <h2>Finished Stock</h2>
      <table><thead><tr><th>Product</th><th class="num">Reserved</th><th class="num">Free</th><th class="num">Total</th><th class="num">Unit Cost</th><th class="num">Value</th></tr></thead>
      <tbody>${finRows || '<tr><td colspan="6">No finished stock</td></tr>'}</tbody></table>`;

    this.svc.openPrintWindow('Inventory Valuation',
      filtersHtml + kpisHtml + rawTable + finTable);
  }
}
