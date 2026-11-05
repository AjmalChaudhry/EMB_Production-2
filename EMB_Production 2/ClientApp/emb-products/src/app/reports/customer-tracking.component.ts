import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ReportsService } from '../services/reports.service';
import { CustomerTrackingReport } from '../shared/models/reports.dto';

@Component({
  selector: 'app-customer-tracking',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-wrap">
      <div class="d-flex justify-content-between align-items-center mb-3">
        <div>
          <h5 class="mb-0 d-flex align-items-center">
            <i class="bi bi-card-checklist me-2" style="color:#6f42c1;"></i>Customer Order Tracking
          </h5>
          <div class="text-muted small">
            Job Order → Work Order → Finished → Dispatched per customer.
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
              <label class="form-label small mb-0">Filter</label>
              <select class="form-select form-select-sm" [(ngModel)]="status" (change)="load()">
                <option value="all">All Orders</option>
                <option value="open">Open / Running only</option>
                <option value="closed">Fully Dispatched</option>
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
        <div class="col-md kpi-tile primary"><div class="lbl">Total Orders</div><div class="val">{{ data.summary.totalOrders }}</div></div>
        <div class="col-md kpi-tile warning"><div class="lbl">Open</div><div class="val">{{ data.summary.openCount }}</div></div>
        <div class="col-md kpi-tile info"><div class="lbl">Partial / Ready</div><div class="val">{{ data.summary.partialCount }}</div></div>
        <div class="col-md kpi-tile success"><div class="lbl">Fully Dispatched</div><div class="val">{{ data.summary.fullyDispatched }}</div></div>
        <div class="col-md kpi-tile warning"><div class="lbl">Outstanding Qty</div><div class="val">{{ data.summary.outstandingQty | number }}</div></div>
      </div>

      <div class="card" *ngIf="data">
        <div class="card-header py-2"><strong><i class="bi bi-list-ul me-1"></i>Orders</strong></div>
        <div class="table-responsive">
          <table class="table table-sm align-middle mb-0">
            <thead class="table-light">
              <tr>
                <th>JO Code</th><th>Customer</th><th>Product</th><th>Design</th>
                <th class="text-end">Ordered</th><th>Order Date</th><th>Due</th>
                <th class="text-end">WOs</th>
                <th class="text-end">Finished</th>
                <th class="text-end">Dispatched</th>
                <th class="text-end">Outstanding</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of data.rows">
                <td class="fw-semibold">{{ r.jobOrderCode }}</td>
                <td>{{ r.customerName }}</td>
                <td>{{ r.productName }}</td>
                <td>{{ r.designCode }}</td>
                <td class="text-end">{{ r.orderQty | number }}</td>
                <td>{{ r.orderDate }}</td>
                <td>{{ r.dueDate || '-' }}</td>
                <td class="text-end">{{ r.workOrderCount }}</td>
                <td class="text-end">{{ r.finishedQty | number }}</td>
                <td class="text-end">{{ r.dispatchedQty | number }}</td>
                <td class="text-end fw-semibold"
                    [class.text-warning]="r.outstandingQty > 0"
                    [class.text-success]="r.outstandingQty === 0">
                  {{ r.outstandingQty | number }}
                </td>
                <td><span class="badge" [ngClass]="statusClass(r.status)">{{ r.status }}</span></td>
              </tr>
              <tr *ngIf="!data.rows.length">
                <td colspan="12" class="text-center text-muted py-3">No orders found.</td>
              </tr>
            </tbody>
          </table>
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
export class CustomerTrackingComponent implements OnInit {
  status = 'all';
  data: CustomerTrackingReport | null = null;
  loading = false;

  constructor(private svc: ReportsService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.svc.customerTracking({ status: this.status }).subscribe({
      next: r => { this.loading = false; if (r?.success) this.data = r.data; },
      error: () => { this.loading = false; }
    });
  }

  statusClass(s: string): string {
    switch (s) {
      case 'OPEN':       return 'bg-secondary';
      case 'READY':      return 'bg-info';
      case 'PARTIAL':    return 'bg-warning text-dark';
      case 'DISPATCHED': return 'bg-success';
      default: return 'bg-light text-dark';
    }
  }

  private params() { return { status: this.status }; }
  excel(): void { window.open(this.svc.customerExcelUrl(this.params()), '_blank'); }
  pdf():   void { window.open(this.svc.customerPdfUrl(this.params()),   '_blank'); }

  print(): void {
    if (!this.data) return;
    const d = this.data;
    const esc = (s: any) => this.svc.escape(s);
    const num = (n: any) => (n ?? 0).toLocaleString();

    const statusLabel = this.status === 'open' ? 'Open / Running'
      : this.status === 'closed' ? 'Fully Dispatched'
      : 'All Orders';

    const filtersHtml = `
      <div class="filters">
        <span>Filter: <b>${esc(statusLabel)}</b></span>
      </div>`;

    const kpisHtml = `
      <div class="kpis">
        <div class="kpi"><div class="lbl">Total Orders</div><div class="val">${num(d.summary.totalOrders)}</div></div>
        <div class="kpi"><div class="lbl">Open</div><div class="val">${num(d.summary.openCount)}</div></div>
        <div class="kpi"><div class="lbl">Partial / Ready</div><div class="val">${num(d.summary.partialCount)}</div></div>
        <div class="kpi"><div class="lbl">Fully Dispatched</div><div class="val">${num(d.summary.fullyDispatched)}</div></div>
        <div class="kpi"><div class="lbl">Outstanding Qty</div><div class="val">${num(d.summary.outstandingQty)}</div></div>
      </div>`;

    const rows = d.rows.map(r => `
      <tr>
        <td>${esc(r.jobOrderCode)}</td>
        <td>${esc(r.customerName)}</td>
        <td>${esc(r.productName)}</td>
        <td>${esc(r.designCode)}</td>
        <td class="num">${num(r.orderQty)}</td>
        <td>${esc(r.orderDate)}</td>
        <td>${esc(r.dueDate || '-')}</td>
        <td class="num">${num(r.workOrderCount)}</td>
        <td class="num">${num(r.finishedQty)}</td>
        <td class="num">${num(r.dispatchedQty)}</td>
        <td class="num">${num(r.outstandingQty)}</td>
        <td>${esc(r.status)}</td>
      </tr>`).join('');

    const ordersTable = `
      <h2>Orders</h2>
      <table>
        <thead><tr>
          <th>JO Code</th><th>Customer</th><th>Product</th><th>Design</th>
          <th class="num">Ordered</th><th>Order Date</th><th>Due</th>
          <th class="num">WOs</th><th class="num">Finished</th><th class="num">Dispatched</th>
          <th class="num">Outstanding</th><th>Status</th>
        </tr></thead>
        <tbody>${rows || '<tr><td colspan="12">No orders</td></tr>'}</tbody>
      </table>`;

    this.svc.openPrintWindow('Customer Order Tracking',
      filtersHtml + kpisHtml + ordersTable);
  }
}
