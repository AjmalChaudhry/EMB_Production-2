import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';

import { Toast, ToastKind, ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './toast-container.component.html',
  styleUrls: ['./toast-container.component.css']
})
export class ToastContainerComponent implements OnInit, OnDestroy {
  toasts: Toast[] = [];
  private sub?: Subscription;

  constructor(private toastSvc: ToastService) {}

  ngOnInit(): void {
    this.sub = this.toastSvc.toasts$.subscribe(list => this.toasts = list);
  }
  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  dismiss(id: number): void {
    this.toastSvc.dismiss(id);
  }

  iconFor(kind: ToastKind): string {
    switch (kind) {
      case 'success': return 'bi-check-circle-fill';
      case 'error':   return 'bi-exclamation-triangle-fill';
      case 'warning': return 'bi-exclamation-octagon-fill';
      case 'info':
      default:        return 'bi-info-circle-fill';
    }
  }

  trackById(_: number, t: Toast): number { return t.id; }
}
