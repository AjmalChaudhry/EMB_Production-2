import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ThreadStockService, ThreadStockBalance, ThreadHistory } from '../services/thread-stock.service';

@Component({
  selector: 'app-thread-stock',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './thread-stock.component.html',
  styleUrls: ['./thread-stock.component.css']
})
export class ThreadStockComponent implements OnInit {
  rows: ThreadStockBalance[] = [];
  loading = false;
  search = '';

  history: ThreadHistory | null = null;
  historyLoading = false;
  historyOpen = false;

  ngOnInit(): void { this.load(); }

  constructor(private api: ThreadStockService) { }

  load(): void {
    this.loading = true;
    this.api.balance(undefined, this.search).subscribe({
      next: r => { this.rows = r?.data ?? []; this.loading = false; },
      error: _ => { this.rows = []; this.loading = false; }
    });
  }

  openHistory(row: ThreadStockBalance): void {
    this.historyOpen = true;
    this.history = null;
    this.historyLoading = true;
    this.api.history(row.threadId).subscribe({
      next: r => { this.history = r?.data ?? null; this.historyLoading = false; },
      error: _ => { this.history = null; this.historyLoading = false; }
    });
  }

  closeHistory(): void { this.historyOpen = false; this.history = null; }

  trackByThread(_i: number, r: ThreadStockBalance): number { return r.threadId; }

  get totalBalance(): number { return this.rows.reduce((s, r) => s + (r.balance || 0), 0); }
  get totalValue(): number { return this.rows.reduce((s, r) => s + (r.balanceValue || 0), 0); }
  get belowZeroCount(): number { return this.rows.filter(r => (r.balance || 0) < 0).length; }
}
