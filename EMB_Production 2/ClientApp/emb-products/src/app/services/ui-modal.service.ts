import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class UiModalService {

  private _activeModal: string | null = null;

  open(key: string): void {
    this._activeModal = key;
    document.body.classList.add('modal-open');
  }

  close(): void {
    this._activeModal = null;
    document.body.classList.remove('modal-open');
  }

  isOpen(key: string): boolean {
    return this._activeModal === key;
  }
}
