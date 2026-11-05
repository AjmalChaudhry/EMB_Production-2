import { Component, OnInit, ViewChild, ElementRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormsModule,
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators
} from '@angular/forms';
import { HttpClientModule } from '@angular/common/http';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { UsersService } from '../services/adm-user.service';
import { ApiDataService, ExportColumn } from '../shared/services/api-data.service';
import { UserRowViewModel, UserDetailViewModel } from '../shared/models/user.dto';
import { normalizeArray } from 'src/app/shared/utils/array-utils';
import { ApiResponseData } from '../shared/models/api-response';

declare var bootstrap: any;

@Component({
  selector: 'app-adm-users',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, HttpClientModule],
  templateUrl: './adm-user.component.html',
  styleUrls: ['./adm-user.component.css']
})
export class AdmUsersComponent implements OnInit {
  @ViewChild('userDetailModal', { static: true }) userDetailModal!: ElementRef;

  // ========= DATA =========
  users: UserRowViewModel[] = [];
  branches: any[] = [];
  roles: any[] = [];
  companies: any[] = [];
  designations: Array<{ Id: number; Name: string; Code?: string }> = [];

  selectedUser: UserDetailViewModel | null = null;
  editingUser: UserRowViewModel | null = null;
  deleteTarget: UserRowViewModel | null = null;

  // ========= FORM =========
  userForm!: FormGroup;
  selectedFile: File | null = null;
  loading = false;

  // ========= PAGINATION =========
  searchText = '';
  page = 1;
  pageSize = 10;
  totalItems = 0;
  totalPages = 1;
  paginationArray: number[] = [];

  // ========= PHASE R - FILTERS =========
  filterRoleId: number | null = null;
  filterBranchId: number | null = null;
  filterCompanyId: number | null = null;
  filterDesignationId: number | null = null;
  filterActive = '';      // 'active' | 'inactive' | ''
  filterDashboard = '';   // 'yes' | 'no' | ''

  // ========= PHASE R - SUMMARY =========
  summary = {
    count: 0,
    activeCount: 0,
    inactiveCount: 0,
    dashboardCount: 0,
    loggedInCount: 0,
    neverLoggedInCount: 0,
    distinctRoles: 0,
    distinctBranches: 0,
    byRole: [] as Array<{ roleId: number; roleName: string; count: number }>,
    byBranch: [] as Array<{ branchId: number; branchName: string; count: number }>
  };

  // ========= PHASE R - 360 PROFILE DRAWER =========
  profile: any = null;
  profileLoading = false;
  showProfile = false;

  // ========= SERVICES =========
  private fb = inject(FormBuilder);
  private userSvc = inject(UsersService);
  private apiData = inject(ApiDataService);

  // ==============================================================  
  // INIT  
  // ==============================================================  
  ngOnInit(): void {
    this.initForm();
    this.loadDropdownsAndUsers();
  }

  private initForm(): void {
    this.userForm = this.fb.group({
      employeeId: [null],
      userName: ['', Validators.required],
      firstName: [''],
      lastName: [''],
      emailAddress: ['', [Validators.email]],
      mobilePrimary: [''],
      landline: [''],
      password: [''],
      branchId: [null, Validators.required],
      roleId: [null, Validators.required],
      companyId: [null, Validators.required],
      designationId: [null],
      active: [true],
      dashboardAccess: [false],
      approvel: ['false'],
      facialRecognition: [null]
    });
  }

  private normalizeDropdown(res: any): any[] {
    if (!res) return [];
    if (Array.isArray(res)) return res;
    if (res.data && Array.isArray(res.data)) return res.data;
    return [];
  }

  // ==============================================================  
  // LOAD DROPDOWNS + USERS (FIXED LIKE BRAND)  
  // ==============================================================  
  private loadDropdownsAndUsers(): void {
    this.loading = true;

    forkJoin({
      branches: this.userSvc.getBranches().pipe(catchError(() => of([]))),
      roles: this.userSvc.getRoles().pipe(catchError(() => of([]))),
      companies: this.userSvc.getCompanies().pipe(catchError(() => of([]))),
      designations: this.userSvc.getDesignations().pipe(catchError(() => of([]))),
      users: this.userSvc.getAll({
        search: this.searchText,
        page: this.page,
        pageSize: this.pageSize,
        roleId: this.filterRoleId,
        branchId: this.filterBranchId,
        companyId: this.filterCompanyId,
        designationId: this.filterDesignationId,
        activeFilter: this.filterActive,
        dashboardFilter: this.filterDashboard
      }).pipe(
        catchError(() => of({ data: { items: [], totalItems: 0, summary: null } }))
      )
    }).subscribe({
      next: ({ branches, roles, companies, designations, users }) => {
        // Dropdowns
        this.branches = Array.isArray(branches) ? branches : [];
        this.roles = Array.isArray(roles) ? roles : [];
        this.companies = Array.isArray(companies) ? companies : [];
        this.designations = Array.isArray(designations) ? designations : [];

        // Users table
        const paged = users?.data;
        this.users = paged?.items ?? [];
        this.totalItems = paged?.totalItems ?? this.users.length;

        // Summary chips (Phase R)
        const s = paged?.summary;
        if (s) {
          this.summary = {
            count: Number(s.count ?? 0),
            activeCount: Number(s.activeCount ?? 0),
            inactiveCount: Number(s.inactiveCount ?? 0),
            dashboardCount: Number(s.dashboardCount ?? 0),
            loggedInCount: Number(s.loggedInCount ?? 0),
            neverLoggedInCount: Number(s.neverLoggedInCount ?? 0),
            distinctRoles: Number(s.distinctRoles ?? 0),
            distinctBranches: Number(s.distinctBranches ?? 0),
            byRole: Array.isArray(s.byRole) ? s.byRole : [],
            byBranch: Array.isArray(s.byBranch) ? s.byBranch : []
          };
        }

        this.setupPagination();
      },

      error: (err) => {
        console.error('Dropdown or user load failed:', err);
        this.branches = [];
        this.roles = [];
        this.companies = [];
        this.users = [];
        this.totalItems = 0;
      },

      complete: () => (this.loading = false)
    });
  }



  // ==============================================================  
  // PAGINATION  
  // ==============================================================  
  private setupPagination(): void {
    this.totalPages = Math.max(1, Math.ceil(this.totalItems / this.pageSize));
    this.paginationArray = Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  onPageChange(pg: number): void {
    if (pg >= 1 && pg <= this.totalPages) {
      this.page = pg;
      this.loadDropdownsAndUsers();
    }
  }

  // ==============================================================  
  // SEARCH  
  // ==============================================================  
  onSearch(): void {
    this.page = 1;
    this.loadDropdownsAndUsers();
  }

  clearSearch(): void {
    this.searchText = '';
    this.page = 1;
    this.loadDropdownsAndUsers();
  }

  // ==============================================================
  // PHASE R - FILTERS
  // ==============================================================
  onFiltersChange(): void {
    this.page = 1;
    this.loadDropdownsAndUsers();
  }

  clearFilters(): void {
    this.searchText = '';
    this.filterRoleId = null;
    this.filterBranchId = null;
    this.filterCompanyId = null;
    this.filterDesignationId = null;
    this.filterActive = '';
    this.filterDashboard = '';
    this.page = 1;
    this.loadDropdownsAndUsers();
  }

  // ==============================================================
  // PHASE R - 360 PROFILE DRAWER
  // ==============================================================
  openProfile(id: number): void {
    if (!id || id <= 0) {
      alert('User id missing on this row.');
      return;
    }
    this.profileLoading = true;
    this.profile = null;
    this.showProfile = true;

    this.userSvc.getProfile(id).subscribe({
      next: (res: any) => {
        this.profileLoading = false;
        const ok = res?.success ?? res?.Success ?? false;
        if (!ok) {
          alert(res?.message || 'Failed to load profile.');
          this.showProfile = false;
          return;
        }
        this.profile = res?.data ?? res?.Data ?? null;
      },
      error: err => {
        this.profileLoading = false;
        console.error('User profile error:', err);
        alert(err?.error?.message || 'Failed to load profile.');
        this.showProfile = false;
      }
    });
  }

  closeProfile(): void {
    this.showProfile = false;
    this.profile = null;
  }

  // ==============================================================  
  // MODAL HELPERS  
  // ==============================================================  
  private showModal(id: string): void {
    const el = document.getElementById(id);
    if (el) new bootstrap.Modal(el).show();
  }

  private hideModal(id: string): void {
    const el = document.getElementById(id);
    if (el) bootstrap.Modal.getInstance(el)?.hide();
  }

  closeFormModal(): void {
    this.hideModal('userFormModal');
  }

  // ==============================================================  
  // CREATE / EDIT  
  // ==============================================================  
  openCreate(): void {
    this.editingUser = null;
    this.selectedFile = null;

    this.userForm.reset({
      employeeId: null,
      userName: '',
      firstName: '',
      lastName: '',
      emailAddress: '',
      mobilePrimary: '',
      landline: '',
      password: '',
      branchId: null,
      roleId: null,
      companyId: null,
      active: true,
      dashboardAccess: false,
      approvel: 'false',
      facialRecognition: null
    });

    this.showModal('userFormModal');
  }

  openEdit(user: UserRowViewModel): void {
    this.editingUser = user;
    this.selectedFile = null;

    this.userForm.patchValue({
      employeeId: user.EmployeeId,
      userName: user.UserName,
      firstName: user.FirstName,
      lastName: user.LastName,
      emailAddress: user.EmailAddress,
      mobilePrimary: user.MobilePrimary,
      landline: user.Landline,
      branchId: user.BranchId,
      roleId: user.RoleId,
      companyId: user.CompanyId,
      designationId: user.DesignationId ?? null,
      active: !!user.Active,
      dashboardAccess: !!user.DashboardAccess,
      approvel: user.Approvel ?? 'false'
    });

    this.showModal('userFormModal');
  }

  onFileChange(e: Event): void {
    const input = e.target as HTMLInputElement;
    if (input.files?.length) this.selectedFile = input.files[0];
  }

  saveUser(): void {
    if (this.userForm.invalid || this.loading) return;

    this.loading = true;
    const form = this.userForm.value;
    const formData = new FormData();

    Object.keys(form).forEach(k => formData.append(k, form[k] ?? ''));
    if (this.selectedFile)
      formData.append('file', this.selectedFile, this.selectedFile.name);

    const obs$ = this.editingUser
      ? this.userSvc.update(this.editingUser.Id, formData)
      : this.userSvc.create(formData);

    obs$.subscribe({
      next: res => {
        this.loading = false;
        if (res.success) {
          alert(res.message || 'Saved.');
          this.closeFormModal();
          this.loadDropdownsAndUsers();
        } else alert(res.message || 'Failed.');
      },
      error: err => {
        this.loading = false;
        console.error(err);
        alert('❌ Error saving user.');
      }
    });
  }

  // ==============================================================  
  // DELETE  
  // ==============================================================  
  confirmDelete(u: UserRowViewModel): void {
    this.deleteTarget = u;
    this.showModal('deleteUserModal');
  }

  deleteUser(): void {
    if (!this.deleteTarget) return;
    this.userSvc.delete(this.deleteTarget.Id).subscribe({
      next: res => {
        alert(res.message ?? 'Deleted.');
        this.hideModal('deleteUserModal');
        this.loadDropdownsAndUsers();
      },
      error: err => alert('Error deleting user.')
    });
  }

  // ==============================================================  
  // DETAIL  
  // ==============================================================  
  openDetail(id: number): void {
    this.userSvc.getDetail(id).subscribe({
      next: (res: ApiResponseData<UserDetailViewModel>) => {
        if (!res.success || !res.data) {
          alert('User not found.');
          return;
        }

        const u = res.data as any;

        this.selectedUser = {
          ...u,
          ApprovelLabel: u.Approvel === 'true' ? 'Approved' : 'Pending'
        };

        new bootstrap.Modal(this.userDetailModal.nativeElement).show();
      },
      error: err => {
        console.error(err);
        alert('Failed to load user details.');
      }
    });
  }

  closeDetail(): void {
    bootstrap.Modal.getInstance(this.userDetailModal.nativeElement)?.hide();
    this.selectedUser = null;
  }

  // ==============================================================  
  // IMAGE HELPERS  
  // ==============================================================  
  getPhotoUrl(imagePath?: string | null): string {
    if (!imagePath) return 'assets/no-image.png';
    const t = imagePath.trim();
    if (t.startsWith('http') || t.startsWith('/Uploads/FacialRecognition/'))
      return t;
    return '/Uploads/FacialRecognition/' + t.replace(/^\/+/, '');
  }

  onImageError(event: Event): void {
    const img = event.target as HTMLImageElement | null;
    if (!img) return;

    const anyImg = img as any;
    if (!anyImg._errorHandled) {
      anyImg._errorHandled = true;
      img.src = 'assets/no-image.png';
    }
  }

  // ==============================================================  
  // EXPORTS  
  // ==============================================================  
  exportUsersToExcel(): void {
    const cols: ExportColumn[] = [
      { header: 'Name (Employee ID)', field: 'FullName' },
      { header: 'Contact', field: 'MobilePrimary' },
      { header: 'Email', field: 'EmailAddress' },
      { header: 'Role', field: 'RoleName' },
      { header: 'Branch', field: 'BranchName' },
      { header: 'Company', field: 'CompanyName' },
      { header: 'Status', field: 'Active' }
    ];
    this.apiData.exportToExcel(this.users, 'UsersList', cols);
  }

  exportUsersToPDF(): void {
    const cols: ExportColumn[] = [
      { header: 'Name (Employee ID)', field: 'FullName' },
      { header: 'Contact', field: 'MobilePrimary' },
      { header: 'Email', field: 'EmailAddress' },
      { header: 'Role', field: 'RoleName' },
      { header: 'Branch', field: 'BranchName' },
      { header: 'Company', field: 'CompanyName' },
      { header: 'Status', field: 'Active' }
    ];
    this.apiData.exportToPDF(this.users, 'UsersList', cols);
  }

  printUsers(): void {
    const cols: ExportColumn[] = [
      { header: 'Name (Employee ID)', field: 'FullName' },
      { header: 'Contact', field: 'MobilePrimary' },
      { header: 'Email', field: 'EmailAddress' },
      { header: 'Role', field: 'RoleName' },
      { header: 'Branch', field: 'BranchName' },
      { header: 'Company', field: 'CompanyName' },
      { header: 'Status', field: 'Active' }
    ];
    this.apiData.printTable(this.users, cols, 'Users List');
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-3  BULK SELECTION + actions
  // ──────────────────────────────────────────────────────────────────
  selectedIds = new Set<number>();
  bulkBusy = false;
  isSelected(id: number): boolean { return this.selectedIds.has(id); }
  toggleSelect(id: number): void {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }
  allVisibleSelected(): boolean {
    return this.users.length > 0 && this.users.every((r: any) => this.selectedIds.has(r.Id));
  }
  toggleSelectAll(): void {
    if (this.allVisibleSelected()) this.users.forEach((r: any) => this.selectedIds.delete(r.Id));
    else this.users.forEach((r: any) => this.selectedIds.add(r.Id));
  }
  clearSelection(): void { this.selectedIds.clear(); }

  private afterBulk(res: any, verb: string): void {
    const d = res?.data ?? {};
    const ok = Number(d.successCount || 0);
    const failed = Number(d.failureCount || 0);
    let msg = `${ok} ${verb}, ${failed} failed.`;
    if (failed > 0 && Array.isArray(d.failures)) {
      msg += '\n\nFailures:\n' + d.failures.slice(0, 10)
        .map((f: any) => `  • #${f.id} ${f.code}: ${f.reason}`).join('\n');
      if (d.failures.length > 10) msg += `\n  …and ${d.failures.length - 10} more.`;
    }
    alert(msg);
    this.clearSelection();
    this.loadDropdownsAndUsers();
  }
  bulkActivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Activate ${ids.length} user(s)?`)) return;
    this.bulkBusy = true;
    this.userSvc.bulkActivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'activated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk activate failed.'); }
    });
  }
  bulkDeactivate(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Deactivate ${ids.length} user(s)?`)) return;
    this.bulkBusy = true;
    this.userSvc.bulkDeactivate(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deactivated'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk deactivate failed.'); }
    });
  }
  bulkDeleteRows(): void {
    const ids = Array.from(this.selectedIds);
    if (!ids.length) return;
    if (!confirm(`Delete ${ids.length} user(s)? (soft-delete)`)) return;
    this.bulkBusy = true;
    this.userSvc.bulkDelete(ids).subscribe({
      next: (r: any) => { this.bulkBusy = false; this.afterBulk(r, 'deleted'); },
      error: (e: any) => { this.bulkBusy = false; alert(e?.error?.message || 'Bulk delete failed.'); }
    });
  }

  // ──────────────────────────────────────────────────────────────────
  //  Sprint-2  Audit log loader
  // ──────────────────────────────────────────────────────────────────
  auditLog: any[] = [];
  auditLogLoading = false;
  loadAuditLog(id: number): void {
    if (!id) { this.auditLog = []; return; }
    this.auditLogLoading = true;
    this.userSvc.getAuditLog(id).subscribe({
      next: (r: any) => {
        this.auditLogLoading = false;
        this.auditLog = Array.isArray(r?.data) ? r.data : [];
      },
      error: () => { this.auditLogLoading = false; this.auditLog = []; }
    });
  }
  auditPill(action: string): string {
    const a = (action || '').toUpperCase();
    if (a === 'CREATED')       return 'bg-primary';
    if (a === 'EDITED')        return 'bg-warning text-dark';
    if (a === 'ACTIVATED')     return 'bg-success';
    if (a === 'DEACTIVATED')   return 'bg-secondary';
    if (a === 'DELETED')       return 'bg-danger';
    return 'bg-light text-dark';
  }
}
