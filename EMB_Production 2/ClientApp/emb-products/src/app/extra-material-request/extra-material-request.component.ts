import { Component, ElementRef, OnInit, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as bootstrap from 'bootstrap';

import { EmrService } from '../services/emr.service';
import {
  EmrRow, EmrItemRow, EmrSavePayload, EmrSummary,
  WorkOrderDropdownItem, RawItemDropdown, ThreadDropdown,
  EMR_REASONS, EmrItemType
} from '../shared/models/emr.dto';

interface EditableItemRow {
  id: number;
  itemType: EmrItemType;
  rawItemId: number | null;
  threadId: number | null;
  itemName: string;
  unitOfMeasure: string;
  qty: number;
  rate: number | null;
  remarks: string;
}

@Component({
  selector: 'app-extra-material-request',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './extra-material-request.component.html',
  styleUrls: ['./extra-material-request.component.css']
})
export class ExtraMaterialRequestComponent implements OnInit, AfterViewInit {
  // ============ STATE ============
  rows: EmrRow[] = [];
  summary: EmrSummary = { count: 0, draftCount: 0, pendingCount: 0, approvedCount: 0, rejectedCount: 0 };
  loading = false;
  saving = false;

  // Filters
  statusFilter = '';
  workOrderFilter: number | null = null;
  searchText = '';
  showDeleted = '';

  // Dropdowns
  workOrders: WorkOrderDropdownItem[] = [];
  rawItems: RawItemDropdown[] = [];
  threads: ThreadDropdown[] = [];

  readonly reasons = EMR_REASONS;

  // Form state
  modalMode: 'create' | 'edit' = 'create';
  form: {
    id: number;
    emrCode: string;
    workOrderId: number | null;
    reason: string;
    remarks: string;
    items: EditableItemRow[];
  } = this.emptyForm();

  // Reject dialog
  rejectId: number | null = null;
  rejectReason = '';

  // Detail view
  selectedDetailItems: EmrItemRow[] = [];
  selectedDetailHeader: EmrRow | null = null;

  // 360 Profile drawer
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ============ MODALS ============
  @ViewChild('formModal')   formModal?: ElementRef;
  @ViewChild('detailModal') detailModal?: ElementRef;
  @ViewChild('rejectModal') rejectModal?: ElementRef;
  private formModalInstance?: bootstrap.Modal;
  private detailModalInstance?: bootstrap.Modal;
  private rejectModalInstance?: bootstrap.Modal;

  constructor(private svc: EmrService) {}

  ngOnInit(): void {
    this.loadDropdowns();
    this.loadList();
  }

  ngAfterViewInit(): void {
    if (this.formModal)   this.formModalInstance   = new bootstrap.Modal(this.formModal.nativeElement);
    if (this.detailModal) this.detailModalInstance = new bootstrap.Modal(this.detailModal.nativeElement);
    if (this.rejectModal) this.rejectModalInstance = new bootstrap.Modal(this.rejectModal.nativeElement);
  }

  // ============ DROPDOWNS ============
  private loadDropdowns(): void {
    this.svc.workOrders().subscribe(r => { if (r?.success) this.workOrders = r.data || []; });
    this.svc.rawItems().subscribe(r => { if (r?.success) this.rawItems = r.data || []; });
    this.svc.threads().subscribe(r => { if (r?.success) this.threads = r.data || []; });
  }

  // ============ LIST ============
  loadList(): void {
    this.loading = true;
    this.svc.list({
      status: this.statusFilter,
      workOrderId: this.workOrderFilter || undefined,
      search: this.searchText,
      showDeleted: this.showDeleted
    }).subscribe({
      next: r => {
        this.loading = false;
        if (r?.success) {
          this.rows = r.data?.items || [];
          this.summary = r.data?.summary || this.summary;
        }
      },
      error: () => { this.loading = false; }
    });
  }

  applyFilters(): void { this.loadList(); }
  clearFilters(): void {
    this.statusFilter = '';
    this.workOrderFilter = null;
    this.searchText = '';
    this.showDeleted = '';
    this.loadList();
  }

  // ============ CREATE / EDIT ============
  openCreate(): void {
    this.modalMode = 'create';
    this.form = this.emptyForm();
    this.svc.nextCode().subscribe(r => {
      if (r?.success && r.data?.code) this.form.emrCode = r.data.code;
    });
    this.formModalInstance?.show();
  }

  openEdit(id: number): void {
    this.svc.detail(id).subscribe(r => {
      if (!r?.success || !r.data) return;
      this.modalMode = 'edit';
      const h = r.data.header;
      this.form = {
        id: h.id,
        emrCode: h.emrCode,
        workOrderId: h.workOrderId,
        reason: h.reason,
        remarks: h.remarks || '',
        items: (r.data.items || []).map(i => ({
          id: i.id,
          itemType: i.itemType,
          rawItemId: i.rawItemId,
          threadId: i.threadId,
          itemName: i.itemName,
          unitOfMeasure: i.unitOfMeasure || '',
          qty: Number(i.qty || 0),
          rate: i.rate,
          remarks: i.remarks || ''
        }))
      };
      this.formModalInstance?.show();
    });
  }

  closeForm(): void { this.formModalInstance?.hide(); }

  addItem(): void {
    this.form.items.push({
      id: 0, itemType: 'RAW',
      rawItemId: null, threadId: null,
      itemName: '', unitOfMeasure: '', qty: 0, rate: null, remarks: ''
    });
  }

  removeItem(i: number): void { this.form.items.splice(i, 1); }

  onItemTypeChange(row: EditableItemRow): void {
    row.rawItemId = null;
    row.threadId  = null;
    row.itemName  = '';
    row.unitOfMeasure = '';
    row.rate = null;
  }

  onRawPick(row: EditableItemRow): void {
    const r = this.rawItems.find(x => x.id === Number(row.rawItemId));
    if (r) { row.itemName = r.name; row.unitOfMeasure = row.unitOfMeasure || ''; }
  }

  onThreadPick(row: EditableItemRow): void {
    const t = this.threads.find(x => x.id === Number(row.threadId));
    if (t) {
      row.itemName = `${t.code} - ${t.name}`;
      row.unitOfMeasure = t.unit || 'MTR';
      if (row.rate == null) row.rate = t.rate;
    }
  }

  save(): void {
    if (!this.form.workOrderId || this.form.workOrderId <= 0) {
      return alert('Work Order is required.');
    }
    if (!this.form.reason) return alert('Reason is required.');
    if (!this.form.items.length) return alert('Add at least one item.');

    const payload: EmrSavePayload = {
      id: this.form.id || undefined,
      emrCode: this.form.emrCode,
      workOrderId: this.form.workOrderId,
      reason: this.form.reason,
      remarks: this.form.remarks,
      items: this.form.items
        .filter(i => i.qty > 0 && (i.rawItemId || i.threadId))
        .map(i => ({
          id: i.id || undefined,
          itemType: i.itemType,
          rawItemId: i.itemType === 'RAW' ? i.rawItemId : null,
          threadId:  i.itemType === 'THREAD' ? i.threadId : null,
          unitOfMeasure: i.unitOfMeasure,
          qty: Number(i.qty),
          rate: i.rate != null ? Number(i.rate) : null,
          remarks: i.remarks
        }))
    };

    if (!payload.items.length) return alert('Each line needs an item and a positive quantity.');

    this.saving = true;
    const obs = this.modalMode === 'create' ? this.svc.create(payload) : this.svc.update(payload);
    obs.subscribe({
      next: r => {
        this.saving = false;
        if (!r?.success) return alert(r?.message || 'Save failed.');
        this.formModalInstance?.hide();
        this.loadList();
      },
      error: e => {
        this.saving = false;
        alert(e?.error?.message || e?.message || 'Save failed.');
      }
    });
  }

  // ============ ACTIONS ============
  submitFor(id: number): void {
    if (!confirm('Submit this request for approval?')) return;
    this.svc.submit(id).subscribe({
      next: r => {
        if (r?.success) { this.loadList(); return; }
        // Server returns a structured shortage list when on-hand stock
        // is insufficient. Format it cleanly so the operator can act.
        const shortages: any[] = (r as any)?.shortages || [];
        if (shortages.length) {
          const lines = shortages.map(s =>
            ` • ${s.itemName}: requested ${s.requested}, on hand ${s.onHand}, short by ${s.shortBy}`).join('\n');
          alert(`${r?.message || 'Insufficient stock'}\n\n${lines}`);
        } else {
          alert(r?.message || 'Submit failed.');
        }
      },
      error: e => alert(e?.error?.message || e?.message || 'Submit failed.')
    });
  }

  approve(id: number): void {
    if (!confirm('Approve and auto-issue materials? This writes Gen_Store OUT entries against the Work Order.')) return;
    this.svc.approve(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Approve failed.');
      this.loadList();
    });
  }

  openReject(id: number): void {
    this.rejectId = id;
    this.rejectReason = '';
    this.rejectModalInstance?.show();
  }
  confirmReject(): void {
    if (!this.rejectId) return;
    if (!this.rejectReason.trim()) return alert('Rejection reason is required.');
    this.svc.reject(this.rejectId, this.rejectReason.trim()).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Reject failed.');
      this.rejectModalInstance?.hide();
      this.loadList();
    });
  }

  remove(id: number): void {
    if (!confirm('Soft-delete this request?')) return;
    this.svc.remove(id).subscribe(r => {
      if (!r?.success) return alert(r?.message || 'Delete failed.');
      this.loadList();
    });
  }

  // ============ DETAIL ============
  openDetail(id: number): void {
    this.svc.detail(id).subscribe(r => {
      if (!r?.success || !r.data) return;
      this.selectedDetailHeader = r.data.header;
      this.selectedDetailItems = r.data.items || [];
      this.detailModalInstance?.show();
    });
  }

  // ============ 360 PROFILE DRAWER ============
  openProfile(id: number): void {
    this.profileLoading = true;
    this.showProfile = true;
    this.profile = null;
    this.svc.profile(id).subscribe({
      next: r => {
        this.profileLoading = false;
        if (r?.success && r.data) this.profile = r.data;
      },
      error: () => { this.profileLoading = false; }
    });
  }

  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
  }

  // ============ EXPORT / PRINT ============
  private filterParams() {
    return {
      status: this.statusFilter,
      workOrderId: this.workOrderFilter || undefined,
      search: this.searchText,
      showDeleted: this.showDeleted
    };
  }

  exportExcel(): void {
    window.open(this.svc.exportExcelUrl(this.filterParams()), '_blank');
  }

  exportPdf(): void {
    window.open(this.svc.exportPdfUrl(this.filterParams()), '_blank');
  }

  printList(): void {
    if (!this.rows.length) return alert('No data');

    const esc = (v: any) => String(v ?? '')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

    const fmt = (d: string | null) => d ? new Date(d).toLocaleDateString() : '';

    const trs = this.rows.map(r => `
      <tr>
        <td>${esc(r.emrCode)}</td>
        <td>${esc(r.workOrderCode)}</td>
        <td>${esc(r.branchName || '-')}</td>
        <td>${esc(fmt(r.requestDate))}</td>
        <td>${esc(this.reasonLabel(r.reason))}</td>
        <td>${esc(r.status)}</td>
        <td>${esc(r.requestedByName || '-')}</td>
        <td>${esc(r.approvedByName || '-')}</td>
      </tr>`).join('');

    const html = `<!doctype html>
      <html>
      <head>
        <meta charset="utf-8">
        <title>Extra Material Requests — Print</title>
        <style>
          @page { size: A4 landscape; margin: 10mm; }
          body { font-family: "Segoe UI", Arial, sans-serif; color: #212529; font-size: 10pt; margin: 0; }
          h2 { text-align: center; margin: 0 0 10px; }
          table { width: 100%; border-collapse: collapse; }
          thead { background: #0d6efd; color: #fff; }
          th, td { border: 1px solid #ccc; padding: 5px 7px; text-align: left; font-size: 9pt; }
          tbody tr:nth-child(even) { background: #f6f8fb; }
          .footer { text-align: right; font-size: 8.5pt; color: #6c757d; margin-top: 10px; }
        </style>
      </head>
      <body>
        <h2>Extra Material Requests &mdash; List</h2>
        <table>
          <thead>
            <tr>
              <th>EMR Code</th><th>Work Order</th><th>Branch</th><th>Date</th>
              <th>Reason</th><th>Status</th><th>Requested By</th><th>Approved By</th>
            </tr>
          </thead>
          <tbody>${trs}</tbody>
        </table>
        <div class="footer">Generated: ${new Date().toLocaleString()} | Rows: ${this.rows.length}</div>
        <script>
          window.addEventListener('load', function () {
            setTimeout(function () { window.print(); }, 100);
          });
        </script>
      </body>
      </html>`;

    const win = window.open('', '_blank');
    if (!win) return alert('Pop-up blocked. Please allow pop-ups for this site.');
    win.document.open();
    win.document.write(html);
    win.document.close();
  }

  // ============ HELPERS ============
  badgeClass(status: string): string {
    switch ((status || '').toUpperCase()) {
      case 'DRAFT':     return 'bg-secondary';
      case 'SUBMITTED': return 'bg-warning text-dark';
      case 'APPROVED':  return 'bg-success';
      case 'REJECTED':  return 'bg-danger';
      default:          return 'bg-light text-dark';
    }
  }

  reasonLabel(value: string): string {
    const m = this.reasons.find(r => r.value === (value as any));
    return m ? m.label : (value || '-');
  }

  totalValue(items: EditableItemRow[] | EmrItemRow[] | undefined): number {
    if (!items) return 0;
    // Manual sum sidesteps TS's union-array inference issue with reduce(),
    // which couldn't decide between EditableItemRow[] and EmrItemRow[].
    let sum = 0;
    for (const i of items as any[]) {
      sum += Number(i.qty || 0) * Number(i.rate || 0);
    }
    return sum;
  }

  private emptyForm() {
    return {
      id: 0, emrCode: '', workOrderId: null as number | null,
      reason: 'WASTE', remarks: '', items: [] as EditableItemRow[]
    };
  }
}
