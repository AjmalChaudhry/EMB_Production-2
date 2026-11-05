import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

export type ConfirmKind = 'default' | 'danger' | 'warning';

export interface ConfirmRequest {
  id: number;
  message: string;
  title: string;
  kind: ConfirmKind;
  confirmText: string;
  cancelText: string;
  resolve: (ok: boolean) => void;
}

export interface ConfirmOptions {
  title?: string;
  kind?: ConfirmKind;       // 'danger' = red OK button (delete/reverse), 'warning' = yellow, 'default' = blue
  confirmText?: string;     // default 'OK'
  cancelText?: string;      // default 'Cancel'
}

/**
 * Promise-based replacement for window.confirm(). The native dialog
 * anchors to the top of the viewport and collides with our app topbar;
 * the ConfirmDialogComponent (mounted once in app.component.html)
 * renders a styled modal below the topbar instead.
 *
 * Usage:
 *   if (await this.confirmSvc.ask('Reverse this WO?')) { ... }
 *   if (await this.confirmSvc.danger('Delete forever?')) { ... }
 *
 * Multiple concurrent confirms are NOT supported (would be confusing
 * UX) — the second call queues until the first resolves.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private nextId = 1;
  private readonly _active$ = new BehaviorSubject<ConfirmRequest | null>(null);
  readonly active$: Observable<ConfirmRequest | null> = this._active$.asObservable();

  // Pending requests queued behind the currently-displayed one.
  private queue: ConfirmRequest[] = [];

  /** Generic confirm. Returns true if the user clicked Confirm. */
  ask(message: string, opts: ConfirmOptions = {}): Promise<boolean> {
    return new Promise<boolean>(resolve => {
      const req: ConfirmRequest = {
        id: this.nextId++,
        message,
        title:       opts.title       ?? 'Please confirm',
        kind:        opts.kind        ?? 'default',
        confirmText: opts.confirmText ?? 'OK',
        cancelText:  opts.cancelText  ?? 'Cancel',
        resolve
      };
      if (this._active$.value) {
        this.queue.push(req);
      } else {
        this._active$.next(req);
      }
    });
  }

  /** Convenience: red OK button for destructive actions. */
  danger(message: string, opts: ConfirmOptions = {}): Promise<boolean> {
    return this.ask(message, { kind: 'danger', confirmText: 'Confirm', ...opts });
  }

  /** Called by the dialog component when the user clicks a button. */
  resolveActive(ok: boolean): void {
    const current = this._active$.value;
    if (!current) return;
    current.resolve(ok);

    // Pull the next queued (if any) onto the stage.
    const next = this.queue.shift() ?? null;
    this._active$.next(next);
  }
}
