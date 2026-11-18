import { Component, OnInit } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { IssueMaterialsService, IssueSuggestion, IssueSuggestResponse, IssueListRow, IssueDetail, IssueDto } from '../services/issue-materials.service';
import { WorkOrderService } from '../services/work-order.service';

interface DraftItem extends IssueSuggestion {
  qty: number;
  rate: number;
  remarks: string;
  include: boolean;
}

@Component({
  selector: 'app-issue-materials',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './issue-materials.component.html',
  styleUrls: ['./issue-materials.component.css']
})
export class IssueMaterialsComponent implements OnInit {

  workOrders: Array<{ id: number; workOrderCode: string }> = [];
  selectedWorkOrderId: number | null = null;
  remarks = '';

  suggest: IssueSuggestResponse | null = null;
  draft: DraftItem[] = [];

  loading = false;
  saving = false;
  error = '';

  list: IssueListRow[] = [];
  detail: IssueDetail | null = null;
  detailOpen = false;

  // Edit mode — when set, Save will reverse this issue first then create a new one
  editingIssueCode: string | null = null;

  constructor(
    private api: IssueMaterialsService,
    private wo: WorkOrderService,
    private location: Location,
    private router: Router
  ) { }

  /** Back button — uses the browser-history Location service so deep
   *  links (e.g. opened from a WO row) return to the referrer, while a
   *  cold-load (direct URL paste, no history) falls back to the Work
   *  Orders list as the natural parent. */
  goBack(): void {
    if (window.history.length > 1) this.location.back();
    else this.router.navigate(['/work-orders']);
  }

  /** Cancel — if a draft is in progress, ask before discarding. */
  cancel(): void {
    const dirty = this.draft.some(d => d.include) && !this.saving;
    if (dirty && !confirm('Discard the current issue draft and leave?')) return;
    this.goBack();
  }

  ngOnInit(): void {
    this.refreshList();
    this.loadWorkOrders();
  }

  loadWorkOrders(): void {
    this.wo.getPendingForIssue().subscribe({
      next: (rows) => {
        this.workOrders = rows
          .filter(x => x.code)
          .map(x => ({ id: x.id, workOrderCode: x.code }));
      },
      error: _ => { this.workOrders = []; }
    });
  }

  onSuggest(): void {
    this.error = '';
    if (!this.selectedWorkOrderId) { this.error = 'Pick a work order first.'; return; }
    this.loading = true;
    this.suggest = null;
    this.draft = [];

    this.api.suggest(this.selectedWorkOrderId).subscribe({
      next: r => {
        this.suggest = r?.data ?? null;
        const sugs = this.suggest?.suggestions ?? [];
        this.draft = sugs.map(s => ({
          ...s,
          qty: s.suggestedQty,
          rate: s.unitCost,
          remarks: '',
          include: s.suggestedQty > 0
        }));
        this.loading = false;
      },
      error: e => { this.error = e?.error?.message || 'Failed to fetch suggestions.'; this.loading = false; }
    });
  }

  shortfall(d: DraftItem): number {
    if (!d.include || d.qty <= 0) return 0;
    return Math.max(0, d.qty - d.currentBalance);
  }
  hasShortfall(d: DraftItem): boolean { return this.shortfall(d) > 0; }

  get includedItems(): DraftItem[] { return this.draft.filter(d => d.include && d.qty > 0); }
  get totalQty(): number { return this.includedItems.reduce((s, d) => s + d.qty, 0); }
  get totalAmount(): number { return this.includedItems.reduce((s, d) => s + d.qty * (d.rate || 0), 0); }
  get anyShortfall(): boolean { return this.includedItems.some(d => this.hasShortfall(d)); }

  onSave(): void {
    this.error = '';
    if (!this.selectedWorkOrderId) { this.error = 'No work order selected.'; return; }
    const items = this.includedItems;
    if (items.length === 0) { this.error = 'Tick at least one row with qty > 0.'; return; }
    if (this.anyShortfall) {
      this.error = 'One or more items exceed available stock. Adjust qty or skip.';
      return;
    }

    const dto: IssueDto = {
      workOrderId: this.selectedWorkOrderId,
      remarks: this.remarks || null,
      items: items.map(d => ({
        threadId: d.threadId,
        qty: d.qty,
        rate: d.rate,
        remarks: d.remarks || null
      }))
    };

    this.saving = true;

    // In edit mode: reverse the old issue first, then save the new one.
    // The reverse restores stock so the new save's availability check passes.
    if (this.editingIssueCode) {
      const oldCode = this.editingIssueCode;
      this.api.reverse(oldCode).subscribe({
        next: rr => {
          if (!rr?.success) {
            this.saving = false;
            this.error = rr?.message || 'Could not reverse the original issue.';
            return;
          }
          this.api.save(dto).subscribe({
            next: r => this.afterSave(r),
            error: e => { this.saving = false; this.error = e?.error?.message || 'Save failed after reverse.'; }
          });
        },
        error: e => { this.saving = false; this.error = e?.error?.message || 'Reverse failed.'; }
      });
      return;
    }

    this.api.save(dto).subscribe({
      next: r => this.afterSave(r),
      error: e => { this.saving = false; this.error = e?.error?.message || 'Save failed.'; }
    });
  }

  private afterSave(r: any): void {
    this.saving = false;
    if (!r?.success) { this.error = r?.message || 'Save failed.'; return; }
    this.draft = [];
    this.suggest = null;
    this.remarks = '';
    this.editingIssueCode = null;
    this.refreshList();
  }

  // Edit an existing issue: pull its lines, mark mode, scroll user to the form.
  // Existing items are merged into the BOM-suggested list so the user can adjust
  // qty / rate / remarks. Save will reverse the old issue then create a new one.
  onEdit(row: IssueListRow): void {
    this.error = '';
    if (!row?.issueCode) return;
    if (!row.workOrderId) {
      this.error = 'Issue has no work order link.';
      return;
    }

    this.loading = true;
    this.editingIssueCode = row.issueCode;
    this.selectedWorkOrderId = row.workOrderId;

    this.api.detail(row.issueCode).subscribe({
      next: d => {
        const detail = d?.data;
        if (!detail) {
          this.loading = false;
          this.error = 'Issue detail not found.';
          this.editingIssueCode = null;
          return;
        }
        this.remarks = '';
        // Get fresh BOM suggestions for current balances + thread metadata,
        // then overwrite qty / rate / remarks with the existing issue's lines.
        this.api.suggest(row.workOrderId!).subscribe({
          next: s => {
            this.suggest = s?.data ?? null;
            const sugs = this.suggest?.suggestions ?? [];
            // Map by threadId. For lines that exist in the issue but not in
            // current BOM (e.g. BOM was edited later) we still surface them.
            const byThread = new Map<number, DraftItem>();
            for (const sg of sugs) {
              byThread.set(sg.threadId, {
                ...sg,
                qty: sg.suggestedQty,
                rate: sg.unitCost,
                remarks: '',
                include: false
              });
            }
            for (const it of detail.items) {
              if (!it.threadId) continue;
              const existing = byThread.get(it.threadId);
              if (existing) {
                existing.qty = it.qty;
                existing.rate = it.rate;
                existing.remarks = it.remarks || '';
                existing.include = true;
                // Add the issued qty back to the available balance for display
                // (the reverse step will physically restore it on save).
                existing.currentBalance = existing.currentBalance + it.qty;
              } else {
                byThread.set(it.threadId, {
                  threadId: it.threadId,
                  code: it.threadCode,
                  name: it.threadName,
                  color: it.threadColor,
                  unit: it.unit,
                  unitCost: it.rate,
                  perPiece: 0,
                  suggestedQty: it.qty,
                  currentBalance: it.qty,
                  qty: it.qty,
                  rate: it.rate,
                  remarks: it.remarks || '',
                  include: true
                });
              }
            }
            this.draft = Array.from(byThread.values()).sort((a, b) => a.code.localeCompare(b.code));
            this.loading = false;
            // Smooth scroll the suggest card into view
            setTimeout(() => {
              document.querySelector('.issue-materials-page .card.mb-3:nth-of-type(2)')
                ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }, 50);
          },
          error: e => {
            this.loading = false;
            this.error = e?.error?.message || 'Failed to load BOM suggestions for edit.';
          }
        });
      },
      error: e => {
        this.loading = false;
        this.editingIssueCode = null;
        this.error = e?.error?.message || 'Failed to load issue detail.';
      }
    });
  }

  cancelEdit(): void {
    this.editingIssueCode = null;
    this.draft = [];
    this.suggest = null;
    this.remarks = '';
    this.error = '';
  }

  refreshList(): void {
    this.api.list().subscribe({
      next: r => { this.list = r?.data ?? []; },
      error: _ => { this.list = []; }
    });
  }

  openDetail(row: IssueListRow): void {
    this.detailOpen = true;
    this.detail = null;
    this.api.detail(row.issueCode).subscribe({
      next: r => { this.detail = r?.data ?? null; },
      error: _ => { this.detail = null; }
    });
  }
  closeDetail(): void { this.detailOpen = false; this.detail = null; }

  reverse(row: IssueListRow): void {
    if (!confirm(`Reverse issue ${row.issueCode}? This will restore the threads to stock.`)) return;
    this.api.reverse(row.issueCode).subscribe({
      next: r => { if (r?.success) this.refreshList(); else this.error = r?.message || 'Reverse failed.'; },
      error: e => { this.error = e?.error?.message || 'Reverse failed.'; }
    });
  }
}
