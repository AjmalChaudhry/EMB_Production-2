import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: number;
  message: string;
  kind: ToastKind;
  /** ms until auto-dismiss. 0 = never auto-dismiss. */
  durationMs: number;
}

/**
 * App-wide toast bus. Components call success/error/info/warning;
 * the single ToastContainerComponent (mounted in app.component.html)
 * subscribes to toasts$ and renders the stack at top-right.
 *
 * Usage:
 *   constructor(private toast: ToastService) {}
 *   this.toast.success('Saved.');
 *   this.toast.error('Save failed.', 6000);   // override duration
 *   this.toast.show('Custom', 'warning', 0);  // sticky (no auto-dismiss)
 *
 * Returns the toast id from show() so callers can dismiss specific
 * toasts early (rare — auto-dismiss handles 99% of cases).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  private readonly _toasts$ = new BehaviorSubject<Toast[]>([]);
  readonly toasts$: Observable<Toast[]> = this._toasts$.asObservable();

  show(message: string, kind: ToastKind = 'info', durationMs = 4000): number {
    const id = this.nextId++;
    const toast: Toast = { id, message, kind, durationMs };
    this._toasts$.next([...this._toasts$.value, toast]);

    if (durationMs > 0) {
      setTimeout(() => this.dismiss(id), durationMs);
    }
    return id;
  }

  // Sensible defaults: errors stay longer so the user can read them;
  // warnings sit between info and error.
  success(message: string, durationMs = 4000): number {
    return this.show(message, 'success', durationMs);
  }
  error(message: string, durationMs = 6000): number {
    return this.show(message, 'error', durationMs);
  }
  info(message: string, durationMs = 4000): number {
    return this.show(message, 'info', durationMs);
  }
  warning(message: string, durationMs = 5000): number {
    return this.show(message, 'warning', durationMs);
  }

  dismiss(id: number): void {
    const remaining = this._toasts$.value.filter(t => t.id !== id);
    if (remaining.length !== this._toasts$.value.length) {
      this._toasts$.next(remaining);
    }
  }

  clear(): void {
    if (this._toasts$.value.length > 0) this._toasts$.next([]);
  }
}
