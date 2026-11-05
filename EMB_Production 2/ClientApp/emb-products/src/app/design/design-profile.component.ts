import { Component, ElementRef, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { DesignService } from '../services/design.service';

declare var bootstrap: any;

interface DesignProfileVm {
  id: number;
  d_Code: string;
  dNo: string;
  collectionName: string;
  productId: number | null;
  productName: string;
  machineId: number | null;
  machineName: string;
  customerId: number | null;
  customerName: string;
  branchId: number | null;
  branchName: string;
  active: boolean;
  componentCount: number;
  components: ComponentVm[];
}

interface ComponentVm {
  id: number;
  code: number | null;
  name: string;
  headToHead: number | null;
  stitch: number | null;
  tilla: number | null;
  seqA: number | null;
  seqB: number | null;
  width: number | null;
  height: number | null;
  colors: number | null;
  machineHead: number | null;
  repeatVal: number | null;
  hSpace: number | null;
  ups: number | null;
  active: boolean;

  // Phase 3 — version meta
  version?: number;
  hasOriginal?: boolean;
}

@Component({
  selector: 'app-design-profile',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './design-profile.component.html',
  styleUrls: ['./design-profile.component.css']
})
export class DesignProfileComponent implements OnInit {

  profile: DesignProfileVm | null = null;
  loading = false;
  designId = 0;

  // form state for add/edit component
  modalMode: 'create' | 'edit' = 'create';
  form: any = this.blankForm();
  saving = false;

  @ViewChild('compModal') compModal!: ElementRef;
  @ViewChild('deleteModal') deleteModal!: ElementRef;

  pendingDeleteId: number | null = null;

  // Phase 2A — files
  files: any[] = [];
  uploadingFile = false;

  // Phase 2B — thread usage
  threadUsage: { totalQtyPerUnit: number; rows: any[] } | null = null;
  loadingUsage = false;

  // Phase 3 — comparison
  compareData: any = null;
  loadingCompare = false;
  @ViewChild('compareModal') compareModal!: ElementRef;

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private designSvc: DesignService
  ) { }

  ngOnInit(): void {
    this.designId = Number(this.route.snapshot.paramMap.get('id'));
    if (!this.designId) {
      this.router.navigate(['/designs']);
      return;
    }
    this.load();
  }

  load(): void {
    this.loading = true;
    this.designSvc.getProfile(this.designId).subscribe({
      next: res => {
        this.loading = false;
        if (res?.success) {
          this.profile = res.data;
          this.loadFiles();
          this.loadThreadUsage();
        } else {
          alert(res?.message || 'Failed to load profile.');
        }
      },
      error: err => {
        this.loading = false;
        console.error('Profile load failed', err);
        alert('Failed to load profile.');
      }
    });
  }

  // -------- Phase 2A: files --------
  loadFiles(): void {
    this.designSvc.listFiles(this.designId).subscribe({
      next: res => { this.files = res?.data ?? []; },
      error: () => { this.files = []; }
    });
  }

  onFileSelected(ev: Event): void {
    const input = ev.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;
    const file = input.files[0];
    this.uploadFile(file);
    input.value = '';
  }

  uploadFile(file: File): void {
    if (this.uploadingFile) return;
    this.uploadingFile = true;
    this.designSvc.uploadFile(this.designId, file).subscribe({
      next: res => {
        this.uploadingFile = false;
        if (res?.success) {
          this.loadFiles();
        } else {
          alert(res?.message || 'Upload failed.');
        }
      },
      error: err => {
        this.uploadingFile = false;
        console.error('Upload failed', err);
        alert('Upload failed.');
      }
    });
  }

  downloadFile(fileId: number): void {
    window.open(this.designSvc.downloadFileUrl(fileId), '_blank');
  }

  deleteFile(fileId: number): void {
    if (!confirm('Delete this file?')) return;
    this.designSvc.deleteFile(fileId).subscribe({
      next: () => this.loadFiles(),
      error: err => { console.error('Delete file failed', err); alert('Delete failed.'); }
    });
  }

  setPrimary(fileId: number): void {
    this.designSvc.setPrimaryFile(fileId).subscribe({
      next: () => this.loadFiles(),
      error: err => console.error('Set primary failed', err)
    });
  }

  // -------- Phase 2B: thread usage --------
  loadThreadUsage(): void {
    this.loadingUsage = true;
    this.designSvc.getThreadUsage(this.designId).subscribe({
      next: res => {
        this.loadingUsage = false;
        this.threadUsage = res?.data ?? null;
      },
      error: err => {
        this.loadingUsage = false;
        console.error('Thread usage failed', err);
      }
    });
  }

  fmtBytes(n: number | null | undefined): string {
    if (!n) return '-';
    if (n < 1024) return n + ' B';
    if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
    return (n / 1024 / 1024).toFixed(2) + ' MB';
  }

  // -------- Phase 3: versioning + comparison --------
  saveAsNewVersion(c: ComponentVm): void {
    if (!confirm(`Freeze V${c.version ?? 1} and start a new version?`)) return;
    this.designSvc.snapshotNewVersion(c.id).subscribe({
      next: res => {
        if (res?.success) { alert(res.message || 'New version saved.'); this.load(); }
        else alert(res?.message || 'Failed.');
      },
      error: err => { console.error(err); alert('Failed.'); }
    });
  }

  captureOriginal(c: ComponentVm): void {
    const msg = c.hasOriginal
      ? 'Replace existing ORIGINAL baseline with current spec?'
      : 'Capture current spec as ORIGINAL baseline?';
    if (!confirm(msg)) return;
    this.designSvc.captureOriginal(c.id).subscribe({
      next: res => {
        if (res?.success) { alert(res.message || 'Captured.'); this.load(); }
        else alert(res?.message || 'Failed.');
      },
      error: err => { console.error(err); alert('Failed.'); }
    });
  }

  openCompare(): void {
    this.loadingCompare = true;
    this.designSvc.compareDesign(this.designId).subscribe({
      next: res => {
        this.loadingCompare = false;
        this.compareData = res?.data ?? null;
        this.show(this.compareModal);
      },
      error: err => {
        this.loadingCompare = false;
        console.error('Compare failed', err);
        alert('Compare failed.');
      }
    });
  }

  blankForm() {
    return {
      id: 0,
      name: '',
      headToHead: 0,
      stitch: 0,
      tilla: 0,
      seqA: 0,
      seqB: 0,
      width: null,
      height: null,
      colors: null,
      machineHead: null,
      repeatVal: null,
      hSpace: null,
      ups: null,
      active: true
    };
  }

  // -------- modals --------
  private show(ref: ElementRef): void {
    new bootstrap.Modal(ref.nativeElement).show();
  }
  private hide(ref: ElementRef): void {
    const inst = bootstrap.Modal.getInstance(ref.nativeElement);
    if (inst) inst.hide();
  }

  openCreate(): void {
    this.modalMode = 'create';
    this.form = this.blankForm();
    this.show(this.compModal);
  }

  openEdit(c: ComponentVm): void {
    this.modalMode = 'edit';
    this.form = {
      id: c.id,
      name: c.name,
      headToHead: c.headToHead ?? 0,
      stitch: c.stitch ?? 0,
      tilla: c.tilla ?? 0,
      seqA: c.seqA ?? 0,
      seqB: c.seqB ?? 0,
      width: c.width,
      height: c.height,
      colors: c.colors,
      machineHead: c.machineHead,
      repeatVal: c.repeatVal,
      hSpace: c.hSpace,
      ups: c.ups,
      active: c.active
    };
    this.show(this.compModal);
  }

  save(): void {
    if (this.saving || !this.profile) return;

    if (!this.form.name?.trim()) {
      alert('Component name is required.');
      return;
    }

    this.saving = true;

    const payload = {
      id: this.form.id || 0,
      name: this.form.name.trim(),
      designId: this.profile.id,
      headToHead: Number(this.form.headToHead) || 0,
      stitch: Number(this.form.stitch) || 0,
      tilla: Number(this.form.tilla) || 0,
      seqA: Number(this.form.seqA) || 0,
      seqB: Number(this.form.seqB) || 0,
      width: this.toNum(this.form.width),
      height: this.toNum(this.form.height),
      colors: this.toNum(this.form.colors),
      machineHead: this.toNum(this.form.machineHead),
      repeat: this.toNum(this.form.repeatVal),
      hSpace: this.toNum(this.form.hSpace),
      ups: this.toNum(this.form.ups),
      branchId: this.profile.branchId || 0,
      active: this.form.active ?? true
    };

    this.designSvc.saveComponent(payload).subscribe({
      next: res => {
        this.saving = false;
        if (res?.success) {
          this.hide(this.compModal);
          this.load();
        } else {
          alert(res?.message || 'Save failed.');
        }
      },
      error: err => {
        this.saving = false;
        console.error('Component save failed', err);
        alert('Save failed. Please try again.');
      }
    });
  }

  askDelete(id: number): void {
    this.pendingDeleteId = id;
    this.show(this.deleteModal);
  }

  confirmDelete(): void {
    if (!this.pendingDeleteId) return;
    this.designSvc.deleteComponent(this.pendingDeleteId).subscribe({
      next: res => {
        this.hide(this.deleteModal);
        this.pendingDeleteId = null;
        this.load();
      },
      error: err => {
        console.error('Delete failed', err);
        alert('Delete failed.');
      }
    });
  }

  private toNum(v: any): number | null {
    if (v === null || v === undefined || v === '') return null;
    const n = Number(v);
    return isNaN(n) ? null : n;
  }
}
