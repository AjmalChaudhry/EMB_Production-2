import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RolesService } from '../services/adm-roles.service';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'app-adm-roles',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  templateUrl: './adm-roles.component.html',
})

export class AdmRolesComponent implements OnInit {
  roles: any[] = [];
  filteredRoles: any[] = [];
  searchTerm = '';
  selectedRole: any = null;
  formMode: 'create' | 'edit' = 'create';
  roleForm!: FormGroup;
  deleteId: number | null = null;
  loading = false;
  modalTitle = '';

  constructor(private fb: FormBuilder, private service: RolesService) { }

  ngOnInit() {
    this.initForm();
    this.loadRoles();
  }

  initForm() {
    this.roleForm = this.fb.group({
      id: [0],
      name: ['', Validators.required],
      authorityLevel: [''],
      workingCriteria: [''],
      active: [true]
    });
  }

  // ======== Load All ========
  loadRoles() {
    this.loading = true;
    this.service.getAll().subscribe({
      next: (res) => {
        if (res.success) {
          this.roles = res.data;
          this.filteredRoles = this.roles;
        }
        this.loading = false;
      },
      error: () => (this.loading = false)
    });
  }

  // ======== Search ========
  onSearch() {
    const s = this.searchTerm.toLowerCase();
    this.filteredRoles = this.roles.filter((r) =>
      r.name.toLowerCase().includes(s)
    );
  }

  // ======== Create Modal ========
  openCreateModal() {
    this.formMode = 'create';
    this.modalTitle = 'Add New Role';
    this.roleForm.reset({ active: true });
  }

  // ======== Edit Modal ========
  openEditModal(role: any) {
    this.formMode = 'edit';
    this.modalTitle = 'Edit Role';
    this.roleForm.patchValue(role);
  }

  // ======== Detail Modal ========
  openDetailModal(roleId: number) {
    this.selectedRole = null;
    this.service.getDetail(roleId).subscribe({
      next: (res) => {
        if (res.success) this.selectedRole = res.data;
      }
    });
  }

  // ======== Save (Create or Update) ========
  saveRole() {
    if (this.roleForm.invalid) return;
    const formData = this.roleForm.value;

    if (this.formMode === 'create') {
      this.service.create(formData).subscribe({
        next: (res) => {
          if (res.success) {
            this.loadRoles();
            alert('Role created successfully');
          } else alert(res.message);
        }
      });
    } else {
      this.service.update(formData.id, formData).subscribe({
        next: (res) => {
          if (res.success) {
            this.loadRoles();
            alert('Role updated successfully');
          } else alert(res.message);
        }
      });
    }
  }

  // ======== Delete ========
  confirmDelete(id: number) {
    this.deleteId = id;
  }

  deleteRole() {
    if (!this.deleteId) return;
    this.service.delete(this.deleteId).subscribe({
      next: (res) => {
        if (res.success) {
          this.loadRoles();
          alert('Role deleted successfully');
        } else alert(res.message);
        this.deleteId = null;
      }
    });
  }
}
