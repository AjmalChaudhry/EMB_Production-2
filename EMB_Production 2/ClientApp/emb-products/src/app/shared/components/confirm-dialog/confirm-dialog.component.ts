import { Component, HostListener, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';

import { ConfirmKind, ConfirmRequest, ConfirmService } from '../../services/confirm.service';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './confirm-dialog.component.html',
  styleUrls: ['./confirm-dialog.component.css']
})
export class ConfirmDialogComponent implements OnInit, OnDestroy {
  current: ConfirmRequest | null = null;
  private sub?: Subscription;

  constructor(private svc: ConfirmService) {}

  ngOnInit(): void {
    this.sub = this.svc.active$.subscribe(r => this.current = r);
  }
  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  ok(): void { this.svc.resolveActive(true); }
  cancel(): void { this.svc.resolveActive(false); }

  /** Esc cancels, Enter confirms. */
  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!this.current) return;
    if (e.key === 'Escape') { e.preventDefault(); this.cancel(); }
    else if (e.key === 'Enter') { e.preventDefault(); this.ok(); }
  }

  confirmBtnClass(kind: ConfirmKind): string {
    switch (kind) {
      case 'danger':  return 'btn-danger';
      case 'warning': return 'btn-warning';
      default:        return 'btn-primary';
    }
  }
  iconFor(kind: ConfirmKind): string {
    switch (kind) {
      case 'danger':  return 'bi-exclamation-triangle-fill text-danger';
      case 'warning': return 'bi-exclamation-octagon-fill text-warning';
      default:        return 'bi-question-circle-fill text-primary';
    }
  }
}
