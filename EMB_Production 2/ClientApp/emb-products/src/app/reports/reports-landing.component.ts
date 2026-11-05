import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-reports-landing',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <div class="page-wrap">
      <div class="mb-3">
        <h5 class="mb-0 d-flex align-items-center">
          <i class="bi bi-bar-chart-line me-2 text-primary"></i>Reports
        </h5>
        <div class="text-muted small">
          Essential operational reports. Each opens with on-demand Excel / PDF export.
        </div>
      </div>

      <div class="row g-3">
        <div class="col-md-6 col-lg-3" *ngFor="let c of cards">
          <a [routerLink]="c.route" class="card report-card text-decoration-none">
            <div class="card-body">
              <div class="report-icon" [style.background]="c.color">
                <i class="bi" [ngClass]="c.icon"></i>
              </div>
              <h6 class="mt-3 mb-1">{{ c.title }}</h6>
              <div class="text-muted small">{{ c.description }}</div>
            </div>
          </a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .page-wrap { padding: 10px 6px; }
    .report-card {
      border: 1px solid #e9ecef;
      border-radius: 12px;
      transition: transform 0.15s ease, box-shadow 0.15s ease;
      color: inherit;
      display: block;
    }
    .report-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 6px 18px rgba(0,0,0,0.08);
    }
    .report-icon {
      width: 48px; height: 48px;
      border-radius: 12px;
      display: flex; align-items: center; justify-content: center;
      color: #fff; font-size: 24px;
    }
  `]
})
export class ReportsLandingComponent {
  cards = [
    {
      title: 'Production Summary',
      description: 'Daily output, machine-wise, QC pass rate.',
      icon: 'bi-gear-fill',
      color: 'linear-gradient(135deg, #0d6efd, #084298)',
      route: '/reports/production-summary'
    },
    {
      title: 'Sales & Dispatch',
      description: 'What shipped, to whom, value and margin.',
      icon: 'bi-truck',
      color: 'linear-gradient(135deg, #198754, #0f5132)',
      route: '/reports/sales-dispatch'
    },
    {
      title: 'Inventory Valuation',
      description: 'Stock balance + cost per branch.',
      icon: 'bi-boxes',
      color: 'linear-gradient(135deg, #fd7e14, #b6500e)',
      route: '/reports/inventory-valuation'
    },
    {
      title: 'Customer Order Tracking',
      description: 'JO → WO → finished → dispatched per customer.',
      icon: 'bi-card-checklist',
      color: 'linear-gradient(135deg, #6f42c1, #4a2d82)',
      route: '/reports/customer-tracking'
    },
  ];
}
