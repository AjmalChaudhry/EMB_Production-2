import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';

import { SupplierService } from '../services/supplier.service';
import {
  SupplierProfileViewModel,
  SupplierProfileTrendMonth
} from '../shared/models/supplier.dto';

@Component({
  selector: 'app-supplier-profile',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './supplier-profile.component.html',
  styleUrls: ['./supplier-profile.component.css']
})
export class SupplierProfileComponent implements OnInit {

  loading = false;
  errorMsg = '';
  profile: SupplierProfileViewModel | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private supplierSvc: SupplierService
  ) { }

  ngOnInit(): void {
    this.route.paramMap.subscribe(p => {
      const id = Number(p.get('id') || 0);
      if (!id) {
        this.errorMsg = 'No supplier id supplied.';
        return;
      }
      this.load(id);
    });
  }

  load(id: number): void {
    this.loading = true;
    this.errorMsg = '';
    this.supplierSvc.getProfile(id).subscribe({
      next: (res: any) => {
        this.loading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          this.errorMsg = res?.message || 'Failed to load profile.';
          this.profile = null;
          return;
        }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.loading = false;
        this.errorMsg = err?.error?.message || 'Failed to load profile.';
        this.profile = null;
      }
    });
  }

  goBack(): void {
    this.router.navigate(['/suppliers']);
  }

  goToPurchase(id: number): void {
    // /purchase doesn't accept a deep-link to a row yet; just navigate.
    this.router.navigate(['/purchase']);
  }

  // Trend mini-chart helpers (same shape as Purchase F.3)
  trendMax(): number {
    const t = this.profile?.monthlyTrend ?? [];
    return Math.max(...t.map(m => Number(m.amount || 0)), 1);
  }
  trendBarHeight(m: SupplierProfileTrendMonth): number {
    const max = this.trendMax();
    const pct = (Number(m.amount || 0) / max) * 100;
    return Math.max(2, Math.round(pct));
  }
  formatTrendAmount(amount: number): string {
    const n = Number(amount || 0);
    if (n === 0) return '0';
    const abs = Math.abs(n);
    if (abs >= 1_000_000) return (n / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1) + 'M';
    if (abs >= 1_000)     return (n / 1_000).toFixed(abs >= 10_000 ? 0 : 1) + 'K';
    return String(Math.round(n));
  }
}
