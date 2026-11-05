import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ProductionReport, SalesReport, InventoryReport, CustomerTrackingReport
} from '../shared/models/reports.dto';

interface ApiEnvelope<T> { success: boolean; message: string; data: T; }

@Injectable({ providedIn: 'root' })
export class ReportsService {
  private base = '/emb-products/reports';

  constructor(private http: HttpClient) {}

  // ------- DATA -------
  production(p: { from?: string; to?: string; branchId?: number; machineId?: number; productId?: number; }
            ): Observable<ApiEnvelope<ProductionReport>> {
    return this.http.get<ApiEnvelope<ProductionReport>>(`${this.base}/production-summary${this.qs(p)}`);
  }
  sales(p: { from?: string; to?: string; branchId?: number; customerId?: number; }
       ): Observable<ApiEnvelope<SalesReport>> {
    return this.http.get<ApiEnvelope<SalesReport>>(`${this.base}/sales-dispatch${this.qs(p)}`);
  }
  inventory(p: { branchId?: number; stockType?: string; }): Observable<ApiEnvelope<InventoryReport>> {
    return this.http.get<ApiEnvelope<InventoryReport>>(`${this.base}/inventory-valuation${this.qs(p)}`);
  }
  customerTracking(p: { customerId?: number; status?: string; }
                  ): Observable<ApiEnvelope<CustomerTrackingReport>> {
    return this.http.get<ApiEnvelope<CustomerTrackingReport>>(`${this.base}/customer-tracking${this.qs(p)}`);
  }

  // ------- EXPORT URLs (open in new tab) -------
  productionExcelUrl(p: any): string { return `${this.base}/production-summary/export-excel${this.qs(p)}`; }
  productionPdfUrl(p: any):   string { return `${this.base}/production-summary/export-pdf${this.qs(p)}`; }

  salesExcelUrl(p: any): string { return `${this.base}/sales-dispatch/export-excel${this.qs(p)}`; }
  salesPdfUrl(p: any):   string { return `${this.base}/sales-dispatch/export-pdf${this.qs(p)}`; }

  inventoryExcelUrl(p: any): string { return `${this.base}/inventory-valuation/export-excel${this.qs(p)}`; }
  inventoryPdfUrl(p: any):   string { return `${this.base}/inventory-valuation/export-pdf${this.qs(p)}`; }

  customerExcelUrl(p: any): string { return `${this.base}/customer-tracking/export-excel${this.qs(p)}`; }
  customerPdfUrl(p: any):   string { return `${this.base}/customer-tracking/export-pdf${this.qs(p)}`; }

  private qs(p: any): string {
    const qs = new URLSearchParams();
    if (!p) return '';
    for (const k of Object.keys(p)) {
      const v = (p as any)[k];
      if (v !== undefined && v !== null && v !== '') qs.set(k, String(v));
    }
    const s = qs.toString();
    return s ? '?' + s : '';
  }

  // ------- PRINT (opens a styled window and triggers print) -------
  // Each report component generates its own body HTML (filters + KPIs +
  // section tables) and passes it in. We wrap with consistent CSS so the
  // print output looks the same across reports.
  openPrintWindow(title: string, bodyHtml: string): void {
    const w = window.open('', '_blank', 'width=1100,height=800');
    if (!w) return;

    const stamp = new Date().toLocaleString();
    const css = `
      * { box-sizing: border-box; }
      body { font-family: Arial, Helvetica, sans-serif; color: #212529; margin: 24px; }
      h1 { font-size: 18px; margin: 0 0 4px 0; }
      .meta { font-size: 11px; color: #6c757d; margin-bottom: 16px; }
      .kpis { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 18px; }
      .kpi { border: 1px solid #dee2e6; border-radius: 6px; padding: 6px 12px; min-width: 140px; }
      .kpi .lbl { font-size: 10px; text-transform: uppercase; color: #6c757d; letter-spacing: 0.04em; font-weight: 600; }
      .kpi .val { font-size: 14px; font-weight: 700; }
      .filters { font-size: 11px; margin-bottom: 14px; }
      .filters span { margin-right: 14px; }
      .filters b { color: #212529; }
      h2 { font-size: 13px; margin: 18px 0 6px; padding: 4px 8px; background: #e8f4fd; border-radius: 4px; }
      table { width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 8px; }
      th, td { border: 1px solid #dee2e6; padding: 4px 8px; }
      th { background: #f1f3f5; text-align: left; font-weight: 700; }
      td.num, th.num { text-align: right; }
      tr.total td { font-weight: 700; border-top: 2px solid #adb5bd; background: #f8f9fa; }
      @media print {
        body { margin: 12mm; }
        .no-print { display: none; }
      }
    `;

    w.document.open();
    w.document.write(`<!doctype html>
<html><head><meta charset="utf-8"><title>${this.escape(title)}</title>
<style>${css}</style></head>
<body>
  <h1>${this.escape(title)}</h1>
  <div class="meta">Generated: ${stamp}</div>
  ${bodyHtml}
  <script>
    window.onload = function() {
      setTimeout(function() { window.print(); }, 200);
    };
  </script>
</body></html>`);
    w.document.close();
  }

  // Helper used by report components to build print-friendly HTML safely.
  escape(s: any): string {
    return String(s ?? '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
}
